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

// ------------------------------
// Singleton (Hot Reload safe)
// ------------------------------
function getS() {
    globalThis.__OutZenSingleton ??= {
        version: "2026.01.14-clean",
        initialized: false,
        bootTs: 0,

        map: null,
        mapContainerId: null,
        mapContainerEl: null,

        cluster: null,
        chart: null,

        markers: new Map(), // id -> leaflet marker (generic/weather/gpt/traffic/etc)
        placeMarkers: new Map(),
        placeIndex: new Map(),

        // Bundles
        bundleMarkers: new Map(), // key -> marker
        bundleIndex: new Map(),   // key -> bundle object
        bundleLastInput: null,    // { events, places, crowds, traffic, weather, suggestions, gpt }

        // Hybrid / details
        detailLayer: null,
        detailMarkers: new Map(),
        hybrid: { enabled: true, threshold: 13, showing: null },
        _hybridBound: false,

        // Debounce
        _bundleRefreshT: 0,

        // Incremental indexes
        _weatherById: new Map(),

        // Registries
        consts: {},
        utils: {},
    };

    return globalThis.__OutZenSingleton;
}
const S = getS();
globalThis.__OutZenGetS ??= () => getS();

// ------------------------------
// Consts / Utils
// ------------------------------
S.consts.BELGIUM ??= { minLat: 49.45, maxLat: 51.60, minLng: 2.30, maxLng: 6.60 };

S.utils.safeNum ??= (x) => {
    if (x == null) return null;
    if (typeof x === "string") x = x.replace(",", ".");
    const n = Number(x);
    return Number.isFinite(n) ? n : null;
};

S.utils.escapeHtml ??= (v) =>
    String(v ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

S.utils.titleOf ??= (kind, item) => {
    if (!item) return kind;
    const s = (v) => (v == null ? "" : String(v)).trim();

    const title =
        s(item.Title) || s(item.title) ||
        s(item.Name) || s(item.name) ||
        s(item.LocationName) || s(item.locationName) ||
        s(item.RoadName) || s(item.roadName) ||
        s(item.Message) || s(item.message) ||
        s(item.Prompt) || s(item.prompt);

    if (title) return title;

    const id =
        item.Id ?? item.id ??
        item.EventId ?? item.PlaceId ?? item.CrowdInfoId ??
        item.TrafficConditionId ?? item.WeatherForecastId ?? item.SuggestionId;

    return id != null ? `${kind} #${id}` : kind;
};

// ------------------------------
// Internal helpers
// ------------------------------
async function waitForContainer(mapId, tries = 15) {
    for (let i = 0; i < tries; i++) {
        const el = document.getElementById(mapId);
        if (el) return el;
        await new Promise(r => requestAnimationFrame(() => r()));
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

function ensureMapReady() {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return null;
    if (!S.map) return null;
    return Leaflet;
}

function resetLeafletDomId(mapId) {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return;
    const dom = Leaflet.DomUtil.get(mapId);
    if (dom && dom._leaflet_id) {
        try { delete dom._leaflet_id; } catch { dom._leaflet_id = undefined; }
    }
}

function isInBelgium(ll) {
    const BE = S.consts.BELGIUM;
    return !!ll &&
        Number.isFinite(ll.lat) && Number.isFinite(ll.lng) &&
        ll.lat >= BE.minLat && ll.lat <= BE.maxLat &&
        ll.lng >= BE.minLng && ll.lng <= BE.maxLng;
}

// Parse lat/lng robustly (supports nested Location/position)
function pickLatLng(it, utils) {
    const safeNum = utils?.safeNum ?? ((v) => {
        if (v == null) return null;
        if (typeof v === "string") v = v.replace(",", ".");
        const n = Number(v);
        return Number.isFinite(n) ? n : null;
    });

    const latRaw =
        it?.Latitude ?? it?.latitude ?? it?.lat ?? it?.Lat ??
        it?.Location?.Latitude ?? it?.location?.latitude ??
        it?.Geo?.lat ?? it?.geo?.lat ??
        (Array.isArray(it?.Coordinates) ? it.Coordinates[0] : null);

    const lngRaw =
        it?.Longitude ?? it?.longitude ?? it?.lng ?? it?.Lng ??
        it?.lon ?? it?.Lon ??
        it?.Location?.Longitude ?? it?.location?.longitude ??
        it?.Geo?.lng ?? it?.Geo?.lon ?? it?.geo?.lng ?? it?.geo?.lon ??
        (Array.isArray(it?.Coordinates) ? it.Coordinates[1] : null);

    const lat = safeNum(latRaw);
    let lng = safeNum(lngRaw);
    if (lat == null || lng == null) return null;

    // normalize 0..360 -> -180..180
    if (lng > 180 && lng <= 360) lng -= 360;
    while (lng > 180) lng -= 360;
    while (lng < -180) lng += 360;

    if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;
    return { lat, lng };
}

function addLayerSmart(layer) {
    if (!S.map || !layer) return;

    // Bundles bypass cluster
    if (layer?.options?.__ozNoCluster) {
        try { S.map.addLayer(layer); } catch { }
        return;
    }

    if (S.cluster && typeof S.cluster.addLayer === "function") {
        try { S.cluster.addLayer(layer); } catch { }
    } else {
        try { S.map.addLayer(layer); } catch { }
    }
}

function removeLayerSmart(layer) {
    if (!S.map || !layer) return;

    if (layer?.options?.__ozNoCluster) {
        try { S.map.removeLayer(layer); } catch { }
        return;
    }

    if (S.cluster && typeof S.cluster.removeLayer === "function") {
        try { S.cluster.removeLayer(layer); } catch { }
    } else {
        try { S.map.removeLayer(layer); } catch { }
    }
}

function destroyChartIfAny() {
    if (S.chart && typeof S.chart.destroy === "function") {
        try { S.chart.destroy(); } catch { }
    }
    S.chart = null;
}

function ensureChartCanvas() {
    const canvas = document.getElementById("crowdChart");
    return canvas ?? null;
}

function isDetailsModeNow() {
    if (!S.map) return false;
    if (!S.hybrid?.enabled) return false;
    const z = S.map.getZoom();
    return z >= (Number(S.hybrid.threshold) || 13);
}

// ------------------------------
// Public API
// ------------------------------
export function isOutZenReady() {
    return !!S.initialized && !!S.map;
}

function hardDisposeCurrentMap() {
    try { disposeOutZen({ mapId: S.mapContainerId }); } catch { }
    try { S.map?.remove?.(); } catch { }
    S.map = null;
    S.cluster = null;
    S.mapContainerEl = null;
    S.mapContainerId = null;
    try { S.markers?.clear?.(); } catch { }
}
/**
 * Boot Leaflet map
 * options: { mapId, center:[lat,lng], zoom, enableChart, force, enableWeatherLegend, resetMarkers }
 */
export async function bootOutZen(options) {
    const {
        mapId = "leafletMap",
        center = [50.45, 4.6],
        zoom = 12,
        enableChart = false,
        force = false,
        enableWeatherLegend = false,
        resetMarkers = false,
    } = options || {};

    //if (typeof mapId === "string" && mapId.startsWith("_")) {
    //    console.warn("[OutZen][bootOutZen] suspicious mapId (starts with _):", mapId);
    //}

    if (!mapId) { console.error("[OutZen][bootOutZen] mapId required"); return false; }

    if (mapId === "leafletMap") {
        console.warn("[OutZen][bootOutZen] ⚠️ unexpected mapId leafLetMap");
        console.trace("[OutZen][bootOutZen] WHO called leafLetMap");
    }

    console.log("[OutZen][bootOutZen] called mapId=", mapId);
    console.trace("[OutZen][bootOutZen] stack");

    const Leaflet = ensureLeaflet();
    if (!Leaflet) return false;

    const host = await waitForContainer(mapId, 15);
    console.info("[OutZen][bootOutZen] host null?", !host, "id=", mapId);
    if (!host) return false;

    const containerChanged = !!S.map && !!S.mapContainerEl && (S.mapContainerEl !== host);
    const idChanged = !!S.map && (S.mapContainerId !== mapId);

    // If map is bound to a different DOM node or id, nuke it.
    if (containerChanged || idChanged) {
        console.warn("[OutZen][bootOutZen] container/id changed -> hard dispose");
        hardDisposeCurrentMap();
    }

    const same = !!S.map && (S.mapContainerId === mapId) && (S.mapContainerEl === host);
    const hasPane = !!host.querySelector(".leaflet-pane");

    // Safe reuse
    if (!force && same && host.classList.contains("leaflet-container") && hasPane) {
        try { S.map.invalidateSize(true); } catch { }
        S.initialized = true;
        return true;
    }

    if (host && host._leaflet_id) {
        try { delete host._leaflet_id; } catch { host._leaflet_id = undefined; }
    }

    // If map exists but not reusable, dispose before creating new
    if (S.map) hardDisposeCurrentMap();

    // Create map using HOST ELEMENT (not the id string)
    S.map = Leaflet.map(host, {
        zoomAnimation: true,
        fadeAnimation: true,
        markerZoomAnimation: true,
    }).setView(center, zoom);

    S.mapContainerId = mapId;
    S.mapContainerEl = host;

    console.log("[OutZen] mapContainerEl id=", S.mapContainerEl?.id, "same as host?", S.mapContainerEl === host);

    Leaflet.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "© OpenStreetMap contributors",
        maxZoom: 19,
    }).addTo(S.map);

    queueMicrotask(() => { try { S.map.invalidateSize(true); } catch { } });
    setTimeout(() => { try { S.map.invalidateSize(true); } catch { } }, 250);

    /*setTimeout(() => { try { S.map.invalidateSize(true); } catch { } }, 800);*/

    // cluster
    if (Leaflet.markerClusterGroup) {
        S.cluster = Leaflet.markerClusterGroup({
            disableClusteringAtZoom: 18,
            spiderfyOnMaxZoom: true,
            removeOutsideVisibleBounds: false,
            maxClusterRadius: 60,
        });
        S.map.addLayer(S.cluster);
    } else {
        S.cluster = null;
    }

    // markers registry
    if (resetMarkers) S.markers = new Map();
    else S.markers ??= new Map();

    if (enableWeatherLegend) {
        try { createWeatherLegendControl(Leaflet).addTo(S.map); } catch { }
    }

    destroyChartIfAny();
    if (enableChart && globalThis.Chart) {
        const canvas = ensureChartCanvas();
        if (canvas) {
            const ctx = canvas.getContext("2d");
            S.chart = new Chart(ctx, {
                type: "bar",
                data: { labels: [], datasets: [{ label: "Metric", data: [] }] },
                options: { responsive: true, animation: false },
            });
        }
    }

    try { enableHybridZoom({ threshold: 13 }); } catch { }
    try { S.map?.setZoom?.(13); } catch { }

    if (S.bundleLastInput) {
        try { addOrUpdateBundleMarkers(S.bundleLastInput, 80); } catch { }
    }

    S.initialized = true;
    S.bootTs = Date.now();
    console.log("[OutZen][bootOutZen] map created=", !!S.map);
    return true;
}

export function activateHybridAndZoom(threshold = 13, zoom = 13) {
    if (!S.map) return false;
    try { enableHybridZoom({ threshold }); } catch { }
    try { S.map.setZoom(zoom); } catch { }
    return true;
}

export function getCurrentMapId() { return S.mapContainerId; }
globalThis.OutZenInterop.getCurrentMapId = getCurrentMapId;

export function disposeOutZen({ mapId } = {}) {
    console.warn("[OutZen][disposeOutZen] called", { mapId, current: S.mapContainerId, ts: Date.now() });
    console.trace("[OutZen][disposeOutZen] stack");
    try {
        if (S.map && S._hybridBound) S.map.off("zoomend", refreshHybridVisibility);
    } catch { }
    S._hybridBound = false;

    try { if (S.detailLayer && S.map) S.map.removeLayer(S.detailLayer); } catch { }
    S.detailLayer = null;
    try { S.detailMarkers?.clear?.(); } catch { }

    try {
        if (S.cluster) {
            try { S.cluster.clearLayers(); } catch { }
            try { S.map?.removeLayer(S.cluster); } catch { }
        }
    } catch { }
    S.cluster = null;

    try {
        if (S.map) {
            try { S.map.stop(); } catch { }
            try { S.map.off(); } catch { }
            try { S.map.remove(); } catch { }
        }
    } catch { } finally {
        S.map = null;
    }

    // reset leaflet dom id if mapId provided
    if (mapId) {
        try {
            const el = document.getElementById(mapId);
            if (el && el._leaflet_id) {
                try { delete el._leaflet_id; } catch { el._leaflet_id = undefined; }
            }
        } catch { }
    }

    try { S.markers?.clear?.(); } catch { }
    try { S.bundleMarkers?.clear?.(); } catch { }
    try { S.bundleIndex?.clear?.(); } catch { }

    S.bundleLastInput = null;
    S.mapContainerEl = null;
    S.mapContainerId = null;
    S.initialized = false;
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
export function setWeatherChart(points, metricType = "Temperature") {
    if (!S.chart || !Array.isArray(points)) return;

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

    const ds = S.chart.data.datasets[0];
    S.chart.data.labels = labels;
    ds.label = datasetLabel;
    ds.data = values;

    try { S.chart.update(); } catch { }
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

function buildMarkerIcon(L, level, { isTraffic = false, weatherType = null, iconOverride = null } = {}) {
    const lvlClass = getMarkerClassForLevel(level);
    const trafficClass = isTraffic ? "oz-marker-traffic" : "";
    const emoji = iconOverride ? iconOverride : (weatherType ? getWeatherEmoji(weatherType) : "");

    return L.icon({
        iconUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
        shadowUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
        iconSize: [25, 41],
        iconAnchor: [12, 41]
    });

    //return L.divIcon({
    //    className: `oz-marker ${lvlClass} ${trafficClass}`.trim(),
    //    html: `<div class="oz-marker-inner">${emoji}</div>`,
    //    iconSize: [26, 26],
    //    iconAnchor: [13, 26],
    //    popupAnchor: [0, -26],
    //});
}

function buildPopupHtml(info) {
    const title = info?.title ?? "Unknown";
    const desc = info?.description ?? "";
    return `<div class="outzen-popup">
    <div class="title">${S.utils.escapeHtml(title)}</div>
    <div class="desc">${S.utils.escapeHtml(desc)}</div>
  </div>`;
}

// ------------------------------
// Crowd / generic marker API
// ------------------------------
export function addOrUpdateCrowdMarker(id, lat, lng, level, info) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return;

    const latNum = Number(lat);
    const lngNum = Number(lng);
    if (!Number.isFinite(latNum) || !Number.isFinite(lngNum)) return;

    const key = String(id);
    const existing = S.markers.get(key);

    const popupHtml = buildPopupHtml(info ?? {});
    const icon = buildMarkerIcon(Leaflet, level, {
        isTraffic: !!info?.isTraffic,
        weatherType: info?.weatherType ?? info?.WeatherType ?? null,
        iconOverride: info?.icon ?? info?.Icon ?? null,
    });

    if (existing) {
        try { existing.setLatLng([latNum, lngNum]); } catch { }
        try { existing.setPopupContent(popupHtml); } catch { }
        try { existing.setIcon(icon); } catch { }
        return;
    }

    const marker = Leaflet.marker([latNum, lngNum], {
        title: info?.title ?? key,
        riseOnHover: true,
        icon
    }).bindPopup(popupHtml);

    addLayerSmart(marker);
    S.markers.set(key, marker);
}

export function removeCrowdMarker(id) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return;

    const key = String(id);
    const marker = S.markers.get(key);
    if (!marker) return;

    removeLayerSmart(marker);
    S.markers.delete(key);
}

export function clearCrowdMarkers() {
    if (!S.map) return;

    try { S.cluster?.clearLayers?.(); } catch { }
    if (!S.cluster) {
        for (const m of S.markers.values()) {
            try { S.map.removeLayer(m); } catch { }
        }
    }
    S.markers.clear();
}

export function fitToMarkers() {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return;
    if (!S.markers || S.markers.size === 0) return;

    const latlngs = [];
    for (const m of S.markers.values()) {
        try { latlngs.push(m.getLatLng()); } catch { }
    }
    if (latlngs.length === 0) return;

    if (latlngs.length === 1) {
        try { S.map.setView(latlngs[0], 15, { animate: false }); } catch { }
        return;
    }

    const b = Leaflet.latLngBounds(latlngs).pad(0.1);
    try { S.map.fitBounds(b, { padding: [32, 32], maxZoom: 17, animate: false }); } catch { }
}

export function refreshMapSize() {
    if (!S.map) return;
    setTimeout(() => { try { S.map.invalidateSize(true); } catch { } }, 50);
}

export function debugClusterCount() {
    const s = __OutZenGetS();
    const layers = s?.cluster?.getLayers?.();
    console.log("[DBG] markers=", s?.markers?.size ?? 0, "clusterLayers=", layers?.length ?? 0);
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

function clampLevel14(x) {
    const n = Number(x);
    if (!Number.isFinite(n)) return null;
    if (n < 1) return 1;
    if (n > 4) return 4;
    return n;
}

function pickCrowdLevel(item) {
    return clampLevel14(item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level);
}
function pickTrafficLevel(item) {
    return clampLevel14(item?.TrafficLevel ?? item?.trafficLevel ?? item?.CongestionLevel ?? item?.level);
}
function pickWeatherLevel(item) {
    // 4 if severe, otherwise 2 (or 1 if you want very neutral)
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
    // si tu as WeatherType / WeatherMain / Summary -> adapte
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

function weatherLineHtml(w) {
    const esc = S.utils.escapeHtml;
    const t = w?.TemperatureC ?? w?.temperatureC;
    const hum = w?.Humidity ?? w?.humidity;
    const wind = w?.WindSpeedKmh ?? w?.windSpeedKmh;
    const rain = w?.RainfallMm ?? w?.rainfallMm;
    const sum = w?.Summary ?? w?.summary ?? "Weather";
    const desc = w?.Description ?? w?.description ?? "";
    const main = w?.WeatherMain ?? w?.weatherMain ?? "";
    const sev = !!(w?.IsSevere ?? w?.isSevere);

    // Small, readable format, without geo/metadata
    const parts = [];
    if (t != null) parts.push(`${esc(t)}°C`);
    if (hum != null) parts.push(`Hum ${esc(hum)}%`);
    if (wind != null) parts.push(`Wind${esc(wind)} km/h`);
    if (rain != null) parts.push(`Rain ${esc(rain)} mm`);

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

function bundlePopupHtml(b) {
    const esc = S.utils.escapeHtml;
    const tOf = S.utils.titleOf;

    const row = (kind, it, extra = "") => `
    <div class="oz-row">
      <span class="oz-k">${esc(kind)}</span>
      <span class="oz-v">${esc(tOf(kind, it))}${extra}</span>
    </div>`;

    const section = (title, items, renderer) => {
        if (!Array.isArray(items) || items.length === 0) return "";
        const rows = items.slice(0, 8).map(renderer).join("");
        const more = items.length > 8 ? `<div class="oz-more">+${items.length - 8} more…</div>` : "";
        return `
      <div class="oz-sec">
        <div class="oz-sec-title">${esc(title)} <span class="oz-sec-count">${items.length}</span></div>
        <div class="oz-sec-body">${rows}${more}</div>
      </div>`;
    };

    const totals = bundleBreakdown(b);
    const total = bundleTotal(b);

    return `
    <div class="oz-bundle-popup">
      <div class="oz-bundle-head">
        <div class="oz-bundle-title">Summary area</div>
        <div class="oz-bundle-sub">${total} element(s) •
          E:${totals.events} P:${totals.places} C:${totals.crowds}
          T:${totals.traffic} W:${totals.weather} S:${totals.suggestions} G:${totals.gpt}
        </div>
        <div class="oz-bundle-coords">${Number(b.lat).toFixed(5)}, ${Number(b.lng).toFixed(5)}</div>
      </div>

      ${section("Events", b.events, (e) => row("Event", e))}
      ${section("Places", b.places, (p) => row("Place", p))}
      ${section("Crowds", b.crowds, (c) => row("Crowd", c, c?.CrowdLevel != null ? ` (L${esc(c.CrowdLevel)})` : ""))}
      ${section("Traffic", b.traffic, (t) => row("Traffic", t))}
      ${section("Weather", b.weather, (w) => `
                                          <div class="oz-row oz-row-wx">
                                            <span class="oz-k">Weather</span>
                                            <span class="oz-v">${weatherLineHtml(w)}</span>
                                          </div>
                                        `)}
      ${section("Suggestions", b.suggestions, (s) => row("Suggestion", s))}
      ${section("GPT", b.gpt, (g) => row("GPT", g))}
    </div>
  `.trim();
}

function resolveLatLngForItem(kindLower, item, indexes) {
    // 1) direct coords
    let ll = pickLatLng(item, S.utils);
    if (ll) return ll;

    // 2) by PlaceId
    const placeId = item?.PlaceId ?? item?.placeId;
    if (placeId != null) {
        const p = indexes.placeById.get(placeId);
        ll = pickLatLng(p, S.utils);
        if (ll) return ll;
    }

    // 3) by EventId
    const eventId = item?.EventId ?? item?.eventId;
    if (eventId != null) {
        const e = indexes.eventById.get(eventId);
        ll = pickLatLng(e, S.utils);
        if (ll) return ll;
    }

    // 4) by CrowdInfoId
    const crowdId = item?.CrowdInfoId ?? item?.crowdInfoId;
    if (crowdId != null) {
        const c = indexes.crowdById.get(crowdId);
        ll = pickLatLng(c, S.utils);
        if (ll) return ll;
    }

    // 5) by WeatherForecastId
    const wfId = item?.WeatherForecastId ?? item?.weatherForecastId;
    if (wfId != null) {
        const w = indexes.weatherById.get(wfId);
        ll = pickLatLng(w, S.utils);
        if (ll) return ll;
    }

    // 6) by TrafficConditionId
    const tcId = item?.TrafficConditionId ?? item?.trafficConditionId;
    if (tcId != null) {
        const t = indexes.trafficById.get(tcId);
        ll = pickLatLng(t, S.utils);
        if (ll) return ll;
    }
    return null;
}
function computeBundles(payload, tolMeters) {
    const buckets = new Map();

    const placesArr = Array.isArray(payload?.places) ? payload.places : [];
    const eventsArr = Array.isArray(payload?.events) ? payload.events : [];
    const crowdsArr = Array.isArray(payload?.crowds) ? payload.crowds : [];
    const trafficArr = Array.isArray(payload?.traffic) ? payload.traffic : [];
    const weatherArr = Array.isArray(payload?.weather) ? payload.weather : [];
    const suggestionsArr = Array.isArray(payload?.suggestions) ? payload.suggestions : [];
    const gptArr = Array.isArray(payload?.gpt) ? payload.gpt : [];

    const indexes = {
        placeById: new Map(placesArr.map(p => [p?.Id ?? p?.id, p]).filter(([id]) => id != null)),
        eventById: new Map(eventsArr.map(e => [e?.Id ?? e?.id, e]).filter(([id]) => id != null)),
        crowdById: new Map(crowdsArr.map(c => [c?.Id ?? c?.id, c]).filter(([id]) => id != null)),
        trafficById: new Map(trafficArr.map(t => [t?.Id ?? t?.id, t]).filter(([id]) => id != null)),
        weatherById: new Map(weatherArr.map(w => [w?.Id ?? w?.id, w]).filter(([id]) => id != null)),
    };

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;
        const kindLower = String(kind).toLowerCase();

        for (const item of arr) {
            const ll = resolveLatLngForItem(kindLower, item, indexes);
            if (!ll) {
                if (kindLower !== "gpt") console.warn("[Bundles] no coords", kindLower, item);
                continue;
            }
            if (!isInBelgium(ll)) continue;

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
    push(gptArr, "gpt");

    return buckets;
}

export function updateBundleMarker(b) {
    const L = ensureMapReady();
    if (!L) return;

    const total = bundleTotal(b);
    const sev = bundleSeverity(b);
    const existing = S.bundleMarkers.get(b.key);
    const detailsMode = isDetailsModeNow();

    if (total <= 0) {
        if (existing) {
            removeLayerSmart(existing);
            S.bundleMarkers.delete(b.key);
            S.bundleIndex.delete(b.key);
        }
        return;
    }

    const icon = makeBadgeIcon(total, sev, b);
    const popup = bundlePopupHtml(b);

    if (!existing) {
        const m = L.marker([b.lat, b.lng], {
            icon,
            pane: "markerPane",
            title: `Area (${total})`,
            riseOnHover: true,
            __ozNoCluster: true,
        });

        m.bindPopup(popup, { maxWidth: 420, closeButton: true, autoPan: true });
        m.bindTooltip(`Area • ${total} elements`, { direction: "top", sticky: true, opacity: 0.95 });

        if (!detailsMode) addLayerSmart(m);

        S.bundleMarkers.set(b.key, m);
        S.bundleIndex.set(b.key, b);
        return;
    }

    try { existing.setLatLng([b.lat, b.lng]); } catch { }
    try { if (icon) existing.setIcon(icon); } catch { }
    try { if (existing.getPopup()) existing.setPopupContent(popup); } catch { }

    try {
        if (detailsMode) {
            if (S.map.hasLayer(existing)) S.map.removeLayer(existing);
        } else {
            if (!S.map.hasLayer(existing)) S.map.addLayer(existing);
        }
    } catch { }

    S.bundleIndex.set(b.key, b);
}

S.flags ??= {};
S.flags.showWeatherPinsInBundles ??= false;
export function addOrUpdateBundleMarkers(payload, toleranceMeters = 80) {
    const L = ensureMapReady();
    if (!L) return false;

    console.log("[Bundles] input keys", Object.keys(payload || {}));
    console.log("[Bundles] counts", {
        events: payload?.events?.length,
        places: payload?.places?.length,
        crowds: payload?.crowds?.length,
        suggestions: payload?.suggestions?.length,
        traffic: payload?.traffic?.length,
        weather: payload?.weather?.length,
        gpt: payload?.gpt?.length,
    });

    S.bundleLastInput = payload;

    const tol = Number(toleranceMeters);
    const tolMeters = Number.isFinite(tol) && tol > 0 ? tol : 80;

    const newBundles = computeBundles(payload, tolMeters);

    // remove old bundles
    for (const oldKey of Array.from(S.bundleMarkers.keys())) {
        if (!newBundles.has(oldKey)) {
            const marker = S.bundleMarkers.get(oldKey);
            removeLayerSmart(marker);
            S.bundleMarkers.delete(oldKey);
            S.bundleIndex.delete(oldKey);
        }
    }

    // upsert bundles
    for (const b of newBundles.values()) updateBundleMarker(b);

    // OPTIONAL: show weather individuals in bundles mode
    if (S.hybrid?.showing !== "details" && Array.isArray(payload?.weather)) {
        addOrUpdateWeatherMarkers(payload.weather);
    }

    try { refreshHybridVisibility(); } catch { }

    if (S.cluster && typeof S.cluster.refreshClusters === "function") {
        try { S.cluster.refreshClusters(); } catch { }
    }

    if (S.flags.showWeatherPinsInBundles && S.hybrid?.showing !== "details") {
        addOrUpdateWeatherMarkers(payload?.weather ?? []);
    }

    return true;
}

export function addOrUpdateWeatherMarkers(items) {
    const L = ensureMapReady();
    if (!L) return false;

    if (!Array.isArray(items)) return false;

    for (const w of items) {
        const ll = pickLatLng(w, S.utils);
        if (!ll || !isInBelgium(ll)) continue;

        const id = "wf:" + String(w.Id ?? w.id ?? "");
        if (id === "wf:") continue;

        const level = (w.IsSevere || w.isSevere) ? 4 : 2;

        addOrUpdateCrowdMarker(id, ll.lat, ll.lng, level, {
            title: w.Summary ?? w.summary ?? "Weather",
            description: [
                `Temp: ${w.TemperatureC ?? w.temperatureC ?? "?"}°C`,
                `Hum: ${w.Humidity ?? w.humidity ?? "?"}%`,
                `Vent: ${w.WindSpeedKmh ?? w.windSpeedKmh ?? "?"} km/h`,
                `Pluie: ${w.RainfallMm ?? w.rainfallMm ?? "?"} mm`,
                (w.Description ?? w.description) ? `Desc: ${w.Description ?? w.description}` : null
            ].filter(Boolean).join(" • "),
            weatherType: (w.WeatherType ?? w.weatherType ?? "").toString(),
            isTraffic: false
        });
    }
    return true;
}

export function scheduleBundleRefresh(delayMs = 150, tolMeters = 80) {
    clearTimeout(S._bundleRefreshT);
    S._bundleRefreshT = setTimeout(() => {
        try {
            if (S.bundleLastInput) addOrUpdateBundleMarkers(S.bundleLastInput, tolMeters);
        } catch { }
    }, delayMs);
}

// ------------------------------
// Detail markers + Hybrid zoom
// ------------------------------
function ensureDetailLayer() {
    const L = ensureMapReady();
    if (!L) return null;

    if (!S.detailLayer) {
        S.detailLayer = L.layerGroup();
        S.map.addLayer(S.detailLayer);
    }
    return S.detailLayer;
}

function clearDetailMarkers() {
    try { S.detailLayer?.clearLayers?.(); } catch { }
    try { S.detailMarkers?.clear?.(); } catch { }
}

function makeDetailKey(kind, item) {
    const id = item?.Id ?? item?.id ?? (globalThis.crypto?.randomUUID?.() ?? String(Math.random()));
    return `${kind}:${id}`;
}

function addDetailMarker(kind, item) {
    const L = ensureMapReady();
    if (!L) return;

    const layer = ensureDetailLayer();
    if (!layer) return;

    let ll = null;

    if (String(kind).toLowerCase() === "weather") {
        // Weather: try item coords, else fallback to linked place coords if any
        const placeId = item?.PlaceId ?? item?.placeId;
        const place = placeId != null ? (S.bundleLastInput?.places ?? []).find(p => (p?.Id ?? p?.id) == placeId) : null;

        ll = pickLatLng(item, S.utils) ?? pickLatLng(place, S.utils);
    } else {
        ll = pickLatLng(item, S.utils);
    }

    if (!ll || !isInBelgium(ll)) return;

    const key = makeDetailKey(kind, item);
    if (S.detailMarkers.has(key)) return;

    const title =
        s(item.Summary) || s(item.summary) ||
        s(item.Description) || s(item.description) ||
        s(item.Title) || s(item.title) ||
        s(item.Name) || s(item.name) ||
        s(item.LocationName) || s(item.locationName) ||
        s(item.RoadName) || s(item.roadName) ||
        s(item.Message) || s(item.message) ||
        s(item.Prompt) || s(item.prompt);

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

        m.bindTooltip(`Weather: ${title}`, { sticky: true, opacity: 0.95 });
        m.bindPopup(buildPopupHtml({
            title: item?.Summary ?? item?.summary ?? title,
            description: `Temp: ${item?.TemperatureC ?? item?.temperatureC ?? "?"}°C • Vent: ${item?.WindSpeedKmh ?? item?.windSpeedKmh ?? "?"} km/h`
        }));

        if (String(kind).toLowerCase() === "weather") {
            console.log("[DETAILS] add weather", makeDetailKey(kind, item));
            layer.addLayer(m);
            S.detailMarkers.set(key, m);
            return;
        }

        layer.addLayer(m);
        S.detailMarkers.set(key, m);
        return;
    }

    // other kinds: keep circleMarker (or convert later)
    const m = L.circleMarker([ll.lat, ll.lng], { radius: 7, className: "oz-detail-marker" });
    m.bindTooltip(`${kind}: ${title}`, { sticky: true, opacity: 0.95 });
    m.bindPopup(`<div class="oz-popup"><b>${S.utils.escapeHtml(kind)}</b><br>${S.utils.escapeHtml(title)}</div>`);

    layer.addLayer(m);
    S.detailMarkers.set(key, m);
}

export function forceDetailsMode() {
    if (!S.map) return false;
    S.hybrid.enabled = true;
    // Option 1: Lower the threshold just to force details
    // S.hybrid.threshold = 1;

    // Option 2: Just zoom in above the current threshold
    const th = Number(S.hybrid.threshold) || 13;
    if (S.map.getZoom() < th) S.map.setZoom(th);
    // triggers hybrid logic
    try { refreshHybridVisibility(); } catch { }
    return true;
}


function getLatLngAny(p) {
    if (!p) return null;

    const lat = S.utils.safeNum(p.Latitude ?? p.Lat ?? p.lat);
    const lng = S.utils.safeNum(p.Longitude ?? p.Lon ?? p.lng ?? p.lon);

    if (lat == null || lng == null) return null;
    return { lat, lng };
}

function addOrUpdateWeatherMarkerStandalone(w) {
    const L = ensureMapReady();
    if (!L) return;

    const ll = pickLatLng(w, S.utils);
    if (!ll || !isInBelgium(ll)) return;

    const id = "wf:" + String(w.Id ?? w.id ?? "");
    if (id === "wf:") return;

    const level = (w.IsSevere || w.isSevere) ? 4 : 2;

    const icon = buildMarkerIcon(L, level, {
        weatherType: (w.WeatherType ?? w.weatherType ?? "").toString(),
        isTraffic: false
    });

    const popupHtml = buildPopupHtml({
        title: w.Summary ?? w.summary ?? "Weather",
        description: `Temp: ${w.TemperatureC ?? w.temperatureC ?? "?"}°C`,
        weatherType: (w.WeatherType ?? w.weatherType ?? "").toString(),
    });

    // upsert
    const key = String(id);
    const existing = S.markers.get(key);
    if (existing) {
        try { existing.setLatLng([ll.lat, ll.lng]); } catch { }
        try { existing.setIcon(icon); } catch { }
        try { existing.setPopupContent(popupHtml); } catch { }
        return;
    }

    const m = L.marker([ll.lat, ll.lng], {
        title: w.Summary ?? w.summary ?? key,
        riseOnHover: true,
        icon,
        __ozNoCluster: true,          // ✅ bypass cluster
        pane: "markerPane"
    }).bindPopup(popupHtml);

    addLayerSmart(m);
    S.markers.set(key, m);
}

export function addOrUpdateDetailMarkers(payload) {
    const L = ensureMapReady();
    if (!L) return false;

    clearDetailMarkers();

    let counts = { Event: 0, Place: 0, Crowd: 0, Traffic: 0, Weather: 0, Suggestion: 0, GPT: 0 };

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;
        for (const x of arr) {
            counts[kind]++;
            addDetailMarker(kind, x);
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
            addDetailMarker("weather", w); // force lowercase
        }
    }

    console.log("[DETAILS] counts", counts, "detailMarkers.size", S.detailMarkers.size);
    console.log("[DETAILS] weather sample", payload?.weather?.[0]);

    return true;
}

export function enableHybridZoom({ threshold = 13 } = {}) {
    const L = ensureMapReady();
    if (!L) return false;

    S.hybrid.enabled = true;
    S.hybrid.threshold = Number(threshold) || 13;

    if (!S._hybridBound) {
        S._hybridBound = true;
        S.map.on("zoomend", refreshHybridVisibility);
    }

    refreshHybridVisibility();
    return true;
}

function refreshHybridVisibility() {
    if (!S.map || !S.hybrid?.enabled) return;

    requestAnimationFrame(() => {
        const z = S.map.getZoom();
        const wantDetails = z >= S.hybrid.threshold;

        if (wantDetails && S.hybrid.showing !== "details") {
            // hide bundles
            for (const m of S.bundleMarkers.values()) { try { S.map.removeLayer(m); } catch { } }

            // ✅ purge weather markers that were added to cluster in past runs
            purgeClusterWeatherMarkers();

            // hide cluster markers (important for “propre”)
            try { if (S.cluster && S.map.hasLayer(S.cluster)) S.map.removeLayer(S.cluster); } catch { }

            ensureDetailLayer();
            if (S.bundleLastInput) addOrUpdateDetailMarkers(S.bundleLastInput);
            S.hybrid.showing = "details";
        }
        else if (!wantDetails && S.hybrid.showing !== "bundles") {
            // hide details
            if (S.detailLayer) { try { S.map.removeLayer(S.detailLayer); } catch { } }
            clearDetailMarkers();

            // show cluster back
            try { if (S.cluster && !S.map.hasLayer(S.cluster)) S.map.addLayer(S.cluster); } catch { }

            // show bundles
            for (const m of S.bundleMarkers.values()) { try { S.map.addLayer(m); } catch { } }
            S.hybrid.showing = "bundles";
        }
    });
}

function purgeClusterWeatherMarkers() {
    // Removes any wf:* markers that might have been added earlier into the cluster registry
    try {
        for (const [k, m] of S.markers.entries()) {
            if (!String(k).startsWith("wf:")) continue;
            try { removeLayerSmart(m); } catch { }
            S.markers.delete(k);
        }
    } catch { }
}

// ------------------------------
// Incremental weather bundle input
// ------------------------------
export function upsertWeatherIntoBundleInput(delta) {
    // delta:
    //  - { action:"upsert", item:{ Id, Latitude, Longitude, ... } }
    //  - { action:"delete", id:123 }
    //  - OR directly a weather item (compat).

    S.bundleLastInput ??= { events: [], places: [], crowds: [], traffic: [], weather: [], suggestions: [], gpt: [] };
    S._weatherById ??= new Map();

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
        S._weatherById.delete(key);
        S.bundleLastInput.weather = Array.from(S._weatherById.values());
        return true;
    }

    const raw = item;
    if (!raw) return false;

    const wid = raw.Id ?? raw.id;
    if (wid == null) return false;

    const ll = pickLatLng(raw, S.utils);

    if (!ll || !isInBelgium(ll)) {
        S._weatherById.delete(String(wid));
        S.bundleLastInput.weather = Array.from(S._weatherById.values());
        return true;
    }

    const normalized = {
        ...raw,
        Id: wid,
        Latitude: raw.Latitude ?? raw.latitude ?? ll.lat,
        Longitude: raw.Longitude ?? raw.longitude ?? ll.lng,
    };

    S._weatherById.set(String(wid), normalized);
    S.bundleLastInput.weather = Array.from(S._weatherById.values());
    return true;
}

export function refreshHybridNow() {
    try { refreshHybridVisibility(); } catch { }
    return true;
}

export function fitToBundles(padding = 30) {
    const L = ensureMapReady();
    if (!L) return false;

    const pts = [];
    for (const b of S.bundleIndex.values()) {
        if (Number.isFinite(b.lat) && Number.isFinite(b.lng)) pts.push([b.lat, b.lng]);
    }
    if (pts.length === 0) return false;

    const bounds = L.latLngBounds(pts);
    try { S.map.fitBounds(bounds.pad(0.1), { padding: [pad, pad], animate: false, maxZoom: 17 }); } catch { }

    const ms = Array.from(S.bundleMarkers?.values?.() ?? []);
    if (ms.length === 0) return false;

    const latlngs = [];
    for (const m of ms) { try { latlngs.push(m.getLatLng()); } catch { } }
    if (latlngs.length === 0) return false;

    const b = L.latLngBounds(latlngs).pad(0.1);
    S.map.fitBounds(b, { padding: [padding, padding], maxZoom: 16, animate: false });
    return true;
}
export function debugDumpMarkers() {
    console.log("[DBG] markers keys =", Array.from(S.markers.keys()));
    console.log("[DBG] bundle keys =", Array.from(S.bundleMarkers.keys()));
    console.log("[DBG] map initialized =", !!S.map, "cluster =", !!S.cluster);
}

export function elementExists(id) {
    return !!document.getElementById(id);
}


// ------------------------------
// Legacy bridge (optional)
// ------------------------------
globalThis.OutZenInterop ??= {};
globalThis.OutZenInterop.bootOutZen = bootOutZen;
globalThis.OutZenInterop.isOutZenReady = isOutZenReady;
globalThis.OutZenInterop.disposeOutZen = disposeOutZen;

globalThis.OutZenInterop.addOrUpdateCrowdMarker = addOrUpdateCrowdMarker;
globalThis.OutZenInterop.removeCrowdMarker = removeCrowdMarker;
globalThis.OutZenInterop.clearCrowdMarkers = clearCrowdMarkers;
globalThis.OutZenInterop.fitToMarkers = fitToMarkers;
globalThis.OutZenInterop.refreshMapSize = refreshMapSize;

globalThis.OutZenInterop.addOrUpdateBundleMarkers = addOrUpdateBundleMarkers;
globalThis.OutZenInterop.updateBundleMarker = updateBundleMarker;
globalThis.OutZenInterop.scheduleBundleRefresh = scheduleBundleRefresh;

globalThis.OutZenInterop.enableHybridZoom = enableHybridZoom;
globalThis.OutZenInterop.addOrUpdateDetailMarkers = addOrUpdateDetailMarkers;

globalThis.OutZenInterop.setWeatherChart = setWeatherChart;
globalThis.OutZenInterop.upsertWeatherIntoBundleInput = upsertWeatherIntoBundleInput;
globalThis.OutZenInterop.debugClusterCount = debugClusterCount;
globalThis.OutZenInterop.addOrUpdateWeatherMarkers = addOrUpdateWeatherMarkers;
globalThis.OutZenInterop.activateHybridAndZoom = activateHybridAndZoom;

globalThis.OutZenInterop.forceDetailsMode = forceDetailsMode;
globalThis.OutZenInterop.refreshHybridNow = refreshHybridNow;





















































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/