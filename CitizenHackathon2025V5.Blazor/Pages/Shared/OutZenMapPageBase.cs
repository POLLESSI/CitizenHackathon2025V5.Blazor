using Microsoft.AspNetCore.Components;
using CitizenHackathon2025V5.Blazor.Client.Services.Interop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Shared
{
    public abstract class OutZenMapPageBase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected OutZenMapInterop MapInterop { get; set; } = default!;

        // ===== Page contract =====
        protected abstract string ScopeKey { get; }
        protected abstract string MapId { get; }

        // Some pages may not have a map (ex: AntennaCrowdPanelView)
        protected virtual bool MapEnabled => true;

        // Boot options
        protected virtual (double lat, double lng) DefaultCenter => (50.85, 4.35);
        protected virtual int DefaultZoom => 12;
        protected virtual bool EnableChart => false;
        protected virtual bool EnableWeatherLegend => false;

        // Behavior toggles
        protected virtual bool ForceBootOnFirstRender => false;
        protected virtual bool ResetMarkersOnBoot => false;
        protected virtual bool DisposeOnNavigate => true;

        private bool _booted;
        private bool _disposed;

        // Optional hooks for derived pages
        protected virtual Task OnMapReadyAsync() => Task.CompletedTask;
        protected virtual Task OnBeforeDisposeAsync() => Task.CompletedTask;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            if (!MapEnabled) return;

            // Ensures JS is ready
            await MapInterop.EnsureAsync();

            var (lat, lng) = DefaultCenter;
            _booted = await MapInterop.BootAsync(
                mapId: MapId,
                scopeKey: ScopeKey,
                lat: lat,
                lng: lng,
                zoom: DefaultZoom,
                enableChart: EnableChart,
                force: ForceBootOnFirstRender,
                enableWeatherLegend: EnableWeatherLegend,
                resetMarkers: ResetMarkersOnBoot
            );

            if (_booted)
            {
                // Fix sizing issues after first layout (common in Blazor)
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await OnMapReadyAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try { await OnBeforeDisposeAsync(); } catch { /* swallow */ }

            if (MapEnabled && DisposeOnNavigate)
            {
                try { await MapInterop.DisposeMapAsync(ScopeKey, MapId); } catch { /* swallow */ }
            }
        }
    }
}
