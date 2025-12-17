///* wwwroot/js/outzen-interop.js (hardened) */
///* global L, Chart */
//(function () {
//    "use strict";

//    console.warn("[OutZen] __OUTZEN_ESM_ONLY__ =", window.__OUTZEN_ESM_ONLY__);

//    if (window.__OUTZEN_ESM_ONLY__) {
//        // Only loads audio (or even nothing)
//        // => keep only beep/mute/etc then return
//        console.log("[OutZen] legacy interop disabled (ESM only).");
//        return;
//    }

//    window.OutZenInterop = window.OutZenInterop || {};

//    if (window.OutZenInterop?.initMap) {
//        // ESM already present: only exposes audio (mute/unmute/testBeep)
//        // ... keep only the AudioCtx/beep section... then
//        Object.assign(window.OutZenInterop, { beepCritical, setBeepConfig, getBeepConfig, mute, unmute, testBeep });
//        console.log("[OutZen] interop: audio-only (ESM owns map).");
//        return;
//    }

//    window.OutZenInterop = window.OutZenInterop || {};
//    // ---------------- internal state ----------------
//    const OutZen = {
//        map: null,
//        cluster: null,
//        markers: new Map(),
//        chart: null,
//        booting: false
//    };

//    // expose a readable boot flag for .NET side polling
//    window.__outzenBootFlag = window.__outzenBootFlag ?? false;

//    // ---------------- utils ----------------
//    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

//    function escapeHtml(s) {
//        return String(s ?? "")
//            .replaceAll("&", "&amp;")
//            .replaceAll("<", "&lt;")
//            .replaceAll(">", "&gt;")
//            .replaceAll('"', "&quot;")
//            .replaceAll("'", "&#39;");
//    }

//    function colorFromLevel(level) {
//        const n = Number(level) || 0;
//        if (n <= 3) return "#4CAF50"; // green
//        if (n <= 6) return "#FFC107"; // amber
//        if (n <= 8) return "#FF5722"; // deep orange
//        return "#D32F2F";             // red
//    }

//    function iconFromLevel(level) {
//        const n = Number(level) || 0;
//        let color = "#9E9E9E", size = 25;
//        if (n <= 3) { color = "#4CAF50"; size = 30; }
//        else if (n <= 6) { color = "#FFC107"; size = 35; }
//        else if (n <= 8) { color = "#FF5722"; size = 40; }
//        else { color = "#D32F2F"; size = 45; }

//        return L.divIcon({
//            className: "outzen-crowd-marker",
//            html: `<div style="background:${color};width:${size}px;height:${size}px;border-radius:50%;border:3px solid white;box-shadow:0 2px 6px rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;color:white;font-weight:bold;font-size:${size > 35 ? '14px' : '12px'};line-height:1;">${n}</div>`,
//            iconSize: [size, size],
//            iconAnchor: [Math.round(size / 2), Math.round(size / 2)],
//            popupAnchor: [0, -Math.round(size / 2)]
//        });
//    }

//    // ---------------- map ----------------
//    async function waitForNonZeroSize(el, retries = 30, delay = 50) {
//        for (let i = 0; i < retries; i++) {
//            const r = el.getBoundingClientRect();
//            if (r.width > 50 && r.height > 50) return true;
//            await new Promise(r => setTimeout(r, delay));
//        }
//        return false;
//    }

//    async function initMap(containerId = "homeMap", center = [50.85, 4.35], zoom = 8, zoomMaybe) {
//        const el = document.getElementById(containerId);
//        if (!el) { console.warn(`[OutZen] #${containerId} not found`); return null; }
//        if (typeof L === "undefined") { console.error("[OutZen] Leaflet not loaded"); return null; }

//        // legacy signature: (id, lat, lng, zoom)
//        if (typeof center === "number") {
//            const latN = Number(center);
//            const lngN = Number(zoom);
//            const zN = Number(zoomMaybe);
//            center = [latN, lngN];
//            zoom = Number.isFinite(zN) ? zN : 13;
//        } else {
//            zoom = Number.isFinite(Number(zoom)) ? Number(zoom) : 13;
//        }

