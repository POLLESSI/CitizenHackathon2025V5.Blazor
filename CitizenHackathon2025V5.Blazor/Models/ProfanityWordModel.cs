namespace CitizenHackathon2025V5.Blazor.Client.Models
{
    public sealed class ProfanityWordModel
    {
        public int Id { get; set; }
        public string Word { get; set; } = string.Empty;
        public string NormalizedWord { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = "fr";
        public int Weight { get; set; } = 1;
        public bool IsRegex { get; set; }
        public bool Active { get; set; } = true;
        public string? Category { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
