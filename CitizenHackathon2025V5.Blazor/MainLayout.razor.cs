using Blazored.Toast.Services;
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

        private OutZenSignalRService? SignalRService;
        private IJSObjectReference? _layoutModule;

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
            // 1) OutZen via service dédié (JWT géré dans la factory/service)
            SignalRService = await SignalRFactory.CreateAsync();
            await SignalRService.InitializeOutZenAsync();

            // 2) MultiHubSignalRClient pour les autres hubs (sans OutZen pour éviter la double connexion)
            await Hubs.ConnectAsync(new[]
            {
                HubName.Crowd,
                HubName.Suggestion,
                HubName.WeatherForecast
            });

            // 3) Enregistrer les handlers UNE seule fois
            if (!_wired)
            {
                _wired = true;

                Hubs.RegisterHandler<string>(HubName.Crowd, "notifynewCrowd", msg =>
                {
                    Console.WriteLine($"[Crowd] {msg}");
                    // TODO: MAJ UI / état
                });

                Hubs.RegisterHandler(HubName.Suggestion, "NewSuggestion", () =>
                {
                    Console.WriteLine("[Suggestion] NewSuggestion");
                    // TODO: rafraîchir liste
                });

                Hubs.RegisterHandler<string>(HubName.WeatherForecast, "NewWeatherForecast", forecast =>
                {
                    Console.WriteLine($"[WF] {forecast}");
                    // TODO: MAJ météo
                });
            }

            // 4) Abonnements côté OutZen service — ⚠️ pas de return Task
            SignalRService.OnCrowdInfoUpdated += dto =>
            {
                Console.WriteLine("📡 CrowdInfo received (OutZen)");
            };
            SignalRService.OnSuggestionsUpdated += suggestions =>
            {
                Console.WriteLine("📡 Suggestions received (OutZen)");
            };
            SignalRService.OnWeatherUpdated += forecast =>
            {
                Console.WriteLine("📡 Weather received (OutZen)");
            };
            SignalRService.OnTrafficUpdated += traffic =>
            {
                Console.WriteLine("📡 Traffic received (OutZen)");
            };
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            try
            {
                _layoutModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./js/layoutCanvas.js"
                );

                await _layoutModule.InvokeVoidAsync("startBackgroundCanvas");

                // Autres initialisations visuelles (sans connexion SignalR côté JS)
                await JSRuntime.InvokeVoidAsync("GeometryCanvas.init");
                await JSRuntime.InvokeVoidAsync("initializeLeafletMap");
                await JSRuntime.InvokeVoidAsync("initScrollAnimations");
                await JSRuntime.InvokeVoidAsync("initParallax");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ JS error in MainLayout: {ex.Message}");
            }
        }

        private void ShowTestToast()
        {
            ToastService.ShowSuccess("It works!");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_layoutModule is not null)
                    await _layoutModule.DisposeAsync();

                if (SignalRService is not null)
                    await SignalRService.DisposeAsync();
                // Ne pas disposer Hubs (Singleton DI)
            }
            catch { /* noop */ }
        }
    }
}





































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.