//        // validate center
//        if (!Array.isArray(center) || center.length !== 2) center = [50.85, 4.35];
//        let lat = Number(center[0]), lng = Number(center[1]);
//        if (!Number.isFinite(lat) || !Number.isFinite(lng)) { lat = 50.85; lng = 4.35; }

//        // wait for non-zero size
//        for (let i = 0; i < 30; i++) {
//            const r = el.getBoundingClientRect();
//            if (r.width > 50 && r.height > 50) break;
//            await new Promise(r => setTimeout(r, 50));
//        }
//        const r = el.getBoundingClientRect();
//        if (!(r.width > 50 && r.height > 50)) {
//            console.error("[OutZen] container still has zero size, abort init:", r);
//            return null;
//        }

//        if (OutZen.map) {
//            OutZen.map.setView([lat, lng], zoom);
//            setTimeout(() => { try { OutZen.map.invalidateSize(true); } catch { } }, 100);
//            return OutZen.map;
//        }

//        const map = L.map(containerId, { zoomControl: true, preferCanvas: true }).setView([lat, lng], zoom);

//        L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", { attribution: "© OpenStreetMap", maxZoom: 19 }).addTo(map);

//        if (typeof L.markerClusterGroup === "function") {
//            OutZen.cluster = L.markerClusterGroup();
//            map.addLayer(OutZen.cluster);
//        }

//        OutZen.map = map;
//        window.leafletMap = map;

//        setTimeout(() => { try { map.invalidateSize(true); } catch { } }, 100);
//        return map;
//    }

//    function _bindPopup(marker, level, info) {
//        const html = `
//      <div style="font-family:Poppins;padding:8px;">
//        <strong>${escapeHtml(info?.title)}</strong><br>
//        ${escapeHtml(info?.description)}<br><br>
//        <span style="color:${colorFromLevel(level)};font-weight:bold;">Level ${Number(level) || 0}</span>
//      </div>`;
//        return marker.bindPopup(html);
//    }

//    function addOrUpdateCrowdMarker(id, lat, lng, level = 0, info = { title: "", description: "" }) {
//        if (!OutZen.map) { console.warn("[OutZen] map not initialized"); return; }

//        const nlat = Number(lat), nlng = Number(lng);
//        if (!Number.isFinite(nlat) || !Number.isFinite(nlng)) return;

//        const key = String(id);
//        const existed = OutZen.markers.has(key);

//        if (existed) {
//            const existing = OutZen.markers.get(key);
//            try { existing.marker.setLatLng([nlat, nlng]); } catch { }
//            try { existing.marker.setIcon(iconFromLevel(level)); } catch { }
//            existing.marker.options.level = level;
//            _bindPopup(existing.marker, level, info);
//            return;
//        }

//        const marker = L.marker([nlat, nlng], { icon: iconFromLevel(level), level });
//        _bindPopup(marker, level, info);
//        if (OutZen.cluster) OutZen.cluster.addLayer(marker); else marker.addTo(OutZen.map);
//        OutZen.markers.set(key, { marker, level, cluster: OutZen.cluster || null });

//        // --- auto-fit upon adding a first/new marker ---
//        queueMicrotask(() => {
//            try { fitToMarkers(); } catch { }
//        });
//    }

//    function removeCrowdMarker(id) {
//        const e = OutZen.markers.get(String(id));
//        if (!e) return;
//        try { e.cluster ? e.cluster.removeLayer(e.marker) : OutZen.map.removeLayer(e.marker); } catch { }
//        OutZen.markers.delete(String(id));
//    }

//    function clearCrowdMarkers() {
//        for (const { marker, cluster } of OutZen.markers.values()) {
//            try { cluster ? cluster.removeLayer(marker) : OutZen.map.removeLayer(marker); } catch { }
//        }
//        OutZen.markers.clear();
//    }

//    function fitToMarkers(padRatio = 0.12, opts = { minZoom: 3, maxZoom: 18, animate: true }) {
//        if (!OutZen.map) return;

//        const doFit = () => {
//            let bounds = null;

//            // 1) If a cluster is present, use its bounds (more reliable when there are many items).
//            if (OutZen.cluster && typeof OutZen.cluster.getBounds === 'function') {
//                const b = OutZen.cluster.getBounds();
//                if (b && b.isValid && b.isValid()) bounds = b;
//            }

//            // 2) fallback: bounds from the tracked markers
//            if (!bounds) {
//                const latlngs = [];
//                for (const { marker } of OutZen.markers.values()) {
//                    try { latlngs.push(marker.getLatLng()); } catch { }
//                }
//                if (latlngs.length === 0) return;

//                if (latlngs.length === 1) {
//                    const target = latlngs[0];
//                    const z = Math.min(opts.maxZoom ?? 18, Math.max(opts.minZoom ?? 3, 15));
//                    OutZen.map.setView(target, z, { animate: opts.animate !== false });
//                    return;
//                }
//                bounds = L.latLngBounds(latlngs).pad(0.02);
//            }

//            const padX = Math.max(16, Math.round(padRatio * window.innerWidth));
//            const padY = Math.max(16, Math.round(padRatio * window.innerHeight));
//            OutZen.map.flyToBounds(bounds, {
//                paddingTopLeft: [padX, padY],
//                paddingBottomRight: [padX, padY],
//                maxZoom: opts.maxZoom ?? 17,
//                animate: opts.animate !== false
//            });
//        };

//        // Allow the cluster time to absorb the new markers.
//        requestAnimationFrame(() => setTimeout(doFit, 0));
//    }

//    // ---------------- GPT markers helpers ----------------
//    // Deterministic pseudo-geographic position based on the ID
//    function pseudoPositionForId(id) {
//        const baseLat = 50.85;  // “OutZen” center (Brussels approx)
//        const baseLng = 4.35;

//        const s = String(id ?? "");
//        let hash = 0;
//        for (let i = 0; i < s.length; i++) {
//            hash = (hash * 31 + s.charCodeAt(i)) | 0;
//        }

//        // We generate modest deltas around the center (± ~0.05°)
//        const dLat = ((hash % 2000) / 2000) * 0.1 - 0.05;
//        const dLng = ((((hash / 2000) | 0) % 2000) / 2000) * 0.1 - 0.05;

//        return [baseLat + dLat, baseLng + dLng];
//    }

//    // Marker for a GPT interaction (reuses the "crowd" logic)
//    function addOrUpdateEventMarker(id, prompt, response, createdAt, extra) {
//        if (!OutZen.map) {
//            console.warn("[OutZen] map not initialized for GPT markers");
//            return;
//        }

//        // 1) Preferred coordinates: extra.lat / extra.lng (or variants)
//        let lat = Number(
//            extra?.lat ??
//            extra?.latitude ??
//            extra?.Lat ??
//            extra?.Latitude
//        );
//        let lng = Number(
//            extra?.lng ??
//            extra?.lon ??
//            extra?.longitude ??
//            extra?.Lng ??
//            extra?.Longitude
//        );

//        // Pseudo-geographic fallback if no valid coordinates are provided.
//        if (!Number.isFinite(lat) || !Number.isFinite(lng)) {
//            const [pLat, pLng] = pseudoPositionForId(id);
//            lat = pLat;
//            lng = pLng;
//        }

//        // 2) Title/description
//        const title =
//            (extra && extra.title) ||
//            (prompt && prompt.substring(0, 80)) ||
//            "GPT interaction";

//        const descriptionParts = [];

//        if (extra && extra.description) {
//            descriptionParts.push(extra.description);
//        }

//        if (response) {
//            descriptionParts.push(String(response).substring(0, 220));
//        }

//        if (createdAt) {
//            const stamp = new Date(createdAt).toLocaleString();
//            descriptionParts.push(`Created / maj : ${stamp}`);
//        }

//        const desc = descriptionParts.join("\n\n");

