using Blazored.Toast.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client
{
    public partial class MainLayout : LayoutComponentBase, IAsyncDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private IToastService ToastService { get; set; } = default!;
        [Inject] public IOutZenSignalRFactory SignalRFactory { get; set; } = default!;
        [Inject] public MultiHubSignalRClient Hubs { get; set; } = default!;
        [Inject] NavigationManager Nav { get; set; } = default!;

        private OutZenSignalRService? SignalRService;
        private IJSObjectReference? _layoutModule;
        private bool _disposed;

        private bool _wired;

        // --- Background Image Logic ---
        private string GetBackgroundImage()
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 8 => "/images/dawn.jpg",
                < 17 => "/images/day.jpg",
                < 20 => "/images/sunset.jpg",
                _ => "/images/night.jpg"
            };
        }

        private string GetTimeClass()
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 8 => "dawn",
                < 17 => "day",
                < 20 => "sunset",
                _ => "night"
            };
        }

        // --- Lifecycle Methods ---
        protected override async Task OnInitializedAsync()
        {
            try
            {
                try
                {
                    var token = await SignalRFactory.GetAccessTokenAsync();
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        // 1) OutZen
                        SignalRService = await SignalRFactory.CreateAsync();
                        await SignalRService.InitializeOutZenAsync();
                    }
                    else
                    {
                        Console.WriteLine("Skipping OutZen hub: no access token.");
                    }

                    // 2) Other hubs
                    await Hubs.ConnectAsync(new[]
                    {
                        HubName.Crowd,
                        HubName.Suggestions,
                        HubName.Weather
                    });

                    // 3) Handlers (once only)
                    if (!_wired)
                    {
                        _wired = true;

                        Hubs.RegisterHandler<string>(HubName.Crowd, "notifynewCrowd", msg =>
                            Console.WriteLine($"[Crowd] {msg}"));

                        Hubs.RegisterHandler(HubName.Suggestions, "NewSuggestion", () =>
                            Console.WriteLine("[Suggestion] NewSuggestion"));

                        Hubs.RegisterHandler<string>(HubName.Weather, "NewWeatherForecast", forecast =>
                            Console.WriteLine($"[WF] {forecast}"));
                    }
                }
                catch (Exception ex)
                {

                    Console.WriteLine($"OutZen init skipped: {ex.Message}");
                }

                // 4) Subscriptions on OutZen (if present)
                if (SignalRService is not null)
                {
                    SignalRService.OnCrowdInfoUpdated += _ => Console.WriteLine("?? CrowdInfo (OutZen)");
                    SignalRService.OnSuggestionsUpdated += _ => Console.WriteLine("?? Suggestions (OutZen)");
                    SignalRService.OnWeatherUpdated += _ => Console.WriteLine("?? Weather (OutZen)");
                    SignalRService.OnTrafficUpdated += _ => Console.WriteLine("?? Traffic (OutZen)");
                }

                // 4) OutZen service side subscriptions — ?? no return Task
                //SignalRService.OnCrowdInfoUpdated += dto =>
                //{
                //    Console.WriteLine("?? CrowdInfo received (OutZen)");
                //};
                //SignalRService.OnSuggestionsUpdated += suggestions =>
                //{
                //    Console.WriteLine("?? Suggestions received (OutZen)");
                //};
                //SignalRService.OnWeatherUpdated += forecast =>
                //{
                //    Console.WriteLine("?? Weather received (OutZen)");
                //};
                //SignalRService.OnTrafficUpdated += traffic =>
                //{
                //    Console.WriteLine("?? Traffic received (OutZen)");
                //};
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignalR init error: {ex.Message}");
            }
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || _disposed) return;

            //var uri = Nav.ToBaseRelativePath(Nav.Uri);
            //if (!string.Equals(uri, "", StringComparison.OrdinalIgnoreCase)) return;
            //_layoutModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
            //    "import", "/js/app/leafletOutZen.module.js");

            //await _layoutModule.InvokeVoidAsync("bootOutZen", new
            //{
            //    mapId = "leafletMap",
            //    center = new[] { 50.89, 4.34 },
            //    zoom = 13,
            //    enableChart = true
            //});
        }

        private void ShowTestToast()
        {
            ToastService.ShowSuccess("It works!");
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            try
            {
                if (_layoutModule is not null)
                    await _layoutModule.DisposeAsync();

                if (SignalRService is not null)
                    await SignalRService.DisposeAsync();
            }
            catch { /* noop */ }
        }
    }
}





































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




