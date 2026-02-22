using CitizenHackathon2025V5.Blazor.Client.DTOs.Options;
using CitizenHackathon2025V5.Blazor.Client.Services.Interop;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Shared
{
    public abstract class OutZenMapPageBase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected OutZenMapInterop MapInterop { get; set; } = default!;

        protected abstract string ScopeKey { get; }
        protected abstract string MapId { get; }

        protected virtual bool MapEnabled => true;

        protected virtual (double lat, double lng) DefaultCenter => (50.85, 4.35);
        protected virtual int DefaultZoom => 13;
        protected virtual bool EnableChart => false;
        protected virtual bool EnableWeatherLegend => false;

        protected virtual bool ForceBootOnFirstRender => false;
        protected virtual bool ResetMarkersOnBoot => false;
        protected virtual bool DisposeOnNavigate => true;

        protected bool IsMapBooted { get; private set; }
        private bool _disposed;

        protected virtual Task OnMapReadyAsync() => Task.CompletedTask;
        protected virtual Task OnBeforeDisposeAsync() => Task.CompletedTask;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            if (!MapEnabled) return;

            await MapInterop.EnsureAsync();

            var (lat, lng) = DefaultCenter;

            IsMapBooted = await MapInterop.BootAsync(new OutZenBootOptions(
                MapId: MapId,
                ScopeKey: ScopeKey,
                Lat: lat,
                Lng: lng,
                Zoom: DefaultZoom,
                EnableChart: EnableChart,
                Force: ForceBootOnFirstRender,
                EnableWeatherLegend: EnableWeatherLegend,
                ResetMarkers: ResetMarkersOnBoot
            ));

            if (IsMapBooted)
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await OnMapReadyAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try { await OnBeforeDisposeAsync(); } catch { }

            if (MapEnabled && DisposeOnNavigate)
            {
                try { await MapInterop.DisposeMapAsync(ScopeKey, MapId); } catch { }
            }
        }
    }

}






























































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.