//        // 3) Visual level (optional): you can use extra.level or source
//        let level = 3;
//        if (typeof extra?.level === "number") {
//            level = extra.level;
//        } else if (typeof extra?.source === "string") {
//            const src = extra.source.toLowerCase();
//            if (src.includes("crowd")) level = 7;
//            else if (src.includes("event")) level = 5;
//            else if (src.includes("traffic")) level = 8;
//            else if (src.includes("weather")) level = 4;
//            else if (src.includes("place")) level = 4;
//        }

//        // 4) Reuse of the “crowd” marker engine
//        addOrUpdateCrowdMarker(
//            id,
//            lat,
//            lng,
//            level,
//            {
//                title,
//                description: desc
//            }
//        );
//    }
//    function removeEventMarker(id) {
//        removeCrowdMarker(id);
//    }
//    function refreshMapSize() {
//        if (OutZen.map && typeof OutZen.map.invalidateSize === "function") {
//            OutZen.map.invalidateSize(true);
//            setTimeout(() => { try { OutZen.map.invalidateSize(true); } catch { } }, 150);
//            setTimeout(() => { try { fitToMarkers(); } catch { } }, 220);
//        }
//    }

//    // ---------------- Chart (optional) ----------------
//    function initCrowdChart(config = null) {
//        if (typeof Chart === "undefined") { console.warn("[OutZen] Chart.js is missing."); return null; }
//        const canvas = document.getElementById("crowdChart");
//        if (!canvas) { console.warn("[OutZen] #crowdChart not found."); return null; }
//        if (OutZen.chart?.destroy) { try { OutZen.chart.destroy(); } catch { } OutZen.chart = null; }

//        const ctx = canvas.getContext("2d");
//        const cfg = config ?? {
//            type: "line",
//            data: { labels: [], datasets: [{ label: "Crowd", data: [], tension: 0.25 }] },
//            options: { responsive: true, maintainAspectRatio: false }
//        };
//        OutZen.chart = new Chart(ctx, cfg);
//        return OutZen.chart;
//    }

//    function tryInitCrowdChartLater(retries = 6, intervalMs = 500, config = null) {
//        (async function loop(n) {
//            if (n <= 0) return;
//            if (document.getElementById("crowdChart")) { initCrowdChart(config); return; }
//            await sleep(intervalMs);
//            loop(n - 1);
//        })(retries);
//    }

//    function destroyCrowdChart() {
//        if (OutZen.chart?.destroy) { try { OutZen.chart.destroy(); } catch { } }
//        OutZen.chart = null;
//    }

//    function updateCrowdChart(value) {
//        if (!OutZen.chart) return;
//        const numeric = Number(String(value).replace("%", "").trim());
//        if (!Number.isFinite(numeric)) return;
//        const now = new Date().toLocaleTimeString();
//        OutZen.chart.data.labels.push(now);
//        OutZen.chart.data.datasets[0].data.push(numeric);
//        if (OutZen.chart.data.labels.length > 20) {
//            OutZen.chart.data.labels.shift();
//            OutZen.chart.data.datasets[0].data.shift();
//        }
//        OutZen.chart.update();
//    }

//    window.initCrowdChart = function (config) {
//        return initCrowdChart(config);
//    };
//    window.updateCrowdChart = function (value) {
//        return updateCrowdChart(value);
//    };
//    window.destroyCrowdChart = function () {
//        return destroyCrowdChart();
//    };
//    window.tryInitCrowdChartLater = function (retries, intervalMs, config) {
//        return tryInitCrowdChartLater(retries, intervalMs, config);
//    };

//    // ---------------- Boot ----------------
//    //async function bootOutZen(opts = { mapId: "leafletMap", center: [50.89, 4.34], zoom: 13, enableChart: false }) {
//    //    if (OutZen.map || OutZen.booting) return; // idempotent
//    //    OutZen.booting = true;

//    //    const elId = opts.mapId || "leafletMap";
//    //    for (let i = 0; i < 24; i++) {
//    //        if (document.getElementById(elId)) break;
//    //        await sleep(150);
//    //    }

