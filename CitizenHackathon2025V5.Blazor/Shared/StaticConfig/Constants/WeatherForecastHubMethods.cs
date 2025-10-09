namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class WeatherForecastHubMethods
    {
        /// <summary>
        /// Hub path (must match the MapHub on the Blazor side).
        /// Adapt if you use another path (e.g.: "/hubs/weatherForecastHub").
        /// </summary>
        public const string HubPath = "/hubs/weatherHub";

        /// <summary>Events sent by the server to clients.</summary>
        public static class ToClient
        {
            /// <summary>
            /// Broadcasting a new weather forecast (payload string/JSON).
            /// Corresponds to: Clients.All.SendAsync("NewWeatherForecast", message)
            /// </summary>
            public const string NewWeatherForecast = "NewWeatherForecast";

            /// <summary>
            /// Generic notification / message (payload string).
            /// Corresponds to: Clients.All.SendAsync("ReceiveForecast", message)
            /// </summary>
            public const string ReceiveForecast = "ReceiveForecast";
        }

        /// <summary>Methods invoked by clients on the hub.</summary>
        public static class FromClient
        {
            /// <summary>
            /// Asks the server to broadcast a new forecast.
            /// Signature hub: Task RefreshWeatherForecast(string message)
            /// </summary>
            public const string RefreshWeatherForecast = "RefreshWeatherForecast";

            /// <summary>
            /// Sending a generic notification.
            /// Hub signature: Task Notify(string message)
            /// </summary>
            public const string Notify = "Notify";
        }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.