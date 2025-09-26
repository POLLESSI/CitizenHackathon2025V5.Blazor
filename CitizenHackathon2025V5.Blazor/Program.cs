using Blazored.Toast;
using CitizenHackathon2025V5.Blazor.Client;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;

// -----------------------------
// Polly Policies
// -----------------------------
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions.HandleTransientHttpError()
        .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)));

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

// === Handler JWT (only once) ===
builder.Services.AddTransient<JwtAttachHandler>();

// === HttpClients (ONLY ONE REGISTRATION PER NAME) ===

// 1) Application client to /api/* with JWT
builder.Services.AddHttpClient("ApiWithAuth", c =>
{
    var baseUrl = apiRestBase.EndsWith("/") ? apiRestBase : apiRestBase + "/";
    c.BaseAddress = new Uri(baseUrl); // ex: https://localhost:7254/api/
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddHttpMessageHandler<JwtAttachHandler>()  
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// 2) Default client (also /api/*) with JWT
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

// 3) Root client for auth WITHOUT handler (login, hub-token, refresh)
builder.Services.AddHttpClient("ApiRootAuth", c =>
{
    c.BaseAddress = new Uri(apiBaseUrl); 
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
// .AddHttpMessageHandler<JwtAttachHandler>() // DO NOT add
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// Default HttpClient injected (when requesting "HttpClient")
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Default"));

// IAuthService uses the client without a handler
builder.Services.AddScoped<IAuthService>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiRootAuth");
    var js = sp.GetRequiredService<IJSRuntime>();
    var provider = sp.GetRequiredService<AuthenticationStateProvider>();
    return new AuthService(http, js, provider);
});

// Application services
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




