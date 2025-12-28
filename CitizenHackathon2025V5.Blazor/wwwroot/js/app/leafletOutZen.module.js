// wwwroot/js/app/leafletOutZen.module.js
/* global L, Chart */
"use strict";

/* =========================================================
   OutZen Leaflet Module (ESM)
   - Hot reload safe singleton
   - bootOutZen(map)
   - Crowd/GPT markers
   - Bundle markers (grouped by proximity)
   - Hybrid zoom: bundles far, details near
   ========================================================= */

// ------------------------------
// Singleton (Hot Reload safe)
// ------------------------------
function getS() {
    globalThis.__OutZenSingleton ??= {
        version: "2025.12.23-clean",
        initialized: false,
        bootTs: 0,

        map: null,
        cluster: null,
        chart: null,

        markers: new Map(),       // id -> leaflet marker (crowd/gpt/traffic/weather)
        bundleMarkers: new Map(), // key -> leaflet marker (bundle)
        bundleIndex: new Map(),   // key -> bundle object

        // detail mode
        detailLayer: null,
        detailMarkers: new Map(),
        hybrid: { enabled: false, threshold: 13, showing: null },

        // last payload
        bundleLastInput: null,

        // registries
        consts: {},
        fns: {},
        utils: {},
    };

    return globalThis.__OutZenSingleton;
}
const S = getS();
globalThis.__OutZenGetS ??= () => getS();

// ------------------------------
// Consts / Utils (idempotent)
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
//function readLevel(x) {
//    const n = Number(
//        x?.CrowdLevel ?? x?.crowdLevel ??
//        x?.TrafficLevel ?? x?.trafficLevel ??
//        x?.Level ?? x?.level ??
//        0
//    );
//    if (!Number.isFinite(n)) return 0;
//    return Math.max(0, Math.min(4, n));
//}

//function bundleSeverity(b) {
//    let s = 0;
//    for (const c of (b.crowds ?? [])) s = Math.max(s, readLevel(c));
//    for (const t of (b.traffic ?? [])) s = Math.max(s, readLevel(t));
//    return s; // 0..4
//}

function isDetailsModeNow() {
    if (!S.map) return false;
    if (!S.hybrid?.enabled) return false;
    const z = S.map.getZoom();
    return z >= (Number(S.hybrid.threshold) || 13);
}

function ensureLeaflet() {
    const Leaflet = globalThis.L;
    if (!Leaflet) {
        console.error("[OutZen] ❌ window.L not found (Leaflet not loaded).");
        return null;
    }
    return Leaflet;
}

function ensureContainer(mapId) {
    const el = document.getElementById(mapId);
    if (!el) console.warn("[OutZen] ❌ Map container not found:", mapId);
    return el;
}

function ensureMapReady() {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return null;
    if (!S.map) {
        console.warn("[OutZen] map not ready (S.map is null).");
        return null;
    }
    return Leaflet;
}

function ensureChartCanvas() {
    const canvas = document.getElementById("crowdChart");
    if (!canvas) {
        console.warn("[OutZen] crowdChart canvas not found; chart disabled.");
        return null;
    }
    return canvas;
}

function destroyChartIfAny() {
    if (S.chart && typeof S.chart.destroy === "function") {
        try { S.chart.destroy(); } catch (e) { console.warn("[OutZen] chart.destroy failed:", e); }
    }
    S.chart = null;
}

function resetLeafletDomId(mapId) {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return;
    const dom = Leaflet.DomUtil.get(mapId);
    if (dom && dom._leaflet_id) {
        try { delete dom._leaflet_id; } catch { dom._leaflet_id = undefined; }
    }
}

function addLayerSmart(layer) {
    // bypass cluster if asked
    if (layer?.options?.__ozNoCluster) {
        S.map.addLayer(layer);
        return;
    }
    if (S.cluster && typeof S.cluster.addLayer === "function") S.cluster.addLayer(layer);
    else S.map.addLayer(layer);
}

function removeLayerSmart(layer) {
    if (!layer) return;

    if (layer?.options?.__ozNoCluster) {
        try { S.map.removeLayer(layer); } catch { }
        return;
    }
    if (S.cluster && typeof S.cluster.removeLayer === "function") S.cluster.removeLayer(layer);
    else if (S.map && typeof S.map.removeLayer === "function") S.map.removeLayer(layer);
}

function isInBelgium(ll) {
    const BE = S.consts.BELGIUM;
    return !!ll &&
        Number.isFinite(ll.lat) && Number.isFinite(ll.lng) &&
        ll.lat >= BE.minLat && ll.lat <= BE.maxLat &&
        ll.lng >= BE.minLng && ll.lng <= BE.maxLng;
}

// Parse lat/lng robustly (supports nested Location/position)
function pickLatLng(obj) {
    const lat = S.utils.safeNum(
        obj?.Latitude ?? obj?.latitude ?? obj?.lat ?? obj?.Lat ?? obj?.LAT ??
        obj?.Location?.Latitude ?? obj?.location?.latitude ??
        obj?.position?.lat ?? obj?.position?.Lat
    );

    let lng = S.utils.safeNum(
        obj?.Longitude ?? obj?.longitude ?? obj?.lng ?? obj?.Lng ?? obj?.LNG ??
        obj?.Lon ?? obj?.lon ??
        obj?.Location?.Longitude ?? obj?.location?.longitude ??
        obj?.position?.lng ?? obj?.position?.Lng
    );

    if (lat == null || lng == null) return null;

    // normalize 0..360 -> -180..180
    if (lng > 180 && lng <= 360) lng -= 360;
    while (lng > 180) lng -= 360;
    while (lng < -180) lng += 360;

    // rejects
    if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;
    return { lat, lng };
}

// ------------------------------
// Public API
// ------------------------------
export function isOutZenReady() {
    return !!S.initialized && !!S.map;
}

/**
 * Boot Leaflet map
 * options: { mapId, center:[lat,lng], zoom, enableChart, force, enableWeatherLegend }
 */
export async function bootOutZen(options) {
    const {
        mapId = "leafletMap",
        center = [50.45, 4.6],
        zoom = 12,
        enableChart = false,
        force = true,
        enableWeatherLegend = false,
    } = options || {};

    const Leaflet = ensureLeaflet();
    if (!Leaflet) return false;

    const host = ensureContainer(mapId);
    if (!host) {
        console.info("[OutZen] bootOutZen skipped: container missing:", mapId);
        return false;
    }

    if (S.map && !force) {
        try {
            // container might have resized after render
            setTimeout(() => { try { S.map.invalidateSize(true); } catch { } }, 0);
            // optional: keep user zoom if you want, or setView if you need to
            // S.map.setView(center, zoom, { animate: false });
        } catch { }
        console.info("[OutZen] bootOutZen reused existing map.");
        S.initialized = true;
        return true;
    }

    console.log("[OutZen] bootOutZen init…", { mapId, center, zoom, force });

    // clean old map
    if (S.map && force) {
        try { S.map.remove(); } catch (e) { console.warn("[OutZen] map.remove failed:", e); }
        S.map = null;
    }
    resetLeafletDomId(mapId);

    disposeOutZen({ mapId });

    // create
    S.map = Leaflet.map(mapId).setView(center, zoom);

    Leaflet.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "© OpenStreetMap contributors",
        maxZoom: 19,
    }).addTo(S.map);

    // cluster (optional)
    if (Leaflet.markerClusterGroup) {
        if (!S.cluster) {
            S.cluster = Leaflet.markerClusterGroup({
                disableClusteringAtZoom: 18,
                spiderfyOnMaxZoom: true,
                removeOutsideVisibleBounds: false,
                maxClusterRadius: 60,
            });
        } else {
            S.cluster.clearLayers();
        }
        S.map.addLayer(S.cluster);
        console.log("[OutZen] MarkerCluster enabled.");
    } else {
        S.cluster = null;
        console.warn("[OutZen] MarkerCluster plugin not found.");
    }

    // reset state
    S.markers = new Map();

    // legend (optional)
    if (enableWeatherLegend) {
        try {
            const legend = createWeatherLegendControl(Leaflet);
            legend.addTo(S.map);
            S.weatherLegend = legend;
        } catch (e) {
            console.warn("[OutZen] weather legend failed:", e);
        }
    }

    // chart (optional)
    destroyChartIfAny();
    if (enableChart && globalThis.Chart) {
        const canvas = ensureChartCanvas();
        if (canvas) {
            const ctx = canvas.getContext("2d");
            S.chart = new Chart(ctx, {
                type: "bar",
                data: { labels: [], datasets: [{ label: "Crowd Level", data: [] }] },
                options: { responsive: true, animation: false, scales: { y: { beginAtZero: true, max: 5 } } },
            });
            console.log("[OutZen] Chart initialized.");
        }
    }

    // nav resize hook
    if (!S._navListenerBound) {
        S._navListenerBound = true;
        window.addEventListener("outzen:nav", () => {
            if (S.map && typeof S.map.invalidateSize === "function") {
                setTimeout(() => { try { S.map.invalidateSize(true); } catch { } }, 50);
            }
        });
        window.dispatchEvent(new Event("outzen:nav"));
    }

    S.initialized = true;
    S.bootTs = Date.now();
    globalThis.__outzenBootFlag = true;

    console.log("[OutZen] ✅ bootOutZen completed.");
    return true;
}

export function disposeOutZen({ mapId = "leafletMap" } = {}) {
    const L = ensureLeaflet();

    // 1) Detach hybrid listener safely
    try {
        if (S.map && S._hybridBound) {
            S.map.off("zoomend", refreshHybridVisibility);
        }
    } catch { }

    S._hybridBound = false;
    if (S.hybrid) S.hybrid.showing = null;

    // 2) Remove layers (detail layer + clusters) before removing map
    try {
        if (S.detailLayer && S.map) S.map.removeLayer(S.detailLayer);
    } catch { }
    S.detailLayer = null;
    S.detailMarkers?.clear?.();

    try {
        if (S.cluster) {
            try { S.cluster.clearLayers(); } catch { }
            if (S.map) { try { S.map.removeLayer(S.cluster); } catch { } }
        }
    } catch { }
    S.cluster = null;

    // 3) Stop animations + remove map
    try {
        if (S.map) {
            try { S.map.stop(); } catch { }
            try { S.map.off(); } catch { }
            try { S.map.remove(); } catch { }
        }
    } finally {
        S.map = null;
    }

    // 4) Reset container leaflet id (important for re-create)
    try {
        const el = document.getElementById(mapId);
        if (el && el._leaflet_id) {
            try { delete el._leaflet_id; } catch { el._leaflet_id = undefined; }
        }
    } catch { }

    // 5) Clear caches
    destroyChartIfAny();
    S.markers?.clear?.();
    S.bundleMarkers?.clear?.();
    S.bundleIndex?.clear?.();
    S.bundleLastInput = null;

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
    if (!S.chart || !Array.isArray(points)) {
        console.warn("[OutZen] setWeatherChart: no chart or invalid points");
        return;
    }

    const metric = (metricType || "Temperature").toLowerCase();
    const labels = [];
    const values = [];
    const bgColors = [];

    let datasetLabel = "Temperature (°C)";
    if (metric === "humidity") datasetLabel = "Humidity (%)";
    else if (metric === "wind") datasetLabel = "Wind (km/h)";

    for (const p of points) {
        labels.push(p.label ?? "");

        const isSevere = !!p.isSevere;
        const t = Number(p.temperature ?? p.value ?? 0);
        const h = Number(p.humidity ?? 0);
        const w = Number(p.windSpeed ?? 0);

        let val = t || Number(p.value) || 0;
        if (metric === "humidity") val = h || Number(p.value) || 0;
        if (metric === "wind") val = w || Number(p.value) || 0;

        values.push(val);

        let color;
        if (isSevere) color = "rgba(198,40,40,0.95)";
        else if (metric === "humidity") color = val <= 30 ? "rgba(255,193,7,0.9)" : "rgba(76,175,80,0.85)";
        else if (metric === "wind") color = val < 30 ? "rgba(3,169,244,0.85)" : "rgba(244,67,54,0.95)";
        else color = t <= 0 ? "rgba(33,150,243,0.85)" : (t <= 25 ? "rgba(255,193,7,0.9)" : "rgba(244,67,54,0.9)");

        bgColors.push(color);
    }

    const borderColors = bgColors.map(c => c.replace("0.85", "1").replace("0.9", "1").replace("0.95", "1"));
    const ds = S.chart.data.datasets[0];

    S.chart.data.labels = labels;
    ds.label = datasetLabel;
    ds.data = values;
    ds.backgroundColor = bgColors;
    ds.borderColor = borderColors;
    ds.borderWidth = 1.5;

    S.chart.update();
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

    return L.divIcon({
        className: `oz-marker ${lvlClass} ${trafficClass}`.trim(),
        html: `<div class="oz-marker-inner">${emoji}</div>`,
        iconSize: [26, 26],
        iconAnchor: [13, 26],
        popupAnchor: [0, -26],
    });
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
    if (!Number.isFinite(latNum) || !Number.isFinite(lngNum)) {
        console.warn("[OutZen] addOrUpdateCrowdMarker invalid coords", { id, lat, lng });
        return;
    }

    const key = String(id);
    const existing = S.markers.get(key);

    const popupHtml = buildPopupHtml(info ?? {});
    const icon = buildMarkerIcon(Leaflet, level, {
        isTraffic: !!info?.isTraffic,
        weatherType: info?.weatherType ?? info?.WeatherType ?? null,
        iconOverride: info?.icon ?? info?.Icon ?? null,
    });

    if (existing) {
        existing.setLatLng([latNum, lngNum]);
        existing.setPopupContent(popupHtml);
        try { existing.setIcon(icon); } catch { }
        return;
    }

    const marker = Leaflet.marker([latNum, lngNum], { title: info?.title ?? key, riseOnHover: true, icon })
        .bindPopup(popupHtml);

    if (S.cluster) S.cluster.addLayer(marker);
    else marker.addTo(S.map);

    S.markers.set(key, marker);
}

export function removeCrowdMarker(id) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return;

    const key = String(id);
    const marker = S.markers.get(key);
    if (!marker) return;

    if (S.cluster) S.cluster.removeLayer(marker);
    else S.map.removeLayer(marker);

    S.markers.delete(key);
}

export function clearCrowdMarkers() {
    if (!S.map) return;

    if (S.cluster) S.cluster.clearLayers();
    else for (const m of S.markers.values()) S.map.removeLayer(m);

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
        S.map.setView(latlngs[0], 15, { animate: true });
        return;
    }

    const b = Leaflet.latLngBounds(latlngs).pad(0.1);
    S.map.fitBounds(b, { padding: [32, 32], maxZoom: 17 });
}

export function debugDumpMarkers() {
    console.log("[OutZen] map =", !!S.map, "markers =", S.markers?.size ?? 0);
}

export function refreshMapSize() {
    if (!S.map) return;
    setTimeout(() => { try { S.map.invalidateSize(); } catch { } }, 50);
}

// Example alert marker
export function notifyHeavyRain(alert) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return;

    const ll = pickLatLng(alert);
    if (!ll) {
        console.warn("[OutZen] notifyHeavyRain missing coords:", alert);
        return;
    }

    const msg = alert.message ?? alert.Message ?? "Heavy rain alert";

    const marker = Leaflet.circleMarker([ll.lat, ll.lng], { radius: 12, className: "rain-alert-marker" })
        .addTo(S.map);

    marker.bindPopup(`<div class="rain-alert-popup"><strong>🌧 Heavy rain alert</strong><br/>${S.utils.escapeHtml(msg)}</div>`)
        .openPopup();

    const z = S.map.getZoom?.() ?? 10;
    S.map.setView([ll.lat, ll.lng], Math.max(z, 11), { animate: true });
}

// ------------------------------
// GPT markers (reuse crowd marker)
// ------------------------------
export function addOrUpdateGptMarker(dto) {
    if (!dto) return;

    const id = dto.id ?? dto.Id;
    const lat = dto.lat ?? dto.Latitude ?? dto.latitude;
    const lng = dto.lng ?? dto.Longitude ?? dto.longitude;

    const crowdLevel = dto.crowdLevel ?? dto.CrowdLevel ?? 3;
    const title = dto.title ?? dto.Title ?? dto.Prompt ?? `[GPT] #${id}`;
    const description = dto.description ?? dto.Description ?? dto.Response ?? "";

    addOrUpdateCrowdMarker(id, Number(lat), Number(lng), crowdLevel, { title, description, icon: "🤖" });
    /*addOrUpdateCrowdMarker(id, lat, lng, trafficLevel, { title, description, isTraffic: true });*/
}

export function removeGptMarker(id) {
    removeCrowdMarker(id);
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
    return clampLevel14(
        item?.CrowdLevel ?? item?.crowdLevel ?? item?.Level ?? item?.level
    );
}

function pickTrafficLevel(item) {
    return clampLevel14(
        item?.TrafficLevel ?? item?.trafficLevel ?? item?.CongestionLevel ?? item?.level
    );
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

    return sev; // 1..4
}

function bundleTotal(b) {
    const d = bundleBreakdown(b);
    return d.events + d.places + d.crowds + d.traffic + d.weather + d.suggestions + d.gpt;
}

function makeBadgeIcon(totalCount, breakdown, severity = 0) {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return null;

    const lvlClass =
        severity === 1 ? "oz-bundle-lvl1" :
        severity === 2 ? "oz-bundle-lvl2" :
        severity === 3 ? "oz-bundle-lvl3" :
        severity === 4 ? "oz-bundle-lvl4" : "oz-bundle-lvl0";

    const html = `
    <div class="oz-bundle ${lvlClass}">
      <div class="oz-badge">${totalCount}</div>
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
      ${section("Weather", b.weather, (w) => row("Weather", w))}
      ${section("Suggestions", b.suggestions, (s) => row("Suggestion", s))}
      ${section("GPT", b.gpt, (g) => row("GPT", g))}
    </div>
  `.trim();
}

function computeBundles(payload, tolMeters) {
    const buckets = new Map();

    // index des places par id
    const placesArr = Array.isArray(payload?.places) ? payload.places : [];
    const placeById = new Map(
        placesArr
            .map(p => [p?.Id ?? p?.id, p])
            .filter(([id]) => id != null)
    );

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;

        for (const item of arr) {

            // ✅ 1) If it's an event linked to a location, we take the location as the source of coordinates.
            const placeId = item?.PlaceId ?? item?.placeId;
            const place = placeId != null ? placeById.get(placeId) : null;

            const ll = pickLatLng(place ?? item);
            if (!ll) continue;
            if (!isInBelgium(ll)) continue;
            // possibly:
            if (Math.abs(ll.lng) === 180) continue;


            console.log("event sample", payload?.events?.[0]);

            if (ll.lng === 0 || ll.lng === 180 || ll.lng === -180) continue;
            const key = bundleKeyFor(ll.lat, ll.lng, tolMeters);

            let b = buckets.get(key);
            if (!b) {
                b = { key, lat: ll.lat, lng: ll.lng, events: [], places: [], crowds: [], traffic: [], weather: [], suggestions: [], gpt: [] };
                buckets.set(key, b);
            }
            
            b[kind].push(item);
        }
    };

    push(payload?.places, "places");
    push(payload?.events, "events");      // events after places to take advantage of placeById
    push(payload?.crowds, "crowds");
    push(payload?.traffic, "traffic");
    push(payload?.weather, "weather");
    push(payload?.suggestions, "suggestions");
    push(payload?.gpt, "gpt");

    return buckets;
}


export function updateBundleMarker(b) {
    const L = ensureMapReady();
    if (!L) return;

    const total = bundleTotal(b);
    const breakdown = bundleBreakdown(b);
    const sev = bundleSeverity(b); // 1..4
    const existing = S.bundleMarkers.get(b.key);
    const detailsMode = isDetailsModeNow();

    // remove if empty
    if (total <= 0) {
        if (existing) {
            removeLayerSmart(existing);
            S.bundleMarkers.delete(b.key);
            S.bundleIndex.delete(b.key);
        }
        return;
    }

    const icon = makeBadgeIcon(total, breakdown, sev);
    const popup = bundlePopupHtml(b);

    if (!existing) {
        const m = L.marker([b.lat, b.lng], {
            icon,
            pane: "markerPane",
            title: `Area (${total})`,
            riseOnHover: true,
            __ozNoCluster: true, // bundles bypass cluster
        });

        m.bindPopup(popup, { maxWidth: 420, closeButton: true, autoPan: true });
        m.bindTooltip(`Area • ${total} elements`, { direction: "top", sticky: true, opacity: 0.95 });

        // show only if not in details mode
        if (!detailsMode) addLayerSmart(m);

        S.bundleMarkers.set(b.key, m);
        S.bundleIndex.set(b.key, b);
        return;
    }

    // update existing
    existing.setLatLng([b.lat, b.lng]);
    if (icon) existing.setIcon(icon);
    if (existing.getPopup()) existing.setPopupContent(popup);

    // keep visibility consistent with mode
    try {
        if (detailsMode) {
            if (S.map.hasLayer(existing)) S.map.removeLayer(existing);
        } else {
            if (!S.map.hasLayer(existing)) S.map.addLayer(existing);
        }
    } catch { }

    S.bundleIndex.set(b.key, b);
}
export function addOrUpdateBundleMarkers(payload, toleranceMeters = 80) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

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

    // upsert
    for (const b of newBundles.values()) updateBundleMarker(b);

    try { refreshHybridVisibility(); } catch { }

    if (S.cluster && typeof S.cluster.refreshClusters === "function") {
        try { S.cluster.refreshClusters(); } catch { }
    }

    console.info(`[OutZen][Bundle] updated: ${newBundles.size} bundles (tol=${tolMeters}m)`);
    try { fitToBundles(24); } catch { }

    return true;
}

const EPS_180 = 1e-6;
export function fitToBundles(padding = 24) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

    const pts = Array.from(S.bundleMarkers.values())
        .map(m => m?.getLatLng?.())
        .filter(ll =>
            ll &&
            Number.isFinite(ll.lat) && Number.isFinite(ll.lng) &&
            Math.abs(ll.lat) <= 90 &&
            Math.abs(ll.lng) <= 180 &&
            Math.abs(Math.abs(ll.lng) - 180) > EPS_180 &&
            isInBelgium(ll)
        );

    if (pts.length === 0) {
        S.map.setView([50.45, 4.6], 12);
        return false;
    }

    const b = Leaflet.latLngBounds(pts);
    S.map.fitBounds(b, { padding: [padding, padding], maxZoom: 16, animate: false });
    return true;
}

// ------------------------------
// Detail markers + Hybrid zoom
// ------------------------------
function ensureDetailLayer() {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return null;

    if (!S.detailLayer) {
        S.detailLayer = Leaflet.layerGroup();
        S.map.addLayer(S.detailLayer);
    }
    return S.detailLayer;
}

function clearDetailMarkers() {
    if (S.detailLayer) S.detailLayer.clearLayers();
    S.detailMarkers.clear();
}

function makeDetailKey(kind, item) {
    const id = item?.Id ?? item?.id ?? (globalThis.crypto?.randomUUID?.() ?? String(Math.random()));
    return `${kind}:${id}`;
}

function addDetailMarker(kind, item) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return;

    const layer = ensureDetailLayer();
    if (!layer) return;

    const ll = pickLatLng(item);
    if (!ll) return;

    const key = makeDetailKey(kind, item);
    if (S.detailMarkers.has(key)) return;

    const title = S.utils.titleOf(kind, item);

    const m = Leaflet.circleMarker([ll.lat, ll.lng], { radius: 7, className: "oz-detail-marker" });
    m.bindTooltip(`${kind}: ${title}`, { sticky: true, opacity: 0.95 });
    m.bindPopup(`<div class="oz-popup"><b>${S.utils.escapeHtml(kind)}</b><br>${S.utils.escapeHtml(title)}</div>`);
    layer.addLayer(m);

    S.detailMarkers.set(key, m);
}

function haversineMeters(a, b) {
    const R = 6371000;
    const toRad = d => d * Math.PI / 180;
    const dLat = toRad(b.lat - a.lat);
    const dLng = toRad(b.lng - a.lng);
    const sa = Math.sin(dLat / 2) ** 2 +
        Math.cos(toRad(a.lat)) * Math.cos(toRad(b.lat)) *
        Math.sin(dLng / 2) ** 2;
    return 2 * R * Math.asin(Math.sqrt(sa));
}


export function addOrUpdateDetailMarkers(payload) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

    clearDetailMarkers();

    const push = (arr, kind) => Array.isArray(arr) && arr.forEach(x => addDetailMarker(kind, x));
    push(payload?.events, "Event");
    push(payload?.places, "Place");
    push(payload?.crowds, "Crowd");
    push(payload?.traffic, "Traffic");
    push(payload?.weather, "Weather");
    push(payload?.suggestions, "Suggestion");
    push(payload?.gpt, "GPT");

    return true;
}

export function enableHybridZoom({ threshold = 13 } = {}) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

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

    const z = S.map.getZoom();
    const wantDetails = z >= S.hybrid.threshold;

    if (wantDetails && S.hybrid.showing !== "details") {
        // hide bundles
        for (const m of S.bundleMarkers.values()) { try { S.map.removeLayer(m); } catch { } }

        // show details
        ensureDetailLayer();
        if (S.bundleLastInput) addOrUpdateDetailMarkers(S.bundleLastInput);

        S.hybrid.showing = "details";
    } else if (!wantDetails && S.hybrid.showing !== "bundles") {
        // hide details
        if (S.detailLayer) { try { S.map.removeLayer(S.detailLayer); } catch { } }
        clearDetailMarkers();

        // show bundles
        for (const m of S.bundleMarkers.values()) { try { S.map.addLayer(m); } catch { } }

        S.hybrid.showing = "bundles";
    }
}

// optional: detail only mode (no bundles)
export function enableAutoDetailMode(thresholdZoom = 15) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

    const thr = Number(thresholdZoom) || 15;

    const apply = () => {
        const z = S.map.getZoom();
        const showDetail = z >= thr;

        for (const m of S.bundleMarkers.values()) {
            if (!m) continue;
            try { showDetail ? S.map.removeLayer(m) : S.map.addLayer(m); } catch { }
        }

        if (showDetail) {
            if (S.bundleLastInput) addOrUpdateDetailMarkers(S.bundleLastInput);
        } else {
            clearDetailMarkers();
        }
    };

    S.map.off("zoomend", apply);
    S.map.on("zoomend", apply);
    apply();
    return true;
}

// ------------------------------
// Legacy bridge (optional)
// - safe even in ESM
// - but your outzen-interop already does Object.assign(OutZenInterop, module)
// ------------------------------
globalThis.OutZenInterop ??= {};
globalThis.OutZenInterop.bootOutZen = bootOutZen;
globalThis.OutZenInterop.isOutZenReady = isOutZenReady;
globalThis.OutZenInterop.disposeOutZen = disposeOutZen;
globalThis.OutZenInterop.addOrUpdateCrowdMarker = addOrUpdateCrowdMarker;
globalThis.OutZenInterop.removeCrowdMarker = removeCrowdMarker;
globalThis.OutZenInterop.clearCrowdMarkers = clearCrowdMarkers;
globalThis.OutZenInterop.fitToMarkers = fitToMarkers;
globalThis.OutZenInterop.addOrUpdateGptMarker = addOrUpdateGptMarker;
globalThis.OutZenInterop.removeGptMarker = removeGptMarker;
globalThis.OutZenInterop.addOrUpdateBundleMarkers = addOrUpdateBundleMarkers;
globalThis.OutZenInterop.fitToBundles = fitToBundles;
globalThis.OutZenInterop.enableHybridZoom = enableHybridZoom;
globalThis.OutZenInterop.addOrUpdateDetailMarkers = addOrUpdateDetailMarkers;
globalThis.OutZenInterop.enableAutoDetailMode = enableAutoDetailMode;
globalThis.OutZenInterop.refreshMapSize = refreshMapSize;
globalThis.OutZenInterop.notifyHeavyRain = notifyHeavyRain;




















































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/