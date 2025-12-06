using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Enums;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class CrowdInfoSeverityHelpers
    {
        // string -> int (enum value)
        public static int ToEnum(string? crowdLevel)
        {
            if (string.IsNullOrWhiteSpace(crowdLevel))
                return (int)CrowdLevelEnum.Low;

            if (int.TryParse(crowdLevel, out int n))
                return ToEnum(n);

            return crowdLevel.Trim().ToLowerInvariant() switch
            {
                "low" => (int)CrowdLevelEnum.Low,
                "medium" => (int)CrowdLevelEnum.Medium,
                "high" => (int)CrowdLevelEnum.High,
                "critical" => (int)CrowdLevelEnum.Critical,
                _ => (int)CrowdLevelEnum.Low
            };
        }

        // int -> int (bucketing or direct casting)
        public static int ToEnum(int level)
        {
            if (level <= 3) return (int)CrowdLevelEnum.Low;
            if (level <= 6) return (int)CrowdLevelEnum.Medium;
            if (level <= 8) return (int)CrowdLevelEnum.High;
            return (int)CrowdLevelEnum.Critical;
        }

        public static string GetColor(int level) => ((CrowdLevelEnum)level) switch
        {
            CrowdLevelEnum.Low => "#4CAF50",
            CrowdLevelEnum.Medium => "#FFC107",
            CrowdLevelEnum.High => "#FF5722",
            CrowdLevelEnum.Critical => "#D32F2F",
            _ => "#9E9E9E"
        };

        public static string GetIcon(int level) => ((CrowdLevelEnum)level) switch
        {
            CrowdLevelEnum.Low => "check-circle",
            CrowdLevelEnum.Medium => "triangle-exclamation",
            CrowdLevelEnum.High => "fire",
            CrowdLevelEnum.Critical => "ban",
            _ => "question"
        };

        public static string GetDescription(int level) => ((CrowdLevelEnum)level) switch
        {
            CrowdLevelEnum.Low => "Low attendance",
            CrowdLevelEnum.Medium => "Moderate crowd",
            CrowdLevelEnum.High => "High attendance",
            CrowdLevelEnum.Critical => "Critical attendance",
            _ => "Unknown level"
        };

        // If ClientCrowdInfoDTO.CrowdLevel is **int**
        public static int GetSeverity(ClientCrowdInfoDTO dto) => ToEnum(dto.CrowdLevel);

        // If it's a **string** instead, use:
        // public static int GetSeverity(ClientCrowdInfoDTO dto) => ToEnum(dto.CrowdLevelString);
    }
}



































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





