namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class UserHubMethods
    {
        /// <summary>Hub path. Must match the MapHub on the Blazor side.</summary>
        public const string HubPath = "/hubs/userHub";

        /// <summary>Events sent by the server to clients.</summary>
        public static class ToClient
        {
            /// <summary>
            /// Notification that a user has just been registered.
            /// Corresponds to: Clients.All.SendAsync("UserRegistered", email)
            /// </summary>
            public const string UserRegistered = "UserRegistered";
        }

        /// <summary>Methods invoked by clients to the server.</summary>
        public static class FromClient
        {
            /// <summary>
            /// Request to issue a registration notification.
            /// Signature hub: Task NotifyUserRegistered(string email)
            /// </summary>
            public const string NotifyUserRegistered = "NotifyUserRegistered";
        }
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.