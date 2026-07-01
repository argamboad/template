using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Template.Api.Configuration;

/// <summary>
/// Rate-limiting policies for abuse-prone endpoints. The passwordless send/verify endpoints are an
/// unauthenticated email-bomb / outbound-cost amplifier and a brute-force surface, so they're
/// throttled per client IP (CONF-5). The per-email dimension — making resends unable to reset a
/// brute-force budget across IPs — is enforced independently and IP-agnostically by the cumulative
/// OTP lockout in <c>PasswordlessService.RedeemOtpAsync</c>.
/// </summary>
public static class RateLimiting
{
    /// <summary>Named policy applied to <c>/otp/send</c>, <c>/magic-link/send</c>, and <c>/otp/verify</c>.</summary>
    public const string PasswordlessPolicy = "passwordless";

    /// <summary>Per-API-key throttle for the public API (PUBAPI-2) — partitions by the key id.</summary>
    public const string PublicApiPolicy = "public-api";

    /// <summary>Requests allowed per IP per <see cref="Window"/> before the limiter returns 429.</summary>
    public const int PermitLimit = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Requests allowed per API key per <see cref="PublicApiWindow"/> before 429.</summary>
    public const int PublicApiPermitLimit = 60;
    public static readonly TimeSpan PublicApiWindow = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddApiRateLimiters(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Unauthenticated passwordless endpoints: per-IP (email-bomb / brute-force — CONF-5).
            options.AddPolicy(PasswordlessPolicy, httpContext =>
            {
                var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    QueueLimit = 0,
                });
            });

            // Public API: per-key (PUBAPI-2). The endpoint is API-key-authenticated, so by the time the
            // limiter runs the principal carries the key id (NameIdentifier) — partition on it so one
            // tenant's key can't exhaust another's budget. Falls back to IP if somehow unauthenticated.
            options.AddPolicy(PublicApiPolicy, httpContext =>
            {
                var keyId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(keyId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PublicApiPermitLimit,
                    Window = PublicApiWindow,
                    QueueLimit = 0,
                });
            });
        });
}
