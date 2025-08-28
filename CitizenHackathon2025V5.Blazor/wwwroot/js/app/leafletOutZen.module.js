// 📂 wwwroot/js/app/leafletOutZen.module.js
// ============================
// LEAFLET OUTZEN MODULE
// ============================

// 🔗 Low-level Leaflet utilities
import L from 'leaflet';
import { Chart } from 'chart.js';
import * as signalR from '@microsoft/signalr';

// 🌍 Internal variables
let map = null;
const crowdMarkers = {};
let filterLevel = null;
let crowdChart = null;

// 🎯 Crowd Icons (Leaflet divIcon)
const crowdIcons = {
    0: L.divIcon({ className: 'crowd-icon level-0', html: '🟢' }),
    1: L.divIcon({ className: 'crowd-icon level-1', html: '🟡' }),
    2: L.divIcon({ className: 'crowd-icon level-2', html: '🟠' }),
    3: L.divIcon({ className: 'crowd-icon level-3', html: '🔴' }),
};

// 🔒 HTML anti-injection
function escapeHtml(s) {
    return String(s)
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}

// ============================
// Map Initialization
// ============================
export function initMap(containerId = 'map', center = [50.89, 4.34], zoom = 13) {
    const el = document.getElementById(containerId);
    if (!el) throw new Error(`❌ Element #${containerId} not found.`);

    map = L.map(containerId).setView(center, zoom);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap contributors',
    }).addTo(map);

    initAnimatedBackground();
    initScrollAndParallax();

    window.leafletMap = map; // external access
    return map;
}

// ============================
// Crowd Markers
// ============================
export function addOrUpdateCrowdMarker(id, lat, lng, level = 0, info = { title: '', description: '' }) {
    if (!map) return;
    if (crowdMarkers[id]) map.removeLayer(crowdMarkers[id].marker);

    const safeLevel = level in crowdIcons ? level : 0;
    const marker = L.marker([lat, lng], { icon: crowdIcons[safeLevel] })
        .bindPopup(`<strong>${escapeHtml(info.title)}</strong><br/>${escapeHtml(info.description)}`);

    marker.on('add', () => blinkEffect(marker));
    marker.addTo(map);

    crowdMarkers[id] = { marker, level: safeLevel };
    updateVisibleCrowdMarkers();
}

export function removeCrowdMarker(id) {
    if (!map || !crowdMarkers[id]) return;
    map.removeLayer(crowdMarkers[id].marker);
    delete crowdMarkers[id];
}

export function setFilterLevel(level) {
    filterLevel = level === undefined ? null : level;
    updateVisibleCrowdMarkers();
}

function updateVisibleCrowdMarkers() {
    Object.values(crowdMarkers).forEach(({ marker, level }) => {
        if (filterLevel === null || level === filterLevel) {
            if (!map.hasLayer(marker)) marker.addTo(map);
        } else {
            if (map.hasLayer(marker)) map.removeLayer(marker);
        }
    });
}

function blinkEffect(marker) {
    const el = marker.getElement();
    if (!el) return;
    el.classList.add('fade-in', 'blink');
    setTimeout(() => el.classList.remove('blink'), 2000);
}

// ============================
// Suggestions
// ============================
export function showSuggestions(suggestions = []) {
    if (!map || !Array.isArray(suggestions)) return;

    suggestions.forEach(s => {
        const icon = L.icon({ iconUrl: s.icon || 'icons/suggestion-marker.png', iconSize: [32, 32] });
        const title = s.title ?? s.name ?? 'Suggestion';
        const distance = s.distanceKm != null ? `${s.distanceKm} km` : '';

        L.marker([s.latitude, s.longitude], { icon })
            .addTo(map)
            .bindPopup(`<strong>${escapeHtml(title)}</strong>${distance ? `<br/>À ${escapeHtml(String(distance))}` : ''}`);
    });
}

// ============================
// Traffic Markers
// ============================
const trafficIconCache = {};
function getTrafficIcon(level) {
    if (trafficIconCache[level]) return trafficIconCache[level];
    const color = level === 1 ? 'green' : level === 2 ? 'orange' : level === 3 ? 'red' : 'gray';
    const icon = L.divIcon({ className: 'traffic-icon', html: `<div class="pulse-${color}"></div>`, iconSize: [16, 16], iconAnchor: [8, 8], popupAnchor: [0, -10] });
    trafficIconCache[level] = icon;
    return icon;
}

export function addTrafficMarkers(trafficEvents = []) {
    if (!map) return;
    trafficEvents.forEach(e => {
        const marker = L.marker([e.latitude, e.longitude], { icon: getTrafficIcon(e.level) })
            .bindPopup(`<strong>${escapeHtml(e.description)}</strong><br/>${new Date(e.timestamp).toLocaleString()}`);
        marker.addTo(map);
    });
}

// ============================
// Charts
// ============================
export function initCrowdChart(canvasId = 'crowdChart') {
    const ctx = document.getElementById(canvasId)?.getContext('2d');
    if (!ctx) return console.warn(`📉 Element #${canvasId} not found.`);

    crowdChart = new Chart(ctx, {
        type: 'line',
        data: { labels: [], datasets: [{ label: 'Crowd Level (%)', data: [], borderColor: '#FFD700', backgroundColor: 'rgba(255,215,0,0.3)' }] },
        options: { scales: { y: { beginAtZero: true, max: 100 } } },
    });
}

export function updateCrowdChart(value) {
    if (!crowdChart) return;
    const now = new Date().toLocaleTimeString();
    const numeric = Number(String(value).replace('%', '').trim());
    if (!Number.isFinite(numeric)) return;

    crowdChart.data.labels.push(now);
    crowdChart.data.datasets[0].data.push(numeric);

    if (crowdChart.data.labels.length > 20) {
        crowdChart.data.labels.shift();
        crowdChart.data.datasets[0].data.shift();
    }

    crowdChart.update();
}

// ============================
// Animations (Background & Parallax)
// ============================
export function initAnimatedBackground(svgSelector = '.svg-background svg', stop1Id = 'stop1', stop2Id = 'stop2') {
    const svg = document.querySelector(svgSelector);
    const stop1 = document.getElementById(stop1Id);
    const stop2 = document.getElementById(stop2Id);
    if (!svg || !stop1 || !stop2) return;

    let hue = 0;
    setInterval(() => {
        hue = (hue + 1) % 360;
        stop1.setAttribute('stop-color', `hsl(${hue}, 100%, 60%)`);
        stop2.setAttribute('stop-color', `hsl(${(hue + 120) % 360}, 100%, 60%)`);
    }, 50);

    document.addEventListener('mousemove', e => {
        const x = (e.clientX / window.innerWidth - 0.5) * 20;
        const y = (e.clientY / window.innerHeight - 0.5) * 20;
        svg.style.transform = `rotateX(${y}deg) rotateY(${x}deg) scale(1.05)`;
    });

    document.addEventListener('mouseleave', () => svg.style.transform = 'rotateX(0deg) rotateY(0deg)');
    document.addEventListener('scroll', () => {
        const scrollY = window.scrollY;
        const intensity = Math.min(scrollY / 1000, 1);
        svg.style.transform += ` scale(${1 + intensity * 0.05})`;
    });

    document.body.style.background = 'linear-gradient(135deg, #000428, #004e92)';
}

export function initScrollAndParallax() {
    const sections = document.querySelectorAll('section.animate-on-scroll, .scroll-reveal');
    const observer = new IntersectionObserver(entries => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('visible');
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.1 });

    sections.forEach(s => observer.observe(s));

    const bg = document.querySelector('.parallax-bg');
    if (bg) document.addEventListener('mousemove', e => {
        const x = (e.clientX / window.innerWidth - 0.5) * 20;
        const y = (e.clientY / window.innerHeight - 0.5) * 20;
        bg.style.transform = `translate(${x}px, ${y}px)`;
    });
}

// ============================
// SignalR
// ============================
export async function startSignalR(hubUrl, onDataReceived = {}) {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, { transport: signalR.HttpTransportType.WebSockets })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Information)
        .build();

    if (onDataReceived.crowd) connection.on('ReceiveCrowdInfo', onDataReceived.crowd);
    if (onDataReceived.traffic) connection.on('ReceiveTrafficInfo', onDataReceived.traffic);
    if (onDataReceived.suggestions) connection.on('ReceiveSuggestions', onDataReceived.suggestions);
    if (onDataReceived.weatherForecasts) connection.on('ReceiveWeatherForecasts', onDataReceived.weatherForecasts);

    connection.onclose(err => console.warn('SignalR disconnected:', err));

    try {
        await connection.start();
        console.log('✅ SignalR connected.');
    } catch (err) {
        console.error('❌ SignalR connection failed:', err);
        setTimeout(() => startSignalR(hubUrl, onDataReceived), 5000);
    }

    window.outzenConnection = connection;
}

// ---------------- Compatibility alias for Blazor
window.initScrollFade = initScrollAndParallax;














































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/