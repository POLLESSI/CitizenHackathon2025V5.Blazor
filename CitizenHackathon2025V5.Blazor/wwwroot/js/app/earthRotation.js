/* wwwroot/js/app/earthRotation.js
   3D Earth (Three.js already loaded from index.html) + day/night textures
   Simple day/night choice based on local hour.
*/
let scene, camera, renderer;
let earth = null;                
let rotationSpeed = 0.01;
let frameId = 0;

export function initEarth(opts) {
    const canvas = document.getElementById("rotatingEarth");
    if (!canvas) {
        console.error("Canvas #rotatingEarth not found");
        return;
    }

    // Customizable URLs
    const dayUrl = (opts && opts.dayUrl) || "/images/earth_texture.jpg?v=1";
    const nightUrl = (opts && opts.nightUrl) || "/images/earth_texture_night.jpg?v=1";

    // Day = 06:00–18:00
    const hour = new Date().getHours();
    const isNight = (hour < 6 || hour >= 18);
    const textureUrl = isNight ? nightUrl : dayUrl;

    // Cleans up any previous scene
    disposeEarth();

    scene = new THREE.Scene();
    camera = new THREE.PerspectiveCamera(45, canvas.clientWidth / canvas.clientHeight, 0.1, 1000);
    camera.position.z = 3;

    renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true });
    renderer.setSize(canvas.clientWidth, canvas.clientHeight);

    scene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const dir = new THREE.DirectionalLight(0xffffff, 0.8);
    dir.position.set(5, 3, 5);
    scene.add(dir);

    const loader = new THREE.TextureLoader();

    loader.load(
        textureUrl,
        (tex) => makeEarth(tex),
        undefined,
        () => {
            const fallback = isNight ? dayUrl : nightUrl;
            console.warn(`Texture load failed: ${textureUrl}, fallback to ${fallback}`);
            loader.load(fallback, (tex2) => makeEarth(tex2));
        }
    );

    const speedControl = document.getElementById("speedRange");
    if (speedControl) {
        speedControl.addEventListener("input", (e) => {
            const v = parseFloat(e.target.value);
            if (Number.isFinite(v)) rotationSpeed = v;
        });
    }

    window.addEventListener('resize', onWindowResize);

    function makeEarth(texture) {
        // Destroys the previous mesh if it exists
        if (earth) {
            try {
                earth.geometry.dispose();
                earth.material.dispose();
                scene.remove(earth);
            } catch { /* ignore */ }
        }

        const geometry = new THREE.SphereGeometry(1, 64, 64);
        const material = new THREE.MeshPhongMaterial({
            map: texture,
            shininess: 30,
            specular: new THREE.Color('grey')
        });

        earth = new THREE.Mesh(geometry, material);
        scene.add(earth);
        animate();
    }
}

function animate() {
    frameId = requestAnimationFrame(animate);
    if (earth) {
        earth.rotation.y += rotationSpeed;
    }
    if (renderer && scene && camera) {
        renderer.render(scene, camera);
    }
}

function onWindowResize() {
    const canvas = document.getElementById("rotatingEarth");
    if (!canvas || !camera || !renderer) return;
    const w = canvas.clientWidth || canvas.width || 450;
    const h = canvas.clientHeight || canvas.height || 450;
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    renderer.setSize(w, h);
}

export function disposeEarth() {
    if (frameId) {
        cancelAnimationFrame(frameId);
        frameId = 0;
    }
    if (earth) {
        try {
            earth.geometry.dispose();
            earth.material.dispose();
            scene && scene.remove(earth);
        } catch { /* ignore */ }
        earth = null;
    }
    if (renderer) {
        try { renderer.dispose(); } catch { /* ignore */ }
        renderer = null;
    }
    scene = null;
    camera = null;
}

// global ignoreExpose for Blazor (if you do JS.InvokeVoidAsync("initEarth", ...))
window.initEarth = initEarth;
window.disposeEarth = disposeEarth;

/* ============================================================
   Day/Night based on the sun – local calculation (NOAA algorithm)
   Consumer reference: "NOAA Solar Calculator" (usual formulas)
   Returns sunrise/sunset in local date, or null if polar.
============================================================ */
function applyDayNightNow(loc) {
    const { sunrise, sunset } = sunTimes(new Date(), loc.lat, loc.lon);
    const now = new Date();

    // Polar cases / failures: default day 07:00–19:00
    if (!sunrise || !sunset) {
        const h = now.getHours();
        const night = (h < 7) || (h >= 19);
        setDayNight(night, true);
        return;
    }

    const night = (now < sunrise) || (now >= sunset);
    setDayNight(night, false);
}

function setDayNight(night, immediate) {
    if (night === currentIsNight && !immediate) return;
    currentIsNight = night;
    if (!earthDay || !earthNight) return;

    if (immediate) {
        earthNight.material.opacity = night ? 1 : 0;
        earthDay.material.opacity = night ? 0 : 1;
        return;
    }
    // fade 1.2s
    fadeTo(night, 1200);
}

function fadeTo(night, durationMs) {
    if (fading) return;
    fading = true;
    const start = performance.now();
    const from = earthNight.material.opacity;
    const to = night ? 1 : 0;

    const step = (t) => {
        const k = Math.min(1, (t - start) / durationMs);
        const v = from + (to - from) * k;
        earthNight.material.opacity = v;
        earthDay.material.opacity = 1 - v;
        if (k < 1) requestAnimationFrame(step);
        else fading = false;
    };
    requestAnimationFrame(step);
}

