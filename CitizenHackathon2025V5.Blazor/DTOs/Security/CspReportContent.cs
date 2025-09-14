using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.DTOs.Security
{
    public class CspReportContent
    {
#nullable disable
        [JsonPropertyName("document-uri")]
        public string DocumentUri { get; set; }

        [JsonPropertyName("referrer")]
        public string Referrer { get; set; }

        [JsonPropertyName("violated-directive")]
        public string ViolatedDirective { get; set; }

        [JsonPropertyName("effective-directive")]
        public string EffectiveDirective { get; set; }

        [JsonPropertyName("original-policy")]
        public string OriginalPolicy { get; set; }

        [JsonPropertyName("disposition")]
        public string Disposition { get; set; }

        [JsonPropertyName("blocked-uri")]
        public string BlockedUri { get; set; }

        [JsonPropertyName("line-number")]
        public int? LineNumber { get; set; }

        [JsonPropertyName("column-number")]
        public int? ColumnNumber { get; set; }

        [JsonPropertyName("source-file")]
        public string SourceFile { get; set; }

        [JsonPropertyName("status-code")]
        public int? StatusCode { get; set; }

        [JsonPropertyName("script-sample")]
        public string ScriptSample { get; set; }
    }
}













































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/




