using System;
using Microsoft.AspNetCore.Components;

namespace zxadocsui.Components.Pages;

public partial class Login
{
    [Inject] NavigationManager Navigator { set; get; }
    bool rememberPassword = true;
    async Task UserLogin()
    {
        Console.WriteLine("Clicked .... ");
        Navigator.NavigateTo("/dashboard");
        await Task.CompletedTask;
    }
}
