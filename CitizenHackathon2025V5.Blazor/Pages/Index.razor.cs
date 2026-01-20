// Pages/Index.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Pages.OutZens;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Utils.OutZen;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class Index 
    {
#nullable disable
        [Inject] public MessageService MessageService { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public TrafficConditionService TrafficConditionService { get; set; } = default!;
        [Inject] public CrowdInfoService CrowdInfoService { get; set; } = default!;
        [Inject] public EventService EventService { get; set; } = default!;
        [Inject] public SuggestionService SuggestionService { get; set; } = default!;
        [Inject] public PlaceService PlaceService { get; set; } = default!;
        [Inject] public WeatherForecastService WeatherForecastService { get; set; } = default!;
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;


        // Data
        private List<ClientTrafficConditionDTO> TrafficConditionsList = new();
        private List<ClientCrowdInfoDTO> CrowdInfos = new();
        private List<ClientEventDTO> Events = new();
        private List<ClientSuggestionDTO> Suggestions = new();
        private List<ClientPlaceDTO> Places = new();
        private List<ClientWeatherForecastDTO> WeatherPoints = new();
        protected List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();
        public MessageFormModel Model { get; } = new();
        private const string _homeMapId = "leafletMap-home";
        protected string NewMessage { get; set; } = string.Empty;
        protected bool IsSending { get; set; }

        // JS
        private IJSObjectReference _leafletModule;
        private IJSObjectReference _mod;
        private bool _dataLoaded;
        private bool _mapBooted;
        private bool _bundlePushed;

        //private const string LeafletModulePath = "/js/app/leafletOutZen.module.js";

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

            await InvokeAsync(StateHasChanged);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            var uri = Navigation.ToBaseRelativePath(Navigation.Uri);
            if (!string.IsNullOrWhiteSpace(uri)) return;

            Console.WriteLine($"[Index] baseRel='{uri}' full='{Navigation.Uri}' path='{new Uri(Navigation.Uri).AbsolutePath}'");

            // 1) Boot map only once
            if (!_mapBooted)
            {
                await JS.InvokeVoidAsync("OutZen.ensure");

                _mapBooted = await JS.InvokeAsync<bool>("OutZenInterop.bootOutZen", new
                {
                    mapId = _homeMapId,
                    center = new[] { 50.45, 4.6 },
                    zoom = 13,
                    force = true,
                    enableChart = false,
                    enableWeatherLegend = true,
                    resetMarkers = true
                });

                Console.WriteLine($"[Index] bootOutZen ok={_mapBooted}");
            }

            if (!_mapBooted) return;

            // 2) Push bundles as soon as dataLoaded becomes true (and only once).
            if (_dataLoaded && !_bundlePushed)
            {
                try { await PushBundlesOnceAsync(); }
                catch (Exception ex) { Console.Error.WriteLine($"[Index] ❌ PushBundlesOnceAsync failed: {ex}"); }
            }
        }


        private async Task PushBundlesOnceAsync()
        {
            Console.WriteLine($"[Index] PushBundlesOnceAsync ENTER dataLoaded={_dataLoaded} bundlePushed={_bundlePushed}");
            if (!_dataLoaded || _bundlePushed) return;

            _bundlePushed = true; // ✅ reserve immediately

            try
            {
                var payload = new
                {
                    events = Events,
                    places = Places,
                    crowds = CrowdInfos,
                    suggestions = Suggestions,
                    traffic = TrafficConditionsList,
                    weather = WeatherPoints,
                    gpt = GptInteractions
                };

                var ok = await JS.InvokeAsync<bool>("OutZenInterop.addOrUpdateBundleMarkers", payload, 80);

                Console.WriteLine($"[Index] addOrUpdateBundleMarkers ok={ok}");

                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateWeatherMarkers", WeatherPoints);

                //if (!ok)
                //{
                //    _bundlePushed = false; // Rollback if you want to try again
                //    return;
                //}
                if (!ok) { _bundlePushed = false; return; }

                await JS.InvokeVoidAsync("OutZenInterop.enableHybridZoom", new { threshold = 13 });
                await JS.InvokeVoidAsync("OutZenInterop.fitToBundles", 30);
                await JS.InvokeVoidAsync("OutZenInterop.activateHybridAndZoom", 13, 13);
                await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow");
            }
            catch (Exception ex)
            {
                _bundlePushed = false; // rollback
                Console.Error.WriteLine($"[Index] PushBundlesOnceAsync FAIL: {ex}");
                throw;
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
                    //await _leafletModule.DisposeAsync();
                    await JS.InvokeVoidAsync("OutZenInterop.disposeOutZen", new { mapId = _homeMapId });
            }
            catch { /* ignore */ }
        }
    }
}






















































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.