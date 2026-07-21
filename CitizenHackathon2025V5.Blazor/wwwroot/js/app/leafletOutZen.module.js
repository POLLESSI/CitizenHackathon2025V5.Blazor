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
            antennaAlertMarkers: new Map(), // antenna alert markers


            // hybrid
            hybrid: { enabled: true, threshold: 13, showing: null },
            _hybridBound: false,
            _hybridHandler: null,
            _hybridSwitching: false,

            // incremental bundles input
            bundleLastInput: null,
            _bundleRefreshT: 0,
            _weatherById: new Map(),
            bundleToleranceMeters: null,

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

function ensureCustomPane(map, name, zIndex) {
    if (!map) return null;

    let pane = map.getPane(name);
    if (!pane) {
        pane = map.createPane(name);
    }

    pane.style.zIndex = String(zIndex);
    pane.style.pointerEvents = "auto";
    pane.classList.add(name);

    return pane;
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
    if (!s?.map || !layer) {
        return;
    }

    try {
        if (s.cluster?.hasLayer?.(layer)) {
            s.cluster.removeLayer(layer);
        }
    } catch {
    }

    try {
        if (s.map.hasLayer(layer)) {
            s.map.removeLayer(layer);
        }
    } catch {
    }
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
function isInsideBelgium(lat, lng, sOrScopeKey = null) {
    let bounds = null;

    if (typeof sOrScopeKey === "string" || sOrScopeKey == null) {
        const s = peekS(pickScopeKey(sOrScopeKey)) || getS(pickScopeKey(sOrScopeKey));
        bounds = s?.consts?.BELGIUM ?? null;
    } else {
        bounds = sOrScopeKey?.consts?.BELGIUM ?? null;
    }

    bounds ??= { minLat: 49.45, maxLat: 51.6, minLng: 2.3, maxLng: 6.6 };

    return lat >= bounds.minLat &&
        lat <= bounds.maxLat &&
        lng >= bounds.minLng &&
        lng <= bounds.maxLng;
}

function pickLatLngBelgiumOnly(o, sOrScopeKey = null) {
    const ll = pickLatLng(o);
    if (!ll) return null;
    if (!isInsideBelgium(ll.lat, ll.lng, sOrScopeKey)) return null;
    return ll;
}
function buildCalendarMarkerIcon(L, level, iconOverride = "🥁🎉", scopeKey = null) {
    const lvl = normalizeLevel(level);

    return L.divIcon({
        className: `oz-marker oz-marker--calendar oz-marker-lvl${lvl} ${scopeKey ? `oz-scope--${String(scopeKey).toLowerCase()}` : ""}`.trim(),
        html: `<div class="oz-marker-inner oz-calendar-inner">${iconOverride}</div>`,
        iconSize: [30, 30],
        iconAnchor: [15, 15],
        popupAnchor: [0, -15],
    });
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
        hasCalendarLayer: !!s.calendarLayer,

        antennaMarkers: s.antennaMarkers?.size ?? 0,
        antennaAlertMarkers: s.antennaAlertMarkers?.size ?? 0,
        hasAntennaLayer: !!s.antennaLayer,
        hasAntennaAlertPane: !!s.map?.getPane?.("ozAntennaAlertPane"),

        showing: s.hybrid?.showing ?? null,
        hasDetailLayer: !!s.detailLayer

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

    let s = getS(scopeKey);

    const host = await waitForContainer(mapId, 30);

    if (!host) {
        return bootFail(
            mapId,
            scopeKey,
            "container-not-found");
    }

    // Force dispose current
    const sameMap =
        !!s.map &&
        s.mapContainerId === mapId;

    const canReuseExistingMap =
        sameMap &&
        !force &&
        !resetAll &&
        !resetMarkers;

    if (canReuseExistingMap) {
        try {
            s.map.invalidateSize({
                animate: false,
                debounceMoveend: true
            });
        } catch {
        }

        return {
            ok: true,
            reused: true,
            token: s._domToken,
            mapId,
            scopeKey
        };
    }
    /*
     * Optionally save the last payload before
     * disposeOutZen() does not remove it.
     */
    const previousBundleInput =
        resetAll
            ? null
            : s.bundleLastInput;

    if (s.map) {
        disposeOutZen({
            mapId: s.mapContainerId,
            scopeKey,
            allowNoToken: true
        });

        /*
         * Very important:
         * disposeOutZen() has removed the singleton.
         */
        s = getS(scopeKey);

        if (previousBundleInput) {
            s.bundleLastInput = previousBundleInput;
        }
    }

    resetLeafletDomId(mapId);

    const L = ensureLeaflet();

    if (!L) {
        return bootFail(
            mapId,
            scopeKey,
            "leaflet-missing");
    }

    // Always clean host before creating map
    try { host.replaceChildren(); } catch { host.innerHTML = ""; }

    const map = L.map(host, {
        zoomAnimation: false,
        fadeAnimation: false,
        markerZoomAnimation: false,
        preferCanvas: false,
        zoomControl: true,
        trackResize: true,
        minZoom: 5,
        maxZoom: 19,
        zoomSnap: 1,
        zoomDelta: 1
    }).setView(center, zoom);

    ensureCustomPane(map, "ozCalendarPane", 5000);

    ensureCustomPane(map, "ozAntennaAlertPane", 9000);

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

    // RECREATE calendar layer on each boot
    try { s.calendarLayer?.clearLayers?.(); } catch { }
    s.calendarLayer = L.layerGroup();
    s.calendarLayer.addTo(map);

    // antenna can also be recreated defensively
    try { s.antennaLayer?.clearLayers?.(); } catch { }
    s.antennaLayer = null;

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
        s.antennaAlertMarkers = new Map();
        s.bundleToleranceMeters = null;

        s._weatherById = new Map();
        s.bundleLastInput = null;
        s.hybrid.showing = null;
    } else {
        if (resetMarkers) s.markers = new Map();
        else s.markers ??= new Map();

        s.bundleMarkers ??= new Map();
        s.bundleIndex ??= new Map();
        s.detailMarkers ??= new Map();

        // IMPORTANT: calendar markers must not survive a boot on a recreated map
        s.calendarMarkers = new Map();

        // safer for antenna too
        s.antennaMarkers = new Map();

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
        try { addOrUpdateBundleMarkers(s.bundleLastInput, 0, scopeKey); } catch { }
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
    for (const marker of s.markers.values()) {
        clearManagedMarkerTimers(marker);
    }

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
    s.antennaAlertMarkers = new Map();
    s._weatherById = new Map();
    s.bundleLastInput = null;
    s.bundleToleranceMeters = null;

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

    const resolvedKind =
        info?.kind ??
        (info?.weatherType ? "weather" :
            (info?.isTraffic ? "traffic" : "crowd"));

    const warn = shouldWarnMarker(resolvedKind, level, info, k);

    const icon = buildMarkerIcon(L, level, {
        kind: resolvedKind,
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

        applyWarningStateToMarker(existing, { warn, isCalendar: false });
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

    applyWarningStateToMarker(marker, { warn, isCalendar: false });
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
    clearManagedMarkerTimers(marker)
    return true;
}

export function clearCrowdMarkers(scopeKey = null) {

    const ready = ensureMapReady(scopeKey);

    if (!ready) {
        console.warn("[clearCrowdMarkers] map not ready",
            {
                scopeKey:
                    pickScopeKey(scopeKey)
            });

        return false;
    }

    const { s } = ready;

    let removed = 0;

    for (const marker of Array.from(s.markers.values())) {

        clearManagedMarkerTimers(marker);
        removeLayerSmart(marker, s);

        removed++;
    }

    /*
     * removeLayerSmart() normally removes each
     * marker from the cluster. The clearLayers is an
     * additional safety measure for this scope.
     */
    try {
        s.cluster?.clearLayers?.();
    }
    catch {
    }

    console.log("[clearCrowdMarkers] completed",
    {
        scopeKey: pickScopeKey(scopeKey),

        removed,

        remaining: s.markers.size
    });

    s.markers.clear();

    return true;
}
export function clearGeneralMarkers(scopeKey = null) {

    return clearCrowdMarkers( scopeKey);
}

export function clearMarkersByPrefix(prefix = "", scopeKey = null) {

    return pruneMarkersByPrefix(prefix, scopeKey);
}

export function addOrUpdatePlaceMarker(place, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { k } = ready;

    const ll = pickLatLng(place);
    if (!ll) return false;

    const id = `place:${place?.Id ?? place?.id}`;
    const title = place?.Name ?? place?.name ?? place?.Title ?? place?.title ?? "Place";
    const description = place?.Description ?? place?.description ?? place?.Type ?? place?.type ?? "";

    return addOrUpdateCrowdMarker(id, ll.lat, ll.lng, 1, {
        kind: "place",
        title,
        description,
        icon: "🏰"
    }, k);
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

export function addOrUpdateFullAlertMarker(
    alert,
    scopeKey = "home") {

    const ready =
        ensureMapReady(scopeKey);

    if (!ready) {
        console.warn(
            "[FULL ALERT] map not ready",
            { scopeKey }
        );

        return false;
    }

    const { k, s, L, map } = ready;

    const ll = pickLatLng(alert);

    if (!ll) {
        console.warn(
            "[FULL ALERT] invalid coordinates",
            alert
        );

        return false;
    }

    const placeId =
        alert.PlaceId ??
        alert.placeId ??
        "unknown";

    const key =
        `full-alert:${placeId}`;

    const placeName =
        alert.PlaceName ??
        alert.placeName ??
        "FULL ALERT";

    const declaredAtUtc =
        alert.DeclaredAtUtc ??
        alert.declaredAtUtc ??
        new Date().toISOString();

    const expiresAtUtc =
        alert.ExpiresAtUtc ??
        alert.expiresAtUtc ??
        new Date(
            Date.now() +
            5 * 60 * 1000
        ).toISOString();

    /*
     * Pane independent of the bundles,
     * clusters and detail markers.
     */
    ensureCustomPane(
        map,
        "ozCriticalAlertPane",
        12000
    );

    const popupHtml =
        buildPopupHtml(
            {
                title:
                    "🚨 FULL ALERT",

                description:
                    `${placeName} • ` +
                    `declared at ` +
                    `${fmtTime(declaredAtUtc)} • ` +
                    `expires at ` +
                    `${fmtTime(expiresAtUtc)}`
            },
            s
        );

    const icon =
        L.divIcon({
            className:
                "oz-full-alert-marker",

            html: `
                <div class="oz-full-alert-ring">
                    <div class="oz-full-alert-core">
                        <div class="oz-full-alert-title">
                            FULL
                        </div>

                        <div class="oz-full-alert-title">
                            ALERT
                        </div>
                    </div>
                </div>
            `.trim(),

            iconSize: [86, 86],
            iconAnchor: [43, 43],
            popupAnchor: [0, -46]
        });

    let marker =
        s.markers.get(key);

    if (!marker) {
        marker =
            L.marker(
                [ll.lat, ll.lng],
                {
                    icon,
                    pane:
                        "ozCriticalAlertPane",

                    title:
                        "FULL ALERT",

                    riseOnHover:
                        true,

                    zIndexOffset:
                        50000,

                    __ozNoCluster:
                        true
                }
            );

        safeBindPopup(
            marker,
            popupHtml
        );

        s.markers.set(
            key,
            marker
        );
    }
    else {
        try {
            marker.setLatLng([
                ll.lat,
                ll.lng
            ]);
        }
        catch (error) {
            console.error(
                "[FULL ALERT] setLatLng failed",
                error
            );
        }

        try {
            marker.setIcon(icon);
        }
        catch (error) {
            console.error(
                "[FULL ALERT] setIcon failed",
                error
            );
        }

        try {
            marker.options.pane =
                "ozCriticalAlertPane";

            marker.options.zIndexOffset =
                50000;
        }
        catch {
        }

        try {
            if (marker.getPopup()) {
                marker.setPopupContent(
                    popupHtml
                );
            }
            else {
                safeBindPopup(
                    marker,
                    popupHtml
                );
            }
        }
        catch (error) {
            console.error(
                "[FULL ALERT] popup update failed",
                error
            );
        }
    }

    /*
     * Ne pas utiliser addLayerSmart ici :
     * une alerte critique doit toujours être
     * ajoutée directement à la carte.
     */
    try {
        const hasLayer =
            map.hasLayer(marker);

        const hasElement =
            !!marker.getElement?.();

        /*
         * Répare aussi le cas où Leaflet croit
         * posséder le layer alors que son élément
         * DOM n'existe plus.
         */
        if (hasLayer && !hasElement) {
            map.removeLayer(marker);
        }

        if (!map.hasLayer(marker)) {
            marker.addTo(map);
        }
    }
    catch (error) {
        console.error(
            "[FULL ALERT] marker attachment failed",
            {
                key,
                scopeKey: k,
                latitude: ll.lat,
                longitude: ll.lng,
                error
            }
        );

        return false;
    }

    const expiresMs =
        new Date(
            expiresAtUtc
        ).getTime();

    const effectiveExpiresMs =
        Number.isFinite(expiresMs)
            ? expiresMs
            : Date.now() +
            5 * 60 * 1000;

    const delay =
        Math.max(
            1000,
            effectiveExpiresMs -
            Date.now()
        );

    clearTimeout(
        marker.__ozFullAlertTimer
    );

    marker.__ozKind =
        "full-alert";

    marker.__ozFullAlertKey =
        key;

    marker.__ozFullAlertExpiresMs =
        effectiveExpiresMs;

    marker.__ozFullAlertTimer =
        setTimeout(
            () => {
                try {
                    removeCrowdMarker(
                        key,
                        k
                    );
                }
                catch (error) {
                    console.error(
                        "[FULL ALERT] removal failed",
                        error
                    );
                }
            },
            delay
        );

    requestAnimationFrame(() => {
        console.log(
            "[FULL ALERT] marker upserted",
            {
                key,
                scopeKey: k,
                mapHasLayer:
                    map.hasLayer(marker),

                elementExists:
                    !!marker.getElement?.(),

                elementClass:
                    marker
                        .getElement?.()
                        ?.className ??
                    null,

                expiresAtUtc,
                delay
            }
        );
    });

    return true;
}

const MANAGED_MARKER_TIMER_KEYS = [
    "__ozFullAlertTimer",
    "__ozWeatherAlertTimer",
    "__ozTrafficAlertTimer",
    "__ozDisasterAlertTimer"
];

function clearManagedMarkerTimers(marker) {
    if (!marker) {
        return;
    }

    for (const timerKey of MANAGED_MARKER_TIMER_KEYS) {
        clearTimeout(marker[timerKey]);
        marker[timerKey] = 0;
    }
}

export function addOrUpdateWeatherAlertMarker(alert, scopeKey = "home") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;

    const ll = pickLatLng(alert);
    if (!ll) return false;

    const placeId = alert.PlaceId ?? alert.placeId ?? "gps";
    const key = `weather-alert:${placeId}`;

    const placeName = alert.PlaceName ?? alert.placeName ?? "Current location";
    const declaredAtUtc = alert.DeclaredAtUtc ?? alert.declaredAtUtc ?? new Date().toISOString();
    const expiresAtUtc = alert.ExpiresAtUtc ?? alert.expiresAtUtc;

    const popupHtml = buildPopupHtml({
        title: "⛈️ CRITICAL WEATHER",
        description: `${placeName} • declared at ${fmtTime(declaredAtUtc)} • expires at ${fmtTime(expiresAtUtc)}`
    }, s);

    const icon = L.divIcon({
        className: "oz-weather-alert-marker",
        html: `
            <div class="oz-weather-alert-ring">
                <div class="oz-weather-alert-core">
                    <div class="oz-weather-alert-icon">⛈️</div>
                    <div class="oz-weather-alert-title">WEATHER</div>
                </div>
            </div>
        `.trim(),
        iconSize: [86, 86],
        iconAnchor: [43, 43],
        popupAnchor: [0, -46]
    });

    let marker = s.markers.get(key);

    if (marker) {
        try { marker.setLatLng([ll.lat, ll.lng]); } catch { }
        try { marker.setIcon(icon); } catch { }
        try {
            if (marker.getPopup()) marker.setPopupContent(popupHtml);
            else safeBindPopup(marker, popupHtml);
        } catch { }
    } else {
        marker = L.marker([ll.lat, ll.lng], {
            icon,
            title: "CRITICAL WEATHER",
            riseOnHover: true,
            zIndexOffset: 50000,
            __ozNoCluster: true
        });

        safeBindPopup(marker, popupHtml);
        addLayerSmart(marker, s);
        s.markers.set(key, marker);
    }

    const expiresMs = expiresAtUtc
        ? new Date(expiresAtUtc).getTime()
        : Date.now() + 5 * 60 * 1000;

    const delay = Math.max(1000, expiresMs - Date.now());

    clearTimeout(marker.__ozWeatherAlertTimer);

    marker.__ozWeatherAlertTimer = setTimeout(() => {
        try { removeCrowdMarker(key, k); } catch { }
    }, delay);

    console.log("[WEATHER ALERT]", { declaredAtUtc, expiresAtUtc, expiresMs, delay });

    return true;
    
}

export function addOrUpdateTrafficAlertMarker(alert, scopeKey = "home") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;

    const ll = pickLatLng(alert);
    if (!ll) return false;

    const placeId = alert.PlaceId ?? alert.placeId ?? "gps";
    const key = `traffic-alert:${placeId}`;

    const placeName = alert.PlaceName ?? alert.placeName ?? "Current location";
    const declaredAtUtc = alert.DeclaredAtUtc ?? alert.declaredAtUtc ?? new Date().toISOString();
    const expiresAtUtc = alert.ExpiresAtUtc ?? alert.expiresAtUtc;

    const popupHtml = buildPopupHtml({
        title: "🚗 CRITICAL TRAFFIC",
        description: `${placeName} • declared at ${fmtTime(declaredAtUtc)} • expires at ${fmtTime(expiresAtUtc)}`
    }, s);

    const icon = L.divIcon({
        className: "oz-traffic-alert-marker",
        html: `
            <div class="oz-traffic-alert-ring">
                <div class="oz-traffic-alert-core">
                    <div class="oz-traffic-alert-icon">🚗</div>
                    <div class="oz-traffic-alert-title">TRAFFIC</div>
                </div>
            </div>
        `.trim(),
        iconSize: [86, 86],
        iconAnchor: [43, 43],
        popupAnchor: [0, -46]
    });

    let marker = s.markers.get(key);

    if (marker) {
        try { marker.setLatLng([ll.lat, ll.lng]); } catch { }
        try { marker.setIcon(icon); } catch { }
        try {
            if (marker.getPopup()) marker.setPopupContent(popupHtml);
            else safeBindPopup(marker, popupHtml);
        } catch { }
    } else {
        marker = L.marker([ll.lat, ll.lng], {
            icon,
            title: "CRITICAL TRAFFIC",
            riseOnHover: true,
            zIndexOffset: 50000,
            __ozNoCluster: true
        });

        safeBindPopup(marker, popupHtml);
        addLayerSmart(marker, s);
        s.markers.set(key, marker);
    }

    const expiresMs = expiresAtUtc
        ? new Date(expiresAtUtc).getTime()
        : Date.now() + 5 * 60 * 1000;

    const delay = Math.max(5 * 60 * 1000, expiresMs - Date.now());

    clearTimeout(marker.__ozTrafficAlertTimer);

    marker.__ozTrafficAlertTimer = setTimeout(() => {
        try { removeCrowdMarker(key, k); } catch { }
    }, delay);

    console.log("[TRAFFIC ALERT]", { declaredAtUtc, expiresAtUtc, expiresMs, delay });

    return true;
    
}

export function addOrUpdateDisasterAlertMarker(alert, scopeKey = "home") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;
    const ll = pickLatLng(alert);
    if (!ll) return false;

    const key = `disaster-alert:${alert.PlaceName ?? alert.placeName ?? "gps"}`;

    const placeName = alert.PlaceName ?? alert.placeName ?? "Current location";
    const declaredAtUtc = alert.DeclaredAtUtc ?? alert.declaredAtUtc ?? new Date().toISOString();
    const expiresAtUtc = alert.ExpiresAtUtc ?? alert.expiresAtUtc;

    const popupHtml = buildPopupHtml({
        title: "🚨 DISASTER ALERT",
        description: `${placeName} • simulation emergency escalation • ${fmtTime(declaredAtUtc)}`
    }, s);

    const icon = L.divIcon({
        className: "oz-disaster-alert-marker",
        html: `
            <div class="oz-disaster-alert-ring">
                <div class="oz-disaster-alert-core">
                    <div class="oz-disaster-alert-icon">🚨</div>
                    <div class="oz-disaster-alert-title">DISASTER</div>
                </div>
            </div>
        `.trim(),
        iconSize: [96, 96],
        iconAnchor: [48, 48],
        popupAnchor: [0, -52]
    });

    let marker = s.markers.get(key);

    if (marker) {
        try { marker.setLatLng([ll.lat, ll.lng]); } catch { }
        try { marker.setIcon(icon); } catch { }
        try {
            if (marker.getPopup()) marker.setPopupContent(popupHtml);
            else safeBindPopup(marker, popupHtml);
        } catch { }
    } else {
        marker = L.marker([ll.lat, ll.lng], {
            icon,
            title: "DISASTER ALERT",
            riseOnHover: true,
            zIndexOffset: 60000,
            __ozNoCluster: true
        });

        safeBindPopup(marker, popupHtml);
        addLayerSmart(marker, s);
        s.markers.set(key, marker);
    }

    const expiresMs = expiresAtUtc
        ? new Date(expiresAtUtc).getTime()
        : Date.now() + 10 * 60 * 1000;

    const delay = Math.max(10 * 60 * 1000, expiresMs - Date.now());

    clearTimeout(marker.__ozDisasterAlertTimer);

    marker.__ozDisasterAlertTimer = setTimeout(() => {
        try { removeCrowdMarker(key, k); } catch { }
    }, delay);

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

    const { k, s, L, map } = ready;

    const latNum = toNumLoose(lat);
    const lngNum = toNumLoose(lng);
    if (latNum == null || lngNum == null) return false;

    if (!isInsideBelgium(latNum, lngNum, s)) {
        console.warn("[CIC][reject-marker-outside-belgium]", { id, lat: latNum, lng: lngNum, scopeKey: k });
        return false;
    }

    ensureCustomPane(map, "ozCalendarPane", 5000);

    const layer = ensureCalendarLayer(s, L);
    if (!layer) return false;

    s.calendarMarkers ??= new Map();
    const key = String(id);

    const title = info?.eventname ?? info?.title ?? "Crowd Calendar";
    const desc = info?.description ?? "";
    const lvl = normalizeLevel(level);
    const isCalendarWarning = k === "crowdinfocalendarview" && lvl >= 2;

    const icon = buildCalendarMarkerIcon(L, level, info?.icon ?? "🥁🎉", k);
    const popupHtml = buildPopupHtml({ title, description: desc }, s);

    let mk = s.calendarMarkers.get(key);

    if (!mk) {
        mk = L.marker([latNum, lngNum], {
            icon,
            pane: "ozCalendarPane",
            title,
            riseOnHover: true,
            zIndexOffset: 10000,
            keyboard: false,
            __ozNoCluster: true
        });

        safeBindPopup(mk, popupHtml, { maxWidth: 420, closeButton: true, autoPan: true });
        layer.addLayer(mk);
        s.calendarMarkers.set(key, mk);

        applyWarningStateToMarker(mk, {
            warn: isCalendarWarning,
            isCalendar: true
        });

        console.log("[CIC][marker:new]", { key, scopeKey: k, isCalendarWarning, pane: mk?.options?.pane });
        console.log("[CIC][warning-check]", { key, scopeKey: k, level, lvl, isCalendarWarning });

        return true;
    }

    try { mk.setLatLng([latNum, lngNum]); } catch { }
    try { mk.setIcon(icon); } catch { }
    try {
        if (mk.getPopup()) {
            mk.setPopupContent(popupHtml);
        } else {
            safeBindPopup(mk, popupHtml, { maxWidth: 420, closeButton: true, autoPan: true });
        }
    } catch { }

    try {
        if (!layer.hasLayer(mk)) layer.addLayer(mk);
    } catch { }

    applyWarningStateToMarker(mk, {
        warn: isCalendarWarning,
        isCalendar: true
    });

    console.log("[CIC][marker:update]", { key, scopeKey: k, isCalendarWarning, pane: mk?.options?.pane });

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

    const allCoords = [];

    for (const it of items) {
        const raw = pickLatLng(it);
        if (raw) allCoords.push(raw);

        const ll = pickLatLngBelgiumOnly(it, k);
        if (!ll) {
            console.warn("[CIC][skip-outside-belgium]", {
                id: it?.Id ?? it?.id,
                lat: it?.Latitude ?? it?.latitude,
                lng: it?.Longitude ?? it?.longitude,
                region: it?.RegionCode ?? it?.regionCode,
                event: it?.EventName ?? it?.eventName
            });
            continue;
        }

        const rawId = it?.Id ?? it?.id ?? it?.CalendarId ?? it?.calendarId ?? `${ll.lat},${ll.lng}`;
        const id = `cc:${rawId}`;

        const level = Number(
            it?.ExpectedLevel ?? it?.expectedLevel ??
            it?.Level ?? it?.level ??
            it?.CrowdLevel ?? it?.crowdLevel ?? 1
        );

        const startLocalTime = it?.StartLocalTime ?? it?.startLocalTime ?? "—";
        const endLocalTime = it?.EndLocalTime ?? it?.endLocalTime ?? "—";
        const leadHours = it?.LeadHours ?? it?.leadHours ?? "—";
        const confidence = it?.Confidence ?? it?.confidence ?? "—";

        const info = {
            eventname: it?.EventName ?? it?.eventName ?? it?.Title ?? it?.title ?? "Crowd Calendar",
            description: `Start ${startLocalTime} • End ${endLocalTime} • LeadHours ${leadHours} • Confidence ${confidence}%`,
            messagetemplate: it?.MessageTemplate ?? it?.messageTemplate ?? "",
            active: it?.Active ?? it?.active ?? true,
            icon: it?.Icon ?? it?.icon ?? "🥁🎉"
        };

        console.log("[CIC][upsert:item]", {
            rawId, id, lat: ll.lat, lng: ll.lng, level,
            event: it?.EventName ?? it?.eventName,
            region: it?.RegionCode ?? it?.regionCode
        });

        addOrUpdateCrowdCalendarMarker(id, ll.lat, ll.lng, level, info, k);
    }

    if (allCoords.length) {
        console.log("[CIC][coords-range]", {
            minLat: Math.min(...allCoords.map(x => x.lat)),
            maxLat: Math.max(...allCoords.map(x => x.lat)),
            minLng: Math.min(...allCoords.map(x => x.lng)),
            maxLng: Math.max(...allCoords.map(x => x.lng))
        });
    }

    return true;
}
export function pruneCrowdCalendarMarkers(activeIds, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s } = ready;

    s.calendarMarkers ??= new Map();
    const keep = new Set((activeIds ?? []).map(String));

    for (const [key, marker] of Array.from(s.calendarMarkers.entries())) {
        if (keep.has(key)) continue;

        try {
            if (s.calendarLayer?.removeLayer) s.calendarLayer.removeLayer(marker);
            else removeLayerSmart(marker, s);
        } catch { }

        s.calendarMarkers.delete(key);
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
    const warn = shouldWarnMarker("antenna", lvl, antenna, k);

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

        applyWarningStateToMarker(existing, { warn, isCalendar: false });
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

    applyWarningStateToMarker(m, { warn, isCalendar: false });
    return true;
}

function computeAntennaAlertSize(activeConnections) {
    const n = Number(activeConnections) || 0;

    if (n >= 1000) return 96;
    if (n >= 750) return 86;
    if (n >= 500) return 76;
    if (n >= 250) return 66;
    if (n >= 100) return 56;

    return 46;
}

export function addOrUpdateAntennaAlertCircle(alert, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;

    if (!alert) return false;

    const status =
        String(
            alert.Status ??
            alert.status ??
            ""
        )
            .trim()
            .toLowerCase();

    const confirmationCount =
        Number(
            alert.ConfirmationCount ??
            alert.confirmationCount ??
            0
        );

    const requestedRequiredCount =
        Number(
            alert.RequiredCount ??
            alert.requiredCount ??
            4
        );

    const requiredCount =
        Math.max(
            4,
            Number.isFinite(
                requestedRequiredCount)
                ? requestedRequiredCount
                : 4
        );

    if (
        status !== "confirmed" ||
        confirmationCount < requiredCount
    ) {
        console.info(
            "[FULL ALERT] marker skipped: " +
            "Incomplete confirmation",
            {
                status,
                confirmationCount,
                requiredCount
            }
        );

        return false;
    }

    const ll = pickLatLng(alert);
    if (!ll) return false;

    const antennaId = alert.AntennaId ?? alert.antennaId ?? alert.Id ?? alert.id;
    if (antennaId == null) return false;

    s.antennaAlertMarkers ??= new Map();

    const key = `antenna-alert:${antennaId}`;

    const active =
        Number(alert.ActiveConnections ?? alert.activeConnections ?? 0);

    const unique =
        Number(alert.UniqueDevices ?? alert.uniqueDevices ?? 0);

    const severity =
        Number(alert.Severity ?? alert.severity ?? 4);

    const size = computeAntennaAlertSize(active);

    const title =
        alert.Title ?? alert.title ?? "On-air alert";

    const message =
        alert.Message ?? alert.message ?? "Critical concentration detected.";

    const popupHtml =
        buildPopupHtml(
            {
                title:
                    "🚨 FULL ALERT CONFIRMED",

                description:
                    `${title} • ` +
                    `${confirmationCount}/${requiredCount} ` +
                    `distinct devices • ` +
                    `declared at ${fmtTime(declaredAtUtc)} • ` +
                    `expires at ${fmtTime(expiresAtUtc)}`
            },
            s
        );

    const icon = L.divIcon({
        className: "oz-antenna-alert-marker",
        html: `
            <div class="oz-antenna-alert-ring" style="--oz-ant-alert-size:${size}px">
                <div class="oz-antenna-alert-pulse"></div>
                <div class="oz-antenna-alert-core">
                    <div class="oz-antenna-alert-count">${active}</div>
                    <div class="oz-antenna-alert-label">ALERT</div>
                </div>
            </div>
        `.trim(),
        iconSize: [size, size],
        iconAnchor: [size / 2, size / 2],
        popupAnchor: [0, -(size / 2)]
    });

    let marker = s.antennaAlertMarkers.get(key);

    if (marker) {
        try { marker.setLatLng([ll.lat, ll.lng]); } catch { }
        try { marker.setIcon(icon); } catch { }
        try {
            if (marker.getPopup()) marker.setPopupContent(popupHtml);
            else safeBindPopup(marker, popupHtml);
        } catch { }
    } else {
        marker = L.marker([ll.lat, ll.lng], {
            icon,
            title,
            pane: "ozAntennaAlertPane",
            riseOnHover: true,
            zIndexOffset: 100000,
            __ozNoCluster: true
        });

        safeBindPopup(marker, popupHtml);

        marker.addTo(s.map);
        s.antennaAlertMarkers.set(key, marker);
    }

    return true;
}

export function removeAntennaAlertCircle(antennaId, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s } = ready;

    const key = String(antennaId).startsWith("antenna-alert:")
        ? String(antennaId)
        : `antenna-alert:${antennaId}`;

    const marker = s.antennaAlertMarkers?.get(key);
    if (!marker) return true;

    try { s.map.removeLayer(marker); } catch { }

    s.antennaAlertMarkers.delete(key);

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
        const level = 1;
        const icon = buildMarkerIcon(L, level, { kind: "place", iconOverride: "🏰" });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Place: ${title}`,
            riseOnHover: true,
            pane: "markerPane",
            __ozNoCluster: true
        });

        safeBindPopup(m, buildPopupHtml({
            title,
            description: item?.Description ?? item?.description ?? ""
        }, s));

        layer.addLayer(m);
        s.detailMarkers.set(key, m);

        return;
    }

    // --- Event
    if (kindLower === "event") {
        const level = 2;
        const icon = buildMarkerIcon(L, level, { kind: "event", iconOverride: "🎪" });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Event: ${title}`,
            riseOnHover: true,
            pane: "markerPane",
            __ozNoCluster: true
        });

        safeBindPopup(m, buildPopupHtml({
            title,
            description: item?.Description ?? item?.description ?? ""
        }, s));

        layer.addLayer(m);
        s.detailMarkers.set(key, m);

        return;
    }

    // --- Crowd
    if (kindLower === "crowd") {
        const level = clampLevel14(item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level ?? 2);
        const icon = buildMarkerIcon(L, level, { kind: "crowd" });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Crowd: ${title}`,
            riseOnHover: true,
            pane: "markerPane",
            __ozNoCluster: true
        });

        safeBindPopup(m, buildPopupHtml({
            title,
            description: item?.Message ?? item?.message ?? ""
        }, s));

        layer.addLayer(m);
        s.detailMarkers.set(key, m);

        return;
    }

    // --- Traffic
    if (kindLower === "traffic") {
        const level = clampLevel14(item?.TrafficLevel ?? item?.trafficLevel ?? item?.CongestionLevel ?? item?.level ?? 2);
        const icon = buildMarkerIcon(L, level, {
            kind: "traffic",
            isTraffic: true,
            iconOverride: "🚗"
        });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Traffic: ${title}`,
            riseOnHover: true,
            pane: "markerPane",
            __ozNoCluster: true
        });

        safeBindPopup(m, buildPopupHtml({
            title,
            description: item?.Description ?? item?.description ?? ""
        }, s));

        layer.addLayer(m);
        s.detailMarkers.set(key, m);

        return;
    }

    // --- Suggestion
    if (kindLower === "suggestion") {
        const level = 2;
        const icon = buildMarkerIcon(L, level, {
            kind: "suggestion",
            iconOverride: "💡"
        });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Suggestion: ${title}`,
            riseOnHover: true,
            pane: "markerPane",
            __ozNoCluster: true
        });

        const popupDesc = [
            item?.Reason ? `Reason: ${item.Reason}` : null,
            item?.OriginalPlace ? `From: ${item.OriginalPlace}` : null,
            item?.SuggestedAlternatives ? `Alt: ${item.SuggestedAlternatives}` : null,
            (item?.DistanceKm != null) ? `Distance: ${item.DistanceKm} km` : null,
        ].filter(Boolean).join(" • ");

        safeBindPopup(m, buildPopupHtml({
            title,
            description: popupDesc
        }, s));

        const sid = Number(item?.SuggestionId ?? item?.suggestionId ?? item?.Id ?? item?.id);
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

        applyWarningStateToMarker(m, {
            warn: shouldWarnMarker("suggestion", level, item, scopeKey),
            isCalendar: false
        });
        return;
    }

    // --- Generic fallback
    {
        const level = 1;
        const icon = buildMarkerIcon(L, level, { kind: kindLower });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `${kindLower}: ${title}`,
            riseOnHover: true,
            pane: "markerPane",
            __ozNoCluster: true
        });

        safeBindPopup(m, buildPopupHtml({
            title,
            description: item?.Description ?? item?.description ?? ""
        }, s));

        layer.addLayer(m);
        s.detailMarkers.set(key, m);

        applyWarningStateToMarker(m, {
            warn: shouldWarnMarker(kindLower, level, item, scopeKey),
            isCalendar: false
        });
    }
}

export function addOrUpdateDetailMarkers(
    payload,
    scopeKey = null) {

    const ready =
        ensureMapReady(scopeKey);

    if (!ready) {
        return false;
    }

    const { k, s, L } = ready;

    clearDetailMarkers(s);

    const norm =
        normalizePayload(payload);

    const push = (items, kind) => {
        if (!Array.isArray(items)) {
            return;
        }

        for (const item of items) {
            addDetailMarker(
                kind,
                item,
                s,
                L,
                k
            );
        }
    };

    push(norm.events, "event");
    push(norm.places, "place");
    push(norm.crowds, "crowd");
    push(norm.traffic, "traffic");
    push(norm.weather, "weather");
    push(norm.suggestions, "suggestion");
    push(norm.gpt, "gpt");

    console.log(
        "[OutZen details] markers created",
        {
            scopeKey: k,
            events: norm.events.length,
            places: norm.places.length,
            crowds: norm.crowds.length,
            traffic: norm.traffic.length,
            weather: norm.weather.length,
            suggestions: norm.suggestions.length,
            gpt: norm.gpt.length,
            markerCount:
                s.detailMarkers.size
        }
    );

    return true;
}

function showSelectedBundleDetails(
    bundle,
    scopeKey = null) {

    const ready =
        ensureMapReady(scopeKey);

    if (!ready || !bundle) {
        return false;
    }

    const { k, s, L, map } =
        ready;

    /*
     * Clear the old details.
     */
    clearDetailMarkers(s);

    /*
     * Hide all bundles.
     */
    for (const bundleMarker
        of s.bundleMarkers.values()) {

        try {
            if (map.hasLayer(bundleMarker)) {
                map.removeLayer(bundleMarker);
            }
        }
        catch {
        }
    }

    const addItems = (items, kind) => {
        if (!Array.isArray(items)) {
            return;
        }

        for (const item of items) {
            addDetailMarker(
                kind,
                item,
                s,
                L,
                k
            );
        }
    };

    /*
     * Here, places are also added
     * on the homepage.
     */
    addItems(bundle.events, "event");
    addItems(bundle.places, "place");
    addItems(bundle.crowds, "crowd");
    addItems(bundle.traffic, "traffic");
    addItems(bundle.weather, "weather");
    addItems(bundle.suggestions, "suggestion");
    addItems(bundle.gpt, "gpt");

    s.flags.userLockedMode = true;
    s.hybrid.enabled = true;
    s.hybrid.showing = "details";
    s.activeDetailBundleKey =
        bundle.key ?? null;

    const positions = [];

    for (const detailMarker
        of s.detailMarkers.values()) {

        try {
            const latLng =
                detailMarker.getLatLng?.();

            if (latLng) {
                positions.push(latLng);
            }
        }
        catch {
        }
    }

    if (positions.length === 0) {
        console.warn(
            "[Bundle details] no marker created",
            {
                scopeKey: k,
                bundleKey: bundle.key,
                bundle
            }
        );

        /*
         * Do not leave the card blank.
         */
        s.flags.userLockedMode = false;
        s.hybrid.showing = "bundles";

        for (const bundleMarker
            of s.bundleMarkers.values()) {

            try {
                if (!map.hasLayer(bundleMarker)) {
                    map.addLayer(bundleMarker);
                }
            }
            catch {
            }
        }

        return false;
    }

    try {
        if (positions.length === 1) {
            map.setView(
                positions[0],
                Math.max(
                    Number(s.hybrid.threshold) || 14,
                    15
                ),
                {
                    animate: true
                }
            );
        }
        else {
            const bounds =
                L.latLngBounds(positions);

            map.fitBounds(
                bounds,
                {
                    padding: [45, 45],
                    maxZoom: 16,
                    animate: true
                }
            );
        }
    }
    catch (error) {
        console.warn(
            "[Bundle details] fit failed",
            error
        );

        map.setView(
            [
                Number(bundle.lat),
                Number(bundle.lng)
            ],
            15,
            {
                animate: false
            }
        );
    }

    console.log(
        "[Bundle details] visible",
        {
            scopeKey: k,
            bundleKey:
                bundle.key,

            markerCount:
                s.detailMarkers.size,

            zoom:
                map.getZoom?.(),

            places:
                bundle.places?.length ?? 0,

            events:
                bundle.events?.length ?? 0,

            crowds:
                bundle.crowds?.length ?? 0,

            traffic:
                bundle.traffic?.length ?? 0,

            weather:
                bundle.weather?.length ?? 0
        }
    );

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
        const zoom =
            Number(map.getZoom?.()) || 0;

        const threshold =
            Number(s.hybrid.threshold) || 14;

        /*
         * If the user drops back below the threshold,
         * resume normal hybrid behavior.
         */
        if (zoom < threshold - 1) {
            s.flags.userLockedMode = false;
            s.activeDetailBundleKey = null;

            switchToBundles(
                s,
                map
            );

            return;
        }

        /*
         * Keep the selected details.
         * Do not rebuild the global details.
         */
        if (s.hybrid.showing !== "details") {
            s.hybrid.showing = "details";
        }

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
        const h = throttleOZ(() => {
            const zoom = s.map?.getZoom?.() ?? 12;
            const threshold = Number(s.hybrid.threshold) || 13;

            if (!s.flags?.userLockedMode &&
                zoom < threshold &&
                s.bundleLastInput) {

                const nextTolerance = bundleToleranceForZoom(zoom);

                if (nextTolerance !== s.bundleToleranceMeters) {
                    addOrUpdateBundleMarkers(
                        s.bundleLastInput,
                        0,
                        k);
                }
            }

            refreshHybridVisibility(k);
        }, 180);
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
function bundleToleranceForZoom(zoom) {
    const z =
        Number(zoom) || 12;

    if (z <= 7) return 20_000;
    if (z === 8) return 10_000;
    if (z === 9) return 5_000;
    if (z === 10) return 2_500;
    if (z === 11) return 1_200;
    if (z === 12) return 600;
    if (z === 13) return 300;
    if (z === 14) return 150;

    return 80;
}
function haversineMeters(lat1, lng1, lat2, lng2) {
    const earthRadiusMeters = 6_371_000;
    const toRadians = value => value * Math.PI / 180;

    const dLat = toRadians(lat2 - lat1);
    const dLng = toRadians(lng2 - lng1);

    const a =
        Math.sin(dLat / 2) ** 2 +
        Math.cos(toRadians(lat1)) *
        Math.cos(toRadians(lat2)) *
        Math.sin(dLng / 2) ** 2;

    return 2 * earthRadiusMeters * Math.asin(Math.sqrt(a));
}

function flattenBundlePayload(payload) {
    const points = [];

    const append = (items, kind) => {
        if (!Array.isArray(items)) return;

        for (const item of items) {
            const ll = pickLatLng(item);
            if (!ll) continue;

            points.push({
                kind,
                item,
                lat: ll.lat,
                lng: ll.lng
            });
        }
    };

    append(payload.events, "events");
    append(payload.places, "places");
    append(payload.crowds, "crowds");
    append(payload.traffic, "traffic");
    append(payload.weather, "weather");
    append(payload.suggestions, "suggestions");
    append(payload.gpt, "gpt");

    // A stable order limits the unnecessary recreation of bundles.
    points.sort((a, b) =>
        a.lat - b.lat ||
        a.lng - b.lng ||
        a.kind.localeCompare(b.kind));

    return points;
}

function computeBundles(payload, tolMeters) {
    const radiusMeters = Math.max(25, Number(tolMeters) || 80);
    const referenceLatitude = 50.5;

    const metersPerDegreeLatitude = 111_320;
    const metersPerDegreeLongitude =
        111_320 * Math.cos(referenceLatitude * Math.PI / 180);

    const points = flattenBundlePayload(payload);
    const grid = new Map();
    const bundles = [];

    const getCell = (lat, lng) => {
        const y = Math.floor(
            lat * metersPerDegreeLatitude / radiusMeters);

        const x = Math.floor(
            lng * metersPerDegreeLongitude / radiusMeters);

        return { x, y, key: `${x}:${y}` };
    };

    const addBundleToCell = (bundle, cellKey) => {
        let set = grid.get(cellKey);

        if (!set) {
            set = new Set();
            grid.set(cellKey, set);
        }

        set.add(bundle);
        bundle._cellKey = cellKey;
    };

    const removeBundleFromCell = bundle => {
        const set = grid.get(bundle._cellKey);
        if (!set) return;

        set.delete(bundle);

        if (set.size === 0) {
            grid.delete(bundle._cellKey);
        }
    };

    const createBundle = (point, index) => {
        const id =
            point.item?.Id ??
            point.item?.id ??
            point.item?.EventId ??
            point.item?.CrowdInfoId ??
            point.item?.TrafficConditionId ??
            point.item?.WeatherForecastId ??
            index;

        const bundle = {
            key:
                `bundle:${point.kind}:${id}:` +
                `${Math.round(point.lat * 100_000)}:` +
                `${Math.round(point.lng * 100_000)}`,

            lat: point.lat,
            lng: point.lng,

            events: [],
            places: [],
            crowds: [],
            traffic: [],
            weather: [],
            suggestions: [],
            gpt: [],

            _sumLat: 0,
            _sumLng: 0,
            _pointCount: 0,
            _cellKey: null
        };

        bundles.push(bundle);

        const cell = getCell(point.lat, point.lng);
        addBundleToCell(bundle, cell.key);

        return bundle;
    };

    const addPointToBundle = (bundle, point) => {
        bundle[point.kind].push(point.item);

        bundle._sumLat += point.lat;
        bundle._sumLng += point.lng;
        bundle._pointCount++;

        bundle.lat = bundle._sumLat / bundle._pointCount;
        bundle.lng = bundle._sumLng / bundle._pointCount;

        const newCell = getCell(bundle.lat, bundle.lng);

        if (newCell.key !== bundle._cellKey) {
            removeBundleFromCell(bundle);
            addBundleToCell(bundle, newCell.key);
        }
    };

    points.forEach((point, index) => {
        const cell = getCell(point.lat, point.lng);
        const candidates = new Set();

        // A distance less than the radius implies a neighboring cell.
        // or the current cell.
        for (let dy = -1; dy <= 1; dy++) {
            for (let dx = -1; dx <= 1; dx++) {
                const candidateCell =
                    grid.get(`${cell.x + dx}:${cell.y + dy}`);

                if (!candidateCell) continue;

                for (const bundle of candidateCell) {
                    candidates.add(bundle);
                }
            }
        }

        let selectedBundle = null;
        let selectedDistance = Number.POSITIVE_INFINITY;

        for (const bundle of candidates) {
            const distance = haversineMeters(
                point.lat,
                point.lng,
                bundle.lat,
                bundle.lng);

            if (distance <= radiusMeters &&
                distance < selectedDistance) {

                selectedBundle = bundle;
                selectedDistance = distance;
            }
        }

        selectedBundle ??= createBundle(point, index);
        addPointToBundle(selectedBundle, point);
    });

    const result = new Map();

    for (const bundle of bundles) {
        delete bundle._sumLat;
        delete bundle._sumLng;
        delete bundle._pointCount;
        delete bundle._cellKey;

        result.set(bundle.key, bundle);
    }

    return result;
}

function pickCrowdLevel(item) {
    return clampLevel14(item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level);
}
function pickTrafficLevel(item) {
    return clampLevel14(item?.TrafficLevel ?? item?.trafficLevel ?? item?.CongestionLevel ?? item?.level);
}
function pickWeatherLevel(item) {
    const severe = !!(item?.IsSevere ?? item?.isSevere);

    return severe ? 4 : 1;
}

function pickEventLevel(item) {
    const expectedCrowd = readNumber(
        item,
        "ExpectedCrowd",
        "expectedCrowd");

    if (expectedCrowd == null) return 1;
    if (expectedCrowd >= 50_000) return 4;
    if (expectedCrowd >= 10_000) return 3;
    if (expectedCrowd >= 1_000) return 2;

    return 1;
}
function bundleSeverity(b) {
    let severity = 1;

    for (const crowd of b?.crowds ?? []) {
        severity = Math.max(
            severity,
            pickCrowdLevel(crowd));
    }

    for (const traffic of b?.traffic ?? []) {
        severity = Math.max(
            severity,
            pickTrafficLevel(traffic));
    }

    for (const weather of b?.weather ?? []) {
        severity = Math.max(
            severity,
            pickWeatherLevel(weather));
    }

    for (const event of b?.events ?? []) {
        severity = Math.max(
            severity,
            pickEventLevel(event));
    }

    return severity;
}
function bundleTotal(b) {
    const arrs = ["events", "places", "crowds", "traffic", "weather", "suggestions"/*, "gpt"*/];
    let total = 0;
    for (const k of arrs) total += (b?.[k]?.length ?? 0);
    return total;
}
function makeBadgeIcon(totalCount, severity = 1, b = null, zoom = 12) {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return null;

    const categories = [
        {
            key: "events",
            icon: "🎪",
            label: "Événements",
            count: b?.events?.length ?? 0
        },
        {
            key: "places",
            icon: "📍",
            label: "Lieux",
            count: b?.places?.length ?? 0
        },
        {
            key: "crowds",
            icon: "👥",
            label: "Affluence",
            count: b?.crowds?.length ?? 0
        },
        {
            key: "traffic",
            icon: "🚗",
            label: "Trafic",
            count: b?.traffic?.length ?? 0
        },
        {
            key: "weather",
            icon: "🌦️",
            label: "Météo",
            count: b?.weather?.length ?? 0
        },
        {
            key: "suggestions",
            icon: "💡",
            label: "Suggestions",
            count:
                (b?.suggestions?.length ?? 0) +
                (b?.gpt?.length ?? 0)
        }
    ].filter(x => x.count > 0);

    const numericZoom =
        Number(zoom) || 12;

    const compact =
        numericZoom <= 8;

    const medium =
        numericZoom >= 9 &&
        numericZoom <= 11;

    const maxDisplayedCategories =
        compact
            ? 0
            : medium
                ? 2
                : 4;

    const displayedCategories =
        categories.slice(
            0,
            maxDisplayedCategories
        );

    const hiddenCategoryCount =
        compact
            ? 0
            : Math.max(
                0,
                categories.length -
                displayedCategories.length
            );

    const displayClass =
        compact
            ? "oz-bundle-icon--compact"
            : medium
                ? "oz-bundle-icon--medium"
                : "oz-bundle-icon--full";

    const categoryHtml = displayedCategories
        .map(category => `
            <span class="oz-zone-type oz-zone-type--${category.key}"
                  title="${category.label}: ${category.count}">
                <span>${category.icon}</span>
                <strong>${category.count}</strong>
            </span>
        `)
        .join("");

    const moreHtml = hiddenCategoryCount > 0
        ? `<span class="oz-zone-more">+${hiddenCategoryCount}</span>`
        : "";

    const criticalCrowdCount =
        (b?.crowds ?? [])
            .filter(crowd =>
                pickCrowdLevel(crowd) >= 4
            )
            .length;

    const hasCriticalCrowd =
        criticalCrowdCount > 0;

    const criticalCrowdClass =
        hasCriticalCrowd
            ? "oz-bundle-icon--critical-crowd"
            : "";

    const html = `
        <div class="oz-zone-marker oz-zone-sev-${severity}">
            <div class="oz-zone-count">${totalCount}</div>

            <div class="oz-zone-types">
                ${categoryHtml}
                ${moreHtml}
            </div>
        </div>
    `.trim();

    return Leaflet.divIcon({
        className:
            `oz-bundle-icon ${displayClass} ${criticalCrowdClass}`.trim(),
        html,
        iconSize: [48, 48],
        iconAnchor: [24, 24],
        popupAnchor: [0, -26]
    });
}

function fmtDate(v) {
    if (!v) return "—";
    try {
        const d = new Date(v);
        if (Number.isNaN(d.getTime())) return String(v);
        return d.toLocaleString("fr-BE", {
            year: "numeric",
            month: "2-digit",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit"
        });
    } catch {
        return String(v);
    }
}

function fmtTime(v) {
    if (!v) return "—";
    try {
        const d = new Date(v);
        if (Number.isNaN(d.getTime())) return String(v);
        return d.toLocaleTimeString("fr-BE", {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
    } catch {
        return String(v);
    }
}

function firstSingleBundleItem(b) {
    const groups = [
        ["place", b?.places],
        ["weather", b?.weather],
        ["event", b?.events],
        ["crowd", b?.crowds],
        ["traffic", b?.traffic],
        ["suggestion", b?.suggestions]
        /*["gpt", b?.gpt]*/
    ];

    const found = groups
        .filter(([, arr]) => Array.isArray(arr) && arr.length > 0);

    if (found.length !== 1) return null;
    if (found[0][1].length !== 1) return null;

    return {
        kind: found[0][0],
        item: found[0][1][0]
    };
}

function readFirst(item, ...propertyNames) {
    for (const propertyName of propertyNames) {
        const value = item?.[propertyName];

        if (value !== null &&
            value !== undefined &&
            String(value).trim() !== "") {

            return value;
        }
    }

    return null;
}

function readNumber(item, ...propertyNames) {
    const value = readFirst(item, ...propertyNames);

    if (value === null) return null;

    const number = Number(
        typeof value === "string"
            ? value.replace(",", ".")
            : value);

    return Number.isFinite(number) ? number : null;
}

function maximumNumber(items, ...propertyNames) {
    const values = items
        .map(item => readNumber(item, ...propertyNames))
        .filter(Number.isFinite);

    return values.length > 0 ? Math.max(...values) : null;
}

function minimumNumber(items, ...propertyNames) {
    const values = items
        .map(item => readNumber(item, ...propertyNames))
        .filter(Number.isFinite);

    return values.length > 0 ? Math.min(...values) : null;
}

function uniqueTexts(items, ...propertyNames) {
    return [
        ...new Set(
            items
                .map(item => readFirst(item, ...propertyNames))
                .filter(Boolean)
                .map(String)
                .map(value => value.trim())
                .filter(Boolean)
        )
    ];
}

function levelLabel(level) {
    const numericLevel = clampLevel14(level);

    return numericLevel === 4 ? "Critique" :
        numericLevel === 3 ? "Élevé" :
            numericLevel === 2 ? "Modéré" :
                "Faible";
}

function bundleRiskLabel(severity) {
    return severity >= 4 ? "Critique" :
        severity === 3 ? "Attention" :
            severity === 2 ? "Surveillance" :
                "Normal";
}

function bundleZoneName(b) {
    const orderedItems = [
        ...(b.places ?? []),
        ...(b.crowds ?? []),
        ...(b.traffic ?? []),
        ...(b.events ?? []),
        ...(b.weather ?? [])
    ];

    for (const item of orderedItems) {
        const name = readFirst(
            item,
            "LocationName",
            "locationName",
            "PlaceName",
            "placeName",
            "Location",
            "location",
            "Road",
            "road",
            "RoadName",
            "roadName",
            "Name",
            "name");

        if (name) return String(name);
    }

    return "zone sélectionnée";
}

function buildZoneCard({
    type,
    icon,
    title,
    count,
    headline,
    details
}) {
    return `
        <section class="oz-zone-card oz-zone-card--${type}">
            <div class="oz-zone-card-icon">${icon}</div>

            <div class="oz-zone-card-content">
                <div class="oz-zone-card-head">
                    <strong>${title}</strong>
                    <span class="oz-zone-card-count">${count}</span>
                </div>

                <div class="oz-zone-card-headline">${headline}</div>

                ${details
            ? `<div class="oz-zone-card-details">${details}</div>`
            : ""}
            </div>
        </section>
    `.trim();
}
function buildSingleBundlePopupHtml(kind, item, s) {
    const esc = s.utils.escapeHtml;

    if (kind === "place") {
        const name = item?.Name ?? item?.name ?? "Place";
        const type = item?.Type ?? item?.type ?? "—";
        const indoor = item?.Indoor ?? item?.indoor ?? item?.IsIndoor ?? item?.isIndoor;
        const capacity = item?.Capacity ?? item?.capacity ?? item?.MaxCapacity ?? item?.maxCapacity;
        const tag = item?.Tag ?? item?.tag ?? item?.Tags ?? item?.tags ?? "—";

        return `
        <div class="oz-bundle-popup oz-bundle-popup--single">
            <div class="oz-bundle-title">${esc(name)}</div>
            <div class="oz-bundle-sub">
                ${esc(type)} ${indoor === true ? "(indoor)" : indoor === false ? "(outdoor)" : ""}
                ${capacity != null ? ` • Cap: ${esc(capacity)}` : ""}
                ${tag ? ` • Tag: ${esc(tag)}` : ""}
            </div>
        </div>`.trim();
    }

    if (kind === "weather") {
        const title = item?.Summary ?? item?.summary ?? item?.WeatherType ?? item?.weatherType ?? "Generated";
        const temp = item?.TemperatureC ?? item?.temperatureC ?? "—";
        const hum = item?.Humidity ?? item?.humidity ?? "—";
        const wind = item?.WindSpeedKmh ?? item?.windSpeedKmh ?? item?.WindSeedKmh ?? item?.windSeedKmh ?? "—";
        const rain = item?.RainfallMm ?? item?.rainfallMm ?? "—";
        const date = item?.DateWeather ?? item?.dateWeather ?? item?.CreatedAt ?? item?.createdAt;

        return `
        <div class="oz-bundle-popup oz-bundle-popup--single">
            <div class="oz-bundle-title">${esc(title)}</div>
            <div class="oz-bundle-sub">
                Temp: ${esc(temp)}°C • Hum: ${esc(hum)}% • Vent: ${esc(wind)} km/h • Pluie: ${esc(rain)} mm
            </div>
            <div class="oz-bundle-coords">Maj ${esc(fmtTime(date))}</div>
        </div>`.trim();
    }

    if (kind === "event") {
        const name = item?.Name ?? item?.name ?? item?.Title ?? item?.title ?? "Local Event";
        const date = item?.DateEvent ?? item?.dateEvent ?? item?.StartDate ?? item?.startDate;
        const expectedCrowd = item?.ExpectedCrowd ?? item?.expectedCrowd ?? "—";
        const isOutdoor = item?.IsOutdoor ?? item?.isOutdoor;

        return `
        <div class="oz-bundle-popup oz-bundle-popup--single">
            <div class="oz-bundle-title">${esc(name)}</div>
            <div class="oz-bundle-sub">
                ${esc(fmtDate(date))} • ExpectedCrowd: ${esc(expectedCrowd)}
                ${isOutdoor === true ? " • Outdoor" : isOutdoor === false ? " • Indoor" : ""}
            </div>
        </div>`.trim();
    }

    if (kind === "crowd") {
        const location = item?.LocationName ?? item?.locationName ?? item?.Name ?? item?.name ?? "Crowd info";
        const timestamp = item?.Timestamp ?? item?.timestamp ?? item?.CreatedAt ?? item?.createdAt;
        const level = item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level ?? "—";

        return `
        <div class="oz-bundle-popup oz-bundle-popup--single">
            <div class="oz-bundle-title">${esc(location)}</div>
            <div class="oz-bundle-sub">CrowdLevel: ${esc(level)}</div>
            <div class="oz-bundle-coords">Maj ${esc(fmtTime(timestamp))}</div>
        </div>`.trim();
    }

    if (kind === "traffic") {
        const incident = item?.IncidentType ?? item?.incidentType ?? item?.Description ?? item?.description ?? "Traffic condition";
        const location = item?.Location ?? item?.location ?? item?.RoadName ?? item?.roadName ?? "—";
        const congestion = item?.CongestionLevel ?? item?.congestionLevel ?? item?.TrafficLevel ?? item?.trafficLevel ?? "—";
        const date = item?.DateCondition ?? item?.dateCondition ?? item?.Timestamp ?? item?.timestamp;

        return `
        <div class="oz-bundle-popup oz-bundle-popup--single">
            <div class="oz-bundle-title">${esc(incident)}</div>
            <div class="oz-bundle-sub">
                ${esc(congestion)} • ${esc(location)} • ${esc(fmtDate(date))}
            </div>
        </div>`.trim();
    }

    if (kind === "suggestion") {
        const title =
            item?.Title ??
            item?.title ??
            item?.SuggestedAlternatives ??
            item?.suggestedAlternatives ??
            item?.LocationLabel ??
            item?.locationLabel ??
            "Suggestion";

        const original =
            item?.OriginalPlace ??
            item?.originalPlace ??
            "—";

        const reason =
            item?.Reason ??
            item?.reason ??
            "—";

        const distance =
            item?.DistanceKm ??
            item?.distanceKm ??
            null;

        return `
    <div class="oz-bundle-popup oz-bundle-popup--single">
        <div class="oz-bundle-title">${esc(title)}</div>
        <div class="oz-bundle-sub">
            From: ${esc(original)} • Reason: ${esc(reason)}
            ${distance != null ? ` • Distance: ${esc(distance)} km` : ""}
        </div>
    </div>`.trim();
    }

    return null;
}
function bundlePopupHtml(b, s) {
    const single = firstSingleBundleItem(b);

    if (single) {
        const html =
            buildSingleBundlePopupHtml(
                single.kind,
                single.item,
                s);

        if (html) return html;
    }

    const esc = s.utils.escapeHtml;
    const total = bundleTotal(b);
    const severity = bundleSeverity(b);
    const cards = [];

    const zoneName = bundleZoneName(b);

    // -----------------------------------------------------
    // Events
    // -----------------------------------------------------
    if ((b.events?.length ?? 0) > 0) {
        const events = [...b.events];

        events.sort((a, b) => {
            const dateA = new Date(
                readFirst(a,
                    "DateEvent",
                    "dateEvent",
                    "StartDate",
                    "startDate") ?? 0);

            const dateB = new Date(
                readFirst(b,
                    "DateEvent",
                    "dateEvent",
                    "StartDate",
                    "startDate") ?? 0);

            return dateA.getTime() - dateB.getTime();
        });

        const nextEvent = events[0];

        const eventName = readFirst(
            nextEvent,
            "Name",
            "name",
            "Title",
            "title") ?? "Événement";

        const eventDate = readFirst(
            nextEvent,
            "DateEvent",
            "dateEvent",
            "StartDate",
            "startDate");

        const expectedCrowd = maximumNumber(
            events,
            "ExpectedCrowd",
            "expectedCrowd");

        cards.push(buildZoneCard({
            type: "events",
            icon: "🎪",
            title: "Événements",
            count: events.length,
            headline: esc(eventName),
            details: [
                eventDate
                    ? `Prochain : ${esc(fmtDate(eventDate))}`
                    : null,

                expectedCrowd !== null
                    ? `Affluence attendue max. : ${expectedCrowd.toLocaleString("fr-BE")}`
                    : null
            ].filter(Boolean).join(" • ")
        }));
    }

    // -----------------------------------------------------
    // Crowd
    // -----------------------------------------------------
    if ((b.crowds?.length ?? 0) > 0) {
        const crowdLevel = maximumNumber(
            b.crowds,
            "CrowdLevel",
            "crowdLevel",
            "Level",
            "level");

        cards.push(buildZoneCard({
            type: "crowds",
            icon: "👥",
            title: "Affluence",
            count: b.crowds.length,
            headline:
                `Niveau maximal : ${esc(levelLabel(crowdLevel ?? 1))}`,
            details:
                `${b.crowds.length} observation(s) active(s)`
        }));
    }

    // -----------------------------------------------------
    // Traffic
    // -----------------------------------------------------
    if ((b.traffic?.length ?? 0) > 0) {
        const trafficLevel = maximumNumber(
            b.traffic,
            "TrafficLevel",
            "trafficLevel",
            "CongestionLevel",
            "congestionLevel",
            "Level",
            "level");

        const roads = uniqueTexts(
            b.traffic,
            "Road",
            "road",
            "RoadName",
            "roadName",
            "Location",
            "location");

        const incidents = uniqueTexts(
            b.traffic,
            "IncidentType",
            "incidentType",
            "Title",
            "title");

        cards.push(buildZoneCard({
            type: "traffic",
            icon: "🚗",
            title: "Trafic",
            count: b.traffic.length,
            headline:
                `Congestion maximale : ${esc(levelLabel(trafficLevel ?? 1))}`,
            details: [
                roads.length > 0
                    ? `Route(s) : ${esc(roads.slice(0, 3).join(", "))}`
                    : null,

                incidents.length > 0
                    ? `Incident(s) : ${esc(incidents.slice(0, 3).join(", "))}`
                    : null
            ].filter(Boolean).join(" • ")
        }));
    }

    // -----------------------------------------------------
    // Weather
    // -----------------------------------------------------
    if ((b.weather?.length ?? 0) > 0) {
        const minimumTemperature = minimumNumber(
            b.weather,
            "TemperatureC",
            "temperatureC");

        const maximumTemperature = maximumNumber(
            b.weather,
            "TemperatureC",
            "temperatureC");

        const maximumWind = maximumNumber(
            b.weather,
            "WindSpeedKmh",
            "windSpeedKmh",
            "WindSeedKmh",
            "windSeedKmh");

        const maximumRain = maximumNumber(
            b.weather,
            "RainfallMm",
            "rainfallMm");

        const severeCount = b.weather.filter(
            item => item?.IsSevere ?? item?.isSevere).length;

        const temperatureText =
            minimumTemperature !== null &&
                maximumTemperature !== null &&
                minimumTemperature !== maximumTemperature

                ? `${minimumTemperature} à ${maximumTemperature} °C`
                : maximumTemperature !== null
                    ? `${maximumTemperature} °C`
                    : "Température inconnue";

        cards.push(buildZoneCard({
            type: "weather",
            icon: severeCount > 0 ? "⛈️" : "🌦️",
            title: "Météo",
            count: b.weather.length,
            headline: esc(temperatureText),
            details: [
                maximumWind !== null
                    ? `Vent max. : ${maximumWind} km/h`
                    : null,

                maximumRain !== null
                    ? `Pluie max. : ${maximumRain} mm`
                    : null,

                severeCount > 0
                    ? `${severeCount} prévision(s) sévère(s)`
                    : null
            ].filter(Boolean).join(" • ")
        }));
    }

    // -----------------------------------------------------
    // Places
    // -----------------------------------------------------
    if ((b.places?.length ?? 0) > 0) {
        const placeNames = uniqueTexts(
            b.places,
            "Name",
            "name",
            "Title",
            "title");

        cards.push(buildZoneCard({
            type: "places",
            icon: "📍",
            title: "Lieux",
            count: b.places.length,
            headline:
                esc(placeNames.slice(0, 3).join(", ") || "Lieux proches"),
            details: ""
        }));
    }

    return `
        <div class="oz-zone-popup">
            <header class="oz-zone-popup-header">
                <div>
                    <div class="oz-zone-popup-title">
                        Zone autour de ${esc(zoneName)}
                    </div>

                    <div class="oz-zone-popup-meta">
                        ${total} information(s) fusionnée(s)
                    </div>
                </div>

                <span class="oz-zone-risk oz-zone-risk--${severity}">
                    ${bundleRiskLabel(severity)}
                </span>
            </header>

            <div class="oz-zone-card-list">
                ${cards.join("")}
            </div>

            <div class="oz-zone-popup-actions">
                <button type="button"
                        class="oz-zone-action"
                        data-oz-action="details">
                    🔎 Zoomer et afficher les détails
                </button>
            </div>

            <div class="oz-zone-popup-coordinates">
                Centre de la zone :
                ${Number(b.lat).toFixed(5)},
                ${Number(b.lng).toFixed(5)}
            </div>
        </div>
    `.trim();
}

const BUNDLE_POPUP_WIRING_VERSION = 3;

function wireBundlePopupActions(
    marker,
    s,
    scopeKey) {

    if (!marker || !s?.map) {
        return;
    }

    if (
        marker.__ozBundlePopupWiringVersion ===
        BUNDLE_POPUP_WIRING_VERSION
    ) {
        return;
    }

    marker.__ozBundlePopupWiringVersion =
        BUNDLE_POPUP_WIRING_VERSION;

    marker.on("popupopen", () => {
        requestAnimationFrame(() => {
            const popupElement =
                marker
                    .getPopup?.()
                    ?.getElement?.();

            if (!popupElement) {
                return;
            }

            const detailsButton =
                popupElement.querySelector(
                    '[data-oz-action="details"]'
                );

            if (!detailsButton) {
                return;
            }

            try {
                globalThis.L
                    ?.DomEvent
                    ?.disableClickPropagation(
                        detailsButton
                    );
            }
            catch {
            }

            detailsButton.onclick = event => {
                event.preventDefault();
                event.stopPropagation();

                const bundle =
                    marker.__ozBundleData;

                if (!bundle) {
                    console.warn(
                        "[Bundle popup] no bundle data"
                    );

                    return;
                }

                try {
                    marker.closePopup();
                }
                catch {
                }

                showSelectedBundleDetails(
                    bundle,
                    scopeKey
                );
            };
        });
    });
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

    const icon = makeBadgeIcon(total, sev, b, map.getZoom?.() ?? 12);

    if (!existing) {
        const m = L.marker([b.lat, b.lng], {
            icon,
            title: `Zone (${total})`,
            riseOnHover: true,
            __ozNoCluster: true
        });

        m.__ozBundleData = b;

        safeBindPopup(m, popup, {
            maxWidth: 520,
            minWidth: 320
        });

        wireBundlePopupActions(m, s, scopeKey);

        if (s.hybrid?.showing !== "details") {
            addLayerSmart(m, s);
        }

        s.bundleMarkers.set(b.key, m);
        s.bundleIndex.set(b.key, b);
        return;
    }

    existing.__ozBundleData = b;

    try {
        existing.setLatLng([
            b.lat,
            b.lng
        ]);
    }
    catch {
    }

    try {
        existing.setIcon(icon);
    }
    catch {
    }

    try {
        if (existing.getPopup()) {
            existing.setPopupContent(popup);
        }
        else {
            safeBindPopup(
                existing,
                popup,
                {
                    maxWidth: 520,
                    minWidth: 320
                }
            );
        }
    }
    catch {
    }

    wireBundlePopupActions(existing, s, scopeKey
    );

    s.bundleIndex.set(b.key, b);
}

function rebuildWeatherIndex(s, weatherItems) {
    const index = new Map();

    for (const item of weatherItems ?? []) {
        const id =
            item?.Id ??
            item?.id ??
            item?.WeatherForecastId ??
            item?.weatherForecastId;

        if (id == null) {
            continue;
        }

        index.set(String(id), item);
    }

    s._weatherById = index;
}

export function addOrUpdateBundleMarkers(
    payload,
    tolMeters = 0,
    scopeKey = null) {

    const ready = ensureMapReady(scopeKey);

    if (!ready) {
        console.warn(
            "[addOrUpdateBundleMarkers] map not ready",
            { scopeKey }
        );

        return false;
    }

    /*
     * We need to retrieve k AND s.
     *
     * k = standardized scope
     * s = Leaflet state of the scope
     */
    const { k, s } = ready;

    const requestedTolerance = Number(tolMeters);
    const currentZoom =
        s.map?.getZoom?.() ?? 12;

    const tolFinal =
        Number.isFinite(requestedTolerance) &&
            requestedTolerance > 0

            ? requestedTolerance
            : bundleToleranceForZoom(currentZoom);

    s.bundleToleranceMeters = tolFinal;

    const norm = normalizePayload(payload);

    s.bundleLastInput = norm;

    /*
     * Synchronize the weather index before future
     * incremental updates.
     */
    rebuildWeatherIndex(
        s,
        norm.weather
    );

    /*
     * Single declaration.
     */
    const bundles =
        computeBundles(
            norm,
            tolFinal
        );

    /*
     * Removes bundles that have become obsolete.
     */
    for (const oldKey of Array.from(
        s.bundleMarkers.keys())) {

        if (bundles.has(oldKey)) {
            continue;
        }

        const marker =
            s.bundleMarkers.get(oldKey);

        removeLayerSmart(
            marker,
            s
        );

        s.bundleMarkers.delete(oldKey);
        s.bundleIndex.delete(oldKey);
    }

    /*
     * Adds or updates the current bundles.
     */
    for (const bundle of bundles.values()) {
        updateBundleMarker(
            bundle,
            k
        );
    }

    /*
     * Refreshes the bundles/details display.
     */
    try {
        refreshHybridVisibility(k);
    }
    catch (error) {
        console.warn(
            "[addOrUpdateBundleMarkers] " +
            "hybrid refresh failed",
            error
        );
    }

    /*
     * Updates the classic clusters if the
     * The plugin is active for this page.
     */
    if (
        s.cluster &&
        typeof s.cluster.refreshClusters === "function"
    ) {
        try {
            s.cluster.refreshClusters();
        }
        catch (error) {
            console.warn(
                "[addOrUpdateBundleMarkers] " +
                "cluster refresh failed",
                error
            );
        }
    }

    /*
     * Optional: show markers
     * Individual weather items in bundle mode.
     */
    if (
        s.flags.showWeatherPinsInBundles &&
        s.hybrid?.showing !== "details"
    ) {
        addOrUpdateWeatherMarkers(
            norm.weather ?? [],
            k
        );
    }

    console.log(
        "[addOrUpdateBundleMarkers] ok",
        {
            scopeKey: k,
            zoom: currentZoom,
            toleranceMeters: tolFinal,
            bundleCount: bundles.size,
            events: norm.events.length,
            places: norm.places.length,
            crowds: norm.crowds.length,
            traffic: norm.traffic.length,
            weather: norm.weather.length,
            suggestions: norm.suggestions.length
        }
    );

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

function applyWarningStateToMarker(marker, {
    warn = false,
    isCalendar = false
} = {}) {
    if (!marker) return;

    waitForMarkerElement(marker).then(el => {
        if (!el) return;

        el.classList.toggle("oz-marker-warning", !!warn);
        el.toggleAttribute("data-oz-warning", !!warn);

        const inner =
            el.querySelector(".oz-marker-inner") ||
            el.querySelector(".oz-ant-dot");

        if (!inner) return;

        if (isCalendar) {
            inner.classList.toggle("oz-calendar-warning-inner", !!warn);
        } else {
            inner.classList.remove("oz-calendar-warning-inner");
        }
    });
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
        try {
            const ll = m.getLatLng();
            if (!ll) continue;
            if (!isInsideBelgium(ll.lat, ll.lng, s)) {
                console.warn("[CIC][fit-skip-outside-belgium]", ll);
                continue;
            }
            latlngs.push(ll);
        } catch { }
    }

    if (!latlngs.length) {
        console.warn("[CIC][fitToCalendar] no valid Belgium markers, fallback");
        try { map.setView([50.5039, 4.4699], 8, { animate: false }); } catch { }
        return false;
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

export function fitToAntennaAlertMarkers(scopeKey = null, opts = {}) {
    const ready = ensureMapReady(scopeKey);

    if (!ready) {
        console.warn("[OutZen] fitToAntennaAlertMarkers: map not ready", scopeKey);
        return false;
    }

    const { s, L, map } = ready;

    const padding = opts.padding ?? [40, 40];
    const maxZoom = opts.maxZoom ?? 9;

    s.antennaAlertMarkers ??= new Map();

    const latlngs = [];

    for (const marker of s.antennaAlertMarkers.values()) {
        try {
            const latlng = marker.getLatLng?.();

            if (latlng) {
                latlngs.push(latlng);
            }
        } catch {
            // ignore invalid marker
        }
    }

    if (latlngs.length === 0) {
        console.warn("[OutZen] fitToAntennaAlertMarkers: no alert markers");
        return false;
    }

    try {
        const bounds = L.latLngBounds(latlngs);

        map.fitBounds(bounds, {
            padding: opts.padding ?? [40, 40],
            maxZoom: opts.maxZoom ?? 9,
            animate: true
        });

        return true;
    } catch (err) {
        console.error("[OutZen] fitToAntennaAlertMarkers failed", err);
        return false;
    }
}

export function activateHybridAndZoom(scopeKey = null, threshold = 13) {
    const k = pickScopeKey(scopeKey);
    enableHybridZoom(true, threshold, k);

    const s = peekS(k) || getS(k);
    const z = s?.map?.getZoom?.() ?? 0;
    const wantDetails = z >= threshold;

    return wantDetails ? fitToDetails(k) : fitToBundles(k);
}

export function pruneMarkersByPrefix(prefix = "", scopeKey = null) {
    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);
    if (!(s?.markers instanceof Map)) return 0;

    let removed = 0;

    for (const [key, marker] of Array.from(s.markers.entries())) {
        if (!String(key).startsWith(prefix)) continue;

        try {
            if (s.cluster?.hasLayer?.(marker)) s.cluster.removeLayer(marker);
        } catch { }

        try {
            if (s.map?.hasLayer?.(marker)) s.map.removeLayer(marker);
        } catch { }

        s.markers.delete(key);
        removed++;
    }

    try { s.cluster?.refreshClusters?.(); } catch { }

    return removed;
}

export function pruneAntennaAlertMarkers(activeIds, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);

    if (!ready) {
        console.warn("[OutZen] pruneAntennaAlertMarkers: map not ready", scopeKey);
        return 0;
    }

    const { s, map } = ready;

    s.antennaAlertMarkers ??= new Map();

    const activeSet = new Set(
        (activeIds ?? [])
            .filter(x => x !== null && x !== undefined)
            .map(x => `antenna-alert:${x}`)
    );

    let removed = 0;

    for (const [key, marker] of Array.from(s.antennaAlertMarkers.entries())) {
        if (activeSet.has(key)) {
            continue;
        }

        try {
            map.removeLayer(marker);
        } catch {
            // ignore remove failure
        }

        s.antennaAlertMarkers.delete(key);
        removed++;
    }

    return removed;
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
export function scheduleBundleRefresh(delayMs = 150, tolMeters = 0, scopeKey = null) {

    const k = pickScopeKey(scopeKey);
    const s = peekS(k) || getS(k);

    clearTimeout(s._bundleRefreshT);

    s._bundleRefreshT = setTimeout(() => {
        try {
            if (s.bundleLastInput) {
                addOrUpdateBundleMarkers(
                    s.bundleLastInput,
                    tolMeters,
                    k);
            }
        } catch {
        }
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

    for (const marker of s.bundleMarkers?.values?.() ?? []) {
        try {
            removeLayerSmart(marker, s);
        } catch {
        }
    }

    for (const marker of s.antennaAlertMarkers?.values?.() ?? []) {
        try {
            if (s.map?.hasLayer?.(marker)) {
                s.map.removeLayer(marker);
            }
        } catch {
        }
    }

    destroyChartIfAny(s);
    destroyWxChartIfAny(s);

    for (const marker of s.markers.values()) {
        clearManagedMarkerTimers(marker);
        removeLayerSmart(marker, s);
    }
    // Reset registries
    try { s.markers?.clear?.(); } catch { }
    try { s.bundleMarkers?.clear?.(); } catch { }
    try { s.bundleIndex?.clear?.(); } catch { }
    try { s.detailMarkers?.clear?.(); } catch { }
    try { s.calendarMarkers?.clear?.(); } catch { }
    try { s.antennaMarkers?.clear?.(); } catch { }
    try {s.antennaAlertMarkers?.clear?.(); } catch { }

    // Reset bundle cache (optional, but consistent if you “clear all”)
    clearTimeout(s._bundleRefreshT);

    s._bundleRefreshT = 0;
    s._weatherById = new Map();
    s.bundleLastInput = null;
    s.bundleToleranceMeters = null;
    s.hybrid.showing = null;
    s.flags.userLockedMode = false;

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

function shouldWarnMarker(kind, level, item = null, scopeKey = null) {
    const k = String(kind || "").toLowerCase();
    const lvl = normalizeLevel(level);

    switch (k) {
        case "crowd":
            return lvl >= 4;

        case "event":
            return true;

        case "suggestion":
            return true;

        case "traffic":
            return lvl >= 2;

        case "weather":
            return lvl >= 2 || !!(item?.IsSevere ?? item?.isSevere);

        case "antenna":
            return lvl >= 4;

        case "place":
            return true;

        case "calendar":
            return scopeKey === "crowdinfocalendarview" && lvl >= 2;

        default:
            return false;
    }
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