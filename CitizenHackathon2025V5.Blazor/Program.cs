using Blazored.Toast;
using CitizenHackathon2025V5.Blazor.Client;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;

// -----------------------------
// Polly Policies
// -----------------------------

// Retry exponentiel
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
        );

// Timeout (ex: 10 secondes)
static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy() =>
    Policy.TimeoutAsync<HttpResponseMessage>(10);

// Circuit breaker
static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30)
        );

// -----------------------------
// Host Builder
// -----------------------------
var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Root components
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// -----------------------------
// HttpClient avec Polly
// -----------------------------
builder.Services.AddHttpClient("CitizenHackathonAPI", client =>
{
    client.BaseAddress = new Uri("https://localhost:7254/api/");
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy());

// Default HttpClient Service
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// -----------------------------
// Services Scoped
// -----------------------------
builder.Services.AddScoped<CitizenHackathon2025V5.Blazor.Client.Services.AuthService>();
builder.Services.AddScoped<CrowdInfoService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<GptInteractionService>();
builder.Services.AddScoped<PlaceService>();
builder.Services.AddScoped<SuggestionService>();
builder.Services.AddScoped<TrafficConditionService>();
//builder.Services.AddSingleton<UserService>();
builder.Services.AddScoped<CitizenHackathon2025V5.Blazor.Client.Services.UserService>();
builder.Services.AddScoped<TrafficStateService>();
builder.Services.AddScoped<WeatherForecastService>();

// -----------------------------
// SignalR & OutZen
// -----------------------------
// Factory to dynamically create OutZenSignalRService
builder.Services.AddScoped<IOutZenSignalRFactory, OutZenSignalRFactory>();

// You can add a general SignalR service if needed
// builder.Services.AddScoped<ISignalRService, SignalRService>();

// -----------------------------
// Services Singletons
// -----------------------------
builder.Services.AddSingleton<TrafficServiceBlazor>();
builder.Services.AddSingleton<TrafficSignalRService>();

// Multi-hubs SignalR Client (NOUVEAU)
builder.Services.AddScoped(sp =>
{
    var baseHubUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7254";
    var auth = sp.GetRequiredService<CitizenHackathon2025V5.Blazor.Client.Services.AuthService>();

    Func<Task<string?>> tokenProvider = async () =>
        await auth.GetAccessTokenOrNullAsync();

    return new MultiHubSignalRClient(baseHubUrl, tokenProvider);
});

// -----------------------------
// Blazored Toast
// -----------------------------
builder.Services.AddBlazoredToast();

// -----------------------------
// Build et Run
// -----------------------------
await builder.Build().RunAsync();











































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/