using Blazored.Toast;
using CitizenHackathon2025V5.Blazor.Client;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Services.Interop;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy() =>
    Policy.TimeoutAsync<HttpResponseMessage>(300);

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(300));

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuration = builder.Configuration;

var apiBaseUrl = (configuration["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');

var apiRestBase = configuration["Api:RestBase"];
if (string.IsNullOrWhiteSpace(apiRestBase))
{
    apiRestBase = $"{apiBaseUrl}/api";
}
apiRestBase = apiRestBase.TrimEnd('/') + "/";

var hubBaseUrl = (configuration["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

builder.Services.AddOptions();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, SimpleAuthStateProvider>();
builder.Services.AddTransient<JwtAttachHandler>();

builder.Services.AddHttpClient("ApiWithAuth", client =>
{
    client.BaseAddress = new Uri(apiRestBase);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddHttpMessageHandler<JwtAttachHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services.AddHttpClient("Default", client =>
{
    client.BaseAddress = new Uri(apiRestBase);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddHttpMessageHandler<JwtAttachHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services.AddHttpClient("ApiRootAuth", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.EndsWith("/") ? apiBaseUrl : apiBaseUrl + "/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services.AddHttpClient("OllamaClient", client =>
{
    client.BaseAddress = new Uri(apiRestBase);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (CitizenHackathon2025V5.Blazor)");
})
.AddHttpMessageHandler<JwtAttachHandler>()
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiWithAuth"));

builder.Services.AddScoped<IAuthService>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiRootAuth");
    var js = sp.GetRequiredService<IJSRuntime>();
    var authStateProvider = sp.GetRequiredService<AuthenticationStateProvider>();
    return new AuthService(http, js, authStateProvider);
});

builder.Services.AddScoped<IHubTokenService, HubTokenService>();

builder.Services.AddScoped<IHubUrlBuilder, HubUrlBuilder>();
builder.Services.AddScoped<IOutZenSignalRFactory, OutZenSignalRFactory>();

builder.Services.AddScoped<MultiHubSignalRClient>(sp =>
{
    var auth = sp.GetRequiredService<IAuthService>();

    return new MultiHubSignalRClient(
        baseUrl: hubBaseUrl,
        tokenProvider: () => auth.GetAccessTokenAsync()
    );
});

builder.Services.AddScoped<IMultiHubSignalRClient>(sp =>
    sp.GetRequiredService<MultiHubSignalRClient>());

builder.Services.AddScoped<OutZenMapInterop>();

builder.Services.AddBlazoredToast();

builder.Services.AddScoped<AntennaService>();
builder.Services.AddScoped<AntennaCrowdService>();
builder.Services.AddScoped<ICrowdInfoAntennaService, CrowdInfoAntennaService>();

builder.Services.AddScoped<CitizenHackathon2025V5.Blazor.Client.Services.CrowdInfoCalendarService>();
builder.Services.AddScoped<CrowdInfoService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<GptInteractionService>();
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
builder.Services.AddScoped<CrowdCalendarHubClient>();

builder.Services.AddSingleton<TrafficServiceBlazor>();
builder.Services.AddSingleton<TrafficSignalRService>();

Console.WriteLine("✅ PROGRAM CLIENT V5 - Blazor DI cleaned and loaded");

await builder.Build().RunAsync();























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




