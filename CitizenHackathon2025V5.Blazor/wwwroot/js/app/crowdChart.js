// wwwroot/js/app/crowdChart.js
// Requires Chart.js already loaded (index.html)

window.initCrowdChart = function (canvasId, jsonConfig) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
        console.warn("[OutZen] initCrowdChart: canvas not found:", canvasId);
        return;
    }

    let config = jsonConfig;
    if (typeof jsonConfig === "string") {
        try {
            config = JSON.parse(jsonConfig);
        } catch (e) {
            console.error("[OutZen] initCrowdChart: invalid JSON config", e);
            return;
        }
    }

    const ctx = canvas.getContext("2d");
    // You can store the instance in window.__outzenChartInstance if you want to manage destroy()
    new Chart(ctx, config);
};
