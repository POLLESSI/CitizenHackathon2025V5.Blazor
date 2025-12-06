namespace CitizenHackathon2025V5.Blazor.Client.Shared.Options
{
    public class BaseOptions
    {
        public string TimeZone { get; set; } = "Europe/Brussels";
        public string LocationName { get; set; } = string.Empty;
        public int Hour { get; set; }
        public int Minute { get; set; }
    }
    public class SharedOptions : BaseOptions
    {
        public new string TimeZone { get; set; } = "Europe/Brussels";
        public new string LocationName { get; set; } = "Default Location";
        public new int Hour { get; set; } = 0;
        public new int Minute { get; set; } = 0;

        // Other features specific to SharedOptions...
    }
}
