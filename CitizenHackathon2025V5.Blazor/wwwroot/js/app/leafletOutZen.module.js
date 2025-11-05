// ============================
// leafletOutZen.module.js (ESM, robust boot + readiness)
// ============================
/* global L, Chart */
"use strict";

// --- module-eval guard + singleton bookkeeping (HMR-safe) ---
if (!window.__OutZenSingleton) {
    window.__OutZenSingleton = {
        createdAt: Date.now(),
        chartCreated: false,
        mapCreated: false,
        bootTs: 0,
        loggedReeval: false
    };
} else if (!window.__OutZenSingleton.loggedReeval) {
    console.info("[OutZen] module re-evaluated — reusing singleton");
    window.__OutZenSingleton.loggedReeval = true;
}
window.__outzenModuleEvaled = true;
window.__outzenBootFlag = window.__outzenBootFlag ?? false;
window.__outzenBootTs = window.__outzenBootTs ?? 0;
window.__outzenChartInstance = window.__outzenChartInstance ?? null;

const OutZen = {
    version: "2025.11.01",
    initialized: false,
    map: null,
    cluster: null,
    markers: new Map()
};

/* ------------------------------------------------------------------ /
/  UTILITIES                                                          /
/ ------------------------------------------------------------------ */
function _escapeHtml(s) {
    return String(s ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

async function waitForElement(id, tries = 60, intervalMs = 150) { // ~9s
    for (let i = 0; i < tries; i++) {
        const el = document.getElementById(id);
        if (el) { await new Promise(r => setTimeout(r, 0)); return true; }
        await new Promise(r => setTimeout(r, intervalMs));
    }
    // last immediate check
    return !!document.getElementById(id);
}

function safeDestroyChart() {
    try {
        const canvas = document.getElementById("crowdChart");
        if (canvas && typeof Chart !== "undefined" && typeof Chart.getChart === "function") {
            const existing = Chart.getChart(canvas);
            if (existing) {
                existing.destroy();
                console.info("[OutZen] Destroyed existing Chart via Chart.getChart.");
            }
        }
        if (typeof Chart !== "undefined" && Chart.instances) {
            for (const k of Object.keys(Chart.instances)) {
                try { Chart.instances[k]?.destroy?.(); delete Chart.instances[k]; } catch { }
            }
            console.info("[OutZen] Cleared Chart.instances fallback.");
        }
    } catch (err) {
        console.warn("[OutZen] safeDestroyChart error:", err);
    } finally {
        try { window.__outzenChartInstance = null; } catch { }
        try { window.__OutZenSingleton.chartCreated = false; } catch { }
    }
}

/* ------------------------------------------------------------------ /
/  MAP INITIALISATION                                                 /
/ ------------------------------------------------------------------ */
export function initMap(containerId = "leafletMap", center = [50.89, 4.34], zoom = 13) {
    const el = document.getElementById(containerId);
    if (!el) {
        console.warn(`[OutZen] Element #${containerId} not found – skipping init.`);
        return null;
    }

    // NEW: safety – ensure visible height
    const h = el.clientHeight || parseInt(getComputedStyle(el).height, 10) || 0;
    if (h < 150) {
        el.style.minHeight = "480px";
        el.style.height = "480px";
        el.style.width = el.style.width || "100%";
        console.info("[OutZen] Applied fallback height to map container (480px).");
    }

    if (OutZen.map && window.leafletMap === OutZen.map) {
        // same instance already installed in the same container -> do nothing
        console.info("[OutZen] initMap skipped: same container & map already set.");
        return OutZen.map;
    }

    // Cleanup previous
    try {
        if (window.leafletMap && typeof window.leafletMap.remove === "function") {
            window.leafletMap.remove();
            console.info("[OutZen] Removed previous Leaflet map instance.");
        }
        delete window.leafletMap;
    } catch (e) { console.warn("[OutZen] Leaflet cleanup failed:", e); }

    if (typeof L === "undefined") throw new Error("Leaflet (L) is not loaded.");

    const mapInstance = L.map(containerId).setView(center, zoom);
    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "© OpenStreetMap contributors", maxZoom: 19
    }).addTo(mapInstance);

    OutZen.map = mapInstance;
    window.leafletMap = mapInstance;
    window.__OutZenSingleton.mapCreated = true;

    // NEW: reflow when the container changes size/visibility
    try {
        const ro = new ResizeObserver(() => {
            try { mapInstance.invalidateSize(true); } catch { }
        });
        ro.observe(el);
    } catch { }

    console.info("[OutZen] Leaflet map initialized.");
    return mapInstance;
}

/* ------------------------------------------------------------------ /
/  ICONS & COLORS                                                     /
/ ------------------------------------------------------------------ */
function getColorFromLevel(level) {
    const n = Number(level) || 0;
    if (n === 1) return "#4CAF50";   // green
    if (n === 2) return "#FF9800";   // orange (a bit more readable than #FFC107)
    if (n === 3) return "#F44336";   // red
    if (n === 4) return "#B71C1C";   // dark red
    return "#9E9E9E";                // unknown / 0
}

function getCrowdIcon(level) {
    const n = Number(level) || 0;
    const color = getColorFromLevel(n);
    const size = (n >= 4) ? 44 : (n === 3 ? 40 : (n === 2 ? 36 : (n === 1 ? 32 : 28)));
    const alarm = (n === 4);

    return L.divIcon({
        className: "outzen-crowd-marker" + (alarm ? " outzen-crowd-marker--alarm" : ""),
        html: `<div class="oz-core" style="
      background:${color};
      width:${size}px;height:${size}px;
      border-radius:50%;
      border:3px solid white;
      box-shadow:0 2px 6px rgba(0,0,0,.4);
      display:flex;align-items:center;justify-content:center;
      color:#fff;font-weight:700;
      font-family:Poppins,system-ui,Segoe UI,Arial,sans-serif;
      font-size:${size > 36 ? '14px' : '12px'};
      line-height:1;">${n}</div>`,
        iconSize: [size, size],
        iconAnchor: [Math.round(size / 2), Math.round(size / 2)],
        popupAnchor: [0, -Math.round(size / 2)]
    });
}

/* ------------------------------------------------------------------ /
/  MARKERS                                                            /
/ ------------------------------------------------------------------ */
export function addOrUpdateCrowdMarker(id, lat, lng, level = 0, info = { title: "", description: "" }) {
    if (!OutZen.map) { console.warn("[OutZen] map not initialized."); return; }
    const before = OutZen.markers.size;
    const nlat = Number(lat), nlng = Number(lng);
    if (!Number.isFinite(nlat) || !Number.isFinite(nlng)) { 
        console.warn("[OutZen] invalid lat/lng:", lat, lng); 
        return; 
    }

    const key = String(id);
    const existing = OutZen.markers.get(key);
    if (existing) {
        try { OutZen.map.removeLayer(existing.marker); } catch { }
        if (existing.cluster) {
            try { existing.cluster.removeLayer(existing.marker); } catch { }
        }
    }

    const icon = getCrowdIcon(level);
    const marker = L.marker([nlat, nlng], { icon, level })
        .bindPopup(`
            <div style="font-family:Poppins;padding:8px;">
                <strong>${_escapeHtml(info?.title)}</strong><br>
                ${_escapeHtml(info?.description)}<br><br>
                <span style="color:${getColorFromLevel(level)};font-weight:bold;">Niveau ${level}</span>
            </div>
        `);
    if (OutZen.cluster) {
        OutZen.cluster.addLayer(marker);
        OutZen.markers.set(key, { marker, level, cluster: OutZen.cluster });
    } else {
        marker.addTo(OutZen.map);
        OutZen.markers.set(key, { marker, level });
    }
    // auto-fit: on the first marker added, or if the existing one has just been replaced
    const after = OutZen.markers.size;
    if ((before === 0 && after > 0) || !existing) {
        try { fitToMarkers(); } catch { }
    }
    if (Number(level) === 4) {
        marker.on('add', () => { try { marker.openPopup(); } catch { } });
    }
}

export function removeCrowdMarker(id) {
    const e = OutZen.markers.get(String(id));
    if (!e) return;
    try { OutZen.map.removeLayer(e.marker); } catch { }
    if (e.cluster) try { e.cluster.removeLayer(e.marker); } catch { }
    OutZen.markers.delete(String(id));
}

export function clearCrowdMarkers() {
    if (!OutZen.map) return;
    for (const { marker, cluster } of OutZen.markers.values()) {
        try { OutZen.map.removeLayer(marker); } catch { }
        if (cluster) try { cluster.removeLayer(marker); } catch { }
    }
    OutZen.markers.clear();
}

/* ------------------------------------------------------------------ /
/  VIEW HELPERS                                                       /
/ ------------------------------------------------------------------ */
export function fitToMarkers(padRatio = 0.12, opts = { minZoom: 3, maxZoom: 18, animate: true }) {
    if (!OutZen.map) return;

    const doFit = () => {
        let bounds = null;

        // 1) If a cluster is present, use its bounds (more reliable when there are many items).
        if (OutZen.cluster && typeof OutZen.cluster.getBounds === 'function') {
            const b = OutZen.cluster.getBounds();
            if (b && b.isValid && b.isValid()) bounds = b;
        }

        // 2) fallback: bounds from the tracked markers
        if (!bounds) {
            const latlngs = [];
            for (const { marker } of OutZen.markers.values()) {
                try { latlngs.push(marker.getLatLng()); } catch { }
            }
            if (latlngs.length === 0) return;

            if (latlngs.length === 1) {
                const target = latlngs[0];
                const z = Math.min(opts.maxZoom ?? 18, Math.max(opts.minZoom ?? 3, 15));
                OutZen.map.setView(target, z, { animate: opts.animate !== false });
                return;
            }
            bounds = L.latLngBounds(latlngs).pad(0.02);
        }

        const padX = Math.max(16, Math.round(padRatio * window.innerWidth));
        const padY = Math.max(16, Math.round(padRatio * window.innerHeight));
        OutZen.map.flyToBounds(bounds, {
            paddingTopLeft: [padX, padY],
            paddingBottomRight: [padX, padY],
            maxZoom: opts.maxZoom ?? 17,
            animate: opts.animate !== false
        });
    };

    // Allow the cluster time to absorb the new markers.
    requestAnimationFrame(() => setTimeout(doFit, 0));
}

export async function refreshMapSize() {
    if (OutZen.map && typeof OutZen.map.invalidateSize === "function") {
        OutZen.map.invalidateSize(true);
        setTimeout(() => { try { OutZen.map.invalidateSize(true); } catch { } }, 200);
    }
}

/* ------------------------------------------------------------------ /
/  LEGEND                                                             /
/ ------------------------------------------------------------------ */
export function addCrowdLegend() {
    if (!OutZen.map) return;
    const legend = L.control({ position: "bottomright" });
    legend.onAdd = function () {
        const div = L.DomUtil.create("div", "info legend outzen-legend");
        const labels = ["Level 1 (Low)", "Level 2 (Medium)", "Level 3 (High)", "Level 4 (Critical)"];
        const colors = [getColorFromLevel(1), getColorFromLevel(2), getColorFromLevel(3), getColorFromLevel(4)];
        let html = `<div class="outzen-legend__box">
      <strong class="outzen-legend__title">Density</strong>`;
        for (let i = 0; i < labels.length; i++) {
            html += `<div class="outzen-legend__row">
        <i class="outzen-legend__dot" style="background:${colors[i]}"></i>
        <span class="outzen-legend__label">${labels[i]}</span>
      </div>`;
        }
        html += `</div>`;
        div.innerHTML = html;
        return div;
    };
    legend.addTo(OutZen.map);
}
/* ------------------------------------------------------------------ /
/  CHART                                                             /
/ ------------------------------------------------------------------ */
export async function initCrowdChart(config = null) {
    if (typeof Chart === "undefined") { console.warn("[OutZen] Chart.js not present."); return null; }
    const canvas = document.getElementById("crowdChart");
    if (!canvas) { console.warn("[OutZen] canvas #crowdChart not found - chart creation skipped."); return null; }
    if (window.__OutZenSingleton.chartCreated) {
        console.info("[OutZen] Chart already created - skipping.");
        return window.__outzenChartInstance;
    }
    safeDestroyChart();
    try {
        const ctx = canvas.getContext("2d");
        const cfg = config ?? {
            type: "line",
            data: { labels: [], datasets: [{ label: "Crowd", data: [], tension: 0.25 }] },
            options: { responsive: true, maintainAspectRatio: false }
        };
        const ch = new Chart(ctx, cfg);
        window.__outzenChartInstance = ch;
        window.__OutZenSingleton.chartCreated = true;
        console.info("[OutZen] Chart initialized.");
        return ch;
    } catch (err) {
        console.error("[OutZen] Chart init failed:", err);
        return null;
    }
}

export function tryInitCrowdChartLater(retries = 6, intervalMs = 500, config = null) {
    (async function attempt(n) {
        if (n <= 0) return null;
        const canvas = document.getElementById("crowdChart");
        if (canvas) return await initCrowdChart(config);
        await new Promise(r => setTimeout(r, intervalMs));
        return attempt(n - 1);
    })(retries);
}

export function destroyCrowdChart() { safeDestroyChart(); }

export function updateCrowdChart(value, canvasId = "crowdChart") {
    const chart = window.__outzenChartInstance;
    if (!chart) return;
    const now = new Date().toLocaleTimeString();
    const numeric = Number(String(value).replace("%", "").trim());
    if (!Number.isFinite(numeric)) return;
    chart.data.labels.push(now);
    chart.data.datasets[0].data.push(numeric);
    if (chart.data.labels.length > 20) { chart.data.labels.shift(); chart.data.datasets[0].data.shift(); }
    chart.update();
}
/* ------------------------------------------------------------------ /
/  BOOT                                                              /
/ ------------------------------------------------------------------ */
export async function bootOutZen(opts = {
    mapId: "leafletMap",
    center: [50.89, 4.34],
    zoom: 13,
    enableChart: false,
    force: false
}) {
    // mutex "boot in progress" (avoids races)
    if (!window.__OutZenSingleton.bootingPromise) {
        window.__OutZenSingleton.bootingPromise = Promise.resolve();
    }

    if (OutZen.map) {
        console.info("[OutZen] map already exists — skip boot (idempotent).");
        return OutZen;
    }

    // serialize concurrent boots
    const chain = window.__OutZenSingleton.bootingPromise.then(async () => {
        if (OutZen.map) return OutZen; // recheck
        // ... the existing boot code here ...
    });
    window.__OutZenSingleton.bootingPromise = chain.catch(() => { }).then(() => null);
    return await chain;
    // cooldown/hot-reload guard
    if (!opts.force && window.__outzenBootFlag && ((Date.now() - (window.__outzenBootTs || 0)) < 800)) {
        console.warn("[OutZen] bootOutZen skipped (cooldown).");
        return OutZen;
    }
    if (!opts.force && window.__outzenBootFlag && OutZen.map) {
        console.info("[OutZen] already booted – returning API.");
        return OutZen;
    }

    window.__outzenBootTs = Date.now();

    // Wait for container to exist (Blazor renders after first paint)
    const ok = await waitForElement(opts.mapId ?? "leafletMap", 24, 150);
    if (!ok) {
        console.warn(`[OutZen] #${opts.mapId} not present after wait — retry later.`);
        // NE PAS forcer. Re-tenter en douceur, seulement si rien n’a booté entre-temps.
        setTimeout(() => {
            try {
                if (!window.__outzenBootFlag && !OutZen.map) {
                    bootOutZen({ ...opts, force: false });
                }
            } catch { }
        }, 250);
        return OutZen;
    }

    // Init map
    let mapInstance = null;
    try {
        mapInstance = initMap(opts.mapId, opts.center, opts.zoom);
    } catch (err) {
        console.error("[OutZen] initMap error:", err);
    }
    if (!mapInstance) {
        console.warn("[OutZen] initMap returned null — abort boot.");
        return OutZen; // do not set flag
    }

    // Chart + legend
    try {
        if (opts.enableChart) {
            const created = await initCrowdChart(opts.chartConfig ?? null);
            if (!created) tryInitCrowdChartLater(6, 500, opts.chartConfig ?? null);
        }
        addCrowdLegend();
    } catch (e) { console.warn("[OutZen] chart/legend init non-fatal:", e); }

    // marker clustering
    try {
        if (typeof L !== "undefined" && typeof L.markerClusterGroup === "function") {
            OutZen.cluster = L.markerClusterGroup({
                iconCreateFunction: function (cluster) {
                    const childCount = cluster.getChildCount();
                    const size = childCount < 10 ? 40 : childCount < 50 ? 50 : 60;
                    let maxLevel = 0;
                    cluster.getAllChildMarkers().forEach(m => maxLevel = Math.max(maxLevel, Number(m.options.level) || 0));
                    const color = getColorFromLevel(maxLevel);
                    const alarmCls = (maxLevel === 4) ? " outzen-cluster--alarm" : "";

                    return L.divIcon({
                        className: "outzen-cluster" + alarmCls,
                        html: `<div class="oz-cluster-core" style="
                            background:${color};
                            width:${size}px;height:${size}px;border-radius:50%;
                            display:flex;align-items:center;justify-content:center;
                            color:#fff;font-weight:700;">${childCount}</div>`,
                        iconSize: [size, size]
                    });
                }
            });
            if (OutZen.map) {
                OutZen.map.addLayer(OutZen.cluster);
                console.info("[OutZen] Cluster added to map.");
            }
        } else {
            console.info("[OutZen] MarkerCluster not present; continuing without clusters.");
        }
    } catch (e) { console.warn("[OutZen] cluster init non-fatal:", e); }

    // expose OutZenInterop (non-module interop for Blazor)
    window.OutZenInterop = Object.assign(window.OutZenInterop || {}, {
        addOrUpdateCrowdMarker,
        removeCrowdMarker,
        clearCrowdMarkers,
        fitToMarkers,
        refreshMapSize,
        initCrowdChart,
        tryInitCrowdChartLater,
        destroyCrowdChart,
        updateCrowdChart
    });

    // size fix after map is attached
    try { mapInstance.invalidateSize(true); setTimeout(() => mapInstance.invalidateSize(true), 200); } catch { }

    window.__outzenBootFlag = true; // flag only when map is OK
    OutZen.initialized = true;
    console.info("[OutZen] booted (flag set).");
    return OutZen;
}

/* ------------------------------------------------------------------ /
/  READINESS & DEBUG EXPORTS                                         /
/ ------------------------------------------------------------------ */
export function isOutZenBooted() { return !!window.__outzenBootFlag; }
export function isOutZenReady() { return !!OutZen.map; }
export function outzenState() {
    return {
        bootFlag: !!window.__outzenBootFlag,
        hasMap: !!OutZen.map,
        hasCluster: !!OutZen.cluster,
        markersCount: OutZen.markers?.size ?? 0
    };
}
export function debugOutZen() {
    const canvas = document.getElementById("crowdChart");
    const chart = (typeof Chart !== "undefined" && typeof Chart.getChart === "function") ? Chart.getChart(canvas) : window.__outzenChartInstance;
    const info = {
        bootFlag: !!window.__outzenBootFlag,
        bootTs: window.__outzenBootTs,
        hasChart: !!chart,
        chartIds: Object.keys(Chart?.instances ?? {}),
        OutZenInteropKeys: Object.keys(window.OutZenInterop ?? {})
    };
    console.table(info);
    return info;
}

const _default = {
    bootOutZen, debugOutZen, initCrowdChart, destroyCrowdChart, initMap,
    isOutZenBooted, isOutZenReady, refreshMapSize, fitToMarkers, outzenState
};
export default _default;

// Global helpers for C# InvokeAsync<bool>("isOutZenReady")
window.isOutZenBooted = () => !!window.__outzenBootFlag;
window.isOutZenReady = () => !!OutZen.map;

// Minimal CSS suggestion (keep in your CSS bundle):
// .outzen-crowd-marker { background: transparent !important; border: none !important; padding: 0 !important; }
// .outzen-crowd-marker > div { line-height: 1 !important; }

// End of module










































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/