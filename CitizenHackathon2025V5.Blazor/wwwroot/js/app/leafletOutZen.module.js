// wwwroot/js/app/leafletOutZen.module.js
/* global L, Chart */
"use strict";

// Global singleton to avoid duplicates in hot reload
if (!window.__OutZenSingleton) {
    window.__OutZenSingleton = {
        version: "2025.11.16",
        bootTs: 0,
        map: null,
        cluster: null,
        markers: new Map(),
        chart: null,
        initialized: false,
        loggedReeval: false,
        weatherLegend: null
    };
} else if (!window.__OutZenSingleton.loggedReeval) {
    console.info("[OutZen] module re-evaluated — reusing singleton");
    window.__OutZenSingleton.loggedReeval = true;
}

const S = window.__OutZenSingleton;

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

    let datasetLabel = "Température (°C)";
    if (metric === "humidity") {
        datasetLabel = "Humidité (%)";
    } else if (metric === "wind") {
        datasetLabel = "Vent (km/h)";
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
        console.info(`[OutZen] mapId='${mapId}' not found in DOM — bootOutZen skipped.`);
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
            S.cluster = L.markerClusterGroup();
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

function buildMarkerIcon(L, level, isTraffic = false, weatherType = null) {
    const lvlClass = getMarkerClassForLevel(level);
    const trafficClass = isTraffic ? "oz-marker-traffic" : "";
    const emoji = getWeatherEmoji(weatherType);

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

    if (!S.markers) S.markers = new Map();

    const key = String(id);
    const existing = S.markers.get(key);

    const popupHtml = buildPopupHtml(info);

    const isTraffic = info?.isTraffic;
    const weatherType = info?.weatherType ?? info?.WeatherType ?? null;

    const icon = buildMarkerIcon(L, level, isTraffic, weatherType);

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

    const marker = L.marker([lat, lng], opts).bindPopup(popupHtml);
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
    if (!L || !S.map || !dto) {
        console.warn("[OutZen] addOrUpdateGptMarker: missing map or dto", { dto });
        return;
    }

    // Tolerates camelCase / PascalCase from C#
    const id = dto.id ?? dto.Id;
    const lat = Number(dto.lat ?? dto.Latitude);
    const lng = Number(dto.lng ?? dto.Longitude);

    if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
        console.warn("[OutZen] addOrUpdateGptMarker: invalid coordinates", { dto, lat, lng });
        return;
    }

    const sourceType = dto.sourceType ?? dto.SourceType ?? "GPT";
    const title = dto.title ?? dto.Prompt ?? `[${sourceType}] #${id}`;
    const description = dto.description ?? dto.Response ?? "";
    const crowdLevel = dto.crowdLevel ?? dto.CrowdLevel ?? 3; // neutral

    // We reuse the same mechanism as for the crowd
    addOrUpdateCrowdMarker(
        id,
        lat,
        lng,
        crowdLevel,
        {
            title,
            description
        }
        
    );

    // Option: Auto cropping (gentle)
    setTimeout(() => {
        try {
            fitToMarkers();
        } catch (e) {
            console.warn("[OutZen] fitToMarkers failed from GPT marker:", e);
            console.log("[OutZen] GPT marker", { id, lat, lng, sourceType });
        }
    }, 50);
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


// Don't forget to export:
//export {
//    bootOutZen,
//    addOrUpdateCrowdMarker,
//    removeCrowdMarker,
//    addOrUpdateGptMarker,
//    removeGptMarker,
//    fitToMarkers,
//    refreshMapSize,
//    disposeOutZen
//};

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
        <div class="title">${escapeHtml(title)}</div>
        <div class="desc">${escapeHtml(desc)}</div>
    </div>`;
}

function escapeHtml(str) {
    if (!str) return "";
    return String(str)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
}

if (!window.OutZenInterop) {
    window.OutZenInterop = {};
}
window.OutZenInterop.isOutZenReady = isOutZenReady;
window.OutZenInterop.disposeOutZen = disposeOutZen;
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
    S.initialized = false;
}












































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/