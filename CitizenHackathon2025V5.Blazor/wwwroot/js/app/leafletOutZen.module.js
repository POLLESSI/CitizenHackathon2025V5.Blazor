/* wwwroot/js/app/leafletOutZen.module.js */
/* global L, Chart */
"use strict";

/* =========================================================
   OutZen Leaflet Module (ESM) - Guarded & Hot-reload safe
   - Singleton per scopeKey: __OutZenSingleton__{scopeKey}
   - bootOutZen / disposeOutZen (token-protected)
   - Markers: crowd/place/event/weather (cluster-aware)
   - Calendar + Antenna markers: NO cluster
   - Bundles + Hybrid mode: bundles far, details near
   - Incremental weather updates: upsertWeatherIntoBundleInput + scheduleBundleRefresh
   - Blazor interop: registerDotNetRef + click on suggestion
   ========================================================= */

/* ---------------------------------------------------------
   Scope helpers
--------------------------------------------------------- */
function pickScopeKey(scopeKey) {
    return scopeKey || globalThis.__OutZenActiveScope || "main";
}

function peekS(scopeKey = "main") {
    const key = "__OutZenSingleton__" + String(scopeKey || "main");
    return globalThis[key] ?? null;
}

function initState(s) {
    s.consts ??= {};
    s.utils ??= {};
    s.flags ??= {};
    s.hybrid ??= { enabled: true, threshold: 13, showing: null };

    s.flags.userLockedMode ??= false;
    s.flags.showWeatherPinsInBundles ??= false;

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
}

function getS(scopeKey = "main") {
    const key = "__OutZenSingleton__" + String(scopeKey || "main");

    if (!globalThis[key]) {
        globalThis[key] = {
            version: "2026.02.28-clean",

            initialized: false,
            bootTs: 0,

            map: null,
            mapContainerId: null,
            mapContainerEl: null,
            _domToken: null,

            // base layers
            cluster: null,        // markerClusterGroup (optional)
            layerGroup: null,     // normal markers
            detailLayer: null,    // details mode (optional)
            calendarLayer: null,  // NO cluster
            antennaLayer: null,   // NO cluster

            // marker registries
            markers: new Map(),         // general (crowd/event/place/weather...)
            bundleMarkers: new Map(),   // bundle markers
            bundleIndex: new Map(),     // bundle data
            detailMarkers: new Map(),   // detailed markers
            calendarMarkers: new Map(), // calendar markers
            antennaMarkers: new Map(),  // antenna markers

            // hybrid
            hybrid: { enabled: true, threshold: 13, showing: null },
            _hybridBound: false,
            _hybridHandler: null,
            _hybridSwitching: false,

            // incremental bundles input
            bundleLastInput: null,
            _bundleRefreshT: 0,
            _weatherById: new Map(),

            // charts
            chart: null,
            _wxChart: null,
            _wxChartCanvasId: null,

            // observers/tokens
            _mapToken: 0,
            _ro: null,
            _resizeQueued: false,

            // misc
            consts: {},
            utils: {},
            flags: {},
        };

        initState(globalThis[key]);
    }

    return globalThis[key];
}

globalThis.__OutZenGetS ??= (scopeKey = "main") => getS(scopeKey);

/* ---------------------------------------------------------
   Guards / Leaflet helpers
--------------------------------------------------------- */
function ensureLeaflet() {
    const Leaflet = globalThis.L;
    if (!Leaflet) {
        console.error("[OutZen] ❌ Leaflet not loaded (window.L missing).");
        return null;
    }
    return Leaflet;
}

function makeHas(s) {
    return (layer) =>
        !!layer &&
        !!s.map &&
        typeof s.map.hasLayer === "function" &&
        s.map.hasLayer(layer);
}

async function waitForContainer(mapId, tries = 30) {
    for (let i = 0; i < tries; i++) {
        const el = document.getElementById(mapId);
        if (el) return el;
        await new Promise((r) => requestAnimationFrame(r));
    }
    return null;
}

function resetLeafletDomId(mapId) {
    const L = ensureLeaflet();
    if (!L) return;
    const dom = L.DomUtil.get(mapId);
    if (dom && dom._leaflet_id) {
        try { delete dom._leaflet_id; } catch { dom._leaflet_id = undefined; }
    }
}

function ensureMapReady(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k);
    if (!s || !s.map) return null;

    const L = ensureLeaflet();
    if (!L) return null;

    return { k, s, L, map: s.map, has: makeHas(s) };
}

/* ---------------------------------------------------------
   Popup binding (supports plugin OR fallback to bindPopup)
   - supports both signatures:
       bindPopupSmart(html, options)
       bindPopupSmart(marker, html, options)
--------------------------------------------------------- */
function safeBindPopup(marker, html, opts = {}) {
    if (!marker) return false;

    const options = {
        maxWidth: 420,
        closeButton: true,
        autoPan: true,
        autoClose: true,
        closeOnClick: true,
        className: "oz-popup",
        ...opts
    };

    try {
        // If a plugin/patch exists, we try both possible signatures
        if (typeof marker.bindPopupSmart === "function") {
            try { marker.bindPopupSmart(html, options); return true; } catch { }
            try { marker.bindPopupSmart(marker, html, options); return true; } catch { }
            // If that fails, we fall back on bindPopup
        }

        if (typeof marker.bindPopup === "function") {
            marker.bindPopup(html, options);
            return true;
        }

        console.warn("[OutZen] safeBindPopup: marker has no bindPopup method", marker);
        return false;
    } catch (e) {
        console.error("[OutZen] safeBindPopup failed", e);
        return false;
    }
}

function buildPopupHtml(info, s) {
    const title = info?.title ?? "Unknown";
    const desc = info?.description ?? "";
    const esc = s.utils.escapeHtml;
    return `
    <div class="outzen-popup">
      <div class="title">${esc(title)}</div>
      <div class="desc">${esc(desc)}</div>
    </div>
  `.trim();
}

/* ---------------------------------------------------------
   Cluster-aware add/remove layer
--------------------------------------------------------- */
function addLayerSmart(layer, s) {
    if (!s?.map || !layer) return;

    // if marker is flagged no-cluster, always add to map/layer
    const noCluster = !!(layer?.options?.__ozNoCluster);

    const hasCluster = !!s.cluster;
    const clusterOnMap = hasCluster && typeof s.map.hasLayer === "function" && s.map.hasLayer(s.cluster);

    if (!noCluster && hasCluster && clusterOnMap && typeof s.cluster.addLayer === "function") {
        try { s.cluster.addLayer(layer); return; } catch { /* fallback */ }
    }

    try { s.map.addLayer(layer); } catch { }
}

function removeLayerSmart(layer, s) {
    if (!s?.map || !layer) return;

    const noCluster = !!(layer?.options?.__ozNoCluster);

    if (noCluster) {
        try { s.map.removeLayer(layer); } catch { }
        return;
    }

    try {
        if (s.cluster && typeof s.cluster.removeLayer === "function") s.cluster.removeLayer(layer);
        else s.map.removeLayer(layer);
    } catch { }
}

/* ---------------------------------------------------------
   Lat/Lng helpers
--------------------------------------------------------- */
function toNumLoose(v) {
    if (v == null) return null;
    if (typeof v === "string") v = v.replace(",", ".");
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
}

function pickLatLng(o) {
    if (!o) return null;

    const latVal =
        o.lat ?? o.Lat ?? o.LAT ??
        o.latitude ?? o.Latitude ?? o.LATITUDE ??
        o?.location?.lat ?? o?.Location?.Lat ?? o?.Location?.Latitude ??
        o?.coords?.lat ?? o?.Coords?.Latitude;

    const lngVal =
        o.lng ?? o.Lng ?? o.LNG ??
        o.lon ?? o.Lon ?? o.longitude ?? o.Longitude ?? o.LONGITUDE ??
        o?.location?.lng ?? o?.Location?.Lng ?? o?.Location?.Longitude ??
        o?.coords?.lng ?? o?.Coords?.Longitude;

    const lat = toNumLoose(latVal);
    const lng = toNumLoose(lngVal);
    if (lat == null || lng == null) return null;
    if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;

    return { lat, lng };
}

/* ---------------------------------------------------------
   Icons
--------------------------------------------------------- */
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
    kind = "generic",
    scopeKey = null,
    isTraffic = false,
    weatherType = null,
    iconOverride = null,
} = {}) {
    const lvlClass = getMarkerClassForLevel(level);
    const trafficClass = isTraffic ? "oz-marker--traffic" : "";
    const kindClass = `oz-marker--${String(kind).toLowerCase()}`;
    const scopeClass = scopeKey ? `oz-scope--${String(scopeKey).toLowerCase()}` : "";
    const emoji = iconOverride ? iconOverride : (weatherType ? getWeatherEmoji(weatherType) : "");
    const content = (emoji && String(emoji).trim()) ? emoji : "•";

    return L.divIcon({
        className: `oz-marker ${lvlClass} ${kindClass} ${trafficClass} ${scopeClass}`.trim(),
        html: `<div class="oz-marker-inner">${content}</div>`,
        iconSize: [26, 26],
        iconAnchor: [13, 26],
        popupAnchor: [0, -26],
    });
}

/* ---------------------------------------------------------
   Debug exports
--------------------------------------------------------- */
export function dumpState(scopeKey = null, { createIfMissing = false } = {}) {
    const k = pickScopeKey(scopeKey);
    const s = createIfMissing ? getS(k) : peekS(k);
    if (!s) return { loaded: true, scopeKey: k, exists: false };

    const has = makeHas(s);
    const safeCount = (arr) => Array.isArray(arr) ? arr.length : 0;

    return {
        loaded: true,
        scopeKey: k,
        mapId: s.mapContainerId ?? null,
        zoom: s.map?.getZoom?.() ?? null,

        hasMap: !!s.map,
        initialized: !!s.initialized,
        bootTs: s.bootTs ?? 0,

        hasCluster: !!s.cluster,
        markers: s.markers?.size ?? 0,
        bundleMarkers: s.bundleMarkers?.size ?? 0,
        detailMarkers: s.detailMarkers?.size ?? 0,

        calendarMarkers: s.calendarMarkers?.size ?? 0,
        hasCalendarLayer: has(s.calendarLayer),

        antennaMarkers: s.antennaMarkers?.size ?? 0,
        hasAntennaLayer: has(s.antennaLayer),

        showing: s.hybrid?.showing ?? null,
        hasDetailLayer: has(s.detailLayer),

        bundleLastInputSizes: s.bundleLastInput ? {
            places: safeCount(s.bundleLastInput.places),
            events: safeCount(s.bundleLastInput.events),
            crowds: safeCount(s.bundleLastInput.crowds),
            traffic: safeCount(s.bundleLastInput.traffic),
            weather: safeCount(s.bundleLastInput.weather),
            suggestions: safeCount(s.bundleLastInput.suggestions),
            gpt: safeCount(s.bundleLastInput.gpt),
        } : null,
    };
}

export function listScopes() {
    const out = [];
    for (const k of Object.keys(globalThis)) {
        if (!k.startsWith("__OutZenSingleton__")) continue;
        const scopeKey = k.replace("__OutZenSingleton__", "");
        const s = globalThis[k];
        const has = s ? makeHas(s) : (() => false);
        out.push({
            scopeKey,
            mapId: s?.mapContainerId ?? null,
            hasMap: !!s?.map,
            initialized: !!s?.initialized,
            bootTs: s?.bootTs ?? 0,
            markers: s?.markers?.size ?? 0,
            bundleMarkers: s?.bundleMarkers?.size ?? 0,
            detailMarkers: s?.detailMarkers?.size ?? 0,
            showing: s?.hybrid?.showing ?? null,
            hasDetailLayer: !!(s && has(s.detailLayer)),
        });
    }
    out.sort((a, b) => (b.bootTs || 0) - (a.bootTs || 0));
    console.table(out);
    return out;
}

export function isOutZenReady(scopeKey = null) {
    const s = peekS(pickScopeKey(scopeKey));
    return !!s?.initialized && !!s?.map;
}

/* ---------------------------------------------------------
   Boot / Dispose
--------------------------------------------------------- */
function bootFail(mapId, scopeKey, reason = "") {
    return { ok: false, token: null, mapId: mapId ?? null, scopeKey: scopeKey ?? null, reason };
}

function destroyChartIfAny(s) {
    if (s?.chart && typeof s.chart.destroy === "function") {
        try { s.chart.destroy(); } catch { }
    }
    if (s) s.chart = null;
}

function destroyWxChartIfAny(s) {
    try {
        if (s?._wxChart && typeof s._wxChart.destroy === "function") s._wxChart.destroy();
    } catch { }
    if (s) s._wxChart = null;
}

function ensureWxChartState(s) {
    s._wxChart ??= null;
    s._wxChartCanvasId ??= null;
}

/**
 * Boot Leaflet map
 * options: {
 *   mapId, scopeKey, center:[lat,lng], zoom,
 *   enableChart, force, resetMarkers, resetAll,
 *   enableHybrid, enableCluster, hybridThreshold
 * }
 */
export async function bootOutZen({
    mapId,
    scopeKey = "main",
    center = [50.85, 4.35],
    zoom = 12,
    enableChart = false,
    force = false,
    resetMarkers = false,
    resetAll = false,
    enableHybrid = true,
    enableCluster = true,
    hybridThreshold = 13,
} = {}) {
    globalThis.__OutZenActiveScope = scopeKey;
    const s = getS(scopeKey);

    const host = await waitForContainer(mapId, 30);
    if (!host) return bootFail(mapId, scopeKey, "container-not-found");

    // If map exists and container changed => dispose old map (internal call => allowNoToken)
    if (s.map && s.mapContainerId && s.mapContainerId !== mapId) {
        disposeOutZen({ mapId: s.mapContainerId, scopeKey, allowNoToken: true });
    }

    // Force dispose current
    if (s.map && force) {
        disposeOutZen({ mapId: s.mapContainerId, scopeKey, allowNoToken: true });
    }

    resetLeafletDomId(mapId);

    const L = ensureLeaflet();
    if (!L) return bootFail(mapId, scopeKey, "leaflet-missing");

    // Always clean host before creating map
    try { host.replaceChildren(); } catch { host.innerHTML = ""; }

    const map = L.map(host, {
        zoomAnimation: false,
        fadeAnimation: false,
        markerZoomAnimation: false,
        preferCanvas: true,
        zoomControl: true,
        trackResize: true,
        minZoom: 5,
        maxZoom: 19,
        zoomSnap: 1,
        zoomDelta: 1
    }).setView(center, zoom);

    // Base tile
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "© OpenStreetMap contributors",
        maxZoom: 19,
    }).addTo(map);

    try { map.doubleClickZoom.disable(); } catch { }

    // Optional cluster
    if (enableCluster && L.markerClusterGroup) {
        s.cluster ??= L.markerClusterGroup({
            disableClusteringAtZoom: 16,
            spiderfyOnMaxZoom: true,
            showCoverageOnHover: false,
            zoomToBoundsOnClick: true,
        });
        if (!map.hasLayer(s.cluster)) s.cluster.addTo(map);
    } else {
        s.cluster = null;
    }

    // Token
    const token = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    host.dataset.ozToken = token;
    s._domToken = token;

    // Store map
    s.map = map;
    s.mapContainerId = mapId;
    s.mapContainerEl = host;

    // Resize observer
    try {
        if (s._ro) { try { s._ro.disconnect(); } catch { } }
        s._ro = new ResizeObserver(() => {
            if (!s.map) return;
            try { s.map.invalidateSize({ animate: false }); } catch { }
        });
        s._ro.observe(host);
    } catch { }

    // invalidate async ops
    s._mapToken = (s._mapToken || 0) + 1;

    // Base layers
    s.layerGroup ??= L.layerGroup();
    if (!map.hasLayer(s.layerGroup)) s.layerGroup.addTo(map);

    s.calendarLayer ??= L.layerGroup();
    if (!map.hasLayer(s.calendarLayer)) s.calendarLayer.addTo(map);

    // antenna layer created on demand
    // detail layer created on demand

    // Reset logic
    if (resetAll) {
        try { s.cluster?.clearLayers?.(); } catch { }
        try { s.layerGroup?.clearLayers?.(); } catch { }
        try { s.calendarLayer?.clearLayers?.(); } catch { }
        try { s.detailLayer?.clearLayers?.(); } catch { }
        try { s.antennaLayer?.clearLayers?.(); } catch { }

        s.markers = new Map();
        s.bundleMarkers = new Map();
        s.bundleIndex = new Map();
        s.detailMarkers = new Map();
        s.calendarMarkers = new Map();
        s.antennaMarkers = new Map();

        s._weatherById = new Map();
        s.bundleLastInput = null;
        s.hybrid.showing = null;
    } else {
        if (resetMarkers) s.markers = new Map();
        else s.markers ??= new Map();

        s.bundleMarkers ??= new Map();
        s.bundleIndex ??= new Map();
        s.detailMarkers ??= new Map();
        s.calendarMarkers ??= new Map();
        s.antennaMarkers ??= new Map();
        s._weatherById ??= new Map();
    }

    // Optional chart
    destroyChartIfAny(s);
    if (enableChart && globalThis.Chart) {
        const canvas = document.getElementById("crowdChart");
        if (canvas) {
            const ctx = canvas.getContext("2d");
            if (ctx) {
                s.chart = new Chart(ctx, {
                    type: "bar",
                    data: { labels: [], datasets: [{ label: "Metric", data: [] }] },
                    options: { responsive: true, animation: false },
                });
            }
        }
    }

    // Hybrid
    try {
        if (enableHybrid) enableHybridZoom(true, hybridThreshold, scopeKey);
        else s.hybrid.enabled = false;
    } catch (e) {
        console.warn("[bootOutZen] hybrid init failed", e);
    }

    // Restore last bundles
    if (s.bundleLastInput) {
        try { addOrUpdateBundleMarkers(s.bundleLastInput, 80, scopeKey); } catch { }
    }

    queueMicrotask(() => {
        try { map.invalidateSize({ animate: false, debounceMoveend: true }); } catch { }
    });

    s.initialized = true;
    s.bootTs = Date.now();

    console.log("[bootOutZen] ok", { mapId, scopeKey, token });
    return { ok: true, token, mapId, scopeKey };
}

export function getCurrentMapId(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    return s?.mapContainerId ?? null;
}

export function disposeOutZen({ mapId, scopeKey = "main", token = null, allowNoToken = false } = {}) {
    const s = peekS(scopeKey) || getS(scopeKey);
    if (!s) return false;

    const host = s.mapContainerEl || (mapId ? document.getElementById(mapId) : null);
    const currentTok = host?.dataset?.ozToken ?? s._domToken ?? null;

    if (!allowNoToken) {
        if (!token || !currentTok || token !== currentTok) {
            console.warn("[disposeOutZen] token mismatch -> IGNORE", { mapId, scopeKey, token, currentTok });
            return false;
        }
    }

    // clear layers
    try { s.cluster?.clearLayers?.(); } catch { }
    try { s.layerGroup?.clearLayers?.(); } catch { }
    try { s.calendarLayer?.clearLayers?.(); } catch { }
    try { s.detailLayer?.clearLayers?.(); } catch { }
    try { s.antennaLayer?.clearLayers?.(); } catch { }

    // charts
    destroyChartIfAny(s);
    destroyWxChartIfAny(s);

    // remove map
    try { s.map?.remove?.(); } catch { }
    s.map = null;
    s.mapContainerId = null;
    s.mapContainerEl = null;

    // reset registries
    s.markers = new Map();
    s.bundleMarkers = new Map();
    s.bundleIndex = new Map();
    s.detailMarkers = new Map();
    s.calendarMarkers = new Map();
    s.antennaMarkers = new Map();
    s._weatherById = new Map();
    s.bundleLastInput = null;

    s.initialized = false;

    // cleanup DOM
    if (host) {
        try { host.replaceChildren(); } catch { host.innerHTML = ""; }
        try { delete host.dataset.ozToken; } catch { }
    }

    // disconnect observer
    try { s._ro?.disconnect?.(); } catch { }
    s._ro = null;

    // HARD RESET: delete singleton (prevents stale scopes in dev hot-reload)
    const gk = "__OutZenSingleton__" + String(scopeKey || "main");
    try { delete globalThis[gk]; } catch { globalThis[gk] = undefined; }

    return true;
}

/* ---------------------------------------------------------
   Public marker API (crowd/place/event/weather)
--------------------------------------------------------- */
export function addOrUpdateCrowdMarker(id, lat, lng, level, info, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;

    const latNum = toNumLoose(lat);
    const lngNum = toNumLoose(lng);
    if (latNum == null || lngNum == null) {
        console.warn("[addOrUpdateCrowdMarker] invalid coords", { id, lat, lng });
        return false;
    }

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
        try { existing.setIcon(icon); } catch { }
        try {
            if (existing.getPopup()) existing.setPopupContent(popupHtml);
            else safeBindPopup(existing, popupHtml);
        } catch { }
        return true;
    }

    const marker = L.marker([latNum, lngNum], {
        title: info?.title ?? key,
        riseOnHover: true,
        icon,
    });

    safeBindPopup(marker, popupHtml);
    addLayerSmart(marker, s);
    s.markers.set(key, marker);
    return true;
}

export function removeCrowdMarker(id, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
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
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s } = ready;

    try { s.cluster?.clearLayers?.(); } catch { }
    if (!s.cluster) {
        for (const m of s.markers.values()) {
            try { s.map.removeLayer(m); } catch { }
        }
    }
    s.markers.clear();
    return true;
}

export function clearMarkersByPrefix(prefix, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    if (!(s?.markers instanceof Map)) return 0;

    let n = 0;
    for (const [id, m] of Array.from(s.markers.entries())) {
        if (!String(id).startsWith(prefix)) continue;
        try { removeLayerSmart(m, s); } catch { }
        s.markers.delete(id);
        n++;
    }

    try { s.cluster?.refreshClusters?.(); } catch { }
    return n;
}

export function addOrUpdatePlaceMarker(place, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { k, s } = ready;

    const ll = pickLatLng(place);
    if (!ll) return false;

    const id = `place:${place?.Id ?? place?.id}`;
    const title = place?.Name ?? place?.name ?? place?.Title ?? place?.title ?? "Place";
    const description = place?.Description ?? place?.description ?? place?.Type ?? place?.type ?? "";

    return addOrUpdateCrowdMarker(id, ll.lat, ll.lng, 1, { kind: "place", title, description, icon: "🏰" }, k);
}

export function addOrUpdateEventMarker(ev, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { k } = ready;

    const ll = pickLatLng(ev);
    if (!ll) return false;

    const id = `event:${ev?.Id ?? ev?.id}`;
    const title = ev?.Title ?? ev?.Name ?? ev?.title ?? ev?.name ?? "Event";
    const description = ev?.Description ?? ev?.description ?? "";

    return addOrUpdateCrowdMarker(id, ll.lat, ll.lng, 2, { kind: "event", title, description, icon: "🎪" }, k);
}

export function addOrUpdateWeatherMarkers(items, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { k } = ready;

    if (!Array.isArray(items)) return false;

    for (const w of items) {
        const ll = pickLatLng(w);
        if (!ll) continue;

        const wid = (w?.Id ?? w?.id);
        if (wid == null) continue;

        const id = `wf:${wid}`;
        const level = (w?.IsSevere || w?.isSevere) ? 4 : 2;

        const description = [
            `Temp: ${w?.TemperatureC ?? w?.temperatureC ?? "?"}°C`,
            `Hum: ${w?.Humidity ?? w?.humidity ?? "?"}%`,
            `Wind: ${w?.WindSpeedKmh ?? w?.windSpeedKmh ?? "?"} km/h`,
            `Rain: ${w?.RainfallMm ?? w?.rainfallMm ?? "?"} mm`,
            (w?.Description ?? w?.description) ? `Desc: ${w?.Description ?? w?.description}` : null,
        ].filter(Boolean).join(" • ");

        addOrUpdateCrowdMarker(id, ll.lat, ll.lng, level, {
            kind: "weather",
            title: w?.Summary ?? w?.summary ?? "Weather",
            description,
            weatherType: (w?.WeatherType ?? w?.weatherType ?? "").toString(),
            isTraffic: false,
        }, k);
    }

    return true;
}

/* ---------------------------------------------------------
   Calendar markers (NO cluster)
--------------------------------------------------------- */
function ensureCalendarLayer(s, L) {
    if (!s?.map) return null;
    s.calendarLayer ??= L.layerGroup();
    if (!s.map.hasLayer(s.calendarLayer)) s.calendarLayer.addTo(s.map);
    return s.calendarLayer;
}

export function addOrUpdateCrowdCalendarMarker(id, lat, lng, level, info, scopeKey = "main") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;

    const latNum = toNumLoose(lat);
    const lngNum = toNumLoose(lng);
    if (latNum == null || lngNum == null) return false;

    const layer = ensureCalendarLayer(s, L);
    if (!layer) return false;

    s.calendarMarkers ??= new Map();
    const key = String(id);

    const title = info?.eventname ?? info?.title ?? "Crowd Calendar";
    const desc = info?.description ?? "";

    const icon = buildMarkerIcon(L, level, {
        kind: "calendar",
        scopeKey: k,
        iconOverride: info?.icon ?? "🥁🎉",
    });

    const popupHtml = buildPopupHtml({ title, description: desc }, s);

    let mk = s.calendarMarkers.get(key);

    if (!mk) {
        mk = L.marker([latNum, lngNum], {
            icon,
            title,
            riseOnHover: true,
            zIndexOffset: 2000,
            __ozNoCluster: true,
        });

        safeBindPopup(mk, popupHtml, { maxWidth: 420, closeButton: true, autoPan: true });
        layer.addLayer(mk);
        s.calendarMarkers.set(key, mk);
        return true;
    }

    try { mk.setLatLng([latNum, lngNum]); } catch { }
    try { mk.setIcon(icon); } catch { }
    try {
        if (mk.getPopup()) mk.setPopupContent(popupHtml);
        else safeBindPopup(mk, popupHtml);
    } catch { }

    try { layer.addLayer(mk); } catch { } // layerGroup can safely re-add
    return true;
}

export function clearCrowdCalendarMarkers(scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s } = ready;

    try { s.calendarLayer?.clearLayers?.(); } catch { }
    try { s.calendarMarkers?.clear?.(); } catch { }
    return true;
}

export function removeCrowdCalendarMarker(markerId, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s } = ready;

    const key = String(markerId);
    const m = s.calendarMarkers?.get?.(key);
    if (!m) return true;

    try {
        if (s.calendarLayer?.removeLayer) s.calendarLayer.removeLayer(m);
        else removeLayerSmart(m, s);
    } catch { }

    s.calendarMarkers.delete(key);
    return true;
}

// Bulk upsert expected by Blazor: OutZenInterop.upsertCrowdCalendarMarkers(items, scopeKey)
export function upsertCrowdCalendarMarkers(items, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k } = ready;
    if (!Array.isArray(items)) return false;

    for (const it of items) {
        const ll = pickLatLng(it);
        if (!ll) continue;

        // id: adapt if your DTO has a CalendarId / CrowdCalendarId / Id
        const id = it?.Id ?? it?.id ?? it?.CalendarId ?? it?.calendarId ?? `${ll.lat},${ll.lng}`;

        // level: adapt to your DTO
        const level = Number(it?.Level ?? it?.level ?? it?.CrowdLevel ?? it?.crowdLevel ?? 1);

        // info: what your popup wants to display
        const info = {
            title: it?.Title ?? it?.title ?? it?.EventName ?? it?.eventName ?? "Crowd Calendar",
            description: it?.Description ?? it?.description ?? it?.Message ?? it?.message ?? "",
            icon: it?.Icon ?? it?.icon ?? "🥁🎉",
        };

        addOrUpdateCrowdCalendarMarker(id, ll.lat, ll.lng, level, info, k);
    }
    return true;
}

/* ---------------------------------------------------------
   Antenna markers (NO cluster)
--------------------------------------------------------- */
function ensureAntennaLayer(s, L) {
    if (!s?.map) return null;
    s.antennaLayer ??= L.featureGroup();
    if (!s.map.hasLayer(s.antennaLayer)) s.antennaLayer.addTo(s.map);
    return s.antennaLayer;
}

function makeAntennaKey(a) {
    const id = a?.Id ?? a?.id ?? a?.AntennaId ?? a?.antennaId;
    if (id == null) return null;
    return `ant:${id}`;
}

export function addOrUpdateAntennaMarker(antenna, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { k, s, L } = ready;

    if (!antenna) return false;
    const ll = pickLatLng(antenna);
    if (!ll) return false;

    const layer = ensureAntennaLayer(s, L);
    if (!layer) return false;

    s.antennaMarkers ??= new Map();

    const key = makeAntennaKey(antenna) ?? `ant:${ll.lat.toFixed(5)},${ll.lng.toFixed(5)}`;
    const lvl = Number(antenna.Level ?? antenna.level ?? 1);
    const iconOverride = antenna.Icon ?? antenna.icon ?? "📡";
    const icon = buildMarkerIcon(L, lvl, { kind: "antenna", scopeKey: k, iconOverride });

    const title = antenna?.Name ?? antenna?.name ?? "Antenna";
    const desc = antenna?.Description ?? antenna?.description ?? "";
    const popup = buildPopupHtml({ title, description: desc }, s);

    const existing = s.antennaMarkers.get(key);
    if (existing) {
        try { existing.setLatLng([ll.lat, ll.lng]); } catch { }
        try { existing.setIcon(icon); } catch { }
        try {
            if (existing.getPopup()) existing.setPopupContent(popup);
            else safeBindPopup(existing, popup);
        } catch { }
        try { layer.addLayer(existing); } catch { }
        return true;
    }

    const m = L.marker([ll.lat, ll.lng], {
        icon,
        title,
        riseOnHover: true,
        __ozNoCluster: true,
    });

    safeBindPopup(m, popup);
    try { layer.addLayer(m); } catch { addLayerSmart(m, s); }
    s.antennaMarkers.set(key, m);
    return true;
}

export function removeAntennaMarker(key, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s } = ready;

    s.antennaMarkers ??= new Map();
    const k = String(key);
    const m = s.antennaMarkers.get(k);
    if (!m) return true;

    try {
        if (s.antennaLayer?.removeLayer) s.antennaLayer.removeLayer(m);
        else removeLayerSmart(m, s);
    } catch { }

    s.antennaMarkers.delete(k);
    return true;
}

export function pruneAntennaMarkers(activeKeys, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s } = ready;

    s.antennaMarkers ??= new Map();
    const keep = new Set((activeKeys ?? []).map(String));

    for (const [key, m] of Array.from(s.antennaMarkers.entries())) {
        if (keep.has(key)) continue;
        try {
            if (s.antennaLayer?.removeLayer) s.antennaLayer.removeLayer(m);
            else removeLayerSmart(m, s);
        } catch { }
        s.antennaMarkers.delete(key);
    }

    return true;
}

/* ---------------------------------------------------------
   Normalize payload (bundles/details)
--------------------------------------------------------- */
function normalizeItems(arr) {
    if (!Array.isArray(arr)) return [];
    const out = [];
    for (const x of arr) {
        const ll = pickLatLng(x);
        if (!ll) continue;
        out.push({ ...x, lat: ll.lat, lng: ll.lng });
    }
    return out;
}

function normalizePayload(payload) {
    const p = payload || {};
    const norm = {
        places: p.places ?? p.Places ?? [],
        events: p.events ?? p.Events ?? [],
        crowds: p.crowds ?? p.Crowds ?? [],
        traffic: p.traffic ?? p.Traffic ?? [],
        weather: p.weather ?? p.Weather ?? [],
        suggestions: p.suggestions ?? p.Suggestions ?? [],
        gpt: p.gpt ?? p.Gpt ?? p.GPT ?? [],
    };

    norm.places = normalizeItems(norm.places);
    norm.events = normalizeItems(norm.events);
    norm.crowds = normalizeItems(norm.crowds);
    norm.traffic = normalizeItems(norm.traffic);
    norm.weather = normalizeItems(norm.weather);
    norm.suggestions = normalizeItems(norm.suggestions);
    norm.gpt = normalizeItems(norm.gpt);

    return norm;
}

