/* wwwroot/js/outzen-interop.js */
(() => {
    "use strict";

    console.log("[outzen-interop] loaded");

    globalThis.OutZen =
        (typeof globalThis.OutZen === "object" && globalThis.OutZen)
            ? globalThis.OutZen
            : {};

    globalThis.OutZenInterop =
        (typeof globalThis.OutZenInterop === "object" && globalThis.OutZenInterop)
            ? globalThis.OutZenInterop
            : {};

    const isDev =
        location.hostname === "localhost" ||
        location.hostname === "127.0.0.1";

    const BUILD = globalThis.__ozBuild || "20260318-calendar-fix-2";

    const url = isDev
        ? `/js/app/leafletOutZen.module.js?v=${encodeURIComponent(BUILD)}&t=${Date.now()}`
        : `/js/app/leafletOutZen.module.js?v=${encodeURIComponent(BUILD)}`;

    async function loadModule(force = false) {
        if (force || globalThis.__OutZenImportUrl !== url || !globalThis.__OutZenImportP) {
            globalThis.__OutZenImportUrl = url;
            globalThis.__OutZenImportP = import(url);
        }

        const m = await globalThis.__OutZenImportP;

        for (const [k, v] of Object.entries(m)) {
            globalThis.OutZenInterop[k] = v;
        }

        globalThis.OutZenInterop.__esm = m;
        globalThis.OutZenInterop.module = m;

        return m;
    }

    globalThis.OutZen.ensure = async (force = false) => {
        await loadModule(force);
        return true;
    };

    globalThis.OutZen.reload = async () => {
        globalThis.__OutZenImportP = null;
        globalThis.__OutZenImportUrl = null;
        await loadModule(true);
        return true;
    };

    globalThis.OutZenInterop.pruneMarkersByPrefix = async (prefix, scopeKey) => {
        const m = await loadModule(false);

        if (m?.pruneMarkersByPrefix) {
            return m.pruneMarkersByPrefix(prefix, scopeKey);
        }

        console.warn("[OutZenInterop] pruneMarkersByPrefix missing export", {
            url,
            esmKeys: m ? Object.keys(m) : []
        });

        return 0;
    };

    globalThis.OutZenReady = async () => {
        await globalThis.OutZen.ensure(false);
        console.log("[OutZen] ✅ interop ready:", Object.keys(globalThis.OutZenInterop));
        return true;
    };

    // ---------------------------------------------------------------------
    // Legacy lowercase alias expected by Presentation.razor
    // ---------------------------------------------------------------------
    globalThis.outzen = globalThis.outzen || {};

    globalThis.outzen.initPresentation = async function () {
        try {
            await globalThis.OutZen.ensure(false);

            const mapId = "leafletMap";
            const scopeKey = "presentation";

            const host = document.getElementById(mapId);
            if (!host) {
                console.warn("[outzen.initPresentation] map container not found:", mapId);
                return false;
            }

            const currentMapId = globalThis.OutZenInterop.getCurrentMapId?.(scopeKey);
            if (currentMapId && currentMapId !== mapId) {
                try {
                    globalThis.OutZenInterop.disposeOutZen?.({
                        mapId: currentMapId,
                        scopeKey,
                        allowNoToken: true
                    });
                } catch { }
            }

            const boot = await globalThis.OutZenInterop.bootOutZen({
                mapId,
                scopeKey,
                center: [50.8503, 4.3517],
                zoom: 8,
                enableChart: false,
                force: true,
                resetMarkers: true,
                resetAll: true,
                enableHybrid: true,
                enableCluster: true,
                hybridThreshold: 13
            });

            try {
                globalThis.OutZenInterop.refreshMapSize?.(scopeKey);
            } catch { }

            console.log("[outzen.initPresentation] ok", boot);
            return true;
        } catch (e) {
            console.error("[outzen.initPresentation] failed", e);
            throw e;
        }
    };

    globalThis.outzen.disposePresentation = function () {
        try {
            return globalThis.OutZenInterop.disposeOutZen?.({
                mapId: "leafletMap",
                scopeKey: "presentation",
                allowNoToken: true
            });
        } catch (e) {
            console.warn("[outzen.disposePresentation] failed", e);
            return false;
        }
    };

    // ---------------------------------------------------------------------
    // Safe legacy helpers
    // ---------------------------------------------------------------------
    window.OutZen = window.OutZen || {};

    window.OutZen.safeMakeDrawerDraggable = function (id) {
        try {
            if (typeof window.OutZen.makeDrawerDraggable === "function") {
                return !!window.OutZen.makeDrawerDraggable(id);
            }
        } catch { }
        return false;
    };

    window.OutZen.safeMakeDrawerResizable = function (id) {
        try {
            if (typeof window.OutZen.makeDrawerResizable === "function") {
                return !!window.OutZen.makeDrawerResizable(id);
            }
        } catch { }
        return false;
    };

    window.OutZen.safeBringToFront = function (id) {
        try {
            if (typeof window.OutZen.bringToFront === "function") {
                window.OutZen.bringToFront(id);
            }
        } catch { }
    };

    window.OutZen.safeAvoidOverlap = function (id) {
        try {
            if (typeof window.OutZen.avoidOverlap === "function") {
                window.OutZen.avoidOverlap(id);
            }
        } catch { }
    };

    window.outzenScrollToMap = function (id) {
        const el = document.getElementById(id);
        if (!el) return false;
        el.scrollIntoView({ behavior: "smooth", block: "center" });
        return true;
    };
})();







































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */