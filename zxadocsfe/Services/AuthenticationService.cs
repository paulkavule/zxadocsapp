using System;
using zxadocslib.Dtos;

namespace zxadocsfe.Services;

public interface IAuthService
{
    Task<(bool, AuthToken)> GenerateNewCoreToken(string refreshToken);
    /// <summary>Error is the server's message when the login is refused (e.g. a deactivated account).</summary>
    Task<(bool, AuthToken, string? Error)> UserLogin(string username, string password);
}
public class AuthService : IAuthService
{
    private readonly IHttpService httpService;

    public AuthService(IHttpService httpService)
    {
        this.httpService = httpService;
    }

    public async Task<(bool, AuthToken, string? Error)> UserLogin(string username, string password)
    {
        var data = new
        {
            username,
            password
        };
        var (status, result, error) = await httpService.ExecuteRequestAsync<ApiResponse<AuthToken>>(HttpVerb.Post, $"/api/user/login", data);

        return (status, result?.Data!, ErrorMessage.Extract(error));
    }

    public async Task<(bool, AuthToken)> GenerateNewCoreToken(string refreshToken)
    {
        await Task.Delay(2);

        return (true, new AuthToken { ExpireDate = DateTime.UtcNow.AddMinutes(30) });
    }

}
