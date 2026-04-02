// wwwroot/js/simulator.control.js

export function simulateCrowdEvent() {
    const payload = [{
        latitude: 50.8503,
        longitude: 4.3517,
        level: 4,
        title: "Simulated Crowd Event",
        description: "High crowd density simulated from MapSimulator",
        timestamp: new Date().toISOString()
    }];

    console.log("[simulator] simulateCrowdEvent", payload);

    if (globalThis.crowdInterop?.updateCrowdMarkers) {
        globalThis.crowdInterop.updateCrowdMarkers(payload, {
            scopeKey: "presentation",
            clearPrevious: false,
            fit: true
        });
    } else {
        console.warn("[simulator] crowdInterop unavailable");
    }
}

export function simulateTrafficEvent() {
    const payload = [{
        latitude: 50.8466,
        longitude: 4.3528,
        level: 3,
        title: "Simulated Traffic Event",
        description: "Simulated traffic congestion",
        timestamp: new Date().toISOString()
    }];

    console.log("[simulator] simulateTrafficEvent", payload);

    if (globalThis.trafficInterop?.updateTrafficMarkers) {
        globalThis.trafficInterop.updateTrafficMarkers(payload, {
            scopeKey: "presentation",
            clearPrevious: false,
            fit: true
        });
    } else {
        console.warn("[simulator] trafficInterop unavailable");
    }
}

export function simulateWeatherForecast() {
    const payload = [{
        Latitude: 50.8440,
        Longitude: 4.3600,
        Summary: "Simulated Weather Forecast",
        Description: "Heavy rain expected",
        TemperatureC: 7,
        Humidity: 91,
        WindSpeedKmh: 35,
        RainfallMm: 12,
        WeatherType: "rain",
        IsSevere: true,
        timestamp: new Date().toISOString()
    }];

    console.log("[simulator] simulateWeatherForecast", payload);

    if (globalThis.weatherInterop?.updateWeatherMarkers) {
        globalThis.weatherInterop.updateWeatherMarkers(payload, {
            scopeKey: "presentation",
            fit: true
        });
    } else {
        console.warn("[simulator] weatherInterop unavailable");
    }
}

export function closeSimulator() {
    console.log("[simulator] closeSimulator");
    globalThis.dispatchEvent(new CustomEvent("outzen:simulator:close"));
}

export const Simulator = {
    boot() {
        console.log("[simulator] boot");
    },
    stop() {
        console.log("[simulator] stop");
    },
    simulateCrowdEvent,
    simulateTrafficEvent,
    simulateWeatherForecast,
    closeSimulator
};

export default Simulator;