namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public sealed class EmergencySourceCheckDTO
    {
        public string SourceCode { get; set; } =
            string.Empty;

        public int AlertCount { get; set; }

        public DateTimeOffset FetchedAtUtc { get; set; }

        public string? ETag { get; set; }

        public DateTimeOffset? LastModifiedUtc { get; set; }

        public bool IsRemoteProviderConfigured { get; set; }
    }
}