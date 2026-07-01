using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Template.Api.Authentication;
using Template.Api.Configuration;
using Template.Api.Features;
using Template.Api.Features.Notes;
using Template.Api.Observability;
using Template.Api.Services;
using Template.Core.Abstractions;
using Template.Infrastructure;
using Template.Infrastructure.Persistence;

// Local dev: load secrets/config from the repo-root .env (the single local source of truth —
// see docs/DECISIONS.md). TraversePath walks up to find it regardless of the working dir; the
// try/catch makes it a no-op when there's no .env (e.g. production, which uses real env vars).
try { DotNetEnv.Env.TraversePath().Load(); } catch { /* no .env present */ }

var builder = WebApplication.CreateBuilder(args);

// Structured logging with per-request scopes (OBS-1, ADR-008): readable single-line console in dev,
// JSON in prod so a log aggregator can index the tenant_id/user_id scope. Swap in Serilog/OTel-logs
// later without touching call sites.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
    builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.SingleLine = true; });
else
    builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.UseUtcTimestamp = true; });

builder.Services.AddControllers();

// OpenAPI / Swagger UI. The "Authorize" button takes a JWT access token (get one
// from POST /api/auth/refresh after signing in) so protected endpoints are testable.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Template API", Version = "v1" });
    // A curated "public" document with ONLY the /api/public routes (PUBAPI-2) — the customer-facing
    // contract, served leak-free at /api/public/openapi.json when PUBAPI is enabled (see below).
    o.SwaggerDoc("public", new OpenApiInfo
    {
        Title = "Public API",
        Version = "v1",
        Description = "Programmatic API authenticated with a tenant API key sent in the X-Api-Key header.",
    });
    o.DocInclusionPredicate((docName, api) =>
    {
        var isPublic = api.RelativePath?.StartsWith("api/public", StringComparison.OrdinalIgnoreCase) == true;
        return docName == "public" ? isPublic : true; // "public" = only /api/public; "v1" = everything
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste a JWT access token (without the 'Bearer ' prefix)."
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Infrastructure: DbContext, Data Protection, email, repositories, and the
// External cookie + OAuth provider schemes.
builder.Services.AddInfrastructure(builder.Configuration);

// OpenTelemetry traces + metrics (OBS-2). Exporter is config-gated (OTLP when configured); see
// TelemetryExtensions. Spans are tagged with tenant_id/user_id.
builder.Services.AddAppTelemetry(builder.Configuration);

// Health checks (OBS-3). /health = liveness (process up); /health/ready = readiness (DB reachable).
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// Typed settings (read configuration once at startup).
builder.Services.AddSingleton<IJwtSettings>(new JwtSettings(builder.Configuration));
builder.Services.AddSingleton<IRefreshTokenSettings>(new RefreshTokenSettings(builder.Configuration));
builder.Services.AddSingleton<IApplicationSettings>(new ApplicationSettings(builder.Configuration));
builder.Services.AddSingleton<IPasswordlessSettings>(new PasswordlessSettings(builder.Configuration));
builder.Services.AddSingleton<IInvitationSettings>(new InvitationSettings(builder.Configuration));

// Clock — injected so services are testable.
builder.Services.AddSingleton(TimeProvider.System);

// Auth services.
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IClaimsExtractor, ClaimsExtractor>();
// Per-provider email-trust policy (tenant-gated for Microsoft) layered on the fail-closed claim check.
builder.Services.AddSingleton<IProviderEmailTrust, ProviderEmailTrust>();
builder.Services.AddScoped<IPasswordlessService, PasswordlessService>();
builder.Services.AddScoped<ICookieService, CookieService>();
builder.Services.AddScoped<IErrorResponseFactory, ErrorResponseFactory>();
builder.Services.AddScoped<ITokenGenerator, TokenGenerator>();
builder.Services.AddScoped<ITokenHasher, TokenHasher>();
builder.Services.AddSingleton<ILinkTokenService, LinkTokenService>();
builder.Services.AddSingleton<INativeAuthCodeService, NativeAuthCodeService>();

// Tenant ("household") management services.
// Current-tenant accessor — reads the JWT tenant_id claim; drives the global tenant
// query filter in AppDbContext and is the slice-facing tenancy entry point.
builder.Services.AddHttpContextAccessor();
// One scoped HttpCurrentTenant backs both interfaces, so entering a tenant via ITenantContext is seen
// by ICurrentTenant (and thus the AppDbContext filter/stamping) within the same scope.
builder.Services.AddScoped<HttpCurrentTenant>();
builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<HttpCurrentTenant>());
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpCurrentTenant>());
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<ITenantInvitationService, TenantInvitationService>();

// Tenant data export (GDPR-1, ADR-011). Assembles core + each contributor's section into a JSON
// bundle stored via IFileStorage, returned as a signed URL. Owner-gated at the endpoint.
builder.Services.AddScoped<ITenantExportService, TenantExportService>();
// Account erasure (GDPR-2, ADR-011). "Delete my account" — wipes identity/PII in one audited
// transaction, honoring the single-owner invariant (transfer-or-dissolve first).
builder.Services.AddScoped<IAccountErasureService, AccountErasureService>();
// MFA — authenticator-app TOTP (MFA-1, ADR-012). Secret encrypted at rest; hashed recovery codes.
builder.Services.AddScoped<IMfaService, MfaService>();
// MFA login step-up (MFA-2). Signed short-lived challenge + verify → completes the session.
builder.Services.AddSingleton<IMfaChallengeService, MfaChallengeService>();
builder.Services.AddScoped<IMfaLoginService, MfaLoginService>();
// Per-user in-app notifications (NOTIFY-1, ADR-013). NotifyAsync stages an in-app row on the caller's
// unit of work; the center API reads/marks the caller's own notifications.
builder.Services.AddScoped<INotificationService, NotificationService>();

// Platform-staff admin surface (ADMIN, ADR-014). Staff is an out-of-band config allowlist; the admin
// endpoints gate on it per-request. Cross-tenant reads use EnterTenant / non-scoped tables — the global
// filter is never loosened.
builder.Services.Configure<PlatformAdminSettings>(builder.Configuration.GetSection("Admin"));
builder.Services.AddScoped<IPlatformStaffService, PlatformStaffService>();

// RBAC permission seam (ADR-009). Server-side role→permission check behind .RequirePermission(...);
// resolves the caller's membership and consults the RolePermissions matrix; fails closed.
builder.Services.AddScoped<IPermissionService, PermissionService>();

// Billing entitlements (ADR-006). Server-side plan gate behind .RequireEntitlement(...); reads the
// tenant's Subscription projection and fails closed to Free.
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
// Billing quotas (BILLING-5): seat + metered-usage limits from the plan; used by the invite flow.
builder.Services.AddScoped<IQuotaService, QuotaService>();
// Billing dunning (BILLING-6): notify the tenant owner on failed-payment/cancel transitions, and a
// scheduled sweep that nudges once when a paid period lapses without a webhook.
builder.Services.AddScoped<IBillingNotifier, BillingNotifier>();
builder.Services.AddScoped<IScheduledJob, SubscriptionLapseSweepJob>();
// Billing checkout orchestration (BILLING-2), behind the platform BillingController. The
// IBillingProvider (Stripe or fake) is registered in AddInfrastructure.
builder.Services.AddScoped<IBillingService, BillingService>();
// Billing webhook handler (BILLING-3): verify → inbox-dedup → EnterTenant → upsert Subscription.
builder.Services.AddScoped<BillingWebhookHandler>();

// 🗑️ DELETE-ME: sample feature slice (Features/Notes) — the reference for how a vertical
// slice wires up: a handler + a tenant-data contributor, with endpoints mapped below.
builder.Services.AddScoped<NotesHandler>();
builder.Services.AddScoped<ITenantDataContributor, NotesDataContributor>();

// Billing participates in tenant dissolve (BILLING-7): wipe the Subscription projection + cancel the
// provider subscription (via the outbox) so a dissolved tenant stops being billed.
builder.Services.AddScoped<ITenantDataContributor, BillingDataContributor>();

// Caches + session (LinkTokenService uses IMemoryCache; session backed by distributed cache).
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// JWT Bearer — authenticates /api/* endpoints with the app-issued access token.
// Validation mirrors JwtTokenService (issuer = audience = Jwt:Issuer).
var jwtSettings = new JwtSettings(builder.Configuration);
var authBuilder = builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        // Single validation definition shared with JwtTokenService — see JwtValidation.
        options.TokenValidationParameters = jwtSettings.CreateParameters();
    });

// PUBAPI (ADR-015): the public API + API keys. Default OFF — a deployment opts in via PublicApi:Enabled.
// Strong gating: the API-key scheme is only added, and the routes only mapped (below), when enabled.
var publicApiSettings = new PublicApiSettings();
builder.Configuration.GetSection(PublicApiSettings.SectionName).Bind(publicApiSettings);
builder.Services.AddSingleton(publicApiSettings);
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
if (publicApiSettings.Enabled)
    authBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

// HOOKS (ADR-016): outbound webhooks. Default OFF — a deployment opts in via Webhooks:Enabled. The
// delivery machinery is always registered (dormant); only the management routes (below) are gated.
var webhooksSettings = new WebhooksSettings();
builder.Configuration.GetSection(WebhooksSettings.SectionName).Bind(webhooksSettings);
builder.Services.AddSingleton(webhooksSettings);
builder.Services.AddScoped<IWebhookSubscriptionService, WebhookSubscriptionService>();
builder.Services.AddScoped<IWebhookPublisher, WebhookPublisher>();

// Single tenant-API authorization policy, shared by the platform controllers and feature groups.
builder.Services.AddTenantApiAuthorization();

// Throttle the unauthenticated passwordless endpoints (email-bomb / brute-force surface) — CONF-5.
builder.Services.AddApiRateLimiters();

// CORS — allow the Blazor WASM client to send credentialed requests (cookies).
var allowedOrigins = builder.Configuration
    .GetSection("Auth:AllowedOrigins")
    .Get<string[]>() ?? [];

if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddPolicy("BlazorClient", p => p
        .WithOrigins(allowedOrigins)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials()));
}

var app = builder.Build();

// Apply EF migrations on startup so a freshly-created database (e.g. after a
// `docker compose down -v && up`) gets its schema with no manual `dotnet ef database update`.
// QA_TEST_PLAN documents "re-apply migrations by starting the API" — this is what does it.
// Migrate() is idempotent (a no-op when already current); the relational guard keeps
// non-relational test providers unaffected. AppDbContext needs ICurrentTenant, which resolves
// to a null tenant outside a request — fine, migrating doesn't use the tenant filter.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Template API v1");
        if (publicApiSettings.Enabled)
            c.SwaggerEndpoint("/swagger/public/swagger.json", "Public API"); // PUBAPI-2
        c.RoutePrefix = string.Empty; // serve the UI at the API root (/)
    });
}

// HTTPS redirect is a production concern. In Development we deliberately skip it so the
// Android emulator can talk cleartext HTTP to the host (http://10.0.2.2:5238) without the
// request being 307'd to a port/cert it can't reach. Native auth uses body tokens (no
// cookies), so none of the web client's HTTPS/SameSite requirements apply to that leg.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

if (allowedOrigins.Length > 0)
    app.UseCors("BlazorClient");

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
// Enrich every log line with tenant_id/user_id once the principal is resolved (OBS-1).
app.UseRequestLoggingScope();
app.UseRateLimiter();
app.MapControllers();

// Health/readiness (OBS-3). Unauthenticated, status-only (the default writer emits just the status,
// so no internals leak). Liveness runs no checks; readiness runs the "ready"-tagged DB check.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// 🗑️ DELETE-ME: sample feature slice endpoints (remove with Features/Notes).
app.MapNotes();
// Billing is a platform controller (BillingController) — auto-mapped by MapControllers above.

// PUBAPI (ADR-015): map key management + the public routes only when enabled — off ⇒ they don't exist.
if (publicApiSettings.Enabled)
{
    app.MapApiKeyManagement();
    app.MapPublicApi();

    // The customer-facing OpenAPI contract (PUBAPI-2): emit ONLY the curated "public" document, so the
    // internal "v1" surface is never exposed in production. Anonymous (a published contract), any env.
    app.MapGet("/api/public/openapi.json", (Swashbuckle.AspNetCore.Swagger.ISwaggerProvider swagger) =>
    {
        var document = swagger.GetSwagger("public");
        using var writer = new StringWriter();
        document.SerializeAsV3(new Microsoft.OpenApi.Writers.OpenApiJsonWriter(writer));
        return Results.Text(writer.ToString(), "application/json");
    }).AllowAnonymous().WithTags("Public API");
}

// HOOKS (ADR-016): map webhook management only when enabled — off ⇒ the routes don't exist.
if (webhooksSettings.Enabled)
    app.MapWebhookManagement();

app.Run();
