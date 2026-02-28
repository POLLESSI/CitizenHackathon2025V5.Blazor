///* wwwroot/js/outzen-interop.js
//   Stable globals + ESM loader + Blazor-friendly wrappers (reload-safe)
//*/
//(() => {
//    "use strict";

//    // ------------------------------------------------------------
//    // 0) Stable globals (never replace objects, only mutate)
//    // ------------------------------------------------------------
//    globalThis.OutZen = (globalThis.OutZen && typeof globalThis.OutZen === "object") ? globalThis.OutZen : {};
//    globalThis.OutZenInterop = (globalThis.OutZenInterop && typeof globalThis.OutZenInterop === "object") ? globalThis.OutZenInterop : {};

//    const OutZen = globalThis.OutZen;
//    const OZ = globalThis.OutZenInterop;

//    const BUILD = globalThis.__ozBuild || "20260224-wxchart-1";

//    // Ensure DotNet refs map exists
//    globalThis.__ozDotNet = (globalThis.__ozDotNet instanceof Map) ? globalThis.__ozDotNet : new Map();

//    function now() { return Date.now(); }
//    function logInfo(...a) { console.info("[OutZen]", ...a); }
//    function logWarn(...a) { console.warn("[OutZen]", ...a); }
//    function logErr(...a) { console.error("[OutZen]", ...a); }

//    const PREV = OZ.__build;
//    OZ.__build = BUILD;

//    function ensureFn(name, fn) {
//        if (PREV !== BUILD) { OZ[name] = fn; return; }
//        if (typeof OZ[name] !== "function") OZ[name] = fn;
//    }
//    // ------------------------------------------------------------
//    // 1) Loader state (hot reload safe)
//    // ------------------------------------------------------------
//    const BOOT = globalThis.__OutZenBoot =
//        (globalThis.__OutZenBoot && typeof globalThis.__OutZenBoot === "object")
//            ? globalThis.__OutZenBoot
//            : { modulePromise: null, module: null, lastLoadedAt: 0, lastError: null, boundAt: 0 };

//    // Bump this string to bust cache when you change interop behavior
//    /*const BUILD = globalThis.__ozBuild || "20260224-wxchart-1";*/

//    async function loadModuleOnce() {
//        if (BOOT.module) return BOOT.module;
//        if (BOOT.modulePromise) return BOOT.modulePromise;

//        const url = `/js/app/leafletOutZen.module.js?v=${encodeURIComponent(BUILD)}`;

//        BOOT.modulePromise = import(url)
//            .then(m => {
//                BOOT.module = m;
//                BOOT.lastLoadedAt = now();
//                BOOT.lastError = null;

//                OZ.module = m;
//                OZ.__esm = m;
//                OZ.__loadedAt = BOOT.lastLoadedAt;
//                OZ.__lastError = null;

//                logInfo("✅ module loaded:", Object.keys(m));
//                return m;
//            })
//            .catch(err => {
//                BOOT.modulePromise = null;
//                BOOT.module = null;
//                BOOT.lastError = err;

//                OZ.module = null;
//                OZ.__esm = null;
//                OZ.__lastError = err;

//                logErr("❌ module import failed", err);
//                throw err;
//            });

//        return BOOT.modulePromise;
//    }

//    OZ.ensureModule = OZ.ensureModule || (() => loadModuleOnce());

//    // ------------------------------------------------------------
//    // 2) Binding (optional: direct passthroughs)
//    // ------------------------------------------------------------
//    const REQUIRED_EXPORTS = [
//        "bootOutZen",
//        "disposeOutZen",
//        "isOutZenReady",
//        "dumpState",
//        "listScopes",
//        "refreshMapSize",

//        "addOrUpdateBundleMarkers",
//        "addOrUpdateDetailMarkers",
//        "addOrUpdateCrowdMarker",
//        "addOrUpdatePlaceMarker",
//        "clearCrowdMarkers",
//        "removeCrowdMarker",
//        "addOrUpdateWeatherMarkers",

//        "fitToAllMarkers",
//        "fitToBundles",
//        "fitToDetails",
//        "fitToMarkers",
//        "fitToCalendar",

//        "enableHybridZoom",
//        "activateHybridAndZoom",
//        "unlockHybrid",
//        "refreshHybridNow",
//        "forceDetailsMode",

//        "upsertCrowdCalendarMarkers",
//        "clearCrowdCalendarMarkers",
//        "removeCrowdCalendarMarker",
//        "pruneCrowdCalendarMarkers",
//        "addOrUpdateCrowdCalendarMarker",

//        "addOrUpdateAntennaMarker",
//        "pruneAntennaMarkers",
//        "removeAntennaMarker",

//        "clearTrafficMarkers",
//        "removeTrafficMarker",
//        "upsertTrafficMarker",

//        "clearAllOutZenLayers",
//        "clearMarkersByPrefix",
//        "pruneMarkersByPrefix",
//        "setWeatherChart",

//        "debugDumpMarkers",
//        "debugClusterCount",
//        "debugExplainBundles"
//    ];

//    function bindInterop(m) {
//        try {
//            if (!m || typeof m !== "object") {
//                logWarn("⚠️ bindInterop called with invalid module:", m);
//                return false;
//            }

//            const already = OZ.__boundAt || 0;
//            if (already && BOOT.boundAt === already && BOOT.module === m) return true;

//            for (const name of REQUIRED_EXPORTS) {
//                const fn = m[name];
//                if (typeof fn === "function") {
//                    // Do not replace existing async wrappers
//                    // (but at least guarantee access via __esm)
//                    // -> There's nothing to do here if OZ[name] already exists
//                    if (typeof OZ[name] !== "function") {
//                        OZ[name] = (...args) => fn(...args);
//                    }
//                }
//            }

//            OZ.call = (fnName, ...args) => {
//                const fn = m[fnName];
//                if (typeof fn !== "function") {
//                    const keys = Object.keys(m);
//                    throw new Error(`[OutZenInterop.call] Missing export: ${fnName}. Exports: ${keys.join(", ")}`);
//                }
//                return fn(...args);
//            };

//            BOOT.boundAt = now();
//            OZ.__boundAt = BOOT.boundAt;

//            logInfo("✅ interop bound:", { build: BUILD, boundAt: OZ.__boundAt, exports: Object.keys(m).length });
//            // after module is loaded and OZ.__esm is set
//            OZ.fitToAllMarkers = async (scopeKey, opts) => {
//                const m = OZ.__esm;
//                if (!m) throw new Error("OutZenInterop not ready: __esm missing");

//                // Preferred
//                if (typeof m.fitToAllMarkers === "function")
//                    return m.fitToAllMarkers(scopeKey, opts);

//                // Fallbacks (adapt to what you actually export)
//                if (typeof m.fitToMarkers === "function")
//                    return m.fitToMarkers(scopeKey, opts);

//                if (typeof m.fitToAntennaMarkers === "function")
//                    return m.fitToAntennaMarkers(scopeKey, opts);

//                console.warn("[OutZenInterop] fitToAllMarkers: no compatible function found in module exports");
//                return false;
//            };
//            return true;
//        } catch (err) {
//            BOOT.lastError = err;
//            OZ.__lastError = err;
//            logErr("❌ bindInterop failed", err);
//            return false;
//        }
//    }

//    // ------------------------------------------------------------
//    // 3) Public ensure(): load + bind (idempotent)
//    // ------------------------------------------------------------
//    // DEV: always overwrite ensure to avoid stale functions after hot reload
//    OutZen.ensure = async () => {
//        const m = await OZ.ensureModule();
//        bindInterop(m);
//        return m;
//    };

//    // ------------------------------------------------------------
//    // 4) Async wrappers (Blazor-safe): always load module before calling
//    // ------------------------------------------------------------
//    function wrap(fnName) {
//        return async (...args) => {
//            const m = await OZ.ensureModule();
//            if (!bindInterop(m)) throw new Error(`[OutZenInterop] bindInterop failed for ${fnName}`);
//            const fn = m[fnName];
//            if (typeof fn !== "function") throw new Error(`[OutZenInterop] Missing export: ${fnName}`);
//            return fn(...args);
//        };
//    }

//    // Custom wrapper with fallbacks (IMPORTANT: defined outside bindInterop)
//    function wrapFitAllMarkers() {
//        return async (scopeKey = null, opts = {}) => {
//            const m = await OZ.ensureModule();
//            if (!bindInterop(m)) throw new Error("[OutZenInterop] bindInterop failed for fitToAllMarkers");

//            // Preferred: your real export
//            if (typeof m.fitToAllMarkers === "function") return m.fitToAllMarkers(scopeKey, opts);

//            // Fallbacks (adapt to what exists)
//            if (typeof m.fitToMarkers === "function") return m.fitToMarkers(scopeKey, opts);
//            if (typeof m.fitToAntennaMarkers === "function") return m.fitToAntennaMarkers(scopeKey, opts);

//            console.warn("[OutZenInterop] fitToAllMarkers: no compatible export found");
//            return false;
//        };
//    }

//    // Core map
//    ensureFn("bootOutZen", wrap("bootOutZen"));
//    ensureFn("disposeOutZen", wrap("disposeOutZen"));
//    ensureFn("isOutZenReady", wrap("isOutZenReady"));
//    ensureFn("dumpState", wrap("dumpState"));
//    ensureFn("listScopes", wrap("listScopes"));
//    ensureFn("refreshMapSize", wrap("refreshMapSize"));

//    // Markers + bundles + details
//    ensureFn("addOrUpdateBundleMarkers", wrap("addOrUpdateBundleMarkers"));
//    ensureFn("addOrUpdateDetailMarkers", wrap("addOrUpdateDetailMarkers"));
//    ensureFn("addOrUpdateCrowdMarker", wrap("addOrUpdateCrowdMarker"));
//    ensureFn("clearCrowdMarkers", wrap("clearCrowdMarkers"));
//    ensureFn("removeCrowdMarker", wrap("removeCrowdMarker"));
//    ensureFn("addOrUpdateWeatherMarkers", wrap("addOrUpdateWeatherMarkers"));

//    // Fit
//    ensureFn("fitToBundles", wrap("fitToBundles"));
//    ensureFn("fitToDetails", wrap("fitToDetails"));
//    ensureFn("fitToMarkers", wrap("fitToMarkers"));
//    ensureFn("fitToCalendar", wrap("fitToCalendar"));
//    ensureFn("fitToAllMarkers", wrapFitAllMarkers());

//    // Hybrid
//    ensureFn("enableHybridZoom", wrap("enableHybridZoom"));
//    ensureFn("activateHybridAndZoom", wrap("activateHybridAndZoom"));
//    ensureFn("unlockHybrid", wrap("unlockHybrid"));
//    ensureFn("refreshHybridNow", wrap("refreshHybridNow"));
//    ensureFn("forceDetailsMode", wrap("forceDetailsMode"));

//    // Calendar
//    ensureFn("upsertCrowdCalendarMarkers", wrap("upsertCrowdCalendarMarkers"));
//    ensureFn("clearCrowdCalendarMarkers", wrap("clearCrowdCalendarMarkers"));
//    ensureFn("removeCrowdCalendarMarker", wrap("removeCrowdCalendarMarker"));
//    ensureFn("pruneCrowdCalendarMarkers", wrap("pruneCrowdCalendarMarkers"));
//    ensureFn("addOrUpdateCrowdCalendarMarker", wrap("addOrUpdateCrowdCalendarMarker"));

//    // Antenna
//    ensureFn("addOrUpdateAntennaMarker", wrap("addOrUpdateAntennaMarker"));
//    ensureFn("pruneAntennaMarkers", wrap("pruneAntennaMarkers"));
//    ensureFn("removeAntennaMarker", wrap("removeAntennaMarker"));

//    // Traffic
//    ensureFn("clearTrafficMarkers", wrap("clearTrafficMarkers"));
//    ensureFn("removeTrafficMarker", wrap("removeTrafficMarker"));
//    ensureFn("upsertTrafficMarker", wrap("upsertTrafficMarker"));

//    // Misc
//    ensureFn("clearAllOutZenLayers", wrap("clearAllOutZenLayers"));
//    ensureFn("pruneMarkersByPrefix", wrap("pruneMarkersByPrefix"));
//    ensureFn("setWeatherChart", wrap("setWeatherChart"));

//    // Debug
//    ensureFn("debugDumpMarkers", wrap("debugDumpMarkers"));
//    ensureFn("debugClusterCount", wrap("debugClusterCount"));
//    ensureFn("debugExplainBundles", wrap("debugExplainBundles"));

//    // Prefix helper
//    ensureFn("clearMarkersByPrefix", wrap("clearMarkersByPrefix"));

//    // DotNet refs (sync)
//    ensureFn("registerDotNetRef", (scopeKey, dotnetRef) => {
//        globalThis.__ozDotNet.set(String(scopeKey || "main"), dotnetRef);
//        return true;
//    });

//    ensureFn("unregisterDotNetRef", (scopeKey) => {
//        globalThis.__ozDotNet.delete(String(scopeKey || "main"));
//        return true;
//    });

//    // Debug helper object
//    globalThis.OutZenDebug = (globalThis.OutZenDebug && typeof globalThis.OutZenDebug === "object") ? globalThis.OutZenDebug : {};
//    Object.assign(globalThis.OutZenDebug, {
//        isLoaded: () => !!BOOT.module,
//        exports: () => Object.keys(BOOT.module || {}),
//        lastError: () => BOOT.lastError || null,
//        lastLoadedAt: () => BOOT.lastLoadedAt || 0,
//        state: (scope = "main") => globalThis.OutZenInterop?.dumpState?.(scope),
//        scopes: () => globalThis.OutZenInterop?.listScopes?.()
//    });

//    // Warmup (optional)

//    OutZen.ensure().catch(() => { });

//    logInfo("✅ interop ready:", { build: BUILD, ensure: typeof OutZen.ensure });
//})();
