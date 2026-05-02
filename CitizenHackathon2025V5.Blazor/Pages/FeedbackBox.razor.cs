using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class FeedbackBox
    {
        private string _content = string.Empty;
        private bool _isSending;
        private string? _successMessage;
        private string? _errorMessage;
        private List<ClientMessageDTO> _messages = new();
        private HashSet<int> _deletingIds = new();

        protected override async Task OnInitializedAsync()
        {
            _messages = await MessageService.GetLatestAsync(20) ?? new();
        }

        private async Task SendAsync()
        {
            _successMessage = null;
            _errorMessage = null;

            var content = _content?.Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                _errorMessage = "Please enter a message before sending.";
                return;
            }

            if (content.Length < 3)
            {
                _errorMessage = "Your feedback is too short.";
                return;
            }

            try
            {
                _isSending = true;

                var created = await MessageService.PostAsync(content);

                if (created is null)
                {
                    _errorMessage = "The feedback could not be saved.";
                    return;
                }

                _messages.Insert(0, created);
                _messages = _messages
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(20)
                    .ToList();

                _content = string.Empty;
                _successMessage = "Thank you. Your feedback has been sent.";
            }
            catch (Exception ex)
            {
                _errorMessage = $"An error occurred while sending feedback: {ex.Message}";
            }
            finally
            {
                _isSending = false;
            }
        }

        private async Task DeleteAsync(int id)
        {
            if (!CanModerate)
                return;

            _errorMessage = null;
            _successMessage = null;

            try
            {
                _deletingIds.Add(id);

                var ok = await MessageService.DeleteAsync(id);

                if (!ok)
                {
                    _errorMessage = "Comment not found or already deleted.";
                    return;
                }

                _messages = _messages.Where(x => x.Id != id).ToList();
                _successMessage = "Comment deleted successfully.";
            }
            catch (Exception ex)
            {
                _errorMessage = $"Deletion failed: {ex.Message}";
            }
            finally
            {
                _deletingIds.Remove(id);
            }
        }
    }
}
































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.