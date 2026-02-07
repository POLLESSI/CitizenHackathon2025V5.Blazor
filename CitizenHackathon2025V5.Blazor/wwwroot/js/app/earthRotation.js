/* earthRotation.js — instance-based version + halo pulse at night
   - ES module exports + window wrappers
   - Instances stored in _instances keyed by canvasId
   - makePlanet returns { earthDay, earthNight, atmosphere, glow } (no globals)
   - All listeners/timers stored per-instance and removed on dispose
*/

// Default textures (in case Blazor doesn't apply them)
const DEFAULT_DAY_URL = "/images/earth_texture.jpg?v=1";
const DEFAULT_NIGHT_URL = "/images/earth_texture_night.jpg?v=1";
/*const nowMs = () => (globalThis.performance?.now?.() ?? Date.now());*/
function nowMs() {
    return (globalThis.performance?.now?.() ?? Date.now());
}


const _instances = {}; // key = canvasId

// ----------------------------- Public API (exports module) -----------------------------
export async function initEarth(opts) {
    const canvasId = (opts && opts.canvasId) || "rotatingEarth";
    const key = String(canvasId);

    // Avoid double init on the same canvas
    if (_instances[key]) {
        // console.log("initEarth: instance already exists for", key);
        return;
    }

    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.error(`Canvas #${canvasId} not found`);
        return;
    }

    const state = _createState(canvasId, canvas, opts || {});
    _instances[key] = state;

    try {
        _initThree(state);
        await _loadAndCreatePlanet(state, opts || {});
        _startAnimation(state);
        _setupResize(state);
        _setupControls(state, opts || {});

        // Initial location + periodic day/night update
        const loc = await resolveLocation({
            fallbackLat: opts?.fallbackLat ?? 48.8566,
            fallbackLon: opts?.fallbackLon ?? 2.3522
        });

        applyDayNightNowInstance(state, loc);

        state.dayNightTimerId = setInterval(async () => {
            const l = await resolveLocation({
                fallbackLat: opts?.fallbackLat ?? 48.8566,
                fallbackLon: opts?.fallbackLon ?? 2.3522
            });
            applyDayNightNowInstance(state, l);
        }, opts?.dayNightIntervalMs ?? 10 * 60 * 1000);
        
        console.log(`initEarth: initialized instance ${canvasId}`);
    } catch (err) {
        console.error("initEarth: failed to initialize", err);
        // best-effort cleanup
        disposeEarth(canvasId);
    }
}

export function disposeEarth(canvasId = "rotatingEarth") {
    const key = String(canvasId);
    const state = _instances[key];
    if (!state) {
        // console.log(`disposeEarth: no instance for ${canvasId}`);
        return;
    }

    // cancel animation
    if (state.frameId) {
        cancelAnimationFrame(state.frameId);
        state.frameId = 0;
    }

    // timers
    if (state.dayNightTimerId) {
        clearInterval(state.dayNightTimerId);
        state.dayNightTimerId = null;
    }

    // remove listeners
    state.listeners.forEach(un => {
        try { un(); } catch { }
    });
    state.listeners = [];

    // dispose created meshes/materials/geometries
    const kill = (mesh) => {
        if (!mesh) return;
        try {
            mesh.geometry?.dispose?.();
            if (Array.isArray(mesh.material)) {
                mesh.material.forEach(m => m?.dispose?.());
            } else {
                mesh.material?.dispose?.();
            }
            state.scene?.remove?.(mesh);
        } catch { }
    };

    kill(state.earthDay);
    kill(state.earthNight);
    kill(state.atmosphere);
    if (state.glow) {
        try {
            state.scene?.remove?.(state.glow);
            state.glow.material?.dispose?.();
        } catch { }
    }

    // renderer
    try { state.renderer?.dispose?.(); } catch { }

    // null references
    state.scene = null;
    state.camera = null;
    state.renderer = null;
    state.earthDay = null;
    state.earthNight = null;
    state.atmosphere = null;
    state.glow = null;

    delete _instances[key];
    console.log(`disposeEarth: disposed ${canvasId}`);
}

export function setLightsIntensity(value, canvasId = "rotatingEarth") {
    const s = _instances[String(canvasId)];
    if (!s) return;
    s.lightsIntensity = Number(value) || s.lightsIntensity;
    if (s.earthNight?.material) {
        s.earthNight.material.emissiveIntensity = s.lightsIntensity;
        s.earthNight.material.needsUpdate = true;
    }
}

