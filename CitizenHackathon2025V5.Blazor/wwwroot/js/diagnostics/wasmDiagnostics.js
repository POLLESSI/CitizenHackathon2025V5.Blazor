window.outzenDiagnostics = window.outzenDiagnostics || {};

window.outzenDiagnostics.runWasmDiagnostic = async function () {
    const wasm = await checkWasmAccessInternal();

    const resources = performance
        .getEntriesByType("resource")
        .map(r => {
            const transfer = Math.trunc(r.transferSize || 0);
            const encoded = Math.trunc(r.encodedBodySize || 0);
            const decoded = Math.trunc(r.decodedBodySize || 0);

            let cacheMode = "network";

            if (transfer === 0 && encoded > 0) {
                cacheMode = "memory/disk cache";
            } else if (transfer > 0 && encoded > 0 && transfer < encoded) {
                cacheMode = "compressed/network";
            }

            return {
                name: r.name,
                type: r.initiatorType || guessType(r.name),
                transferSize: transfer,
                encodedBodySize: encoded,
                decodedBodySize: decoded,
                durationMs: r.duration || 0,
                cacheMode
            };
        });

    const totalBytes = sum(resources, x => x.transferSize);
    const frameworkBytes = sum(resources.filter(isFrameworkAsset), x => x.transferSize);
    const jsBytes = sum(resources.filter(x => isJs(x.name)), x => x.transferSize);
    const cssBytes = sum(resources.filter(x => isCss(x.name)), x => x.transferSize);

    return {
        wasm,
        resources,
        totalBytes,
        frameworkBytes,
        javaScriptBytes: jsBytes,
        cssBytes,
        signalRScriptCount: resources.filter(x => (x.name || "").toLowerCase().includes("signalr")).length,
        signalRConnectionCount: estimateSignalRConnections(resources),
        hasLeaflet: !!window.L,
        hasMarkerCluster: !!(window.L && (L.MarkerClusterGroup || L.markerClusterGroup)),
        hasOutZenInterop: !!window.OutZenInterop
    };
};

async function checkWasmAccessInternal() {
    const url = `${location.origin}/_framework/dotnet.native.wasm`;

    try {
        const response = await fetch(url, {
            method: "GET",
            cache: "no-store"
        });

        const buffer = await response.arrayBuffer();

        return {
            url,
            ok: response.ok,
            status: response.status,
            contentType: response.headers.get("content-type"),
            contentLength: response.headers.get("content-length"),
            sizeBytes: buffer.byteLength,
            error: response.ok ? null : response.statusText
        };
    } catch (e) {
        return {
            url,
            ok: false,
            status: 0,
            contentType: null,
            contentLength: null,
            sizeBytes: null,
            error: e?.message ?? String(e)
        };
    }
}

function guessType(name) {
    const n = (name || "").toLowerCase();

    if (n.endsWith(".wasm")) return "wasm";
    if (n.endsWith(".dll")) return "dll";
    if (n.endsWith(".js")) return "script";
    if (n.endsWith(".css")) return "css";
    if (n.endsWith(".json")) return "json";
    if (n.match(/\.(png|jpg|jpeg|webp|svg|gif)$/)) return "image";

    return "other";
}

function sum(items, selector) {
    return items.reduce((acc, x) => acc + (selector(x) || 0), 0);
}

function isFrameworkAsset(r) {
    const n = (r.name || "").toLowerCase();
    return n.includes("/_framework/") || n.endsWith(".dll") || n.endsWith(".wasm");
}

function isJs(name) {
    return (name || "").toLowerCase().endsWith(".js");
}

function isCss(name) {
    return (name || "").toLowerCase().endsWith(".css");
}

function estimateSignalRConnections(resources) {
    return resources.filter(x => {
        const n = (x.name || "").toLowerCase();
        return n.includes("/negotiate") || n.includes("/hubs/");
    }).length;
}