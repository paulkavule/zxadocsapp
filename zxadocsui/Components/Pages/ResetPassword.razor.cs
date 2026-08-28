using FluentValidation;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using Severity = MudBlazor.Severity;

namespace zxadocsui.Components.Pages;

/// <summary>
/// The page the emailed reset link opens (ZD-106). Anonymous, and it sends no bearer token: the
/// single-use token in the query string is the only thing authorising the change.
/// </summary>
public partial class ResetPassword
{
    [Inject] private IHttpService HttpSvc { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Navigator { get; set; } = default!;
    [Inject] private ILogger<ResetPassword> Logger { get; set; } = default!;

    /// <summary>The token from ?token=... — never rendered, logged, or echoed into a message.</summary>
    [SupplyParameterFromQuery(Name = "token")]
    private string? Token { get; set; }

    private MudForm _form = default!;
    private readonly ResetModel _model = new();
    private readonly ResetPasswordValidator _validator = new();
    private bool _isValid;
    private bool _saving;

    /// <summary>What went wrong, if anything. Never contains the token.</summary>
    private string? _failure;

    /// <summary>
    /// False both when the link carried no token and after the server has refused one. In either
    /// case the form is not rendered, so there is nothing to submit against a dead link.
    /// </summary>
    private bool HasToken => !string.IsNullOrWhiteSpace(Token) && !_tokenRefused;

    private bool _tokenRefused;

    protected override void OnInitialized() => HttpSvc.Initialize(AppConstants.HttpSchemes.Core);

    private async Task Submit()
    {
        await _form.ValidateAsync();

        var result = _validator.Validate(_model);
        if (!result.IsValid)
        {
            Snackbar.Clear();
            Snackbar.Add(string.Join(", ", result.Errors.Select(e => e.ErrorMessage)), Severity.Warning);
            return;
        }

        // Blocks the double submit: a reset token is single use, so a second click would spend it
        // and come back as "invalid or expired" on a password that had just been accepted.
        if (_saving) return;
        _saving = true;
        _failure = null;

        try
        {
            var (status, response, message) = await HttpSvc.ExecuteRequestAsync<ApiResponse<string>>(
                HttpVerb.Post, "api/users/password/reset",
                new { token = Token, newPassword = _model.NewPassword });

            if (!status)
            {
                // The server returns one indistinguishable message for unknown, used and expired
                // tokens. Show it as-is rather than guessing which of the three it was.
                _failure = message ?? "This link is invalid or has expired.";

                // A rejected token cannot become valid, so stop offering the form.
                _tokenRefused = true;
                return;
            }

            Snackbar.Clear();
            Snackbar.Add("Your password is set. Sign in with it now.", Severity.Success);
            Navigator.NavigateTo("/");
        }
        catch (Exception ex)
        {
            // The token must not reach the log, so only the exception's own text is written.
            Logger.LogDebug(ex.Message);
            _failure = "Something went wrong setting your password. Try the link again.";
        }
        finally
        {
            _saving = false;
        }
    }

    private sealed class ResetModel
    {
        public string NewPassword { get; set; } = string.Empty;
        public string Confirm { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mirrors the server's policy (ZD-103) and adds the confirm-field match, which is a UI concern
    /// the server cannot check. The server is the authority: it enforces the minimum length and
    /// refuses a password equal to the current one, which this cannot know.
    /// </summary>
    private sealed class ResetPasswordValidator : AbstractValidator<ResetModel>
    {
        public ResetPasswordValidator()
        {
            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("Enter a new password")
                .MinimumLength(8).WithMessage("Use at least 8 characters")
                .Matches("[A-Z]").WithMessage("Include an upper-case letter")
                .Matches("[a-z]").WithMessage("Include a lower-case letter")
                .Matches("[0-9]").WithMessage("Include a digit");

            RuleFor(x => x.Confirm)
                .Equal(x => x.NewPassword).WithMessage("The two passwords do not match");
        }

        public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
        {
            var result = await ValidateAsync(
                ValidationContext<ResetModel>.CreateWithOptions((ResetModel)model,
                    x => x.IncludeProperties(propertyName)));
            return result.IsValid ? Array.Empty<string>() : result.Errors.Select(e => e.ErrorMessage);
        };
    }
}