export function setHaloStrength(value, canvasId = "rotatingEarth") {
    const s = _instances[String(canvasId)];
    if (!s) return;
    s.haloBaseStrength = Number(value) || s.haloBaseStrength;
    s.currentHaloBase = s.haloBaseStrength;
}

export function switchNightMode(on, immediate = false, canvasId = "rotatingEarth") {
    const s = _instances[String(canvasId)];
    if (!s) return;
    setDayNightInstance(s, !!on, immediate);
}

export function setRotationSpeed(value, canvasId = "rotatingEarth") {
    const s = _instances[String(canvasId)];
    if (!s) return;
    s.rotationSpeed = Number(value) || s.rotationSpeed;
}

export function listInstances() {
    return Object.keys(_instances);
}

/**
 * Applies night/day mode to *all* instances.
 */
export function setNightMode(isNight, immediate = true) {
    const flag = !!isNight;
    for (const key of Object.keys(_instances)) {
        setDayNightInstance(_instances[key], flag, immediate);
    }
}

// ----------------------------- Internal helpers -----------------------------
function _createState(canvasId, canvas, opts) {
    const baseHalo = opts.haloStrength ?? 0.7;

    return {
        canvasId,
        canvas,
        scene: null,
        camera: null,
        renderer: null,
        frameId: 0,
        dayNightTimerId: null,
        listeners: [],

        rotationSpeed: opts.rotationSpeed ?? 0.01,

        // Halo & light
        lightsIntensity: opts.lightsIntensity ?? 2.8,
        haloBaseStrength: baseHalo,     // "Normal" halo strength (night baseline)
        currentHaloBase: baseHalo,      // animated day/night base value

        // Pulse params
        haloPulseEnabled: opts.haloPulse ?? true,
        haloPulseAmount: opts.haloPulseAmount ?? 0.30, // 30% around the base
        haloPulseSpeed: opts.haloPulseSpeed ?? 0.8,    // speed (rad/s)
        haloPulsePhase: 0,                             // 👈 IMPORTANT: initial phase
        currentIsNight: null,

        // Three.js objects
        earthDay: null,
        earthNight: null,
        atmosphere: null,
        glow: null,

        // Animation timing
        lastTime: nowMs()
    };
}

function _initThree(state) {
    const canvas = state.canvas;
    const w = canvas.clientWidth || 450;
    const h = canvas.clientHeight || 450;

    state.scene = new THREE.Scene();
    state.camera = new THREE.PerspectiveCamera(45, w / h, 0.1, 1000);
    state.camera.position.z = 3;

    state.renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true });
    if ("outputColorSpace" in state.renderer) {
        state.renderer.outputColorSpace = THREE.SRGBColorSpace;
    } else {
        state.renderer.outputEncoding = THREE.sRGBEncoding;
    }
    state.renderer.setSize(w, h);

    // Basic lighting
    state.scene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const dir = new THREE.DirectionalLight(0xffffff, 0.8);
    dir.position.set(5, 3, 5);
    state.scene.add(dir);
}

async function _loadAndCreatePlanet(state, opts = {}) {
    const loader = new THREE.TextureLoader();
    const dayUrl = opts.dayUrl || DEFAULT_DAY_URL;
    const nightUrl = opts.nightUrl || DEFAULT_NIGHT_URL;

    const [dayTex, nightTex] = await Promise.all([
        loadTex(loader, dayUrl),
        loadTex(loader, nightUrl)
    ]);

    const { earthDay, earthNight, atmosphere, glow } = makePlanet(state, dayTex, nightTex);
    state.earthDay = earthDay;
    state.earthNight = earthNight;
    state.atmosphere = atmosphere;
    state.glow = glow;
}

function _startAnimation(state) {
    function animate() {
        const now = (globalThis.performance?.now?.() ?? Date.now());
        const dt = (now - state.lastTime) / 1000;
        state.lastTime = now;

        state.frameId = requestAnimationFrame(animate);

        // Rotation of spheres
        if (state.earthDay) state.earthDay.rotation.y += state.rotationSpeed;
        if (state.earthNight) state.earthNight.rotation.y += state.rotationSpeed * 1.0002;
        if (state.atmosphere) state.atmosphere.rotation.y += state.rotationSpeed * 0.9998;

        // Pulsating halo only at night
        if (state.atmosphere?.material?.uniforms) {
            let base = state.currentHaloBase ?? state.haloBaseStrength;
            let actualHalo = base;

            if (state.haloPulseEnabled && state.currentIsNight) {
                state.haloPulsePhase += dt * state.haloPulseSpeed;
                const pulse = 1.0 + Math.sin(state.haloPulsePhase) * state.haloPulseAmount;
                actualHalo = base * pulse;
            }

            state.atmosphere.material.uniforms.uStrength.value = actualHalo;
        }

        if (state.renderer && state.scene && state.camera) {
            state.renderer.render(state.scene, state.camera);
        }
    }
    animate();
}

