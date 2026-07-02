using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests.Integration;

/// <summary>
/// DEPLOY-1 (ADR-017): proves single-origin hosting against the REAL app routing. When enabled, the API
/// serves the Blazor WASM client + framework assets and falls back to the SPA shell for client-side
/// routes — but an unmatched <c>/api/*</c> must stay an API-shaped 404, never the shell (the sharp edge).
/// Serving is config-gated OFF by default, so the template's existing behavior is unchanged.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SingleOriginHostingTests(IntegrationTestFactory factory)
{
    private readonly IntegrationTestFactory _factory = factory;

    /// <summary>A client on a host that serves the web client from <paramref name="webRoot"/>.</summary>
    private HttpClient CreateServingClient(TempWebRoot webRoot) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Hosting:ServeWebClient", "true");
            b.UseWebRoot(webRoot.Path);
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Default_DoesNotServeTheWebClient()
    {
        // Off by default (additive): a client-side route has no server endpoint ⇒ 404, not an SPA shell.
        var res = await _factory.CreateClient().GetAsync("/some/client/route");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task WhenEnabled_UnknownNonApiRoute_ServesTheSpaShell()
    {
        using var web = TempWebRoot.Create();
        var res = await CreateServingClient(web).GetAsync("/settings");

        res.EnsureSuccessStatusCode();
        Assert.Contains(TempWebRoot.Sentinel, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WhenEnabled_FrameworkAssets_AreServed()
    {
        using var web = TempWebRoot.Create();
        var res = await CreateServingClient(web).GetAsync("/_framework/dotnet.js");

        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task WhenEnabled_UnknownApiRoute_Is404_NotTheSpaShell()
    {
        using var web = TempWebRoot.Create();
        var res = await CreateServingClient(web).GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain(TempWebRoot.Sentinel, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WhenEnabled_KnownApiRoute_StillAuthenticates_NotShadowed()
    {
        using var web = TempWebRoot.Create();
        // A protected API route without a token must still 401 from the API — not be swallowed by the shell.
        var res = await CreateServingClient(web).GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
