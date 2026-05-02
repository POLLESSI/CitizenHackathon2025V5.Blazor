using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CitizenHackathon2025.Blazor.DTOs.Diagnostics
{
    public sealed class WasmDiagnosticReport
    {
        public WasmAccessResult? Wasm { get; set; }

        public List<ResourceDiagnosticItem> Resources { get; set; } = new();

        public long TotalBytes { get; set; }
        public long FrameworkBytes { get; set; }
        public long JavaScriptBytes { get; set; }
        public long CssBytes { get; set; }

        public int SignalRScriptCount { get; set; }
        public int SignalRConnectionCount { get; set; }

        public bool HasLeaflet { get; set; }
        public bool HasMarkerCluster { get; set; }
        public bool HasOutZenInterop { get; set; }
    }
    public sealed class ResourceDiagnosticItem
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public long TransferSize { get; set; }
        public long EncodedBodySize { get; set; }
        public long DecodedBodySize { get; set; }
        public double DurationMs { get; set; }
        public string? CacheMode { get; set; }
    }
}












































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.