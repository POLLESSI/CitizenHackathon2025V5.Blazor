// wwwroot/js/outzen.hub.js
import * as signalR from "@microsoft/signalr";

let connection = null;

export async function startOutzen(hubUrl, getTokenFuncName, dotNetRef, eventId) {
    const hubToken = await window[getTokenFuncName]();
    connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, { accessTokenFactory: () => hubToken })
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .build();

    connection.on("CrowdInfoUpdated", dto => dotNetRef.invokeMethodAsync("OnCrowdInfoUpdatedFromJs", dto));
    connection.on("SuggestionsUpdated", list => dotNetRef.invokeMethodAsync("OnSuggestionsUpdatedFromJs", list));

    await connection.start();
    if (eventId) await connection.invoke("JoinEventGroup", eventId);
}

export async function stopOutzen() {
    if (connection) {
        await connection.stop();
        connection = null;
    }
}