function _setupResize(state) {
    const resizeHandler = () => {
        if (!state.renderer || !state.camera || !state.canvas) return;
        const w = state.canvas.clientWidth || state.canvas.width || 450;
        const h = state.canvas.clientHeight || state.canvas.height || 450;
        state.camera.aspect = w / h;
        state.camera.updateProjectionMatrix();
        state.renderer.setSize(w, h);
    };
    window.addEventListener("resize", resizeHandler);
    state.listeners.push(() => window.removeEventListener("resize", resizeHandler));
}

function _setupControls(state, opts = {}) {
    if (opts.speedControlId) {
        const el = document.getElementById(opts.speedControlId);
        if (el) {
            const cb = (e) => { state.rotationSpeed = parseFloat(e.target.value); };
            el.addEventListener("input", cb);
            state.listeners.push(() => el.removeEventListener("input", cb));
        }
    }

    if (opts.lightsControlId) {
        const el = document.getElementById(opts.lightsControlId);
        if (el) {
            const cb = (e) => { setLightsIntensity(e.target.value, state.canvasId); };
            el.addEventListener("input", cb);
            state.listeners.push(() => el.removeEventListener("input", cb));
        }
    }

    if (opts.haloControlId) {
        const el = document.getElementById(opts.haloControlId);
        if (el) {
            const cb = (e) => { setHaloStrength(e.target.value, state.canvasId); };
            el.addEventListener("input", cb);
            state.listeners.push(() => el.removeEventListener("input", cb));
        }
    }
}

// ----------------------------- makePlanet (returns objects) -----------------------------
function makePlanet(state, dayTex, nightTex) {
    // Dispose prior if any in the state
    const kill = m => {
        if (!m) return;
        try {
            m.geometry?.dispose?.();
            if (Array.isArray(m.material)) {
                m.material.forEach(mt => mt?.dispose?.());
            } else {
                m.material?.dispose?.();
            }
            state.scene?.remove?.(m);
        } catch { }
    };
    kill(state.earthDay);
    kill(state.earthNight);
    kill(state.atmosphere);
    kill(state.glow);

    const geoDay = new THREE.SphereGeometry(1, 64, 64);
    const geoNight = new THREE.SphereGeometry(1, 64, 64);

    const dayMat = new THREE.MeshPhongMaterial({
        map: dayTex,
        shininess: 30,
        specular: new THREE.Color("grey")
    });
    const earthDay = new THREE.Mesh(geoDay, dayMat);
    earthDay.renderOrder = 1;
    state.scene.add(earthDay);

    const nightMat = new THREE.MeshPhongMaterial({
        color: 0x000000,
        emissive: 0xffffff,
        emissiveMap: nightTex,
        emissiveIntensity: state.lightsIntensity,
        transparent: true,
        opacity: 0.0,
        depthWrite: false,
        toneMapped: false
    });
    const earthNight = new THREE.Mesh(geoNight, nightMat);
    earthNight.renderOrder = 2;
    state.scene.add(earthNight);

    // Atmospheric halo shader (BackSide)
    const haloGeo = new THREE.SphereGeometry(1.08, 64, 64);
    const haloMat = new THREE.ShaderMaterial({
        side: THREE.BackSide,
        transparent: true,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
        uniforms: { uStrength: { value: state.currentHaloBase } },
        vertexShader: `
            varying vec3 vNormal;
            varying vec3 vWorldPos;
            void main() {
                vNormal = normalize(normalMatrix * normal);
                vec4 wp = modelMatrix * vec4(position, 1.0);
                vWorldPos = wp.xyz;
                gl_Position = projectionMatrix * viewMatrix * wp;
            }
        `,
        fragmentShader: `
            varying vec3 vNormal;
            varying vec3 vWorldPos;
            uniform float uStrength;
            void main() {
                vec3 V = normalize(cameraPosition - vWorldPos);
                float rim = 1.0 - max(0.0, dot(normalize(vNormal), V));
                rim = pow(rim, 3.0);
                vec3 color = vec3(0.35, 0.75, 1.0);
                float a = rim * uStrength;
                gl_FragColor = vec4(color * a, a);
            }
        `
    });
    const atmosphere = new THREE.Mesh(haloGeo, haloMat);
    atmosphere.renderOrder = 999;
    state.scene.add(atmosphere);

    // Glow sprite
    const glowTex = new THREE.TextureLoader().load("/images/glow-soft.png");
    if ("colorSpace" in glowTex) glowTex.colorSpace = THREE.SRGBColorSpace;
    else glowTex.encoding = THREE.sRGBEncoding;

    const glowMat = new THREE.SpriteMaterial({
        map: glowTex,
        color: 0x66ccff,
        transparent: true,
        blending: THREE.AdditiveBlending,
        depthWrite: false
    });
    const glow = new THREE.Sprite(glowMat);
    glow.scale.set(2.6, 2.6, 1);
    state.scene.add(glow);

    // return objects (no globals)
    return { earthDay, earthNight, atmosphere, glow };
}

// ----------------------------- Instance day/night helpers -----------------------------
function applyDayNightNowInstance(state, loc) {
    const now = new Date();
    const { sunrise, sunset } = sunTimes(now, loc.lat, loc.lon);

    // If the solar calculation fails, we fall back on OutZenIsNight or a fixed window.
    if (!sunrise || !sunset) {
        if (typeof window !== "undefined" && typeof window.OutZenIsNight === "boolean") {
            setDayNightInstance(state, window.OutZenIsNight, true);
        } else {
            const h = now.getHours();
            const night = (h < 7) || (h >= 19);
            setDayNightInstance(state, night, true);
        }
        return;
    }

    const isNight = (now < sunrise || now >= sunset);
    setDayNightInstance(state, isNight, false);
}

function setDayNightInstance(state, night, immediate = false) {
    if (night === state.currentIsNight && !immediate) return;
    state.currentIsNight = night;

    const targetBase = night
        ? state.haloBaseStrength           // night = strong halo
        : state.haloBaseStrength * 0.25;   // daytime = more discreet halo
    if (immediate) {
        state.currentHaloBase = targetBase;
        return;
    }

    const start = performance.now();
    const dur = 900;
    const startBase = state.currentHaloBase;

    const step = (t) => {
        const k = Math.min(1, (t - start) / dur);
        state.currentHaloBase = startBase + (targetBase - startBase) * k;
        if (k < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
}

// ----------------------------- Texture loader helper -----------------------------
function loadTex(loader, url) {
    return new Promise((res, rej) => loader.load(
        url,
        tex => {
            if ("colorSpace" in tex) tex.colorSpace = THREE.SRGBColorSpace;
            else tex.encoding = THREE.sRGBEncoding;
            res(tex);
        },
        undefined,
        rej
    ));
}

// ----------------------------- Geolocation & sun times -----------------------------
function resolveLocation(cfg, state) {
    return new Promise((resolve) => {
        if (!navigator.geolocation) {
            if (state) state.geoDenied = true;
            resolve({ lat: cfg.fallbackLat, lon: cfg.fallbackLon });
            return;
        }
        navigator.geolocation.getCurrentPosition(
            pos => resolve({ lat: pos.coords.latitude, lon: pos.coords.longitude }),
            err => {
                if (state && err && err.code === err.PERMISSION_DENIED) state.geoDenied = true;
                resolve({ lat: cfg.fallbackLat, lon: cfg.fallbackLon });
            },
            { maximumAge: 3600_000, timeout: 6000, enableHighAccuracy: false }
        );
    });
}
function sunTimes(dateLocal, latDeg, lonDeg) {
    const tzOffsetMin = dateLocal.getTimezoneOffset();
    const dateUTC = new Date(dateLocal.getTime() + tzOffsetMin * 60_000);

    const JD = julianDay(dateUTC);
    const T = (JD - 2451545.0) / 36525.0;

    const L0 = fixAngle(280.46646 + T * (36000.76983 + T * 0.0003032));
    const M = fixAngle(357.52911 + T * (35999.05029 - 0.0001537 * T));
    const e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T);

    const Mrad = toRad(M);
    const C =
        (1.914602 - T * (0.004817 + 0.000014 * T)) * Math.sin(Mrad) +
        (0.019993 - 0.000101 * T) * Math.sin(2 * Mrad) +
        0.000289 * Math.sin(3 * Mrad);
    const trueLong = L0 + C;
    const omega = 125.04 - 1934.136 * T;
    const lambda = trueLong - 0.00569 - 0.00478 * Math.sin(toRad(omega));

    const epsilon0 =
        23 +
        (26 + (21.448 - T * (46.815 + T * (0.00059 - 0.001813 * T))) / 60) / 60;
    const epsilon = epsilon0 + 0.00256 * Math.cos(toRad(omega));

    const decl = toDeg(
        Math.asin(
            Math.sin(toRad(epsilon)) * Math.sin(toRad(lambda))
        )
    );

    const y = Math.tan(toRad(epsilon / 2)) ** 2;
    const Etime = 4 * toDeg(
        y * Math.sin(2 * toRad(L0)) -
        2 * e * Math.sin(Mrad) +
        4 * e * y * Math.sin(Mrad) * Math.cos(2 * toRad(L0)) -
        0.5 * y * y * Math.sin(4 * toRad(L0)) -
        1.25 * e * e * Math.sin(2 * Mrad)
    );

    const lat = toRad(latDeg);
    const h0 = toRad(-0.833); // civil twilight
    const cosH =
        (Math.sin(h0) - Math.sin(lat) * Math.sin(toRad(decl))) /
        (Math.cos(lat) * Math.cos(toRad(decl)));

    if (cosH < -1 || cosH > 1) {
        return { sunrise: null, sunset: null };
    }

    const H = toDeg(Math.acos(cosH));
    const solarNoonMinUTC = 720 - 4 * lonDeg - Etime;
    const sunriseMinUTC = solarNoonMinUTC - 4 * H;
    const sunsetMinUTC = solarNoonMinUTC + 4 * H;

    const sunrise = minutesUTCToLocalDate(dateUTCFrom(dateLocal), sunriseMinUTC);
    const sunset = minutesUTCToLocalDate(dateUTCFrom(dateLocal), sunsetMinUTC);
    return { sunrise, sunset };
}

function dateUTCFrom(dateLocal) {
    const tzOffsetMin = dateLocal.getTimezoneOffset();
    return new Date(dateLocal.getTime() + tzOffsetMin * 60_000);
}

function julianDay(dateUTC) {
    const Y = dateUTC.getUTCFullYear();
    const M = dateUTC.getUTCMonth() + 1;
    const D =
        dateUTC.getUTCDate() +
        dateUTC.getUTCHours() / 24 +
        dateUTC.getUTCMinutes() / 1440 +
        dateUTC.getUTCSeconds() / 86400;

    const A = Math.floor((14 - M) / 12);
    const y = Y + 4800 - A;
    const m = M + 12 * A - 3;

    let JDN =
        D +
        Math.floor((153 * m + 2) / 5) +
        365 * y +
        Math.floor(y / 4) -
        Math.floor(y / 100) +
        Math.floor(y / 400) -
        32045;
    const frac =
        (dateUTC.getUTCHours() - 12) / 24 +
        dateUTC.getUTCMinutes() / 1440 +
        dateUTC.getUTCSeconds() / 86400;
    return JDN + frac;
}

function minutesUTCToLocalDate(baseUTC, minutes) {
    const d = new Date(
        Date.UTC(
            baseUTC.getUTCFullYear(),
            baseUTC.getUTCMonth(),
            baseUTC.getUTCDate(),
            0,
            0,
            0
        )
    );
    d.setUTCMinutes(minutes);
    return new Date(d.getTime()); // local
}

const toRad = d => d * Math.PI / 180;
const toDeg = r => r * 180 / Math.PI;
const fixAngle = a => ((a % 360) + 360) % 360;

// ----------------------------- Compatibility wrappers on window -----------------------------
if (typeof window !== "undefined") {
    window.initEarth = (opts) => initEarth(opts || {});
    window.disposeEarth = (canvasId) => disposeEarth(canvasId || "rotatingEarth");
    window.setLightsIntensity = (v, canvasId) => setLightsIntensity(v, canvasId || "rotatingEarth");
    window.setHaloStrength = (v, canvasId) => setHaloStrength(v, canvasId || "rotatingEarth");
    window.switchNightMode = (on, immediate, canvasId) =>
        switchNightMode(on, immediate, canvasId || "rotatingEarth");
    window.setRotationSpeed = (v, canvasId) => setRotationSpeed(v, canvasId || "rotatingEarth");
}

// ----------------------------- End of file -----------------------------


















































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/