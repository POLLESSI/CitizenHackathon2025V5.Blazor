# OutZen - CitizenHackathon2025

**OutZen** — Blazor WebAssembly application in .NET 8, connected to an ASP.NET Core backend.
It helps you better plan your outings by displaying in real time:

- Traffic (Waze data)
- Crowd size
- Weather conditions
- Contextual suggestions

---

## ⚙️ Technologies

- .NET 8 - Blazor WebAssembly (AOT)
- SignalR (realtime updates)
- Leaflet.js (interactive map)
- Chart.js (affluence graph)
- OpenWeatherMap API
- Waze Traffic Simulation
- Blazored.Toast

---

## 🔧 Architecture

- **Client** : Blazor WebAssembly (.NET 8), AOT, PWA-ready
- **Serveur** : ASP.NET Core Web API (SignalR, CQRS, Dapper, MediatR)
- **Real-time**: SignalR for crowds, traffic and notifications
- **Integrations**:
  - OpenWeather
  - API Waze (mocked or real)
  - GPT-4o-mini (Azure OpenAI Service)
- **Design** : Responsive, animations canvas, Leaflet maps

## ⚙️ Build / Run

1. Opens in Visual Studio 2022 or VS Code.
2. Run the `CitizenHackathon2025.API` project on `https://localhost:7254/`
3. Launches `CitizenHackathon2025V4.Blazor.Client` (WASM) in `Release` mode for AOT.

## 🚀 Deployment

To deploy the app:

```bash
dotnet clean
dotnet build -c Release
dotnet publish -c Release


























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/