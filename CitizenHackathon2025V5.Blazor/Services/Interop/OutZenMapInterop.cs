using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.DTOs.Options;
using Microsoft.JSInterop;
using System.Text.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services.Interop
{
    public class OutZenMapInterop
    {
        private readonly IJSRuntime _js;
        private readonly Dictionary<string, string> _tokens = new();

        public OutZenMapInterop(IJSRuntime js) => _js = js;

        public async Task EnsureAsync()
        {
            // 1) wait for the "interop ready" promise if it exists
            try
            {
                await _js.InvokeVoidAsync("eval", "globalThis.__OZ_INTEROP_READY_P__ && globalThis.__OZ_INTEROP_READY_P__");
            }
            catch
            {
                // Ignore (some policies block eval). In this case, fallback:
            }

            // 2) ensure normal
            await _js.InvokeVoidAsync("OutZen.ensure");
        }

        public async Task<bool> BootAsync(OutZenBootOptions opt)
        {
            await EnsureAsync();

            var res = await _js.InvokeAsync<BootResult>("OutZenInterop.bootOutZen", new
            {
                mapId = opt.MapId,
                scopeKey = opt.ScopeKey,
                center = new[] { opt.Lat, opt.Lng },
                zoom = opt.Zoom,
                enableChart = opt.EnableChart,
                force = opt.Force,
                resetMarkers = opt.ResetMarkers,
                enableHybrid = opt.EnableHybrid,
                enableCluster = opt.EnableCluster,
                hybridThreshold = opt.HybridThreshold
            });

            if (res?.Ok == true && !string.IsNullOrWhiteSpace(res.Token))
                _tokens[opt.ScopeKey] = res.Token;

            return res?.Ok == true;
        }

        public async Task DisposeMapAsync(string scopeKey, string mapId)
        {
            await EnsureAsync();

            _tokens.TryGetValue(scopeKey, out var token);

            await _js.InvokeVoidAsync("OutZenInterop.disposeOutZen", new
            {
                mapId,
                scopeKey,
                token,
                allowNoToken = false
            });

            _tokens.Remove(scopeKey);
        }

        public Task RefreshSizeAsync(string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.refreshMapSize", scopeKey).AsTask();

        public Task FitToDetailsAsync(string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.fitToDetails", scopeKey).AsTask();

        public Task ClearCrowdMarkersAsync(string scopeKey)
    => _js.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", scopeKey).AsTask();

        public Task RemoveCrowdMarkerAsync(string markerId, string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.removeCrowdMarker", markerId, scopeKey).AsTask();

        public Task UpsertCrowdMarkerAsync(
            string id,
            double lat,
            double lng,
            int level,
            object info,
            string scopeKey)
            => _js.InvokeVoidAsync(
                    "OutZenInterop.addOrUpdateCrowdMarker",
                    id,
                    lat,
                    lng,
                    level,
                    info,
                    scopeKey
               ).AsTask();

        public Task ClearCrowdCalendarMarkersAsync(string scopeKey)
    => _js.InvokeVoidAsync("OutZenInterop.clearCrowdCalendarMarkers", scopeKey).AsTask();

        public Task RemoveCrowdCalendarMarkerAsync(string markerId, string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.removeCrowdCalendarMarker", markerId, scopeKey).AsTask();

        public Task UpsertCrowdCalendarMarkerAsync(string id, double lat, double lng, int level, object info, string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.addOrUpdateCrowdCalendarMarker", id, lat, lng, level, info, scopeKey).AsTask();

        public Task FitToMarkersAsync(string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.fitToMarkers", scopeKey).AsTask();

        public Task ClearAllOutZenLayersAsync(string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.clearAllOutZenLayers", scopeKey).AsTask();

        public Task PruneMarkersByPrefixAsync(string allowedPrefix, string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.pruneMarkersByPrefix", allowedPrefix, scopeKey).AsTask();

        public Task ClearTrafficMarkersAsync(string scopeKey)
    => _js.InvokeVoidAsync("OutZenInterop.clearMarkersByPrefix", "traffic:", scopeKey).AsTask();

        public Task RemoveTrafficMarkerAsync(string id, string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.removeCrowdMarker", id, scopeKey).AsTask();

        public ValueTask UpsertTrafficMarkerAsync(string id, double lat, double lng, int level, object info, string scopeKey)
            => _js.InvokeVoidAsync("OutZenInterop.upsertTrafficMarker", id, lat, lng, level, info, scopeKey);

        private sealed class BootResult
        {
            public bool Ok { get; set; }
            public string? Token { get; set; }
            public string? Reason { get; set; }
        }
    }

}   


















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.