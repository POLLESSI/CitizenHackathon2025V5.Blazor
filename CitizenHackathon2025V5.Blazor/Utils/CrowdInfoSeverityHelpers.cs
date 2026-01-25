using System;
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Enums;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    /// <summary>
    /// Crowd severity helpers supporting TWO modes:
    ///  - Live mode (CrowdInfo): raw values potentially 0..10 / 0..100 -> bucketed to CrowdLevelEnum
    ///  - Calendar mode (CrowdInfoCalendar): ExpectedLevel 1..4 -> direct mapping to CrowdLevelEnum
    /// 
    /// Output is always an int representing the CrowdLevelEnum value.
    /// </summary>
    public static class CrowdInfoSeverityHelpers
    {
        // --------------------------------------------------------------------
        // Public API (recommended entry points)
        // --------------------------------------------------------------------

        /// <summary>
        /// Live (CrowdInfo) severity from ClientCrowdInfoDTO (expects dto.CrowdLevel is int).
        /// Buckets raw values to CrowdLevelEnum.
        /// </summary>
        public static int GetSeverity(ClientCrowdInfoDTO dto)
            => ToEnumLive(dto.CrowdLevel);

        /// <summary>
        /// Calendar severity from ClientCrowdInfoCalendarDTO (ExpectedLevel is 1..4).
        /// Direct mapping to CrowdLevelEnum.
        /// </summary>
        public static int GetSeverity(ClientCrowdInfoCalendarDTO dto)
            => ToEnumCalendar(dto.ExpectedLevel);

        /// <summary>
        /// Convenience: choose live bucketing for a raw numeric value.
        /// </summary>
        public static int GetSeverityLive(int level)
            => ToEnumLive(level);

        /// <summary>
        /// Convenience: choose calendar mapping for a raw expected level (1..4).
        /// </summary>
        public static int GetSeverityCalendar(int expectedLevel)
            => ToEnumCalendar(expectedLevel);

        /// <summary>
        /// Generic severity from int (defaults to live bucketing to preserve backward behavior).
        /// Use GetSeverityCalendar if you explicitly want 1..4 mapping.
        /// </summary>
        public static int GetSeverity(int level)
            => ToEnumLive(level);

        /// <summary>
        /// Generic severity from nullable int (defaults to live bucketing).
        /// </summary>
        public static int GetSeverity(int? level)
            => ToEnumLive(level.GetValueOrDefault());

        /// <summary>
        /// Generic severity from string.
        /// - If string is numeric => treated as live numeric (bucketed).
        /// - If string is "low/medium/high/critical" => maps directly to enum.
        /// </summary>
        public static int GetSeverity(string? crowdLevel)
            => ToEnumString(crowdLevel);

        // --------------------------------------------------------------------
        // UI helpers (color/icon/description)
        // These expect an int that is a CrowdLevelEnum value.
        // If you pass a raw live level, call GetSeverityLive(raw) first.
        // If you pass an ExpectedLevel 1..4, call GetSeverityCalendar(raw) first.
        // --------------------------------------------------------------------

        public static string GetColor(int severityEnum) => ((CrowdLevelEnum)ClampEnum(severityEnum)) switch
        {
            CrowdLevelEnum.Low => "#4CAF50",
            CrowdLevelEnum.Medium => "#FFC107",
            CrowdLevelEnum.High => "#FF5722",
            CrowdLevelEnum.Critical => "#D32F2F",
            _ => "#9E9E9E"
        };

        public static string GetIcon(int severityEnum) => ((CrowdLevelEnum)ClampEnum(severityEnum)) switch
        {
            CrowdLevelEnum.Low => "check-circle",
            CrowdLevelEnum.Medium => "triangle-exclamation",
            CrowdLevelEnum.High => "fire",
            CrowdLevelEnum.Critical => "ban",
            _ => "question"
        };

        public static string GetDescription(int severityEnum) => ((CrowdLevelEnum)ClampEnum(severityEnum)) switch
        {
            CrowdLevelEnum.Low => "Low attendance",
            CrowdLevelEnum.Medium => "Moderate crowd",
            CrowdLevelEnum.High => "High attendance",
            CrowdLevelEnum.Critical => "Critical attendance",
            _ => "Unknown level"
        };

        // --------------------------------------------------------------------
        // Conversion helpers (two modes)
        // --------------------------------------------------------------------

        /// <summary>
        /// Live mode conversion: buckets raw numeric values to CrowdLevelEnum.
        /// Keeps your previous behavior (<=3 low, <=6 medium, <=8 high, else critical).
        /// </summary>
        public static int ToEnumLive(int level)
        {
            // You can clamp raw if you want; keeping loose to allow 0..100 if needed.
            // level = Math.Clamp(level, 0, 100);

            if (level <= 3) return (int)CrowdLevelEnum.Low;
            if (level <= 6) return (int)CrowdLevelEnum.Medium;
            if (level <= 8) return (int)CrowdLevelEnum.High;
            return (int)CrowdLevelEnum.Critical;
        }

        /// <summary>
        /// Calendar mode conversion: direct mapping for expected levels 1..4.
        /// 1=Low, 2=Medium, 3=High, 4=Critical.
        /// </summary>
        public static int ToEnumCalendar(int? expectedLevel)
        {
            var lvl = expectedLevel.GetValueOrDefault(1);
            lvl = Math.Clamp(lvl, 1, 4);

            return lvl switch
            {
                1 => (int)CrowdLevelEnum.Low,
                2 => (int)CrowdLevelEnum.Medium,
                3 => (int)CrowdLevelEnum.High,
                _ => (int)CrowdLevelEnum.Critical
            };
        }

        /// <summary>
        /// String conversion:
        /// - numeric => live bucketing
        /// - "low/medium/high/critical" => direct enum
        /// </summary>
        public static int ToEnumString(string? crowdLevel)
        {
            if (string.IsNullOrWhiteSpace(crowdLevel))
                return (int)CrowdLevelEnum.Low;

            if (int.TryParse(crowdLevel, out int n))
                return ToEnumLive(n);

            return crowdLevel.Trim().ToLowerInvariant() switch
            {
                "low" => (int)CrowdLevelEnum.Low,
                "medium" => (int)CrowdLevelEnum.Medium,
                "high" => (int)CrowdLevelEnum.High,
                "critical" => (int)CrowdLevelEnum.Critical,
                _ => (int)CrowdLevelEnum.Low
            };
        }

        // --------------------------------------------------------------------
        // Internal helpers
        // --------------------------------------------------------------------

        /// <summary>
        /// Ensures value is within the known enum range.
        /// Adjust bounds if your enum values differ.
        /// </summary>
        private static int ClampEnum(int severityEnum)
        {
            // Assumes enum values are 1..4 or 0..3? adapt if needed.
            // We'll accept both: clamp to min/max of defined names by numeric range.
            // If your enum is Low=1..Critical=4: clamp 1..4.
            // If your enum is Low=0..Critical=3: clamp 0..3.
            // We'll detect by checking whether 0 is defined; safest: clamp to 0..4 then normalize unknowns to Low.
            var v = Math.Clamp(severityEnum, 0, 4);

            // If enum is 1..4, v=0 would be invalid. Keep it but UI methods map default to "#9E9E9E"/"Unknown".
            return v;
        }
    }
}



































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





