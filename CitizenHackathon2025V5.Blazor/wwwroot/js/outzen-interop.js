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

    globalThis.__OZ_INTEROP_READY_P__ = globalThis.__OZ_INTEROP_READY_P__ || (async () => {
        globalThis.OutZen = (typeof globalThis.OutZen === "object" && globalThis.OutZen) ? globalThis.OutZen : {};
        globalThis.OutZenInterop = (typeof globalThis.OutZenInterop === "object" && globalThis.OutZenInterop) ? globalThis.OutZenInterop : {};

        const OZ = globalThis.OutZenInterop;

        const BUILD = globalThis.__ozBuild || "20260315-calendar-warning-fix-1";
        const url = `/js/app/leafletOutZen.module.js?v=${encodeURIComponent(BUILD)}`;

        if (globalThis.__OutZenImportUrl !== url) {
            globalThis.__OutZenImportUrl = url;
            globalThis.__OutZenImportP = import(url);
        }

        globalThis.OutZen.ensure = async () => {
            const m = await globalThis.__OutZenImportP;
            for (const [k, v] of Object.entries(m)) {
                globalThis.OutZenInterop[k] = v;
            }
            globalThis.OutZenInterop.__esm = m;
            globalThis.OutZenInterop.module = m;
            return true;
        };

        OZ.pruneMarkersByPrefix = async (prefix, scopeKey) => {
            await globalThis.OutZen.ensure();
            const m = globalThis.OutZenInterop.__esm;

            if (m?.pruneMarkersByPrefix) {
                return m.pruneMarkersByPrefix(scopeKey, prefix);
            }

            console.warn("[OutZenInterop] pruneMarkersByPrefix: missing ESM export, noop.", {
                url,
                esmKeys: m ? Object.keys(m) : []
            });
            return 0;
        };

        await globalThis.OutZen.ensure();

        const oldPrune = globalThis.OutZenInterop.pruneMarkersByPrefix;
        globalThis.OutZenInterop.pruneMarkersByPrefix = async (...args) => {
            console.log("[DBG] pruneMarkersByPrefix called", args, {
                hasEsm: !!globalThis.OutZenInterop.__esm,
                esmKeys: globalThis.OutZenInterop.__esm ? Object.keys(globalThis.OutZenInterop.__esm) : [],
                keys: Object.keys(globalThis.OutZenInterop || {}).slice(0, 20)
            });
            return oldPrune?.(...args);
        };

        console.log("[OutZen] ✅ interop ready:", Object.keys(globalThis.OutZenInterop));
        return true;
    })();

    globalThis.OutZenReady = async () => {
        return await globalThis.__OZ_INTEROP_READY_P__;
    };
})();







































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */