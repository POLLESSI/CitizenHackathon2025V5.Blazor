using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastUpdate : ComponentBase, IAsyncDisposable
    {
        // DI
        [Inject] public WeatherForecastService Wx { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        // State
        protected List<ClientWeatherForecastDTO> _items = new();
        protected ClientWeatherForecastDTO? _editing;
        protected string? _q;

        private HubConnection? _hub;

        protected override async Task OnInitializedAsync()
        {
            // Snapshot initial
            _items = await Wx.GetAllAsync();

            // ==== SignalR ====
            var baseUrl = (Config["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');
            var hubUrl = $"{baseUrl}/hubs/weatherforecastHub";

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? "";
                })
                .WithAutomaticReconnect()
                .Build();

            _hub.On<ClientWeatherForecastDTO>("ReceiveForecast", async dto =>
            {
                // upsert
                var i = _items.FindIndex(x => x.Id == dto.Id);
                if (i >= 0) _items[i] = dto; else _items.Insert(0, dto);

                // si on édite la même ligne, refléter l’update
                if (_editing?.Id == dto.Id)
                {
                    _editing = dto;
                }
                await InvokeAsync(StateHasChanged);
            });

            try { await _hub.StartAsync(); } catch { /* log si besoin */ }
        }

        protected IEnumerable<ClientWeatherForecastDTO> Filter(IEnumerable<ClientWeatherForecastDTO> src)
        {
            var q = _q?.Trim();
            if (string.IsNullOrEmpty(q)) return src;
            return src.Where(x =>
                (x.Summary ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || x.TemperatureC.ToString().Contains(q));
        }

        protected void Edit(ClientWeatherForecastDTO it) => _editing = new ClientWeatherForecastDTO
        {
            Id = it.Id,
            DateWeather = it.DateWeather,
            TemperatureC = it.TemperatureC,
            Summary = it.Summary,
            RainfallMm = it.RainfallMm,
            Humidity = it.Humidity,
            WindSpeedKmh = it.WindSpeedKmh,
            Icon = it.Icon,
            IconUrl = it.IconUrl,
            WeatherMain = it.WeatherMain,
            IsSevere = it.IsSevere,
            Description = it.Description
        };

        protected void Cancel() => _editing = null;

        protected async Task Save()
        {
            if (_editing is null) return;

            // Optimistic UI
            var idx = _items.FindIndex(x => x.Id == _editing.Id);
            if (idx >= 0) _items[idx] = _editing;
            StateHasChanged();

            var updated = await Wx.UpdateAsync(_editing);
            if (updated is null)
            {
                await JS.InvokeVoidAsync("alert", "Update failed.");
                return;
            }

            _editing = updated;
            StateHasChanged();
            // Le hub re-diffusera aussi l’update aux autres clients
        }

        public async ValueTask DisposeAsync()
        {
            if (_hub is not null)
            {
                try { await _hub.StopAsync(); } catch { }
                try { await _hub.DisposeAsync(); } catch { }
            }
        }
    }
}








































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.