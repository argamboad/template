using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Perezosoft.Api.Configuration;

namespace Perezosoft.Api.Tests.Hosting;

/// <summary>
/// DEPLOY-1 (ADR-017): the reverse-proxy correctness gate. Exercises <see cref="ProxyForwardingExtensions"/>
/// through a minimal TestServer with a trivial echo endpoint — asserting X-Forwarded-For/-Proto are honored
/// when <c>Proxy:Enabled</c> is on and IGNORED when off (the anti-spoofing default). No DB/Program needed;
/// this is a focused middleware test of the exact configuration the deployed app uses.
/// </summary>
public class ProxyForwardingTests
{
    private static async Task<HttpClient> BuildEchoServerAsync(bool proxyEnabled)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Proxy:Enabled"] = proxyEnabled ? "true" : "false",
                }));
                web.ConfigureServices((ctx, services) => services.AddProxyForwarding(ctx.Configuration));
                web.Configure((ctx, app) =>
                {
                    app.UseProxyForwarding(ctx.Configuration);
                    app.Run(async http =>
                    {
                        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
                        await http.Response.WriteAsync($"{ip}|{http.Request.Scheme}");
                    });
                });
            })
            .StartAsync();

        return host.GetTestClient();
    }

    private static HttpRequestMessage ForwardedRequest()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add("X-Forwarded-For", "203.0.113.7");
        req.Headers.Add("X-Forwarded-Proto", "https");
        return req;
    }

    [Fact]
    public async Task WhenEnabled_HonorsForwardedForAndProto()
    {
        var client = await BuildEchoServerAsync(proxyEnabled: true);

        var body = await (await client.SendAsync(ForwardedRequest())).Content.ReadAsStringAsync();

        Assert.Equal("203.0.113.7|https", body); // real client IP + proxied scheme
    }

    [Fact]
    public async Task WhenDisabled_IgnoresForwardedHeaders()
    {
        var client = await BuildEchoServerAsync(proxyEnabled: false);

        var body = await (await client.SendAsync(ForwardedRequest())).Content.ReadAsStringAsync();

        Assert.DoesNotContain("203.0.113.7", body); // spoofable IP not trusted
        Assert.EndsWith("|http", body);             // scheme unchanged
    }
}
