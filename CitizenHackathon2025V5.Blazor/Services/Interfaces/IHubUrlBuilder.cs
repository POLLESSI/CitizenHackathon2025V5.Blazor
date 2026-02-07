namespace CitizenHackathon2025V5.Blazor.Client.Services.Interfaces
{
    public interface IHubUrlBuilder
    {
        string Build(string hubRelativePath); // ex: "crowdHub" -> "https://localhost:7254/hubs/crowdHub"
    }

}
