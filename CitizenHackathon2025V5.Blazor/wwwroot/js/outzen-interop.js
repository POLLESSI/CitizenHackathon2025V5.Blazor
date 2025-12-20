/* wwwroot/js/outzen-interop.js
   Purpose: minimal, idempotent UI interop (nav + small helpers + optional audio)
   Works in DEV with Hot Reload (safe re-eval).
*/
// wwwroot/js/outzen-interop.js
(() => {
    "use strict";

    // Guard global (safe hot reload)
    window.__OutZenBoot ??= { loadedAt: Date.now(), modulePromise: null };

    // Single global namespace
    window.OutZen ??= {};
    function hookLeafletMapIfAsked() {
        const url = new URL(location.href);
        if (url.searchParams.get("hook") !== "1") return;

        if (!window.L || !window.L.map) {
            console.warn("[HOOK] Leaflet not ready yet (retry).");
            setTimeout(hookLeafletMapIfAsked, 250);
            return;
        }
        if (window.__LMapHooked) return;

        window.__LMapHooked = true;
        const original = window.L.map.bind(window.L);
        window.L.map = (...args) => {
            console.warn("[HOOK] L.map called with:", args);
            return original(...args);
        };
        console.info("[HOOK] ✅ L.map hooked");
    }

    hookLeafletMapIfAsked();


    function ensureLeafletModule() {
        if (window.__OutZenBoot.modulePromise) return window.__OutZenBoot.modulePromise;

        window.__OutZenBoot.modulePromise = import("/js/app/leafletOutZen.module.js")
            .then((m) => {
                Object.assign(window.OutZen, m);
                console.info("[OutZen] ✅ leafletOutZen.module.js loaded & bound to window.OutZen");
                return m;
            })
            .catch((err) => {
                console.error("[OutZen] ❌ Failed to import leafletOutZen.module.js", err);
                // Important: rethrow so Blazor can see it if awaited
                 throw err;
            });

        return window.__OutZenBoot.modulePromise;
    }

    // Public awaitable
    window.OutZen.ensure = ensureLeafletModule;

    // Pure UI helper
    window.OutZen.setNavLock = (locked) => {
        document.documentElement.classList.toggle("nav-lock", !!locked);
        document.body.classList.toggle("nav-lock", !!locked);
    };

    // Optional preload
    ensureLeafletModule();
})();


































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */