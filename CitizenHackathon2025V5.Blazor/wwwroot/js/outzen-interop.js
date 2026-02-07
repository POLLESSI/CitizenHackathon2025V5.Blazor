/* wwwroot/js/outzen-interop.js
   Purpose: minimal, idempotent ESM loader + stable globals for Blazor JSInterop
   - Single import() promise (reload-safe)
   - Overwrites module exports on reload (dev hot reload friendly)
   - Keeps a tiny, stable public surface: OutZen.ensure() + OutZenInterop.*
*/
(() => {
    "use strict";

    // ------------------------------
    // Stable globals
    // ------------------------------
    globalThis.OutZen = (typeof globalThis.OutZen === "object" && globalThis.OutZen) ? globalThis.OutZen : {};
    globalThis.OutZenInterop = (typeof globalThis.OutZenInterop === "object" && globalThis.OutZenInterop) ? globalThis.OutZenInterop : {};
    const OZ = globalThis.OutZenInterop;

    // Single bootstrap state (one per window)
    globalThis.__OutZenBoot = (typeof globalThis.__OutZenBoot === "object" && globalThis.__OutZenBoot) ? globalThis.__OutZenBoot : {
        modulePromise: null,
        lastLoadedAt: 0,
        lastError: null,
    };

    // ------------------------------
    // Loader (idempotent)
    // ------------------------------
    function mergeExports(module) {
        // Keep internal refs
        OZ.__esm = module;
        OZ.__loadedAt = Date.now();
        OZ.__lastError = null;
    }

    async function importModule() {
        const m = await import("/js/app/leafletOutZen.module.js");
        mergeExports(m);

        globalThis.__OutZenBoot.lastLoadedAt = OZ.__loadedAt;
        globalThis.__OutZenBoot.lastError = null;

        console.info("[OutZen] ✅ leafletOutZen.module.js loaded. Exports:", Object.keys(m));
        return m;
    }

    // Define once, never overwritten (safe re-eval)
    if (!Object.prototype.hasOwnProperty.call(OZ, "ensureModule")) {
        Object.defineProperty(OZ, "ensureModule", {
            value: function ensureModule() {
                if (globalThis.__OutZenBoot.modulePromise) return globalThis.__OutZenBoot.modulePromise;

                globalThis.__OutZenBoot.modulePromise = importModule().catch((e) => {
                    // allow retry next time
                    globalThis.__OutZenBoot.modulePromise = null;
                    OZ.__lastError = e;
                    globalThis.__OutZenBoot.lastError = e;

                    console.error("[OutZen] ❌ import leafletOutZen.module.js failed", e);
                    throw e;
                });

                return globalThis.__OutZenBoot.modulePromise;
            },
            writable: false,
            configurable: false,
            enumerable: false,
        });
    }

    // ------------------------------
    // Stable entry point for Blazor
    // ------------------------------
    globalThis.OutZen.ensure = async function ensure() {
        if (globalThis.OutZenInterop?.ensureModule) {
            await globalThis.OutZenInterop.ensureModule();
            return true;
        }
        if (globalThis.OutZenInterop?.isOutZenReady) return true;

        console.error("[OutZen.ensure] OutZenInterop.ensureModule missing. Check script loading order.");
        return false;
    };

    // ------------------------------
    // Debug helpers
    // ------------------------------
    globalThis.OutZenDebug = {
        isLoaded: () => !!globalThis.OutZenInterop?.__esm,
        lastLoadedAt: () => globalThis.OutZenInterop?.__loadedAt ?? 0,
        lastError: () => globalThis.OutZenInterop?.__lastError ?? null,
        dumpState: () => globalThis.OutZenInterop?.dumpState?.(),
        exports: () => Object.keys(globalThis.OutZenInterop?.__esm ?? {}),
    };

    // ------------------------------
    // Wrappers (stable) for Blazor calls
    // ------------------------------
    function wrapScopeDevWarnCall(fnName) {
        return async (...args) => {
            await OZ.ensureModule();

            const m = OZ.__esm;
            const fn = m?.[fnName];
            if (typeof fn !== "function") {
                console.error(`[OutZenInterop] Missing export: ${fnName}`);
                return false;
            }

            const last = args.length ? args[args.length - 1] : null;
            const hasScope = (typeof last === "string" && last.length);

            if (!hasScope && (globalThis.__OZ_ENV === "dev")) {
                console.warn(`[OutZen][${fnName}] called without scopeKey -> using __OutZenActiveScope fallback`);
            }

            // Do not rewrite args; module handles scope via pickScopeKey()
            return fn(...args);
        };
    }

    // Expose stable wrappers (Blazor should call these)
    OZ.bootOutZen = wrapScopeDevWarnCall("bootOutZen");
    OZ.addOrUpdateCrowdMarker = wrapScopeDevWarnCall("addOrUpdateCrowdMarker");
    OZ.addOrUpdateBundleMarkers = wrapScopeDevWarnCall("addOrUpdateBundleMarkers");
    OZ.enableHybridZoom = wrapScopeDevWarnCall("enableHybridZoom");
    OZ.disposeOutZen = wrapScopeDevWarnCall("disposeOutZen");
    OZ.fitToBundles = wrapScopeDevWarnCall("fitToBundles");
    OZ.fitToDetails = wrapScopeDevWarnCall("fitToDetails");
    OZ.fitToCalendarMarkers = wrapScopeDevWarnCall("fitToCalendarMarkers");
    OZ.activateHybridAndZoom = wrapScopeDevWarnCall("activateHybridAndZoom");
    OZ.refreshMapSize = wrapScopeDevWarnCall("refreshMapSize");
    OZ.fitToMarkers = wrapScopeDevWarnCall("fitToMarkers");
    OZ.clearCrowdMarkers = wrapScopeDevWarnCall("clearCrowdMarkers");
    OZ.highlightPlaceMarker = wrapScopeDevWarnCall("highlightPlaceMarker");
    OZ.clearPlaceHighlight = wrapScopeDevWarnCall("clearPlaceHighlight");
    OZ.addOrUpdateSuggestionMarkers = wrapScopeDevWarnCall("addOrUpdateSuggestionMarkers");
    OZ.setWeatherChart = wrapScopeDevWarnCall("setWeatherChart"); 
    OZ.upsertWeatherIntoBundleInput = wrapScopeDevWarnCall("upsertWeatherIntoBundleInput"); 
    OZ.refreshHybridNow = wrapScopeDevWarnCall("refreshHybridNow");
    OZ.forceDetailsMode = wrapScopeDevWarnCall("forceDetailsMode");
    OZ.addOrUpdateWeatherMarkers = wrapScopeDevWarnCall("addOrUpdateWeatherMarkers");
    OZ.upsertCrowdCalendarMarkers = wrapScopeDevWarnCall("upsertCrowdCalendarMarkers");
    OZ.pruneCrowdCalendarMarkers = wrapScopeDevWarnCall("pruneCrowdCalendarMarkers");
    OZ.removeCrowdCalendarMarker = wrapScopeDevWarnCall("removeCrowdCalendarMarker");
    OZ.clearCrowdCalendarMarkers = wrapScopeDevWarnCall("clearCrowdCalendarMarkers");
    OZ.debugDumpMarkers = wrapScopeDevWarnCall("debugDumpMarkers");
    OZ.debugClusterCount = wrapScopeDevWarnCall("debugClusterCount");
    OZ.dumpState = wrapScopeDevWarnCall("dumpState");
    OZ.listScopes = wrapScopeDevWarnCall("listScopes");
    // --- Sync debug helpers (no Promise surprises) ---
    OZ.dumpStateSync = (scopeKey) => {
        const m = OZ.__esm;
        return (m && typeof m.dumpState === "function")
            ? m.dumpState(scopeKey)
            : { loaded: false, scopeKey: scopeKey ?? globalThis.__OutZenActiveScope ?? "main" };
    };
    OZ.listScopesSync = () => {
        const m = OZ.__esm;
        return (m && typeof m.listScopes === "function") ? m.listScopes() : [];
    };
    function bridgeAllExports() {
        const m = OZ.__esm;
        if (!m) return;

        for (const [name, value] of Object.entries(m)) {
            // only functions, skip default / internals
            if (typeof value !== "function") continue;
            if (name.startsWith("_")) continue;

            // don't override existing stable wrappers
            if (typeof OZ[name] === "function") continue;

            OZ[name] = wrapScopeDevWarnCall(name);
        }
    }

    async function ensureAndBridge() {
        await OZ.ensureModule();
        bridgeAllExports();
    }

    // After preload:
    /*ensureAndBridge().catch(() => { });*/

    // Preload (silent)
    /*OZ.ensureModule().catch(() => { });*/

    globalThis.checkElementExists ??= (id) => !!document.getElementById(id);
    globalThis.getScrollTop ??= (el) => el?.scrollTop ?? 0;
    globalThis.getScrollHeight ??= (el) => el?.scrollHeight ?? 0;
    globalThis.getClientHeight ??= (el) => el?.clientHeight ?? 0;

})();





































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */