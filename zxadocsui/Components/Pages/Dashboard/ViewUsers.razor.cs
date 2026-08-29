using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class ViewUsers
{
    [Inject] private IHttpService HttpSvc { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<ViewUsers> Logger { get; set; } = default!;

    private List<UserSummary> _users = new();
    private bool _loading = true;
    private string _searchTerm = string.Empty;

    // Resend is gated on the same permission the create action uses.
    private bool _canResendInvite;

    // Editing sets roles, so it needs the permission the update endpoint enforces (ZD-114).
    private bool _canEdit;

    // Id of the user currently being re-invited, so only that row's button shows a busy state.
    private int _resending;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        // Any user-administration permission opens the list; the server enforces the same set.
        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.ViewUsers, Permission.CreateUser, Permission.ManageUsers))
        {
            Snackbar.Add("You do not have permission to view users.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        _canResendInvite = await Permissions.Has(Permission.CreateUser);
        _canEdit = await Permissions.Has(Permission.ManageUsers);

        HttpSvc.Initialize(AppConstants.HttpSchemes.Core);
        await LoadUsers();
        StateHasChanged();
    }

    private async Task LoadUsers()
    {
        _loading = true;
        try
        {
            var (status, result, message) = await HttpSvc.GetAsync<ApiPaginatedResponse<List<UserSummary>>>("api/users");
            if (!status || result?.Data == null)
            {
                Snackbar.Clear();
                Snackbar.Add(message ?? "Could not load users", Severity.Info);
                return;
            }
            _users = result.Data;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex.Message);
            Snackbar.Clear();
            Snackbar.Add($"Failed to load users. {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// A user who still holds the password they were provisioned with. The API reports this as
    /// Status PENDING; resend-invite is refused with a 409 for anyone else.
    /// </summary>
    private static bool IsPending(UserSummary user) =>
        string.Equals(user.Status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private async Task ResendInvite(UserSummary user)
    {
        _resending = user.Id;
        try
        {
            var (status, response, message) = await HttpSvc.ExecuteRequestAsync<ApiResponse<string>>(
                HttpVerb.Post, $"api/users/{user.Id}/resend-invite");

            Snackbar.Clear();
            if (!status)
            {
                Snackbar.Add($"Could not resend the invitation. {message}", Severity.Error);
                return;
            }

            Snackbar.Add($"A new invitation has been emailed to {user.Email}.", Severity.Success);

            // The temporary password changed, so reload rather than trusting the cached row.
            await LoadUsers();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex.Message);
            Snackbar.Clear();
            Snackbar.Add($"Could not resend the invitation. {ex.Message}", Severity.Error);
        }
        finally
        {
            _resending = 0;
        }
    }

    // Quick-filter across the visible columns for the toolbar search box.
    private Func<UserSummary, bool> _quickFilter => user =>
    {
        if (string.IsNullOrWhiteSpace(_searchTerm)) return true;
        var term = _searchTerm.Trim();
        return Contains(user.Name, term)
            || Contains(user.Username, term)
            || Contains(user.Email, term)
            || Contains(user.PhoneNumber.ToString(), term);
    };

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
