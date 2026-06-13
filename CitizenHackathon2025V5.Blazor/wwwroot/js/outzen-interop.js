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

    const BUILD = globalThis.__ozBuild || "20260603-full-alert-marker-1";

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

        if (panel.dataset.draggable === "true") {
            return true;
        }

        panel.dataset.draggable = "true";

        const lockSize = () => {
            panel.style.removeProperty("width");
            panel.style.removeProperty("min-width");
            panel.style.removeProperty("max-width");
            panel.style.setProperty("height", "auto", "important");
        };

        const setPosition = (left, top) => {
            panel.style.setProperty("left", `${left}px`, "important");
            panel.style.setProperty("top", `${top}px`, "important");
            panel.style.setProperty("right", "auto", "important");
            panel.style.setProperty("bottom", "auto", "important");
            lockSize();
        };

        lockSize();

        panel.addEventListener("dblclick", function () {
            panel.classList.toggle("is-expanded");
        });

        const saved = localStorage.getItem("outzen.alertCluster.position");

        if (saved) {
            try {
                const pos = JSON.parse(saved);

                if (Number.isFinite(pos.left) && Number.isFinite(pos.top)) {
                    setPosition(pos.left, pos.top);
                }
            } catch {
                localStorage.removeItem("outzen.alertCluster.position");
            }
        }

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
            panel.setPointerCapture(e.pointerId);

            e.preventDefault();
        });

        panel.addEventListener("pointermove", function (e) {
            if (!dragging) {
                return;
            }

            const dx = e.clientX - startX;
            const dy = e.clientY - startY;

            const rect = panel.getBoundingClientRect();
            const margin = 8;

            const nextLeft = Math.min(
                Math.max(startLeft + dx, margin),
                window.innerWidth - rect.width - margin
            );

            const nextTop = Math.min(
                Math.max(startTop + dy, margin),
                window.innerHeight - rect.height - margin
            );

            setPosition(nextLeft, nextTop);
        });

        panel.addEventListener("pointerup", function (e) {
            if (!dragging) {
                return;
            }

            dragging = false;
            panel.classList.remove("is-dragging");

            try {
                panel.releasePointerCapture(e.pointerId);
            } catch { }

            const rect = panel.getBoundingClientRect();

            localStorage.setItem("outzen.alertCluster.position", JSON.stringify({
                left: rect.left,
                top: rect.top
            }));
        });

        console.log("[OutZen] alert cluster draggable enabled");

        return true;
    };
    window.OutZenInterop = window.OutZenInterop || {};

})();







































































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */