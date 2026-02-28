/* wwwroot/js/outzen-interop.js
   Minimal, reload-safe ESM loader for Blazor + __esm compatibility
*/
(() => {
    "use strict";

    if (globalThis.__OZ_INTEROP_LOADER_LOADED__ === true) {
        console.warn("[OutZen] outzen-interop already loaded; skipping.");
        return;
    }
    globalThis.__OZ_INTEROP_LOADER_LOADED__ = true;

    globalThis.OutZen = (typeof globalThis.OutZen === "object" && globalThis.OutZen) ? globalThis.OutZen : {};
    globalThis.OutZenInterop = (typeof globalThis.OutZenInterop === "object" && globalThis.OutZenInterop) ? globalThis.OutZenInterop : {};

    const BUILD = globalThis.__ozBuild || "20260224-wxchart-1";
    const url = `/js/app/leafletOutZen.module.js?v=${encodeURIComponent(BUILD)}`;

    globalThis.__OutZenImportP = globalThis.__OutZenImportP || import(url);

    globalThis.OutZen.ensure = async () => {
        const m = await globalThis.__OutZenImportP;

        // expose every export as OutZenInterop.<name>
        for (const [k, v] of Object.entries(m)) {
            globalThis.OutZenInterop[k] = v;
        }

        // 🔥 compat: your Blazor code calls OutZenInterop.__esm.<fn>
        globalThis.OutZenInterop.__esm = m;
        globalThis.OutZenInterop.module = m;

        return true;
    };

    // warmup (optional)
    globalThis.OutZen.ensure()
        .then(() => console.log("[OutZen] ✅ interop ready:", Object.keys(globalThis.OutZenInterop)))
        .catch(err => console.error("[OutZen] ❌ interop import failed", err));
})();








































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */