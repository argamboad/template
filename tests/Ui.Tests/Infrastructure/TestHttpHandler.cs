using System.Net;
using System.Text;

namespace Perezosoft.Ui.Tests.Infrastructure;

/// <summary>
/// A controllable <see cref="HttpMessageHandler"/> for the client's <see cref="HttpClient"/>: route
/// "METHOD /path" to a canned response, and record every request for assertions. Unmatched requests 404
/// (a component that calls an unstubbed endpoint fails loudly rather than hanging).
/// </summary>
public sealed class TestHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every request the components made, in order — assert against these.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Stub "METHOD /path" (path only, query ignored) to return <paramref name="json"/> with <paramref name="status"/>.</summary>
    public TestHttpHandler On(HttpMethod method, string path, string json = "{}", HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[Key(method, path)] = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var key = Key(request.Method, request.RequestUri?.AbsolutePath ?? "/");
        var response = _routes.TryGetValue(key, out var factory)
            ? factory(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"{{\"error\":\"no stub for {key}\"}}", Encoding.UTF8, "application/json"),
            };
        return Task.FromResult(response);
    }

    private static string Key(HttpMethod method, string path) => $"{method} {path}";
}
