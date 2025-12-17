// wwwroot/js/navMenuInterop.js
(() => {
    "use strict";
    if (window.NavMenuInterop) return;

    const overlay = () => document.getElementById("ozNavOverlay");

    function setOpen(navEl, open) {
        const ov = overlay();
        if (!navEl || !ov) return;

        ov.classList.toggle("is-open", !!open);
        ov.setAttribute("aria-hidden", open ? "false" : "true");

        // Clean old handler
        ov.onclick = null;
        document.removeEventListener("mousedown", navEl.__ozOutsideHandler, true);

        if (open) {
            // close on overlay click
            ov.onclick = () => {
                // Ask Blazor to click the burger again is messy; easiest: remove class + unlock
                navEl.classList.remove("is-open");
                ov.classList.remove("is-open");
                if (window.OutZen?.setNavLock) window.OutZen.setNavLock(false);
            };

            // close if click outside nav (but NOT inside)
            const handler = (e) => {
                if (!navEl.contains(e.target)) {
                    navEl.classList.remove("is-open");
                    ov.classList.remove("is-open");
                    if (window.OutZen?.setNavLock) window.OutZen.setNavLock(false);
                }
            };
            navEl.__ozOutsideHandler = handler;
            document.addEventListener("mousedown", handler, true);
        }
    }

    window.NavMenuInterop = { setOpen };
})();

