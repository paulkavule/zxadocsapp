using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Newtonsoft.Json;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
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
    Task UpdateTokens(string accessToken, string refreshToken);
    Task SignOut();

    // Drops every per-circuit cache belonging to the outgoing user. Call before storing a new
    // sign-in as well as on sign-out — an expired session lands on the login page without
    // SignOut ever running.
    void ResetUserState();
}
public class UserSession : IUserSession, ITokenProvider
{
    private readonly ProtectedLocalStorage storage;
    private readonly NavigationManager nav;
    private readonly ILogger<UserSession> logger;
    private readonly IServiceProvider services;

    private Dictionary<string, object> Items { get; set; } = new Dictionary<string, object>();

    // IScopedUserState implementations are resolved lazily rather than injected: several of them
    // reach IHttpService, whose handler depends on this class as ITokenProvider, so constructor
    // injection would be a circular dependency.
    public UserSession(ProtectedLocalStorage storage, NavigationManager nav, ILogger<UserSession> logger,
        IServiceProvider services)
    {
        this.storage = storage;
        this.nav = nav;
        this.logger = logger;
        this.services = services;
    }

    public void ResetUserState()
    {
        Items.Clear();
        foreach (var state in services.GetServices<IScopedUserState>())
        {
            try
            {
                state.ClearUserState();
            }
            catch (Exception ex)
            {
                // One uncooperative cache must not abort the rest of the sign-out.
                logger.LogDebug(ex, "Failed to clear {State}", state.GetType().Name);
            }
        }
    }

    public string? AccessToken => GetItem<string>(AppConstants.SessionVariables.TOKEN);
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

        try
        {
            var currentUser = await GetSessionData<UserData>(SessionVariable.CurrentUser);
            if (currentUser == null)
                return user;

            user.LoginDate = currentUser.LoginDate;
            user.RoleId = currentUser.RoleId;

            user.Username = DataEncryptor.Decrypt(currentUser.Username);
            user.FullName = DataEncryptor.Decrypt(currentUser.FullName);
            user.RoleName = DataEncryptor.Decrypt(currentUser.RoleName);
            user.OrgId = DataEncryptor.Decrypt(currentUser.OrgId);
            user.UserId = DataEncryptor.Decrypt(currentUser.UserId);
            user.Token = DataEncryptor.Decrypt(currentUser.Token);
            user.EntityId = DataEncryptor.Decrypt(currentUser.EntityId);
            user.UserReference = DataEncryptor.Decrypt(currentUser.UserReference);
            user.RefreshToken = DataEncryptor.Decrypt(currentUser.RefreshToken);

            Items.Remove(userDataKey);
            Items.Add(userDataKey, user);
            Items[AppConstants.SessionVariables.TOKEN] = user.Token;
            Items[AppConstants.SessionVariables.REFRESH_TOKEN] = user.RefreshToken;
        }
        catch (Exception ex)
        {
            // ProtectedLocalStorage is unavailable during prerender, and stored data may be missing or unreadable.
            logger.LogDebug(ex, "Failed to load user session from storage");
            return new UserData();
        }

        return user;
    }

    public async Task UpdateTokens(string accessToken, string refreshToken)
    {
        var userDataKey = "101Session";
        var user = await GetCurrentUser();

        user.Token = accessToken;
        user.RefreshToken = refreshToken;
        Items[userDataKey] = user;
        Items[AppConstants.SessionVariables.TOKEN] = accessToken;
        Items[AppConstants.SessionVariables.REFRESH_TOKEN] = refreshToken;

        try
        {
            var encrypted = new UserData
            {
                Username = DataEncryptor.Encrypt(user.Username),
                FullName = DataEncryptor.Encrypt(user.FullName),
                RoleName = DataEncryptor.Encrypt(user.RoleName),
                OrgId = DataEncryptor.Encrypt(user.OrgId),
                UserId = DataEncryptor.Encrypt(user.UserId),
                Token = DataEncryptor.Encrypt(accessToken),
                RefreshToken = DataEncryptor.Encrypt(refreshToken),
                LoginDate = user.LoginDate,
                RoleId = user.RoleId
            };
            await SaveSessionData(SessionVariable.CurrentUser, encrypted);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to persist refreshed tokens to storage");
        }
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
        // Clears Items AND every other per-circuit cache. Signing out navigates within the same
        // circuit, so anything left here is served to whoever signs in next.
        ResetUserState();
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
