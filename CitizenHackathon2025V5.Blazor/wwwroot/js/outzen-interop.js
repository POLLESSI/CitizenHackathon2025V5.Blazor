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

    const isDev = location.hostname === "localhost" || location.hostname === "127.0.0.1";

    const BUILD = globalThis.__ozBuild || "20260714-map-bundle-fix-1";

    const MODULE_BASE_URL = `/js/app/leafletOutZen.module.js` + `?v=${encodeURIComponent(BUILD)}`;

    let importAttempt = 0;

    function createModuleUrl(cacheBust = false) {
        /*
         * During development, each new attempt
         * receives a different URL.
         *
         * In production, the URL remains stable unless an
         * explicit reload is requested.
         */
        if (!isDev && !cacheBust) {
            return MODULE_BASE_URL;
        }

        importAttempt++;

        return (
            MODULE_BASE_URL +
            `&t=${Date.now()}-${importAttempt}`
        );
    }

    function exposeModuleExports(module) {
        if (!module) {
            return;
        }

        for (const [key, value]
            of Object.entries(module)) {

            globalThis.OutZenInterop[key] =
                value;
        }

        globalThis.OutZenInterop.__esm =
            module;

        globalThis.OutZenInterop.module =
            module;
    }

    async function loadModule(force = false) {
        const mustCreateImport =
            force ||
            !globalThis.__OutZenImportP ||
            !globalThis.__OutZenImportUrl;

        if (mustCreateImport) {
            const importUrl =
                createModuleUrl(
                    force || isDev
                );

            globalThis.__OutZenImportUrl =
                importUrl;

            console.log(
                "[OutZenInterop] loading ESM",
                {
                    force,
                    url: importUrl
                }
            );

            globalThis.__OutZenImportP =
                import(importUrl);
        }

        const importUrl =
            globalThis.__OutZenImportUrl;

        try {
            const module =
                await globalThis.__OutZenImportP;

            exposeModuleExports(module);

            delete globalThis.OutZenInterop.__loadError;

            /*
             * Several callers can wait for the same one
             * import promise. The module should only be
             * logged once per URL.
             */
            if (
                globalThis.__OutZenLastLoggedImportUrl !==
                importUrl
            ) {
                globalThis.__OutZenLastLoggedImportUrl =
                    importUrl;

                console.log(
                    "[OutZenInterop] ESM loaded",
                    {
                        url: importUrl,

                        exportCount:
                            Object.keys(module).length,

                        hasBootOutZen:
                            typeof module.bootOutZen ===
                            "function",

                        hasBundleUpdater:
                            typeof module
                                .addOrUpdateBundleMarkers ===
                            "function"
                    }
                );
            }

            return module;
        }
        catch (error) {
            /*
             * Do not keep a rejected promise.
             * The next call can make a new
             * attempt with a new URL.
             */
            globalThis.__OutZenImportP = null;
            globalThis.__OutZenImportUrl = null;

            globalThis.OutZenInterop.__loadError = {
                name:
                    error?.name ??
                    "UnknownError",

                message:
                    error?.message ??
                    String(error),

                url:
                    importUrl ??
                    null,

                timestamp:
                    new Date().toISOString()
            };

            console.error(
                "[OutZenInterop] " +
                "leafletOutZen.module.js load failed",
                {
                    url: importUrl,
                    name: error?.name,
                    message: error?.message,
                    stack: error?.stack,
                    error
                }
            );

            throw error;
        }
    }

    globalThis.OutZen.ensure =
        async function (force = false) {

            await loadModule(force);

            return true;
        };

    globalThis.OutZen.reload =
        async function () {

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

    globalThis.OutZenReady =
        async function (force = false) {

            try {
                await globalThis.OutZen.ensure(force);

                const requiredFunctions = [
                    "bootOutZen",
                    "disposeOutZen",
                    "addOrUpdateBundleMarkers",
                    "addOrUpdateDetailMarkers",
                    "refreshHybridNow"
                ];

                const missingFunctions =
                    requiredFunctions.filter(
                        function (name) {
                            return (
                                typeof globalThis
                                    .OutZenInterop[name] !==
                                "function"
                            );
                        });

                if (missingFunctions.length > 0) {
                    console.error(
                        "[OutZen] interop incomplete",
                        {
                            missingFunctions,
                            availableExports:
                                Object.keys(
                                    globalThis
                                        .OutZenInterop
                                )
                        }
                    );

                    return false;
                }

                console.log(
                    "[OutZen] ✅ interop ready",
                    {
                        exports:
                            Object.keys(
                                globalThis.OutZenInterop
                            ),

                        moduleUrl:
                            globalThis
                                .__OutZenImportUrl
                    }
                );

                return true;
            }
            catch (error) {
                console.error(
                    "[OutZen] ❌ interop initialization failed",
                    error
                );

                return false;
            }
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

    window.OutZenInterop.setTrafficOverview = function (scopeKey) {
        const state =
            window.OutZen?.getState?.(scopeKey)
            || window[`__OutZenSingleton__${scopeKey}`];

        const map = state?.map;
        if (!map) return;

        map.setView([50.45, 4.75], 8);
    };

    window.OutZenDevice = {

        getOrCreateDeviceId: () => {

            let id = localStorage.getItem("outzen.device.id");

            if (!id) {
                id = crypto.randomUUID();
                localStorage.setItem("outzen.device.id", id);
            }

            return id;
        }
    };

    globalThis.outzenLocation = {
        getCurrentPosition: function () {
            return new Promise(function (resolve, reject) {
                if (!navigator.geolocation) {
                    reject("Geolocation is not supported by this browser.");
                    return;
                }

                navigator.geolocation.getCurrentPosition(
                    function (position) {
                        resolve({
                            latitude: position.coords.latitude,
                            longitude: position.coords.longitude
                        });
                    },
                    function (error) {
                        reject(error.message);
                    },
                    {
                        enableHighAccuracy: true,
                        timeout: 10000,
                        maximumAge: 30000
                    }
                );
            });
        }
    };

    console.log("[outzen-location] registered", !!globalThis.outzenLocation);

    globalThis.OutZenInterop.makeAlertClusterDraggable = function () {
        const panel = document.querySelector(".critical-alert-panel.alert-cluster");

        if (!panel) {
            console.warn("[OutZen] alert cluster not found");
            return false;
        }

        const storageKey = "outzen.alertCluster.position";
        const margin = 10;

        const clamp = (left, top) => {
            const rect = panel.getBoundingClientRect();

            const width = Math.max(rect.width || 210, 210);
            const height = Math.max(rect.height || 210, 210);

            const maxLeft = Math.max(margin, window.innerWidth - width - margin);
            const maxTop = Math.max(margin, window.innerHeight - height - margin);

            return {
                left: Math.min(Math.max(left, margin), maxLeft),
                top: Math.min(Math.max(top, margin), maxTop)
            };
        };

        const setPosition = (left, top, save = false) => {
            const pos = clamp(left, top);

            panel.style.setProperty("position", "fixed", "important");
            panel.style.setProperty("left", `${pos.left}px`, "important");
            panel.style.setProperty("top", `${pos.top}px`, "important");
            panel.style.setProperty("right", "auto", "important");
            panel.style.setProperty("bottom", "auto", "important");

            if (save) {
                localStorage.setItem(storageKey, JSON.stringify(pos));
            }

            return pos;
        };

        const restoreOrDefault = () => {
            const rect = panel.getBoundingClientRect();

            let left = window.innerWidth - rect.width - 30;
            let top = 120;

            const saved = localStorage.getItem(storageKey);

            if (saved) {
                try {
                    const pos = JSON.parse(saved);

                    if (Number.isFinite(pos.left) && Number.isFinite(pos.top)) {
                        left = pos.left;
                        top = pos.top;
                    }
                } catch {
                    localStorage.removeItem(storageKey);
                }
            }

            setPosition(left, top, true);
        };

        restoreOrDefault();

        if (panel.dataset.draggable === "true") {
            return true;
        }

        panel.dataset.draggable = "true";

        panel.addEventListener("dblclick", function (e) {
            if (e.target.closest("button")) {
                return;
            }

            const shouldExpand = !panel.classList.contains("is-expanded");

            panel.classList.toggle("is-expanded", shouldExpand);

            requestAnimationFrame(() => {
                const rect = panel.getBoundingClientRect();
                setPosition(rect.left, rect.top, true);

                console.log("[OutZen] alert cluster expanded =", shouldExpand, {
                    left: rect.left,
                    top: rect.top,
                    width: rect.width,
                    height: rect.height
                });
            });
        });

        let dragging = false;
        let startX = 0;
        let startY = 0;
        let startLeft = 0;
        let startTop = 0;

        panel.addEventListener("pointerdown", function (e) {
            if (e.target.closest("button")) {
                return;
            }

            dragging = true;

            const rect = panel.getBoundingClientRect();

            startX = e.clientX;
            startY = e.clientY;
            startLeft = rect.left;
            startTop = rect.top;

            panel.classList.add("is-dragging");

            try {
                panel.setPointerCapture(e.pointerId);
            } catch { }

            e.preventDefault();
        });

        panel.addEventListener("pointermove", function (e) {
            if (!dragging) {
                return;
            }

            const dx = e.clientX - startX;
            const dy = e.clientY - startY;

            setPosition(startLeft + dx, startTop + dy, false);
        });

        const stopDrag = function (e) {
            if (!dragging) {
                return;
            }

            dragging = false;
            panel.classList.remove("is-dragging");

            try {
                panel.releasePointerCapture(e.pointerId);
            } catch { }

            const rect = panel.getBoundingClientRect();
            setPosition(rect.left, rect.top, true);
        };

        panel.querySelectorAll(".outzen-alert-button").forEach(btn => {
            btn.addEventListener("dblclick", function (e) {
                e.preventDefault();
                e.stopPropagation();
            }, true);
        });

        panel.addEventListener("pointerup", stopDrag);
        panel.addEventListener("pointercancel", stopDrag);

        window.addEventListener("resize", function () {
            const rect = panel.getBoundingClientRect();
            setPosition(rect.left, rect.top, true);
        });

        console.log("[OutZen] alert cluster draggable + double-click enabled");

        return true;
    };

    window.OutZen.__drawerZ = window.OutZen.__drawerZ || 30000;

    window.OutZen.bringToFront = function (id) {
        const drawer = document.getElementById(id);
        if (!drawer) return false;

        window.OutZen.__drawerZ += 1;
        drawer.style.setProperty("z-index", String(window.OutZen.__drawerZ), "important");

        return true;
    };

    window.OutZen.makeDrawerDraggable = function (id) {
        const drawer = document.getElementById(id);

        if (!drawer) {
            console.warn("[OutZen] drawer not found:", id);
            return false;
        }

        const handle =
            drawer.querySelector("[data-oz-drag-handle='true']")
            || drawer.querySelector(".oz-titlebar");

        if (!handle) {
            console.warn("[OutZen] drawer drag handle not found:", id);
            return false;
        }

        const storageKey = `outzen.drawer.${id}.position`;
        const margin = 8;

        const clamp = function (left, top) {
            const rect = drawer.getBoundingClientRect();

            const width = Math.max(rect.width || 320, 320);
            const height = Math.max(rect.height || 160, 160);

            const maxLeft = Math.max(margin, window.innerWidth - width - margin);
            const maxTop = Math.max(margin, window.innerHeight - height - margin);

            return {
                left: Math.min(Math.max(left, margin), maxLeft),
                top: Math.min(Math.max(top, margin), maxTop)
            };
        };

        const setPosition = function (left, top, save) {
            const pos = clamp(left, top);

            drawer.style.setProperty("position", "fixed", "important");
            drawer.style.setProperty("left", `${pos.left}px`, "important");
            drawer.style.setProperty("top", `${pos.top}px`, "important");
            drawer.style.setProperty("right", "auto", "important");
            drawer.style.setProperty("bottom", "auto", "important");

            drawer.classList.remove("dock-right");

            if (save) {
                localStorage.setItem(storageKey, JSON.stringify(pos));
            }

            return pos;
        };

        const restorePosition = function () {
            const saved = localStorage.getItem(storageKey);

            if (saved) {
                try {
                    const pos = JSON.parse(saved);

                    if (Number.isFinite(pos.left) && Number.isFinite(pos.top)) {
                        setPosition(pos.left, pos.top, true);
                        return;
                    }
                } catch {
                    localStorage.removeItem(storageKey);
                }
            }

            const rect = drawer.getBoundingClientRect();

            const startLeft = Number.isFinite(rect.left) && rect.left > 0 ? rect.left : 24;
            const startTop = Number.isFinite(rect.top) && rect.top > 0 ? rect.top : 120;

            setPosition(startLeft, startTop, false);
        };

        restorePosition();

        if (drawer.dataset.ozDragWired === "true") {
            return true;
        }

        drawer.dataset.ozDragWired = "true";

        let dragging = false;
        let startX = 0;
        let startY = 0;
        let startLeft = 0;
        let startTop = 0;

        const onPointerMove = function (e) {
            if (!dragging) {
                return;
            }

            const dx = e.clientX - startX;
            const dy = e.clientY - startY;

            setPosition(startLeft + dx, startTop + dy, false);
        };

        const onPointerUp = function () {
            if (!dragging) {
                return;
            }

            dragging = false;
            drawer.classList.remove("oz-dragging");

            const rect = drawer.getBoundingClientRect();
            setPosition(rect.left, rect.top, true);

            document.removeEventListener("pointermove", onPointerMove, true);
            document.removeEventListener("pointerup", onPointerUp, true);
            document.removeEventListener("pointercancel", onPointerUp, true);

            console.log("[OutZen] drawer saved", id, localStorage.getItem(storageKey));
        };

        handle.addEventListener("pointerdown", function (e) {
            if (e.target.closest("button, input, textarea, select, a")) {
                return;
            }

            window.OutZen.bringToFront(id);

            const rect = drawer.getBoundingClientRect();

            dragging = true;
            startX = e.clientX;
            startY = e.clientY;
            startLeft = rect.left;
            startTop = rect.top;

            drawer.classList.add("oz-dragging");

            document.addEventListener("pointermove", onPointerMove, true);
            document.addEventListener("pointerup", onPointerUp, true);
            document.addEventListener("pointercancel", onPointerUp, true);

            e.preventDefault();
        });

        window.addEventListener("resize", function () {
            const rect = drawer.getBoundingClientRect();
            setPosition(rect.left, rect.top, true);
        });

        console.log("[OutZen] drawer draggable wired:", id);

        return true;
    };

    window.OutZen.makeDrawerResizable = function (id) {
        const drawer = document.getElementById(id);

        if (!drawer) {
            console.warn("[OutZen] drawer not found for resize:", id);
            return false;
        }

        const handle =
            drawer.querySelector("[data-oz-resize='true']")
            || drawer.querySelector(".oz-resize-handle");

        if (!handle) {
            console.warn("[OutZen] resize handle not found:", id);
            return false;
        }

        const storageKey = `outzen.drawer.${id}.size`;

        const saved = localStorage.getItem(storageKey);

        if (saved) {
            try {
                const size = JSON.parse(saved);

                if (Number.isFinite(size.width)) {
                    drawer.style.setProperty("width", `${size.width}px`, "important");
                }

                if (Number.isFinite(size.height)) {
                    drawer.style.setProperty("height", `${size.height}px`, "important");
                }
            } catch {
                localStorage.removeItem(storageKey);
            }
        }

        if (drawer.dataset.ozResizeWired === "true") {
            return true;
        }

        drawer.dataset.ozResizeWired = "true";

        let resizing = false;
        let startX = 0;
        let startY = 0;
        let startWidth = 0;
        let startHeight = 0;

        const onPointerMove = function (e) {
            if (!resizing) {
                return;
            }

            const nextWidth = Math.max(320, startWidth + (e.clientX - startX));
            const nextHeight = Math.max(180, startHeight + (e.clientY - startY));

            drawer.style.setProperty("width", `${nextWidth}px`, "important");
            drawer.style.setProperty("height", `${nextHeight}px`, "important");
        };

        const onPointerUp = function () {
            if (!resizing) {
                return;
            }

            resizing = false;
            drawer.classList.remove("oz-resizing");

            const rect = drawer.getBoundingClientRect();

            localStorage.setItem(storageKey, JSON.stringify({
                width: rect.width,
                height: rect.height
            }));

            document.removeEventListener("pointermove", onPointerMove, true);
            document.removeEventListener("pointerup", onPointerUp, true);
            document.removeEventListener("pointercancel", onPointerUp, true);

            console.log("[OutZen] drawer size saved", id, localStorage.getItem(storageKey));
        };

        handle.addEventListener("pointerdown", function (e) {
            window.OutZen.bringToFront(id);

            const rect = drawer.getBoundingClientRect();

            resizing = true;
            startX = e.clientX;
            startY = e.clientY;
            startWidth = rect.width;
            startHeight = rect.height;

            drawer.classList.add("oz-resizing");

            document.addEventListener("pointermove", onPointerMove, true);
            document.addEventListener("pointerup", onPointerUp, true);
            document.addEventListener("pointercancel", onPointerUp, true);

            e.preventDefault();
        });

        console.log("[OutZen] drawer resizable wired:", id);

        return true;
    };
})();







































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */