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

    /// <summary>Requests allowed per IP per <see cref="Window"/> before the limiter returns 429.</summary>
    public const int PermitLimit = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddPasswordlessRateLimiter(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
        });
}
