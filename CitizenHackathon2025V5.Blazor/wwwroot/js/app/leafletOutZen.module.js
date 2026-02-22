// wwwroot/js/app/leafletOutZen.module.js
/* global L, Chart */
"use strict";

/* =========================================================
   OutZen Leaflet Module (ESM) - Guarded & Hot-reload safe
   - Singleton per scopeKey: __OutZenSingleton__{scopeKey}
   - bootOutZen / disposeOutZen
   - Markers (generic, weather, event, place)
   - Calendar + Antenna markers (no cluster)
   - Bundles + Hybrid mode (bundles far, details near)
   - Incremental weather updates: upsertWeatherIntoBundleInput + scheduleBundleRefresh
   ========================================================= */

/* ---------------------------------------------------------
   Scope helpers (SAFE)
--------------------------------------------------------- */
function pickScopeKey(scopeKey) {
    return scopeKey || globalThis.__OutZenActiveScope || "main";
}

function pickBestScopeKey() {
    const a = globalThis.__OutZenActiveScope;
    if (a) {
        const sa = peekS(a);
        if (sa?.map) return a;
    }

    for (const k of Object.keys(globalThis)) {
        if (!k.startsWith("__OutZenSingleton__")) continue;
        const s = globalThis[k];
        if (s?.map) return k.replace("__OutZenSingleton__", "");
    }

    return "main";
}

function pickScopeKeyRead(scopeKey) {
    return scopeKey || pickBestScopeKey();
}
function pickScopeKeyWrite(scopeKey) {
    return scopeKey || pickBestScopeKey();
}

/* ---------------------------------------------------------
   Singleton state (REQUIRED SHAPE)
--------------------------------------------------------- */
function initState(s) {
    s.consts ??= {};
    s.utils ??= {};
    s.flags ??= {};

    s.flags.userLockedMode ??= false;
    s.flags.showBundleStats ??= false;
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
            version: "2026.02.07-clean-dev",
            initialized: false,
            bootTs: 0,

            map: null,
            mapContainerId: null,
            mapContainerEl: null,
            _domToken: null,

            cluster: null,
            layerGroup: null,

            markers: new Map(),
            placeMarkers: new Map(),
            placeIndex: new Map(),

            bundleMarkers: new Map(),
            bundleIndex: new Map(),
            bundleLastInput: null,

            detailLayer: null,
            detailMarkers: new Map(),

            calendarLayer: null,
            calendarMarkers: new Map(),

            antennaLayer: null,
            antennaMarkers: new Map(),

            hybrid: { enabled: true, threshold: 13, showing: null },
            _hybridBound: false,
            _hybridHandler: null,
            _hybridSwitching: false,

            _bundleRefreshT: 0,
            _weatherById: new Map(),

            chart: null,

            consts: {},
            utils: {},
            flags: {},

            _mapToken: 0,
            _resizeQueued: false,
            _fitLock: false,
            _invT: 0,
            _highlightT: 0,
        };

        initState(globalThis[key]);
    }

    return globalThis[key];
}

function peekS(scopeKey = "main") {
    const key = "__OutZenSingleton__" + String(scopeKey || "main");
    return globalThis[key] ?? null; // does not create anything
}

globalThis.__OutZenGetS ??= (scopeKey = "main") => getS(scopeKey);

/* ---------------------------------------------------------
   Universal guard helper (ALWAYS define after `const s = ...`)
--------------------------------------------------------- */
function makeHas(s) {
    return (layer) => !!layer && !!s.map && typeof s.map.hasLayer === "function" && s.map.hasLayer(layer);
}

/* ---------------------------------------------------------
   DOM / Leaflet helpers
--------------------------------------------------------- */
async function waitForContainer(mapId, tries = 30) {
    for (let i = 0; i < tries; i++) {
        const el = document.getElementById(mapId);
        if (el) return el;
        await new Promise((r) => requestAnimationFrame(r));
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

function resetLeafletDomId(mapId) {
    const L = ensureLeaflet();
    if (!L) return;
    const dom = L.DomUtil.get(mapId);
    if (dom && dom._leaflet_id) {
        try { delete dom._leaflet_id; } catch { dom._leaflet_id = undefined; }
    }
}

function ensureMapReady(scopeKey = null) {
    const k = pickScopeKeyRead(scopeKey);
    const s = getS(k);
    const has = makeHas(s);

    const L = ensureLeaflet();
    if (!L) return null;

    if (!s?.map) return null;

    // Note: we DON'T require detailLayer to exist to say the map is ready.
    // detailLayer is created only in details mode.
    return { k, s, has, L, map: s.map };
}

function isContainerVisible(map) {
    const el = map?.getContainer?.();
    if (!el) return false;
    const r = el.getBoundingClientRect?.();
    return !!r && r.width > 10 && r.height > 10;
}

/* ---------------------------------------------------------
   Debug exports
--------------------------------------------------------- */
export function dumpState(scopeKey = "main") {
    const k = pickScopeKeyRead(scopeKey);
    const s = getS(k);
    const has = makeHas(s);

    const safeCount = (arr) => Array.isArray(arr) ? arr.length : 0;

    return {
        loaded: true,
        scopeKey: k,
        mapId: s.mapContainerId ?? null,
        zoom: s.map?.getZoom?.() ?? null,
        hasClusterLayer: !!s.cluster,
        hasMap: !!s.map,
        initialized: !!s.initialized,
        bootTs: s.bootTs ?? 0,

        markers: s.markers?.size ?? 0,
        bundleMarkers: s.bundleMarkers?.size ?? 0,
        detailMarkers: s.detailMarkers?.size ?? 0,
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

/* ---------------------------------------------------------
   Public API: ready
--------------------------------------------------------- */
export function isOutZenReady(scopeKey = "main") {
    const s = peekS(pickScopeKeyRead(scopeKey));
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

/**
 * Boot Leaflet map
 * options: { mapId, scopeKey, center:[lat,lng], zoom, enableChart, force, enableWeatherLegend, resetMarkers, resetAll, enableHybrid, hybridThreshold }
 */
export async function bootOutZen({
    mapId,
    scopeKey = "main",
    center = [50.85, 4.35],
    zoom = 12,
    enableChart = false,
    force = false,
    enableWeatherLegend = false, // kept for compat (unused here)
    resetMarkers = false,
    resetAll = false,
    enableHybrid = true,
    enableCluster = true,
    hybridThreshold = 13,
} = {}) {
    globalThis.__OutZenActiveScope = scopeKey;
    const s = getS(scopeKey);
    const has = makeHas(s);

    const host = await waitForContainer(mapId, 30);
    if (!host) return bootFail(mapId, scopeKey, "container-not-found");

    // container changed => dispose old map
    if (s.map && s.mapContainerId && s.mapContainerId !== mapId) {
        disposeOutZen({ mapId: s.mapContainerId, scopeKey });
    }

    resetLeafletDomId(mapId);

    // already booted + not force
    if (s.map && s.mapContainerId === mapId && !force) {
        const tok = s._domToken ?? host?.dataset?.ozToken ?? null;
        return { ok: true, token: tok, mapId, scopeKey };
    }
    if (s.map && force) disposeOutZen({ mapId: s.mapContainerId, scopeKey });

    const L = ensureLeaflet();
    if (!L) return bootFail(mapId, scopeKey, "leaflet-missing");

    let map = null; // ✅ parent scope

    // Always clean the host before creating the map (cluster or not).
    try { host.replaceChildren(); } catch { host.innerHTML = ""; }

    map = L.map(host, {
        zoomAnimation: false,
        fadeAnimation: false,
        markerZoomAnimation: false,
        preferCanvas: true,
        zoomControl: true,
        trackResize: false,
        minZoom: 5,
        maxZoom: 19,
        zoomSnap: 1,
        zoomDelta: 1
    }).setView(center, zoom);

    // Cluster: optional (but the map still exists)
    if (enableCluster && L.markerClusterGroup) {
        if (!s.cluster) {
            s.cluster = L.markerClusterGroup({
                disableClusteringAtZoom: 16,
                spiderfyOnMaxZoom: true,
                showCoverageOnHover: false,
                zoomToBoundsOnClick: true
            });
            if (!map.hasLayer(s.cluster)) s.cluster.addTo(map);
        }
    } else {
        s.cluster = null;
    }

    const token = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    host.dataset.ozToken = token;
    s._domToken = token;

    console.log("[bootOutZen] map created", { mapId, scopeKey });

    s.map = map;               // ✅ map still exists
    s.mapContainerId = mapId;
    s.mapContainerEl = host;

    // base tile
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "© OpenStreetMap contributors",
        maxZoom: 19,
    }).addTo(map);

    try { map.doubleClickZoom.disable(); } catch { }

    // invalidate async ops
    s._mapToken = (s._mapToken || 0) + 1;

    // base groups
    s.calendarLayer ??= L.featureGroup();
    if (!map.hasLayer(s.calendarLayer)) s.calendarLayer.addTo(map);

    s.layerGroup ??= L.layerGroup();
    if (!map.hasLayer(s.layerGroup)) s.layerGroup.addTo(map);

    // Add cluster if present
    if (s.cluster && !map.hasLayer(s.cluster)) s.cluster.addTo(map);

    // reset logic
    if (resetAll) {
        try { s.cluster?.clearLayers?.(); } catch { }
        try { s.calendarLayer?.clearLayers?.(); } catch { }
        try { if (has(s.detailLayer)) s.detailLayer.clearLayers?.(); } catch { }

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

        s.bundleMarkers ??= new Map();
        s.bundleIndex ??= new Map();
        s.detailMarkers ??= new Map();
        s.calendarMarkers ??= new Map();
        s._weatherById ??= new Map();
    }

    // chart (optional)
    destroyChartIfAny(s);
    if (enableChart && globalThis.Chart) {
        const canvas = document.getElementById("crowdChart");
        if (canvas) {
            const ctx = canvas.getContext("2d");
            s.chart = new Chart(ctx, {
                type: "bar",
                data: { labels: [], datasets: [{ label: "Metric", data: [] }] },
                options: { responsive: true, animation: false },
            });
        }
    }

    // hybrid
    try {
        if (enableHybrid) enableHybridZoom(true, hybridThreshold, scopeKey);
        else s.hybrid.enabled = false;
    } catch (e) {
        console.warn("[bootOutZen] hybrid init failed", e);
    }

    // restore last bundle payload
    if (s.bundleLastInput) {
        try { addOrUpdateBundleMarkers(s.bundleLastInput, 80, scopeKey); } catch { }
    }

    queueMicrotask(() => {
        try { map.invalidateSize({ animate: false, debounceMoveend: true }); } catch { }
    });

    s.initialized = true;
    s.bootTs = Date.now();

    console.log("[bootOutZen] returning", { ok: true, token, mapId, scopeKey });
    return { ok: true, token, mapId, scopeKey };
}

export function getCurrentMapId(scopeKey = null) {
    const s = peekS(pickScopeKeyRead(scopeKey)) || getS(pickScopeKeyRead(scopeKey));
    return s?.mapContainerId ?? null;
}

export function disposeOutZen({ mapId, scopeKey = "main", token = null } = {}) {
    const s = getS(scopeKey);
    if (!s) return false;

    const host = s.mapContainerEl || (mapId ? document.getElementById(mapId) : null);

    // ✅ If internal call (bootOutZen): allows use without token
    // ✅ If external call: token recommended
    const currentTok = host?.dataset?.ozToken ?? s._domToken ?? null;

    const isInternal = (token == null); // <-- simple
    const tokenOk = isInternal || (token && currentTok && token === currentTok);

    if (!tokenOk) {
        console.warn("[disposeOutZen] token mismatch -> IGNORE", { mapId, scopeKey, token, currentTok });
        return false;
    }

    try { s.cluster?.clearLayers?.(); } catch { }
    try { s.layerGroup?.clearLayers?.(); } catch { }
    try { s.calendarLayer?.clearLayers?.(); } catch { }
    try { s.detailLayer?.clearLayers?.(); } catch { }

    try { s.map?.remove?.(); } catch { }
    s.map = null;
    s.mapContainerId = null;
    s.mapContainerEl = null;

    // reset markers maps
    s.markers = new Map();
    s.bundleMarkers = new Map();
    s.bundleIndex = new Map();
    s.detailMarkers = new Map();
    s.calendarMarkers = new Map();
    s._weatherById = new Map();
    s.bundleLastInput = null;
    s.initialized = false;

    if (host) {
        try { host.replaceChildren(); } catch { host.innerHTML = ""; }
        try { delete host.dataset.ozToken; } catch { }
    }
    return true;
}

/* ---------------------------------------------------------
   Layer add/remove (cluster-aware) - GUARDED
--------------------------------------------------------- */
function addLayerSmart(layer, s) {
    if (!s?.map || !layer) return;

    const looksLikeLeafletLayer =
        typeof layer.addTo === "function" ||
        typeof layer.getLatLng === "function" ||
        typeof layer.getLayers === "function";

    if (!looksLikeLeafletLayer) {
        console.error("[addLayerSmart] NOT a Leaflet layer", layer);
        return;
    }

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
        else (layer.addTo ? layer.addTo(s.map) : s.map.addLayer(layer));
    } catch (e) {
        console.warn("[addLayerSmart] failed", e);
    }
}

function removeLayerSmart(layer, s) {
    if (!s?.map || !layer) return;

    if (layer?.options?.__ozNoCluster) {
        try { s.map.removeLayer(layer); } catch { }
        return;
    }

    try {
        if (s.cluster && typeof s.cluster.removeLayer === "function") s.cluster.removeLayer(layer);
        else s.map.removeLayer(layer);
    } catch { }
}

/* ---------------------------------------------------------
   Marker icons (divIcon)
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
   Generic marker API
--------------------------------------------------------- */
function buildPopupHtml(info, s) {
    const title = info?.title ?? "Unknown";
    const desc = info?.description ?? "";
    return `<div class="outzen-popup">
    <div class="title">${s.utils.escapeHtml(title)}</div>
    <div class="desc">${s.utils.escapeHtml(desc)}</div>
  </div>`;
}
function toNumLoose(v) {
    if (v == null) return null;
    if (typeof v === "string") v = v.replace(",", ".");
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
}
export function addOrUpdateCrowdMarker(id, lat, lng, level, info, scopeKey = null) {
    console.log("[OZ] addOrUpdateCrowdMarker", { scopeKey, id, lat, lng, level, kind: info?.kind });
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;

    const latNum = toNumLoose(lat);
    const lngNum = toNumLoose(lng);
    if (latNum == null || lngNum == null) {
        console.warn("[addOrUpdateCrowdMarker] drop invalid coords", { id, lat, lng });
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

export function addOrUpdatePlaceMarker(place, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { k, s } = ready;

    const ll = pickLatLng(place, s.utils);
    if (!ll) return false;

    const id = `place:${place?.Id ?? place?.id}`;
    return addOrUpdateCrowdMarker(id, ll.lat, ll.lng, 1, {
        kind: "place",
        title: place?.Name ?? place?.name ?? "Place",
        description: place?.Description ?? place?.description ?? "",
        icon: "🏰"
    }, k);
}

export function addOrUpdateEventMarker(ev, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { k, s } = ready;

    const ll = pickLatLng(ev, s.utils);
    if (!ll) return false;

    const id = `event:${ev?.Id ?? ev?.id}`;
    return addOrUpdateCrowdMarker(id, ll.lat, ll.lng, 2, {
        kind: "event",
        title: ev?.Title ?? ev?.Name ?? ev?.title ?? ev?.name ?? "Event",
        description: ev?.Description ?? ev?.description ?? "",
        icon: "🎪"
    }, k);
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

/* ---------------------------------------------------------
   Calendar markers (no cluster)
--------------------------------------------------------- */
function ensureCalendarLayer(s, L) {
    if (!s.calendarLayer) {
        s.calendarLayer = L.featureGroup();
        s.map.addLayer(s.calendarLayer);
    } else if (!s.map.hasLayer(s.calendarLayer)) {
        s.calendarLayer.addTo(s.map);
    }
    return s.calendarLayer;
}

function makeCalendarKey(x) {
    const id = x?.Id ?? x?.id;
    if (id == null) return null;
    return `cc:${id}`;
}

function calendarLevelFromExpected(x) {
    const n = Number(x?.ExpectedLevel ?? x?.expectedLevel ?? 2);
    if (!Number.isFinite(n)) return 2;
    return Math.max(1, Math.min(4, n));
}

function calendarIconEmoji(x) {
    const tags = String(x?.Tags ?? x?.tags ?? "").toLowerCase();
    const name = String(x?.EventName ?? x?.eventName ?? "").toLowerCase();
    if (tags.includes("folkl") || name.includes("carnaval")) return "🎭";
    if (tags.includes("music") || name.includes("concert")) return "🎵";
    if (tags.includes("sport")) return "🏟️";
    return "📅";
}

function calendarPopupHtml(x, s) {
    const esc = s.utils.escapeHtml;

    const title = x?.EventName ?? x?.eventName ?? `Calendar #${x?.Id ?? x?.id ?? "?"}`;
    const region = x?.RegionCode ?? x?.regionCode ?? "";
    const msg = x?.MessageTemplate ?? x?.messageTemplate ?? x?.Message ?? x?.message ?? "";
    const dt = x?.DateUtc ?? x?.dateUtc ?? x?.Date ?? x?.date ?? null;

    const start = x?.StartLocalTime ?? x?.startLocalTime ?? null;
    const end = x?.EndLocalTime ?? x?.endLocalTime ?? null;
    const lead = x?.LeadHours ?? x?.leadHours ?? null;

    const parts = [];
    if (region) parts.push(`📍 ${esc(region)}`);
    if (dt) parts.push(`🕒 ${esc(String(dt))}`);
    if (start || end) parts.push(`⏱️ ${esc(String(start ?? "?"))} → ${esc(String(end ?? "?"))}`);
    if (lead != null) parts.push(`⏳ lead: ${esc(String(lead))}h`);
    if (msg) parts.push(`💬 ${esc(String(msg))}`);

    return `
    <div class="outzen-popup">
      <div class="title">${esc(String(title))}</div>
      <div class="desc">${parts.join("<br>")}</div>
    </div>
  `.trim();
}

export function upsertCrowdCalendarMarkers(items, scopeKey = "main") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s, L } = ready;
    if (!Array.isArray(items)) return false;

    ensureCalendarLayer(s, L);
    s.calendarMarkers ??= new Map();

    let dropped = 0;

    for (const x of items) {
        const key = makeCalendarKey(x);
        if (!key) { dropped++; continue; }

        const ll = pickLatLng(x, s.utils);
        if (!ll) {
            dropped++;
            if (dropped <= 5) {
                console.warn("[Bundles] drop sample kind=", kind,
                    +  "keys=", Object.keys(x || {}),
                    +  "item=", x
                );
            }
            continue;
        }

        const level = calendarLevelFromExpected(x);
        const emoji = calendarIconEmoji(x);

        const icon = buildMarkerIcon(L, level, {
            kind: "calendar",
            scopeKey: k,
            iconOverride: emoji
        });

        const popup = calendarPopupHtml(x, s);

        const existing = s.calendarMarkers.get(key);
        if (existing) {
            try { existing.setLatLng([ll.lat, ll.lng]); } catch { }
            try { existing.setIcon(icon); } catch { }
            try { if (existing.getPopup()) existing.setPopupContent(popup); else existing.bindPopup(popup); } catch { }
            try { if (!s.calendarLayer.hasLayer(existing)) s.calendarLayer.addLayer(existing); } catch { }
            continue;
        }

        const marker = L.marker([ll.lat, ll.lng], {
            icon,
            title: (x?.EventName ?? x?.eventName ?? key),
            riseOnHover: true,
            __ozNoCluster: true
        });

        try { marker.bindPopup(popup, { maxWidth: 420, closeButton: true, autoPan: true }); } catch { }
        try { s.calendarLayer.addLayer(marker); } catch { addLayerSmart(marker, s); }

        s.calendarMarkers.set(key, marker);
    }

    if (dropped) console.warn("[Calendar] dropped =", dropped, "(missing id/coords)");
    try { refreshHybridVisibility(k); } catch { }

    return true;
}

export function pruneCrowdCalendarMarkers(activeIds, scopeKey = "main") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s } = ready;
    s.calendarMarkers ??= new Map();
    if (!Array.isArray(activeIds)) activeIds = [];

    const keep = new Set(activeIds.map(String));
    let removed = 0;

    for (const [key, marker] of Array.from(s.calendarMarkers.entries())) {
        if (keep.has(key)) continue;

        try {
            if (s.calendarLayer && s.calendarLayer.hasLayer?.(marker)) s.calendarLayer.removeLayer(marker);
            else removeLayerSmart(marker, s);
        } catch { }

        s.calendarMarkers.delete(key);
        removed++;
    }

    if (removed) console.info("[Calendar] pruned =", removed);
    return true;
}

/* ---------------------------------------------------------
   Antenna markers (no cluster)
--------------------------------------------------------- */
function ensureAntennaLayer(s, L) {
    if (!s.antennaLayer) {
        s.antennaLayer = L.featureGroup();
        s.map.addLayer(s.antennaLayer);
    } else if (!s.map.hasLayer(s.antennaLayer)) {
        s.antennaLayer.addTo(s.map);
    }
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

    const ll = pickLatLng(antenna, s.utils);
    if (!ll) return false;

    ensureAntennaLayer(s, L);
    s.antennaMarkers ??= new Map();

    const key = makeAntennaKey(antenna) ?? `ant:${ll.lat.toFixed(5)},${ll.lng.toFixed(5)}`;

    const icon = buildMarkerIcon(L, 1, {
        kind: "antenna",
        scopeKey: k,
        iconOverride: "📡"
    });

    const title = antenna?.Name ?? antenna?.name ?? "Antenna";
    const desc = antenna?.Description ?? antenna?.description ?? "";
    const popup = buildPopupHtml({ title, description: desc }, s);

    const existing = s.antennaMarkers.get(key);
    if (existing) {
        try { existing.setLatLng([ll.lat, ll.lng]); } catch { }
        try { existing.setIcon(icon); } catch { }
        try { if (existing.getPopup()) existing.setPopupContent(popup); else existing.bindPopup(popup); } catch { }
        try { if (!s.antennaLayer.hasLayer(existing)) s.antennaLayer.addLayer(existing); } catch { }
        return true;
    }

    const m = L.marker([ll.lat, ll.lng], {
        icon,
        title,
        riseOnHover: true,
        __ozNoCluster: true
    });

    try { m.bindPopup(popup, { maxWidth: 420, closeButton: true, autoPan: true }); } catch { }
    try { s.antennaLayer.addLayer(m); } catch { addLayerSmart(m, s); }

    s.antennaMarkers.set(key, m);
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
            if (s.antennaLayer?.hasLayer?.(m)) s.antennaLayer.removeLayer(m);
            else removeLayerSmart(m, s);
        } catch { }
        s.antennaMarkers.delete(key);
    }
    return true;
}

/* ---------------------------------------------------------
   Weather markers (standalone)
--------------------------------------------------------- */
export function addOrUpdateWeatherMarkers(items, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { k, s } = ready;
    if (!Array.isArray(items)) return false;

    for (const w of items) {
        const ll = pickLatLng(w, s.utils);
        if (!ll) continue;

        const wid = (w?.Id ?? w?.id);
        if (wid == null) continue;

        const id = `wf:${wid}`;
        const level = (w?.IsSevere || w?.isSevere) ? 4 : 2;

        addOrUpdateCrowdMarker(id, ll.lat, ll.lng, level, {
            title: w?.Summary ?? w?.summary ?? "Weather",
            description: [
                `Temp: ${w?.TemperatureC ?? w?.temperatureC ?? "?"}°C`,
                `Hum: ${w?.Humidity ?? w?.humidity ?? "?"}%`,
                `Wind: ${w?.WindSpeedKmh ?? w?.windSpeedKmh ?? "?"} km/h`,
                `Rain: ${w?.RainfallMm ?? w?.rainfallMm ?? "?"} mm`,
                (w?.Description ?? w?.description) ? `Desc: ${w?.Description ?? w?.description}` : null
            ].filter(Boolean).join(" • "),
            weatherType: (w?.WeatherType ?? w?.weatherType ?? "").toString(),
            isTraffic: false
        }, k);
    }
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

function toNum(v) {
    if (v == null) return null;
    const n = (typeof v === "string") ? Number(v.replace(",", ".")) : Number(v);
    return Number.isFinite(n) ? n : null;
}

function pickLatLng(o, utils = null) {
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

    const toNum = (v) => {
        if (v == null) return null;
        if (typeof v === "string") v = v.replace(",", ".");
        const n = Number(v);
        return Number.isFinite(n) ? n : null;
    };

    const lat = toNum(latVal);
    const lng = toNum(lngVal);

    if (lat == null || lng == null) return null;
    if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;

    return { lat, lng };
}
function hasCoord(o) {
    const p = pickLatLng(o);
    return !!p;
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
        gpt: p.gpt ?? p.Gpt ?? p.GPT ?? []
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

    const dots = `
    <div class="oz-dots">
      ${nEvents ? `<span class="oz-dot oz-dot-events" title="Events"></span>` : ``}
      ${nPlaces ? `<span class="oz-dot oz-dot-places" title="Places"></span>` : ``}
      ${nCrowds ? `<span class="oz-dot oz-dot-crowds" title="Crowd"></span>` : ``}
      ${nTraffic ? `<span class="oz-dot oz-dot-traffic" title="Traffic"></span>` : ``}
      ${nWeather ? `<span class="oz-dot oz-dot-weather" title="Weather"></span>` : ``}
      ${nSugg ? `<span class="oz-dot oz-dot-gpt" title="Suggestion"></span>` : ``}
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

function resolveLatLngForItem(item, indexes, s) {
    let ll = pickLatLng(item);
    if (ll) return ll;

    const placeId = item?.PlaceId ?? item?.placeId;
    if (placeId != null) {
        const p = indexes.placeById.get(placeId);
        ll = pickLatLng(p);
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

    const wfId = item?.WeatherForecastId ?? item?.weatherForecastId ?? item?.ForecastId ?? item?.forecastId;
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

function computeBundles(payload, tolMeters, s) {
    const buckets = new Map();
    const norm = payload;

    const placesArr = norm.places;
    const eventsArr = norm.events;
    const crowdsArr = norm.crowds;
    const trafficArr = norm.traffic;
    const weatherArr = norm.weather;
    const suggestionsArr = norm.suggestions;
    const gptArr = norm.gpt;

    const indexes = {
        placeById: new Map(placesArr.map(p => [p?.Id ?? p?.id, p]).filter(([id]) => id != null)),
        eventById: new Map(eventsArr.map(e => [e?.Id ?? e?.id, e]).filter(([id]) => id != null)),
        crowdById: new Map(crowdsArr.map(c => [c?.Id ?? c?.id, c]).filter(([id]) => id != null)),
        trafficById: new Map(trafficArr.map(t => [t?.Id ?? t?.id, t]).filter(([id]) => id != null)),
        weatherById: new Map(weatherArr.map(w => [w?.Id ?? w?.id, w]).filter(([id]) => id != null)),
    };

    let dropped = 0;

    const push = (arr, kind) => {
        if (!Array.isArray(arr) || arr.length === 0) return;

        for (const item of arr) {
            const ll = resolveLatLngForItem(item, indexes, s);
            if (!ll) {
                dropped++;

                continue;
            }

            const key = bundleKeyFor(ll.lat, ll.lng, tolMeters);
            let b = buckets.get(key);
            if (!b) {
                b = { key, lat: ll.lat, lng: ll.lng, events: [], places: [], crowds: [], traffic: [], weather: [], suggestions: [], gpt: [] };
                buckets.set(key, b);
            }
            b[kind].push(item);
        }
    };

    push(placesArr, "places");
    push(eventsArr, "events");
    push(crowdsArr, "crowds");
    push(trafficArr, "traffic");
    push(weatherArr, "weather");
    push(suggestionsArr, "suggestions");

    if (Array.isArray(gptArr) && gptArr.length) {
        const gptGeo = gptArr.filter(x => pickLatLng(x) || x?.PlaceId != null || x?.EventId != null);
        push(gptGeo, "gpt");
    }

    if (dropped) console.warn("[Bundles] dropped items without lat/lng =", dropped);
    return buckets;
}

/* ---------------------------------------------------------
   Detail markers + Hybrid zoom (GUARDED)
--------------------------------------------------------- */
function isDetailsModeNow(s) {
    if (!s?.map) return false;
    if (!s.hybrid?.enabled) return false;
    const z = s.map.getZoom?.();
    return (Number(z) || 0) >= (Number(s.hybrid.threshold) || 13);
}

function ensureDetailLayer(s, L) {
    if (!s?.map) return null;
    if (!s.detailLayer) {
        s.detailLayer = L.layerGroup();
        s.map.addLayer(s.detailLayer);
    } else if (s.map && typeof s.map.hasLayer === "function" && !s.map.hasLayer(s.detailLayer)) {
        s.detailLayer.addTo(s.map);
    }
    return s.detailLayer;
}

function clearDetailMarkers(s) {
    const has = makeHas(s);
    try { if (has(s.detailLayer)) s.detailLayer.clearLayers?.(); } catch { }
    try { s.detailMarkers?.clear?.(); } catch { }
}

function makeDetailKey(kind, item, s) {
    const k = String(kind).toLowerCase();

    if (k === "suggestion") {
        const sid = item?.SuggestionId ?? item?.suggestionId ?? item?.Id ?? item?.id;
        if (sid != null) return `suggestion:${sid}`;
    }

    const id = item?.Id ?? item?.id ?? item?.ForecastId ?? item?.forecastId ?? item?.WeatherForecastId ?? item?.weatherForecastId;

    if (id != null) return `${k}:${id}`;

    const placeId = item?.PlaceId ?? item?.placeId;
    const dt = item?.DateWeather ?? item?.dateWeather ?? item?.DateUtc ?? item?.dateUtc;
    if (placeId != null && dt) return `${k}:p${placeId}:${String(dt)}`;

    const ll = pickLatLng(item);
    if (ll) return `${k}:${ll.lat.toFixed(5)},${ll.lng.toFixed(5)}`;

    return `${k}:${JSON.stringify(item).slice(0, 64)}`;
}

function addDetailMarker(kind, item, s, L, scopeKey = null) {
    if (!s?.map) return;

    const layer = ensureDetailLayer(s, L);
    if (!layer) return;

    const kindLower = String(kind).toLowerCase();

    // 1) coords
    const ll = pickLatLng(item);
    if (!ll) return;

    // 2) ✅ Define key/title/sid BEFORE any log/usage (avoids TDZ)
    const key = makeDetailKey(kindLower, item, s);
    if (s.detailMarkers.has(key)) return;

    const title = s.utils.titleOf(kindLower, item);

    // Suggestion id (for click map -> Blazor)
    const sid =
        Number(item?.SuggestionId ?? item?.suggestionId ?? item?.Id ?? item?.id);

    if (kindLower === "suggestion") {
        console.log("[DETAILS] suggestion resolved ll", {
            key,
            sid,
            title,
            rawLat: item?.Latitude ?? item?.latitude ?? item?.lat,
            rawLng: item?.Longitude ?? item?.longitude ?? item?.lng,
            ll
        });
    }

    // 3) icon + popup by type
    if (kindLower === "weather") {
        const severe = !!(item?.IsSevere ?? item?.isSevere);
        const level = severe ? 4 : 2;

        const icon = buildMarkerIcon(L, level, {
            kind: "weather",
            weatherType: (item?.WeatherType ?? item?.weatherType ?? "").toString(),
            isTraffic: false
        });

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Weather: ${title}`,
            riseOnHover: true,
            pane: "markerPane"
        });

        m.bindPopup(buildPopupHtml({
            title: item?.Summary ?? item?.summary ?? title,
            description: `Temp: ${item?.TemperatureC ?? item?.temperatureC ?? "?"}°C • Vent: ${item?.WindSpeedKmh ?? item?.windSpeedKmh ?? "?"} km/h`
        }, s));

        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    if (kindLower === "place") {
        const icon = buildMarkerIcon(L, 1, { kind: "place", iconOverride: "🏰" });
        const m = L.marker([ll.lat, ll.lng], { icon, title: `Place: ${title}`, riseOnHover: true, pane: "markerPane" });
        m.bindPopup(buildPopupHtml({ title, description: item?.Description ?? item?.description ?? "" }, s));
        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    if (kindLower === "event") {
        const icon = buildMarkerIcon(L, 2, { kind: "event", iconOverride: "🎪" });
        const m = L.marker([ll.lat, ll.lng], { icon, title: `Event: ${title}`, riseOnHover: true, pane: "markerPane" });
        m.bindPopup(buildPopupHtml({ title, description: item?.Description ?? item?.description ?? "" }, s));
        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    if (kindLower === "suggestion") {
        const icon = buildMarkerIcon(L, 2, { kind: "suggestion", iconOverride: "💡" });

        const popupDesc = [
            item?.Reason ? `Reason: ${item.Reason}` : null,
            item?.OriginalPlace ? `From: ${item.OriginalPlace}` : null,
            item?.SuggestedAlternatives ? `Alt: ${item.SuggestedAlternatives}` : null,
            (item?.DistanceKm != null) ? `Distance: ${item.DistanceKm} km` : null,
        ].filter(Boolean).join(" • ");

        const m = L.marker([ll.lat, ll.lng], {
            icon,
            title: `Suggestion: ${title}`,
            riseOnHover: true,
            pane: "markerPane"
        });

        // ✅ Click -> Blazor (if dotnet ref is registered)
        m.on("click", () => {
            if (!Number.isFinite(sid)) return;
            const dn = getDotNetRef(scopeKey); // ✅ propagated scopeKey
            if (dn) dn.invokeMethodAsync("SelectSuggestionFromMap", sid);
        });

        m.bindPopup(buildPopupHtml({ title, description: popupDesc }, s));

        layer.addLayer(m);
        s.detailMarkers.set(key, m);
        return;
    }

    // generic fallback
    const icon = buildMarkerIcon(L, 1, { kind: kindLower, iconOverride: "" });
    const m = L.marker([ll.lat, ll.lng], { icon, title: `${kindLower}: ${title}`, riseOnHover: true, pane: "markerPane" });
    m.bindPopup(`<div class="oz-popup"><b>${s.utils.escapeHtml(kindLower)}</b><br>${s.utils.escapeHtml(title)}</div>`);
    layer.addLayer(m);
    s.detailMarkers.set(key, m);
}

const __ozDotNet = globalThis.__ozDotNet || (globalThis.__ozDotNet = new Map());

function getDotNetRef(scopeKey) {
    const m = globalThis.__ozDotNet;
    if (!(m instanceof Map)) return null;
    return m.get(String(scopeKey || "main")) || null;
}

export function registerDotNetRef(scopeKey, dotnetRef) {
    const m = globalThis.__ozDotNet;
    if (!(m instanceof Map)) globalThis.__ozDotNet = new Map();
    globalThis.__ozDotNet.set(String(scopeKey || "main"), dotnetRef);
    return true;
}

export function unregisterDotNetRef(scopeKey) {
    const m = globalThis.__ozDotNet;
    if (m instanceof Map) m.delete(String(scopeKey || "main"));
    return true;
}

export function addOrUpdateDetailMarkers(payload, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;

    const { s, L } = ready;
    clearDetailMarkers(s);

    const norm = normalizePayload(payload);

    console.log("[DETAILS] suggestions input (first 10):",
        (norm.suggestions || []).slice(0, 10).map(x => ({
            id: x.Id ?? x.id,
            title: x.Title ?? x.title,
            lat: x.Latitude ?? x.latitude ?? x.lat,
            lng: x.Longitude ?? x.longitude ?? x.lng
        }))
    );

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;
        for (const x of arr) {
            addDetailMarker(kind, x, s, L, scopeKey);
        }
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

    // ✅ If there are no payload bundles, the switchover is refused.
    if (!s.bundleLastInput) {
        console.warn("[Hybrid] No bundleLastInput => keep bundles/markers");
        s.hybrid.showing = s.hybrid.showing ?? "bundles";
        return;
    }

    for (const m of s.bundleMarkers.values()) {
        try { if (map.hasLayer(m)) map.removeLayer(m); } catch { }
    }

    ensureDetailLayer(s, L);
    //if (s.bundleLastInput)
    //    addOrUpdateDetailMarkers(s.bundleLastInput, scopeKey);
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
    const k = pickScopeKeyRead(scopeKey);
    const s = peekS(k) || getS(k);
    const map = s?.map;
    const token = s?._mapToken;

    if (!s || !map || !s.hybrid?.enabled) return;
    if (s._hybridSwitching) return;

    if (s.flags?.userLockedMode) {
        if (s.hybrid.showing !== "details" && s.map) switchToDetails(s, s.map, k);
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

                if (wantDetails && s.hybrid.showing !== "details") {
                    switchToDetails(s, map, k);
                } else if (!wantDetails && s.hybrid.showing !== "bundles") {
                    switchToBundles(s, map);
                }
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
    const k = pickScopeKeyRead(scopeKey);
    const s = peekS(k) || getS(k);
    if (!s.map) return false;

    s.flags.userLockedMode = true;
    s.hybrid.enabled = true;

    const th = Number(s.hybrid.threshold) || 13;
    try {
        if ((s.map.getZoom?.() ?? 0) < th) s.map.setZoom(th, { animate: false });
    } catch { }

    switchToDetails(s, s.map, k);
    try { refreshHybridVisibility(k); } catch { }
    return true;
}

export function unlockHybrid(scopeKey = null) {
    const k = pickScopeKeyRead(scopeKey);
    const s = peekS(k) || getS(k);
    s.flags.userLockedMode = false;
    refreshHybridVisibility(k);
    return true;
}

export function refreshHybridNow(scopeKey = null) {
    const k = pickScopeKeyRead(scopeKey);
    const s = peekS(k) || getS(k);
    if (!s.map || !s.hybrid?.enabled) return false;
    try { refreshHybridVisibility(k); } catch { }
    return true;
}

/* ---------------------------------------------------------
   Bundle marker update (GUARDED)
--------------------------------------------------------- */
export function updateBundleMarker(b, scopeKey = "main") {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return;

    const { s, L, map } = ready;
    const total = bundleTotal(b);
    const sev = bundleSeverity(b);
    const existing = s.bundleMarkers.get(b.key);
    const detailsMode = isDetailsModeNow(s);

    if (total <= 0) {
        if (existing) {
            removeLayerSmart(existing, s);
            s.bundleMarkers.delete(b.key);
            s.bundleIndex.delete(b.key);
        }
        return;
    }

    const icon = makeBadgeIcon(total, sev, b);
    const popup = bundlePopupHtml(b, s);

    if (!existing) {
        const m = L.marker([b.lat, b.lng], {
            icon,
            title: `Area (${total})`,
            riseOnHover: true,
            __ozNoCluster: true,
        });

        try { m.bindPopup(popup, { maxWidth: 420, closeButton: true, autoPan: true }); } catch { }

        addLayerSmart(m, s);

        s.bundleMarkers.set(b.key, m);
        s.bundleIndex.set(b.key, b);
        return;
    }

    try { existing.setLatLng([b.lat, b.lng]); } catch { }
    try { existing.setIcon(icon); } catch { }
    try { if (existing.getPopup()) existing.setPopupContent(popup); } catch { }

    try {
        if (detailsMode) {
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

    console.log("[Bundles] payload sizes",
        "events", (norm.events?.length ?? 0),
        "places", (norm.places?.length ?? 0),
        "crowds", (norm.crowds?.length ?? 0),
        "traffic", (norm.traffic?.length ?? 0),
        "weather", (norm.weather?.length ?? 0),
        "suggestions", (norm.suggestions?.length ?? 0),
        "gpt", (norm.gpt?.length ?? 0)
    );

    s.bundleLastInput = norm;

    if (!s?.map) return false;

    const bundles = computeBundles(norm, tolFinal, s);
    if (!bundles || bundles.size === 0) {
        for (const oldKey of Array.from(s.bundleMarkers.keys())) {
            const marker = s.bundleMarkers.get(oldKey);
            removeLayerSmart(marker, s);
            s.bundleMarkers.delete(oldKey);
            s.bundleIndex.delete(oldKey);
        }
        try { refreshHybridVisibility(k); } catch { }
        return true;
    }

    for (const oldKey of Array.from(s.bundleMarkers.keys())) {
        if (!bundles.has(oldKey)) {
            const marker = s.bundleMarkers.get(oldKey);
            removeLayerSmart(marker, s);
            s.bundleMarkers.delete(oldKey);
            s.bundleIndex.delete(oldKey);
        }
    }

    for (const b of bundles.values()) updateBundleMarker(b, k);

    try { refreshHybridVisibility(k); } catch { }

    if (s.cluster && typeof s.cluster.refreshClusters === "function") {
        try { s.cluster.refreshClusters(); } catch { }
    }

    if (s.flags.showWeatherPinsInBundles && s.hybrid?.showing !== "details") {
        addOrUpdateWeatherMarkers(norm.weather ?? [], k);
    }
    console.log("[Bundles] sample place", norm.places?.[0], "latlng", pickLatLng(norm.places?.[0]));
    return true;
}

/* ---------------------------------------------------------
   Fit / Resize helpers
--------------------------------------------------------- */
export function refreshMapSize(scopeKey = null) {
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
        if (!r || r.width < 10 || r.height < 10) return;

        if (map._animatingZoom || map._zooming || map._panning) return;

        try { map.invalidateSize({ animate: false, debounceMoveend: true }); } catch { }
    });

    return true;
}

/* ---------------------------------------------------------
   Incremental weather bundle input
--------------------------------------------------------- */
export function scheduleBundleRefresh(delayMs = 150, tolMeters = 80, scopeKey = null) {
    const k = pickScopeKeyRead(scopeKey);
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

    const ll = pickLatLng(raw, s.utils);
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
   Debug
--------------------------------------------------------- */
export function debugDumpMarkers(scopeKey = null) {
    const k = pickScopeKeyRead(scopeKey);
    const s = peekS(k) || getS(k);
    const has = makeHas(s);

    console.log("[DBG] markers keys =", Array.from(s.markers?.keys?.() ?? []));
    console.log("[DBG] bundle keys =", Array.from(s.bundleMarkers?.keys?.() ?? []));
    console.log("[DBG] detail keys =", Array.from(s.detailMarkers?.keys?.() ?? []));
    console.log("[DBG] map initialized =", !!s.map, "cluster =", !!s.cluster, "showing=", s.hybrid?.showing, "hasDetailLayer=", has(s.detailLayer));
}

export function debugClusterCount(scopeKey = null) {
    const k = pickScopeKeyRead(scopeKey);
    const s = peekS(k) || getS(k);
    const layers = s?.cluster?.getLayers?.();
    console.log("[DBG] markers=", s?.markers?.size ?? 0, "clusterLayers=", layers?.length ?? 0);
}

/* ---------------------------------------------------------
   waitForMarkerElement (as requested)
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
function _boundsFromLatLngs(L, latlngs) {
    if (!latlngs || !latlngs.length) return null;
    const b = L.latLngBounds(latlngs);
    return b && b.isValid && b.isValid() ? b : null;
}

export function fitToBundles(scopeKey = null, opts = {}) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s, L, map } = ready;

    const padding = opts.padding ?? [22, 22];
    const maxZoom = opts.maxZoom ?? 16;

    const latlngs = [];
    for (const m of (s.bundleMarkers?.values?.() ?? [])) {
        try { latlngs.push(m.getLatLng()); } catch { }
    }

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
    // detail : detailMarkers
    for (const m of (s.detailMarkers?.values?.() ?? [])) {
        try { latlngs.push(m.getLatLng()); } catch { }
    }
    // fallback : “General” markers
    if (!latlngs.length) {
        for (const m of (s.markers?.values?.() ?? [])) {
            try { latlngs.push(m.getLatLng()); } catch { }
        }
    }

    const b = _boundsFromLatLngs(L, latlngs);
    if (!b) return false;

    try { map.fitBounds(b, { padding, animate: false, maxZoom }); } catch { }
    return true;
}

export function fitToMarkers(scopeKey = null, opts = {}) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s, L, map } = ready;

    const padding = opts.padding ?? [22, 22];
    const maxZoom = opts.maxZoom ?? 17;

    const latlngs = [];
    for (const m of (s.markers?.values?.() ?? [])) {
        try { latlngs.push(m.getLatLng()); } catch { }
    }

    const b = _boundsFromLatLngs(L, latlngs);
    if (!b) return false;

    try { map.fitBounds(b, { padding, animate: false, maxZoom }); } catch { }
    return true;
}

// Practical: “active hybrid + fit”
export function activateHybridAndZoom(scopeKey = null, threshold = 13) {
    const k = pickScopeKeyRead(scopeKey);
    enableHybridZoom(true, threshold, k);

    const s = peekS(k) || getS(k);
    const z = s?.map?.getZoom?.() ?? 0;
    const wantDetails = z >= threshold;

    return wantDetails ? fitToDetails(k) : fitToBundles(k);
}
export function debugExplainBundles(scopeKey = null) {
    const k = pickScopeKeyRead(scopeKey);
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
export function removeAntennaMarker(key, scopeKey = null) {
    const ready = ensureMapReady(scopeKey);
    if (!ready) return false;
    const { s } = ready;

    s.antennaMarkers ??= new Map();
    const k = String(key);
    const m = s.antennaMarkers.get(k);
    if (!m) return true;

    try {
        if (s.antennaLayer?.hasLayer?.(m)) s.antennaLayer.removeLayer(m);
        else removeLayerSmart(m, s);
    } catch { }

    s.antennaMarkers.delete(k);
    return true;
}

export function addOrUpdateCrowdCalendarMarker(item, scopeKey = "main") {
    return upsertCrowdCalendarMarkers([item], scopeKey);
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

window.addEventListener("error", e => console.log("JS error:", e.error));
window.addEventListener("unhandledrejection", e => console.log("Unhandled promise:", e.reason));



























































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/