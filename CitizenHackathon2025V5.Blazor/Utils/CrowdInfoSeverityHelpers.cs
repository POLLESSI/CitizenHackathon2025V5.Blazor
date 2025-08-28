using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Enums;
using CitizenHackathon2025V5.Blazor.Client.Shared.CrowdInfo;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class CrowdInfoSeverityHelpers
    {
        /// <summary>
        /// Converts a CrowdLevel (string or int) to CrowdLevelEnum.
        /// </summary>
        public static CrowdLevelEnum ToEnum(string crowdLevel)
        {
            if (string.IsNullOrWhiteSpace(crowdLevel))
                return CrowdLevelEnum.Low;

            // If crowdLevel is numeric (0-10)
            if (int.TryParse(crowdLevel, out int levelValue))
            {
                if (levelValue <= 3) return CrowdLevelEnum.Low;
                if (levelValue <= 6) return CrowdLevelEnum.Medium;
                if (levelValue <= 8) return CrowdLevelEnum.High;
                return CrowdLevelEnum.Critical;
            }

            // If crowdLevel is text (Low, Medium, High, Critical)
            return crowdLevel.ToLower() switch
            {
                "low" => CrowdLevelEnum.Low,
                "medium" => CrowdLevelEnum.Medium,
                "high" => CrowdLevelEnum.High,
                "critical" => CrowdLevelEnum.Critical,
                _ => CrowdLevelEnum.Low
            };
        }

        /// <summary>
        /// Returns a hex color or CSS class associated with the level.
        /// </summary>
        public static string GetColor(CrowdLevelEnum level) =>
            level switch
            {
                CrowdLevelEnum.Low => "#4CAF50",       // Vert
                CrowdLevelEnum.Medium => "#FFC107",    // Jaune/Orange
                CrowdLevelEnum.High => "#FF5722",      // Orange foncé
                CrowdLevelEnum.Critical => "#D32F2F",  // Rouge vif
                _ => "#9E9E9E"                         // Gris neutre
            };

        /// <summary>
        /// Provides a suitable icon (Material/Iconify/Bootstrap).
        /// </summary>
        public static string GetIcon(CrowdLevelEnum level) =>
            level switch
            {
                CrowdLevelEnum.Low => "✅",         // Check
                CrowdLevelEnum.Medium => "⚠️",     // Warning
                CrowdLevelEnum.High => "🔥",       // Fire
                CrowdLevelEnum.Critical => "⛔",   // No Entry
                _ => "❓"
            };

        /// <summary>
        /// Descriptive text for user display.
        /// </summary>
        public static string GetDescription(CrowdLevelEnum level) =>
            level switch
            {
                CrowdLevelEnum.Low => "Low attendance",
                CrowdLevelEnum.Medium => "Moderate crowd",
                CrowdLevelEnum.High => "High attendance",
                CrowdLevelEnum.Critical => "Critical attendance",
                _ => "Unknown level"
            };

        /// <summary>
        /// Transforme directement un CrowdInfoDTO en CrowdLevelEnum.
        /// </summary>
        public static CrowdLevelEnum GetSeverity(CrowdInfoDTO dto)
            => ToEnum(dto.CrowdLevel);
    }
}




































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
