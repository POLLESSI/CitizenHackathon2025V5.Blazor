// wwwroot/js/outzen.hub.js
// =========================================
// OUTZEN HUB JS CLIENT (no bare imports)
// Uses the global "signalR" from CDN
// =========================================

"use strict";

/* global signalR */
const signalR = window.signalR;
if (!signalR) {
    console.error("❌ SignalR not found: CDN script failed to load.");
    throw new Error("SignalR not loaded");
}

let connection = null;

/**
 * Starts the SignalR connection to the OutZen hub.
 * @param {string} hubUrl - Full URL of the hub (eg: https://localhost:7254/hubs/outzen)
 * @param {string} getTokenFuncName - name of the JS function that provides the JWT token
 * @param {any} dotNetRef - .NET reference for callbacks
 * @param {string} eventId - ID of the event to join
 */
export async function startOutzen(hubUrl, getTokenFuncName, dotNetRef, eventId) {
    // ✅ Checks for the presence of global signalR
    if (typeof signalR === "undefined") {
        console.error("❌ signalR global not found. Make sure CDN is loaded before this script.");
        return;
    }

    const tokenProvider = typeof window[getTokenFuncName] === "function"
        ? window[getTokenFuncName]
        : () => "";

    const hubToken = await tokenProvider();

    connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
            accessTokenFactory: () => hubToken
        })
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // 📡 Event handlers
    connection.on("CrowdInfoUpdated", dto => {
        dotNetRef?.invokeMethodAsync("OnCrowdInfoUpdatedFromJs", dto);
    });

    connection.on("SuggestionsUpdated", list => {
        dotNetRef?.invokeMethodAsync("OnSuggestionsUpdatedFromJs", list);
    });

    try {
        await connection.start();
        console.log("✅ OutZen Hub connected:", hubUrl);

        if (eventId)
            await connection.invoke("JoinEventGroup", eventId);
    } catch (err) {
        console.error("❌ OutZen Hub connection failed:", err);
    }
}

/**
 * tops SignalR connection if active.
 */
export async function stopOutzen() {
    if (connection) {
        try {
            await connection.stop();
            console.log("🛑 OutZen Hub stopped.");
        } catch (err) {
            console.warn("⚠️ Error stopping OutZen Hub:", err);
        } finally {
            connection = null;
        }
    }
}