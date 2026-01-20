/* wwwroot/js/outzen-interop.js
   Purpose: minimal, idempotent UI interop (nav + small helpers + optional audio)
   Works in DEV with Hot Reload (safe re-eval).
*/
// wwwroot/js/outzen-interop.js
(() => {
    "use strict";

    globalThis.__OutZenBoot ??= { loadedAt: Date.now(), modulePromise: null };
    globalThis.OutZenInterop ??= {};
    globalThis.OutZen ??= {};

    function ensureLeafletModule() {
        if (globalThis.__OutZenBoot.modulePromise) return globalThis.__OutZenBoot.modulePromise;

        globalThis.__OutZenBoot.modulePromise =
            import("/js/app/leafletOutZen.module.js")
                .then((m) => {
                    Object.assign(globalThis.OutZenInterop, m);
                    Object.assign(globalThis.OutZen, m);
                    console.info("[OutZen] ✅ leafletOutZen.module.js loaded");
                    return m;
                })
                .catch((err) => {
                    console.error("[OutZen] ❌ import leafletOutZen.module.js failed", err);
                    throw err;
                });

        return globalThis.__OutZenBoot.modulePromise;
    }

    globalThis.OutZen.ensure = ensureLeafletModule;
    ensureLeafletModule().catch(err => console.warn("[OutZen] preload failed:", err));

    globalThis.OutZen ??= {};
    globalThis.OutZen.scrollRowIntoView = (rowId) => {
        const el = document.getElementById(rowId);
        if (!el) return;
        el.scrollIntoView({ behavior: "smooth", block: "nearest" });
    };
    globalThis.checkElementExists = (id) => {
        const el = document.getElementById(id);
        return !!el;
    };
})();



































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */