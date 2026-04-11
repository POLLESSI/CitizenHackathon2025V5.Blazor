/* wwwroot/js/crowdInterop.js */
(function () {
    "use strict";

    const DEFAULT_SCOPE = "presentation";
    const DEFAULT_KIND = "crowd";
    const DEFAULT_ICON = "👥";

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

    function normalizeCrowdItem(item, index = 0) {
        if (!item || typeof item !== "object") return null;

        const lat = toNumber(pick(item,
            "lat", "Lat",
            "latitude", "Latitude"));

        const lng = toNumber(pick(item,
            "lng", "Lng",
            "lon", "Lon",
            "longitude", "Longitude"));

        if (lat == null || lng == null) {
            console.warn("[crowdInterop] skip item: invalid coords", item);
            return null;
        }

        const rawId = pick(item,
            "id", "Id",
            "crowdInfoId", "CrowdInfoId") ?? `crowd-${index}-${lat}-${lng}`;

        const level = normalizeLevel(pick(item,
            "level", "Level",
            "crowdLevel", "CrowdLevel"), 2);

        const title = String(pick(item,
            "title", "Title",
            "summary", "Summary",
            "name", "Name") ?? `Crowd #${rawId}`);

        const description = String(pick(item,
            "description", "Description",
            "message", "Message") ?? "Crowd update");

        return {
            id: `crowd:${rawId}`,
            lat,
            lng,
            level,
            info: {
                kind: DEFAULT_KIND,
                title,
                description,
                icon: DEFAULT_ICON,
                isTraffic: false
            }
        };
    }

    async function ensureReady(scopeKey) {
        if (!globalThis.OutZen?.ensure) {
            console.warn("[crowdInterop] OutZen.ensure missing");
            return false;
        }

        await globalThis.OutZen.ensure(false);

        const dump = globalThis.OutZenInterop?.dumpState?.(scopeKey);
        if (dump?.hasMap) return true;

        console.warn("[crowdInterop] map not ready for scope", scopeKey, dump);
        return false;
    }

    async function clearCrowdMarkers(scopeKey) {
        const interop = globalThis.OutZenInterop;
        if (!interop) return;

        try {
            interop.pruneMarkersByPrefix?.("crowd:", scopeKey);
        } catch (e) {
            console.warn("[crowdInterop] pruneMarkersByPrefix failed", e);
        }
    }

    async function fitCrowd(scopeKey) {
        try {
            globalThis.OutZenInterop?.fitToAllMarkers?.(scopeKey, {
                padding: [22, 22],
                maxZoom: 16
            });
        } catch (e) {
            console.warn("[crowdInterop] fitToAllMarkers failed", e);
        }
    }

    async function updateCrowdMarkers(crowdData, scopeKey = DEFAULT_SCOPE, fit = true, clearPrevious = true) {
        const items = toArray(crowdData);
        console.log("[crowdInterop] updateCrowdMarkers", {
            count: items.length,
            scopeKey,
            fit,
            clearPrevious
        });

        const ready = await ensureReady(scopeKey);
        if (!ready) return false;

        if (clearPrevious) {
            await clearCrowdMarkers(scopeKey);
        }

        let added = 0;

        for (let i = 0; i < items.length; i++) {
            const normalized = normalizeCrowdItem(items[i], i);
            if (!normalized) continue;

            try {
                const ok = globalThis.OutZenInterop?.addOrUpdateCrowdMarker?.(
                    normalized.id,
                    normalized.lat,
                    normalized.lng,
                    normalized.level,
                    normalized.info,
                    scopeKey
                );

                if (ok) added++;
            } catch (e) {
                console.error("[crowdInterop] addOrUpdateCrowdMarker failed", e, normalized);
            }
        }

        if (fit && added > 0) {
            await fitCrowd(scopeKey);
        }

        try {
            globalThis.OutZenInterop?.refreshMapSize?.(scopeKey);
        } catch { }

        console.log("[crowdInterop] done", { added, scopeKey });
        return added > 0;
    }

    globalThis.crowdInterop = globalThis.crowdInterop || {};
    globalThis.crowdInterop.updateCrowdMarkers = updateCrowdMarkers;
    globalThis.crowdInterop.clearCrowdMarkers = clearCrowdMarkers;
})();



















































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/