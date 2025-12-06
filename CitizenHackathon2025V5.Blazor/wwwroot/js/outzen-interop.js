/* wwwroot/js/outzen-interop.js (hardened) */
/* global L, Chart */
(function () {
    "use strict";

    window.OutZenInterop = window.OutZenInterop || {};

    if (window.OutZenInterop?.initMap) {
        // ESM already present: only exposes audio (mute/unmute/testBeep)
        // ... keep only the AudioCtx/beep section... then
        Object.assign(window.OutZenInterop, { beepCritical, setBeepConfig, getBeepConfig, mute, unmute, testBeep });
        console.log("[OutZen] interop: audio-only (ESM owns map).");
        return;
    }

    window.OutZenInterop = window.OutZenInterop || {};
    // ---------------- internal state ----------------
    const OutZen = {
        map: null,
        cluster: null,
        markers: new Map(),
        chart: null,
        booting: false
    };

    // expose a readable boot flag for .NET side polling
    window.__outzenBootFlag = window.__outzenBootFlag ?? false;

    // ---------------- utils ----------------
    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

    function escapeHtml(s) {
        return String(s ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function colorFromLevel(level) {
        const n = Number(level) || 0;
        if (n <= 3) return "#4CAF50"; // green
        if (n <= 6) return "#FFC107"; // amber
        if (n <= 8) return "#FF5722"; // deep orange
        return "#D32F2F";             // red
    }

    function iconFromLevel(level) {
        const n = Number(level) || 0;
        let color = "#9E9E9E", size = 25;
        if (n <= 3) { color = "#4CAF50"; size = 30; }
        else if (n <= 6) { color = "#FFC107"; size = 35; }
        else if (n <= 8) { color = "#FF5722"; size = 40; }
        else { color = "#D32F2F"; size = 45; }

        return L.divIcon({
            className: "outzen-crowd-marker",
            html: `<div style="background:${color};width:${size}px;height:${size}px;border-radius:50%;border:3px solid white;box-shadow:0 2px 6px rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;color:white;font-weight:bold;font-size:${size > 35 ? '14px' : '12px'};line-height:1;">${n}</div>`,
            iconSize: [size, size],
            iconAnchor: [Math.round(size / 2), Math.round(size / 2)],
            popupAnchor: [0, -Math.round(size / 2)]
        });
    }

    // ---------------- map ----------------
    function initMap(containerId = "leafletMap", center = [50.89, 4.34], zoom = 13) {
        const el = document.getElementById(containerId);
        if (!el) { console.warn(`[OutZen] #${containerId} not found.`); return null; }
        if (typeof L === "undefined") { console.error("[OutZen] Leaflet (L) not loaded."); return null; }

        // cleanup previous instance to avoid leaks when re-booting
        try { if (window.leafletMap && typeof window.leafletMap.remove === "function") window.leafletMap.remove(); } catch { }
        try { delete window.leafletMap; } catch { }

        const map = L.map(containerId).setView(center, zoom);
        L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
            attribution: "© OpenStreetMap contributors",
            maxZoom: 19
        }).addTo(map);

        if (typeof L.markerClusterGroup === "function") {
            OutZen.cluster = L.markerClusterGroup({
                iconCreateFunction(cluster) {
                    const childCount = cluster.getChildCount();
                    const size = childCount < 10 ? 40 : (childCount < 50 ? 50 : 60);
                    let maxLevel = 0;
                    cluster.getAllChildMarkers().forEach(m => { maxLevel = Math.max(maxLevel, m.options.level || 0); });
                    const color = colorFromLevel(maxLevel);
                    return L.divIcon({
                        html: `<div style="background:${color};width:${size}px;height:${size}px;border-radius:50%;display:flex;align-items:center;justify-content:center;color:white;font-weight:bold;">${childCount}</div>`,
                        className: "outzen-cluster",
                        iconSize: [size, size]
                    });
                }
            });
            map.addLayer(OutZen.cluster);
        } else {
            OutZen.cluster = null;
            console.warn("[OutZen] Cluster plugin not detected; using plain markers.");
        }

        OutZen.map = map;
        window.leafletMap = map;
        return map;
    }

    function _bindPopup(marker, level, info) {
        const html = `
      <div style="font-family:Poppins;padding:8px;">
        <strong>${escapeHtml(info?.title)}</strong><br>
        ${escapeHtml(info?.description)}<br><br>
        <span style="color:${colorFromLevel(level)};font-weight:bold;">Level ${Number(level) || 0}</span>
      </div>`;
        return marker.bindPopup(html);
    }

    function addOrUpdateCrowdMarker(id, lat, lng, level = 0, info = { title: "", description: "" }) {
        if (!OutZen.map) { console.warn("[OutZen] map not initialized"); return; }

        const nlat = Number(lat), nlng = Number(lng);
        if (!Number.isFinite(nlat) || !Number.isFinite(nlng)) return;

        const key = String(id);
        const existed = OutZen.markers.has(key);

        if (existed) {
            const existing = OutZen.markers.get(key);
            try { existing.marker.setLatLng([nlat, nlng]); } catch { }
            try { existing.marker.setIcon(iconFromLevel(level)); } catch { }
            existing.marker.options.level = level;
            _bindPopup(existing.marker, level, info);
            return;
        }

        const marker = L.marker([nlat, nlng], { icon: iconFromLevel(level), level });
        _bindPopup(marker, level, info);
        if (OutZen.cluster) OutZen.cluster.addLayer(marker); else marker.addTo(OutZen.map);
        OutZen.markers.set(key, { marker, level, cluster: OutZen.cluster || null });

        // --- auto-fit upon adding a first/new marker ---
        queueMicrotask(() => {
            try { fitToMarkers(); } catch { }
        });
    }

    function removeCrowdMarker(id) {
        const e = OutZen.markers.get(String(id));
        if (!e) return;
        try { e.cluster ? e.cluster.removeLayer(e.marker) : OutZen.map.removeLayer(e.marker); } catch { }
        OutZen.markers.delete(String(id));
    }

    function clearCrowdMarkers() {
        for (const { marker, cluster } of OutZen.markers.values()) {
            try { cluster ? cluster.removeLayer(marker) : OutZen.map.removeLayer(marker); } catch { }
        }
        OutZen.markers.clear();
    }

    function fitToMarkers(padRatio = 0.12, opts = { minZoom: 3, maxZoom: 18, animate: true }) {
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

    // ---------------- GPT markers helpers ----------------
    // Deterministic pseudo-geographic position based on the ID
    function pseudoPositionForId(id) {
        const baseLat = 50.85;  // “OutZen” center (Brussels approx)
        const baseLng = 4.35;

        const s = String(id ?? "");
        let hash = 0;
        for (let i = 0; i < s.length; i++) {
            hash = (hash * 31 + s.charCodeAt(i)) | 0;
        }

        // We generate modest deltas around the center (± ~0.05°)
        const dLat = ((hash % 2000) / 2000) * 0.1 - 0.05;
        const dLng = ((((hash / 2000) | 0) % 2000) / 2000) * 0.1 - 0.05;

        return [baseLat + dLat, baseLng + dLng];
    }

    // Marker for a GPT interaction (reuses the "crowd" logic)
    function addOrUpdateEventMarker(id, prompt, response, createdAt, extra) {
        if (!OutZen.map) {
            console.warn("[OutZen] map not initialized for GPT markers");
            return;
        }

        // 1) Preferred coordinates: extra.lat / extra.lng (or variants)
        let lat = Number(
            extra?.lat ??
            extra?.latitude ??
            extra?.Lat ??
            extra?.Latitude
        );
        let lng = Number(
            extra?.lng ??
            extra?.lon ??
            extra?.longitude ??
            extra?.Lng ??
            extra?.Longitude
        );

        // Pseudo-geographic fallback if no valid coordinates are provided.
        if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
            const [pLat, pLng] = pseudoPositionForId(id);
            lat = pLat;
            lng = pLng;
        }

        // 2) Title/description
        const title =
            (extra && extra.title) ||
            (prompt && prompt.substring(0, 80)) ||
            "GPT interaction";

        const descriptionParts = [];

        if (extra && extra.description) {
            descriptionParts.push(extra.description);
        }

        if (response) {
            descriptionParts.push(String(response).substring(0, 220));
        }

        if (createdAt) {
            const stamp = new Date(createdAt).toLocaleString();
            descriptionParts.push(`Créé / maj : ${stamp}`);
        }

        const desc = descriptionParts.join("\n\n");

        // 3) Visual level (optional): you can use extra.level or source
        let level = 3;
        if (typeof extra?.level === "number") {
            level = extra.level;
        } else if (typeof extra?.source === "string") {
            const src = extra.source.toLowerCase();
            if (src.includes("crowd")) level = 7;
            else if (src.includes("event")) level = 5;
            else if (src.includes("traffic")) level = 8;
            else if (src.includes("weather")) level = 4;
            else if (src.includes("place")) level = 4;
        }

        // 4) Reuse of the “crowd” marker engine
        addOrUpdateCrowdMarker(
            id,
            lat,
            lng,
            level,
            {
                title,
                description: desc
            }
        );
    }
    function removeEventMarker(id) {
        removeCrowdMarker(id);
    }
    function refreshMapSize() {
        if (OutZen.map && typeof OutZen.map.invalidateSize === "function") {
            OutZen.map.invalidateSize(true);
            setTimeout(() => { try { OutZen.map.invalidateSize(true); } catch { } }, 150);
            setTimeout(() => { try { fitToMarkers(); } catch { } }, 220);
        }
    }

    // ---------------- Chart (optional) ----------------
    function initCrowdChart(config = null) {
        if (typeof Chart === "undefined") { console.warn("[OutZen] Chart.js is missing."); return null; }
        const canvas = document.getElementById("crowdChart");
        if (!canvas) { console.warn("[OutZen] #crowdChart not found."); return null; }
        if (OutZen.chart?.destroy) { try { OutZen.chart.destroy(); } catch { } OutZen.chart = null; }

        const ctx = canvas.getContext("2d");
        const cfg = config ?? {
            type: "line",
            data: { labels: [], datasets: [{ label: "Crowd", data: [], tension: 0.25 }] },
            options: { responsive: true, maintainAspectRatio: false }
        };
        OutZen.chart = new Chart(ctx, cfg);
        return OutZen.chart;
    }

    function tryInitCrowdChartLater(retries = 6, intervalMs = 500, config = null) {
        (async function loop(n) {
            if (n <= 0) return;
            if (document.getElementById("crowdChart")) { initCrowdChart(config); return; }
            await sleep(intervalMs);
            loop(n - 1);
        })(retries);
    }

    function destroyCrowdChart() {
        if (OutZen.chart?.destroy) { try { OutZen.chart.destroy(); } catch { } }
        OutZen.chart = null;
    }

    function updateCrowdChart(value) {
        if (!OutZen.chart) return;
        const numeric = Number(String(value).replace("%", "").trim());
        if (!Number.isFinite(numeric)) return;
        const now = new Date().toLocaleTimeString();
        OutZen.chart.data.labels.push(now);
        OutZen.chart.data.datasets[0].data.push(numeric);
        if (OutZen.chart.data.labels.length > 20) {
            OutZen.chart.data.labels.shift();
            OutZen.chart.data.datasets[0].data.shift();
        }
        OutZen.chart.update();
    }

    window.initCrowdChart = function (config) {
        return initCrowdChart(config);
    };
    window.updateCrowdChart = function (value) {
        return updateCrowdChart(value);
    };
    window.destroyCrowdChart = function () {
        return destroyCrowdChart();
    };
    window.tryInitCrowdChartLater = function (retries, intervalMs, config) {
        return tryInitCrowdChartLater(retries, intervalMs, config);
    };

    // ---------------- Boot ----------------
    //async function bootOutZen(opts = { mapId: "leafletMap", center: [50.89, 4.34], zoom: 13, enableChart: false }) {
    //    if (OutZen.map || OutZen.booting) return; // idempotent
    //    OutZen.booting = true;

    //    const elId = opts.mapId || "leafletMap";
    //    for (let i = 0; i < 24; i++) {
    //        if (document.getElementById(elId)) break;
    //        await sleep(150);
    //    }

    //    const map = initMap(elId, opts.center, opts.zoom);
    //    if (!map) { OutZen.booting = false; return; }

    //    if (opts.enableChart) {
    //        const ch = initCrowdChart(opts.chartConfig ?? null);
    //        if (!ch) tryInitCrowdChartLater(6, 500, opts.chartConfig ?? null);
    //    }

    //    refreshMapSize();
    //    window.__outzenBootFlag = true;
    //    OutZen.booting = false;
    //}

    // ---------------- Audio (single implementation) ----------------
    const AudioCtx = window.AudioContext || window.webkitAudioContext;
    const __ozAudioCtx = AudioCtx ? new AudioCtx() : null;

    if (__ozAudioCtx) {
        const unlock = () => {
            __ozAudioCtx.resume().catch(() => { });
            window.removeEventListener("pointerdown", unlock);
        };
        window.addEventListener("pointerdown", unlock, { once: true });
    }

    const BEEP_CFG_KEY = "ozBeepCfg";
    const beepCfg = (() => {
        const def = { volume: 0.06, freq: 880, minIntervalMs: 1500, onlyWhenVisible: true, muted: false };
        try {
            const raw = localStorage.getItem(BEEP_CFG_KEY);
            return raw ? { ...def, ...JSON.parse(raw) } : def;
        } catch {
            return def;
        }
    })();

    const saveCfg = () => {
        try { localStorage.setItem(BEEP_CFG_KEY, JSON.stringify(beepCfg)); } catch { }
    };
    const lastBeepById = new Map();

    function beepOnce(durationMs = 120, freq = beepCfg.freq, volume = beepCfg.volume) {
        if (!__ozAudioCtx) return;
        if (beepCfg.onlyWhenVisible && document.visibilityState !== "visible") return;

        const o = __ozAudioCtx.createOscillator();
        const g = __ozAudioCtx.createGain();
        o.type = "sine";
        o.frequency.value = freq;
        g.gain.value = volume;
        o.connect(g);
        g.connect(__ozAudioCtx.destination);
        o.start();
        const endAt = __ozAudioCtx.currentTime + durationMs / 1000;
        try { g.gain.exponentialRampToValueAtTime(0.0001, endAt - 0.04); } catch { }
        o.stop(endAt);
    }

    function beepCritical(id, overrides) {
        if (beepCfg.muted) return;

        const vol = Math.min(0.08, Math.max(0.03, Number(overrides?.volume ?? beepCfg.volume)));
        const fq = Math.min(1200, Math.max(700, Number(overrides?.freq ?? beepCfg.freq)));
        const onlyWhenVisible = Boolean(overrides?.onlyWhenVisible ?? beepCfg.onlyWhenVisible);

        const now = Date.now();
        const last = lastBeepById.get(id) || 0;
        if (now - last < (beepCfg.minIntervalMs || 1500)) return;
        lastBeepById.set(id, now);

        try { __ozAudioCtx && __ozAudioCtx.resume(); } catch { }
        if (!onlyWhenVisible || document.visibilityState === "visible") {
            beepOnce(120, fq, vol);
        }
    }
    function setBeepConfig(cfg = {}) {
        if (typeof cfg.volume === "number") beepCfg.volume = Math.min(0.08, Math.max(0.03, cfg.volume));
        if (typeof cfg.freq === "number") beepCfg.freq = Math.min(1200, Math.max(700, cfg.freq));
        if (typeof cfg.minIntervalMs === "number") beepCfg.minIntervalMs = Math.max(200, cfg.minIntervalMs | 0);
        if (typeof cfg.onlyWhenVisible === "boolean") beepCfg.onlyWhenVisible = cfg.onlyWhenVisible;
        if (typeof cfg.muted === "boolean") beepCfg.muted = cfg.muted;
        saveCfg();
        return { ...beepCfg };
    }

    const getBeepConfig = () => ({ ...beepCfg });
    const mute = () => { beepCfg.muted = true; saveCfg(); };
    const unmute = () => { beepCfg.muted = false; saveCfg(); };
    const testBeep = (overrides) => beepCritical("__test__", overrides);

    // ---------------- export UMD ----------------
    Object.assign(window.OutZenInterop, {
        // Map / markers "crowd"
        initMap,
        addOrUpdateCrowdMarker,
        removeCrowdMarker,
        clearCrowdMarkers,
        fitToMarkers,
        refreshMapSize,

        // Markers GPT (alias/event-level)
        addOrUpdateEventMarker,
        removeMarker: removeEventMarker,

        // Chart (global API pour compatibilité)
        initCrowdChart,
        tryInitCrowdChartLater,
        destroyCrowdChart,
        updateCrowdChart,

        // Boot & states (si tu veux les réutiliser plus tard)
        // isOutZenBooted: () => !!window.__outzenBootFlag && !!window.OutZenInterop,
        // isOutZenReady: () => !!(OutZen.map),
        // ping: () => true,

        // Audio
        beepCritical,
        setBeepConfig,
        getBeepConfig,
        mute,
        unmute,
        testBeep
    });
    // Alias ​​for weather compatibility (same signature as addOrUpdateCrowdMarker)
    window.OutZenInterop.addOrUpdateWeatherForecastMarker = function (id, tempC, windKmh, summary, info) {
        // You can encode the weather "severity" in the level if you want (e.g., tempC/10)
        const level = 2;
        return window.OutZenInterop.addOrUpdateCrowdMarker(
            id,
            50.85,        // TODO: weather latitude if you have 
            4.35,         // TODO: weather lng if you have
            level,
            {
                title: summary,
                description: JSON.stringify({ tempC, windKmh, ...info })
            }
        );
    };

    // Dev-only: first user action -> audio ping once
    if (location.hostname === 'localhost') {
        document.addEventListener('pointerdown', () => {
            try { window.OutZenInterop?.testBeep?.({ volume: 0.06, freq: 1000 }); } catch { }
        }, { once: true });
    }

    // marker helpers exported for tests (optional)
    // window.__OutZenInternal = OutZen; // uncomment if you need white-box tests

    // --- Simulators (no-op or bridge) -----------------
    //window.simulateTrafficEvent = window.simulateTrafficEvent || function (...args) {
    //    console.log("[Sim] simulateTrafficEvent (noop)", args);
    //};
    //window.simulateWeatherForecastEvent = window.simulateWeatherForecastEvent || function (...args) {
    //    console.log("[Sim] simulateWeatherForecastEvent (noop)", args);
    //};
    //window.simulateCrowdEvent = window.simulateCrowdEvent || function (payload) {
    //    try {
    //        const p = typeof payload === "string" ? JSON.parse(payload) : (payload || {});
    //        const { id = "sim-" + Date.now(), lat = 50.85, lng = 4.35, level = 2, title = "Sim Event", description = "" } = p;
    //        if (window.OutZenInterop?.addOrUpdateCrowdMarker) {
    //            window.OutZenInterop.addOrUpdateCrowdMarker(id, lat, lng, level, { title, description });
    //            setTimeout(() => window.OutZenInterop?.fitToMarkers?.(), 50);
    //        } else {
    //            console.warn("[Sim] OutZenInterop.addOrUpdateCrowdMarker not available yet.");
    //        }
    //    } catch (e) { console.warn("[Sim] simulateCrowdEvent parse error", e); }
    //};

    window.outzen = window.outzen || {};
    window.__outzenPresentationBooted = window.__outzenPresentationBooted || false;
    /**
     * Generic initialization (if you want to keep a global entry point)
     */
    window.outzen.init = async function () {
        // To avoid breaking the old pages,
        // You can simply delegate to initPresentation on /presentation,
        // or do nothing if no container is present.
        return window.outzen.initPresentation();
    };

    /**
     * Specific initialization for the /presentation page
     */
    window.outzen.initPresentation = async function () {
        if (window.__outzenPresentationBooted) {
            // Idempotent: We do not reboot the map or the connections
            return;
        }
        window.__outzenPresentationBooted = true;
        console.log("[OutZen] initPresentation: starting…");

        // 1) Map
        const mapEl = document.getElementById("leafletMap");
        if (mapEl) {
            try {
                const mod = await import("/js/app/leafletOutZen.module.js");

                window.OutZen = window.OutZen || {};
                window.OutZen.notifyHeavyRain = mod.notifyHeavyRain;

                const ok = await mod.bootOutZen({
                    mapId: "leafletMap",
                    center: [50.89, 4.34],
                    zoom: 13,
                    enableChart: false,
                    force: false
                });
                if (!ok) {
                    console.warn("[OutZen] bootOutZen returned false on Presentation page.");
                }
            } catch (e) {
                console.error("[OutZen] Failed to boot LeafletOutZen on Presentation:", e);
            }
        } else {
            console.log("[OutZen] No #leafletMap on this page, skipping map boot.");
        }

        // 2) Scroll animations (if your script still exists)
        if (typeof window.initScrollAnimations === "function") {
            try {
                window.initScrollAnimations();
            } catch (e) {
                console.error("[OutZen] initScrollAnimations failed:", e);
            }
        }

        // 3) OutZen connection (if you have a global hub)
        if (typeof window.startOutzenConnection === "function") {
            try {
                await window.startOutzenConnection();
            } catch (e) {
                console.error("[OutZen] startOutzenConnection failed:", e);
            }
        }

        console.log("[OutZen] initPresentation: done.");
    };
    console.log("[OutZen] interop script loaded (map+chart+audio, legacy UMD).");
})();































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */