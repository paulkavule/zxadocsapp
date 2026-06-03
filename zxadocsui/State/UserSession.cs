using System;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Newtonsoft.Json;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using static zxadocsfe.Helpers.AppConstants;

namespace zxadocsui.State;

public interface IUserSession
{
    T GetItem<T>(string key);
    void AddItem<T>(string key, T value);
    void RemoveItem(string key);

    Task<UserData> GetCurrentUser();
    Task<T> GetSessionData<T>(SessionVariable variable);
    Task<bool> SaveSessionData(SessionVariable variable, object value);
    Task SignOut();
}
public class UserSession : /*AuthenticationStateProvider,*/ IUserSession
{
    private readonly ProtectedLocalStorage storage;
    private readonly NavigationManager nav;
    private readonly ILogger<UserSession> logger;

    private Dictionary<string, object> Items { get; set; } = new Dictionary<string, object>();

    public UserSession(ProtectedLocalStorage storage, NavigationManager nav, ILogger<UserSession> logger)
    {
        this.storage = storage;
        this.nav = nav;
        this.logger = logger;
    }
    public T GetItem<T>(string key)
    {
        try
        {
            return (T)Items[key];
        }
        catch //(Exception ex)
        {

            return default(T);
        }
    }

    public (string Token, string refreshToken) GetTokens()
    {
        return ("", "");
    }
    public void AddItem<T>(string key, T value)
    {
        if (Items.Keys.Contains(key))
            Items[key] = value;
        else
            Items.Add(key, value);
    }

    public void RemoveItem(string key)
    {
        if (Items.Keys.Contains(key))
            Items.Remove(key);
    }


    public async Task<UserData> GetCurrentUser()
    {
        var userDataKey = "101Session";
        var user = new UserData();
        if (Items.Keys.Contains(userDataKey))
        {
            user = (UserData)Items[userDataKey];
            if ((DateTime.Now - user.LoginDate).TotalHours > 2)
            {
                nav.NavigateTo($"/?returnUrl={nav.Uri}");
                return new UserData { };
            }
            return user;
        }

        var currentUser = await GetSessionData<UserData>(SessionVariable.CurrentUser);
        user.LoginDate = currentUser.LoginDate;
        user.RoleId = currentUser.RoleId;

        user.Username = DataEncryptor.Decrypt(currentUser.Username);
        user.FullName = DataEncryptor.Decrypt(currentUser.FullName);
        user.RoleName = DataEncryptor.Decrypt(currentUser.RoleName);
        user.OrgId = DataEncryptor.Decrypt(currentUser.OrgId);
        user.UserId = DataEncryptor.Decrypt(currentUser.UserId);
        user.Token = DataEncryptor.Decrypt(currentUser.Token);
        user.RefreshToken = DataEncryptor.Decrypt(currentUser.RefreshToken);

        Items.Remove(userDataKey);
        Items.Add(userDataKey, user);

        return user;
    }

    public async Task<T> GetSessionData<T>(SessionVariable variable)
    {
        var sessionVar = variable.ToString();

        var stored = await storage.GetAsync<string>(sessionVar);
        if (!stored.Success || stored.Value == null)
            return default!;

        var value = JsonConvert.DeserializeObject<T>(stored.Value);
        if (value == null)
            return default!;

        return value;
    }

    public async Task<bool> SaveSessionData(SessionVariable variable, object value)
    {
        if (value == null) return false;

        var strValue = JsonConvert.SerializeObject(value);
        await storage.SetAsync(variable.ToString(), strValue);

        return true;
    }

    public async Task SignOut()
    {
        foreach (var variable in Enum.GetValues<SessionVariable>())
        {
            await storage.DeleteAsync(variable.ToString());
        }
        Console.WriteLine(" -- -- -- -- -- -- > sigining out");
        Items.Clear();
        nav.NavigateTo("/");
    }

    // public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    // {
    //     try
    //     {
    //         var principle = await storage.GetAsync<UserData>(AppConstants.SessionVariable.CurrentUser.ToString());

    //         if (principle.Success == false || principle.Value == null)
    //             return default!;
    //         var token = DataEncryptor.Decrypt(principle.Value.Token);
    //         var handler = new JwtSecurityTokenHandler();
    //         var jwt = handler.ReadJwtToken(token);

    //         NotifyAuthenticationStateChanged(Task.FromResult(state));
    //         return state;
    //     }
    //     catch (Exception ee)
    //     {
    //         log.WriteLine("GetAuthenticationStateAsync Exception: " + ee.StackTrace);
    //     }

    //     return default!;
    // }
}
