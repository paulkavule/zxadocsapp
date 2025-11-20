using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using zxadocsapp.State;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Pages;

public partial class SentDocs
{
    [Inject] AppState? state { set; get; }
    // [Inject] zxadocsapp.Infrastructure.Http.ITokenProvider? tokenProvider { get; set; }
    [Inject] IHttpService? httpSvc { get; set; }
    [Inject] IAuthService? authSvc { get; set; }
    [Inject] IJSRuntime? jsSvc { set; get; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var token = state.Get<string>(zxadocsfe.Helpers.AppConstants.StateKey.TOKEN);
            if (!string.IsNullOrEmpty(token))
            {
                // tokenProvider?.SetTokens(token, tokenProvider.RefreshToken ?? string.Empty);
            }
        }
    }

    protected override void OnInitialized()
    {
        httpSvc!.Initialize("Api");
    }

    private void Incremet()
    {
        state!.NavCount++;
    }

    private async Task Login2()
    {
        var (status, token) = await authSvc!.UserLogin("pkavule", "1234..34");
        if (status)
        {
            await jsSvc!.InvokeVoidAsync("localStorage.setItem", "token", token.Token);
            await jsSvc!.InvokeVoidAsync("localStorage.setItem", "refresh_token", token.RefereshToken);

            state!.Set(AppConstants.StateKey.TOKEN, token.Token);
            state!.Set(AppConstants.StateKey.REFRESH_TOKEN, token.RefereshToken);
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
        var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<QueryDto.DocumentQuery>>>($"/api/documents/dashboard/0?userName=pkavule&pageNumber=1&pageSize=100");
        if (status == false)
            return;

        Console.WriteLine("Access Token: " + result.Data.Count);
    }

}
