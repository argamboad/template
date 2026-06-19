using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Template.Api.Configuration;
using Template.Api.Services;
using Template.Infrastructure;
using Template.Infrastructure.Persistence;

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
builder.Services.AddScoped<IClaimsExtractor, ClaimsExtractor>();
builder.Services.AddScoped<IPasswordlessService, PasswordlessService>();
builder.Services.AddScoped<ICookieService, CookieService>();
builder.Services.AddScoped<IErrorResponseFactory, ErrorResponseFactory>();
builder.Services.AddScoped<ITokenGenerator, TokenGenerator>();
builder.Services.AddScoped<ITokenHasher, TokenHasher>();
builder.Services.AddSingleton<ILinkTokenService, LinkTokenService>();
builder.Services.AddSingleton<INativeAuthCodeService, NativeAuthCodeService>();

// Tenant ("household") management services.
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<ITenantInvitationService, TenantInvitationService>();

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
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Issuer,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

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
app.MapControllers();

app.Run();
