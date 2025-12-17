// Pages/Index.razor.cs (cleaned)
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class Index : IAsyncDisposable
    {
    #nullable disable
        //[Inject] private IJSRuntime JS { get; set; }
        //[Inject] private CrowdInfoService CrowdInfoService { get; set; }
        //[Inject] private EventService EventService { get; set; }
        //[Inject] private SuggestionService SuggestionService { get; set; }
        //[Inject] private PlaceService PlaceService { get; set; }
        //[Inject] private TrafficConditionService TrafficConditionService { get; set; }
        //[Inject] private WeatherForecastService WeatherForecastService { get; set; }
        //[Inject] private GptInteractionService GptInteractionService { get; set; }

        // Data
        private List<ClientTrafficConditionDTO> TrafficConditionsList = new();
        private List<ClientCrowdInfoDTO> CrowdInfos = new();
        private List<ClientEventDTO> Events = new();
        private List<ClientSuggestionDTO> Suggestions = new();
        private List<ClientPlaceDTO> Places = new();
        private List<ClientWeatherForecastDTO> WeatherPoints = new();
        private List<ClientGptInteractionDTO> GptInteractions = new();

        // JS
        private IJSObjectReference _leafletModule;
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
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            try
            {
                // Import ESM once
                _leafletModule = await JS.InvokeAsync<IJSObjectReference>("import", LeafletModulePath);

                // Boot map once (id used in your Index.razor is "homeMap") :contentReference[oaicite:6]{index=6}
                // We force boot to handle initial size race; the module must be singleton-safe.
                _mapBooted = await _leafletModule.InvokeAsync<bool>("bootOutZen", new
                {
                    mapId = "homeMap",
                    // choose your center/zoom defaults
                    center = new[] { 50.39, 4.71 },
                    zoom = 12,
                    enableChart = false,
                    force = true
                });

                await TryPushBundleAsync();
            }
            catch (JSException jse)
            {
                Console.Error.WriteLine("[Index] JS init failed: " + jse.Message);
            }
        }

        private async Task TryPushBundleAsync()
        {
            if (!_dataLoaded || !_mapBooted || _bundlePushed || _leafletModule is null)
                return;

            try
            {
                // IMPORTANT: This function must exist on the module (leafletOutZen.module.js)
                // and must respect window.__OutZenSingleton + existing cluster.
                await _leafletModule.InvokeVoidAsync(
                    "addOrUpdateBundleMarkers",
                    new
                    {
                        events = Events,
                        places = Places,
                        crowds = CrowdInfos,
                        traffic = TrafficConditionsList
                    },
                    80 // tolerance meters (you asked 80)
                );

                _bundlePushed = true;
            }
            catch (JSException jse)
            {
                Console.Error.WriteLine("[Index] addOrUpdateBundleMarkers failed: " + jse.Message);
            }
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
