using Microsoft.AspNetCore.HttpOverrides;

namespace Template.Api.Configuration;

/// <summary>
/// DEPLOY-1 (ADR-017): reverse-proxy correctness, config-gated. When the app runs behind a
/// TLS-terminating proxy (Render, nginx, …) it must honor the proxy's <c>X-Forwarded-For</c> /
/// <c>X-Forwarded-Proto</c> so the real client IP drives the per-IP passwordless rate limiter and the
/// <c>https</c> scheme drives OAuth redirect-URI generation. It is <b>off by default</b>: trusting these
/// headers when there is <i>no</i> proxy in front lets any client spoof its source IP (defeating the
/// rate limiter), so the gate must be turned on explicitly (<c>Proxy:Enabled</c>) only where a proxy
/// actually sets them.
/// </summary>
public static class ProxyForwardingExtensions
{
    private const string EnabledKey = "Proxy:Enabled";

    /// <summary>Configures forwarded-headers processing when <c>Proxy:Enabled</c> is true; otherwise a no-op.</summary>
    public static IServiceCollection AddProxyForwarding(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue(EnabledKey, false))
            return services;

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // A managed proxy (e.g. Render) fronts the app on an unknown, changing IP, so the default
            // loopback-only allowlist would ignore its headers. The proxy is the sole ingress, so trust it.
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });
        return services;
    }

    /// <summary>Applies <c>UseForwardedHeaders</c> when enabled — must run first, before anything reads the IP/scheme.</summary>
    public static IApplicationBuilder UseProxyForwarding(this IApplicationBuilder app, IConfiguration configuration)
    {
        if (configuration.GetValue(EnabledKey, false))
            app.UseForwardedHeaders();
        return app;
    }
}
