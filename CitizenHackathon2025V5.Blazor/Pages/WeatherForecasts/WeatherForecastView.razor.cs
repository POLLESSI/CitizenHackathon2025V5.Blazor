using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using Newtonsoft.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastView : IAsyncDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }  
        [Inject] public WeatherForecastService WeatherForecastService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _outZen;

        public List<ClientWeatherForecastDTO> WeatherForecasts { get; set; } = new();
        private List<ClientWeatherForecastDTO> allWeatherForecasts = new();
        private List<ClientWeatherForecastDTO> visibleWeatherForecasts = new();
        private int currentIndex = 0;
        private const int PageSize = 20;
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        // Fields used by .razor
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = (await WeatherForecastService.GetHistoryAsync(50)).ToList();
            fetched = await WeatherForecastService.GetAllAsync();
            WeatherForecasts = fetched;
            allWeatherForecasts = fetched;
            visibleWeatherForecasts.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase.TrimEnd('/');
            var hubPath = "/hubs/weatherforecastHub";
            var hubUrl = BuildHubUrl(apiBaseUrl, hubPath);

            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        var token = await Auth.GetAccessTokenAsync();
                        return token ?? string.Empty;
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            // Handlers
            hubConnection.On<ClientWeatherForecastDTO>("ReceiveWeatherForecastUpdate", async dto =>
            {
                void Upsert(List<ClientWeatherForecastDTO> list)
                {
                    var i = list.FindIndex(c => c.Id == dto.Id);
                    if (i >= 0) list[i] = dto; else list.Add(dto);
                }

                Upsert(WeatherForecasts);
                Upsert(allWeatherForecasts);

                var j = visibleWeatherForecasts.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleWeatherForecasts[j] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateWeatherForecastMarker",
                    dto.Id.ToString(), dto.TemperatureC, dto.WindSpeedKmh, dto.Summary,
                    new { title = dto.Summary, TemperatureC = $"Maj {dto.DateWeather:HH:mm:ss}" });

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("EventArchived", async id =>
            {
                WeatherForecasts.RemoveAll(c => c.Id == id);
                allWeatherForecasts.RemoveAll(c => c.Id == id);
                visibleWeatherForecasts.RemoveAll(c => c.Id == id);

                await JS.InvokeVoidAsync("window.OutZenInterop.removeMarker", id.ToString());
                await InvokeAsync(StateHasChanged);
            });

            try { await hubConnection.StartAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[WeatherForecastView] Hub start failed: {ex.Message}"); }
        }
        private void LoadMoreItems()
        {
            var next = allWeatherForecasts.Skip(currentIndex).Take(PageSize).ToList();
            visibleWeatherForecasts.AddRange(next);
            currentIndex += next.Count;
        }

        private static string BuildHubUrl(string baseUrl, string path)
        {
            var b = baseUrl.TrimEnd('/');
            var p = path.TrimStart('/');
            if (b.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase) &&
                p.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("hubs/".Length);
            }
            return $"{b}/{p}";
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            _outZen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            await _outZen.InvokeVoidAsync("bootOutZen", new
            {
                mapId = "leafletMap",
                center = new double[] { 50.89, 4.34 },
                zoom = 13,
                enableChart = true
            });

            await _outZen.InvokeVoidAsync("initCrowdChart", "crowdChart");
        }
        private void ClickInfo(int id) => SelectedId = id;

        // Infinite scrolling (uses JS helpers: getScrollTop/getScrollHeight/getClientHeight)
        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                if (currentIndex < allWeatherForecasts.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        private IEnumerable<ClientWeatherForecastDTO> FilterWeather(IEnumerable<ClientWeatherForecastDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (x.Summary ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                            || x.TemperatureC.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent || x.DateWeather >= cutoff);
        }

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_outZen is not null)
                {
                    await _outZen.DisposeAsync();
                }
            }
            catch { /* ignore */ }

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}







































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




