/* wwwroot/js/app/earthRotation.js
   3D Globe (Three.js loaded via index.html) + day/night textures
   - Additive atmospheric halo
   - Day/night detection (NOAA) with fade
   - Night light intensity control
*/

// --- Overall state ---
let scene, camera, renderer, frameId = 0;
let earthDay = null, earthNight = null, atmosphere = null;
let rotationSpeed = 0.01;

let lightsIntensity = 2.8;   // 1.6 → 2.8 recommended
let haloStrength = 0.7;    // 0–1
let currentIsNight = null;   // avoid unnecessary blandness
let dayNightTimerId = null;  // for clearInterval

// -----------------------------------------------------------
// API principale
// -----------------------------------------------------------
export function initEarth(opts) {
    const canvasId = (opts && opts.canvasId) || "rotatingEarth";
    const canvas = document.getElementById(canvasId);
    if (!canvas) { console.error(`Canvas #${canvasId} introuvable`); return; }

    const dayUrl = (opts && opts.dayUrl) || "/images/earth_texture.jpg?v=1";
    const nightUrl = (opts && opts.nightUrl) || "/images/earth_texture_night.jpg?v=1";

    disposeEarth(); // cleaning of a possible old globe

    scene = new THREE.Scene();
    camera = new THREE.PerspectiveCamera(45, canvas.clientWidth / canvas.clientHeight, 0.1, 1000);
    camera.position.z = 3;

    renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true });
    renderer.outputEncoding = THREE.sRGBEncoding;
    renderer.setSize(canvas.clientWidth, canvas.clientHeight);

    // Basic lighting
    scene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const dir = new THREE.DirectionalLight(0xffffff, 0.8);
    dir.position.set(5, 3, 5);
    scene.add(dir);

    // Textures
    const loader = new THREE.TextureLoader();
    Promise.all([loadTex(loader, dayUrl), loadTex(loader, nightUrl)])
        .then(([dayTex, nightTex]) => {
            makePlanet(dayTex, nightTex);
            animate();

            // Speed ​​slider (optional)
            const speedControl = document.getElementById("speedRange");
            if (speedControl) {
                speedControl.addEventListener("input", (e) => {
                    const v = parseFloat(e.target.value);
                    if (Number.isFinite(v)) rotationSpeed = v;
                });
            }

            // 1st simple setting (6 p.m.–6 a.m. = night) to avoid flash
            const h = new Date().getHours();
            setDayNight(h < 6 || h >= 18, true);
        })
        .catch(err => console.warn("⚠️ Échec de chargement texture :", err));

    // Application with geolocation + regular updates
    resolveLocation({ fallbackLat: 48.8566, fallbackLon: 2.3522 }).then(applyDayNightNow);

    if (dayNightTimerId) clearInterval(dayNightTimerId);
    dayNightTimerId = setInterval(() => {
        resolveLocation({ fallbackLat: 48.8566, fallbackLon: 2.3522 }).then(applyDayNightNow);
    }, 10 * 60 * 1000);

    window.addEventListener('resize', onWindowResize);
}

export function disposeEarth() {
    if (frameId) { cancelAnimationFrame(frameId); frameId = 0; }

    // remove listeners/timers
    window.removeEventListener('resize', onWindowResize);
    if (dayNightTimerId) { clearInterval(dayNightTimerId); dayNightTimerId = null; }

    // destroys meshes cleanly
    const kill = (mesh) => {
        if (!mesh) return;
        try {
            mesh.geometry && mesh.geometry.dispose();
            mesh.material && mesh.material.dispose();
            scene && scene.remove(mesh);
        } catch { }
    };
    kill(earthDay); earthDay = null;
    kill(earthNight); earthNight = null;
    kill(atmosphere); atmosphere = null;

    if (renderer) { try { renderer.dispose(); } catch { } renderer = null; }
    scene = null; camera = null;
}

export function setLightsIntensity(value) {
    lightsIntensity = Number(value) || lightsIntensity;
    if (earthNight && earthNight.material) {
        earthNight.material.emissiveIntensity = lightsIntensity;
        earthNight.material.needsUpdate = true;   // <—
    }
}

export function switchNightMode(on, immediate = false) {
    setDayNight(!!on, immediate);
}

export function setHaloStrength(value) {
    haloStrength = Number(value) || haloStrength;
    if (atmosphere && atmosphere.material && atmosphere.material.uniforms) {
        atmosphere.material.uniforms.uStrength.value = haloStrength;
        atmosphere.material.needsUpdate = true;   // <—
    }
}

