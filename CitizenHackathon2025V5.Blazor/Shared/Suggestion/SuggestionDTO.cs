namespace CitizenHackathon2025V5.Blazor.Client.Shared.Suggestion
{
    public class SuggestionDTO
    {
#nullable disable
        public int Id { get; set; }
        public string Title { get; set; } = "";             // Display title
        public int User_Id { get; set; }
        public DateTime Date { get; set; }
        public string OriginalPlace { get; set; } = "";     // Source place (where crowd is high)
        public string SuggestedAlternative { get; set; } = "";
        public string Reason { get; set; } = "";            // Why this suggestion
        public double DistanceKm { get; set; }              // Distance to alternative
        public double Latitude { get; set; }                // Alt coordinates
        public double Longitude { get; set; }
    }
}
