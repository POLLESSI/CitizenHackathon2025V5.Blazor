namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class HubUrlBuilder : IHubUrlBuilder
    {
        private readonly IConfiguration _cfg;

        public HubUrlBuilder(IConfiguration cfg) => _cfg = cfg;

        public string Build(string hubPathOrRelative)
        {
            var apiBase = (_cfg["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');
            var hubBase = (_cfg["SignalR:HubBase"] ?? apiBase).TrimEnd('/');

            var p = (hubPathOrRelative ?? "").Trim();

            // accepte: "trafficHub", "/trafficHub", "hubs/trafficHub", "/hubs/trafficHub"
            p = p.TrimStart('/');
            if (p.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring("hubs/".Length);

            // résultat: https://host/hubs/{p}
            return $"{hubBase}/hubs/{p}";
        }
    }
}