//    //    const map = initMap(elId, opts.center, opts.zoom);
//    //    if (!map) { OutZen.booting = false; return; }

//    //    if (opts.enableChart) {
//    //        const ch = initCrowdChart(opts.chartConfig ?? null);
//    //        if (!ch) tryInitCrowdChartLater(6, 500, opts.chartConfig ?? null);
//    //    }

//    //    refreshMapSize();
//    //    window.__outzenBootFlag = true;
//    //    OutZen.booting = false;
//    //}

//    // ---------------- export UMD ----------------
//    Object.assign(window.OutZenInterop, {
//        // Map / markers "crowd"
//        initMap,
//        addOrUpdateCrowdMarker,
//        removeCrowdMarker,
//        clearCrowdMarkers,
//        fitToMarkers,
//        refreshMapSize,

//        // Markers GPT (alias/event-level)
//        addOrUpdateEventMarker,
//        addOrUpdateBundleMarkers,
//        removeMarker: removeEventMarker,

//        // Chart (global API for compatibility)
//        initCrowdChart,
//        tryInitCrowdChartLater,
//        destroyCrowdChart,
//        updateCrowdChart,

//        // Boot & states (if you want to reuse them later)
//        // isOutZenBooted: () => !!window.__outzenBootFlag && !!window.OutZenInterop,
//        // isOutZenReady: () => !!(OutZen.map),
//        // ping: () => true,

//        // Audio
//        beepCritical,
//        setBeepConfig,
//        getBeepConfig,
//        mute,
//        unmute,
//        testBeep
//    });
//    // Alias ​​for weather compatibility (same signature as addOrUpdateCrowdMarker)
//    window.OutZenInterop.addOrUpdateWeatherForecastMarker = function (id, tempC, windKmh, summary, info) {
//        // You can encode the weather "severity" in the level if you want (e.g., tempC/10)
//        const level = 2;
//        return window.OutZenInterop.addOrUpdateCrowdMarker(
//            id,
//            50.85,        // TODO: weather latitude if you have
//            4.35,         // TODO: weather lng if you have
//            level,
//            {
//                title: summary,
//                description: JSON.stringify({ tempC, windKmh, ...info })
//            }
//        );
//    };

//    // Dev-only: first user action -> audio ping once
//    if (location.hostname === 'localhost') {
//        document.addEventListener('pointerdown', () => {
//            try { window.OutZenInterop?.testBeep?.({ volume: 0.06, freq: 1000 }); } catch { }
//        }, { once: true });
//    }

//    // marker helpers exported for tests (optional)
//    // window.__OutZenInternal = OutZen; // uncomment if you need white-box tests

//    // --- Simulators (no-op or bridge) -----------------
//    //window.simulateTrafficEvent = window.simulateTrafficEvent || function (...args) {
//    //    console.log("[Sim] simulateTrafficEvent (noop)", args);
//    //};
//    //window.simulateWeatherForecastEvent = window.simulateWeatherForecastEvent || function (...args) {
//    //    console.log("[Sim] simulateWeatherForecastEvent (noop)", args);
//    //};
//    //window.simulateCrowdEvent = window.simulateCrowdEvent || function (payload) {
//    //    try {
//    //        const p = typeof payload === "string" ? JSON.parse(payload) : (payload || {});
//    //        const { id = "sim-" + Date.now(), lat = 50.85, lng = 4.35, level = 2, title = "Sim Event", description = "" } = p;
//    //        if (window.OutZenInterop?.addOrUpdateCrowdMarker) {
//    //            window.OutZenInterop.addOrUpdateCrowdMarker(id, lat, lng, level, { title, description });
//    //            setTimeout(() => window.OutZenInterop?.fitToMarkers?.(), 50);
//    //        } else {
//    //            console.warn("[Sim] OutZenInterop.addOrUpdateCrowdMarker not available yet.");
//    //        }
//    //    } catch (e) { console.warn("[Sim] simulateCrowdEvent parse error", e); }
//    //};

