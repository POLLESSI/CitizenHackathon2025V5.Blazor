/* wwwroot/js/outzen-interop.js
   Stable globals + ESM loader + Blazor-friendly wrappers (reload-safe)
   - OutZenInterop.* : wrappers to call ESM exports from C#
   - OutZen.*        : helpers UI (drag, resize, bringToFront, avoidOverlap)
*/
(() => {
    "use strict";

    // -------------------------------------------------------------------
    // 0) Stable globals
    // -------------------------------------------------------------------
    globalThis.OutZen = (globalThis.OutZen && typeof globalThis.OutZen === "object") ? globalThis.OutZen : {};
    globalThis.OutZenInterop = (globalThis.OutZenInterop && typeof globalThis.OutZenInterop === "object") ? globalThis.OutZenInterop : {};

    queueMicrotask(() => {
        try {
            globalThis.OutZen?.ensure?.()
                .then(() => {
                    // debug optionnel après chargement
                    globalThis.OutZenInterop?.debugExplainBundles?.("home");
                    // globalThis.OutZenInterop?.forceDetailsMode?.("home");
                    // globalThis.OutZenInterop?.refreshHybridNow?.("home");
                })
                .catch(() => { });
        } catch { }
    });

    if (!globalThis.OutZenInterop || typeof globalThis.OutZenInterop !== "object") globalThis.OutZenInterop = {};
    if (!globalThis.OutZen || typeof globalThis.OutZen !== "object") globalThis.OutZen = {};

    const OutZen = globalThis.OutZen;
    const OZ = globalThis.OutZenInterop;
    //// ✅ Ensures a stable shape BEFORE any reading
    //OZ.module ??= null;
    //OZ.__esm ??= null;

    // DotNet bridge MUST be available even if ESM isn't loaded yet
    globalThis.__ozDotNet = globalThis.__ozDotNet instanceof Map ? globalThis.__ozDotNet : new Map();

    OZ.registerDotNetRef ||= (scopeKey, dotnetRef) => {
        globalThis.__ozDotNet.set(String(scopeKey || "main"), dotnetRef);
        return true;
    };

    OZ.unregisterDotNetRef ||= (scopeKey) => {
        globalThis.__ozDotNet.delete(String(scopeKey || "main"));
        return true;
    };

    OZ.addOrUpdateBundleMarkers ||= async (...args) => {
        console.log("[DBG] addOrUpdateBundleMarkers args=", args);
        const m = await OZ.ensureModule(); bindInterop(m);
        return m.addOrUpdateBundleMarkers(...args);
    };

    // -------------------------------------------------------------------
    // 1) Single loader state (reload-safe across hot reload)
    // -------------------------------------------------------------------
    //await OutZen.ensure();
    //OutZenInterop.debugExplainBundles("home");

    const BOOT = globalThis.__OutZenBoot =
        (globalThis.__OutZenBoot && typeof globalThis.__OutZenBoot === "object")
            ? globalThis.__OutZenBoot
            : { modulePromise: null, module: null, lastLoadedAt: 0, lastError: null, bound: false, boundAt: 0 };

    // Set your build/version string here (or override by globalThis.__ozBuild)
    const BUILD = globalThis.__ozBuild || "20260220-interop-1";

    function now() { return Date.now(); }

    function logInfo(...a) { console.info("[OutZen]", ...a); }
    function logWarn(...a) { console.warn("[OutZen]", ...a); }
    function logErr(...a) { console.error("[OutZen]", ...a); }

    async function loadModuleOnce() {
        if (BOOT.module) return BOOT.module;
        if (BOOT.modulePromise) return BOOT.modulePromise;

        BOOT.modulePromise = import(`/js/app/leafletOutZen.module.js?v=${encodeURIComponent(BUILD)}`)
            .then(m => {
                BOOT.module = m;
                BOOT.lastLoadedAt = now();
                BOOT.lastError = null;

                OZ.module = m; 
                OZ.__esm = m;
                OZ.__loadedAt = BOOT.lastLoadedAt;
                OZ.__lastError = null;

                logInfo("✅ module loaded:", Object.keys(m));
                return m;
            })
            .catch(err => {
                BOOT.modulePromise = null;
                BOOT.module = null;
                BOOT.lastError = err;

                OZ.module = null;
                OZ.__esm = null;
                OZ.__lastError = err;

                logErr("❌ module import failed", err);
                throw err;
            });

        return BOOT.modulePromise;
    }

    OZ.ensureModule ||= function ensureModule() {
        return loadModuleOnce();
    };

    // ✅ unlockHybrid shim (safe)
    // But: provide a function called by Blazor even if your ESM does not export "unlockHybrid"
    OZ.unlockHybrid ||= async (scopeKey = null) => {
        const m = await OZ.ensureModule();
        bindInterop(m);

        // If the ESM module exports unlockHybrid, it is called
        if (typeof m.unlockHybrid === "function") return m.unlockHybrid(scopeKey);

        // Otherwise, we try existing strategies.
        if (typeof m.enableHybridZoom === "function") return m.enableHybridZoom(true, 13, scopeKey); // adapts if different signature
        if (typeof m.activateHybridAndZoom === "function") return m.activateHybridAndZoom(scopeKey, 13);
        if (typeof m.refreshHybridNow === "function") return m.refreshHybridNow(scopeKey);

        console.warn("[OutZenInterop.unlockHybrid] no hybrid functions available; noop");
        return true;
    };

    // -------------------------------------------------------------------
    // 2) Binding: attach OutZenInterop.<fn> to ESM exports exactly once
    // -------------------------------------------------------------------
    const REQUIRED_EXPORTS = [
        "bootOutZen",
        "disposeOutZen",
        "isOutZenReady",
        "dumpState",
        "listScopes",
        "refreshMapSize",

        "addOrUpdateBundleMarkers",
        "addOrUpdateDetailMarkers",
        "addOrUpdateCrowdMarker",
        "clearCrowdMarkers",
        "removeCrowdMarker",
        "addOrUpdateWeatherMarkers",

        "fitToBundles",
        "fitToDetails",
        "fitToMarkers",

        "enableHybridZoom",
        "activateHybridAndZoom",
        "unlockHybrid",
        "refreshHybridNow",
        "forceDetailsMode",

        "upsertCrowdCalendarMarkers",
        "pruneCrowdCalendarMarkers",
        "addOrUpdateCrowdCalendarMarker",

        "addOrUpdateAntennaMarker",
        "pruneAntennaMarkers",
        "removeAntennaMarker",          

        //"registerDotNetRef",
        //"unregisterDotNetRef",         

        "debugDumpMarkers",
        "debugClusterCount",
        "debugExplainBundles"
    ];
    function bindInterop(m) {
        if (BOOT.bound && OZ.__boundAt === BOOT.boundAt) return true;

        let missing = [];
        for (const name of REQUIRED_EXPORTS) {
            const fn = m && m[name];
            if (typeof fn !== "function") missing.push(name);
        }

        if (missing.length) {
            // Don't throw here: keep app alive; but make it loud.
            logErr("❌ Missing ESM exports:", missing, "Available:", m ? Object.keys(m) : []);
            // Still bind the ones that exist to avoid total failure.
        }

        // Bind everything as direct passthrough functions (fast, predictable)
        for (const name of REQUIRED_EXPORTS) {
            if (typeof (m && m[name]) === "function") {
                OZ[name] = (...args) => m[name](...args);
            }
        }

        // also keep a generic call helper (handy for debugging)
        OZ.call = (fnName, ...args) => {
            const fn = m && m[fnName];
            if (typeof fn !== "function") {
                const keys = m ? Object.keys(m) : [];
                throw new Error(`[OutZenInterop.call] Missing export: ${fnName}. Exports: ${keys.join(", ")}`);
            }
            return fn(...args);
        };

        BOOT.bound = true;
        BOOT.boundAt = now();
        OZ.__boundAt = BOOT.boundAt;

        logInfo("✅ interop bound:", {
            build: BUILD,
            boundAt: OZ.__boundAt,
            exports: m ? Object.keys(m).length : 0,
            hasRegisterDotNetRef: typeof OZ.registerDotNetRef === "function",
        });

        return true;
    }

    // -------------------------------------------------------------------
    // 3) Public ensure(): load + bind (idempotent)
    // -------------------------------------------------------------------
    OutZen.ensure ||= async () => {
        const m = await OZ.ensureModule();
        bindInterop(m);
        return m;
    };

    // -------------------------------------------------------------------
    // 4) Wrappers: for safety, we keep async wrappers too (optional)
    //    - You can call OZ.<fn> directly after OutZen.ensure()
    //    - But calling OZ.<fn> before ensure() should still work:
    //      these wrappers load the module on demand.
    // -------------------------------------------------------------------
    function wrap(fnName) {
        return async (...args) => {
            const m = await OZ.ensureModule();
            bindInterop(m);
            const fn = m && m[fnName];
            if (typeof fn !== "function") {
                const keys = m ? Object.keys(m) : [];
                throw new Error(`[OutZenInterop] Missing export: ${fnName}. Exports: ${keys.join(", ")}`);
            }
            return fn(...args);
        };
    }

    // Only define wrappers if not already bound (keeps stable references)
    // Core map
    OZ.bootOutZen ||= wrap("bootOutZen");
    OZ.disposeOutZen ||= wrap("disposeOutZen");
    OZ.isOutZenReady ||= wrap("isOutZenReady");
    OZ.dumpState ||= wrap("dumpState");
    OZ.listScopes ||= wrap("listScopes");
    OZ.refreshMapSize ||= wrap("refreshMapSize");

    // Markers
    OZ.addOrUpdateBundleMarkers ||= wrap("addOrUpdateBundleMarkers");
    OZ.addOrUpdateDetailMarkers ||= wrap("addOrUpdateDetailMarkers");
    OZ.addOrUpdateCrowdMarker ||= wrap("addOrUpdateCrowdMarker");
    OZ.clearCrowdMarkers ||= wrap("clearCrowdMarkers");
    OZ.removeCrowdMarker ||= wrap("removeCrowdMarker");
    OZ.addOrUpdateCrowdCalendarMarker ||= wrap("addOrUpdateCrowdCalendarMarker");
    OZ.addOrUpdateWeatherMarkers ||= wrap("addOrUpdateWeatherMarkers");

    // Hybrid zoom + view
    OZ.enableHybridZoom ||= wrap("enableHybridZoom");
    OZ.activateHybridAndZoom ||= wrap("activateHybridAndZoom");
    OZ.refreshHybridNow ||= wrap("refreshHybridNow");
    OZ.forceDetailsMode ||= wrap("forceDetailsMode");
    OZ.fitToBundles ||= wrap("fitToBundles");
    OZ.fitToDetails ||= wrap("fitToDetails");
    OZ.fitToMarkers ||= wrap("fitToMarkers");

    // Calendar
    OZ.upsertCrowdCalendarMarkers ||= wrap("upsertCrowdCalendarMarkers");
    OZ.pruneCrowdCalendarMarkers ||= wrap("pruneCrowdCalendarMarkers");

    // Antenna
    OZ.addOrUpdateAntennaMarker ||= wrap("addOrUpdateAntennaMarker");
    OZ.pruneAntennaMarkers ||= wrap("pruneAntennaMarkers");
    OZ.removeAntennaMarker ||= wrap("removeAntennaMarker");

    // DotNet bridge (CRITICAL)
    //OZ.registerDotNetRef ||= wrap("registerDotNetRef");
    //OZ.unregisterDotNetRef ||= wrap("unregisterDotNetRef");

    // Debug
    OZ.debugDumpMarkers ||= wrap("debugDumpMarkers");
    OZ.debugClusterCount ||= wrap("debugClusterCount");

    // Optional ensureBoot helper (kept)
    OZ.ensureBoot ||= async (scopeKey, mapId) => {
        const m = await OutZen.ensure();
        const s = (globalThis.__OutZenGetS?.(scopeKey)) ?? null;
        if (s?.initialized && s?.map) return true;
        if (!mapId) return false;

        const el = document.getElementById(mapId);
        if (!el) return false;

        await m.bootOutZen({ mapId, scopeKey, force: true });
        return true;
    };

    // -------------------------------------------------------------------
    // 5) Debug helpers
    // -------------------------------------------------------------------
    globalThis.OutZenDebug ||= {};
    globalThis.OutZenDebug.state = (scope = "home") => globalThis.OutZenInterop?.dumpState?.(scope);
    globalThis.OutZenDebug.scopes = () => globalThis.OutZenInterop?.listScopes?.();

    globalThis.OutZenDebug ||= {
        isLoaded: () => !!BOOT.module,
        exports: () => Object.keys(BOOT.module || {}),
        lastError: () => BOOT.lastError || null,
        lastLoadedAt: () => BOOT.lastLoadedAt || 0,
        boundAt: () => OZ.__boundAt || 0,
        hasRegisterDotNetRef: () => typeof OZ.registerDotNetRef === "function",
    };

    if (!globalThis.OutZenDebug || typeof globalThis.OutZenDebug !== "object") globalThis.OutZenDebug = {};
    Object.assign(globalThis.OutZenDebug, {
        isLoaded: () => !!BOOT.module,
        exports: () => Object.keys(BOOT.module || {}),
        lastError: () => BOOT.lastError || null,
        hasRegisterDotNetRef: () => typeof OZ.registerDotNetRef === "function",
    });

    // -------------------------------------------------------------------
    // 6) UI helpers (drag + resize) (your functions unchanged)
    // -------------------------------------------------------------------
    function px(n) { return `${Math.round(n)}px`; }

    OutZen.bringToFront ||= (id) => {
        const el = document.getElementById(id);
        if (!el) return false;
        document.querySelectorAll(".oz-drawer.oz-front").forEach(x => x.classList.remove("oz-front"));
        el.classList.add("oz-front");
        return true;
    };

    OutZen.avoidOverlap ||= (id) => {
        const el = document.getElementById(id);
        if (!el) return false;

        const rect = el.getBoundingClientRect();
        const others = [...document.querySelectorAll(".oz-drawer.open")].filter(x => x !== el);

        for (const o of others) {
            const r = o.getBoundingClientRect();
            const overlap =
                rect.left < r.right && rect.right > r.left &&
                rect.top < r.bottom && rect.bottom > r.top;

            if (overlap) {
                const left = (parseFloat(el.style.left || "24") || 24) + 32;
                const top = (parseFloat(el.style.top || "120") || 120) + 32;
                el.style.left = px(Math.min(left, window.innerWidth - 60));
                el.style.top = px(Math.min(top, window.innerHeight - 60));
                break;
            }
        }
        return true;
    };

    OutZen.makeDrawerDraggable ||= (id) => {
        const el = document.getElementById(id);
        if (!el) return false;

        if (el.dataset.ozDragWired === "1") return true;
        el.dataset.ozDragWired = "1";

        const handle = el.querySelector('[data-oz-drag-handle="true"]') || el.querySelector(".oz-titlebar") || el;

        let dragging = false;
        let startX = 0, startY = 0, startLeft = 0, startTop = 0;

        const onMove = (e) => {
            if (!dragging) return;

            const dx = e.clientX - startX;
            const dy = e.clientY - startY;

            let left = startLeft + dx;
            let top = startTop + dy;

            left = Math.max(8, Math.min(left, window.innerWidth - 60));
            top = Math.max(8, Math.min(top, window.innerHeight - 60));

            el.style.left = px(left);
            el.style.top = px(top);

            e.preventDefault();
        };

        const onUp = () => {
            dragging = false;
            el.classList.remove("oz-dragging");
            window.removeEventListener("pointermove", onMove);
        };

        const onDown = (e) => {
            const t = e.target;
            if (t && (t.closest(".oz-titlebar-actions") || t.closest("button"))) return;

            OutZen.bringToFront(id);

            dragging = true;
            el.classList.add("oz-dragging");

            const r = el.getBoundingClientRect();
            startX = e.clientX;
            startY = e.clientY;

            startLeft = parseFloat(el.style.left || r.left) || r.left;
            startTop = parseFloat(el.style.top || r.top) || r.top;

            if (el.classList.contains("dock-right")) {
                el.classList.remove("dock-right");
                el.style.left = px(r.left);
                el.style.top = px(r.top);
            }

            try { handle.setPointerCapture?.(e.pointerId); } catch { }

            e.preventDefault();
            e.stopPropagation();

            window.addEventListener("pointermove", onMove, { passive: false });
            window.addEventListener("pointerup", onUp, { passive: false, once: true });
        };

        handle.style.touchAction = "none";
        handle.addEventListener("pointerdown", onDown, { passive: false });

        return true;
    };

    OutZen.makeDrawerResizable ||= (id, opts = {}) => {
        const el = document.getElementById(id);
        if (!el) return false;

        if (el.dataset.ozResizeWired === "1") return true;

        const handle = el.querySelector('[data-oz-resize="true"]');
        if (!handle) return false;

        el.dataset.ozResizeWired = "1";

        const minW = opts.minW ?? 280;
        const minH = opts.minH ?? 180;
        const maxW = opts.maxW ?? Math.min(900, window.innerWidth - 16);
        const maxH = opts.maxH ?? Math.min(900, window.innerHeight - 16);

        let resizing = false;
        let startX = 0, startY = 0;
        let startW = 0, startH = 0;

        const body = el.querySelector(".oz-body");

        const onMove = (e) => {
            if (!resizing) return;

            try {
                const dx = e.clientX - startX;
                const dy = e.clientY - startY;

                let w = startW + dx;
                let h = startH + dy;

                w = Math.max(minW, Math.min(w, maxW));
                h = Math.max(minH, Math.min(h, maxH));

                el.style.width = `${Math.round(w)}px`;
                el.style.height = `${Math.round(h)}px`;
                el.style.maxHeight = "none";

                const titlebar = el.querySelector(".oz-titlebar");
                const headH = titlebar ? Math.round(titlebar.getBoundingClientRect().height) : 44;

                if (body) body.style.maxHeight = `${Math.max(120, Math.round(h - headH))}px`;

                e.preventDefault();
            } catch (err) {
                resizing = false;
                el.classList.remove("oz-resizing");
                window.removeEventListener("pointermove", onMove);
            }
        };

        const onUp = () => {
            resizing = false;
            el.classList.remove("oz-resizing");
            window.removeEventListener("pointermove", onMove);
        };

        const onDown = (e) => {
            OutZen.bringToFront(id);

            const r = el.getBoundingClientRect();
            resizing = true;
            startX = e.clientX;
            startY = e.clientY;
            startW = r.width;
            startH = r.height;

            el.classList.add("oz-resizing");

            try { handle.setPointerCapture?.(e.pointerId); } catch { }

            e.preventDefault();
            e.stopPropagation();

            window.addEventListener("pointermove", onMove, { passive: false });
            window.addEventListener("pointerup", onUp, { passive: false, once: true });
        };

        handle.style.touchAction = "none";
        handle.addEventListener("pointerdown", onDown, { passive: false });

        logInfo("✅ makeDrawerResizable wired for", id);
        return true;
    };

    // -------------------------------------------------------------------
    // 7) Optional: direct call helper (debug)
    // -------------------------------------------------------------------
    OutZen.call ||= async (fn, ...args) => {
        const m = await OutZen.ensure();
        if (!m || typeof m[fn] !== "function") throw new Error("OutZen export not found: " + fn);
        return m[fn](...args);
    };

    OutZen.scrollIntoViewById ||= (id, opts = {}) => {
        try {
            const el = document.getElementById(id);
            if (!el) return false;

            const behavior = opts.behavior || "smooth";
            const block = opts.block || "start";
            const inline = opts.inline || "nearest";

            el.scrollIntoView({ behavior, block, inline });
            return true;
        } catch {
            return false;
        }
    };

    // -------------------------------------------------------------------
    // 8) Warmup (optional) - keep silent on fail
    // -------------------------------------------------------------------
    try {
        if (typeof OutZen.ensure === "function") OutZen.ensure().catch(() => { });
    } catch { }

    logInfo("✅ interop ready:", {
        build: BUILD,
        ensure: typeof OutZen.ensure,
        hasRegisterDotNetRef: () => typeof OZ.registerDotNetRef === "function",
    });
})();









































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */