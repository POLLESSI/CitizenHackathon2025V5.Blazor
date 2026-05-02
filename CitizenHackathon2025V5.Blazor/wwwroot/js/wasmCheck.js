/*wwwroot / js / wasmCheck.js*/
window.checkWasmFile = async function () {
    try {
        const response = await fetch("/_framework/dotnet.wasm", {
            method: "HEAD",
            cache: "no-store"
        });

        return {
            ok: response.ok,
            status: response.status,
            url: response.url,
            message: response.ok
                ? "dotnet.wasm accessible"
                : `dotnet.wasm inaccessible: HTTP ${response.status}`
        };
    } catch (e) {
        return {
            ok: false,
            status: 0,
            url: "/_framework/dotnet.wasm",
            message: e?.message ?? "Unknown JS error"
        };
    }
};