using CitizenHackathon2025.Blazor.DTOs.Diagnostics;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class AotDiagnostics
    {
        private bool _loading = true;
        private string? _error;
        private WasmDiagnosticReport? _report;

        protected override async Task OnInitializedAsync()
        {
            await RunAsync();
        }

        private async Task RunAsync()
        {
            _loading = true;
            _error = null;

            try
            {
                _report = await JS.InvokeAsync<WasmDiagnosticReport>("outzenDiagnostics.runWasmDiagnostic");
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }

            _loading = false;
        }

        private static string FormatBytes(long? bytes)
        {
            if (bytes is null or <= 0) return "N/A";

            var value = bytes.Value;

            if (value < 1024) return $"{value} B";
            if (value < 1024 * 1024) return $"{value / 1024.0:0.0} KB";

            return $"{value / 1024.0 / 1024.0:0.00} MB";
        }

        private static string ShortName(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";

            try
            {
                var uri = new Uri(url);
                return uri.AbsolutePath;
            }
            catch
            {
                return url;
            }
        }
    }
}
















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.