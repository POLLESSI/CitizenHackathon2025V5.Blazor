using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.Net;

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
        private IJSObjectReference _outZen;

        public List<ClientWeatherForecastDTO> WeatherForecasts { get; set; } = new();
        private List<ClientWeatherForecastDTO> allWeatherForecasts = new();
        private List<ClientWeatherForecastDTO> visibleWeatherForecasts = new();
        private int currentIndex = 0;
        private const int PageSize = 20;
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = await WeatherForecastService.GetAllAsync();

            Console.WriteLine($"[WF-Client] Loaded {fetched?.Count ?? 0} forecasts from API.");
            if (fetched is { Count: > 0 })
            {
                var first = fetched[0];
                Console.WriteLine($"[WF-Client] First = Id={first.Id}, Lat={first.Latitude}, Lon={first.Longitude}");
            }

            WeatherForecasts = fetched;
            allWeatherForecasts = fetched;
            visibleWeatherForecasts.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            //var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase;

            // URL finale : https://localhost:7254/hubs/weatherforecastHub
            var url = $"{apiBaseUrl}/hubs/{WeatherForecastHubMethods.HubPath}";
            //var url = $"{apiBaseUrl}/hubs/{HubPaths.WeatherForecast.TrimStart('/')}";

            Console.WriteLine($"[WF-Client] Hub URL = {url}");
            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;

                    // ✅ Bypass completely /negotiate
                    //options.SkipNegotiation = true;
                    //options.Transports = HttpTransportType.WebSockets;
                })
                .WithAutomaticReconnect()
                .Build();

            // === Handler: aligned with the server event "ReceiveForecast" ===
            hubConnection.On<ClientWeatherForecastDTO>( WeatherForecastHubMethods.ToClient.ReceiveForecast, async dto =>
            {
                void Upsert(List<ClientWeatherForecastDTO> list)
                {
                    var i = list.FindIndex(c => c.Id == dto.Id);
                    if (i >= 0) list[i] = dto;
                    else list.Add(dto);
                }

                Upsert(WeatherForecasts);
                Upsert(allWeatherForecasts);

                var j = visibleWeatherForecasts.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleWeatherForecasts[j] = dto;
                else visibleWeatherForecasts.Insert(0, dto);

                if (_outZen is not null)
                {
                    await AddWeatherMarkerAsync(dto);
                    await _outZen.InvokeVoidAsync("fitToMarkers");

                    // Minor chart update: we're only keeping the last X
                    var recent = allWeatherForecasts
                        .OrderByDescending(x => x.DateWeather)
                        .Take(24)
                        .OrderBy(x => x.DateWeather)
                        .Select(x => new
                        {
                            label = x.DateWeather.ToString("HH:mm"),
                            value = x.TemperatureC,
                            isSevere = x.IsSevere,
                            temperature = x.TemperatureC
                        });

                    await _outZen.InvokeVoidAsync("setWeatherChart", recent, _selectedMetric.ToString());
                }

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>(
                WeatherForecastHubMethods.ToClient.EventArchived,
                async id =>
                {
                    WeatherForecasts.RemoveAll(c => c.Id == id);
                    allWeatherForecasts.RemoveAll(c => c.Id == id);
                    visibleWeatherForecasts.RemoveAll(c => c.Id == id);

                    if (_outZen is not null)
                    {
                        await _outZen.InvokeVoidAsync("removeCrowdMarker", id);
                        await _outZen.InvokeVoidAsync("fitToMarkers");
                    }

                    await InvokeAsync(StateHasChanged);
                });

                // SignalR customer event subscription
                WeatherHub.HeavyRainReceived += OnHeavyRainReceived;

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine("[WF-Client] hubConnection.StartAsync() OK.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WF-Client] hubConnection.StartAsync() FAILED: {ex.Message}");
            }

            //try
            //{
            //    await WeatherHub.StartAsync();
            //    Console.WriteLine("[WF-Client] WeatherHub.StartAsync() OK.");
            //}
            //catch (Exception ex)
            //{
            //    Console.Error.WriteLine($"[WF-Client] WeatherHub.StartAsync() FAILED: {ex.Message}");
            //}

        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            _outZen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            await _outZen.InvokeVoidAsync("bootOutZen", new
            {
                mapId = "leafletMap",
                center = new double[] { 50.5, 4.7 },
                zoom = 13,
                enableChart = true,
                force = true,
                enableWeatherLegend = true
            });

            //Console.WriteLine($"[WF-Client] OnAfterRenderAsync: adding {allWeatherForecasts.Count} markers");

            // ➜ we push all the historical forecasts onto the map
            foreach (var dto in allWeatherForecasts)
                await AddWeatherMarkerAsync(dto);

            // ➜ Then we refocus on all the markers.
            await _outZen.InvokeVoidAsync("fitToMarkers");
            await UpdateChartAsync();

            // === Weather chart ===
            var chartPoints = allWeatherForecasts
                .OrderByDescending(x => x.DateWeather)
                .Take(24)                              // e.g., the last 24 statements
                .OrderBy(x => x.DateWeather)           // chronological order for the X-axis
                .Select(x => new
                {
                    label = x.DateWeather.ToString("HH:mm"),
                    value = x.TemperatureC,
                    isSevere = x.IsSevere,
                    temperature = x.TemperatureC
                });

            await _outZen.InvokeVoidAsync("setWeatherChart", chartPoints, _selectedMetric.ToString());
        }


        /// <summary>
        /// Adds or updates a marker for a forecast.
        /// </summary>
        private async Task AddWeatherMarkerAsync(ClientWeatherForecastDTO dto)
        {
            Console.WriteLine($"[WF-Client] AddWeatherMarkerAsync Id={dto.Id}, Lat={dto.Latitude}, Lon={dto.Longitude}");

            if (_outZen is null) return;

            var lat = dto.Latitude.HasValue ? (double)dto.Latitude.Value : 50.85;
            var lng = dto.Longitude.HasValue ? (double)dto.Longitude.Value : 4.35;

            //// We consider that 0 / 0 = "no valid coordinates"
            //if (dto.Latitude.HasValue && dto.Longitude.HasValue &&
            //    dto.Latitude.Value != 0 && dto.Longitude.Value != 0)
            //{
            //    lat = (double)dto.Latitude.Value;
            //    lng = (double)dto.Longitude.Value;
            //}
            //else
            //{
            //    // Fallback: Wallonia/Brussels Centre
            //    lat = 50.85;
            //    lng = 4.35;
            //}

            await _outZen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                dto.Id,
                lat,
                lng,
                dto.IsSevere ? 4 : 2,
                new
                {
                    title = dto.Summary ?? "Weather",
                    description = $"Temp: {dto.TemperatureC}°C, Vent: {dto.WindSpeedKmh} km/h (Maj {dto.DateWeather:HH:mm:ss})",
                    isTraffic = false,
                    weatherType = dto.WeatherType.ToString()
                });
        }



        //private async Task AddWeatherMarkerViaGptAsync(ClientWeatherForecastDTO dto)
        //{
        //    if (_outZen is null) return;

        //    var lat = dto.Latitude.HasValue ? (double)dto.Latitude.Value : 50.5;
        //    var lng = dto.Longitude.HasValue ? (double)dto.Longitude.Value : 4.7;

        //    var payload = new
        //    {
        //        Id = dto.Id,
        //        Latitude = lat,
        //        Longitude = lng,
        //        SourceType = "Weather",
        //        Prompt = dto.Summary ?? "Weather forecast",
        //        Response = $"Temp: {dto.TemperatureC}°C, Vent: {dto.WindSpeedKmh} km/h, Pluie: {dto.RainfallMm} mm",
        //        CrowdLevel = dto.IsSevere ? 4 : 2
        //    };

        //    await _outZen.InvokeVoidAsync("addOrUpdateGptMarker", payload);
        //}


        private void LoadMoreItems()
        {
            var next = allWeatherForecasts.Skip(currentIndex).Take(PageSize).ToList();
            visibleWeatherForecasts.AddRange(next);
            currentIndex += next.Count;
        }

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

        private async Task ChangeMetric(WeatherMetric metric)
        {
            _selectedMetric = metric;
            await UpdateChartAsync();
        }

        private async Task UpdateChartAsync()
        {
            if (_outZen is null || allWeatherForecasts.Count == 0)
                return;

            var recent = allWeatherForecasts
                .OrderByDescending(x => x.DateWeather)
                .Take(24)
                .OrderBy(x => x.DateWeather)
                .Select(x => new
                {
                    label = x.DateWeather.ToString("HH:mm"),
                    value = _selectedMetric switch
                    {
                        WeatherMetric.Temperature => x.TemperatureC,
                        WeatherMetric.Humidity => x.Humidity,
                        WeatherMetric.Wind => x.WindSpeedKmh,
                        _ => x.TemperatureC
                    },
                    isSevere = x.IsSevere,
                    temperature = x.TemperatureC,
                    humidity = x.Humidity,
                    windSpeed = x.WindSpeedKmh
                });

            await _outZen.InvokeVoidAsync("setWeatherChart",
                recent,
                _selectedMetric.ToString()); // "Temperature", "Humidity", "Wind"
        }

        private enum WeatherMetric
        {
            Temperature,
            Humidity,
            Wind
        }

        private WeatherMetric _selectedMetric = WeatherMetric.Temperature;


        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        private void ClickInfo(int id) => SelectedId = id;

        private void OnHeavyRainReceived(RainAlertDTO alert)
        {
            // Always go through the Blazor dispatcher.
            _ = InvokeAsync(() =>
            {
                _currentRainAlert = alert;
                StateHasChanged();
            });
        }

        private Task OnAlertDismissed()
        {
            _currentRainAlert = null;
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_outZen is not null)
                    await _outZen.DisposeAsync();
                WeatherHub.HeavyRainReceived -= OnHeavyRainReceived;
                //await WeatherHub.DisposeAsync();
            }
            catch { }

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }

        private async Task GenerateOne()
        {
            var dto = await WeatherForecastService.GenerateNewForecastAsync();
            if (dto is not null)
            {
                allWeatherForecasts.Insert(0, dto);
                visibleWeatherForecasts.Insert(0, dto);
                WeatherForecasts.Insert(0, dto);

                if (_outZen is not null)
                {
                    await AddWeatherMarkerAsync(dto);
                    await _outZen.InvokeVoidAsync("fitToMarkers");
                }

                StateHasChanged();
            }
        }

        private static string GetWeatherCss(bool isSevere)
            => isSevere ? "severe--true" : "severe--false";
    }
}


































































































7




// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.