/* ---------------------------------------------------------
   Blazor interop (DotNetRef) for suggestion click
--------------------------------------------------------- */
const __ozDotNet = globalThis.__ozDotNet || (globalThis.__ozDotNet = new Map());

function getDotNetRef(scopeKey) {
    const m = globalThis.__ozDotNet;
    if (!(m instanceof Map)) return null;
    return m.get(String(scopeKey || "main")) || null;
}

export function registerDotNetRef(scopeKey, dotnetRef) {
    if (!(globalThis.__ozDotNet instanceof Map)) globalThis.__ozDotNet = new Map();
    globalThis.__ozDotNet.set(String(scopeKey || "main"), dotnetRef);
    return true;
}

export function unregisterDotNetRef(scopeKey) {
    const m = globalThis.__ozDotNet;
    if (m instanceof Map) m.delete(String(scopeKey || "main"));
    return true;
}

/* ---------------------------------------------------------
   Detail markers + Hybrid zoom
--------------------------------------------------------- */
function ensureDetailLayer(s, L) {
    if (!s?.map) return null;
    s.detailLayer ??= L.layerGroup();
    if (!s.map.hasLayer(s.detailLayer)) s.detailLayer.addTo(s.map);
    return s.detailLayer;
}

function clearDetailMarkers(s) {
    try { s.detailLayer?.clearLayers?.(); } catch { }
    try { s.detailMarkers?.clear?.(); } catch { }
}

function clampLevel14(level) {
    const n = Number(level);
    if (!Number.isFinite(n)) return 1;
    return Math.max(1, Math.min(4, n));
}

function makeDetailKey(kind, item) {
    const k = String(kind).toLowerCase();
    if (k === "suggestion") {
        const sid = item?.SuggestionId ?? item?.suggestionId ?? item?.Id ?? item?.id;
        if (sid != null) return `suggestion:${sid}`;
    }
    const id = item?.Id ?? item?.id ?? item?.ForecastId ?? item?.forecastId ?? item?.WeatherForecastId ?? item?.weatherForecastId;
    if (id != null) return `${k}:${id}`;

    const ll = pickLatLng(item);
    if (ll) return `${k}:${ll.lat.toFixed(5)},${ll.lng.toFixed(5)}`;

    return `${k}:${JSON.stringify(item).slice(0, 64)}`;
}

function addDetailMarker(kind, item, s, L, scopeKey) {
    if (!s?.map) return;
    const layer = ensureDetailLayer(s, L);
    if (!layer) return;

    const kindLower = String(kind).toLowerCase();
    const ll = pickLatLng(item);
    if (!ll) return;

    const key = makeDetailKey(kindLower, item);
    if (s.detailMarkers.has(key)) return;

    const title = s.utils.titleOf(kindLower, item);

    // --- Weather
    if (kindLower === "weather") {
        const severe = !!(item?.IsSevere ?? item?.isSevere);
        const level = severe ? 4 : 2;

        const icon = buildMarkerIcon(L, level, {
            kind: "weather",
            weatherType: (item?.WeatherType ?? item?.weatherType ?? "").toString(),
        });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Weather: ${title}`,
            riseOnHover: true,
            pane: "markerPane",
            __ozNoCluster: true,
        });

        const desc = `Temp: ${item?.TemperatureC ?? item?.temperatureC ?? "?"}°C • Wind: ${item?.WindSpeedKmh ?? item?.windSpeedKmh ?? "?"} km/h`;
        safeBindPopup(m, buildPopupHtml({
            title: item?.Summary ?? item?.summary ?? title,
            description: `Temp: ${item?.TemperatureC ?? item?.temperatureC ?? "?"}°C • Vent: ${item?.WindSpeedKmh ?? item?.windSpeedKmh ?? "?"} km/h`
        }, s));
        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    // --- Place
    if (kindLower === "place") {
        const icon = buildMarkerIcon(L, 1, { kind: "place", iconOverride: "🏰" });
        const m = L.marker([ll.lat, ll.lng], { icon, title: `Place: ${title}`, riseOnHover: true, pane: "markerPane", __ozNoCluster: true });
        safeBindPopup(m, buildPopupHtml({ title, description: item?.Description ?? item?.description ?? "" }, s));
        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    // --- Event
    if (kindLower === "event") {
        const icon = buildMarkerIcon(L, 2, { kind: "event", iconOverride: "🎪" });
        const m = L.marker([ll.lat, ll.lng], { icon, title: `Event: ${title}`, riseOnHover: true, pane: "markerPane", __ozNoCluster: true });
        safeBindPopup(m, buildPopupHtml({ title, description: item?.Description ?? item?.description ?? "" }, s));
        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    // --- Crowd (fallback)
    if (kindLower === "crowd") {
        const level = clampLevel14(item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level ?? 2);
        const icon = buildMarkerIcon(L, level, { kind: "crowd" });
        const m = L.marker([ll.lat, ll.lng], { icon, title: `Crowd: ${title}`, riseOnHover: true, pane: "markerPane", __ozNoCluster: true });
        safeBindPopup(m, buildPopupHtml({ title, description: item?.Message ?? item?.message ?? "" }, s));
        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    // --- Traffic (fallback)
    if (kindLower === "traffic") {
        const level = clampLevel14(item?.TrafficLevel ?? item?.trafficLevel ?? item?.CongestionLevel ?? item?.level ?? 2);
        const icon = buildMarkerIcon(L, level, { kind: "traffic", isTraffic: true, iconOverride: "🚗" });
        const m = L.marker([ll.lat, ll.lng], { icon, title: `Traffic: ${title}`, riseOnHover: true, pane: "markerPane", __ozNoCluster: true });
        safeBindPopup(m, buildPopupHtml({ title, description: item?.Description ?? item?.description ?? "" }, s));
        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    // --- Suggestion (click -> Blazor)
    if (kindLower === "suggestion") {
        const icon = buildMarkerIcon(L, 2, { kind: "suggestion", iconOverride: "💡" });
        const m = L.marker([ll.lat, ll.lng], { icon, title: `Suggestion: ${title}`, riseOnHover: true, pane: "markerPane", __ozNoCluster: true });

        const popupDesc = [
            item?.Reason ? `Reason: ${item.Reason}` : null,
            item?.OriginalPlace ? `From: ${item.OriginalPlace}` : null,
            item?.SuggestedAlternatives ? `Alt: ${item.SuggestedAlternatives}` : null,
            (item?.DistanceKm != null) ? `Distance: ${item.DistanceKm} km` : null,
        ].filter(Boolean).join(" • ");

        safeBindPopup(m, buildPopupHtml({ title, description: popupDesc }, s));

        const sid = Number(item?.SuggestionId ?? item?.Id ?? item?.id);
        m.on("click", () => {
            try { m.openPopup(); } catch { }
            if (!Number.isFinite(sid)) return;
            const dn = getDotNetRef(scopeKey);
            if (!dn) return;
            setTimeout(() => {
                try { dn.invokeMethodAsync("SelectSuggestionFromMap", sid); } catch { }
            }, 0);
        });

        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    // Generic fallback
    const icon = buildMarkerIcon(L, 1, { kind: kindLower });
    const m = L.marker([ll.lat, ll.lng], { icon, title: `${kindLower}: ${title}`, riseOnHover: true, pane: "markerPane", __ozNoCluster: true });
    safeBindPopup(m, buildPopupHtml({ title, description: item?.Description ?? item?.description ?? "" }, s));
    layer.addLayer(m);
    s.detailMarkers.set(key, m);
}

export function addOrUpdateDetailMarkers(payload, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;
    clearDetailMarkers(s);

    const norm = normalizePayload(payload);

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;
        for (const x of arr) addDetailMarker(kind, x, s, L, k);
    };

    push(norm.events, "event");
    push(norm.places, "place");
    push(norm.crowds, "crowd");
    push(norm.traffic, "traffic");
    push(norm.weather, "weather");
    push(norm.suggestions, "suggestion");
    push(norm.gpt, "gpt");

    return true;
}

function runWhenMapReallyIdle(map, fn) {
    const tick = () => {
        if (!map) return;
        if (map._animatingZoom || map._zooming || map._panning || map._moving) {
            requestAnimationFrame(tick);
            return;
        }
        requestAnimationFrame(() => {
            if (!map) return;
            if (map._animatingZoom || map._zooming || map._panning || map._moving) return;
            fn();
        });
    };
    requestAnimationFrame(tick);
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

function switchToDetails(s, map, scopeKey) {
    const L = ensureLeaflet();
    if (!L || !s?.map) return;

    if (!s.bundleLastInput) {
        s.hybrid.showing = s.hybrid.showing ?? "bundles";
        return;
    }

    // hide bundle markers
    for (const m of s.bundleMarkers.values()) {
        try { if (map.hasLayer(m)) map.removeLayer(m); } catch { }
    }

    ensureDetailLayer(s, L);
    addOrUpdateDetailMarkers(s.bundleLastInput, scopeKey);
    s.hybrid.showing = "details";
}

function switchToBundles(s, map) {
    clearDetailMarkers(s);

    for (const m of s.bundleMarkers.values()) {
        try { if (!map.hasLayer(m)) map.addLayer(m); } catch { }
    }

    s.hybrid.showing = "bundles";
}

function refreshHybridVisibility(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k);
    const map = s?.map;
    const token = s?._mapToken;

    if (!s || !map || !s.hybrid?.enabled) return;
    if (s._hybridSwitching) return;

    if (s.flags?.userLockedMode) {
        if (s.hybrid.showing !== "details") switchToDetails(s, map, k);
        return;
    }

    setTimeout(() => {
        if (!s.map || s.map !== map || s._mapToken !== token) return;

        runWhenMapReallyIdle(map, () => {
            if (!s.map || s.map !== map || s._mapToken !== token) return;
            if (s._hybridSwitching) return;

            s._hybridSwitching = true;
            try {
                const z = map.getZoom?.() ?? 0;
                const wantDetails = (Number(z) || 0) >= (Number(s.hybrid.threshold) || 13);

                if (wantDetails && s.hybrid.showing !== "details") switchToDetails(s, map, k);
                else if (!wantDetails && s.hybrid.showing !== "bundles") switchToBundles(s, map);
            } finally {
                s._hybridSwitching = false;
            }
        });
    }, 0);
}

export function enableHybridZoom(enabled = true, threshold = 13, scopeKey = "main") {
    const k = pickScopeKey(scopeKey);
    const s = getS(k);

    s.hybrid.enabled = !!enabled;
    s.hybrid.threshold = (threshold ?? s.hybrid.threshold ?? 13);

    if (!s.map) return false;

    if (!s._hybridBound) {
        s._hybridBound = true;
        const h = throttleOZ(() => refreshHybridVisibility(k), 150);
        s._hybridHandler = h;
        try { s.map.on("zoomend", h); } catch { }
    }

    refreshHybridVisibility(k);
    return true;
}

export function forceDetailsMode(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    if (!s.map) return false;

    s.flags.userLockedMode = true;
    s.hybrid.enabled = true;

    const th = Number(s.hybrid.threshold) || 13;
    try { if ((s.map.getZoom?.() ?? 0) < th) s.map.setZoom(th, { animate: false }); } catch { }

    switchToDetails(s, s.map, k);
    try { refreshHybridVisibility(k); } catch { }
    return true;
}

export function unlockHybrid(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    s.flags.userLockedMode = false;
    refreshHybridVisibility(k);
    return true;
}

export function refreshHybridNow(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    if (!s.map || !s.hybrid?.enabled) return false;
    try { refreshHybridVisibility(k); } catch { }
    return true;
}

/* ---------------------------------------------------------
   Bundles (group by proximity)
--------------------------------------------------------- */
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

function pickCrowdLevel(item) {
    return clampLevel14(item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level);
}
function pickTrafficLevel(item) {
    return clampLevel14(item?.TrafficLevel ?? item?.trafficLevel ?? item?.CongestionLevel ?? item?.level);
}
function pickWeatherLevel(item) {
    const severe = !!(item?.IsSevere ?? item?.isSevere);
    return severe ? 4 : 2;
}

function bundleSeverity(b) {
    let sev = 1;
    if (Array.isArray(b?.crowds)) for (const c of b.crowds) sev = Math.max(sev, pickCrowdLevel(c));
    if (Array.isArray(b?.traffic)) for (const t of b.traffic) sev = Math.max(sev, pickTrafficLevel(t));
    if (Array.isArray(b?.weather)) for (const w of b.weather) sev = Math.max(sev, pickWeatherLevel(w));
    return sev;
}

function bundleTotal(b) {
    const arrs = ["events", "places", "crowds", "traffic", "weather", "suggestions", "gpt"];
    let total = 0;
    for (const k of arrs) total += (b?.[k]?.length ?? 0);
    return total;
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

    const nEvents = b?.events?.length ?? 0;
    const nPlaces = b?.places?.length ?? 0;
    const nCrowds = b?.crowds?.length ?? 0;
    const nTraffic = b?.traffic?.length ?? 0;
    const nWeather = b?.weather?.length ?? 0;
    const nSugg = b?.suggestions?.length ?? 0;
    const nGpt = b?.gpt?.length ?? 0;

    const dots = `
    <div class="oz-dots">
      ${nEvents ? `<span class="oz-dot oz-dot-events" title="Events"></span>` : ``}
      ${nPlaces ? `<span class="oz-dot oz-dot-places" title="Places"></span>` : ``}
      ${nCrowds ? `<span class="oz-dot oz-dot-crowds" title="Crowd"></span>` : ``}
      ${nTraffic ? `<span class="oz-dot oz-dot-traffic" title="Traffic"></span>` : ``}
      ${nWeather ? `<span class="oz-dot oz-dot-weather" title="Weather"></span>` : ``}
      ${(nSugg || nGpt) ? `<span class="oz-dot oz-dot-gpt" title="Suggestion/GPT"></span>` : ``}
    </div>
  `.trim();

    const tag = onlyWeather ? `<div class="oz-bundle-tag">${weatherEmojiForBundle(b)}</div>` : "";

    const html = `
    <div class="oz-bundle ${lvlClass} ${onlyWeather ? "oz-bundle-weather" : ""}">
      <div class="oz-badge">${totalCount}</div>
      ${tag}
      ${dots}
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

function bundlePopupHtml(b, s) {
    const esc = s.utils.escapeHtml;
    const total = bundleTotal(b);

    const breakdown = (k) => (b?.[k]?.length ?? 0);

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
          <div class="oz-row"><span class="oz-k">Events</span><span class="oz-v">${esc(breakdown("events"))}</span></div>
          <div class="oz-row"><span class="oz-k">Places</span><span class="oz-v">${esc(breakdown("places"))}</span></div>
          <div class="oz-row"><span class="oz-k">Crowds</span><span class="oz-v">${esc(breakdown("crowds"))}</span></div>
          <div class="oz-row"><span class="oz-k">Traffic</span><span class="oz-v">${esc(breakdown("traffic"))}</span></div>
          <div class="oz-row"><span class="oz-k">Weather</span><span class="oz-v">${esc(breakdown("weather"))}</span></div>
          <div class="oz-row"><span class="oz-k">Suggestions</span><span class="oz-v">${esc(breakdown("suggestions"))}</span></div>
          <div class="oz-row"><span class="oz-k">GPT</span><span class="oz-v">${esc(breakdown("gpt"))}</span></div>
        </div>
      </div>
    </div>
  `.trim();
}

function computeBundles(payload, tolMeters) {
    const buckets = new Map();
    const norm = payload;

    const push = (arr, kind) => {
        if (!Array.isArray(arr) || arr.length === 0) return;
        for (const item of arr) {
            const ll = pickLatLng(item);
            if (!ll) continue;

            const key = bundleKeyFor(ll.lat, ll.lng, tolMeters);
            let b = buckets.get(key);
            if (!b) {
                b = { key, lat: ll.lat, lng: ll.lng, events: [], places: [], crowds: [], traffic: [], weather: [], suggestions: [], gpt: [] };
                buckets.set(key, b);
            }
            b[kind].push(item);
        }
    };

    push(norm.places, "places");
    push(norm.events, "events");
    push(norm.crowds, "crowds");
    push(norm.traffic, "traffic");
    push(norm.weather, "weather");
    push(norm.suggestions, "suggestions");
    push(norm.gpt, "gpt");

    return buckets;
}

export function updateBundleMarker(b, scopeKey = "main") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return;

    const { s, L, map } = ready;

    const total = bundleTotal(b);
    const sev = bundleSeverity(b);

    const existing = s.bundleMarkers.get(b.key);
    const popup = bundlePopupHtml(b, s);

    if (total <= 0) {
        if (existing) {
            removeLayerSmart(existing, s);
            s.bundleMarkers.delete(b.key);
            s.bundleIndex.delete(b.key);
        }
        return;
    }

    const icon = makeBadgeIcon(total, sev, b);

    if (!existing) {
        const m = L.marker([b.lat, b.lng], {
            icon,
            title: `Area (${total})`,
            riseOnHover: true,
            __ozNoCluster: true,
        });

        try { safeBindPopup(m, popup, { maxWidth: 460 }); } catch { }
        addLayerSmart(m, s);

        s.bundleMarkers.set(b.key, m);
        s.bundleIndex.set(b.key, b);
        return;
    }

    try { existing.setLatLng([b.lat, b.lng]); } catch { }
    try { existing.setIcon(icon); } catch { }
    try {
        if (existing.getPopup()) existing.setPopupContent(popup);
        else safeBindPopup(existing, popup);
    } catch { }

    // Hybrid: hide bundle markers when in details
    try {
        if (s.hybrid?.showing === "details") {
            if (map.hasLayer(existing)) map.removeLayer(existing);
        } else {
            if (!map.hasLayer(existing)) map.addLayer(existing);
        }
    } catch { }

    s.bundleIndex.set(b.key, b);
}

export function addOrUpdateBundleMarkers(payload, tolMeters = 80, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s } = ready;

    const tol = Number(tolMeters);
    const tolFinal = (Number.isFinite(tol) && tol > 0) ? tol : 80;

    const norm = normalizePayload(payload);
    s.bundleLastInput = norm;

    const bundles = computeBundles(norm, tolFinal);

    // remove old
    for (const oldKey of Array.from(s.bundleMarkers.keys())) {
        if (!bundles.has(oldKey)) {
            const marker = s.bundleMarkers.get(oldKey);
            removeLayerSmart(marker, s);
            s.bundleMarkers.delete(oldKey);
            s.bundleIndex.delete(oldKey);
        }
    }

    // upsert current
    for (const b of bundles.values()) updateBundleMarker(b, k);

    // hybrid refresh
    try { refreshHybridVisibility(k); } catch { }

    // refresh cluster
    if (s.cluster && typeof s.cluster.refreshClusters === "function") {
        try { s.cluster.refreshClusters(); } catch { }
    }

    // optional: show weather pins in bundles mode
    if (s.flags.showWeatherPinsInBundles && s.hybrid?.showing !== "details") {
        addOrUpdateWeatherMarkers(norm.weather ?? [], k);
    }

    return true;
}

/* ---------------------------------------------------------
   Fit helpers
--------------------------------------------------------- */
function _boundsFromLatLngs(L, latlngs) {
    if (!latlngs || !latlngs.length) return null;
    const b = L.latLngBounds(latlngs);
    return b && b.isValid && b.isValid() ? b : null;
}

export function fitToAllMarkers(scopeKey = null, opts = {}) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s, L, map } = ready;

    const padding = opts.padding ?? [22, 22];
    const maxZoom = opts.maxZoom ?? 17;

    const latlngs = [];

    for (const m of (s.markers?.values?.() ?? [])) { try { latlngs.push(m.getLatLng()); } catch { } }
    for (const m of (s.antennaMarkers?.values?.() ?? [])) { try { latlngs.push(m.getLatLng()); } catch { } }
    for (const m of (s.calendarMarkers?.values?.() ?? [])) { try { latlngs.push(m.getLatLng()); } catch { } }

    const b = _boundsFromLatLngs(L, latlngs);
    if (!b) return false;

    try { map.fitBounds(b, { padding, animate: false, maxZoom }); } catch { }
    return true;
}

export function fitToCalendar(scopeKey = null, opts = {}) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s, L, map } = ready;
    const padding = opts.padding ?? [22, 22];
    const maxZoom = opts.maxZoom ?? 17;

    const latlngs = [];
    for (const m of (s.calendarMarkers?.values?.() ?? [])) {
        try { latlngs.push(m.getLatLng()); } catch { }
    }

    const b = _boundsFromLatLngs(L, latlngs);
    if (!b) return false;

    try { map.fitBounds(b, { padding, animate: false, maxZoom }); } catch { }
    return true;
}
export function fitToBundles(scopeKey = null, opts = {}) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s, L, map } = ready;

    const padding = opts.padding ?? [22, 22];
    const maxZoom = opts.maxZoom ?? 16;

    const latlngs = [];
    for (const m of (s.bundleMarkers?.values?.() ?? [])) { try { latlngs.push(m.getLatLng()); } catch { } }

    const b = _boundsFromLatLngs(L, latlngs);
    if (!b) return false;

    try { map.fitBounds(b, { padding, animate: false, maxZoom }); } catch { }
    return true;
}

export function fitToDetails(scopeKey = null, opts = {}) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s, L, map } = ready;

    const padding = opts.padding ?? [22, 22];
    const maxZoom = opts.maxZoom ?? 17;

    const latlngs = [];
    for (const m of (s.detailMarkers?.values?.() ?? [])) { try { latlngs.push(m.getLatLng()); } catch { } }

    if (!latlngs.length) {
        for (const m of (s.markers?.values?.() ?? [])) { try { latlngs.push(m.getLatLng()); } catch { } }
    }

    const b = _boundsFromLatLngs(L, latlngs);
    if (!b) return false;

    try { map.fitBounds(b, { padding, animate: false, maxZoom }); } catch { }
    return true;
}

export function activateHybridAndZoom(scopeKey = null, threshold = 13) {
    const k = pickScopeKey(scopeKey);
    enableHybridZoom(true, threshold, k);

    const s = peekS(k) || getS(k);
    const z = s?.map?.getZoom?.() ?? 0;
    const wantDetails = z >= threshold;

    return wantDetails ? fitToDetails(k) : fitToBundles(k);
}

/* ---------------------------------------------------------
   Resize helper
--------------------------------------------------------- */
export function refreshMapSize(scopeKey = null, tries = 10) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s, map } = ready;
    const token = s._mapToken;

    if (s._resizeQueued) return true;
    s._resizeQueued = true;

    requestAnimationFrame(() => {
        s._resizeQueued = false;
        if (!s.map || s.map !== map || s._mapToken !== token) return;

        const el = map.getContainer?.();
        if (!el || !el.isConnected) return;

        const r = el.getBoundingClientRect?.();
        const ok = !!r && r.width >= 10 && r.height >= 10;

        if (!ok) {
            if (tries > 0) setTimeout(() => refreshMapSize(scopeKey, tries - 1), 60);
            return;
        }

        if (map._animatingZoom || map._zooming || map._panning) {
            if (tries > 0) setTimeout(() => refreshMapSize(scopeKey, tries - 1), 60);
            return;
        }

        try { map.invalidateSize({ animate: false, debounceMoveend: true }); } catch { }
    });

    return true;
}

/* ---------------------------------------------------------
   Incremental weather bundle input
--------------------------------------------------------- */
export function scheduleBundleRefresh(delayMs = 150, tolMeters = 80, scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);

    clearTimeout(s._bundleRefreshT);
    s._bundleRefreshT = setTimeout(() => {
        try {
            if (s.bundleLastInput) addOrUpdateBundleMarkers(s.bundleLastInput, tolMeters, k);
        } catch { }
    }, delayMs);
}

export function upsertWeatherIntoBundleInput(delta, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s } = ready;

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

    const ll = pickLatLng(raw);
    if (!ll) {
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

/* ---------------------------------------------------------
   Debug helpers
--------------------------------------------------------- */
export function debugDumpMarkers(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    const has = makeHas(s);

    console.log("[DBG] markers keys =", Array.from(s.markers?.keys?.() ?? []));
    console.log("[DBG] bundle keys =", Array.from(s.bundleMarkers?.keys?.() ?? []));
    console.log("[DBG] detail keys =", Array.from(s.detailMarkers?.keys?.() ?? []));
    console.log("[DBG] calendar keys =", Array.from(s.calendarMarkers?.keys?.() ?? []));
    console.log("[DBG] antenna keys =", Array.from(s.antennaMarkers?.keys?.() ?? []));
    console.log("[DBG] map initialized =", !!s.map,
        "cluster =", !!s.cluster,
        "showing=", s.hybrid?.showing,
        "hasDetailLayer=", has(s.detailLayer),
        "hasCalendarLayer=", has(s.calendarLayer),
        "hasAntennaLayer=", has(s.antennaLayer)
    );
}

export function debugClusterCount(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    const layers = s?.cluster?.getLayers?.();
    console.log("[DBG] markers=", s?.markers?.size ?? 0, "clusterLayers=", layers?.length ?? 0);
}

export function debugExplainBundles(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    const li = s.bundleLastInput;
    return {
        hasLastInput: !!li,
        lastCounts: li ? {
            places: li.places?.length ?? 0,
            events: li.events?.length ?? 0,
            crowds: li.crowds?.length ?? 0,
            traffic: li.traffic?.length ?? 0,
            weather: li.weather?.length ?? 0,
            suggestions: li.suggestions?.length ?? 0,
            gpt: li.gpt?.length ?? 0,
        } : null,
        showing: s.hybrid?.showing ?? null,
        zoom: s.map?.getZoom?.() ?? null,
        bundleMarkers: s.bundleMarkers?.size ?? 0,
        detailMarkers: s.detailMarkers?.size ?? 0,
    };
}

export function clearAllOutZenLayers(scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s } = ready;

    // Clear everything we manage
    try { s.cluster?.clearLayers?.(); } catch { }
    try { s.layerGroup?.clearLayers?.(); } catch { }
    try { s.detailLayer?.clearLayers?.(); } catch { }
    try { s.calendarLayer?.clearLayers?.(); } catch { }
    try { s.antennaLayer?.clearLayers?.(); } catch { }

    // Reset registries
    try { s.markers?.clear?.(); } catch { }
    try { s.bundleMarkers?.clear?.(); } catch { }
    try { s.bundleIndex?.clear?.(); } catch { }
    try { s.detailMarkers?.clear?.(); } catch { }
    try { s.calendarMarkers?.clear?.(); } catch { }
    try { s.antennaMarkers?.clear?.(); } catch { }

    // Reset bundle cache (optionnel mais cohérent si tu “clear all”)
    s.bundleLastInput = null;

    return true;
}

/* ---------------------------------------------------------
   waitForMarkerElement
--------------------------------------------------------- */
export function waitForMarkerElement(marker, tries = 30) {
    return new Promise(resolve => {
        if (!marker) return resolve(null);

        const tick = () => {
            try {
                const el = marker.getElement?.();
                if (el) return resolve(el);
            } catch { }

            if (--tries <= 0) return resolve(null);
            requestAnimationFrame(tick);
        };

        try { marker.once?.("add", () => requestAnimationFrame(tick)); } catch { }
        tick();
    });
}

/* ---------------------------------------------------------
   Leaflet patch guard (stability)
--------------------------------------------------------- */
(function patchLeafletMoveEndGuard() {
    const L = globalThis.L;
    if (!L || L.__ozPatchedMoveEnd) return;
    L.__ozPatchedMoveEnd = true;

    const proto = L.GridLayer && L.GridLayer.prototype;
    if (!proto || typeof proto._onMoveEnd !== "function") return;

    const orig = proto._onMoveEnd;
    proto._onMoveEnd = function (...args) {
        try {
            if (!this || !this._map) return;
            return orig.apply(this, args);
        } catch {
            return;
        }
    };
})();

if (!globalThis.__ozGlobalErrorHooks) {
    globalThis.__ozGlobalErrorHooks = true;
    window.addEventListener("error", e => console.log("JS error:", e.error));
    window.addEventListener("unhandledrejection", e => console.log("Unhandled promise:", e.reason));
}

/* ---------------------------------------------------------
   Chart.js (Weather line chart) - independent from Leaflet
--------------------------------------------------------- */
export function setWeatherChart(points, metric = "Temperature", scopeKey = null, canvasId = null) {
    const k = pickScopeKey(scopeKey);
    const s = getS(k);
    ensureWxChartState(s);

    const ChartLib = globalThis.Chart;
    if (!ChartLib) {
        console.warn("[WX] Chart.js not found (window.Chart missing).");
        return false;
    }

    const cid = canvasId || `weatherChart-${k}`;
    const canvas = document.getElementById(cid);
    if (!canvas) {
        console.warn("[WX] canvas not found:", cid);
        return false;
    }

    if (s._wxChart && s._wxChartCanvasId !== cid) destroyWxChartIfAny(s);
    s._wxChartCanvasId = cid;

    const arr = Array.isArray(points) ? points : [];
    const labels = arr.map(p => String(p?.label ?? ""));
    const data = arr.map(p => Number(p?.value ?? 0));

    const label =
        metric === "Humidity" ? "Humidité (%)" :
            metric === "Wind" ? "Vent (km/h)" :
                metric === "Rain" ? "Pluie (mm)" :
                    "Température (°C)";

    if (!s._wxChart) {
        const ctx = canvas.getContext("2d");
        if (!ctx) return false;

        s._wxChart = new ChartLib(ctx, {
            type: "line",
            data: {
                labels,
                datasets: [{
                    label,
                    data,
                    tension: 0.25,
                    pointRadius: 2
                }]
            },
            options: {
                responsive: true,
                animation: false,
                maintainAspectRatio: false
            }
        });
        return true;
    }

    try {
        s._wxChart.data.labels = labels;
        s._wxChart.data.datasets[0].label = label;
        s._wxChart.data.datasets[0].data = data;
        s._wxChart.update("none");
        return true;
    } catch (e) {
        console.warn("[WX] chart update failed -> recreate", e);
        destroyWxChartIfAny(s);
        return setWeatherChart(points, metric, k, cid);
    }
}
export function peekState(scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    return peekS(k);
}
export function getActiveState() {
    const k = globalThis.__OutZenActiveScope || "main";
    return peekS(k);
}

export function setLineChart(points, seriesLabel = "Série", scopeKey = null, canvasId = null) {
    const k = pickScopeKey(scopeKey);
    const s = getS(k);
    ensureWxChartState(s);

    const ChartLib = globalThis.Chart;
    if (!ChartLib) return false;

    const cid = canvasId || `chart-${k}`;
    const canvas = document.getElementById(cid);
    if (!canvas) return false;

    if (s._wxChart && s._wxChartCanvasId !== cid) destroyWxChartIfAny(s);
    s._wxChartCanvasId = cid;

    const arr = Array.isArray(points) ? points : [];
    const labels = arr.map(p => String(p?.label ?? ""));
    const data = arr.map(p => Number(p?.value ?? 0));

    if (!s._wxChart) {
        const ctx = canvas.getContext("2d");
        if (!ctx) return false;

        s._wxChart = new ChartLib(ctx, {
            type: "line",
            data: { labels, datasets: [{ label: seriesLabel, data, tension: 0.25, pointRadius: 2 }] },
            options: { responsive: true, animation: false, maintainAspectRatio: false }
        });
        return true;
    }

    s._wxChart.data.labels = labels;
    s._wxChart.data.datasets[0].label = seriesLabel;
    s._wxChart.data.datasets[0].data = data;
    s._wxChart.update("none");
    return true;
}



























































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/