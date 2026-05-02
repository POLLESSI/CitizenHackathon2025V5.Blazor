using CitizenHackathon2025V5.Blazor.Client.Models;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class AdminProfanity
    {
        private List<ProfanityWordModel> _items = new();
        private HashSet<int> _busyIds = new();

        private bool _loading;
        private bool _saving;
        private string? _success;
        private string? _error;

        private int _page = 1;
        private int _pageSize = 20;
        private int _totalCount;
        private int _totalPages;

        private string _languageCode = string.Empty;
        private string _search = string.Empty;

        private CreateProfanityWordModel _create = new()
        {
            LanguageCode = "fr",
            Weight = 10,
            Active = true
        };

        protected override async Task OnInitializedAsync()
        {
            await LoadAsync();
        }

        private async Task ReloadAsync()
        {
            _page = 1;
            await LoadAsync();
        }

        private async Task OnFilterChangedAsync()
        {
            _page = 1;
            await LoadAsync();
        }

        private async Task OnPageSizeChangedAsync()
        {
            _page = 1;
            await LoadAsync();
        }

        private async Task PrevPageAsync()
        {
            if (_page > 1)
            {
                _page--;
                await LoadAsync();
            }
        }

        private async Task NextPageAsync()
        {
            if (_page < _totalPages)
            {
                _page++;
                await LoadAsync();
            }
        }

        private async Task LoadAsync()
        {
            try
            {
                _loading = true;
                _error = null;

                var result = await ProfanityAdminService.GetPagedAsync(
                    _page,
                    _pageSize,
                    _languageCode,
                    _search);

                _items = result?.Items?.ToList() ?? new();
                _totalCount = result?.TotalCount ?? 0;
                _totalPages = result?.TotalPages ?? 0;
            }
            catch (Exception ex)
            {
                _error = $"Loading failed: {ex.Message}";
            }
            finally
            {
                _loading = false;
            }
        }

        private async Task CreateAsync()
        {
            try
            {
                _saving = true;
                _error = null;
                _success = null;

                if (string.IsNullOrWhiteSpace(_create.Word))
                {
                    _error = "Word is required.";
                    return;
                }

                var currentLanguage = _create.LanguageCode;

                var created = await ProfanityAdminService.CreateAsync(_create);

                _create = new CreateProfanityWordModel
                {
                    LanguageCode = currentLanguage,
                    Weight = 10,
                    Active = true
                };

                _success = $"Word '{created?.Word}' added successfully.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _error = $"Create failed: {ex.Message}";
            }
            finally
            {
                _saving = false;
            }
        }

        private async Task ToggleAsync(ProfanityWordModel item)
        {
            try
            {
                _busyIds.Add(item.Id);
                _error = null;
                _success = null;

                await ProfanityAdminService.SetActiveAsync(item.Id, !item.Active);

                item.Active = !item.Active;
                _success = "Status updated successfully.";
            }
            catch (Exception ex)
            {
                _error = $"Status update failed: {ex.Message}";
            }
            finally
            {
                _busyIds.Remove(item.Id);
            }
        }

        private async Task DeleteAsync(int id)
        {
            try
            {
                _busyIds.Add(id);
                _error = null;
                _success = null;

                await ProfanityAdminService.DeleteAsync(id);

                _success = "Word deleted successfully.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _error = $"Delete failed: {ex.Message}";
            }
            finally
            {
                _busyIds.Remove(id);
            }
        }
    }
}





























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.