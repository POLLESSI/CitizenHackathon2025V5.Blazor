/* wwwroot/js/outzen-interop.js */
(() => {
    "use strict";

    console.log("[outzen-interop] loaded");

    globalThis.OutZen = (typeof globalThis.OutZen === "object" && globalThis.OutZen) ? globalThis.OutZen : {};
    globalThis.OutZenInterop = (typeof globalThis.OutZenInterop === "object" && globalThis.OutZenInterop) ? globalThis.OutZenInterop : {};

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
})();







































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */