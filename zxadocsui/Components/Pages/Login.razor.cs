using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.Pages.LoginComponents;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages;

public partial class Login
{
    [Inject] NavigationManager? navigator { set; get; }
    [Inject] ISnackbar? snackBar { set; get; }
    [Inject] AppState? state { set; get; }
    [Inject] IHttpService? httpSvc { get; set; }
    // [Inject] RequestContext? context { set; get; }
    [Inject] IUserSession? session { get; set; }
    [Inject] IAuthService? authSvc { get; set; }
    [Inject] IJSRuntime? jsSvc { set; get; }
    [Inject] SideDialogService? sideDialog { set; get; } = default!;

    MudForm _form;
    string _username = "pkavule", _password = "1234..34";
    bool _rememberPassword = true, _formValid;
    private string[] _errors = [];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            string token = await jsSvc!.InvokeAsync<string>("localStorage.getItem", "token");
            string refreshToken = await jsSvc!.InvokeAsync<string>("localStorage.getItem", "refresh_token");

            if (!string.IsNullOrEmpty(token))
            {
                state!.Set(AppConstants.StateKey.TOKEN, token);
                // session?.SetTokens(token, refreshToken ?? string.Empty);
            }

            if (!string.IsNullOrEmpty(refreshToken))
                state!.Set(AppConstants.StateKey.REFRESH_TOKEN, refreshToken);
        }
    }

    protected override async Task OnInitializedAsync()
    {
        httpSvc!.Initialize("Api");

        // await LoadDocuments();
        await Task.CompletedTask;
    }
    async Task UserLogin()
    {
        if (!_form.IsValid)
        {
            string validationErrors = string.Join(", ", _errors) ?? "Please fill in all fields";
            snackBar?.Clear();
            snackBar?.Add(validationErrors, Severity.Info);
            return;
        }
        var (status, token) = await authSvc!.UserLogin(_username, _password);
        if (!status)
        {
            snackBar?.Clear();
            snackBar?.Add("Invalid username or password", Severity.Error);
            return;
        }

        var role = token.Roles.Count == 1 ? token.Roles[0] : await sideDialog!.Show<RolesMenu, UserRole>(title: "Select Role", parameters: new Dictionary<string, object> { { "UserRoles", token.Roles } });
        if (role == null)
        {
            snackBar!.Add("You need to select a role", Severity.Info);
            return;
        }
        // Drop the previous user's cached permissions/state BEFORE storing this token. A session
        // that expired straight to this page never ran SignOut, and the circuit — with all its
        // scoped services — is still the one the previous user was using.
        session!.ResetUserState();

        session!.AddItem(AppConstants.SessionVariables.TOKEN, token.Token);
        session!.AddItem(AppConstants.SessionVariables.REFRESH_TOKEN, token.RefereshToken);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token.Token);
        var claim1 = jwt.Claims.FirstOrDefault(dd => dd.Type == "sub");
        var claim2 = jwt.Claims.FirstOrDefault(dd => dd.Type.ToLower() == "userid");
        var claim3 = jwt.Claims.FirstOrDefault(dd => dd.Type.ToLower() == "orgid");
        var claim4 = jwt.Claims.FirstOrDefault(dd => dd.Type.ToLower() == "lable");
        var claim5 = jwt.Claims.FirstOrDefault(dd => dd.Type.ToLower() == "orgentityid");
        var claim6 = jwt.Claims.FirstOrDefault(dd => dd.Type.ToLower() == "userreference");

        var userData = new UserData
        {
            Username = claim1!.Value,
            UserId = claim2!.Value,
            OrgId = claim3!.Value,
            EntityId = claim5!.Value,
            UserReference = claim6!.Value,
            RoleName = claim4!.Value,
            RoleId = role.RoleId.ToString(),
            FullName = jwt.Claims.First(dd => dd.Type == ClaimTypes.Name)!.Value,
            LoginDate = DateTime.Now,
            Token = DataEncryptor.Encrypt(token.Token),
            RefreshToken = DataEncryptor.Encrypt(token.RefereshToken)
        };
        await session.SaveSessionData(AppConstants.SessionVariable.CurrentUser, userData);
        navigator?.NavigateTo("/dashboard");

        Console.WriteLine("Login successful. Token expires at: " + token.ExpireDate);
    }
}
