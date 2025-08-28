//using CitizenHackathon2025V4.Blazor.Client.Common.SignalR;
//using Microsoft.AspNetCore.Components;
//using System.Net.Http.Json;

//namespace CitizenHackathon2025V5.Blazor.Client.Pages.GenericEntities
//{
//    public partial class GenericEntityList<TModel> : SignalRComponentBase<TModel>
//        where TModel : class
//    {
//        [Parameter] public RenderFragment<TModel>? ItemTemplate { get; set; }
//        [Parameter] public EventCallback<TModel> ItemSelected { get; set; }
//        [Parameter] public string? ApiEndpoint { get; set; } // ex: "/api/genericentities"
//        [Parameter] public string? HubRoute { get; set; }    // ex: "/hubs/genericentities"
//        [Parameter] public string? HubEvent { get; set; }    // ex: "ReceiveGenericEntity"

//        [Inject] public HttpClient Http { get; set; }

//        protected override string HubUrl
//            => HubRoute ?? throw new InvalidOperationException("HubRoute parameter is required.");

//        protected override string HubEventName
//            => HubEvent ?? throw new InvalidOperationException("HubEvent parameter is required.");

//        /// <summary>
//        /// Initial data loading from API (ADO.NET server-side)
//        /// </summary>
//        protected override async Task<List<TModel>> LoadDataAsync()
//        {
//            if (string.IsNullOrWhiteSpace(ApiEndpoint))
//                throw new InvalidOperationException("ApiEndpoint parameter is required.");

//            var result = await Http.GetFromJsonAsync<List<TModel>>(ApiEndpoint);
//            return result ?? new List<TModel>();
//        }

//        /// <summary>
//        /// Custom reaction when receiving a new item via SignalR
//        /// </summary>
//        protected override async Task OnSignalRMessageReceivedAsync(TModel item)
//        {
//            // Example: console display (or toast UI later)
//            Console.WriteLine($"[SignalR] New {typeof(TModel).Name} received: {item}");
//            await Task.CompletedTask;
//        }

//        protected void OnItemSelected(TModel item)
//        {
//            if (ItemSelected.HasDelegate)
//                ItemSelected.InvokeAsync(item);
//        }
//    }
//}













































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.