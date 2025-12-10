using System;
using System.Collections.Generic;

namespace CitizenHackathon2025.Blazor.DTOs
{
    public class SuggestionGroupedByPlaceDTO
    {
        // Must match the JSON structure returned by the API
        public string PlaceName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool Indoor { get; set; }

        // You're not really using it right now, but we're leaving it for the future UI
        public string CrowdLevel { get; set; } = string.Empty;

        // You receive them as strings in your domain => we keep the string
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public int SuggestionCount { get; set; }
        public DateTime LastSuggestedAt { get; set; }

        // Optional: the complete list of aggregated suggestions
        // You can leave it for JSON alignment, or remove it if you want to reduce the file size.
        public List<ClientSuggestionDTO>? Suggestions { get; set; }
    }
}



























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/