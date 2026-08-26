using FluentValidation;
using Severity = MudBlazor.Severity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class AddUser
{
    [Inject] private IHttpService HttpSvc { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigator { get; set; } = default!;
    [Inject] private ILogger<AddUser> Logger { get; set; } = default!;

    private MudForm _form = default!;
    private readonly User _user = new();
    private readonly UserFluentValidator _validator = new();
    private bool _isValid;
    private bool _saving;

    private List<ListOption> _roles = new();
    private IReadOnlyCollection<int> _selectedRoleIds = new HashSet<int>();
    private string? _signatureFileName;
    private UserData _currentUser = new();

    // Signatures are small images; cap the upload so a huge file can't be streamed in.
    private const long MaxSignatureSize = 5 * 1024 * 1024; // 5 MB

    protected override void OnInitialized() => HttpSvc.Initialize(AppConstants.HttpSchemes.Core);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _currentUser = await Session.GetCurrentUser();

        if (!await Permissions.Has(Permission.CreateUser))
        {
            Snackbar.Add("You do not have permission to create users.", Severity.Warning);
            Navigator.NavigateTo("/users");
            return;
        }

        await LoadRoles();
        StateHasChanged();
    }

    private async Task LoadRoles()
    {
        try
        {
            var (status, result, message) = await HttpSvc.GetAsync<ApiResponse<List<ListOption>>>("api/listoptions/1?type=role");
            if (!status || result?.Data == null)
            {
                Snackbar.Add(message ?? "Could not load roles", Severity.Info);
                return;
            }
            _roles = result.Data;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex.Message);
        }
    }

    private async Task OnSignatureSelected(IBrowserFile? file)
    {
        if (file is null) return;
        try
        {
            using var stream = file.OpenReadStream(MaxSignatureSize);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            _user.Signature = Convert.ToBase64String(ms.ToArray());
            _signatureFileName = file.Name;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex.Message);
            Snackbar.Clear();
            Snackbar.Add("Could not read the signature file. " + ex.Message, Severity.Error);
        }
    }

    private async Task Submit()
    {
        await _form.ValidateAsync();

        // Build the Roles array (a MudSelect can't bind directly to UserRole[]) and stamp org context
        // so the whole DTO can be validated and posted in one shot.
        _user.Roles = _selectedRoleIds.Select(id =>
        {
            var option = _roles.FirstOrDefault(r => r.Id == id);
            return new UserRole { RoleId = id, RoleName = option?.Name ?? string.Empty, OrganisationId = OrgId };
        }).ToArray();
        _user.OrganisationId = OrgId;
        _user.CreatedBy = CreatedBy;

        var result = _validator.Validate(_user);
        if (!result.IsValid)
        {
            Snackbar.Clear();
            Snackbar.Add(string.Join(", ", result.Errors.Select(e => e.ErrorMessage)), Severity.Warning);
            return;
        }

        _saving = true;
        try
        {
            var (status, response, message) = await HttpSvc.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Post, "api/users", _user);
            if (!status)
            {
                Snackbar.Clear();
                Snackbar.Add("Failed to create user. " + message, Severity.Error);
                return;
            }

            Snackbar.Clear();
            Snackbar.Add(response?.Message ?? "User created successfully", Severity.Success);
            Navigator.NavigateTo("/users");
        }
        finally
        {
            _saving = false;
        }
    }

    private int OrgId => int.TryParse(_currentUser.OrgId, out var v) ? v : 0;
    private int CreatedBy => int.TryParse(_currentUser.UserId, out var v) ? v : 0;
}

/// <summary>
/// FluentValidation rules for the create-user form. Wired into <see cref="MudBlazor.MudForm"/>
/// via <see cref="ValidateValue"/> for live per-field feedback, and re-run in full on submit.
/// </summary>
public class UserFluentValidator : AbstractValidator<User>
{
    public UserFluentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Full name is required");
        RuleFor(x => x.Username).NotEmpty().WithMessage("Username is required").MinimumLength(3);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required").EmailAddress();
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required").MinimumLength(6);
        RuleFor(x => x.Department).NotEmpty().WithMessage("Department is required");
        RuleFor(x => x.Grade).NotEmpty().WithMessage("Grade is required");
        RuleFor(x => x.CountryCode).GreaterThan(0).WithMessage("Country code is required");
        RuleFor(x => x.PhoneNumber).GreaterThan(0).WithMessage("Phone number is required");
        RuleFor(x => x.Signature).NotEmpty().WithMessage("Upload a signature file");
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Select at least one role");
    }

    public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
    {
        var result = await ValidateAsync(
            ValidationContext<User>.CreateWithOptions((User)model, x => x.IncludeProperties(propertyName)));
        return result.IsValid ? Array.Empty<string>() : result.Errors.Select(e => e.ErrorMessage);
    };
}
