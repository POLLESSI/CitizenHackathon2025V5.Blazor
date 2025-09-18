using Blazored.Toast;
using CitizenHackathon2025V5.Blazor.Client;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;

// -----------------------------
// Polly Policies
// -----------------------------
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions.HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy() =>
    Policy.TimeoutAsync<HttpResponseMessage>(10);

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions.HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

// -----------------------------
// Host Builder
// -----------------------------
var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Config
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7254";
var apiRestBase = builder.Configuration["Api:RestBase"] ?? $"{apiBaseUrl.TrimEnd('/')}/api/";
var hubBaseUrl = builder.Configuration["SignalR:HubBase"] ?? apiBaseUrl.TrimEnd('/');

// =============================
// Auth / DI de base
// =============================
builder.Services.AddOptions();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, SimpleAuthStateProvider>();

// Services applicatifs
builder.Services.AddHttpClient("ApiWithAuth", c =>
{
    c.BaseAddress = new Uri("https://localhost:7254/api/"); // note it /api/
})
.AddHttpMessageHandler<JwtAttachHandler>();
builder.Services.AddScoped<CrowdInfoService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<GptInteractionService>();
builder.Services.AddScoped<PlaceService>();
builder.Services.AddScoped<SuggestionService>();
builder.Services.AddScoped<TrafficConditionService>();
builder.Services.AddScoped<CitizenHackathon2025V5.Blazor.Client.Services.UserService>();
builder.Services.AddScoped<TrafficStateService>();
builder.Services.AddScoped<WeatherForecastService>();
builder.Services.AddScoped<IHubTokenService, HubTokenService>();

// Toasts
builder.Services.AddBlazoredToast();

// =============================
// HTTP CLIENTS
// =============================

// Handler that adds Authorization: Bearer ...
builder.Services.AddTransient<JwtAttachHandler>();

builder.Services.AddHttpClient("ApiWithAuth", c =>
{
    c.BaseAddress = new Uri(apiRestBase); // ✅ /api/
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddHttpMessageHandler<JwtAttachHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// "ApiRootAuth" Client (BASE = /) for /auth/hub-token (protected) ✅
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

// "Auth" Client (NO JWT handler -> avoids loop when AuthService is constructed)
builder.Services.AddHttpClient("ApiRootAuth", c =>
{
    c.BaseAddress = new Uri(apiBaseUrl); // ✅ pas de /api/
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddHttpMessageHandler<JwtAttachHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// Provides HttpClient by default to services that inject "HttpClient"
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Default"));

// Register IAuthService using the "Auth" client
builder.Services.AddScoped<IAuthService>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Auth");
    var js = sp.GetRequiredService<IJSRuntime>();
    var provider = sp.GetRequiredService<AuthenticationStateProvider>();
    return new AuthService(http, js, provider);
});

// ⚠️ DO NOT register the concrete type AuthService:
// builder.Services.AddScoped<AuthService>(); // <-- deleted

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

await builder.Build().RunAsync();










































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




