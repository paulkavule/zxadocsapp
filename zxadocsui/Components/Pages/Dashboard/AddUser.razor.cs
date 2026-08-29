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
    [Inject] private IUserRoleClientService RolesApi { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigator { get; set; } = default!;
    [Inject] private ILogger<AddUser> Logger { get; set; } = default!;

    private MudForm _form = default!;
    private readonly User _user = new();
    private readonly UserFluentValidator _validator = new();
    private bool _isValid;
    private bool _saving;

    private List<UserRole> _roles = new();
    private IReadOnlyCollection<int> _selectedRoleIds = new HashSet<int>();
    private string? _signatureFileName;

    // The signature is uploaded as a file and only its reference travels on the user payload
    // (ZD-111). Signature is varchar(250) server-side, so base64 never fitted; every reader —
    // GetSignatureByReference, CreateDocument — already treats the column as a path.
    private byte[]? _signatureBytes;
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
        // The org's real roles, not list options: no ListOption of type "role" has ever existed, so
        // the old lookup always came back empty and the form could never satisfy its own
        // "select at least one role" rule. This client takes the organisation from the token.
        var (ok, roles, error) = await RolesApi.List();
        if (!ok)
        {
            Snackbar.Add(error ?? "Could not load roles", Severity.Info);
            return;
        }

        _roles = roles.ToList();
    }

    private async Task OnSignatureSelected(IBrowserFile? file)
    {
        if (file is null) return;
        try
        {
            using var stream = file.OpenReadStream(MaxSignatureSize);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            _signatureBytes = ms.ToArray();
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
            var role = _roles.FirstOrDefault(r => r.RoleId == id);
            return new UserRole { RoleId = id, RoleName = role?.RoleName ?? string.Empty, OrganisationId = OrgId };
        }).ToArray();
        _user.OrganisationId = OrgId;
        _user.CreatedBy = CreatedBy;

        // Signature left the DTO, so the validator cannot check it; the file is checked here.
        if (_signatureBytes is null || _signatureBytes.Length == 0)
        {
            Snackbar.Clear();
            Snackbar.Add("Upload a signature file before creating the user.", Severity.Warning);
            return;
        }

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
            // ApiResponse<CreatedUser>: the endpoint returns the id and the reference the
            // signature upload is keyed on. Deserialising it as a string throws, and would report
            // a failure for a user that exists.
            var (status, response, message) = await HttpSvc.ExecuteRequestAsync<ApiResponse<CreatedUser>>(
                HttpVerb.Post, "api/users", _user);

            if (!status || response?.Data is null)
            {
                Snackbar.Clear();
                Snackbar.Add("Failed to create user. " + message, Severity.Error);
                return;
            }

            Snackbar.Clear();

            // The signature is a separate call now: it is a file, and POST /api/users/signature is
            // keyed on the reference that has only just come back. The user already exists at this
            // point, so a failure here is reported without pretending the create failed.
            var signatureUploaded = await UploadSignatureAsync(response.Data.UserReference);

            // The admin never sees the password, so say where the credentials went instead.
            Snackbar.Add(signatureUploaded
                    ? $"{_user.Name} was created. Sign-in instructions have been emailed to {_user.Email}."
                    : $"{_user.Name} was created and emailed sign-in instructions, but the signature "
                      + "could not be uploaded. Add it again from the user's profile.",
                signatureUploaded ? Severity.Success : Severity.Warning);

            Navigator.NavigateTo("/users");
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>
    /// Attaches the signature to a user that now exists. Returns false rather than throwing: the
    /// user has already been created, so the caller reports a partial success instead of a failure.
    /// </summary>
    private async Task<bool> UploadSignatureAsync(Guid userReference)
    {
        if (_signatureBytes is null || _signatureBytes.Length == 0)
            return false;

        var (status, _, message) = await HttpSvc.UploadUserSignatureAsync<ApiResponse<string>>(
            userReference, _signatureBytes, _signatureFileName ?? "signature.png");

        if (!status)
            Logger.LogDebug("Signature upload failed: {Message}", message);

        return status;
    }

    // MultiSelection shows the chosen values in the closed field; without this they render as ids.
    private string RoleName(int roleId) =>
        _roles.FirstOrDefault(role => role.RoleId == roleId)?.RoleName ?? roleId.ToString();

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
        RuleFor(x => x.Department).NotEmpty().WithMessage("Department is required");
        RuleFor(x => x.Grade).NotEmpty().WithMessage("Grade is required");
        RuleFor(x => x.CountryCode).GreaterThan(0).WithMessage("Country code is required");
        RuleFor(x => x.PhoneNumber).GreaterThan(0).WithMessage("Phone number is required");
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Select at least one role");
    }

    public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
    {
        var result = await ValidateAsync(
            ValidationContext<User>.CreateWithOptions((User)model, x => x.IncludeProperties(propertyName)));
        return result.IsValid ? Array.Empty<string>() : result.Errors.Select(e => e.ErrorMessage);
    };
}
