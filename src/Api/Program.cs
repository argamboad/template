using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Template.Api.Configuration;
using Template.Api.Features.Notes;
using Template.Api.Services;
using Template.Core.Abstractions;
using Template.Infrastructure;
using Template.Infrastructure.Persistence;

// Local dev: load secrets/config from the repo-root .env (the single local source of truth —
// see docs/DECISIONS.md). TraversePath walks up to find it regardless of the working dir; the
// try/catch makes it a no-op when there's no .env (e.g. production, which uses real env vars).
try { DotNetEnv.Env.TraversePath().Load(); } catch { /* no .env present */ }

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// OpenAPI / Swagger UI. The "Authorize" button takes a JWT access token (get one
// from POST /api/auth/refresh after signing in) so protected endpoints are testable.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Template API", Version = "v1" });
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

// Billing entitlements (ADR-006). Server-side plan gate behind .RequireEntitlement(...); reads the
// tenant's Subscription projection and fails closed to Free.
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
// Billing checkout orchestration (BILLING-2), behind the platform BillingController. The
// IBillingProvider (Stripe or fake) is registered in AddInfrastructure.
builder.Services.AddScoped<IBillingService, BillingService>();
// Billing webhook handler (BILLING-3): verify → inbox-dedup → EnterTenant → upsert Subscription.
builder.Services.AddScoped<BillingWebhookHandler>();

// 🗑️ DELETE-ME: sample feature slice (Features/Notes) — the reference for how a vertical
// slice wires up: a handler + a tenant-data contributor, with endpoints mapped below.
builder.Services.AddScoped<NotesHandler>();
builder.Services.AddScoped<ITenantDataContributor, NotesDataContributor>();

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
builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        // Single validation definition shared with JwtTokenService — see JwtValidation.
        options.TokenValidationParameters = jwtSettings.CreateParameters();
    });

// Single tenant-API authorization policy, shared by the platform controllers and feature groups.
builder.Services.AddTenantApiAuthorization();

// Throttle the unauthenticated passwordless endpoints (email-bomb / brute-force surface) — CONF-5.
builder.Services.AddPasswordlessRateLimiter();

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
app.UseRateLimiter();
app.MapControllers();

// 🗑️ DELETE-ME: sample feature slice endpoints (remove with Features/Notes).
app.MapNotes();
// Billing is a platform controller (BillingController) — auto-mapped by MapControllers above.

app.Run();
