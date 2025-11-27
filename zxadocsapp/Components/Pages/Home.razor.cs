using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsapp.State;
using zxadocsapp.States;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Pages;

public partial class Home
{
    [Inject] ISnackbar? snackbar { set; get; }
    [Inject] RequestContext? context { set; get; }
    [Inject] AppState? state { set; get; }
    [Inject] IUserSession? session { get; set; }
    [Inject] IHttpService? httpSvc { get; set; }
    [Inject] IAuthService? authSvc { get; set; }
    [Inject] IJSRuntime? jsSvc { set; get; }
    string userId = "1";
    List<QueryDto.DocumentQuery> docList = new();

    private string _message = "Before render";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {

            // httpSvc?.Initialize(AppConstants.HttpSchemes.Core);

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
        try
        {
            httpSvc?.Initialize(AppConstants.HttpSchemes.Core);
        }
        catch (Exception ee)
        {
            snackbar?.Add(ee.Message, Severity.Error);
        }

        // await LoadDocuments();
        await Task.CompletedTask;
    }
    public void Login()
    {
        Console.WriteLine("Login clicked");
    }

    private async Task Login2()
    {
        context!.TenantId = Guid.NewGuid().ToString();

        var (status, token) = await authSvc!.UserLogin("dkavule", "2344..98");
        if (status)
        {
            session!.AddItem("token", token.Token);
            session!.AddItem("refreshToken", token.Token);
            context.RefreshToken = token.RefereshToken;
            context.Token = token.Token;

            await jsSvc!.InvokeVoidAsync("localStorage.setItem", "token", token.Token);
            await jsSvc!.InvokeVoidAsync("localStorage.setItem", "refresh_token", token.RefereshToken);



            // state.Set(AppConstants.StateKey.TOKEN, token.Token);
            // state.Set(AppConstants.StateKey.REFRESH_TOKEN, token.RefereshToken);
            // tokenProvider?.SetTokens(token.Token, token.RefereshToken);


            Console.WriteLine("Login successful. Token expires at: " + token.ExpireDate);
        }
        else
        {
            Console.WriteLine("Login failed.");
        }
    }

    private async Task LoadDocuments()
    {
        var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<QueryDto.DocumentQuery>>>($"/api/documents/dashboard/0?userName={userId}&pageNumber=1&pageSize=100");
        if (status == false)
            return;
        if (status && result != null && result?.Data.Count > 0)
            docList = result.Data ?? new List<QueryDto.DocumentQuery>();

        Console.WriteLine("Access Token: " + docList.Count);
    }

}
