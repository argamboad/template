using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Core.Abstractions;
using Template.Core.Repositories;
using Template.Infrastructure.Billing;
using Template.Infrastructure.Email;
using Template.Infrastructure.Inbox;
using Template.Infrastructure.Outbox;
using Template.Infrastructure.Scheduling;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Repositories;

namespace Template.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Dedicated cookie scheme that serves only as the temporary carrier for the
    /// external OAuth principal. The provider handlers sign into this scheme; the
    /// callback reads the external principal from it, then issues the real session
    /// (JWT + refresh cookie) and signs the carrier out.
    /// </summary>
    public const string ExternalScheme = "External";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Persistence
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // Data Protection — keys stored in DB so the OAuth correlation/nonce cookies
        // survive server restarts/redeploys.
        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>()
            .SetApplicationName("template");

        // Email — dev: points to Mailpit via appsettings.Development.json
        services.Configure<SmtpSettings>(configuration.GetSection("Email:Smtp"));
        // The real SMTP sender, registered KEYED so the outbox handler can resolve it without
        // getting the outbox decorator (the default IEmailSender) back.
        services.AddKeyedTransient<IEmailSender, SmtpEmailSender>("smtp");
        // App-facing IEmailSender enqueues onto the outbox instead of sending inline (ADR-007);
        // the dispatcher performs the real send out-of-band, with retry. Existing call sites
        // (passwordless, invitations) are unchanged — they still depend on IEmailSender.
        services.AddScoped<IEmailSender, OutboxEmailSender>();

        // Transactional outbox + background dispatcher (ADR-007). OutboxMessage is staged in the
        // same transaction as the business change; the dispatcher drains it via typed handlers.
        services.AddSingleton(new OutboxOptions());
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<IOutboxHandler>(sp =>
            new EmailOutboxHandler(sp.GetRequiredKeyedService<IEmailSender>("smtp")));
        services.AddHostedService<OutboxDispatcher>();

        // Inbox dedup gate — idempotent inbound (webhook) deliveries (ADR-007). Used inline by the
        // receiving endpoint inside its unit of work; no background service.
        services.AddScoped<IInbox, EfInbox>();

        // Scheduled/recurring jobs (ADR-007). The host ticks and runs each IScheduledJob on its own
        // interval; add a job by registering IScheduledJob (no host edits). Jobs are scoped so they
        // get a fresh DbContext per run.
        services.AddSingleton(new ScheduledJobsOptions());
        services.AddScoped<IScheduledJob, ExpiredTokenCleanupJob>();
        services.AddHostedService<ScheduledJobsHost>();

        // Billing provider (ADR-006). Stripe when a secret key is configured; otherwise the in-memory
        // fake, so the app boots and dev/E2E run with zero Stripe setup and zero real charges.
        services.Configure<StripeSettings>(configuration.GetSection("Billing:Stripe"));
        if (!string.IsNullOrEmpty(configuration["Billing:Stripe:SecretKey"]))
            services.AddScoped<IBillingProvider, StripeBillingProvider>();
        else
            services.AddScoped<IBillingProvider, FakeBillingProvider>();

        // Clock — repositories/services depend on TimeProvider for testable time. The host
        // (API) also registers it; TryAdd keeps Infrastructure self-contained without conflict.
        services.TryAddSingleton(TimeProvider.System);

        // Repositories + unit of work
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserLoginRepository, UserLoginRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ILoginTokenRepository, LoginTokenRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantInvitationRepository, TenantInvitationRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Generic repository for feature/domain entities (vertical slices). Platform/auth
        // entities use their dedicated repositories above.
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        // External OAuth — to add a new provider, append .AddXxx(...) below.
        // Credentials come from config; set them in .env for dev (never commit secrets).
        var auth = services.AddAuthentication();

        // Temporary carrier cookie for the external principal during the OAuth round-trip.
        auth.AddCookie(ExternalScheme, o =>
        {
            o.Cookie.Name = ".app.external";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        });

        // When the provider redirect fails (user denies consent, state mismatch, etc.)
        // send them back to the client error page instead of throwing an unhandled 500.
        var appBase = configuration["Auth:AppBaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var remoteFailureUrl = $"{appBase}/auth-error";
        Task OnRemoteFailure(RemoteFailureContext ctx)
        {
            ctx.Response.Redirect(remoteFailureUrl);
            ctx.HandleResponse();
            return Task.CompletedTask;
        }

        // Only register an OAuth provider when credentials are present.
        // Configure via .env in dev; environment variables in production.
        if (!string.IsNullOrEmpty(configuration["Authentication:Google:ClientId"]))
            auth.AddGoogle(google =>
            {
                google.ClientId = configuration["Authentication:Google:ClientId"]!;
                google.ClientSecret = configuration["Authentication:Google:ClientSecret"]!;
                google.SignInScheme = ExternalScheme;
                google.Events.OnRemoteFailure = OnRemoteFailure;
            });

        if (!string.IsNullOrEmpty(configuration["Authentication:Microsoft:ClientId"]))
            auth.AddMicrosoftAccount(ms =>
            {
                ms.ClientId = configuration["Authentication:Microsoft:ClientId"]!;
                ms.ClientSecret = configuration["Authentication:Microsoft:ClientSecret"]!;
                ms.SignInScheme = ExternalScheme;
                // Pin the tenant authority. "consumers" = personal Microsoft accounts;
                // the bare default routes to the legacy login.live.com endpoint, which
                // rejects the registered redirect URI. Override to "common",
                // "organizations", or a tenant GUID as needed.
                var msTenant = configuration["Authentication:Microsoft:Tenant"] ?? "consumers";
                ms.AuthorizationEndpoint = $"https://login.microsoftonline.com/{msTenant}/oauth2/v2.0/authorize";
                ms.TokenEndpoint = $"https://login.microsoftonline.com/{msTenant}/oauth2/v2.0/token";
                ms.Events.OnRemoteFailure = OnRemoteFailure;
            });

        return services;
    }
}