//    window.outzen = window.outzen || {};
//    window.__outzenPresentationBooted = window.__outzenPresentationBooted || false;
//    /**
//     * Generic initialization (if you want to keep a global entry point)
//     */
//    window.outzen.init = async function () {
//        // To avoid breaking the old pages,
//        // You can simply delegate to initPresentation on /presentation,
//        // or do nothing if no container is present.
//        return window.outzen.initPresentation();
//    };

//    /**
//     * Specific initialization for the /presentation page
//     */
//    window.outzen.initPresentation = async function () {
//        if (window.__outzenPresentationBooted) {
//            // Idempotent: We do not reboot the map or the connections
//            return;
//        }
//        window.__outzenPresentationBooted = true;
//        console.log("[OutZen] initPresentation: starting…");

//        // 1) Map
//        const mapEl = document.getElementById("leafletMap");
//        if (mapEl) {
//            try {
//                const mod = await import("/js/app/leafletOutZen.module.js");

//                window.OutZen = window.OutZen || {};
//                window.OutZen.notifyHeavyRain = mod.notifyHeavyRain;

//                const ok = await mod.bootOutZen({
//                    mapId: "leafletMap",
//                    center: [50.89, 4.34],
//                    zoom: 13,
//                    enableChart: false,
//                    force: false
//                });
//                if (!ok) {
//                    console.warn("[OutZen] bootOutZen returned false on Presentation page.");
//                }
//            } catch (e) {
//                console.error("[OutZen] Failed to boot LeafletOutZen on Presentation:", e);
//            }
//        } else {
//            console.log("[OutZen] No #leafletMap on this page, skipping map boot.");
//        }

//        // 2) Scroll animations (if your script still exists)
//        if (typeof window.initScrollAnimations === "function") {
//            try {
//                window.initScrollAnimations();
//            } catch (e) {
//                console.error("[OutZen] initScrollAnimations failed:", e);
//            }
//        }

//        // 3) OutZen connection (if you have a global hub)
//        if (typeof window.startOutzenConnection === "function") {
//            try {
//                await window.startOutzenConnection();
//            } catch (e) {
//                console.error("[OutZen] startOutzenConnection failed:", e);
//            }
//        }