/* ---------- location (without dependency) ---------- */
function resolveLocation(cfg) {
    return new Promise((resolve) => {
        if (!navigator.geolocation) {
            resolve({ lat: cfg.fallbackLat, lon: cfg.fallbackLon });
            return;
        }
        navigator.geolocation.getCurrentPosition(
            pos => resolve({ lat: pos.coords.latitude, lon: pos.coords.longitude }),
            () => resolve({ lat: cfg.fallbackLat, lon: cfg.fallbackLon }),
            { maximumAge: 3600_000, timeout: 6000, enableHighAccuracy: false }
        );
    });
}

/* ---------- Sun calculations (simplified NOAA implementation) ---------- */
function sunTimes(dateLocal, latDeg, lonDeg) {
    // Converts local date → UTC for calculations
    const tzOffsetMin = dateLocal.getTimezoneOffset(); // minutes
    const dateUTC = new Date(dateLocal.getTime() + tzOffsetMin * 60_000);

    const JD = julianDay(dateUTC);
    const T = (JD - 2451545.0) / 36525.0;

    // Average position of the sun
    const L0 = fixAngle(280.46646 + T * (36000.76983 + T * 0.0003032)); // deg
    const M = fixAngle(357.52911 + T * (35999.05029 - 0.0001537 * T)); // deg
    const e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T);

    const Mrad = toRad(M);
    const C = (1.914602 - T * (0.004817 + 0.000014 * T)) * Math.sin(Mrad)
        + (0.019993 - 0.000101 * T) * Math.sin(2 * Mrad)
        + 0.000289 * Math.sin(3 * Mrad);
    const trueLong = L0 + C; // deg
    const omega = 125.04 - 1934.136 * T;
    const lambda = trueLong - 0.00569 - 0.00478 * Math.sin(toRad(omega)); // apparent longitude

    const epsilon0 = 23 + (26 + (21.448 - T * (46.815 + T * (0.00059 - 0.001813 * T))) / 60) / 60;
    const epsilon = epsilon0 + 0.00256 * Math.cos(toRad(omega)); // apparent obliquity

    const decl = toDeg(Math.asin(Math.sin(toRad(epsilon)) * Math.sin(toRad(lambda)))); // δ

    // Equation of Time (minutes)
    const y = Math.tan(toRad(epsilon / 2)) ** 2;
    const Etime = 4 * toDeg(
        y * Math.sin(2 * toRad(L0)) -
        2 * e * Math.sin(Mrad) +
        4 * e * y * Math.sin(Mrad) * Math.cos(2 * toRad(L0)) -
        0.5 * y * y * Math.sin(4 * toRad(L0)) -
        1.25 * e * e * Math.sin(2 * Mrad)
    );

    // Hour angle for -0.833° (apparent sun at sunrise/sunset)
    const lat = toRad(latDeg);
    const h0 = toRad(-0.833);
    const cosH = (Math.sin(h0) - Math.sin(lat) * Math.sin(toRad(decl))) / (Math.cos(lat) * Math.cos(toRad(decl)));

    if (cosH < -1 || cosH > 1) {
        // Sun always above or below the horizon (poles, etc.)
        return { sunrise: null, sunset: null };
    }

    const H = toDeg(Math.acos(cosH)); // degrees

    // True solar time → minutes since midnight UTC
    const solarNoonMinUTC = 720 - 4 * lonDeg - Etime;
    const sunriseMinUTC = solarNoonMinUTC - 4 * H;
    const sunsetMinUTC = solarNoonMinUTC + 4 * H;

    // Convert UTC minutes → Local date
    const sunrise = minutesUTCToLocalDate(dateUTC, sunriseMinUTC);
    const sunset = minutesUTCToLocalDate(dateUTC, sunsetMinUTC);
    return { sunrise, sunset };
}

function julianDay(dateUTC) {
    // algorithm for 0h UTC of the current day + fraction
    const Y = dateUTC.getUTCFullYear();
    const M = dateUTC.getUTCMonth() + 1;
    const D = dateUTC.getUTCDate()
        + dateUTC.getUTCHours() / 24
        + dateUTC.getUTCMinutes() / (24 * 60)
        + dateUTC.getUTCSeconds() / (24 * 3600);

    const A = Math.floor((14 - M) / 12);
    const y = Y + 4800 - A;
    const m = M + 12 * A - 3;

    let JDN = D + Math.floor((153 * m + 2) / 5) + 365 * y + Math.floor(y / 4) - Math.floor(y / 100) + Math.floor(y / 400) - 32045;
    const frac = (dateUTC.getUTCHours() - 12) / 24 + dateUTC.getUTCMinutes() / 1440 + dateUTC.getUTCSeconds() / 86400;
    return JDN + frac;
}

function minutesUTCToLocalDate(baseUTC, minutes) {
    const d = new Date(Date.UTC(baseUTC.getUTCFullYear(), baseUTC.getUTCMonth(), baseUTC.getUTCDate(), 0, 0, 0));
    d.setUTCMinutes(minutes);
    // Automatically reset to local by the Date object
    return new Date(d.getTime());
}

const toRad = d => d * Math.PI / 180;
const toDeg = r => r * 180 / Math.PI;
const fixAngle = a => ((a % 360) + 360) % 360;













































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/