// wwwroot/js/app/earthRotation-bridge.js  (classic script)
(() => {
    "use strict";

    globalThis.EarthInterop = globalThis.EarthInterop || {};
    globalThis.__EarthBoot = globalThis.__EarthBoot || { p: null, m: null };

    async function ensure() {
        if (globalThis.__EarthBoot.m) return globalThis.__EarthBoot.m;
        if (!globalThis.__EarthBoot.p) {
            globalThis.__EarthBoot.p = import("/js/app/earthRotation.module.js")
                .then(m => (globalThis.__EarthBoot.m = m, m))
                .catch(err => { globalThis.__EarthBoot.p = null; throw err; });
        }
        return globalThis.__EarthBoot.p;
    }

    globalThis.EarthInterop.initEarth = async (...args) => {
        const m = await ensure();
        return m.initEarth(...args);
    };

    globalThis.EarthInterop.animate = async (...args) => {
        const m = await ensure();
        return m.animate(...args);
    };
})();