//        console.log("[OutZen] initPresentation: done.");
//    };
//    console.log("[OutZen] interop script loaded (map+chart+audio, legacy UMD).");
//})();
/* wwwroot/js/outzen-interop.js
   Purpose: minimal, idempotent UI interop (nav + small helpers + optional audio)
   Works in DEV with Hot Reload (safe re-eval).
*/
(() => {
    "use strict";

    // Global singleton (hot reload safe)
    const S = (window.__OutZenInteropSingleton = window.__OutZenInteropSingleton || {
        version: "2025.12.17",
        nav: { wired: false, open: false },
        audio: { enabled: false, muted: false, cfg: { freq: 880, ms: 90, gain: 0.04 } },
    });

    // -----------------------------
    // Small utilities (safe)
    // -----------------------------
    function $(sel) { return document.querySelector(sel); }
    function addClass(el, c) { if (el) el.classList.add(c); }
    function rmClass(el, c) { if (el) el.classList.remove(c); }

    function setNavLock(locked) {
        const html = document.documentElement;
        const body = document.body;
        if (locked) { addClass(html, "nav-lock"); addClass(body, "nav-lock"); }
        else { rmClass(html, "nav-lock"); rmClass(body, "nav-lock"); }
    }

    // -----------------------------
    // NAV (desktop + mobile overlay)
    // -----------------------------
    function wireNavOnce() {
        if (S.nav.wired) return;

        // Mark as wired once DOM exists
        const nav = $("nav.main-nav");
        if (!nav) return;

        // Ensure overlay exists
        let overlay = $(".nav-overlay");
        if (!overlay) {
            overlay = document.createElement("div");
            overlay.className = "nav-overlay";
            overlay.setAttribute("aria-hidden", "true");
            document.body.appendChild(overlay);
        }

        const toggleBtn = nav.querySelector(".nav-toggle");
        const links = nav.querySelector(".nav-links");

        if (!toggleBtn || !links) {
            console.warn("[OutZen/Nav] Missing .nav-toggle or .nav-links");
            S.nav.wired = true; // avoid spamming
            return;
        }

        function open() {
            S.nav.open = true;
            addClass(nav, "is-open");
            addClass(overlay, "is-open");
            setNavLock(true);
            toggleBtn.setAttribute("aria-expanded", "true");
        }

        function close() {
            S.nav.open = false;
            rmClass(nav, "is-open");
            rmClass(overlay, "is-open");
            setNavLock(false);
            toggleBtn.setAttribute("aria-expanded", "false");
        }

        function toggle() {
            if (S.nav.open) close();
            else open();
        }

        // Toggle click
        toggleBtn.addEventListener("click", (e) => {
            e.preventDefault();
            e.stopPropagation();
            toggle();
        });

        // Click overlay closes
        overlay.addEventListener("click", (e) => {
            e.preventDefault();
            close();
        });

        // Click outside nav closes (mobile only)
        document.addEventListener("pointerdown", (e) => {
            if (!S.nav.open) return;
            const t = e.target;
            if (!(t instanceof Element)) return;
            if (nav.contains(t)) return;
            close();
        }, { passive: true });

        // Any nav link click closes (mobile)
        nav.addEventListener("click", (e) => {
            const t = e.target;
            if (!(t instanceof Element)) return;
            const a = t.closest("a");
            if (!a) return;
            close();
        });

        // Escape closes
        document.addEventListener("keydown", (e) => {
            if (!S.nav.open) return;
            if (e.key === "Escape") close();
        });

        // Responsive safety: if you resize to desktop, force close.
        const mq = window.matchMedia("(min-width: 900px)");
        mq.addEventListener?.("change", () => { if (mq.matches) close(); });

        S.nav.wired = true;
        console.info("[OutZen/Nav] wired");
    }

    // Public API
    window.OutZen = window.OutZen || {};
    window.OutZen.nav = window.OutZen.nav || {};

    window.OutZen.nav.wire = () => {
        // Defer until DOM is ready
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", wireNavOnce, { once: true });
        } else {
            wireNavOnce();
        }
    };

    window.OutZen.nav.close = () => {
        const nav = $("nav.main-nav");
        const overlay = $(".nav-overlay");
        if (!nav || !overlay) return;
        rmClass(nav, "is-open");
        rmClass(overlay, "is-open");
        setNavLock(false);
        S.nav.open = false;
    };

    // -----------------------------
    // Scroll helper (used by .NET)
    // -----------------------------
    window.scrollIntoViewById = (id, options) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.scrollIntoView(options || { behavior: "smooth", block: "start" });
    };

    // -----------------------------
    // Optional: tiny audio beep API
    // -----------------------------
    let audioCtx = null;

    function ensureAudio() {
        if (audioCtx) return audioCtx;
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        if (!AudioContext) return null;
        audioCtx = new AudioContext();
        return audioCtx;
    }

    function beep(freq, ms, gain) {
        if (S.audio.muted) return;
        const ctx = ensureAudio();
        if (!ctx) return;

        const f = Number(freq) || S.audio.cfg.freq;
        const d = Number(ms) || S.audio.cfg.ms;
        const g = Number(gain) || S.audio.cfg.gain;

        const o = ctx.createOscillator();
        const a = ctx.createGain();
        o.frequency.value = f;
        a.gain.value = g;
        o.connect(a);
        a.connect(ctx.destination);
        o.start();

        setTimeout(() => {
            try { o.stop(); } catch { }
            try { o.disconnect(); a.disconnect(); } catch { }
        }, Math.max(20, d));
    }

    window.OutZen.audio = window.OutZen.audio || {};
    window.OutZen.audio.testBeep = () => beep(880, 90, 0.04);
    window.OutZen.audio.mute = () => { S.audio.muted = true; };
    window.OutZen.audio.unmute = () => { S.audio.muted = false; };

    // Auto-wire nav once (safe)
    window.OutZen.nav.wire();
})();































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */