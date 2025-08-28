using System;
using System.Text;
using System.Text.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Auths
{
    public static class JwtParser
    {
        public static JwtPayload DecodePayload(string token)
        {
            var parts = token.Split('.');
            if (parts.Length != 3) throw new ArgumentException("Invalid JWT");

            var payload = parts[1];
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(payload);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<JwtPayload>(json)!;
        }
    }

    public class JwtPayload
    {
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
