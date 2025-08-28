// For the production

//// service-worker.published.js - Production Mode (PWA)

//// Cache versioning
//const CACHE_VERSION = 'v1';
//const CACHE_NAME = `outzen-cache-${CACHE_VERSION}`;
//const ASSETS_TO_CACHE = [
//    '/',
//    'index.html',
//    'manifest.json',
//    'css/app.css',
//    'css/bootstrap/bootstrap.min.css',
//    'icons/icon-192.png',
//    'icons/icon-512.png',
//    '_framework/blazor.webassembly.js',
//    '_framework/dotnet.wasm',
//    '_framework/dotnet.timezones.blat',
//    '_framework/dotnet.native.wasm',
//    '_framework/blazor.boot.json',
//    '_framework/System.Text.Json.dll',
//    // Add other DLLs here if needed
//    // '_content/Blazored.Toast/blazored-toast.css',
//    // etc.
//];

//// Installation: Initial caching
//self.addEventListener('install', event => {
//    console.log('📦 [SW] Installation...');
//    event.waitUntil(
//        caches.open(CACHE_NAME).then(cache => {
//            console.log('📦 [SW] Caching static resources...');
//            return cache.addAll(ASSETS_TO_CACHE);
//        })
//    );
//    self.skipWaiting(); // Forces immediate activation
//});

//// Activation: Deleting old caches
//self.addEventListener('activate', event => {
//    console.log('♻️ [SW] Activation...');
//    event.waitUntil(
//        caches.keys().then(keys => {
//            return Promise.all(
//                keys
//                    .filter(key => key !== CACHE_NAME)
//                    .map(oldKey => {
//                        console.log('🧹 [SW] Deleting obsolete cache :', oldKey);
//                        return caches.delete(oldKey);
//                    })
//            );
//        })
//    );
//    self.clients.claim(); // Take control immediately
//});

//// Interception of HTTP requests
//self.addEventListener('fetch', event => {
//    // Do not intercept POST or non-GET requests
//    if (event.request.method !== 'GET') return;

//    event.respondWith(
//        caches.match(event.request).then(response => {
//            // Resource found in cache
//            if (response) {
//                return response;
//            }

//            // Otherwise: fetch from the network and cache the new resource if possible
//            return fetch(event.request)
//                .then(networkResponse => {
//                    // Dynamic caching only if response is valid
//                    if (
//                        !networkResponse ||
//                        networkResponse.status !== 200 ||
//                        networkResponse.type !== 'basic'
//                    ) {
//                        return networkResponse;
//                    }

//                    const responseClone = networkResponse.clone();
//                    caches.open(CACHE_NAME).then(cache => {
//                        cache.put(event.request, responseClone);
//                    });

//                    return networkResponse;
//                })
//                .catch(() => {
//                    // Optional: return from a fallback (e.g.: offline page)
//                    // return caches.match('/offline.html', '/randompong.html');
//                    return caches.match('offline.html');
//                    });
//                });
//        })
//    );
//});
//self.addEventListener('install', event => {
//    event.waitUntil(
//        caches.open("outzen-cache-v1").then(cache =>
//            cache.addAll([
//                '/',
//                'index.html',
//                'offline.html',
//                'manifest.webmanifest',
//                'icons/icon-192.png',
//                'icons/icon-512.png'
//            ])
//        )
//    );
//    self.skipWaiting();
//});

//self.addEventListener('fetch', event => {
//    event.respondWith(
//        fetch(event.request).catch(() => caches.match(event.request).then(response => response || caches.match('offline.html')))
//    );
//});












































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/