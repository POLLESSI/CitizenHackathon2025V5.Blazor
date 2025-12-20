// wwwroot/js/app/leafletOutZen.module.js
/* global L, Chart */
"use strict";

window.OutZenInterop = window.OutZenInterop ?? {};
window.OutZenInterop.enableHybridZoom = enableHybridZoom;

// Single global singleton (hot reload safe)
const S = (window.__OutZenSingleton ??= {
    version: "2025.12.09",
    bootTs: 0,
    map: null,
    cluster: null,
    markers: new Map(),
    chart: null,
    initialized: false,
    loggedReeval: false,
    weatherLegend: null,

    // bundles
    bundleMarkers: new Map(),
    bundleIndex: new Map(),
    bundleRadiusMeters: 80,
    bundleLastInput: null,
});

// Dev log only once (optional)
if (!S.loggedReeval && S.bootTs !== 0) {
    console.info("[OutZen] module re-evaluated — reusing singleton");
    S.loggedReeval = true;
}

// Idempotent utils namespace
S.utils ||= {};
S.utils.escapeHtml ||= (v) => {
    if (v == null) return "";
    return String(v)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
};
// Single numeric helper (prevents "Identifier 'safeNum' has already been declared")
S.utils.safeNum ||= (x) => {
    if (x == null) return null;
    if (typeof x === "string") x = x.replace(",", ".");

    const n = Number(x);
    return Number.isFinite(n) ? n : null;
};

// NOTE:
// All "bundle" UI helpers (bundlePopupHtml / bundle icon building / etc.)
// must live ONLY in the "Bundle Markers" section below.
// Keeping them here creates duplicate top-level function declarations and triggers:
//   SyntaxError: Identifier 'bundlePopupHtml' / 'safeNum' has already been declared

// ================================
// Internal helpers
// ================================
function ensureLeaflet() {
    const Leaflet = window.L;
    if (!Leaflet) {
        console.error("[OutZen] ❌ Global Leaflet (window.L) not found. Check /lib/leaflet/leaflet.js in index.html.");
        return null;
    }
    return Leaflet;
}
function ensureContainer(mapId) {
    const el = document.getElementById(mapId);
    if (!el) {
        console.warn("[OutZen] ❌ Map container not found:", mapId);
        return null;
    }
    return el;
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
        try {
            S.chart.destroy();
        } catch (err) {
            console.warn("[OutZen] Failed to destroy previous Chart instance:", err);
        }
    }
    S.chart = null;
}

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
    if (metric === "humidity") {
        datasetLabel = "Humidity (%)";
    } else if (metric === "wind") {
        datasetLabel = "Wind (km/h)";
    }

    for (const p of points) {
        const label = p.label ?? "";
        labels.push(label);

        const isSevere = !!p.isSevere;
        const t = Number(p.temperature ?? p.value ?? 0);
        const h = Number(p.humidity ?? 0);
        const w = Number(p.windSpeed ?? 0);

        // The value is chosen based on the metric.
        let val;
        if (metric === "humidity") {
            val = h || Number(p.value) || 0;
        } else if (metric === "wind") {
            val = w || Number(p.value) || 0;
        } else {
            // default temperature
            val = t || Number(p.value) || 0;
        }
        values.push(val);

        // Bar color
        let color;
        if (isSevere) {
            color = "rgba(198,40,40,0.95)"; // bright red for severe cases
        } else if (metric === "humidity") {
            const isFog = h >= 95 && w <= 10;
            if (isFog) color = "rgba(158,158,158,0.95)";      // fog (grey)
            else if (val <= 30) color = "rgba(255,193,7,0.9)"; // dry -> yellow
            else if (val <= 60) color = "rgba(76,175,80,0.85)";// comfort -> green
            else if (val <= 80) color = "rgba(3,169,244,0.85)";// wet -> light blue
            else color = "rgba(63,81,181,0.9)";                // saturated -> dark blue
        } else if (metric === "wind") {
            if (val < 15) color = "rgba(76,175,80,0.85)";      // calm
            else if (val < 30) color = "rgba(3,169,244,0.85)"; // breeze
            else if (val < 60) color = "rgba(255,152,0,0.9)";  // strong wind
            else color = "rgba(244,67,54,0.95)";               // storm
        } else {
            // Temperature
            if (t <= 0) color = "rgba(33,150,243,0.85)";       // cold (blue)
            else if (t <= 15) color = "rgba(76,175,80,0.85)";  // fresh (green)
            else if (t <= 25) color = "rgba(255,193,7,0.9)";   // soft (yellow)
            else color = "rgba(244,67,54,0.9)";                // hot (red)
        }

        bgColors.push(color);
    }

    const borderColors = bgColors.map(c =>
        c.replace("0.85", "1").replace("0.9", "1").replace("0.95", "1")
    );

    const ds = S.chart.data.datasets[0];

    S.chart.data.labels = labels;
    ds.label = datasetLabel;
    ds.data = values;
    ds.backgroundColor = bgColors;
    ds.borderColor = borderColors;
    ds.borderWidth = 1.5;

    // 🔴 IMPORTANT: S.chart.options.scales is no longer being touched here.
    // The scale remains the one defined at creation (0..5, etc.).
    // When everything is stable, we can reintroduce a more controlled y.min / y.max adjustment.

    S.chart.update();
}


function createWeatherLegendControl(L) {
    const legend = L.control({ position: "bottomright" });

    legend.onAdd = function (map) {
        const div = L.DomUtil.create("div", "oz-weather-legend");
        div.innerHTML = `
            <div class="oz-weather-legend-title">Météo</div>
            <div class="oz-weather-legend-row">
                <span class="oz-weather-emoji">☀️</span><span>Sunny</span>
            </div>
            <div class="oz-weather-legend-row">
                <span class="oz-weather-emoji">☁️</span><span>Cloudy</span>
            </div>
            <div class="oz-weather-legend-row">
                <span class="oz-weather-emoji">🌧️</span><span>Rain</span>
            </div>
            <div class="oz-weather-legend-row">
                <span class="oz-weather-emoji">⛈️</span><span>Stormy</span>
            </div>
            <div class="oz-weather-legend-row">
                <span class="oz-weather-emoji">🌫️</span><span>Foggy</span>
            </div>
            <div class="oz-weather-legend-row">
                <span class="oz-weather-emoji">💨</span><span>Windy</span>
            </div>
            <div class="oz-weather-legend-row">
                <span class="oz-weather-emoji">❄️</span><span>Snowy</span>
            </div>
        `;
        return div;
    };

    return legend;
}


// ================================
// API exported to Blazor
// ================================

export function isOutZenReady() {
    return !!S.initialized && !!S.map;
}

/**
 * OutZen main boot (called from CrowdInfoView.OnAfterRenderAsync)
 * opts: { mapId, center, zoom, enableChart, force }
 */
export async function bootOutZen(options) {
    const {
        mapId = "leafletMap",
        center = [50.89, 4.34],
        zoom = 13,
        enableChart = true,
        force = true,
        enableWeatherLegend = false
    } = options || {};

    const L = ensureLeaflet();
    if (!L) return false;

    const host = document.getElementById(mapId);
    if (!host) {
        console.info(`[OutZen] bootOutZen skipped: #${mapId} not found. Available map divs:`,
            [...document.querySelectorAll("div[id]")].map(x => x.id).slice(0, 50));
        return false;
    }

    const container = ensureContainer(mapId);
    if (!container) return false;

    console.log("[OutZen] bootOutZen: initializing map… (force:", force, ")");

    // --- 1) Clean old map if present ---
    if (S.map && force) {
        try {
            S.map.remove();
        } catch (e) {
            console.warn("[OutZen] map.remove() failed:", e);
        }
        S.map = null;
    }

    // HACK standard Leaflet: reset the flag to allow a new map
    const dom = L.DomUtil.get(mapId);
    if (dom && dom._leaflet_id) {
        dom._leaflet_id = null;
    }

    // --- 2) Re-create map ---
    S.map = L.map(mapId).setView(center, zoom);

    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "© OpenStreetMap contributors",
        maxZoom: 19
    }).addTo(S.map);

    // --- 3) Cluster ---
    if (L.markerClusterGroup) {
        if (!S.cluster) {
            S.cluster = L.markerClusterGroup({
                // At very high zoom, it shows the markers rather than continuing to cluster.
                disableClusteringAtZoom: 18,
                spiderfyOnMaxZoom: true,

                // Avoid "disappearances" related to off-screen optimization
                removeOutsideVisibleBounds: false,

                // Optional: less aggressive clusterS
                maxClusterRadius: 60,
            });
        } else {
            S.cluster.clearLayers();
        }
        S.map.addLayer(S.cluster);
        console.log("[OutZen] MarkerCluster enabled.");
    } else {
        S.cluster = null;
        console.warn("[OutZen] MarkerCluster plugin not found, fallback to plain markers.");
    }

    S.markers = new Map();

    // --- 3b) Weather legend ---
    if (enableWeatherLegend) {
        if (S.weatherLegend) {
            S.map.removeControl(S.weatherLegend);
        }
        S.weatherLegend = createWeatherLegendControl(L);
        S.weatherLegend.addTo(S.map);
    }

    // --- 4) Chart ---
    destroyChartIfAny();

    if (enableChart && window.Chart) {
        const canvas = ensureChartCanvas();
        if (canvas) {
            const ctx = canvas.getContext("2d");
            S.chart = new Chart(ctx, {
                type: "bar",
                data: {
                    labels: [],
                    datasets: [{
                        label: "Crowd Level",
                        data: []
                    }]
                },
                options: {
                    responsive: true,
                    animation: false,
                    scales: {
                        y: { beginAtZero: true, max: 5 }
                    }
                }
            });
            console.log("[OutZen] Chart initialized (or reinitialized).");
        }
    }

    S.initialized = true;
    S.bootTs = Date.now();
    window.__outzenBootFlag = true;

    if (!S._navListenerBound) {
        S._navListenerBound = true;
        window.addEventListener("outzen:nav", () => {
            if (S.map && typeof S.map.invalidateSize === "function") {
                setTimeout(() => { try { S.map.invalidateSize(true); } catch { } }, 50);
            }
        });
    }

    console.log("[OutZen] ✅ bootOutZen completed.");
    return true;

}

