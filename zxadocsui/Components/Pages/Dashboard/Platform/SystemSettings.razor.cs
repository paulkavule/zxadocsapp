using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

/// <summary>
/// Deployment-wide settings for a System Admin (ZD-316). Secrets are never shown: the field starts
/// empty and only a typed value replaces the stored one. The API validates and audits every change.
/// </summary>
public partial class SystemSettings
{
    private static readonly string[] Categories = ["Payments", "PettyCash", "Notifications", "Passwords"];

    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<SettingValue> settings = new();
    private Dictionary<string, string> drafts = new();
    private string category = Categories[0];
    private bool loading = true;
    private string? saving;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!(await Permissions.GetSystemRoles()).Contains(SystemRole.SystemAdmin))
        {
            Snackbar.Add("System settings are available to System Admins only.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        await Load();
        StateHasChanged();
    }

    private async Task OnTabChanged(int index)
    {
        category = Categories[index];
        await Load();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<SettingValue>>>($"api/system/settings/{category}");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load settings.", Severity.Error);
                return;
            }

            settings = result.Data;
            drafts = settings.ToDictionary(s => s.Name, s => s.IsSecret ? string.Empty : s.Value);
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Save(SettingValue setting)
    {
        // A secret's empty field means "keep the stored value", not "clear it".
        if (setting.IsSecret && string.IsNullOrEmpty(drafts[setting.Name]))
        {
            Snackbar.Add($"Type a new value for {setting.Name} to replace it.", Severity.Info);
            return;
        }

        saving = setting.Name;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<string>>(
                HttpVerb.Put, $"api/system/settings/{category}/{setting.Name}", new { Value = drafts[setting.Name] });

            Snackbar.Add(ok ? $"{setting.Name} saved." : ErrorMessage.Extract(error) ?? "Failed to save.",
                ok ? Severity.Success : Severity.Error);
            if (ok) await Load();
        }
        finally
        {
            saving = null;
        }
    }
}
