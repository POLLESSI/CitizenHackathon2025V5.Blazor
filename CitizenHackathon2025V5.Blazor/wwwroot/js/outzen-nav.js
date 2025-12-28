// wwwroot/js/outzen-nav.js
(() => {
    "use strict";

    window.OutZen = window.OutZen || {};
    window.OutZen.nav = window.OutZen.nav || {};

    let wired = false;
    let dotnet = null;
    let navSel = "nav.main-nav";
    let drawerSel = ".nav-drawer";

    function onDocPointerDown(e) {
        if (!dotnet) return;

        const nav = document.querySelector(navSel);
        const drawer = document.querySelector(drawerSel);
        if (!nav || !drawer) return;

        const t = e.target;
        if (!(t instanceof Element)) return;

        // click outside nav and drawer => close
        if (!drawer.contains(t) && !nav.contains(t)) {
            dotnet.invokeMethodAsync("CloseFromJs");
        }
    }

    function onKeyDown(e) {
        if (e.key === "Escape") dotnet?.invokeMethodAsync("CloseFromJs");
    }

    window.OutZen.setNavLock = function (locked) {
        document.documentElement.classList.toggle("nav-lock", !!locked);
        document.body.classList.toggle("nav-lock", !!locked);
    };

    window.OutZen.nav.init = function (dotnetRef, navSelector, drawerSelector) {
        dotnet = dotnetRef;
        navSel = navSelector || navSel;
        drawerSel = drawerSelector || drawerSel;

        if (wired) return;
        wired = true;

        document.addEventListener("pointerdown", onDocPointerDown, true);
        document.addEventListener("keydown", onKeyDown, true);
    };

    window.OutZen.nav.setOpen = function (navEl, isOpen) {
        if (!navEl) return;
        navEl.classList.toggle("is-open", !!isOpen);

        // optional: map invalidate hook
        window.dispatchEvent(new CustomEvent("outzen:nav", { detail: { open: !!isOpen } }));
    };
})();














































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.