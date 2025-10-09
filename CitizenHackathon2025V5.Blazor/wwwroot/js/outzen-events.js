/*/wwwroot/js / outzen - events.js*/
export const OutZenEvents = {
    HubPath: "/hubs/suggestionHub",
    ToClient: {
        CrowdInfoUpdated: "CrowdInfoUpdated",
        NewSuggestion: "NewSuggestion",
    },
    Groups: {
        buildEventGroup: (eventId) => `event-${eventId}`,
    },
};