// ================================
// Marker icon helpers (crowd / traffic levels)
// ================================
function normalizeLevel(level) {
    const n = Number(level) || 0;
    if (n < 0) return 0;
    if (n > 4) return 4;
    return n;
}

function getMarkerClassForLevel(level) {
    const lvl = normalizeLevel(level);
    // 0 = neutral / unknown
    switch (lvl) {
        case 1: return "oz-marker-lvl1"; // Low / Freeflow
        case 2: return "oz-marker-lvl2"; // Medium / Moderate
        case 3: return "oz-marker-lvl3"; // High / Heavy
        case 4: return "oz-marker-lvl4"; // Critical / Jammed
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

    return "🌡️"; // by default
}

function buildMarkerIcon(
    L,
    level,
    isTraffic = false,
    weatherType = null,
    iconOverride = null
) {
    const lvlClass = getMarkerClassForLevel(level);
    const trafficClass = isTraffic ? "oz-marker-traffic" : "";

    let emoji = "";
    if (iconOverride) {
        emoji = iconOverride;                       // ex: "🚦", "👥", "💡"
    } else if (weatherType) {
        emoji = getWeatherEmoji(weatherType);       // weather only if necessary
    } else {
        emoji = "";                                 // pure colorful circle
    }

    return L.divIcon({
        className: `oz-marker ${lvlClass} ${trafficClass}`.trim(),
        html: `<div class="oz-marker-inner">${emoji}</div>`,
        iconSize: [26, 26],
        iconAnchor: [13, 26],
        popupAnchor: [0, -26]
    });
}
/**
 * Adds or updates a crowd marker.
 */
export function addOrUpdateCrowdMarker(id, lat, lng, level, info) {
    const L = ensureLeaflet();
    if (!L || !S.map) return;

    const latNum = Number(lat);
    const lngNum = Number(lng);

    if (!Number.isFinite(latNum) || !Number.isFinite(lngNum)) {
        console.warn("[OutZen] addOrUpdateCrowdMarker: invalid coords", { id, lat, lng, latNum, lngNum, info });
        return;
    }

    if (!S.markers) S.markers = new Map();

    const key = String(id);
    const existing = S.markers.get(key);

    const popupHtml = buildPopupHtml(info ?? {});

    const isTraffic = info?.isTraffic;
    const weatherType = info?.weatherType ?? info?.WeatherType ?? null;
    const iconOverride = info?.icon ?? info?.Icon ?? null; 

    const icon = buildMarkerIcon(L, level, isTraffic, weatherType, iconOverride);

    if (existing) {
        existing.setLatLng([lat, lng]);
        existing.setPopupContent(popupHtml);
        existing._outzenLevel = level;
        try {
            existing.setIcon(icon); // 🔁 updates the color if the level changes
        } catch (e) {
            console.warn("[OutZen] Failed to update marker icon:", e);
        }
        return;
    }

    const opts = {
        title: info?.title ?? key,
        riseOnHover: true,
        icon // 👈 plus the default blue icon
    };

    const marker = L.marker([latNum, lngNum], opts).bindPopup(popupHtml);
    marker._outzenId = key;
    marker._outzenLevel = level;

    if (S.cluster) {
        S.cluster.addLayer(marker);
    } else {
        marker.addTo(S.map);
    }

    S.markers.set(key, marker);
    console.log("[OutZen] Marker added. Total markers:", S.markers.size);
}
/**
 * Delete a marker.
 */
export function removeCrowdMarker(id) {
    const L = ensureLeaflet();
    if (!L || !S.map || !S.markers) return;

    const key = String(id);
    const marker = S.markers.get(key);
    if (!marker) return;

    if (S.cluster) {
        S.cluster.removeLayer(marker);
    } else {
        S.map.removeLayer(marker);
    }
    S.markers.delete(key);
}

/**
 * Delete all crowd markers.
 */
export function clearCrowdMarkers() {
    if (!S.map || !S.markers) return;

    if (S.cluster) {
        S.cluster.clearLayers();
        if (S.bundleMarkers) S.bundleMarkers.clear();
    } else {
        for (const m of S.markers.values()) {
            S.map.removeLayer(m);
        }
    }
    S.markers.clear();
}

// ================================
// GPT markers (reuse crowd engine)
// ================================
export function addOrUpdateGptMarker(dto) {
    const L = ensureLeaflet();
    if (!L || !S.map || !dto) return;

    const id = dto.id ?? dto.Id;

    const lat = dto.lat ?? dto.Latitude ?? dto.latitude;
    const lng = dto.lng ?? dto.Longitude ?? dto.longitude;

    const latNum = Number(lat);
    const lngNum = Number(lng);

    const crowdLevel = dto.crowdLevel ?? dto.CrowdLevel ?? 3;

    console.log("[OutZen] GPT marker parsed", { id, lat: latNum, lng: lngNum, crowdLevel });

    if (!Number.isFinite(latNum) || !Number.isFinite(lngNum)) {
        console.error("[OutZen] invalid coords", { id, lat, lng, latType: typeof lat, lngType: typeof lng, latNum, lngNum, info });
        return;
    }

    const sourceType = dto.sourceType ?? dto.SourceType ?? "GPT";
    const title = dto.title ?? dto.Title ?? dto.Prompt ?? `[${sourceType}] #${id}`;
    const description = dto.description ?? dto.Description ?? dto.Response ?? "";

    addOrUpdateCrowdMarker(id, latNum, lngNum, crowdLevel, { title, description });

    setTimeout(() => { try { fitToMarkers(); } catch { } }, 50);
}

export function removeGptMarker(id) {
    removeCrowdMarker(id);
}

/**
 * Adjusts the viewport to all markers.
 */
export function fitToMarkers() {
    if (!S.map || !S.markers || S.markers.size === 0) {
        console.log("[OutZen] fitToMarkers: no markers.");
        return;
    }

    const L = ensureLeaflet();
    if (!L) return;

    let bounds = null;

    // 1) If the cluster is present AND contains layers
    if (S.cluster && typeof S.cluster.getLayers === "function") {
        const layers = S.cluster.getLayers();
        if (Array.isArray(layers) && layers.length > 0) {
            const b = S.cluster.getBounds();
            if (b && typeof b.isValid === "function" && b.isValid()) {
                bounds = b;
            }
        }
    }

    // 2) Fallback : We use S.markers markers
    if (!bounds) {
        const latlngs = [];
        for (const m of S.markers.values()) {
            try {
                latlngs.push(m.getLatLng());
            } catch { }
        }
        if (latlngs.length === 0) {
            console.log("[OutZen] fitToMarkers: markers list empty.");
            return;
        }

        if (latlngs.length === 1) {
            const target = latlngs[0];
            const z = Math.min(18, Math.max(3, 15));
            S.map.setView(target, z, { animate: true });
            return;
        }

        bounds = L.latLngBounds(latlngs).pad(0.1);
    }

    S.map.fitBounds(bounds, {
        padding: [32, 32],
        maxZoom: 17
    });
}

export function debugDumpMarkers() {
    console.log("[OutZen] debugDumpMarkers: map initialized =", !!S.map);
    console.log("[OutZen] debugDumpMarkers: markers size =", S.markers ? S.markers.size : "null");
    if (S.markers && S.markers.size > 0) {
        for (const [key, m] of S.markers.entries()) {
            try {
                console.log("[OutZen] marker", key, m.getLatLng());
            } catch (e) {
                console.warn("[OutZen] marker", key, "getLatLng error:", e);
            }
        }
    }
}

/**
 * Force Leaflet to recalculate the size (useful after layout changes).
 */
export function refreshMapSize() {
    if (!S.map) return;
    setTimeout(() => {
        S.map.invalidateSize();
    }, 50);
}

// alert = { latitude, longitude, message, ... }
export function notifyHeavyRain(alert) {
    const L = ensureLeaflet();
    if (!L || !S.map) {
        console.warn("[OutZen] notifyHeavyRain: map not ready");
        return;
    }

    const lat = alert.latitude ?? alert.Latitude;
    const lon = alert.longitude ?? alert.Longitude;
    const msg = alert.message ?? alert.Message ?? "Heavy rain alert";

    if (lat == null || lon == null) {
        console.warn("[OutZen] notifyHeavyRain: missing coordinates", alert);
        return;
    }

    const marker = L.circleMarker([lat, lon], {
        radius: 12,
        className: "rain-alert-marker"
    }).addTo(S.map);

    marker.bindPopup(
        `<div class="rain-alert-popup">
            <strong>🌧 Heavy rain alert</strong><br/>
            ${msg}
        </div>`
    ).openPopup();

    // Gentle refocusing on the alert zone
    const currentZoom = S.map.getZoom ? S.map.getZoom() : 10;
    S.map.setView([lat, lon], Math.max(currentZoom, 11), { animate: true });
}

// ================================
// Helpers UI / popup
// ================================
function buildPopupHtml(info) {
    const title = info?.title ?? "Unknown";
    const desc = info?.description ?? "";
    return `<div class="outzen-popup">
      <div class="title">${S.utils.escapeHtml(title)}</div>
      <div class="desc">${S.utils.escapeHtml(desc)}</div>
  </div>`;
}

// =====================================================
// Bundle Markers (ESM) — overlap/group by proximity
// Exports: addOrUpdateBundleMarkers, updateBundleMarker
// Requires: window.__OutZenSingleton (S), Leaflet (window.L)
// =====================================================

// Ensure bundle state exists on the singleton (do NOT break existing S)
S.bundleMarkers = S.bundleMarkers || new Map(); // key -> Leaflet marker
S.bundleIndex = S.bundleIndex || new Map();     // key -> bundle object

// ---- Geo helpers ----
function metersToDegLat(m) {
    return m / 111320;
}
function metersToDegLng(m, lat) {
    const cos = Math.cos((lat * Math.PI) / 180);
    return m / (111320 * Math.max(cos, 0.1));
}
// Spatial hash key: grid cell sized by tolerance (in degrees), based on each point lat
function bundleKeyFor(lat, lng, tolMeters) {
    const dLat = metersToDegLat(tolMeters);
    const dLng = metersToDegLng(tolMeters, lat);
    const gy = Math.floor(lat / dLat);
    const gx = Math.floor(lng / dLng);
    return `${gy}:${gx}`;
}

// Belgium (geo-stable filter)
const BELGIUM = { minLat: 49.45, maxLat: 51.60, minLng: 2.30, maxLng: 6.60 };

function isInBelgium(ll) {
    return !!ll &&
        Number.isFinite(ll.lat) && Number.isFinite(ll.lng) &&
        ll.lat >= BELGIUM.minLat && ll.lat <= BELGIUM.maxLat &&
        ll.lng >= BELGIUM.minLng && ll.lng <= BELGIUM.maxLng;
}

// Parse lat/lng robuste (1 seule déclaration lat/lng, + normalisation 0..360 => -180..180)
function pickLatLng(obj) {
    const lat = S.utils.safeNum(
        obj?.Latitude ?? obj?.latitude ?? obj?.lat ?? obj?.Lat ?? obj?.LAT ??
        obj?.position?.lat ?? obj?.position?.Lat ?? obj?.Position?.Lat ??
        obj?.location?.lat ?? obj?.location?.Lat ?? obj?.Location?.Lat ??
        obj?.Location?.Latitude ?? obj?.location?.latitude
    );

    let lng = S.utils.safeNum(
        obj?.Longitude ?? obj?.longitude ?? obj?.lng ?? obj?.Lng ?? obj?.LNG ??
        obj?.Lon ?? obj?.lon ??
        obj?.position?.lng ?? obj?.position?.Lng ?? obj?.Position?.Lng ??
        obj?.location?.lng ?? obj?.location?.Lng ?? obj?.Location?.Lng ??
        obj?.Location?.Longitude ?? obj?.location?.longitude
    );

    if (lat == null || lng == null) return null;

    // Normalisation longitudes 0..360 => -180..180
    if (lng > 180 && lng <= 360) lng -= 360;
    while (lng > 180) lng -= 360;
    while (lng < -180) lng += 360;

    // Rejets
    if (Math.abs(lat) > 90 || Math.abs(lng) > 180) return null;
    if (Math.abs(Math.abs(lng) - 180) < 1e-6) return null;

    return { lat, lng };
}

function ensureMapReady() {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return null;
    if (!S.map) {
        console.warn("[OutZen][Bundle] map not ready (S.map is null).");
        return null;
    }
    return Leaflet;
}

function addLayerSmart(layer) {
    // Bypass cluster for bundles
    if (layer?.options?.__ozNoCluster) {
        S.map.addLayer(layer);
        return;
    }
    // Respect existing cluster if present
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

// ---- Icon: badge count (divIcon) ----
function makeBadgeIcon(totalCount, breakdown) {
    const Leaflet = ensureLeaflet();
    if (!Leaflet) return null;

    // Small dots by category (optional but useful)
    const dots = [];
    const addDot = (n, cls, title) => {
        if (!n) return;
        dots.push(`<span class="oz-dot ${cls}" title="${title}"></span>`);
    };

    addDot(breakdown.events, "oz-dot-events", "Events");
    addDot(breakdown.places, "oz-dot-places", "Places");
    addDot(breakdown.crowds, "oz-dot-crowds", "Crowd");
    addDot(breakdown.traffic, "oz-dot-traffic", "Traffic");
    addDot(breakdown.gpt, "oz-dot-gpt", "GPT");

    const html = `
    <div class="oz-bundle">
      <div class="oz-badge">${totalCount}</div>
      <div class="oz-dots">${dots.join("")}</div>
    </div>
  `.trim();

    return Leaflet.divIcon({
        className: "oz-bundle-icon",
        html,
        iconSize: [34, 34],
        iconAnchor: [17, 17],
        popupAnchor: [0, -12]
    });
}

function bundleBreakdown(b) {
    return {
        events: b.events?.length ?? 0,
        places: b.places?.length ?? 0,
        crowds: b.crowds?.length ?? 0,
        traffic: b.traffic?.length ?? 0,
        gpt: b.gpt?.length ?? 0
    };
}

function bundleTotal(b) {
    const d = bundleBreakdown(b);
    return d.events + d.places + d.crowds + d.traffic + d.gpt;
}

function bundlePopupHtml(b) {
    const esc = (s) => String(s ?? "")
        .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;").replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const section = (title, items, renderer) => {
        if (!items || items.length === 0) return "";
        const rows = items.slice(0, 8).map(renderer).join("");
        const more = items.length > 8 ? `<div class="oz-more">+${items.length - 8} more…</div>` : "";
        return `
      <div class="oz-sec">
        <div class="oz-sec-title">${esc(title)} <span class="oz-sec-count">${items.length}</span></div>
        <div class="oz-sec-body">${rows}${more}</div>
      </div>
    `;
    };

    const ev = section("Events", b.events, e => `
    <div class="oz-row">
      <span class="oz-k">#${esc(e.Id)}</span>
      <span class="oz-v">${esc(e.Name)}</span>
    </div>
  `);

    const pl = section("Places", b.places, p => `
    <div class="oz-row">
      <span class="oz-k">#${esc(p.Id)}</span>
      <span class="oz-v">${esc(p.Name ?? p.LocationName)}</span>
    </div>
  `);

    const cr = section("Crowd", b.crowds, c => `
    <div class="oz-row">
      <span class="oz-k">#${esc(c.Id)}</span>
      <span class="oz-v">${esc(c.LocationName)} (L${esc(c.CrowdLevel)})</span>
    </div>
  `);

    const tr = section("Traffic", b.traffic, t => `
    <div class="oz-row">
      <span class="oz-k">#${esc(t.Id)}</span>
      <span class="oz-v">${esc(t.RoadName ?? t.Name ?? "Traffic")}</span>
    </div>
  `);

    const gp = section("GPT", b.gpt, g => `
    <div class="oz-row">
      <span class="oz-k">#${esc(g.Id)}</span>
      <span class="oz-v">${esc(g.Title ?? g.SourceType ?? "GPT")}</span>
    </div>
  `);

    return `
    <div class="oz-popup">
      <div class="oz-popup-title">Bundle (${bundleTotal(b)})</div>
      ${ev}${pl}${cr}${tr}${gp}
    </div>
  `.trim();
}

// ---- Bundle compute ----
function computeBundles(payload, tolMeters) {
    const buckets = new Map();

    const push = (arr, kind) => {
        if (!Array.isArray(arr)) return;

        for (const item of arr) {
            const ll = pickLatLng(item);
            if (!ll) continue;
            if (!isInBelgium(ll)) continue;

            const key = bundleKeyFor(ll.lat, ll.lng, tolMeters);

            let b = buckets.get(key);
            if (!b) {
                b = { key, lat: ll.lat, lng: ll.lng, events: [], places: [], crowds: [], traffic: [], gpt: [] };
                buckets.set(key, b);
            }
            b[kind].push(item);
        }
    };

    push(payload?.events, "events");
    push(payload?.places, "places");
    push(payload?.crowds, "crowds");
    push(payload?.traffic, "traffic");
    push(payload?.gpt, "gpt");

    return buckets; // ✅ Map
}

// ---- Exported: update one bundle marker ----
export function updateBundleMarker(b) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return;

    const total = bundleTotal(b);
    const breakdown = bundleBreakdown(b);

    // If total==0, remove marker if exists
    const existing = S.bundleMarkers.get(b.key);
    if (total <= 0) {
        if (existing) {
            removeLayerSmart(existing);
            S.bundleMarkers.delete(b.key);
            S.bundleIndex.delete(b.key);
        }
        return;
    }

    const icon = makeBadgeIcon(total, breakdown);
    const popup = bundlePopupHtml(b);

    if (!existing) {
        const m = Leaflet.marker([b.lat, b.lng], {
            icon,
            pane: "markerPane",  
            title: `Bundle (${total})`,
            riseOnHover: true,
            __ozNoCluster: true
        });

        m.bindPopup(popup, { maxWidth: 360, closeButton: true, autoPan: true });

        m.bindTooltip(
            `Bundle (${total}) • E:${breakdown.events} P:${breakdown.places} C:${breakdown.crowds} T:${breakdown.traffic} G:${breakdown.gpt}`,
            { direction: "top", sticky: true, opacity: 0.95 }
        );

        addLayerSmart(m);
        S.bundleMarkers.set(b.key, m);
        S.bundleIndex.set(b.key, b);
        return;
    }

    // Update existing marker
    existing.setLatLng([b.lat, b.lng]);
    if (icon) existing.setIcon(icon);
    if (existing.getPopup()) existing.setPopupContent(popup);
    else existing.bindPopup(popup, { maxWidth: 360 });

    S.bundleIndex.set(b.key, b);
}

// ---- Exported: main entry called from Blazor ----
export function addOrUpdateBundleMarkers(payload, toleranceMeters = 80) {
    S.bundleLastInput = payload;

    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

    const tol = Number(toleranceMeters);
    const tolMeters = Number.isFinite(tol) && tol > 0 ? tol : 80;

    const newBundles = computeBundles(payload, tolMeters); // ✅ Map

    // Remove bundles that no longer exist
    for (const oldKey of Array.from(S.bundleMarkers.keys())) {
        if (!newBundles.has(oldKey)) {
            const marker = S.bundleMarkers.get(oldKey);
            removeLayerSmart(marker);
            S.bundleMarkers.delete(oldKey);
            S.bundleIndex.delete(oldKey);
        }
    }

    // Add / Update bundles
    for (const b of newBundles.values()) {
        updateBundleMarker(b);
    }

    if (S.cluster && typeof S.cluster.refreshClusters === "function") {
        try { S.cluster.refreshClusters(); } catch { }
    }

    console.info(`[OutZen][Bundle] updated: ${newBundles.size} bundles (tol=${tolMeters}m)`);

    try { fitToBundles(24); } catch { }
    return true;
}

//window.OutZenInterop.enableHybridZoom = enableHybridZoom;
//window.OutZenInterop.addOrUpdateDetailMarkers = addOrUpdateDetailMarkers;

const EPS_180 = 1e-6;
export function fitToBundles(padding = 24) {
    const L = ensureMapReady();
    if (!L) return false;

    const pts = Array.from(S.bundleMarkers.values())
        .map(m => m?.getLatLng?.())
        .filter(ll =>
            ll &&
            Number.isFinite(ll.lat) && Number.isFinite(ll.lng) &&
            Math.abs(ll.lat) <= 90 &&
            Math.abs(ll.lng) <= 180 &&
            Math.abs(Math.abs(ll.lng) - 180) > EPS_180 &&
            isInBelgium(ll)               // 👈 key
        );

    if (pts.length === 0) {
        // fallback: centre BE “safe”
        S.map.setView([50.45, 4.6], 12);
        return false;
    }

    const b = L.latLngBounds(pts);
    S.map.fitBounds(b, { padding: [padding, padding], maxZoom: 16 });
    return true;
}
function makeDetailKey(kind, item) {
    return `${kind}:${item?.Id ?? item?.id ?? crypto.randomUUID()}`;
}

function addDetailMarker(kind, item) {
    const Leaflet = ensureLeaflet();
    const layer = ensureDetailLayer();
    if (!Leaflet || !layer) return;

    const ll = pickLatLng(item);
    if (!ll) return;

    const key = makeDetailKey(kind, item);
    if (S.detailMarkers.has(key)) return;

    const title =
        kind === "events" ? (item?.Name ?? "Event") :
            kind === "places" ? (item?.Name ?? item?.LocationName ?? "Place") :
                kind === "crowds" ? (item?.LocationName ?? "Crowd") :
                    kind === "traffic" ? (item?.RoadName ?? item?.Name ?? "Traffic") :
                        (item?.Title ?? item?.SourceType ?? "GPT");

    const m = Leaflet.circleMarker([ll.lat, ll.lng], { radius: 7 });
    m.bindTooltip(`${kind}: ${title}`, { sticky: true, opacity: 0.95 });
    m.bindPopup(`<div class="oz-popup"><b>${S.utils.escapeHtml(title)}</b><div class="oz-small">#${S.utils.escapeHtml(item?.Id)}</div></div>`);
    layer.addLayer(m);

    S.detailMarkers.set(key, m);
}

export function addOrUpdateDetailMarkers(payload) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

    clearDetailMarkers();

    const push = (arr, kind) => Array.isArray(arr) && arr.forEach(x => addDetailMarker(kind, x));
    push(payload?.events, "events");
    push(payload?.places, "places");
    push(payload?.crowds, "crowds");
    push(payload?.traffic, "traffic");
    push(payload?.gpt, "gpt");

    return true;
}

function ensureDetailLayer() {
    const L = ensureLeaflet();
    if (!L || !S.map) return null;

    if (!S.detailLayer) {
        S.detailLayer = L.layerGroup();
        S.map.addLayer(S.detailLayer);
    }
    return S.detailLayer;
}

function clearDetailMarkers() {
    if (S.detailLayer) S.detailLayer.clearLayers();
    S.detailMarkers.clear();
}

function addDetail(kind, item) {
    const L = ensureLeaflet();
    const layer = ensureDetailLayer();
    if (!L || !layer) return;

    const lat = item.lat ?? item.latitude ?? item.Latitude;
    const lng = item.lng ?? item.longitude ?? item.Longitude;

    const latNum = Number(String(lat).replace(",", "."));
    const lngNum = Number(String(lng).replace(",", "."));
    if (!Number.isFinite(latNum) || !Number.isFinite(lngNum)) return;

    const id = `${kind}:${item.id ?? item.Id ?? crypto.randomUUID()}`;
    if (S.detailMarkers.has(id)) return;

    const title =
        item.title ??
        item.Name ??
        item.LocationName ??
        item.Prompt ??
        kind;

    const m = L.circleMarker([latNum, lngNum], {
        radius: 7,
        className: "oz-detail-marker"
    });

    m.bindTooltip(`${kind} • ${S.utils.escapeHtml(title)}`, { sticky: true });
    m.bindPopup(buildPopupHtml({ title, description: item.description ?? item.Response }));

    layer.addLayer(m);
    S.detailMarkers.set(id, m);
}

export function renderDetailMarkers(payload) {
    clearDetailMarkers();

    payload?.events?.forEach(x => addDetail("Event", x));
    payload?.places?.forEach(x => addDetail("Place", x));
    payload?.crowds?.forEach(x => addDetail("Crowd", x));
    payload?.traffic?.forEach(x => addDetail("Traffic", x));
    payload?.gpt?.forEach(x => addDetail("GPT", x));
}

export function enableAutoDetailMode(thresholdZoom = 15) {
    const Leaflet = ensureMapReady();
    if (!Leaflet) return false;

    const apply = () => {
        const z = S.map.getZoom();
        const showDetail = z >= thresholdZoom;

        // Bundles are only visible when you are far away.
        for (const m of S.bundleMarkers.values()) {
            if (!m) continue;
            if (showDetail) S.map.removeLayer(m);
            else S.map.addLayer(m);
        }

        // details visible only when you are close
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

function enableHybridZoom({ threshold = 13 } = {}) {
    const L = ensureLeaflet();
    if (!L || !S.map) {
        console.warn("[OutZen][Hybrid] map not ready");
        return;
    }

    const apply = () => {
        const z = S.map.getZoom();

        if (z >= threshold) {
            // 👉 détails
            for (const m of S.bundleMarkers.values()) {
                if (S.map.hasLayer(m)) S.map.removeLayer(m);
            }
            if (S.cluster) S.map.addLayer(S.cluster);
        } else {
            // 👉 bundles
            if (S.cluster) S.map.removeLayer(S.cluster);
            for (const m of S.bundleMarkers.values()) {
                if (!S.map.hasLayer(m)) S.map.addLayer(m);
            }
        }
    };

    S.map.off("zoomend", apply);
    S.map.on("zoomend", apply);

    apply(); // initial
}

if (!window.OutZenInterop) {
    window.OutZenInterop = {};
}
window.OutZenInterop.isOutZenReady = isOutZenReady;
/*window.__outzenBootFlag = true;*/
export function disposeOutZen() {
    if (S.map) {
        try { S.map.remove(); } catch { }
        S.map = null;
    }
    if (S.cluster) {
        try { S.cluster.clearLayers(); } catch { }
        S.cluster = null;
    }
    destroyChartIfAny();
    if (S.markers) {
        S.markers.clear();
    }
    if (S.bundleMarkers) S.bundleMarkers.clear();
    S.initialized = false;
}

S.detailLayer ??= null;
S.detailMarkers ??= new Map();

// ================================
// Public API bridge (single source of truth)
// ================================
window.OutZenInterop ||= {};
window.OutZenInterop.bootOutZen = bootOutZen;
window.OutZenInterop.addOrUpdateBundleMarkers = addOrUpdateBundleMarkers;
window.OutZenInterop.enableHybridZoom = enableHybridZoom;
window.OutZenInterop.isOutZenReady = isOutZenReady;
window.OutZenInterop.disposeOutZen = disposeOutZen;

/*window.OutZenInterop.disposeOutZen = disposeOutZen;*/

//window.OutZen = window.OutZen || {};
//window.OutZen.addOrUpdateBundleMarkers = addOrUpdateBundleMarkers;
//window.OutZen.updateBundleMarker = function (bundle) {
//    const Leaflet = ensureLeaflet();
//    if (!Leaflet) return;
//    updateBundleMarker(Leaflet, bundle);
//};














































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/