// Exhibition for Blazor / console
window.initEarth = initEarth;
window.disposeEarth = disposeEarth;
window.setLightsIntensity = setLightsIntensity;
window.switchNightMode = switchNightMode;
window.setHaloStrength = setHaloStrength; // exhibited at the DOM
window.setRotationSpeed = (v) => { rotationSpeed = Number(v) || rotationSpeed; };
window.setLightsIntensity = v => { setLightsIntensity(v); };
window.setHaloStrength = v => { setHaloStrength(v); };
window.switchNightMode = (on, immediate) => { switchNightMode(on, immediate); };

// Default value a little more “punchy”
setLightsIntensity(2.4);

// -----------------------------------------------------------
// Construction of the globe
// -----------------------------------------------------------
function loadTex(loader, url) {
    return new Promise((res, rej) => loader.load(url, res, undefined, rej));
    dayTex.encoding = THREE.sRGBEncoding;
    nightTex.encoding = THREE.sRGBEncoding;
}

function makePlanet(dayTex, nightTex) {
    // Pre-cleaning if re-init
    if (earthDay) { earthDay.geometry.dispose(); earthDay.material.dispose(); scene.remove(earthDay); }
    if (earthNight) { earthNight.geometry.dispose(); earthNight.material.dispose(); scene.remove(earthNight); }
    if (atmosphere) { atmosphere.geometry.dispose(); atmosphere.material.dispose(); scene.remove(atmosphere); }

    // Distinct geometries (avoids double .dispose on the same instance)
    const geoDay = new THREE.SphereGeometry(1, 64, 64);
    const geoNight = new THREE.SphereGeometry(1, 64, 64);
    const geoHalo = new THREE.SphereGeometry(1.03, 64, 64);

    // "Day" sphere
    const dayMat = new THREE.MeshPhongMaterial({
        map: dayTex,
        shininess: 30,
        specular: new THREE.Color('grey')
    });
    earthDay = new THREE.Mesh(geoDay, dayMat);
    earthDay.renderOrder = 1;
    scene.add(earthDay);

    // "Night" sphere (emissive = luminous cities)
    const nightMat = new THREE.MeshPhongMaterial({
        color: 0x000000,
        emissive: 0xffffff,             // white to amplify the emissiveMap
        emissiveMap: nightTex,
        emissiveIntensity: lightsIntensity,
        transparent: true,
        opacity: 0.0,
        depthWrite: false,              // avoid crushing the halo
        toneMapped: false       aged by setDayNight
    });
    earthNight = new THREE.Mesh(geoNight, nightMat);
    earthNight.renderOrder = 2;
    scene.add(earthNight);

    // Additive Atmospheric Halo (BackSide)
    const haloGeo = new THREE.SphereGeometry(1.08, 64, 64);
    const haloMat = new THREE.ShaderMaterial({
        side: THREE.BackSide,
        transparent: true,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
        uniforms: { uStrength: { value: haloStrength } },
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
              // un bleu plus punchy
              vec3 color = vec3(0.35, 0.75, 1.0);
              float a = rim * uStrength;
              gl_FragColor = vec4(color * a, a);
            }
          `
    });
    atmosphere = new THREE.Mesh(geoHalo, haloMat);
    atmosphere.renderOrder = 999;
    scene.add(atmosphere);

    const glowTex = new THREE.TextureLoader().load('/images/glow-soft.png'); // make a small white radial PNG
    glowTex.encoding = THREE.sRGBEncoding;
    const glowMat = new THREE.SpriteMaterial({ map: glowTex, color: 0x66ccff, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false });
    const glow = new THREE.Sprite(glowMat);
    glow.scale.set(2.6, 2.6, 1);
    scene.add(glow);
}

// -----------------------------------------------------------
// Animation + responsive
// -----------------------------------------------------------
function animate() {
    frameId = requestAnimationFrame(animate);
    if (earthDay) earthDay.rotation.y += rotationSpeed;
    if (earthNight) earthNight.rotation.y += rotationSpeed * 1.0002;
    if (atmosphere) atmosphere.rotation.y += rotationSpeed * 0.9998;
    if (renderer && scene && camera) renderer.render(scene, camera);
}

setDayNight(true, true);
function onWindowResize() {
    const canvas = document.getElementById("rotatingEarth");
    if (!canvas || !camera || !renderer) return;
    const w = canvas.clientWidth || canvas.width || 450;
    const h = canvas.clientHeight || canvas.height || 450;
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    renderer.setSize(w, h);
}

// -----------------------------------------------------------
// Day/Night Management (+ halo)
// -----------------------------------------------------------
function setDayNight(night, immediate = false) {
    if (night === currentIsNight && !immediate) return;
    currentIsNight = night;
    if (!earthNight || !earthDay || !atmosphere) return;

    const targetNightOpacity = night ? 1.0 : 0.0;                  // lights visible at night
    const targetHalo = night ? haloStrength : haloStrength * 0.25; // softer halo during the day

    if (immediate) {
        earthNight.material.opacity = targetNightOpacity;
        atmosphere.material.uniforms.uStrength.value = targetHalo;
        return;
    }

    const start = performance.now();
    const dur = 900;
    const startOp = earthNight.material.opacity;
    const startHalo = atmosphere.material.uniforms.uStrength.value;

    const step = (t) => {
        const k = Math.min(1, (t - start) / dur);
        earthNight.material.opacity = startOp + (targetNightOpacity - startOp) * k;
        atmosphere.material.uniforms.uStrength.value = startHalo + (targetHalo - startHalo) * k;
        if (k < 1) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
}

// -----------------------------------------------------------
// Sun calculations (NOAA simplified) + geolocation
// -----------------------------------------------------------
function applyDayNightNow(loc) {
    const { sunrise, sunset } = sunTimes(new Date(), loc.lat, loc.lon);
    const now = new Date();

    if (!sunrise || !sunset) {
        const h = now.getHours();
        const night = (h < 7) || (h >= 19);
        setDayNight(night, true);
        return;
    }
    setDayNight(now < sunrise || now >= sunset);
}

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

function sunTimes(dateLocal, latDeg, lonDeg) {
    const tzOffsetMin = dateLocal.getTimezoneOffset();
    const dateUTC = new Date(dateLocal.getTime() + tzOffsetMin * 60_000);

    const JD = julianDay(dateUTC);
    const T = (JD - 2451545.0) / 36525.0;

    const L0 = fixAngle(280.46646 + T * (36000.76983 + T * 0.0003032));
    const M = fixAngle(357.52911 + T * (35999.05029 - 0.0001537 * T));
    const e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T);

    const Mrad = toRad(M);
    const C = (1.914602 - T * (0.004817 + 0.000014 * T)) * Math.sin(Mrad)
        + (0.019993 - 0.000101 * T) * Math.sin(2 * Mrad)
        + 0.000289 * Math.sin(3 * Mrad);
    const trueLong = L0 + C;
    const omega = 125.04 - 1934.136 * T;
    const lambda = trueLong - 0.00569 - 0.00478 * Math.sin(toRad(omega));

    const epsilon0 = 23 + (26 + (21.448 - T * (46.815 + T * (0.00059 - 0.001813 * T))) / 60) / 60;
    const epsilon = epsilon0 + 0.00256 * Math.cos(toRad(omega));

    const decl = toDeg(Math.asin(Math.sin(toRad(epsilon)) * Math.sin(toRad(lambda))));

    const y = Math.tan(toRad(epsilon / 2)) ** 2;
    const Etime = 4 * toDeg(
        y * Math.sin(2 * toRad(L0)) -
        2 * e * Math.sin(Mrad) +
        4 * e * y * Math.sin(Mrad) * Math.cos(2 * toRad(L0)) -
        0.5 * y * y * Math.sin(4 * toRad(L0)) -
        1.25 * e * e * Math.sin(2 * Mrad)
    );

    const lat = toRad(latDeg);
    const h0 = toRad(-0.833);
    const cosH = (Math.sin(h0) - Math.sin(lat) * Math.sin(toRad(decl))) / (Math.cos(lat) * Math.cos(toRad(decl)));
    if (cosH < -1 || cosH > 1) return { sunrise: null, sunset: null };

    const H = toDeg(Math.acos(cosH));
    const solarNoonMinUTC = 720 - 4 * lonDeg - Etime;
    const sunriseMinUTC = solarNoonMinUTC - 4 * H;
    const sunsetMinUTC = solarNoonMinUTC + 4 * H;

    const sunrise = minutesUTCToLocalDate(dateUTC, sunriseMinUTC);
    const sunset = minutesUTCToLocalDate(dateUTC, sunsetMinUTC);
    return { sunrise, sunset };
}

function julianDay(dateUTC) {
    const Y = dateUTC.getUTCFullYear();
    const M = dateUTC.getUTCMonth() + 1;
    const D = dateUTC.getUTCDate()
        + dateUTC.getUTCHours() / 24
        + dateUTC.getUTCMinutes() / 1440
        + dateUTC.getUTCSeconds() / 86400;

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
    return new Date(d.getTime()); // local
}

const toRad = d => d * Math.PI / 180;
const toDeg = r => r * 180 / Math.PI;
const fixAngle = a => ((a % 360) + 360) % 360;














































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/