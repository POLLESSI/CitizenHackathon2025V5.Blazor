using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Services.Interop
{
    public sealed class OutZenMapInterop : IAsyncDisposable
    {
        private readonly IJSRuntime _js;
        private IJSObjectReference? _module; // outzen-interop.js as module OR window.OutZenInterop wrapper usage

        public OutZenMapInterop(IJSRuntime js) => _js = js;

        /// <summary>
        /// Ensures OutZenInterop is loaded and the underlying ESM is available.
        /// Assumes you included /js/outzen-interop.js in index.html as a classic script.
        /// </summary>
        public async ValueTask EnsureAsync()
        {
            // OutZen.ensure() returns true/false in your JS
            var ok = await _js.InvokeAsync<bool>("OutZen.ensure");
            if (!ok) throw new InvalidOperationException("OutZen.ensure() failed. Check script loading order.");
        }

        public async ValueTask<bool> BootAsync(string mapId, string scopeKey, double lat = 50.85, double lng = 4.35, int zoom = 12,
            bool enableChart = false, bool force = false, bool enableWeatherLegend = false, bool resetMarkers = false)
        {
            await EnsureAsync();

            // calling global wrapper: OutZenInterop.bootOutZen({ ... })
            var opts = new
            {
                mapId,
                scopeKey,
                center = new[] { lat, lng },
                zoom,
                enableChart,
                force,
                enableWeatherLegend,
                resetMarkers
            };

            return await _js.InvokeAsync<bool>("OutZenInterop.bootOutZen", opts);
        }

        public async ValueTask<bool> IsReadyAsync(string scopeKey)
        {
            await EnsureAsync();
            return await _js.InvokeAsync<bool>("OutZenInterop.isOutZenReady", scopeKey);
        }

        public async ValueTask DisposeMapAsync(string scopeKey, string? mapId = null)
        {
            // disposeOutZen({ mapId, scopeKey })
            await EnsureAsync();
            await _js.InvokeVoidAsync("OutZenInterop.disposeOutZen", new { mapId, scopeKey });
        }

        public async ValueTask RefreshSizeAsync(string scopeKey)
        {
            await EnsureAsync();
            await _js.InvokeVoidAsync("OutZenInterop.refreshMapSize", scopeKey);
        }

        public async ValueTask FitToBundlesAsync(string scopeKey, int padding = 30)
        {
            await EnsureAsync();
            await _js.InvokeAsync<bool>("OutZenInterop.fitToBundles", padding, scopeKey);
        }

        public async ValueTask FitToDetailsAsync(string scopeKey, int padding = 30)
        {
            await EnsureAsync();
            await _js.InvokeAsync<bool>("OutZenInterop.fitToDetails", padding, scopeKey);
        }

        public async ValueTask FitToCalendarAsync(string scopeKey)
        {
            await EnsureAsync();
            await _js.InvokeAsync<bool>("OutZenInterop.fitToCalendarMarkers", scopeKey);
        }

        // Bundles payload is typically an anonymous object matching your JS expectations
        public async ValueTask<bool> UpsertBundlesAsync(object payload, int tolMeters, string scopeKey)
        {
            await EnsureAsync();
            return await _js.InvokeAsync<bool>("OutZenInterop.addOrUpdateBundleMarkers", payload, tolMeters, scopeKey);
        }

        public async ValueTask<bool> UpsertCrowdMarkerAsync(string id, double lat, double lng, int level, object info, string scopeKey)
        {
            await EnsureAsync();
            return await _js.InvokeAsync<bool>("OutZenInterop.addOrUpdateCrowdMarker", id, lat, lng, level, info, scopeKey);
        }

        public async ValueTask ClearCrowdMarkersAsync(string scopeKey)
        {
            await EnsureAsync();
            await _js.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", scopeKey);
        }

        public async ValueTask ClearCalendarAsync()
        {
            await EnsureAsync();
            // you currently expose window.clearCrowdCalendarMarkers
            await _js.InvokeVoidAsync("clearCrowdCalendarMarkers");
        }

        public async ValueTask<bool> UpsertCalendarAsync(object items, string scopeKey)
        {
            await EnsureAsync();
            // you exposed OutZenDebug.upsertCrowdCalendarMarkers; better to expose OutZenInterop.upsertCrowdCalendarMarkers later
            return await _js.InvokeAsync<bool>("OutZenDebug.upsertCrowdCalendarMarkers", items, scopeKey);
        }

        public ValueTask DisposeAsync()
        {
            _module?.DisposeAsync();
            _module = null;
            return ValueTask.CompletedTask;
        }
    }
}
