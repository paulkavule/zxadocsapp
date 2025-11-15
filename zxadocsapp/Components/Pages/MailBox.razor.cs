using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Pages;

public partial class MailBox
{
    [Inject] IHttpService httpSvc { get; set; }
    [Inject] IAuthService authSvc { get; set; }
    [Inject] IJSRuntime jsSvc { set; get; }
    string userId = "1";
    List<QueryDto.DocumentQuery> docList = new();
    protected override async Task OnInitializedAsync()
    {
        httpSvc.Initialize("Api");

        // await LoadDocuments();
        await Task.CompletedTask;
    }
    public void Login()
    {
        Console.WriteLine("Login clicked");
    }

    private async Task Login2()
    {
        var (status, token) = await authSvc.UserLogin("pkavule", "1234..34");
        if (status)
        {
            await jsSvc.InvokeVoidAsync("localStorage.setItem", "token", token.Token);
            await jsSvc.InvokeVoidAsync("localStorage.setItem", "refresh_token", token.RefereshToken);
            Console.WriteLine("Login successful. Token expires at: " + token.ExpireDate);
        }
        else
        {
            Console.WriteLine("Login failed.");
        }
    }

    private async Task LoadDocuments()
    {
        var (status, result, message) = await httpSvc.GetAsync<ApiResponse<List<QueryDto.DocumentQuery>>>($"/api/documents/dashboard/0?userName={userId}&pageNumber=1&pageSize=100");
        if (status == false)
            return;
        if (status && result != null && result?.Data.Count > 0)
            docList = result.Data ?? new List<QueryDto.DocumentQuery>();

        Console.WriteLine("Access Token: " + docList.Count);
    }

}
