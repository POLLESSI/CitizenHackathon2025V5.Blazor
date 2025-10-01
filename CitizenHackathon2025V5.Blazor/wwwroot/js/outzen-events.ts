// wwwroot/js/outzen-events.ts
export const OutZenEvents = {
    HubPath: "/hubs/outzen",
    Groups: {
        EventPrefix: "event-",
        buildEventGroup: (eventId: string) => `event-${eventId}`,
    },
    ToClient: {
        NewSuggestion: "NewSuggestion",
        CrowdInfoUpdated: "CrowdInfoUpdated",
        SuggestionsUpdated: "SuggestionsUpdated",
        WeatherUpdated: "WeatherUpdated",
        TrafficUpdated: "TrafficUpdated",
    },
} as const;

export type OutZenEventName =
    | typeof OutZenEvents.ToClient.NewSuggestion
    | typeof OutZenEvents.ToClient.CrowdInfoUpdated
    | typeof OutZenEvents.ToClient.SuggestionsUpdated
    | typeof OutZenEvents.ToClient.WeatherUpdated
    | typeof OutZenEvents.ToClient.TrafficUpdated;
