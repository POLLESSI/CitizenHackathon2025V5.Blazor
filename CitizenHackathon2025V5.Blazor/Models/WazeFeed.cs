namespace CitizenHackathon2025V5.Blazor.Client.Models
{
#nullable disable
    public class WazeFeed
    {
        public List<WazeJam> Jams { get; set; } = new();
        public List<WazeAlert> Alerts { get; set; } = new();
    }

    public class WazeJam
    {
        public string Road { get; set; }
        public double Speed { get; set; }
        public string Description { get; set; }
    }

    public class WazeAlert
    {
        public string Type { get; set; }
        public string Message { get; set; }
    }
}








































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




