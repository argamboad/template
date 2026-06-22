using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Template.Api.Configuration;

namespace Template.Api.Tests;

/// <summary>
/// Exercises the real <see cref="RateLimiting.PasswordlessPolicy"/> through actual rate-limiter
/// middleware in a minimal host (no DB / SMTP needed): the passwordless send/verify endpoints must
/// start returning 429 once a client exceeds the per-window budget (CONF-5 / B2-1). Hosting the
/// shared <c>AddPasswordlessRateLimiter</c> extension proves the production policy, not a copy.
/// </summary>
public class RateLimitingTests
{
    private static async Task<TestServer> StartHostAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer()
               .ConfigureServices(services =>
               {
                   services.AddRouting();
                   services.AddPasswordlessRateLimiter();
               })
               .Configure(app =>
               {
                   // UseRateLimiter must run AFTER routing so it can read the endpoint's
                   // RequireRateLimiting metadata (same order as the real Program.cs pipeline).
                   app.UseRouting();
                   app.UseRateLimiter();
                   app.UseEndpoints(endpoints =>
                       endpoints.MapPost("/otp/send", () => Results.Ok())
                                .RequireRateLimiting(RateLimiting.PasswordlessPolicy));
               });
        });
        var host = await builder.StartAsync();
        return host.GetTestServer();
    }

    [Fact]
    public async Task PasswordlessEndpoint_FloodedFromOneClient_Returns429AfterTheLimit()
    {
        using var server = await StartHostAsync();
        var client = server.CreateClient();

        // The first PermitLimit requests pass...
        for (var i = 0; i < RateLimiting.PermitLimit; i++)
        {
            var ok = await client.PostAsync("/otp/send", content: null);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // ...the next one trips the limiter.
        var tripped = await client.PostAsync("/otp/send", content: null);
        Assert.Equal(HttpStatusCode.TooManyRequests, tripped.StatusCode);
    }
}
