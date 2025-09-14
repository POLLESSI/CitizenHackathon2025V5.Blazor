// 📂 wwwroot/js/app/leafletOutZen.module.js
// ============================
// LEAFLET OUTZEN MODULE (no bare imports)
// - Uses globals loaded by CDN: L (Leaflet), Chart, signalR (optional)
// - Safe to load directly in <script type="module"> without bundler
// ============================

/* global L, Chart, signalR */
"use strict";

// 🌍 Internal state (module-scope)
let _map = null;
const _crowdMarkers = new Map(); // id -> { marker, level }
let _filterLevel = null;
let _crowdChart = null;

// 🎯 Crowd Icons (Leaflet divIcon)
const _crowdIcons = {
    0: L.divIcon({ className: "crowd-icon level-0", html: "🟢" }),
    1: L.divIcon({ className: "crowd-icon level-1", html: "🟡" }),
    2: L.divIcon({ className: "crowd-icon level-2", html: "🟠" }),
    3: L.divIcon({ className: "crowd-icon level-3", html: "🔴" }),
};

// 🔒 Tiny HTML-escape
function _escapeHtml(s) {
    return String(s)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#039;");
}

// ============================
// Map Initialization
// ============================
export function initMap(containerId = "leafletMap", center = [50.89, 4.34], zoom = 13) {
    const el = document.getElementById(containerId);
    if (!el) throw new Error(`❌ Element #${containerId} not found.`);

    // Idempotence: if already initialized, reuse the existing instance
    if (_map && window.leafletMap) return _map;
    if (el.classList.contains("leaflet-container") && window.leafletMap) {
            _map = window.leafletMap;
            return _map;
        }
    
    _map = L.map(containerId).setView(center, zoom);

    L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: "&copy; OpenStreetMap contributors",
        maxZoom: 19
    }).addTo(_map);

    // Optional nice effects
    try { initAnimatedBackground(); } catch { }
    try { initScrollAndParallax(); } catch { }

    // For manual debugging
    window.leafletMap = _map;

    return _map;
}

// ============================
// Crowd Markers
// ============================
export function addOrUpdateCrowdMarker(id, lat, lng, level = 0, info = { title: "", description: "" }) {
    if (!_map) return;

    const key = String(id);
    const safeLevel = (level in _crowdIcons) ? level : 0;

    // Remove previous marker for this id
    const existing = _crowdMarkers.get(key);
    if (existing) {
        _map.removeLayer(existing.marker);
    }

    const marker = L.marker([lat, lng], { icon: _crowdIcons[safeLevel] })
        .bindPopup(`<strong>${_escapeHtml(info?.title ?? "")}</strong><br/>${_escapeHtml(info?.description ?? "")}`);

    marker.on("add", () => _blinkEffect(marker));
    marker.addTo(_map);

    _crowdMarkers.set(key, { marker, level: safeLevel });

    _updateVisibleCrowdMarkers();
}

export function removeCrowdMarker(id) {
    if (!_map) return;
    const key = String(id);
    const entry = _crowdMarkers.get(key);
    if (!entry) return;
    _map.removeLayer(entry.marker);
    _crowdMarkers.delete(key);
}

export function setFilterLevel(level) {
    _filterLevel = (level === undefined || level === null) ? null : level;
    _updateVisibleCrowdMarkers();
}

function _updateVisibleCrowdMarkers() {
    if (!_map) return;
    for (const { marker, level } of _crowdMarkers.values()) {
        const shouldShow = (_filterLevel === null) || (level === _filterLevel);
        const isOnMap = _map.hasLayer(marker);
        if (shouldShow && !isOnMap) marker.addTo(_map);
        if (!shouldShow && isOnMap) _map.removeLayer(marker);
    }
}

function _blinkEffect(marker) {
    const el = marker.getElement();
    if (!el) return;
    el.classList.add("fade-in", "blink");
    setTimeout(() => el.classList.remove("blink"), 2000);
}

// ============================
// Suggestions
// ============================
export function showSuggestions(suggestions = []) {
    if (!_map || !Array.isArray(suggestions)) return;

    suggestions.forEach(s => {
        const icon = L.icon({
            iconUrl: s.icon || "images/suggestion-marker.png",
            iconSize: [32, 32],
            iconAnchor: [16, 32]
        });
        const title = s.title ?? s.name ?? "Suggestion";
        const distance = (s.distanceKm != null) ? `${s.distanceKm} km` : "";

        L.marker([s.latitude, s.longitude], { icon })
            .addTo(_map)
            .bindPopup(
                `<strong>${_escapeHtml(title)}</strong>` +
                (distance ? `<br/>À ${_escapeHtml(String(distance))}` : "")
            );
    });
}

// ============================
// Traffic Markers
// ============================
const _trafficIconCache = new Map(); // level -> icon

function _trafficIcon(level) {
    if (_trafficIconCache.has(level)) return _trafficIconCache.get(level);
    const color = (level === 1) ? "green" : (level === 2) ? "orange" : (level === 3) ? "red" : "gray";
    const icon = L.divIcon({
        className: "traffic-icon",
        html: `<div class="pulse-${color}"></div>`,
        iconSize: [16, 16],
        iconAnchor: [8, 8],
        popupAnchor: [0, -10]
    });
    _trafficIconCache.set(level, icon);
    return icon;
}

export function addTrafficMarkers(trafficEvents = []) {
    if (!_map || !Array.isArray(trafficEvents)) return;
    trafficEvents.forEach(e => {
        L.marker([e.latitude, e.longitude], { icon: _trafficIcon(e.level) })
            .addTo(_map)
            .bindPopup(
                `<strong>${_escapeHtml(e.description ?? "Traffic")}</strong><br/>` +
                `${e.timestamp ? new Date(e.timestamp).toLocaleString() : ""}`
            );
    });
}

// ============================
// Charts
// ============================

window.OutZenCharts = {
    _instances: {},
    ensure(canvasId, config) {
        this.destroy(canvasId);
        const el = document.getElementById(canvasId);
        if (!el) { console.warn("Canvas not found:", canvasId); return null; }
        const ctx = el.getContext('2d');
        const chart = new Chart(ctx, config);
        this._instances[canvasId] = chart;
        return chart;
    },
    destroy(canvasId) {
        const chart = this._instances[canvasId];
        if (chart) {
            try { chart.destroy(); } catch { }
            delete this._instances[canvasId];
        }
    }
};

export function initCrowdChart(canvasId = "crowdChart") {
    const config = {
        type: "line",
        data: {
            labels: [],
            datasets: [{
                label: "Crowd Level (%)",
                data: [],
                // (you can let Chart.js choose the colors)
                tension: 0.25
            }]
        },
        options: { responsive: true, scales: { y: { beginAtZero: true, max: 100 } } }
    };
    window.OutZenCharts.ensure(canvasId, config);
}

export function updateCrowdChart(value, canvasId = "crowdChart") {
    const chart = window.OutZenCharts._instances[canvasId];
    if (!chart) return;
    const now = new Date().toLocaleTimeString();
    const numeric = Number(String(value).replace("%", "").trim());
    if (!Number.isFinite(numeric)) return;

    chart.data.labels.push(now);
    chart.data.datasets[0].data.push(numeric);
    if (chart.data.labels.length > 20) {
        chart.data.labels.shift();
        chart.data.datasets[0].data.shift();
    }
    chart.update();
}

// ============================
// Animations (Background & Parallax)
// ============================
export function initAnimatedBackground(svgSelector = ".svg-background svg", stop1Id = "stop1", stop2Id = "stop2") {
    const svg = document.querySelector(svgSelector);
    const stop1 = document.getElementById(stop1Id);
    const stop2 = document.getElementById(stop2Id);
    if (!svg || !stop1 || !stop2) return;

    let hue = 0;
    setInterval(() => {
        hue = (hue + 1) % 360;
        stop1.setAttribute("stop-color", `hsl(${hue}, 100%, 60%)`);
        stop2.setAttribute("stop-color", `hsl(${(hue + 120) % 360}, 100%, 60%)`);
    }, 50);

    document.addEventListener("mousemove", e => {
        const x = (e.clientX / window.innerWidth - 0.5) * 20;
        const y = (e.clientY / window.innerHeight - 0.5) * 20;
        svg.style.transform = `rotateX(${y}deg) rotateY(${x}deg) scale(1.05)`;
    });

    document.addEventListener("mouseout", () => svg.style.transform = "rotateX(0deg) rotateY(0deg)");
    document.addEventListener("scroll", () => {
        const intensity = Math.min(window.scrollY / 1000, 1);
        svg.style.transform += ` scale(${1 + intensity * 0.05})`;
    });

    document.body.style.background = "linear-gradient(135deg, #000428, #004e92)";
}

export function initScrollAndParallax() {
    const sections = document.querySelectorAll("section.animate-on-scroll, .scroll-reveal");
    const observer = new IntersectionObserver(entries => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add("visible");
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.1 });

    sections.forEach(s => observer.observe(s));

    const bg = document.querySelector(".parallax-bg");
    if (bg) document.addEventListener("mousemove", e => {
        const x = (e.clientX / window.innerWidth - 0.5) * 20;
        const y = (e.clientY / window.innerHeight - 0.5) * 20;
        bg.style.transform = `translate(${x}px, ${y}px)`;
    });
}

// ============================
// SignalR (optional JS client)
// ============================
export async function startSignalR(hubUrl, onDataReceived = {}) {
    if (typeof signalR === "undefined") {
        console.warn("⚠️ signalR global not found (JS client). If you only use the C# client, you can ignore this.");
        return { connected: false, start: async () => { }, stop: async () => { } };
    }

    const conn = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, { transport: signalR.HttpTransportType.WebSockets })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Information)
        .build();

    if (onDataReceived.crowd) conn.on("ReceiveCrowdInfo", onDataReceived.crowd);
    if (onDataReceived.traffic) conn.on("ReceiveTrafficInfo", onDataReceived.traffic);
    if (onDataReceived.suggestions) conn.on("ReceiveSuggestions", onDataReceived.suggestions);
    if (onDataReceived.weather) conn.on("ReceiveWeatherForecasts", onDataReceived.weather);

    conn.onclose(err => console.warn("SignalR disconnected:", err));

    try {
        await conn.start();
        console.log("✅ SignalR connected (JS client).");
    } catch (err) {
        console.error("❌ SignalR connection failed:", err);
        setTimeout(() => startSignalR(hubUrl, onDataReceived), 5000);
    }

    window.outzenConnection = conn;
    return {
        connected: true,
        start: () => conn.start(),
        stop: () => conn.stop()
    };
}

// ============================
// Default factory (keeps old usage pattern)
// ============================
export default function OutZenApp(opts = { mapId: "leafletMap", center: [50.89, 4.34], zoom: 13, enableChart: false }) {
    initMap(opts.mapId, opts.center, opts.zoom);
    if (opts.enableChart) initCrowdChart("crowdChart");

    return {
        addOrUpdateCrowdMarker,
        removeCrowdMarker,
        setFilterLevel,
        showSuggestions,
        addTrafficMarkers,
        updateCrowdChart
    };
}
export function bootOutZen(opts) {
    const app = OutZenApp(
        opts ?? { mapId: 'leafletMap', center: [50.89, 4.34], zoom: 13, enableChart: true }
    );

    window.OutZenInterop = {
        addOrUpdateCrowdMarker: (id, lat, lng, level, info) => app.addOrUpdateCrowdMarker(id, lat, lng, level, info),
        setFilter: (lvl) => app.setFilterLevel(lvl),
        removeMarker: (id) => app.removeCrowdMarker(id)
    };

    return app; // optional but useful for debugging
}
// ---------------- Compatibility aliases for Blazor (optional)
window.initScrollFade = initScrollAndParallax;

window.mapInterop = {
    init: function (elementId) {
        initMap(elementId, [48.86, 2.35], 12);
    },
    updateTrafficMarkers: function (events) {
        addTrafficMarkers(events || []);
    }
};













































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/