namespace CitizenHackathon2025V5.Blazor.Client.Models
{
    public sealed class CreateProfanityWordModel
    {
        public string Word { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = "fr";
        public int Weight { get; set; } = 10;
        public bool IsRegex { get; set; }
        public string? Category { get; set; }
        public bool Active { get; set; } = true;
    }
}