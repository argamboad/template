using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Template.Shared.Ui;
using Template.Shared.Ui.Auth;
using Template.Web.Http;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("ApiBaseUrl not configured in wwwroot/appsettings.json.");

// Delegating handlers:
//  - CookieHandler includes browser credentials (the HttpOnly refresh cookie).
//  - AuthHeaderHandler attaches the in-memory JWT as a Bearer header.
// Use IHttpClientFactory so the framework supplies the browser HTTP handler as the
// primary handler (you cannot `new HttpClientHandler()` in Blazor WASM).
builder.Services.AddScoped<CookieHandler>();
builder.Services.AddScoped<AuthHeaderHandler>();

// "Api" — the general-purpose client used by components: credentials + Bearer token.
builder.Services.AddHttpClient("Api", client => client.BaseAddress = new Uri(apiBase))
    .AddHttpMessageHandler<CookieHandler>()
    .AddHttpMessageHandler<AuthHeaderHandler>();

// "ApiAuth" — used only by AuthService for refresh/logout. Credentials only (no
// Bearer handler) to avoid a DI cycle (AuthHeaderHandler depends on AuthService).
builder.Services.AddHttpClient("ApiAuth", client => client.BaseAddress = new Uri(apiBase))
    .AddHttpMessageHandler<CookieHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));

// Localization — IStringLocalizer<AppStrings> resolves the RCL's .resx resources.
builder.Services.AddLocalization();

// Web session store: the browser owns the HttpOnly refresh cookie, so this is a no-op.
builder.Services.AddSingleton<ISessionStore, CookieSessionStore>();

// Auth. AuthService is a SINGLETON: IHttpClientFactory resolves message handlers
// (AuthHeaderHandler) in a separate DI scope, so a scoped AuthService would give the
// handler a different instance with no token — and the Bearer header would never be
// attached. Singleton guarantees the app and the handler share one token store.
builder.Services.AddSingleton(sp => new AuthService(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiAuth"),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuthService>>(),
    sp.GetRequiredService<ISessionStore>()));

var host = builder.Build();

// Apply the user's saved UI culture (device-local for now; per-user server preference is
// layered on later) before the app renders, falling back to English.
var js = host.Services.GetRequiredService<IJSRuntime>();
var stored = await js.InvokeAsync<string?>("localStorage.getItem", "app_culture");
var culture = string.IsNullOrWhiteSpace(stored) ? "en" : stored;
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(culture);

await host.RunAsync();
