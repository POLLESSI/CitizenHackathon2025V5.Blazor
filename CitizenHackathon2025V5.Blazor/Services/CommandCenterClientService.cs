using CitizenHackathon2025.Contracts.DTOs;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class CommandCenterClientService
    {
        private readonly HttpClient _http;

        public CommandCenterClientService(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("ApiWithAuth");
        }

        public async Task<CommandCenterSnapshotDTO> GetSnapshotAsync(CancellationToken ct = default)
        {
            /*
             * IMPORTANT:
             *
             * Global Command Center risk must be based on the
             * fused operational risk zones, not on raw Crowd
             * clusters.
             *
             * GetRiskZonesAsync already applies:
             *
             * Crowd
             * + Official Emergency Intelligence
             * + DecisionEngine risk floors.
             */
            var zones = await GetRiskZonesAsync(ct);


            var averageRisk = zones.Count == 0 ? 0 : Math.Clamp((int)zones.Average(z => z.RiskScore), 0, 100);

            /*
             * An official emergency must never be diluted by
             * averaging it with unrelated lower-risk zones.
             */
            var officialRiskFloor = zones
                    .Where(z => z.HasOfficialEmergencyRisk)
                    .Select(z => z.RiskScore)
                    .DefaultIfEmpty(0)
                    .Max();

            var globalRisk = Math.Max(averageRisk, officialRiskFloor);

            static bool IsCritical(RiskZoneDTO zone)
            {
                return zone.RiskScore >= 85 || zone.Severity >= 4;
            }

            static bool IsHigh(RiskZoneDTO zone)
            {
                return !IsCritical(zone)&&
                    (
                        zone.RiskScore >= 65 || zone.Severity == 3
                    );
            }

            static bool IsModerate(RiskZoneDTO zone)
            {
                return !IsCritical(zone) && !IsHigh(zone) &&
                    (
                        zone.RiskScore >= 40 || zone.Severity == 2
                    );
            }

            var officialZoneCount = zones.Count(z => z.HasOfficialEmergencyRisk);

            return new CommandCenterSnapshotDTO
            {
                GeneratedAtUtc = DateTime.UtcNow,

                GlobalRiskScore = globalRisk,

                CriticalIncidentCount = zones.Count(IsCritical),

                HighIncidentCount = zones.Count(IsHigh),

                ModerateIncidentCount = zones.Count(IsModerate),
                    
                TotalActiveConnections = zones.Sum(z => z.ActiveConnections),

                Summary =
                    zones.Count == 0
                        ? "No active operational incident detected in Wallonia."
                        : officialZoneCount > 0
                            ? $"{zones.Count} active operational zone(s), " +
                              $"{officialZoneCount} affected by official " +
                              $"emergency intelligence."
                            : $"{zones.Count} active operational zone(s) detected."
            };
        }

        public async Task<List<CrowdAlertCluster>> GetIncidentsAsync()
        {
            return await _http.GetFromJsonAsync<List<CrowdAlertCluster>>("commandcenter/incidents") ?? new();
        }

        public async Task<List<DecisionActionDTO>> GetDecisionActionsAsync()
        {
            return await _http.GetFromJsonAsync<List<DecisionActionDTO>>(
                "commandcenter/actions") ?? new();
        }

        public async Task<List<RiskZoneDTO>> GetRiskZonesAsync(CancellationToken ct = default)
        {
            return await _http.GetFromJsonAsync<List<RiskZoneDTO>>("commandcenter/risk-zones", ct) ?? new List<RiskZoneDTO>();
        }
    }
}
