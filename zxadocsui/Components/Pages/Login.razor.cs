using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocsui.Dtos;
using zxadocsui.State;

namespace zxadocsui.Components.Pages;

public partial class Login
{
    [Inject] NavigationManager? navigator { set; get; }
    [Inject] ISnackbar? snackBar { set; get; }
    [Inject] AppState? state { set; get; }
    [Inject] IHttpService? httpSvc { get; set; }
    [Inject] RequestContext? context { set; get; }
    [Inject] IUserSession? session { get; set; }
    [Inject] IAuthService? authSvc { get; set; }
    [Inject] IJSRuntime? jsSvc { set; get; }

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
        context!.TenantId = Guid.NewGuid().ToString();
        var (status, token) = await authSvc!.UserLogin(_username, _password);
        if (status)
        {
            session!.AddItem("token", token.Token);
            session!.AddItem("refreshToken", token.Token);
            context.RefreshToken = token.RefereshToken;
            context.Token = token.Token;

            await jsSvc!.InvokeVoidAsync("localStorage.setItem", "token", token.Token);
            await jsSvc!.InvokeVoidAsync("localStorage.setItem", "refresh_token", token.RefereshToken);

            navigator?.NavigateTo("/dashboard");

            // state.Set(AppConstants.StateKey.TOKEN, token.Token);
            // state.Set(AppConstants.StateKey.REFRESH_TOKEN, token.RefereshToken);
            // tokenProvider?.SetTokens(token.Token, token.RefereshToken);


            Console.WriteLine("Login successful. Token expires at: " + token.ExpireDate);
        }
        else
        {
            snackBar?.Clear();
            snackBar?.Add("Invalid username or password", Severity.Error);
        }
    }
}
