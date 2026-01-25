using System.Net.Http;
using Blazored.Toast;
using CitizenHackathon2025V5.Blazor.Client;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Polly;
using Polly.Extensions.Http;

// -----------------------------
// Polly Policies
// -----------------------------
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy() =>
    Policy.TimeoutAsync<HttpResponseMessage>(10);

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

// -----------------------------
// Host Builder
// -----------------------------
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// =============================
// Configuration (API + SignalR)
// =============================
var configuration = builder.Configuration;

// Base API (ex: https://localhost:7254)
var apiBaseUrl = (configuration["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');

// Base REST (ex: https://localhost:7254/api/)
var apiRestBase = configuration["Api:RestBase"];
if (string.IsNullOrWhiteSpace(apiRestBase))
{
    apiRestBase = $"{apiBaseUrl}/api";
}
apiRestBase = apiRestBase.TrimEnd('/') + "/";

// SignalR base for the multi-hub client
// ⚠️ Here, HubBase is considered to be the root (host) expected by MultiHubSignalRClient.
// The /hubs/* logic is handled on the MultiHub + HubPaths.* side (do not force /hubs here to avoid breaking it).
var hubBaseUrl = (configuration["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

// =============================
// Auth / DI de base
// =============================
builder.Services.AddOptions();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, SimpleAuthStateProvider>();

// Handler JWT (injected only on the HttpClients that need it)
builder.Services.AddTransient<JwtAttachHandler>();

// =============================
// HttpClients
// =============================

// 1) Client applicatif /api/* avec JWT
builder.Services.AddHttpClient("ApiWithAuth", c =>
{
    c.BaseAddress = new Uri(apiRestBase); // ex: https://localhost:7254/api/
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddHttpMessageHandler<JwtAttachHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// 2) Client "Default" (also /api/*) with JWT
builder.Services.AddHttpClient("Default", c =>
{
    c.BaseAddress = new Uri(apiRestBase);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddHttpMessageHandler<JwtAttachHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// 3) Root client for login/hub-token/refresh WITHOUT JWT handler
builder.Services.AddHttpClient("ApiRootAuth", c =>
{
    var root = apiBaseUrl.EndsWith("/") ? apiBaseUrl : apiBaseUrl + "/";
    c.BaseAddress = new Uri(root); // ex: https://localhost:7254/
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
// .AddHttpMessageHandler<JwtAttachHandler>() // especially not here
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// HttpClient is injected by default when you simply request "HttpClient".
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Default"));

// =============================
// Authentication Services
// =============================
builder.Services.AddScoped<IAuthService>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiRootAuth");
    var js = sp.GetRequiredService<IJSRuntime>();
    var provider = sp.GetRequiredService<AuthenticationStateProvider>();
    return new AuthService(http, js, provider);
});

builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("ApiWithAuth");
});
// =============================
// Application services
// =============================
builder.Services.AddScoped<AntennaCrowdService>();
builder.Services.AddScoped<CitizenHackathon2025V5.Blazor.Client.Services.CrowdInfoCalendarService>();
builder.Services.AddScoped<CrowdInfoService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<GptInteractionService>();
builder.Services.AddScoped<IMultiHubSignalRClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var auth = sp.GetRequiredService<IAuthService>();

    var apiBaseUrl = (config["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');
    var hubBaseUrl = (config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

    return new MultiHubSignalRClient(
        baseUrl: hubBaseUrl,
        tokenProvider: () => auth.GetAccessTokenAsync()
    );
});
builder.Services.AddScoped<IHubUrlBuilder, HubUrlBuilder>();
builder.Services.AddScoped<IOutZenSignalRFactory, OutZenSignalRFactory>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<PlaceService>();
builder.Services.AddScoped<SuggestionService>();
builder.Services.AddScoped<SuggestionMapService>();
builder.Services.AddScoped<TrafficConditionService>();
builder.Services.AddScoped<CitizenHackathon2025V5.Blazor.Client.Services.UserService>();
builder.Services.AddScoped<TrafficStateService>();
builder.Services.AddScoped<WeatherForecastService>();
builder.Services.AddScoped<WeatherHubClient>();
builder.Services.AddScoped<WeatherForecastHubClient>();
builder.Services.AddScoped<IHubTokenService, HubTokenService>();
builder.Services.AddScoped<CrowdCalendarHubClient>();

// Toasts
builder.Services.AddBlazoredToast();

// =============================
// SignalR multi-hubs
// =============================
builder.Services.AddSingleton<TrafficServiceBlazor>();
builder.Services.AddSingleton<TrafficSignalRService>();
builder.Services.AddScoped<IOutZenSignalRFactory, OutZenSignalRFactory>();

builder.Services.AddScoped(sp =>
{
    var auth = sp.GetRequiredService<IAuthService>();
    Func<Task<string?>> tokenProvider = async () => await auth.GetAccessTokenAsync();
    return new MultiHubSignalRClient(hubBaseUrl, tokenProvider);
});

// =============================
// Run
// =============================
await builder.Build().RunAsync();























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




