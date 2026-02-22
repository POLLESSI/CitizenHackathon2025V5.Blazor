// wwwroot/js/app/oz-draggable.js
(function () {
    "use strict";

    globalThis.OutZen = globalThis.OutZen || {};

    let topZ = 65000;

    // Small helpers
    function clamp(n, min, max) { return Math.max(min, Math.min(max, n)); }
    function isInteractiveTarget(t) {
        // Don't start dragging if the user is clicking inside interactive controls
        return !!(t && t.closest && t.closest("button,a,input,textarea,select,label,[role='button']"));
    }

    OutZen.bringToFront = function (id) {
        const el = document.getElementById(id);
        if (!el) return false;

        // remove oz-front from the others
        document.querySelectorAll(".oz-drawer.oz-front").forEach(x => x.classList.remove("oz-front"));

        el.classList.add("oz-front");
        el.style.zIndex = String(++topZ);
        return true;
    };

    OutZen.saveWindowPos = function (id) {
        const el = document.getElementById(id);
        if (!el) return false;
        const r = el.getBoundingClientRect();
        localStorage.setItem("ozwin:" + id, JSON.stringify({ left: r.left, top: r.top }));
        return true;
    };

    OutZen.restoreWindowPos = function (id) {
        const el = document.getElementById(id);
        if (!el) return false;
        const raw = localStorage.getItem("ozwin:" + id);
        if (!raw) return false;
        try {
            const p = JSON.parse(raw);
            if (typeof p.left === "number") el.style.left = `${Math.round(p.left)}px`;
            if (typeof p.top === "number") el.style.top = `${Math.round(p.top)}px`;
            el.style.right = "auto";
            el.style.bottom = "auto";
            return true;
        } catch { return false; }
    };

    OutZen.avoidOverlap = function (id) {
        const el = document.getElementById(id);
        if (!el) return false;

        const r = el.getBoundingClientRect();
        const others = Array.from(document.querySelectorAll(".oz-drawer.open"))
            .filter(x => x.id && x.id !== id);

        const overlaps = (a, b) =>
            !(a.right < b.left || a.left > b.right || a.bottom < b.top || a.top > b.bottom);

        let rr = r;
        let shift = 0;

        while (others.some(o => overlaps(rr, o.getBoundingClientRect())) && shift < 10) {
            shift++;
            const left = rr.left + 28;
            const top = rr.top + 28;
            el.style.left = `${left}px`;
            el.style.top = `${top}px`;
            el.style.right = "auto";
            el.style.bottom = "auto";
            rr = el.getBoundingClientRect();
        }
        return true;
    };

    /**
     * Make an element draggable by its ID.
     * - Uses handle: [data-oz-drag-handle="true"] if present, else whole element
     * - Uses pointer events (with capture) + mouse fallback
     * - Viewport clamped
     * - Rerender-safe: cleans up previous handlers if called again
     *
     * Returns: boolean (true if wired)
     */
    OutZen.makeDrawerDraggable = function makeDrawerDraggable(drawerId) {
        const root = document.getElementById(drawerId);
        if (!root) {
            console.warn("[oz-draggable] missing drawer:", drawerId);
            return false;
        }

        // Clean previous wiring (Blazor rerender safe)
        if (typeof root.__ozDragCleanup === "function") {
            try { root.__ozDragCleanup(); } catch { /* noop */ }
            root.__ozDragCleanup = null;
        }

        // Ensure the element can move (position fixed is best for drawers)
        const cs = getComputedStyle(root);
        if (cs.position !== "fixed") root.style.position = "fixed";

        // Handle
        const handle = root.querySelector("[data-oz-drag-handle='true']") || root;
        if (!handle) return false;

        // Stamp to ignore stale handlers
        const stamp = String(Date.now());
        root.setAttribute("data-oz-drag-stamp", stamp);

        let dragging = false;
        let startX = 0, startY = 0;
        let startLeft = 0, startTop = 0;

        function start(clientX, clientY) {
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;
            dragging = true;
            root.classList.add("oz-dragging");

            const r = root.getBoundingClientRect();
            startX = clientX;
            startY = clientY;
            startLeft = r.left;
            startTop = r.top;

            // When we move, prefer left/top and neutralize right/bottom
            // (important if you previously docked-right)
            root.style.right = "auto";
            root.style.bottom = "auto";
        }

        function move(clientX, clientY) {
            if (!dragging) return;
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;

            const dx = clientX - startX;
            const dy = clientY - startY;

            const vw = window.innerWidth;
            const vh = window.innerHeight;

            // Use current rendered size; fallback to sensible defaults
            const w = root.offsetWidth || 360;
            const h = root.offsetHeight || 320;

            const newLeft = clamp(startLeft + dx, 6, vw - w - 6);
            const newTop = clamp(startTop + dy, 6, vh - h - 6);

            root.style.left = `${Math.round(newLeft)}px`;
            root.style.top = `${Math.round(newTop)}px`;
        }

        function end() {
            if (!dragging) return;
            dragging = false;
            root.classList.remove("oz-dragging");
        }

        // We use capture phase to beat Leaflet/map handlers underneath
        const capActive = { passive: false, capture: true };
        const capPassive = { passive: true, capture: true };

        // ----- Pointer events (preferred) -----
        function onPointerDown(e) {
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;
            if (e.button != null && e.button !== 0) return; // left click only
            if (isInteractiveTarget(e.target)) return;

            start(e.clientX, e.clientY);

            try { handle.setPointerCapture(e.pointerId); } catch { /* ignore */ }
            e.preventDefault();
            e.stopPropagation();
        }

        function onPointerMove(e) {
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;
            if (!dragging) return;

            move(e.clientX, e.clientY);
            e.preventDefault();
            e.stopPropagation();
        }

        function onPointerUp(e) {
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;
            if (!dragging) return;

            end();
            try { handle.releasePointerCapture(e.pointerId); } catch { /* ignore */ }
            e.preventDefault();
            e.stopPropagation();
        }

        // ----- Mouse fallback -----
        function onMouseDown(e) {
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;
            if (e.button !== 0) return;
            if (isInteractiveTarget(e.target)) return;

            start(e.clientX, e.clientY);
            e.preventDefault();
            e.stopPropagation();
        }

        function onMouseMove(e) {
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;
            if (!dragging) return;
            move(e.clientX, e.clientY);
        }

        function onMouseUp() {
            if (root.getAttribute("data-oz-drag-stamp") !== stamp) return;
            if (!dragging) return;
            end();
        }

        // Bind
        handle.addEventListener("pointerdown", onPointerDown, capActive);
        window.addEventListener("pointermove", onPointerMove, capActive);
        window.addEventListener("pointerup", onPointerUp, capActive);
        window.addEventListener("pointercancel", onPointerUp, capActive);

        handle.addEventListener("mousedown", onMouseDown, capActive);
        window.addEventListener("mousemove", onMouseMove, capPassive);
        window.addEventListener("mouseup", onMouseUp, capPassive);

        // Optional: touch-action to reduce scroll/zoom fighting when dragging
        // (You can also put this in CSS on .oz-titlebar)
        try { handle.style.touchAction = "none"; } catch { /* ignore */ }

        // Cleanup handle (for rerenders / rewire)
        root.__ozDragCleanup = () => {
            handle.removeEventListener("pointerdown", onPointerDown, capActive);
            window.removeEventListener("pointermove", onPointerMove, capActive);
            window.removeEventListener("pointerup", onPointerUp, capActive);
            window.removeEventListener("pointercancel", onPointerUp, capActive);

            handle.removeEventListener("mousedown", onMouseDown, capActive);
            window.removeEventListener("mousemove", onMouseMove, capPassive);
            window.removeEventListener("mouseup", onMouseUp, capPassive);

            root.classList.remove("oz-dragging");
        };

        return true;
    };

})();













































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved. */