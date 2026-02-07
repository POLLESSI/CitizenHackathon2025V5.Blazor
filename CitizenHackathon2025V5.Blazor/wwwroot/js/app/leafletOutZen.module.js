// wwwroot/js/app/leafletOutZen.module.js
/* global L, Chart */
"use strict";

/* =========================================================
   OutZen Leaflet Module (ESM)
   - Hot reload safe singleton
   - bootOutZen(map)
   - Markers (generic + weather)
   - Bundles (grouped by proximity)
   - Hybrid zoom: bundles far, details near
   - Incremental weather updates: upsertWeatherIntoBundleInput + scheduleBundleRefresh
   ========================================================= */

function pickScopeKey(scopeKey) {
    return scopeKey || globalThis.__OutZenActiveScope || "main";
}
function peekS(scopeKey = "main") {
    const key = "__OutZenSingleton__" + String(scopeKey || "main");
    return globalThis[key] ?? null; // ✅ does not create anything
}
export function dumpState(scopeKey = "main") {
    const k = pickScopeKey(scopeKey);
    const s =
        getS(k) ||
        globalThis.__OutZenGetS?.(k) ||
        globalThis.__OutZenGetS?.() ||
        null;

    if (!s) return { loaded: false, scopeKey: k };

    return {
        loaded: true,
        scopeKey: k,
        mapId: s.mapContainerId ?? null,
        zoom: s.map?.getZoom?.() ?? null,
        hasClusterLayer: !!(s.cluster && s.map?.hasLayer?.(s.cluster)),
        initialized: !!s.initialized,
        hasMap: !!s.map,
        markers: s.markers?.size ?? 0,
        bundleMarkers: s.bundleMarkers?.size ?? 0,
        detailMarkers: s.detailMarkers?.size ?? 0,
        hybrid: s.hybrid ?? null,
    };
}
export function listScopes() {
    const out = [];
    for (const k of Object.keys(globalThis)) {
        if (!k.startsWith("__OutZenSingleton__")) continue;
        const scopeKey = k.replace("__OutZenSingleton__", "");
        const s = globalThis[k];
        out.push({
            scopeKey,
            mapId: s?.mapContainerId ?? null,
            hasMap: !!s?.map,
            initialized: !!s?.initialized,
            bootTs: s?.bootTs ?? 0,
            markers: s?.markers?.size ?? 0,
            bundleMarkers: s?.bundleMarkers?.size ?? 0,
        });
    }
    out.sort((a, b) => (b.bootTs || 0) - (a.bootTs || 0));
    console.table(out);
    return out;
}

// ------------------------------
// Singleton (Hot Reload safe)
// ------------------------------
function initState(s) {
    // Defaults (per-scope)
    s.consts ??= {};
    s.utils ??= {};
    s.flags ??= {};

    // Consts / Utils
    s.consts.BELGIUM ??= { minLat: 49.45, maxLat: 51.6, minLng: 2.3, maxLng: 6.6 };

    s.utils.safeNum ??= (x) => {
        if (x == null) return null;
        if (typeof x === "string") x = x.replace(",", ".");
        const n = Number(x);
        return Number.isFinite(n) ? n : null;
    };

    s.utils.escapeHtml ??= (v) =>
        String(v ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");

    s.utils.titleOf ??= (kind, item) => {
        if (!item) return kind;
        const tr = (v) => (v == null ? "" : String(v)).trim();

        const title =
            tr(item.Summary) || tr(item.summary) ||
            tr(item.Description) || tr(item.description) ||
            tr(item.Title) || tr(item.title) ||
            tr(item.Name) || tr(item.name) ||
            tr(item.LocationName) || tr(item.locationName) ||
            tr(item.RoadName) || tr(item.roadName) ||
            tr(item.Message) || tr(item.message) ||
            tr(item.Prompt) || tr(item.prompt);

        if (title) return title;

        const id =
            item.Id ?? item.id ??
            item.EventId ?? item.PlaceId ?? item.CrowdInfoId ??
            item.TrafficConditionId ?? item.WeatherForecastId ?? item.SuggestionId;

        return id != null ? `${kind} #${id}` : kind;
    };

    // Flags
    s.flags.showBundleStats ??= false;          // prod
    s.flags.showWeatherPinsInBundles ??= false; // optional
}

function getS(scopeKey = "main") {
    const key = "__OutZenSingleton__" + String(scopeKey || "main");
    globalThis[key] ??= {
        version: "2026.01.14-clean",
        initialized: false,
        bootTs: 0,

        map: null,
        mapContainerId: null,
        mapContainerEl: null,

        cluster: null,
        chart: null,

        markers: new Map(),
        placeMarkers: new Map(),
        placeIndex: new Map(),

        bundleMarkers: new Map(),
        bundleIndex: new Map(),
        bundleLastInput: null,

        detailLayer: null,
        detailMarkers: new Map(),
        hybrid: { enabled: true, threshold: 13, showing: null },
        _hybridBound: false,
        _hybridHandler: null,
        _hybridSwitching: false,

        _bundleRefreshT: 0,
        _weatherById: new Map(),

        consts: {},
        utils: {},
        flags: {},

        calendarLayer: null,
        calendarMarkers: new Map(),

        _hybridFireCount: 0,
        _mapToken: 0,
        _invT: 0,
        _highlightT: 0,
    };

    const s = globalThis[key];
    initState(s);
    return s;
}

// By default, we work on "main"
function S(scopeKey = globalThis.__OutZenActiveScope || "main") {
    return getS(scopeKey);
}
// Debug helper (optional)
globalThis.__OutZenGetS ??= (scopeKey = "main") => getS(scopeKey);

function SActive() {
    return getS(globalThis.__OutZenActiveScope || "main");
}
function debounceOncePerFrame(s, key, fn) {
    if (s[key]) return;
    s[key] = true;
    requestAnimationFrame(() => {
        s[key] = false;
        try { fn(); } catch { }
    });
}


// ------------------------------
// Dev / toggles
// ------------------------------
const SHOW_BUNDLE_STATS = (globalThis.__OZ_ENV === "dev") || false; // default prod = false

// ------------------------------
// Internal helpers
// ------------------------------
async function waitForContainer(mapId, tries = 15) {
    for (let i = 0; i < tries; i++) {
        const el = document.getElementById(mapId);
        if (el) return el;
        await new Promise((r) => requestAnimationFrame(() => r()));
    }
    return null;
}

function ensureLeaflet() {
    const Leaflet = globalThis.L;
    if (!Leaflet) {
        console.error("[OutZen] ❌ Leaflet not loaded (window.L missing).");
        return null;
    }
    return Leaflet;
}

function ensureMapReady(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = getS(k);
    const L = ensureLeaflet();
    if (!L) return null;
    if (!s?.map) return null;
    return { s, L, map: s.map };
}

function resetLeafletDomId(mapId) {
    const L = ensureLeaflet();
    if (!L) return;
    const dom = L.DomUtil.get(mapId);
    if (dom && dom._leaflet_id) {
        try { delete dom._leaflet_id; } catch { dom._leaflet_id = undefined; }
    }
}

function isInBelgium(ll, s) {
    const BE = s.consts.BELGIUM;
    return !!ll &&
        Number.isFinite(ll.lat) && Number.isFinite(ll.lng) &&
        ll.lat >= BE.minLat && ll.lat <= BE.maxLat &&
        ll.lng >= BE.minLng && ll.lng <= BE.maxLng;
}
function arr(payload, key) {
    const v =
        payload?.[key] ??
        payload?.[key[0].toUpperCase() + key.slice(1)] ??    // Places
        payload?.[key.toLowerCase()] ??                      // places
        null;

    return Array.isArray(v) ? v : [];
}
// Parse lat/lng robustly (supports nested Location/position)
function pickLatLng(obj, utils) {
    if (!obj) return null;

    const toNum = (v) => {
        if (v == null) return null;
        const n = typeof v === "string" ? Number(v.replace(",", ".")) : Number(v);
        return Number.isFinite(n) ? n : null;
    };

    const latVal =
        obj.lat ?? obj.LAT ?? obj.Lat ??
        obj.latitude ?? obj.Latitude ??
        obj.Latitude ?? obj.LATITUDE ??                      // ✅ add
        obj?.location?.lat ?? obj?.Location?.Lat ?? obj?.Location?.Latitude ??
        obj?.Location?.latitude ?? obj?.location?.Latitude ?? // ✅ add common nesting
        obj?.coords?.lat ?? obj?.Coords?.Latitude;

    const lngVal =
        obj.lng ?? obj.LNG ?? obj.Lng ??
        obj.lon ?? obj.Lon ?? obj.longitude ?? obj.Longitude ??
        obj.Longitude ?? obj.LONGITUDE ??                    // ✅ add
        obj?.location?.lng ?? obj?.Location?.Lng ?? obj?.Location?.Longitude ??
        obj?.Location?.longitude ?? obj?.location?.Longitude ?? // ✅ add common nesting
        obj?.coords?.lng ?? obj?.Coords?.Longitude;

    const lat = toNum(latVal);
    const lng = toNum(lngVal);

    if (lat == null || lng == null) return null;
    if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;

    return { lat, lng };
}

function addLayerSmart(layer, s) {
    if (!s?.map || !layer) return;

    // ✅ Robust Leaflet layer check
    const looksLikeLeafletLayer =
        typeof layer.addTo === "function" ||
        typeof layer.getLatLng === "function" ||
        typeof layer.getLayers === "function"; // groups

    if (!looksLikeLeafletLayer) {
        console.error("[addLayerSmart] NOT a Leaflet layer", layer);
        return;
    }

    // no-cluster marker -> add directly to map
    if (layer?.options?.__ozNoCluster) {
        try { layer.addTo(s.map); } catch { try { s.map.addLayer(layer); } catch { } }
        return;
    }

    const clusterUsable =
        s.cluster &&
        typeof s.cluster.addLayer === "function" &&
        typeof s.map.hasLayer === "function" &&
        s.map.hasLayer(s.cluster);

    try {
        if (clusterUsable) s.cluster.addLayer(layer);
        else layer.addTo ? layer.addTo(s.map) : s.map.addLayer(layer);
    } catch (e) {
        console.warn("[addLayerSmart] failed", e);
    }
}

function removeLayerSmart(marker, s) {
    if (!s?.map || !marker) return;

    if (marker?.options?.__ozNoCluster) {
        try { s.map.removeLayer(marker); } catch { }
        return;
    }

    try {
        if (s.cluster && typeof s.cluster.removeLayer === "function") s.cluster.removeLayer(marker);
        else s.map.removeLayer(marker);
    } catch { }
}

function destroyChartIfAny(s) {
    if (s.chart && typeof s.chart.destroy === "function") {
        try { s.chart.destroy(); } catch { }
    }
    s.chart = null;
}

function ensureChartCanvas() {
    const canvas = document.getElementById("crowdChart");
    return canvas ?? null;
}

function isDetailsModeNow(s) {
    if (!s.map) return false;
    if (!s.hybrid?.enabled) return false;
    const z = s.map.getZoom();
    return z >= (Number(s.hybrid.threshold) || 13);
}

// ------------------------------
// Public API
// ------------------------------
export function isOutZenReady(scopeKey = "main") {
    const s = getS(pickScopeKey(scopeKey));
    return !!s.initialized && !!s.map;
}

/**
 * Boot Leaflet map
 * options: { mapId, center:[lat,lng], zoom, enableChart, force, enableWeatherLegend, resetMarkers }
 */
// helper local (optionnel)
function bootFail(mapId, scopeKey, reason = "") {
    return { ok: false, token: null, mapId: mapId ?? null, scopeKey: scopeKey ?? null, reason };
}
export async function bootOutZen({
    mapId,
    scopeKey = "main",
    center = [50.85, 4.35],
    zoom = 12,
    enableChart = false,
    force = false,
    enableWeatherLegend = false,
    resetMarkers = false,
    resetAll = false,

    enableHybrid = true,      // ✅
    hybridThreshold = 13,     // ✅
} = {}) {
    globalThis.__OutZenActiveScope = scopeKey;
    const s = getS(scopeKey);

    const host = await waitForContainer(mapId, 30);
    if (!host) return bootFail(mapId, scopeKey, "container-not-found");

    if (s.map && s.mapContainerId && s.mapContainerId !== mapId) {
        disposeOutZen({ mapId: s.mapContainerId, scopeKey });
    }

    resetLeafletDomId(mapId);

    // if already booted and not forced
    if (s.map && s.mapContainerId === mapId && !force) {
        const tok = s._domToken ?? host?.dataset?.ozToken ?? null;
        return { ok: true, token: tok, mapId, scopeKey };
    }
    if (s.map && force) disposeOutZen({ mapId: s.mapContainerId, scopeKey });

    const L = ensureLeaflet();
    if (!L) return bootFail(mapId, scopeKey, "leaflet-missing");

    try { host.replaceChildren(); } catch { host.innerHTML = ""; }

    //host.style.outline = "4px solid magenta";
    //host.style.minHeight = "420px";
    //host.style.height = host.style.height || "520px";

    const map = L.map(host, {
        zoomAnimation: false,
        fadeAnimation: false,
        markerZoomAnimation: false,
        preferCanvas: true,
        zoomControl: true,
        trackResize: false, // ✅ IMPORTANT: avoid _onResize -> invalidateSize -> crash

        minZoom: 5,
        maxZoom: 19,      // ✅ important
        zoomSnap: 1,
        zoomDelta: 1
    }).setView(center, zoom);

    const token = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    host.dataset.ozToken = token;
    s._domToken = token;

    console.log("[bootOutZen] map created", { mapId, scopeKey });

    s.map = map;
    s.mapContainerId = mapId;
    s.mapContainerEl = host;

    s.calendarLayer ??= L.featureGroup();
    s.calendarLayer.addTo(map);

    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "© OpenStreetMap contributors",
        maxZoom: 19,
    }).addTo(map);

    try { map.doubleClickZoom.disable(); } catch { }

    s._mapToken = (s._mapToken || 0) + 1;
    const mapToken = s._mapToken;

    queueMicrotask(() => {
        if (s._mapToken !== mapToken || s.map !== map) return;
        try { map.invalidateSize({ animate: false, debounceMoveend: true }); } catch { }
    });

    if (!s.layerGroup) s.layerGroup = L.layerGroup();
    if (!s.map.hasLayer(s.layerGroup)) s.layerGroup.addTo(s.map);

    if (L.markerClusterGroup) {
        if (!s.cluster) {
            s.cluster = L.markerClusterGroup({
                disableClusteringAtZoom: 16,  // or 17/18 depending on your UX
                spiderfyOnMaxZoom: true,
                showCoverageOnHover: false,
                zoomToBoundsOnClick: true
            });
        }
        if (!s.map.hasLayer(s.cluster)) s.cluster.addTo(s.map);
    }

    if (resetMarkers) s.markers = new Map();
    else s.markers ??= new Map();

    if (enableWeatherLegend) {
        try { createWeatherLegendControl(L).addTo(map); } catch { }
    }

    // ✅ HYBRID only once
    try {
        if (enableHybrid) enableHybridZoom(true, hybridThreshold, scopeKey);
        else s.hybrid.enabled = false;
    } catch (e) {
        console.warn("[bootOutZen] hybrid init failed", e);
    }

    destroyChartIfAny(s);
    if (enableChart && globalThis.Chart) {
        const canvas = ensureChartCanvas();
        if (canvas) {
            const ctx = canvas.getContext("2d");
            s.chart = new Chart(ctx, {
                type: "bar",
                data: { labels: [], datasets: [{ label: "Metric", data: [] }] },
                options: { responsive: true, animation: false },
            });
        }
    }

    if (s.bundleLastInput) {
        try { console.log("[bootOutZen] state", dumpState(scopeKey)); } catch { }
    }
    OutZenInterop.dumpStateSync("home");

    if (resetAll) {
        try { s.cluster?.clearLayers?.(); } catch { }
        try { s.calendarLayer?.clearLayers?.(); } catch { }
        try { s.detailLayer?.clearLayers?.(); } catch { }

        s.markers = new Map();
        s.bundleMarkers = new Map();
        s.bundleIndex = new Map();
        s.detailMarkers = new Map();
        s.calendarMarkers = new Map();
        s._weatherById = new Map();
        s.bundleLastInput = null;
        s.hybrid.showing = null;
    } else {
        if (resetMarkers) s.markers = new Map();
        else s.markers ??= new Map();
    }

    s.initialized = true;
    s.bootTs = Date.now();
    // ✅ IMPORTANT: Returning an object (C# contract)
    console.log("[bootOutZen] returning", { ok: true, token, mapId, scopeKey });
    return { ok: true, token, mapId, scopeKey };
    

}
export function getCurrentMapId(scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    return s.mapContainerId;
}
function requireScopeKey(scopeKey, fnName) {
    if (!scopeKey && globalThis.__OZ_ENV === "dev") {
        console.warn(`[OutZen] ${fnName} called WITHOUT scopeKey -> defaulting to 'main' (safe)`);
    }
    return scopeKey || "main";
}
export function disposeOutZen({ mapId, scopeKey = null, token = null } = {}) {
    const k = pickScopeKeyWrite(scopeKey);
    const s = getS(k);
    const map = s.map;
    if (!map) return true;

    console.warn("[OutZen][disposeOutZen] called", { mapId, current: s.mapContainerId, scopeKey: k });

    const realEl = map.getContainer?.();
    const currentToken = realEl?.dataset?.ozToken;
    const callerToken = token ?? null;

    if (callerToken && currentToken && callerToken !== currentToken) {
        console.warn("[disposeOutZen] token mismatch -> IGNORE dispose", { callerToken, currentToken });
        return true;
    }
    //// ✅ HARD GUARD: never dispose if DOM token mismatch
    //if (realEl?.dataset?.ozToken && s._domToken && realEl.dataset.ozToken !== s._domToken) {
    //    console.warn("[disposeOutZen] token mismatch -> skip DOM cleanup");
    //} 
    // ✅ HARD GUARD mapId
    if (mapId && s.mapContainerId && mapId !== s.mapContainerId) {
        console.warn("[disposeOutZen] mapId mismatch -> IGNORE dispose", { mapId, expected: s.mapContainerId });
        return true;
    }
    
    // 0) Invalidate async ops FIRST
    s._mapToken = (s._mapToken || 0) + 1;
    try { clearTimeout(s._invT); } catch { }
    try { clearTimeout(s._bundleRefreshT); } catch { }
    try { clearTimeout(s._highlightT); } catch { }
    s._invT = 0;
    s._bundleRefreshT = 0;
    s._highlightT = 0;

    // 1) Stop animations (safe)
    try { map.stop?.(); } catch { }

    // 2) Remove layers/plugins cleanly (important for markercluster)
    try {
        if (s._hybridBound && s._hybridHandler) {
            map.off("zoomend", s._hybridHandler);
            map.off("moveend", s._hybridHandler);
        }
    } catch { }
    s._hybridBound = false;
    s._hybridHandler = null;

    // Remove cluster BEFORE killing map events
    try {
        if (s.cluster) {
            try { s.cluster.clearLayers?.(); } catch { }
            try {
                // remove from map in the most "official" way
                if (typeof s.cluster.remove === "function") s.cluster.remove();
                else if (map.hasLayer?.(s.cluster)) map.removeLayer(s.cluster);
            } catch { }
            try { s.cluster.off?.(); } catch { }
            s.cluster = null;
        }
    } catch { }

    try { if (s.calendarLayer && map.hasLayer?.(s.calendarLayer)) map.removeLayer(s.calendarLayer); } catch { }
    try { if (s.detailLayer && map.hasLayer?.(s.detailLayer)) map.removeLayer(s.detailLayer); } catch { }
    try { if (s.layerGroup && map.hasLayer?.(s.layerGroup)) map.removeLayer(s.layerGroup); } catch { }
    try { s.layerGroup?.clearLayers?.(); } catch { }
    s.layerGroup = null;

    // 3) Now detach map events and remove map
    try { map.off?.(); } catch { }
    try { map.remove?.(); } catch { }

    try {
        const el = s.mapContainerEl || (mapId ? document.getElementById(mapId) : null);
        if (el) { try { el.replaceChildren(); } catch { el.innerHTML = ""; } }
    } catch { }

    // 4) cleanup DOM leaflet id (Blazor navigation)
    try {
        const el = s.mapContainerEl || (mapId ? document.getElementById(mapId) : null);
        if (el && el._leaflet_id) {
            try { delete el._leaflet_id; } catch { el._leaflet_id = undefined; }
        }
    } catch { }

    try { map.off?.(); } catch { }
    try { map.remove?.(); } catch { }
    // 4.5) HARD purge of layers + refs (prevents memory leaks between Blazor pages)
    try { s.calendarLayer?.clearLayers?.(); } catch { }
    try { s.detailLayer?.clearLayers?.(); } catch { }
    try { s.cluster?.clearLayers?.(); } catch { }

    try {
        for (const m of s.markers?.values?.() ?? []) { try { m.off?.(); } catch { } }
        for (const m of s.bundleMarkers?.values?.() ?? []) { try { m.off?.(); } catch { } }
        for (const m of s.detailMarkers?.values?.() ?? []) { try { m.off?.(); } catch { } }
        for (const m of s.calendarMarkers?.values?.() ?? []) { try { m.off?.(); } catch { } }
    } catch { }

    // ✅ only empty the actual container, not a random getElementById
    try {
        if (realEl && realEl.isConnected) {
            try { realEl.replaceChildren(); } catch { realEl.innerHTML = ""; }
            // purge leaflet id (if applicable)
            try { if (realEl._leaflet_id) delete realEl._leaflet_id; } catch { realEl._leaflet_id = undefined; }
        }
    } catch { }

    // Clear all state containers
    try { s.markers?.clear?.(); } catch { }
    try { s.placeMarkers?.clear?.(); } catch { }
    try { s.placeIndex?.clear?.(); } catch { }
    try { s.bundleMarkers?.clear?.(); } catch { }
    try { s.bundleIndex?.clear?.(); } catch { }
    try { s.detailMarkers?.clear?.(); } catch { }
    try { s.calendarMarkers?.clear?.(); } catch { }
    try { s._weatherById?.clear?.(); } catch { }

    s.bundleLastInput = null;
    s.hybrid.showing = null;

    // 5) reset state
    s.map = null;
    s.cluster = null;
    s.calendarLayer = null;
    s.detailLayer = null;
    s.initialized = false;
    s.mapContainerId = null;
    s.mapContainerEl = null;

    return true;
}

// ------------------------------
// Weather legend
// ------------------------------
function createWeatherLegendControl(L) {
    const legend = L.control({ position: "bottomright" });
    legend.onAdd = function () {
        const div = L.DomUtil.create("div", "oz-weather-legend");
        div.innerHTML = `
      <div class="oz-weather-legend-title">Météo</div>
      <div class="oz-weather-legend-row"><span class="oz-weather-emoji">☀️</span><span>Sunny</span></div>
      <div class="oz-weather-legend-row"><span class="oz-weather-emoji">☁️</span><span>Cloudy</span></div>
      <div class="oz-weather-legend-row"><span class="oz-weather-emoji">🌧️</span><span>Rain</span></div>
      <div class="oz-weather-legend-row"><span class="oz-weather-emoji">⛈️</span><span>Stormy</span></div>
      <div class="oz-weather-legend-row"><span class="oz-weather-emoji">🌫️</span><span>Foggy</span></div>
      <div class="oz-weather-legend-row"><span class="oz-weather-emoji">💨</span><span>Windy</span></div>
      <div class="oz-weather-legend-row"><span class="oz-weather-emoji">❄️</span><span>Snowy</span></div>
    `;
        return div;
    };
    return legend;
}

// ------------------------------
// Chart update
// ------------------------------
export function setWeatherChart(points, metricType = "Temperature", scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    if (!s.chart || !Array.isArray(points)) return;

    const metric = (metricType || "Temperature").toLowerCase();
    const labels = [];
    const values = [];

    let datasetLabel = "Temperature (°C)";
    if (metric === "humidity") datasetLabel = "Humidity (%)";
    else if (metric === "wind") datasetLabel = "Wind (km/h)";

    for (const p of points) {
        labels.push(p.label ?? "");

        const t = Number(p.temperature ?? p.value ?? 0);
        const h = Number(p.humidity ?? 0);
        const w = Number(p.windSpeed ?? 0);

        let val = t || Number(p.value) || 0;
        if (metric === "humidity") val = h || Number(p.value) || 0;
        if (metric === "wind") val = w || Number(p.value) || 0;

        values.push(val);
    }

    const ds = s.chart.data.datasets[0];
    s.chart.data.labels = labels;
    ds.label = datasetLabel;
    ds.data = values;

    try { s.chart.update(); } catch { }
}

// ------------------------------
// Marker icons
// ------------------------------
function normalizeLevel(level) {
    const n = Number(level) || 0;
    if (n < 0) return 0;
    if (n > 4) return 4;
    return n;
}

function getMarkerClassForLevel(level) {
    switch (normalizeLevel(level)) {
        case 1: return "oz-marker-lvl1";
        case 2: return "oz-marker-lvl2";
        case 3: return "oz-marker-lvl3";
        case 4: return "oz-marker-lvl4";
        default: return "oz-marker-lvl0";
    }
}

function getWeatherEmoji(weatherType) {
    const wt = (weatherType || "").toLowerCase();
    if (wt.includes("clear") || wt.includes("sun")) return "☀️";
    if (wt.includes("cloud")) return "☁️";
    if (wt.includes("rain")) return "🌧️";
    if (wt.includes("storm") || wt.includes("thunder")) return "⛈️";
    if (wt.includes("snow")) return "❄️";
    if (wt.includes("fog") || wt.includes("mist")) return "🌫️";
    return "🌡️";
}

function buildMarkerIcon(L, level, {
    kind = "generic",        // ✅ place | event | traffic | crowd | calendar | antenna | weather | suggestion
    scopeKey = null,         // ✅ "home" | "main" | etc
    isTraffic = false,
    weatherType = null,
    iconOverride = null,
    riseOnHover = false
} = {}) {
    const lvlClass = getMarkerClassForLevel(level);
    const trafficClass = isTraffic ? "oz-marker--traffic" : "";
    const kindClass = `oz-marker--${String(kind).toLowerCase()}`;
    const scopeClass = scopeKey ? `oz-scope--${String(scopeKey).toLowerCase()}` : "";
    const emoji = iconOverride ? iconOverride : (weatherType ? getWeatherEmoji(weatherType) : "");

    return L.divIcon({
        className: `oz-marker ${lvlClass} ${kindClass} ${trafficClass} ${scopeClass}`.trim(),
        html: `<div class="oz-marker-inner">${emoji}</div>`,
        iconSize: [26, 26],
        iconAnchor: [13, 26],
        popupAnchor: [0, -26],
    });
}
function iconSuggestion(L, scopeKey) {
    return buildMarkerIcon(L, 2, { kind: "suggestion", scopeKey, iconOverride: "💡" });
}

function iconTraffic(L, level, scopeKey) {
    return buildMarkerIcon(L, level, { kind: "traffic", scopeKey, isTraffic: true, iconOverride: "🚗" });
}

function iconWeather(L, level, scopeKey, weatherType) {
    return buildMarkerIcon(L, level, { kind: "weather", scopeKey, weatherType });
}
function buildPopupHtml(info, s) {
    const title = info?.title ?? "Unknown";
    const desc = info?.description ?? "";
    return `<div class="outzen-popup">
    <div class="title">${s.utils.escapeHtml(title)}</div>
    <div class="desc">${s.utils.escapeHtml(desc)}</div>
  </div>`;
}

// ------------------------------
// Crowd / generic marker API
// ------------------------------
export function addOrUpdateCrowdMarker(id, lat, lng, level, info, scopeKey = null) {
    const k = pickScopeKeyWrite(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, L } = ready;
    const map = s.map;
    if (!map) return false;

    const latNum = Number(lat);
    const lngNum = Number(lng);
    if (!Number.isFinite(latNum) || !Number.isFinite(lngNum)) return false;

    const key = String(id);
    const existing = s.markers.get(key);

    const popupHtml = buildPopupHtml(info ?? {}, s);
    const icon = buildMarkerIcon(L, level, {
        kind: info?.kind ?? (info?.weatherType ? "weather" : (info?.isTraffic ? "traffic" : "generic")),
        scopeKey: k,
        isTraffic: !!info?.isTraffic,
        weatherType: info?.weatherType ?? info?.WeatherType ?? null,
        iconOverride: info?.icon ?? info?.Icon ?? null,
    });

    if (existing) {
        try { existing.setLatLng([latNum, lngNum]); } catch { }
        try { existing.setPopupContent(popupHtml); } catch { }
        try { existing.setIcon(icon); } catch { }
        return true;
    }

    const marker = L.marker([latNum, lngNum], {
        title: info?.title ?? key,
        riseOnHover: true,
        icon
    }).bindPopup(popupHtml);

    addLayerSmart(marker, s);
    s.markers.set(key, marker);
    return true;
}

export function removeCrowdMarker(id, scopeKey = null) {
    const k = pickScopeKeyWrite(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s } = ready;
    const key = String(id);
    const marker = s.markers.get(key);
    if (!marker) return true;

    removeLayerSmart(marker, s);
    s.markers.delete(key);
    return true;
}

export function clearCrowdMarkers(scopeKey = null) {
    const s = getS(pickScopeKeyWrite(scopeKey));
    if (!s.map) return false;

    try { s.cluster?.clearLayers?.(); } catch { }
    if (!s.cluster) {
        for (const m of s.markers.values()) {
            try { s.map.removeLayer(m); } catch { }
        }
    }
    s.markers.clear();
    return true;
}

export function fitToMarkers(scopeKey = null) {
    const k = pickScopeKeyWrite(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, L, map } = ready;
    if (!s.markers || s.markers.size === 0) return false;

    const latlngs = [];
    for (const m of s.markers.values()) {
        try { latlngs.push(m.getLatLng()); } catch { }
    }
    if (latlngs.length === 0) return false;

    if (latlngs.length === 1) {
        try { map.setView(latlngs[0], 15, { animate: false }); } catch { }
        return true;
    }

    const bounds = L.latLngBounds(latlngs).pad(0.1);
    return safeFitBounds(map, bounds, { padding: [32, 32], maxZoom: 17, animate: false });
}
export function refreshMapSize(scopeKey = null) {
    const s = getS(pickScopeKeyWrite(scopeKey));
    const map = s.map;
    const token = s._mapToken;
    if (!map) return false;

    if (s._resizeQueued) return true;
    s._resizeQueued = true;

    requestAnimationFrame(() => {
        s._resizeQueued = false;
        if (!s.map || s.map !== map || s._mapToken !== token) return;

        const el = map.getContainer?.();
        if (!el || !el.isConnected) return; // ✅

        const r = el.getBoundingClientRect?.();
        if (!r || r.width < 10 || r.height < 10) return;

        if (map._animatingZoom || map._zooming || map._panning) return;

        try { map.invalidateSize({ animate: false, debounceMoveend: true }); } catch { }
    });

    return true;
}
export function debugClusterCount(scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    const layers = s?.cluster?.getLayers?.();
    console.log("[DBG] markers=", s?.markers?.size ?? 0, "clusterLayers=", layers?.length ?? 0);
}

async function safeFitBoundsAsync(map, bounds, opts, isStillValid) {
    try {
        // visible container ?
        if (!isContainerVisible(map)) return false;

        // wait until the layout is stable then fit
        return await fitAfterLayout(map, bounds, opts, isStillValid);
    } catch {
        return false;
    }
}

function safeFitBounds(map, bounds, opts, isStillValid = () => true) {
    // API sync that returns a boolean immediately
    safeFitBoundsAsync(map, bounds, opts, isStillValid)
        .catch(() => { /* swallow */ });

    return true;
}

async function fitAfterLayout(map, bounds, opts, stillValid) {
    const s = SActive();              // ou getS(scopeKey) si tu passes scope
    if (s._fitLock) return false;
    s._fitLock = true;
    try {
        await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
        if (stillValid && !stillValid()) return false;
        try { map.invalidateSize(true); } catch { }
        await new Promise(r => requestAnimationFrame(r));
        if (stillValid && !stillValid()) return false;
        if (!isContainerVisible(map)) return false;
        try { map.fitBounds(bounds, opts); return true; } catch { return false; }
    } finally {
        s._fitLock = false;
    }
}
function pickScopeKeyRead(scopeKey) {
    return scopeKey || globalThis.__OutZenActiveScope || "main";
}
// IMPORTANT: write/destructive ops must NOT depend on active scope
function pickScopeKeyWrite(scopeKey) {
    return scopeKey || globalThis.__OutZenActiveScope || "main";
}

function isContainerVisible(map) {
    const el = map?.getContainer?.();
    if (!el) return false;
    const r = el.getBoundingClientRect();
    return r.width > 10 && r.height > 10;
}

// ------------------------------
// Bundles (group by proximity)
// ------------------------------
function metersToDegLat(m) { return m / 111320; }
function metersToDegLng(m, lat) {
    const cos = Math.cos((lat * Math.PI) / 180);
    return m / (111320 * Math.max(cos, 0.1));
}

function bundleKeyFor(lat, lng, tolMeters) {
    const dLat = metersToDegLat(tolMeters);
    const dLng = metersToDegLng(tolMeters, lat);
    const gy = Math.floor(lat / dLat);
    const gx = Math.floor(lng / dLng);
    return `${gy}:${gx}`;
}

function bundleBreakdown(b) {
    return {
        events: b.events?.length ?? 0,
        places: b.places?.length ?? 0,
        crowds: b.crowds?.length ?? 0,
        traffic: b.traffic?.length ?? 0,
        weather: b.weather?.length ?? 0,
        suggestions: b.suggestions?.length ?? 0,
        gpt: b.gpt?.length ?? 0,
    };
}

function clampLevel14(level) {
    const n = Number(level);
    if (!Number.isFinite(n)) return 1;
    return Math.max(1, Math.min(4, n));
}

function pickCrowdLevel(item) {
    return clampLevel14(item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level);
}
function pickTrafficLevel(item) {
    return clampLevel14(item?.TrafficLevel ?? item?.trafficLevel ?? item?.CongestionLevel ?? item?.level);
}
function pickWeatherLevel(item) {
    const severe = !!(item?.IsSevere ?? item?.isSevere);
    if (severe) return 4;
    return 2;
}

function bundleSeverity(b) {
    let sev = 1;

    if (Array.isArray(b?.crowds)) {
        for (const c of b.crowds) {
            const lv = pickCrowdLevel(c);
            if (lv != null && lv > sev) sev = lv;
        }
    }

    if (Array.isArray(b?.traffic)) {
        for (const t of b.traffic) {
            const lv = pickTrafficLevel(t);
            if (lv != null && lv > sev) sev = lv;
        }
    }

    // ✅ NEW: weather affects severity
    if (Array.isArray(b?.weather)) {
        for (const w of b.weather) {
            const lv = pickWeatherLevel(w);
            if (lv != null && lv > sev) sev = lv;
        }
    }

    return sev;
}

function bundleTotal(b) {
    const d = bundleBreakdown(b);
    return d.events + d.places + d.crowds + d.traffic + d.weather + d.suggestions + d.gpt;
}

function hasOnlyWeather(b) {
    return (b.weather?.length ?? 0) > 0
        && (b.events?.length ?? 0) === 0
        && (b.places?.length ?? 0) === 0
        && (b.crowds?.length ?? 0) === 0
        && (b.traffic?.length ?? 0) === 0
        && (b.suggestions?.length ?? 0) === 0
        && (b.gpt?.length ?? 0) === 0;
}

function weatherEmojiForBundle(b) {
    const w = b.weather?.[0];
    const s = String(w?.WeatherMain ?? w?.WeatherType ?? w?.Summary ?? "").toLowerCase();
    if (s.includes("rain") || s.includes("pluie")) return "🌧️";
    if (s.includes("storm") || s.includes("orage")) return "⛈️";
    if (s.includes("snow") || s.includes("neige")) return "❄️";
    if (s.includes("fog") || s.includes("brou")) return "🌫️";
    if (s.includes("cloud") || s.includes("nuage")) return "☁️";
    return "🌤️";
}

function makeBadgeIcon(totalCount, severity = 1, b = null) {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return null;

    const lvlClass =
        severity === 1 ? "oz-bundle-lvl1" :
            severity === 2 ? "oz-bundle-lvl2" :
                severity === 3 ? "oz-bundle-lvl3" :
                    severity === 4 ? "oz-bundle-lvl4" : "oz-bundle-lvl1";

    const onlyWeather = b && hasOnlyWeather(b);
    const tag = onlyWeather ? `<div class="oz-bundle-tag">${weatherEmojiForBundle(b)}</div>` : "";

    const html = `
      <div class="oz-bundle ${lvlClass} ${onlyWeather ? "oz-bundle-weather" : ""}">
        <div class="oz-badge">${totalCount}</div>
        ${tag}
        <div class="oz-dots"></div>
      </div>
    `.trim();

    return Leaflet.divIcon({
        className: "oz-bundle-icon",
        html,
        iconSize: [34, 34],
        iconAnchor: [17, 17],
        popupAnchor: [0, -12],
    });
}

function weatherLineHtml(w, s) {
    const esc = s.utils.escapeHtml;
    const t = w?.TemperatureC ?? w?.temperatureC;
    const hum = w?.Humidity ?? w?.humidity;
    const wind = w?.WindSpeedKmh ?? w?.windSpeedKmh;
    const rain = w?.RainfallMm ?? w?.rainfallMm;
    const sum = w?.Summary ?? w?.summary ?? "Weather";
    const desc = w?.Description ?? w?.description ?? "";
    const main = w?.WeatherMain ?? w?.weatherMain ?? "";
    const sev = !!(w?.IsSevere ?? w?.isSevere);

    const parts = [];
    if (t != null) parts.push(`${esc(t)}°C`);
    if (hum != null) parts.push(`Hum ${esc(hum)}%`);
    if (wind != null) parts.push(`Vent ${esc(wind)} km/h`);
    if (rain != null) parts.push(`Pluie ${esc(rain)} mm`);

    const badge = sev ? `<span class="oz-pill oz-pill-sev">Severe</span>` : "";
    const head = `<div class="oz-wx-head">${esc(sum)} ${badge}</div>`;
    const sub = (main || desc) ? `<div class="oz-wx-sub">${esc([main, desc].filter(Boolean).join(" • "))}</div>` : "";
    const metrics = parts.length ? `<div class="oz-wx-metrics">${esc(parts.join(" • "))}</div>` : "";

    return `
    <div class="oz-wx-row">
      ${head}
      ${sub}
      ${metrics}
    </div>
  `.trim();
}

function bundlePopupHtml(b, s) {
    const esc = s.utils.escapeHtml;
    const totals = bundleBreakdown(b);
    const total = bundleTotal(b);

    return `
    <div class="oz-bundle-popup">
      <div class="oz-bundle-head">
        <div class="oz-bundle-title">Summary area</div>
        <div class="oz-bundle-sub">${total} element(s)</div>
        <div class="oz-bundle-coords">${Number(b.lat).toFixed(5)}, ${Number(b.lng).toFixed(5)}</div>
      </div>

      <div class="oz-sec">
        <div class="oz-sec-title">Counts</div>
        <div class="oz-sec-body">
          <div class="oz-row"><span class="oz-k">Events</span><span class="oz-v">${esc(totals.events)}</span></div>
          <div class="oz-row"><span class="oz-k">Places</span><span class="oz-v">${esc(totals.places)}</span></div>
          <div class="oz-row"><span class="oz-k">Crowds</span><span class="oz-v">${esc(totals.crowds)}</span></div>
          <div class="oz-row"><span class="oz-k">Traffic</span><span class="oz-v">${esc(totals.traffic)}</span></div>
          <div class="oz-row"><span class="oz-k">Weather</span><span class="oz-v">${esc(totals.weather)}</span></div>
          <div class="oz-row"><span class="oz-k">Suggestions</span><span class="oz-v">${esc(totals.suggestions)}</span></div>
          <div class="oz-row"><span class="oz-k">GPT</span><span class="oz-v">${esc(totals.gpt)}</span></div>
        </div>
      </div>
    </div>
  `.trim();
}
function resolveLatLngForItem(kindLower, item, indexes, s) {
    let ll = pickLatLng(item, s.utils);
    if (ll) return ll;

    const placeId = item?.PlaceId ?? item?.placeId;
    if (placeId != null) {
        const p = indexes.placeById.get(placeId);
        ll = pickLatLng(p, s.utils);
        if (ll) return ll;
    }

    const eventId = item?.EventId ?? item?.eventId;
    if (eventId != null) {
        const e = indexes.eventById.get(eventId);
        ll = pickLatLng(e, s.utils);
        if (ll) return ll;
    }

    const crowdId = item?.CrowdInfoId ?? item?.crowdInfoId;
    if (crowdId != null) {
        const c = indexes.crowdById.get(crowdId);
        ll = pickLatLng(c, s.utils);
        if (ll) return ll;
    }

    const wfId = item?.WeatherForecastId ?? item?.weatherForecastId;
    if (wfId != null) {
        const w = indexes.weatherById.get(wfId);
        ll = pickLatLng(w, s.utils);
        if (ll) return ll;
    }

    const tcId = item?.TrafficConditionId ?? item?.trafficConditionId;
    if (tcId != null) {
        const t = indexes.trafficById.get(tcId);
        ll = pickLatLng(t, s.utils);
        if (ll) return ll;
    }
    return null;
}

function countValidResolvable(arr, kindLower, indexes, s) {
    if (!Array.isArray(arr) || arr.length === 0) return 0;

    let ok = 0;
    for (const it of arr) {
        try {
            const ll = resolveLatLngForItem(kindLower, it, indexes, s);
            if (ll) ok++;
        } catch { }
    }
    return ok;
}
function normalizePayload(payload) {
    const p = payload || {};
    return {
        places: p.places ?? p.Places ?? [],
        events: p.events ?? p.Events ?? [],
        crowds: p.crowds ?? p.Crowds ?? [],
        traffic: p.traffic ?? p.Traffic ?? [],
        weather: p.weather ?? p.Weather ?? [],
        suggestions: p.suggestions ?? p.Suggestions ?? [],
        gpt: p.gpt ?? p.Gpt ?? [],
    };
}
function computeBundles(payload, tolMeters, s) {
    const buckets = new Map();

    const placesArr = arr(payload, "places");
    const eventsArr = arr(payload, "events");
    const crowdsArr = arr(payload, "crowds");
    const trafficArr = arr(payload, "traffic");
    const weatherArr = arr(payload, "weather");
    const suggestionsArr = arr(payload, "suggestions");
    const gptArr = arr(payload, "gpt");

    const indexes = {
        placeById: new Map(placesArr.map(p => [p?.Id ?? p?.id, p]).filter(([id]) => id != null)),
        eventById: new Map(eventsArr.map(e => [e?.Id ?? e?.id, e]).filter(([id]) => id != null)),
        crowdById: new Map(crowdsArr.map(c => [c?.Id ?? c?.id, c]).filter(([id]) => id != null)),
        trafficById: new Map(trafficArr.map(t => [t?.Id ?? t?.id, t]).filter(([id]) => id != null)),
        weatherById: new Map(weatherArr.map(w => [w?.Id ?? w?.id, w]).filter(([id]) => id != null)),
    };

    let missingTotal = 0;

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;

        const kindLower = String(kind).toLowerCase();
        let missing = 0;
        console.log("[DBG] push kind=", kind, "len=", arr?.length, "tol=", tolMeters);

        for (const item of arr) {
            try {
                const ll = resolveLatLngForItem(kindLower, item, indexes, s);
                if (!ll) { missing++; missingTotal++; continue; }

                /*if (!isInBelgium(ll, s)) continue;*/

                const key = bundleKeyFor(ll.lat, ll.lng, tolMeters);

                let b = buckets.get(key);
                if (!b) {
                    b = { key, lat: ll.lat, lng: ll.lng, events: [], places: [], crowds: [], traffic: [], weather: [], suggestions: [], gpt: [] };
                    buckets.set(key, b);
                }

                const target = b[kind];
                if (!Array.isArray(target)) {
                    if (missingTotal <= 3) console.warn("[Bundles] unknown kind", { kind, keys: Object.keys(b) });
                    continue;
                }

                target.push(item);
            } catch (err) {
                if (missingTotal <= 3) {
                    console.error("[Bundles] push failed", {
                        kindLower,
                        id: item?.Id ?? item?.id,
                        placeId: item?.PlaceId ?? item?.placeId,
                        eventId: item?.EventId ?? item?.eventId,
                        lat: item?.Latitude ?? item?.latitude ?? item?.Lat ?? item?.lat,
                        lng: item?.Longitude ?? item?.longitude ?? item?.Lng ?? item?.lng,
                        err
                    });
                }
                continue;
            }
        }

        if (missing && kindLower === "suggestions") {
            console.warn(`[Bundles] ${missing} suggestion(s) without coords/PlaceId/EventId`);
        }

        const sampleSug = suggestionsArr[0];
        console.log("[DBG] suggestion fields", {
            Latitude: sampleSug?.Latitude, Longitude: sampleSug?.Longitude,
            lat: sampleSug?.lat, lng: sampleSug?.lng,
            Lat: sampleSug?.Lat, Lng: sampleSug?.Lng,
            PlaceId: sampleSug?.PlaceId, EventId: sampleSug?.EventId
        });
    };

    push(placesArr, "places");
    push(eventsArr, "events");
    push(crowdsArr, "crowds");
    push(trafficArr, "traffic");
    push(weatherArr, "weather");
    push(suggestionsArr, "suggestions");
    push(gptArr, "gpt");

    const stats = {
        places: countValidResolvable(placesArr, "places", indexes, s),
        events: countValidResolvable(eventsArr, "events", indexes, s),
        crowds: countValidResolvable(crowdsArr, "crowds", indexes, s),
        traffic: countValidResolvable(trafficArr, "traffic", indexes, s),
        weather: countValidResolvable(weatherArr, "weather", indexes, s),
        suggestions: countValidResolvable(suggestionsArr, "suggestions", indexes, s),
        gpt: countValidResolvable(gptArr, "gpt", indexes, s),
        missingTotal
    };

    console.log("[Bundles] valid points", stats);

    const totalUseful =
        stats.places + stats.events +
        stats.crowds + stats.weather +
        stats.traffic + stats.suggestions + stats.gpt;

    if (totalUseful === 0) return new Map();


    const sampleSug = suggestionsArr[0];
    console.log("[DBG] suggestion sample", sampleSug);
    console.log("[DBG] pickLatLng(suggestion) =", pickLatLng(sampleSug, s.utils));
    console.log("[DBG] resolveLatLngForItem(suggestions) =", resolveLatLngForItem("suggestions", sampleSug, indexes, s));

    console.log("[DBG] buckets keys =", Array.from(buckets.keys()).slice(0, 5));
    console.log("[DBG] first bucket =", buckets.values().next().value);

    return buckets;
}

export function updateBundleMarker(b, scopeKey = null) {
    console.log("[DBG] updateBundleMarker called", { key: b?.key, lat: b?.lat, lng: b?.lng });
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return;

    const { s, L } = ready;
    const total = bundleTotal(b);
    const sev = bundleSeverity(b);
    const existing = s.bundleMarkers.get(b.key);
    const detailsMode = isDetailsModeNow(s);

    // ✅ Correct: if nothing to show => remove existing bundle marker
    if (total <= 0) {
        if (existing) {
            removeLayerSmart(existing, s);
            s.bundleMarkers.delete(b.key);
            s.bundleIndex.delete(b.key);
        }
        return;
    }


    /*const icon = makeBadgeIcon(total, sev, b);*/
    const icon = undefined;

    const popup = bundlePopupHtml(b, s);

    if (!existing) {
        const m = L.marker([b.lat, b.lng], {
            icon,
            /*pane: "tooltipPane", */ // quick test, above
            /*pane: "markerPane",*/
            title: `Area (${total})`,
            riseOnHover: true,
            __ozNoCluster: true,
        });
        console.log("[DBG] bundle marker created", { m, isMarker: !!m?.getLatLng, opts: m?.options });

        m.bindPopup(popup, { maxWidth: 420, closeButton: true, autoPan: true });
        /*m.bindTooltip(`Area • ${total} elements`, { direction: "top", sticky: true, opacity: 0.95 });*/

        if (!m || typeof m.getLatLng !== "function") console.error("[DBG] bundle marker invalid", m);
        if (!detailsMode) addLayerSmart(m, s);

        debounceOncePerFrame(s, "_hybridTick", () => {
            console.log("[DBG] detailsMode=", detailsMode, "hybrid=", s.hybrid);
        });

        s.bundleMarkers.set(b.key, m);
        s.bundleIndex.set(b.key, b);
        return;
    }

    try { existing.setLatLng([b.lat, b.lng]); } catch { }
    try { if (icon) existing.setIcon(icon); } catch { }
    try { if (existing.getPopup()) existing.setPopupContent(popup); } catch { }

    try {
        if (detailsMode) {
            if (s.map.hasLayer(existing)) s.map.removeLayer(existing);
        } else {
            if (!s.map.hasLayer(existing)) s.map.addLayer(existing);
        }
    } catch { }

    s.bundleIndex.set(b.key, b);
    console.log("[DBG] updateBundleMarker", { key: b.key, total, sev, detailsMode, hasExisting: !!existing });
}

export function addOrUpdateBundleMarkers(payload, toleranceMeters = 80, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s } = ready;

    // 1) tolerance (defined BEFORE use)
    const tol = Number(toleranceMeters);
    const tolMeters = (Number.isFinite(tol) && tol > 0) ? tol : 80;

    // 2) normalize only once
    const norm = normalizePayload(payload);
    s.bundleLastInput = norm;

    console.log("[Bundles] input keys", Object.keys(payload || {}));
    console.log("[Bundles] counts", {
        events: norm.events.length,
        places: norm.places.length,
        crowds: norm.crowds.length,
        suggestions: norm.suggestions.length,
        traffic: norm.traffic.length,
        weather: norm.weather.length,
        gpt: norm.gpt.length,
    });

    console.log("[Bundles] map has cluster?", !!s.cluster, "map?", !!s.map, "zoom", s.map?.getZoom?.());

    // 3) compute bundles (a single statement)
    const bundles = computeBundles(norm, tolMeters, s);
    if (!bundles || bundles.size === 0) {
        // Option: purge old bundles if nothing else is available
        for (const oldKey of Array.from(s.bundleMarkers.keys())) {
            const marker = s.bundleMarkers.get(oldKey);
            removeLayerSmart(marker, s);
            s.bundleMarkers.delete(oldKey);
            s.bundleIndex.delete(oldKey);
        }
        try { refreshHybridVisibility(k); } catch { }
        return true;
    }

    console.log("[Bundles] bundles.size =", bundles?.size ?? null);

    // 4) remove old bundles
    for (const oldKey of Array.from(s.bundleMarkers.keys())) {
        if (!bundles.has(oldKey)) {
            const marker = s.bundleMarkers.get(oldKey);
            removeLayerSmart(marker, s);
            s.bundleMarkers.delete(oldKey);
            s.bundleIndex.delete(oldKey);
        }
    }

    // 5) upsert bundles
    for (const b of bundles.values()) updateBundleMarker(b, k);

    // 6) hybrid refresh + cluster refresh (if available)
    try { refreshHybridVisibility(k); } catch { }

    if (s.cluster && typeof s.cluster.refreshClusters === "function") {
        try { s.cluster.refreshClusters(); } catch { }
    }

    // 7) Optional: Weather pins in bundle mode
    if (s.flags.showWeatherPinsInBundles && s.hybrid?.showing !== "details") {
        addOrUpdateWeatherMarkers(norm.weather ?? [], k);
    }

    try { refreshHybridVisibility(k); } catch { }

    return true;
}
// ------------------------------
// Weather markers (standalone)
// ------------------------------
export function addOrUpdateWeatherMarkers(items, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s } = ready;
    if (!Array.isArray(items)) return false;

    for (const w of items) {
        const ll = pickLatLng(w, s.utils);
        if (!ll || !isInBelgium(ll, s)) continue;

        const wid = (w?.Id ?? w?.id);
        /*const id = "wf:" + String(w.Id ?? w.id ?? "");*/
        if (wid == null) continue;
        const id = `wf:${wid}`;

        console.log("[WeatherMarkers] adding", { id, lat: ll.lat, lng: ll.lng });

        const level = (w.IsSevere || w.isSevere) ? 4 : 2;

        addOrUpdateCrowdMarker(id, ll.lat, ll.lng, level, {
            title: w.Summary ?? w.summary ?? "Weather",
            description: [
                `Temp: ${w.TemperatureC ?? w.temperatureC ?? "?"}°C`,
                `Hum: ${w.Humidity ?? w.humidity ?? "?"}%`,
                `Wind: ${w.WindSpeedKmh ?? w.windSpeedKmh ?? "?"} km/h`,
                `Rain: ${w.RainfallMm ?? w.rainfallMm ?? "?"} mm`,
                (w.Description ?? w.description) ? `Desc: ${w.Description ?? w.description}` : null
            ].filter(Boolean).join(" • "),
            weatherType: (w.WeatherType ?? w.weatherType ?? "").toString(),
            isTraffic: false
        }, k);
    }
    return true;
}

export function addOrUpdateSuggestionMarkersFromPayload(payload, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;
    const { s } = ready;

    const placesArr = Array.isArray(payload?.places) ? payload.places : [];
    const eventsArr = Array.isArray(payload?.events) ? payload.events : [];
    const suggestionsArr = Array.isArray(payload?.suggestions) ? payload.suggestions : [];

    const indexes = {
        placeById: new Map(placesArr.map(p => [p?.Id ?? p?.id, p]).filter(([id]) => id != null)),
        eventById: new Map(eventsArr.map(e => [e?.Id ?? e?.id, e]).filter(([id]) => id != null)),
        crowdById: new Map(),
        trafficById: new Map(),
        weatherById: new Map(),
    };

    for (const it of suggestionsArr) {
        const ll = resolveLatLngForItem("suggestions", it, indexes, s);
        if (!ll) continue;

        addOrUpdateCrowdMarker(
            `sg:${it.Id ?? it.id}`,
            ll.lat, ll.lng,
            2,
            { title: it.Title ?? it.title ?? "Suggestion", description: it.SuggestedAlternatives ?? it.suggestedAlternatives ?? "", icon: "💡", kind: "suggestion" },
            k
        );
    }
    return true;
}
export function scheduleBundleRefresh(delayMs = 150, tolMeters = 80, scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    clearTimeout(s._bundleRefreshT);
    s._bundleRefreshT = setTimeout(() => {
        try {
            if (s.bundleLastInput) addOrUpdateBundleMarkers(s.bundleLastInput, tolMeters, pickScopeKey(scopeKey));
        } catch { }
    }, delayMs);
}

// ------------------------------
// Detail markers + Hybrid zoom
// ------------------------------
function ensureDetailLayer(s, L) {
    if (!s.detailLayer) {
        s.detailLayer = L.layerGroup();
        s.map.addLayer(s.detailLayer);
    }
    return s.detailLayer;
}

function clearDetailMarkers(s) {
    try { s.detailLayer?.clearLayers?.(); } catch { }
    try { s.detailMarkers?.clear?.(); } catch { }
}

function makeDetailKey(kind, item, s) {
    const k = String(kind).toLowerCase();

    const id =
        item?.Id ?? item?.id ??
        item?.ForecastId ?? item?.forecastId ??
        item?.WeatherForecastId ?? item?.weatherForecastId;

    if (id != null) return `${k}:${id}`;

    const placeId = item?.PlaceId ?? item?.placeId;
    const dt = item?.DateWeather ?? item?.dateWeather ?? item?.DateUtc ?? item?.dateUtc;
    if (placeId != null && dt) return `${k}:p${placeId}:${String(dt)}`;

    const ll = pickLatLng(item, s.utils);
    if (ll) return `${k}:${ll.lat.toFixed(5)},${ll.lng.toFixed(5)}`;

    return `${k}:${JSON.stringify(item).slice(0, 64)}`;
}

function addDetailMarker(kind, item, s, L) {
    try {
        const layer = ensureDetailLayer(s, L);
        if (!layer) return;

        let ll = null;

        if (String(kind).toLowerCase() === "weather") {
            const placeId = item?.PlaceId ?? item?.placeId;
            const place = placeId != null ? (s.bundleLastInput?.places ?? []).find(p => (p?.Id ?? p?.id) == placeId) : null;
            ll = pickLatLng(item, s.utils) ?? pickLatLng(place, s.utils);
        } else {
            ll = pickLatLng(item, s.utils);
        }

        if (!ll || !isInBelgium(ll, s)) return;

        const key = makeDetailKey(kind, item, s);
        if (s.detailMarkers.has(key)) return;

        const title = s.utils.titleOf(kind, item);

        // ✅ Weather with icon
        if (String(kind).toLowerCase() === "weather") {
            const severe = !!(item?.IsSevere ?? item?.isSevere);
            const level = severe ? 4 : 2;
            const icon = buildMarkerIcon(L, level, {
                weatherType: (item?.WeatherType ?? item?.weatherType ?? "").toString(),
                isTraffic: false
            });

            const m = L.marker([ll.lat, ll.lng], {
                icon,
                title: `Weather: ${title}`,
                riseOnHover: true,
                pane: "markerPane"
            });

            /*m.bindTooltip(`Weather: ${title}`, { sticky: true, opacity: 0.95 });*/
            m.bindPopup(buildPopupHtml({
                title: item?.Summary ?? item?.summary ?? title,
                description: `Temp: ${item?.TemperatureC ?? item?.temperatureC ?? "?"}°C • Vent: ${item?.WindSpeedKmh ?? item?.windSpeedKmh ?? "?"} km/h`
            }, s));

            console.log("[DETAILS] add weather", key);
            layer.addLayer(m);
            s.detailMarkers.set(key, m);
            return;
        }

        // other kinds: keep circleMarker (or convert later)
        const m = L.circleMarker([ll.lat, ll.lng], { radius: 7, className: "oz-detail-marker" });
        /*m.bindTooltip(`${kind}: ${title}`, { sticky: true, opacity: 0.95 });*/
        m.bindPopup(`<div class="oz-popup"><b>${s.utils.escapeHtml(kind)}</b><br>${s.utils.escapeHtml(title)}</div>`);

        layer.addLayer(m);
        s.detailMarkers.set(key, m);
    } catch (e) {
        console.error("[DETAILS] addDetailMarker failed", { kind, item, e });
    }
}

export function addOrUpdateDetailMarkers(payload, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, L } = ready;

    clearDetailMarkers(s);

    let counts = { Event: 0, Place: 0, Crowd: 0, Traffic: 0, Weather: 0, Suggestion: 0, GPT: 0 };

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;
        for (const x of arr) {
            counts[kind]++;
            addDetailMarker(kind, x, s, L);
        }
    };

    push(payload?.events, "Event");
    push(payload?.places, "Place");
    push(payload?.crowds, "Crowd");
    push(payload?.traffic, "Traffic");
    push(payload?.weather, "Weather");
    push(payload?.suggestions, "Suggestion");
    push(payload?.gpt, "GPT");

    if (Array.isArray(payload?.weather)) {
        for (const w of payload.weather) {
            addDetailMarker("weather", w, s, L); // force lowercase
        }
    }

    console.log("[DETAILS] counts", counts, "detailMarkers.size", s.detailMarkers.size);
    console.log("[DETAILS] weather sample", payload?.weather?.[0]);

    return true;
}

export function enableHybridZoom(enabled = true, threshold = 13, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = getS(k);

    s.hybrid.enabled = !!enabled;
    s.hybrid.threshold = (threshold ?? s.hybrid.threshold ?? 13);

    if (!s.map) return false;

    if (!s._hybridBound) {
        s._hybridBound = true;
        const h = throttleOZ(() => refreshHybridVisibility(k), 150);
        s._hybridHandler = h;

        s.map.on("zoomend", h);   // ✅ keep only zoomend
        // s.map.on("moveend", h); // ❌ remove movesend
    }

    refreshHybridVisibility(k);
    return true;
}

function throttleOZ(fn, ms) {
    let lock = false;
    let lastArgs = null;

    const later = () => {
        lock = false;
        if (lastArgs) {
            const args = lastArgs;
            lastArgs = null;
            wrapper(...args);
        }
    };

    const wrapper = (...args) => {
        if (lock) { lastArgs = args; return; }
        fn(...args);
        lock = true;
        setTimeout(later, ms);
    };

    return wrapper;
}

function runWhenMapReallyIdle(map, fn) {
    const tick = () => {
        if (!map) return;
        // Leaflet internal flags - be conservative
        if (map._animatingZoom || map._zooming || map._panning || map._moving) {
            requestAnimationFrame(tick);
            return;
        }
        // wait one extra frame AFTER it looks idle
        requestAnimationFrame(() => {
            if (!map) return;
            if (map._animatingZoom || map._zooming || map._panning || map._moving) return;
            fn();
        });
    };
    requestAnimationFrame(tick);
}
function purgeClusterWeatherMarkers(s) {
    try {
        const toDelete = [];
        for (const [k, m] of s.markers.entries()) {
            if (!String(k).startsWith("wf:")) continue;
            try { removeLayerSmart(m, s); } catch { }
            toDelete.push(k);
        }
        for (const k of toDelete) s.markers.delete(k);
    } catch (e) {
        console.warn("[Hybrid] purgeClusterWeatherMarkers failed", e);
    }
}
function computeTotalCountFromBundles(input) {
    if (!input || typeof input !== "object") return null;

    const keys = ["places", "events", "crowds", "traffic", "weather", "suggestions", "gpt"];
    let total = 0;

    for (const k of keys) {
        const arr = input[k];
        if (Array.isArray(arr)) total += arr.length;
    }
    return total;
}
function refreshHybridVisibility(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = getS(k);
    const map = s.map;
    const token = s._mapToken;

    if (!map || !s.hybrid?.enabled) return;

    if (s._hybridSwitching) return; // ✅ block reentrancy

    setTimeout(() => {
        if (!s.map || s.map !== map || s._mapToken !== token) return;

        runWhenMapReallyIdle(map, () => {
            if (!s.map || s.map !== map || s._mapToken !== token) return;
            if (s._hybridSwitching) return;

            s._hybridSwitching = true;
            try {
                const z = map.getZoom();
                const wantDetails = z >= s.hybrid.threshold;

                if (wantDetails && s.hybrid.showing !== "details") {
                    switchToDetails(s, map, k);
                } else if (!wantDetails && s.hybrid.showing !== "bundles") {
                    switchToBundles(s, map, k);
                }
            } finally {
                s._hybridSwitching = false;
            }
        });
    }, 0);
}
function switchToDetails(s, map, scopeKey) {
    const L = ensureLeaflet();
    if (!L) return;

    // 1) Hide the bundles (they are directly on the map)
    for (const m of s.bundleMarkers.values()) {
        try { if (map.hasLayer(m)) map.removeLayer(m); } catch { }
    }

    // 2) DO NOT empty the cluster: we want to keep the markers
    //    (disableClusteringAtZoom manages the "detail" at high zoom levels)

    // 3) Add an optional details layer (e.g., circles + enhanced tooltips)
    ensureDetailLayer(s, L);
    if (s.bundleLastInput) addOrUpdateDetailMarkers(s.bundleLastInput, scopeKey);

    s.hybrid.showing = "details";
}

function switchToBundles(s, map, scopeKey) {
    // 1) remove details (optional)
    clearDetailMarkers(s);

    // 2) show bundles
    for (const m of s.bundleMarkers.values()) {
        try { if (!map.hasLayer(m)) map.addLayer(m); } catch { }
    }

    s.hybrid.showing = "bundles";
}
export function activateHybridAndZoom(threshold = 13, zoom = 13, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, map } = ready;

    try { enableHybridZoom(true, threshold, k); } catch { }

    // postpone zoom to avoid zoom transition during layer churn
    setTimeout(() => {
        if (s.map !== map) return;
        try {
            map.invalidateSize({ animate: false, debounceMoveend: true }); // ✅
        } catch { }
        try { map.setZoom(zoom, { animate: false }); } catch { }
    }, 0);

    return true;
}

export function forceDetailsMode(scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    if (!s.map) return false;
    s.hybrid.enabled = true;

    // Option 1: Lower the threshold just to force details
    // S.hybrid.threshold = 1;

    // Option 2: Just zoom in above the current threshold
    const th = Number(s.hybrid.threshold) || 13;
    if (s.map.getZoom() < th) s.map.setZoom(th);

    try { refreshHybridVisibility(pickScopeKey(scopeKey)); } catch { }
    return true;
}

export function refreshHybridNow(scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    if (!s.map || !s.hybrid?.enabled) return false;

    const z = s.map.getZoom();
    const next = z >= s.hybrid.threshold ? "details" : "bundles";

    if (s.hybrid.showing !== next) {
        s.hybrid.showing = next;
        console.log("[Hybrid] showing ->", next, "zoom=", z, "threshold=", s.hybrid.threshold);
    }

    // apply visually only if necessary
    if (next === "details") {
        // show details / hide bundles
        // ...
    } else {
        // show bundles / hide details
        // ...
    }

    return true;
}

export async function fitToBundles(padding = 30, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, L, map } = ready;
    const token = s._mapToken;

    if (!map || typeof map.fitBounds !== "function") return false;

    const ms = Array.from(s.bundleMarkers?.values?.() ?? []);
    const latlngs = [];

    for (const m of ms) {
        try {
            const ll = m?.getLatLng?.();
            if (ll) latlngs.push(ll);
        } catch { }
    }
    if (!latlngs.length) return false;

    // bounds
    const b = L.latLngBounds(latlngs).pad(0.10);

    // Race-safe recheck BEFORE fit
    if (s._mapToken !== token || s.map !== map) return false;

    const opts = { padding: [padding, padding], maxZoom: 16, animate: false };

    // If container not visible yet, skip (or retry via caller)
    if (!isContainerVisible(map)) return false;

    // Do fit after layout stabilization
    return await fitAfterLayout(map, b, opts, () => (s._mapToken === token && s.map === map));
}

export function debugDumpMarkers(scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    console.log("[DBG] markers keys =", Array.from(s.markers.keys()));
    console.log("[DBG] bundle keys =", Array.from(s.bundleMarkers.keys()));
    console.log("[DBG] map initialized =", !!s.map, "cluster =", !!s.cluster);
}

export function fitToDetails(padding = 30, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, L, map } = ready;

    const latlngs = [];
    for (const m of s.detailMarkers.values()) {
        try { latlngs.push(m.getLatLng()); } catch { }
    }
    if (latlngs.length === 0) return false;

    const bounds = L.latLngBounds(latlngs).pad(0.1);
    return safeFitBounds(map, bounds, { padding: [padding, padding], maxZoom: 17, animate: false });
}
export function elementExists(id, scopeKey = null) {
    // scopeKey kept for signature symmetry
    return !!document.getElementById(id);
}

// --- Aliases for backward compat (Blazor pages calling old names) ---
export function checkElementExists(id) {   // C# uses checkElementExists(...)
    return elementExists(id);
}

export function upsertPlaceMarker(id, lat, lng, level, info, scopeKey = null) {
    // Old PlaceView call: maps to the generic marker API
    const s = getS(pickScopeKey(scopeKey));
    if (!s.map) return false;
    return addOrUpdateCrowdMarker(id, lat, lng, level, info, pickScopeKey(scopeKey));
}

export function fitToPlaceMarkers(scopeKey = null) {
    // Old call: just fit to current markers
    return fitToMarkers(pickScopeKey(scopeKey));
}

export function clearPlaceHighlight() {
    // no-op for now
    return true;
}

// Guard: define once (hot reload / accidental paste protection)
function ensureHighlightImpl(s) {
    if (s.utils.highlightPlaceMarker) return;

    s.utils.highlightPlaceMarker = function highlightPlaceMarker(placeId, opts = {}) {
        const s = SActive();
        const L = ensureLeaflet();
        if (!L || !s.map) return false;

        const id = String(placeId ?? "");
        if (!id) return false;

        const {
            openPopup = true,
            panTo = true,
            zoomTo = null,
            dimOthers = false,
            pulseMs = 1400,
        } = opts || {};

        let marker =
            (s.placeMarkers && (s.placeMarkers.get(id) || s.placeMarkers.get(`pl:${id}`))) ||
            (s.markers && (s.markers.get(`pl:${id}`) || s.markers.get(id)));

        if (!marker) {
            console.warn("[OutZen][highlightPlaceMarker] marker not found", { placeId });
            return false;
        }

        // Optional dim
        let restoreDim = null;
        if (dimOthers) {
            const changed = [];
            const all = [];
            try { if (s.markers) for (const m of s.markers.values()) all.push(m); } catch { }
            try { if (s.placeMarkers) for (const m of s.placeMarkers.values()) all.push(m); } catch { }

            for (const m of all) {
                if (!m || m === marker) continue;
                if (typeof m.setOpacity === "function") {
                    const old = (m.options && typeof m.options.opacity === "number") ? m.options.opacity : 1;
                    changed.push([m, old]);
                    try { m.setOpacity(0.25); } catch { }
                }
            }
            restoreDim = () => { for (const [m, old] of changed) { try { m.setOpacity(old); } catch { } } };
        }

        const iconEl = marker?._icon ?? null;
        const cls = "oz-marker-highlight";
        try { iconEl?.classList?.add(cls); } catch { }

        // pan/zoom
        try {
            const ll = marker.getLatLng?.();
            if (ll && panTo) {
                if (typeof zoomTo === "number") s.map.setView(ll, zoomTo, { animate: true });
                else s.map.panTo(ll, { animate: true });
            }
        } catch { }

        // open popup, clustered-safe
        const open = () => { try { marker.openPopup?.(); } catch { } };
        try {
            if (s.cluster && typeof s.cluster.zoomToShowLayer === "function") {
                s.cluster.zoomToShowLayer(marker, open);
            } else open();
        } catch { open(); }

        clearTimeout(s._highlightT);
        s._highlightT = setTimeout(() => {
            try { iconEl?.classList?.remove(cls); } catch { }
            try { restoreDim?.(); } catch { }
        }, Math.max(250, Number(pulseMs) || 1400));

        return true;
    };
}

// ✅ Single export that points to the guarded implementation
export function highlightPlaceMarker(placeId, opts = {}, scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    ensureHighlightImpl(s);
    return s.utils.highlightPlaceMarker(placeId, opts);
}

// ------------------------------
// Calendar markers
// ------------------------------
function calendarDivIcon(level, meta = {}) {
    const L = ensureLeaflet();
    if (!L) return null;

    const lv = clampLevel14(level);
    const emoji = meta?.icon ?? "🥁🎉";

    return L.divIcon({
        className: `oz-cal-icon oz-cal-lvl${lv} ${lv === 4 ? "oz-cal-critical" : ""}`,
        html: `<div class="oz-cal-pin"><span class="oz-cal-emoji">${emoji}</span></div>
           <div class="oz-cal-shadow"></div>`,
        iconSize: [28, 28],
        iconAnchor: [14, 14],
        popupAnchor: [0, -12],
    });
}

