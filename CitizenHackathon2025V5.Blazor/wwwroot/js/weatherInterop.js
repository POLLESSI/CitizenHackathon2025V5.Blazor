/* wwwroot/js/weatherInterop.js */
(function () {
    "use strict";

    const DEFAULT_SCOPE = "presentation";

    function toArray(value) {
        if (Array.isArray(value)) return value;
        if (value == null) return [];
        return [value];
    }

    function toNumber(v) {
        if (v == null) return null;
        if (typeof v === "string") v = v.replace(",", ".");
        const n = Number(v);
        return Number.isFinite(n) ? n : null;
    }

    function pick(obj, ...keys) {
        for (const k of keys) {
            if (obj && obj[k] != null) return obj[k];
        }
        return null;
    }

    function normalizeLevel(value, fallback = 2) {
        const n = Number(value);
        if (!Number.isFinite(n)) return fallback;
        return Math.max(0, Math.min(4, n));
    }

    function normalizeWeatherType(value) {
        const s = String(value ?? "").trim().toLowerCase();
        return s || "clouds";
    }

    function normalizeWeatherItem(item, index = 0) {
        if (!item || typeof item !== "object") return null;

        const lat = toNumber(pick(item,
            "lat", "Lat",
            "latitude", "Latitude"));

        const lng = toNumber(pick(item,
            "lng", "Lng",
            "lon", "Lon",
            "longitude", "Longitude"));

        if (lat == null || lng == null) {
            console.warn("[weatherInterop] skip item: invalid coords", item);
            return null;
        }

        const rawId = pick(item,
            "id", "Id",
            "weatherForecastId", "WeatherForecastId",
            "forecastId", "ForecastId") ?? `weather-${index}-${lat}-${lng}`;

        const weatherType = normalizeWeatherType(pick(item,
            "weatherType", "WeatherType",
            "weatherMain", "WeatherMain"));

        const level = normalizeLevel(pick(item,
            "level", "Level",
            "severity", "Severity",
            "weatherLevel", "WeatherLevel"), weatherType === "storm" ? 4 : 2);

        const title = String(pick(item,
            "title", "Title",
            "summary", "Summary") ?? `Weather #${rawId}`);

        const description = String(pick(item,
            "description", "Description",
            "message", "Message") ?? "Weather update");

        const temperatureC = toNumber(pick(item, "temperatureC", "TemperatureC"));
        const humidity = toNumber(pick(item, "humidity", "Humidity"));
        const windSpeedKmh = toNumber(pick(item, "windSpeedKmh", "WindSpeedKmh"));
        const rainfallMm = toNumber(pick(item, "rainfallMm", "RainfallMm"));
        const isSevere = level >= 4 || weatherType === "storm" || !!pick(item, "isSevere", "IsSevere");

        return {
            Id: rawId,
            Latitude: lat,
            Longitude: lng,
            Summary: title,
            Description: description,
            WeatherType: weatherType,
            TemperatureC: temperatureC ?? 0,
            Humidity: humidity ?? 0,
            WindSpeedKmh: windSpeedKmh ?? 0,
            RainfallMm: rainfallMm ?? 0,
            IsSevere: isSevere
        };
    }

    async function ensureReady(scopeKey) {
        if (!globalThis.OutZen?.ensure) {
            console.warn("[weatherInterop] OutZen.ensure missing");
            return false;
        }

        await globalThis.OutZen.ensure(false);

        const dump = globalThis.OutZenInterop?.dumpState?.(scopeKey);
        if (dump?.hasMap) return true;

        console.warn("[weatherInterop] map not ready for scope", scopeKey, dump);
        return false;
    }

    async function clearWeatherMarkers(scopeKey) {
        const interop = globalThis.OutZenInterop;
        if (!interop) return;

        try {
            interop.pruneMarkersByPrefix?.("wf:", scopeKey);
        } catch (e) {
            console.warn("[weatherInterop] pruneMarkersByPrefix failed", e);
        }
    }

    async function fitWeather(scopeKey) {
        try {
            globalThis.OutZenInterop?.fitToAllMarkers?.(scopeKey, {
                padding: [22, 22],
                maxZoom: 16
            });
        } catch (e) {
            console.warn("[weatherInterop] fitToAllMarkers failed", e);
        }
    }

    async function updateWeatherMarkers(weatherData, scopeKey = DEFAULT_SCOPE, fit = true, clearPrevious = true) {
        const items = toArray(weatherData);
        console.log("[weatherInterop] updateWeatherMarkers", {
            count: items.length,
            scopeKey,
            fit,
            clearPrevious
        });

        const ready = await ensureReady(scopeKey);
        if (!ready) return false;

        if (clearPrevious) {
            await clearWeatherMarkers(scopeKey);
        }

        const normalizedItems = [];

        for (let i = 0; i < items.length; i++) {
            const normalized = normalizeWeatherItem(items[i], i);
            if (normalized) normalizedItems.push(normalized);
        }

        if (!normalizedItems.length) {
            console.warn("[weatherInterop] no valid items to render");
            return false;
        }

        try {
            globalThis.OutZenInterop?.addOrUpdateWeatherMarkers?.(normalizedItems, scopeKey);
        } catch (e) {
            console.error("[weatherInterop] addOrUpdateWeatherMarkers failed", e, normalizedItems);
            return false;
        }

        if (fit) {
            await fitWeather(scopeKey);
        }

        try {
            globalThis.OutZenInterop?.refreshMapSize?.(scopeKey);
        } catch { }

        console.log("[weatherInterop] done", {
            added: normalizedItems.length,
            scopeKey
        });

        return true;
    }

    globalThis.weatherInterop = globalThis.weatherInterop || {};
    globalThis.weatherInterop.updateWeatherMarkers = updateWeatherMarkers;
    globalThis.weatherInterop.clearWeatherMarkers = clearWeatherMarkers;
})();






















































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/