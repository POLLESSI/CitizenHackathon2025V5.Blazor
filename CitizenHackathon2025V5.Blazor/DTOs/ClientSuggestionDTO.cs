namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientSuggestionDTO
    {
    #nullable disable
        public int Id { get; set; }
        public string Title { get; set; } = "";             // Display title
        public int User_Id { get; set; }
        public DateTime DateSuggestion { get; set; }
        public string OriginalPlace { get; set; } = "";     // Source place (where crowd is high)
        public string SuggestedAlternatives { get; set; } = "";
        public string Reason { get; set; } = "";            // Why this suggestion
        public double DistanceKm { get; set; }              // Distance to alternative
        public double Latitude { get; set; }                // Alt coordinates
        public double Longitude { get; set; }
        public string? Context { get; set; }
    }
}

























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.