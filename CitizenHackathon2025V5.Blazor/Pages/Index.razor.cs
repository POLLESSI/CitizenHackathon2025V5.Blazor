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
            await PushBundlesOnceAsync();

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

            _leafletModule = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            _mapBooted = await _leafletModule.InvokeAsync<bool>("bootOutZen", new
            {
                mapId = "leafletMap",
                center = new[] { 50.45, 4.6 },
                zoom = 12,
                enableChart = false,
                force = true,
                enableWeatherLegend = true
            });

            if (!_mapBooted) return;

            // push when everything is ready
            await PushBundlesOnceAsync();
        }

        private async Task PushBundlesOnceAsync()
        {
            if (!_dataLoaded || _bundlePushed || _leafletModule is null) return;

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

            await _leafletModule.InvokeVoidAsync("addOrUpdateBundleMarkers", payload, 80);
            await _leafletModule.InvokeVoidAsync("enableHybridZoom", new { threshold = 13 });
            await _leafletModule.InvokeVoidAsync("fitToBundles", 30);

            _bundlePushed = true;
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






















































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.