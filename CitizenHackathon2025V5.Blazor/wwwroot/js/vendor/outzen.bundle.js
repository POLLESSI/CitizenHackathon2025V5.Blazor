/*wwwroot / js / vendor / outzen.bundle.js*/

// =======================================
// OUTZEN BUNDLE JS - Merging all modules
// =======================================
import * as THREE from 'https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.module.js';

/* =============================
   Leaflet & SignalR
=============================*/
import L from 'https://unpkg.com/leaflet@1.9.4/dist/leaflet-src.esm.js';
import 'https://unpkg.com/leaflet.markercluster@1.5.3/dist/leaflet.markercluster.js';
import * as signalR from 'https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/7.0.5/signalr.min.js';

/* =============================
   Map Logic
=============================*/
class LeafletMap {
    constructor(mapId, center = [50.89, 4.34], zoom = 13) {
        this.map = L.map(mapId).setView(center, zoom);
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '© OpenStreetMap contributors'
        }).addTo(this.map);
        this.markers = L.markerClusterGroup();
        this.map.addLayer(this.markers);
    }
    addMarker(id, lat, lng, info, level) {
        const marker = L.marker([lat, lng], { title: id }).bindPopup(info);
        marker.level = level;
        this.markers.addLayer(marker);
    }
    removeMarker(id) {
        this.markers.eachLayer(m => {
            if (m.options.title === id) this.markers.removeLayer(m);
        });
    }
}

/* =============================
   SignalR Client
=============================*/
class SignalRClient {
    constructor(url) {
        this.connection = new signalR.HubConnectionBuilder().withUrl(url).build();
    }
    start() {
        return this.connection.start().catch(err => console.error(err));
    }
    on(event, callback) {
        this.connection.on(event, callback);
    }
}

/* =============================
   OutZen Core App
=============================*/
export default function OutZenApp({ mapId, center, zoom, enableChart }) {
    const mapApp = new LeafletMap(mapId, center, zoom);
    const hub = new SignalRClient('/outzenHub');
    hub.start().then(() => console.log('✅ SignalR connected'));

    // Managing markers via SignalR
    hub.on('UpdateCrowd', data => {
        mapApp.addMarker(data.id, data.lat, data.lng, data.info, data.level);
    });

    // Chart.js simple wrapper if enabled
    let chart;
    if (enableChart) {
        const ctx = document.createElement('canvas');
        document.body.appendChild(ctx);
        chart = new Chart(ctx, { type: 'bar', data: { labels: [], datasets: [{ label: 'Affluence', data: [] }] } });
    }

    return {
        addOrUpdateCrowdMarker: (id, lat, lng, level, info) => mapApp.addMarker(id, lat, lng, info, level),
        removeCrowdMarker: (id) => mapApp.removeMarker(id),
        setFilterLevel: (level) => {
            mapApp.markers.eachLayer(m => {
                if (m.level !== level) m.setOpacity(0.3);
                else m.setOpacity(1);
            });
        }
    };
}

/* =============================
   Visual effects / Time Effects
=============================*/
function animateBackground() {
    const canvas = document.getElementById('geometryCanvas');
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    let t = 0;
    function loop() {
        ctx.fillStyle = `hsl(${t % 360}, 50%, 5%)`;
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        t += 0.5;
        requestAnimationFrame(loop);
    }
    loop();
}
animateBackground();


























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/