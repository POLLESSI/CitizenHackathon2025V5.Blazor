using CitizenHackathon2025.Contracts.Enums;
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

        protected virtual bool EnableHybrid => false;
        protected virtual bool EnableCluster => false;
        protected virtual bool MapEnabled => true;

        protected virtual (double lat, double lng) DefaultCenter => (50.85, 4.35);
        protected virtual int DefaultZoom => 13;

        protected virtual bool ForceBootOnFirstRender => false;
        protected virtual bool ResetMarkersOnBoot => false;
        protected virtual bool DisposeOnNavigate => true;

        protected virtual OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.AllowAll;
        protected virtual string? AllowedMarkerPrefix => null;

        protected virtual bool ClearAllOnMapReady => false;
        protected virtual bool PruneForeignMarkersOnMapReady => true;
        protected virtual bool EnableChart => false;
        protected virtual bool EnableWeatherLegend => false;
        protected virtual int HybridThreshold => 13;

        protected bool IsMapBooted { get; private set; }
        protected bool IsDisposed { get; private set; }

        private bool _dataLoaded;
        private bool _seededOnce;

        private bool _bootAttempted;
        private int _bootTries;
        private const int BootMaxTries = 20;          // ~ 20 renders max
        private long _lastBootAttemptTick;

        private bool _pendingFit = true;             // last requested fit

        protected virtual Task OnMapReadyAsync() => Task.CompletedTask;
        protected virtual Task OnBeforeDisposeAsync() => Task.CompletedTask;

        protected virtual Task SeedAsync(bool fit) => Task.CompletedTask;

        // ✅ public: allow page to force reseed when filter/pagination changes
        protected void InvalidateSeed() => _seededOnce = false;

        protected async Task ReseedAsync(bool fit = true)
        {
            _pendingFit = fit;
            _seededOnce = false;
            await TrySeedAsync();
        }

        protected async Task NotifyDataLoadedAsync(bool fit = true)
        {
            _dataLoaded = true;
            _pendingFit = fit;
            await EnsureBootAndSeedAsync();
        }

        private async Task EnsureBootAndSeedAsync()
        {
            if (IsDisposed || !MapEnabled) return;

            // 1) ensure boot
            if (!IsMapBooted)
                await TryBootAsync();

            // 2) seed if possible
            await TrySeedAsync();
        }

        private async Task TryBootAsync()
        {
            if (IsDisposed || !MapEnabled) return;
            if (IsMapBooted) return;

            // throttle: avoid boot spam in the same render loop
            var now = Environment.TickCount64;
            if (now - _lastBootAttemptTick < 100) return;
            _lastBootAttemptTick = now;

            if (_bootAttempted && _bootTries >= BootMaxTries) return;

            _bootAttempted = true;
            _bootTries++;

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
                ResetMarkers: ResetMarkersOnBoot,
                EnableHybrid: EnableHybrid,
                EnableCluster: EnableCluster,
                HybridThreshold: HybridThreshold
            ));

            if (!IsMapBooted) return;

            // size refresh can be too early if container just appeared
            await MapInterop.RefreshSizeAsync(ScopeKey);

            if (ClearAllOnMapReady)
                await MapInterop.ClearAllOutZenLayersAsync(ScopeKey);

            if (MarkerPolicy == OutZenMarkerPolicy.OnlyPrefix
                && PruneForeignMarkersOnMapReady
                && !string.IsNullOrWhiteSpace(AllowedMarkerPrefix))
            {
                await MapInterop.PruneMarkersByPrefixAsync(AllowedMarkerPrefix!, ScopeKey);
            }

            await OnMapReadyAsync();
        }

        private async Task TrySeedAsync()
        {
            if (IsDisposed) return;
            if (_seededOnce) return;
            if (!IsMapBooted) return;
            if (!_dataLoaded) return;

            _seededOnce = true;
            await SeedAsync(_pendingFit);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (IsDisposed || !MapEnabled) return;

            // Important: allow retry on subsequent renders until boot is OK.
            await EnsureBootAndSeedAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            try { await OnBeforeDisposeAsync(); } catch { }

            if (MapEnabled && DisposeOnNavigate)
            {
                try { await MapInterop.DisposeMapAsync(ScopeKey, MapId); } catch { }
            }
        }
    }
}






























































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.