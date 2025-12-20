// Pages/Index.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class Index 
    {
#nullable disable
        [Inject] public MessageService MessageService { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        // Data
        private List<ClientTrafficConditionDTO> TrafficConditionsList = new();
        private List<ClientCrowdInfoDTO> CrowdInfos = new();
        private List<ClientEventDTO> Events = new();
        private List<ClientSuggestionDTO> Suggestions = new();
        private List<ClientPlaceDTO> Places = new();
        private List<ClientWeatherForecastDTO> WeatherPoints = new();
        protected List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();
        public MessageFormModel Model { get; } = new();

        protected string NewMessage { get; set; } = string.Empty;
        protected bool IsSending { get; set; }

        // JS
        private IJSObjectReference _leafletModule;
        private IJSObjectReference _mod;
        private bool _dataLoaded;
        private bool _mapBooted;
        private bool _bundlePushed;

        private const string LeafletModulePath = "/js/app/leafletOutZen.module.js";

        private async Task ScrollToSuggestions()
            => await JS.InvokeVoidAsync("OutZen.scrollIntoViewById", "suggestions",
                new { behavior = "smooth", block = "start" });

        protected override async Task OnInitializedAsync()
        {
            var trafficTask = TrafficConditionService.GetLatestTrafficConditionAsync();
            var crowdTask = CrowdInfoService.GetLatestCrowdInfoNonNullAsync();
            var eventTask = EventService.GetLatestEventAsync();
            var suggestionTask = SuggestionService.GetLatestSuggestionAsync();
            var placeTask = PlaceService.GetLatestPlaceAsync();
            var weatherTask = WeatherForecastService.GetLatestWeatherForecastAsync();
            var gptTask = GptInteractionService.GetAllInteractions();

            await Task.WhenAll(trafficTask, crowdTask, eventTask, suggestionTask, placeTask, weatherTask, gptTask);

            TrafficConditionsList = trafficTask.Result ?? new();
            CrowdInfos = crowdTask.Result ?? new();
            Events = (eventTask.Result ?? Enumerable.Empty<ClientEventDTO>()).ToList();
            Suggestions = (suggestionTask.Result ?? Enumerable.Empty<ClientSuggestionDTO>()).ToList();
            Places = (placeTask.Result ?? Enumerable.Empty<ClientPlaceDTO>()).ToList();
            WeatherPoints = (weatherTask.Result ?? Enumerable.Empty<ClientWeatherForecastDTO>()).ToList();
            GptInteractions = (gptTask.Result ?? Enumerable.Empty<ClientGptInteractionDTO>()).ToList();

            _dataLoaded = true;
            await TryPushBundleAsync();

            try
            {
                var data = await GptInteractionService.GetAllInteractions(); // Adapt if your service exposes a different name
                if (data is not null) GptInteractions = data.ToList();
            }
            catch
            {
                // You can log in/toast if you want, but avoid breaking the page.
                GptInteractions = new();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            try
            {
                _leafletModule = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

                var ok = await _leafletModule.InvokeAsync<bool>("bootOutZen", new
                {
                    mapId = "homeMap",
                    center = new[] { 50.85, 4.35 },
                    zoom = 8,
                    enableChart = false,
                    force = true,
                    enableWeatherLegend = true
                });

                _mapBooted = ok;

                await TryPushBundleAsync(); // relance après boot
            }
            catch (JSException jse)
            {
                Console.Error.WriteLine("[Index] JS init failed: " + jse.Message);
            }

            if (firstRender)
            {
                //await JS.InvokeVoidAsync("OutZenInterop.bootOutZen", new
                //{
                //    mapId = "homeMap",
                //    center = new[] { 50.45, 4.6 },
                //    zoom = 12,
                //    enableChart = false,
                //    force = true
                //});

                // construire ton payload multi-types (events/places/crowds/traffic/gpt)
                var payload = new
                {
                    events = Events,
                    places = Places,
                    crowds = CrowdInfos,
                    traffic = TrafficConditionsList,
                    gpt = GptInteractions
                };

                //await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateBundleMarkers", payload, new { radiusMeters = 80 });

                // activates the hybrid after injecting the bundles
                //await JS.InvokeVoidAsync("OutZenInterop.enableHybridZoom", new { threshold = 13 });
            }
        }
        private async Task TryPushBundleAsync()
        {
            if (!_dataLoaded || !_mapBooted || _bundlePushed || _leafletModule is null)
                return;

            try
            {
                await _leafletModule.InvokeVoidAsync("addOrUpdateBundleMarkers", new
                {
                    events = Events,
                    places = Places,
                    crowds = CrowdInfos,
                    traffic = TrafficConditionsList,
                    gpt = GptInteractions,
                    // suggestions / weather if you add them to computeBundles
                }, 80);

                await _leafletModule.InvokeVoidAsync("fitToBundles", 30);

                _bundlePushed = true;

                await _leafletModule.InvokeVoidAsync("enableHybridZoom", 15);

            }
            catch (JSException jse)
            {
                Console.Error.WriteLine("[Index] addOrUpdateBundleMarkers failed: " + jse.Message);
            }
        }


        // ---- Actions ----
        public async Task SendMessageAsync()
        {
            if (IsSending) return;
            if (string.IsNullOrWhiteSpace(NewMessage)) return;

            IsSending = true;
            try
            {
                await MessageService.PostAsync(NewMessage);
                NewMessage = "";
            }
            finally
            {
                IsSending = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        // ---- Helpers ----
        private static string Shorten(string s, int max)
            => string.IsNullOrWhiteSpace(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
        protected Task EnableSoundAsync()
        {
            // future JS hook or user settings
            return Task.CompletedTask;
        }

        public sealed class MessageFormModel
        {
            [Required]
            [MinLength(2)]
            public string NewMessage { get; set; } = string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_leafletModule is not null)
                    await _leafletModule.DisposeAsync();
            }
            catch { /* ignore */ }
        }

    }
}
