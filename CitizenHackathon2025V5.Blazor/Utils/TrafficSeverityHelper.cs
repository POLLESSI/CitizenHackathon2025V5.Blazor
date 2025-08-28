using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Enums;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class TrafficSeverityHelper
    {
        public static string GetColor(int level) => level switch
        {
            1 => "text-green-500",
            2 => "text-yellow-500",
            3 => "text-red-500",
            _ => "text-gray-400"
        };

        public static string GetIcon(int level) => level switch
        {
            1 => "🟢", // or <i class="bi bi-circle-fill text-success"></i>
            2 => "🟡",
            3 => "🔴",
            _ => "⚪"
        };

        public static string GetLabel(int level) => level switch
        {
            1 => "Smooth traffic",
            2 => "Slow traffic",
            3 => "Blockage / Incident",
            _ => "Unknown"
        };
    }
}











































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.