export function addOrUpdateCrowdCalendarMarker(id, lat, lng, level, meta, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, L } = ready;

    const latNum = Number(lat), lngNum = Number(lng);
    if (!Number.isFinite(latNum) || !Number.isFinite(lngNum)) return false;

    s.calendarLayer ??= L.featureGroup().addTo(s.map);
    s.calendarMarkers ??= new Map();

    const key = String(id);
    let m = s.calendarMarkers.get(key);

    const icon = calendarDivIcon(level, meta);
    if (!icon) return false;

    if (!m) {
        m = L.marker([latNum, lngNum], { icon, riseOnHover: true, title: meta?.title ?? key });
        s.calendarLayer.addLayer(m);
        s.calendarMarkers.set(key, m);
    } else {
        m.setLatLng([latNum, lngNum]);
        m.setIcon(icon);
    }

    if (meta?.title || meta?.description) {
        const t = s.utils?.escapeHtml ? s.utils.escapeHtml(meta.title ?? "") : (meta.title ?? "");
        const d = s.utils?.escapeHtml ? s.utils.escapeHtml(meta.description ?? "") : (meta.description ?? "");
        m.bindPopup(`<b>${t}</b><br/>${d}`);
    }

    return true;
}

export function clearCrowdCalendarMarkers(scopeKey = null) {
    console.log("[OutZen] clearCrowdCalendarMarkers called");
    const s = getS(pickScopeKey(scopeKey));
    if (!s?.map) return false;

    const L = ensureLeaflet();
    if (!L) return false;

    // guarantees the existence of the containers
    s.calendarLayer ??= L.layerGroup().addTo(s.map);
    s.calendarMarkers ??= new Map();

    s.calendarLayer.clearLayers();
    s.calendarMarkers.clear();

    console.debug("[Calendar] cleared");
    return true;
}

export function upsertCrowdCalendarMarkers(items, scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    if (!s.map) return false;

    for (const it of (items ?? [])) {
        const id = `cc:${it.id ?? it.Id}`;
        addOrUpdateCrowdCalendarMarker(
            id,
            it.latitude ?? it.Latitude,
            it.longitude ?? it.Longitude,
            it.expectedLevel ?? it.ExpectedLevel ?? 1,
            { title: it.eventName ?? it.EventName, description: it.regionCode ?? it.RegionCode, icon: "🥁🎉" },
            pickScopeKey(scopeKey)
        );
    }
    return true;
}

export function pruneCrowdCalendarMarkers(activeIds, scopeKey = null) {
    const s = getS(pickScopeKey(scopeKey));
    if (!s.map) return false;

    const keep = new Set((activeIds ?? []).map(String));
    for (const [id, m] of s.calendarMarkers.entries()) {
        if (!keep.has(id)) {
            try { s.calendarLayer.removeLayer(m); } catch { }
            s.calendarMarkers.delete(id);
        }
    }
    return true;
}

export function removeCrowdCalendarMarker(id, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, L } = ready;

    s.calendarLayer ??= L.layerGroup().addTo(s.map);
    s.calendarMarkers ??= new Map();

    if (!id) return false;

    const existing = s.calendarMarkers.get(id);
    if (existing) {
        s.calendarLayer.removeLayer(existing);
        s.calendarMarkers.delete(id);
        console.debug("[Calendar] removed", id);
        return true;
    }
    return false;
}

export function fitToCalendarMarkers(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const ready = ensureMapReady(k);
    if (!ready) return false;

    const { s, map } = ready;
    if (!s.calendarLayer) return false;

    const bounds = s.calendarLayer.getBounds?.();
    if (!bounds || !bounds.isValid || !bounds.isValid()) return false;

    return safeFitBounds(map, bounds.pad(0.15), { animate: false });
}

// ------------------------------
// Incremental weather bundle input
// ------------------------------
export function upsertWeatherIntoBundleInput(delta, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = getS(k);
    if (!s.map) return false;

    s.bundleLastInput ??= { events: [], places: [], crowds: [], traffic: [], weather: [], suggestions: [], gpt: [] };
    s._weatherById ??= new Map();

    let action = "upsert";
    let item = null;
    let id = null;

    if (delta && typeof delta === "object" && ("action" in delta || "Action" in delta)) {
        action = String(delta.action ?? delta.Action ?? "upsert").toLowerCase();
        item = delta.item ?? delta.Item ?? null;
        id = delta.id ?? delta.Id ?? null;
    } else {
        item = delta;
        action = "upsert";
    }

    if (action === "delete") {
        const key = String(id ?? item?.Id ?? item?.id ?? "");
        if (!key) return false;
        s._weatherById.delete(key);
        s.bundleLastInput.weather = Array.from(s._weatherById.values());
        return true;
    }

    const raw = item;
    if (!raw) return false;

    const wid = raw.Id ?? raw.id;
    if (wid == null) return false;

    const ll = pickLatLng(raw, s.utils);

    if (!ll || !isInBelgium(ll, s)) {
        s._weatherById.delete(String(wid));
        s.bundleLastInput.weather = Array.from(s._weatherById.values());
        return true;
    }

    const normalized = {
        ...raw,
        Id: wid,
        Latitude: raw.Latitude ?? raw.latitude ?? ll.lat,
        Longitude: raw.Longitude ?? raw.longitude ?? ll.lng,
    };

    s._weatherById.set(String(wid), normalized);
    s.bundleLastInput.weather = Array.from(s._weatherById.values());
    return true;
}
function wrapScope(fn) {
    return (...args) => {
        // Convention: last optional argument = scopeKey
        // If the call comes from an old page (no scope), we fallback.
        const last = args.length ? args[args.length - 1] : null;
        const scopeKey = (typeof last === "string" && last.length) ? last : null;
        return fn(...args, scopeKey);
    };
}
// Example: if you want to make “scopeKey” mandatory for certain calls, we can also log in dev.
function wrapScopeDevWarn(fn, name) {
    return (...args) => {
        const last = args.length ? args[args.length - 1] : null;
        const hasScope = (typeof last === "string" && last.length);
        if (!hasScope && (globalThis.__OZ_ENV === "dev")) {
            console.warn(`[OutZen][${name}] called without scopeKey -> using __OutZenActiveScope fallback`);
        }
        return fn(...args, hasScope ? last : null);
    };
}
(function patchLeafletMoveEndGuard() {
    const L = globalThis.L;
    if (!L || L.__ozPatchedMoveEnd) return;
    L.__ozPatchedMoveEnd = true;

    const proto = L.GridLayer && L.GridLayer.prototype;
    if (!proto || typeof proto._onMoveEnd !== "function") return;

    const orig = proto._onMoveEnd;
    proto._onMoveEnd = function (...args) {
        try {
            // If "this" is broken or detached, we silently ignore it.
            if (!this || !this._map) return;
            return orig.apply(this, args);
        } catch (e) {
            // swallow in dev
            return;
        }
    };
})();

window.addEventListener("error", e => console.log("JS error:", e.error));
window.addEventListener("unhandledrejection", e => console.log("Unhandled promise:", e.reason));

// ------------------------------
// Legacy bridge (optional)
// ------------------------------
//globalThis.OutZenInterop ??= {};
/*globalThis.OutZenInterop.bootOutZen = bootOutZen;*/
//globalThis.OutZenInterop ??= {};
//globalThis.OutZenDebug ??= {};

//globalThis.OutZenInterop.isOutZenReady = isOutZenReady;
//globalThis.OutZenInterop.disposeOutZen = disposeOutZen;

//globalThis.OutZenInterop.addOrUpdateCrowdMarker = addOrUpdateCrowdMarker;
//globalThis.OutZenInterop.removeCrowdMarker = removeCrowdMarker;
//globalThis.OutZenInterop.clearCrowdMarkers = clearCrowdMarkers;
//globalThis.OutZenInterop.fitToMarkers = fitToMarkers;
//globalThis.OutZenInterop.refreshMapSize = refreshMapSize;

//globalThis.OutZenInterop.addOrUpdateBundleMarkers = addOrUpdateBundleMarkers;
//globalThis.OutZenInterop.updateBundleMarker = updateBundleMarker;
//globalThis.OutZenInterop.scheduleBundleRefresh = scheduleBundleRefresh;

//globalThis.OutZenInterop.enableHybridZoom = enableHybridZoom;
//globalThis.OutZenInterop.addOrUpdateDetailMarkers = addOrUpdateDetailMarkers;

//globalThis.OutZenInterop.setWeatherChart = setWeatherChart;
//globalThis.OutZenInterop.upsertWeatherIntoBundleInput = upsertWeatherIntoBundleInput;
//globalThis.OutZenInterop.debugClusterCount = debugClusterCount;
//globalThis.OutZenInterop.addOrUpdateWeatherMarkers = addOrUpdateWeatherMarkers;
//globalThis.OutZenInterop.activateHybridAndZoom = activateHybridAndZoom;

//globalThis.OutZenInterop.forceDetailsMode = forceDetailsMode;
//globalThis.OutZenInterop.refreshHybridNow = refreshHybridNow;
//globalThis.OutZenInterop.fitToDetails = fitToDetails;
//globalThis.OutZenInterop.fitToCalendarMarkers = fitToCalendarMarkers;
//globalThis.OutZenInterop.getCurrentMapId = getCurrentMapId;

//globalThis.OutZenInterop.dumpState = () => {
//    const s = globalThis.__OutZenGetS?.();
//    if (!s) return null;
//    return {
//        mapId: s.mapContainerId,
//        zoom: s.map?.getZoom?.(),
//        hybrid: s.hybrid,
//        hasClusterLayer: !!(s.cluster && s.map?.hasLayer?.(s.cluster)),
//        markers: s.markers?.size,
//        clusterLayers: s.cluster?.getLayers?.()?.length,
//        bundleMarkers: s.bundleMarkers?.size,
//        detailMarkers: s.detailMarkers?.size,
//    };
//};

//globalThis.clearCrowdCalendarMarkers = () => clearCrowdCalendarMarkers();
//globalThis.OutZenDebug ??= {};
//globalThis.OutZenDebug.clearCrowdCalendarMarkers = clearCrowdCalendarMarkers;
//globalThis.OutZenDebug.upsertCrowdCalendarMarkers = upsertCrowdCalendarMarkers;

























































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/