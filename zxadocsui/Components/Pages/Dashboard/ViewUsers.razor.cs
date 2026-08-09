using Microsoft.AspNetCore.Components;
using MudBlazor;
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

    private List<QueryDto.UserQuery> _users = new();
    private bool _loading = true;
    private string _searchTerm = string.Empty;

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

        HttpSvc.Initialize(AppConstants.HttpSchemes.Core);
        await LoadUsers();
        StateHasChanged();
    }

    private async Task LoadUsers()
    {
        _loading = true;
        try
        {
            var (status, result, message) = await HttpSvc.GetAsync<ApiResponse<List<QueryDto.UserQuery>>>("api/users");
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

    // Quick-filter across the visible columns for the toolbar search box.
    private Func<QueryDto.UserQuery, bool> _quickFilter => user =>
    {
        if (string.IsNullOrWhiteSpace(_searchTerm)) return true;
        var term = _searchTerm.Trim();
        return Contains(user.Name, term)
            || Contains(user.UserName, term)
            || Contains(user.Email, term)
            || Contains(user.PhoneNumber, term);
    };

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
