using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client for the org's role CRUD (/api/userroles). The organisation is taken from the token
// server-side (ZD-94), so nothing here passes an org id.
public interface IUserRoleClientService
{
    Task<(bool ok, UserRole[] data, string? error)> List();
    Task<(bool ok, UserRole? data, string? error)> Get(int id);

    // Returns the new role's id — the caller needs it to save the role's permissions next.
    Task<(bool ok, int id, string? error)> Create(string roleName);
    Task<(bool ok, string? error)> Update(int id, string roleName);
    Task<(bool ok, string? error)> Delete(int id);
}

public class UserRoleClientService : IUserRoleClientService
{
    private readonly IHttpService http;
    public UserRoleClientService(IHttpService http) => this.http = http;

    public async Task<(bool ok, UserRole[] data, string? error)> List()
    {
        http.Initialize("Api");
        // PageSize is generous on purpose: an org has tens of roles, and the page shows them all.
        var (ok, resp, error) = await http.GetAsync<ApiPaginatedResponse<UserRole[]>>(
            "api/userroles?pageNumber=1&pageSize=200");
        return ok
            ? (true, resp?.Data ?? Array.Empty<UserRole>(), null)
            : (false, Array.Empty<UserRole>(), ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, UserRole? data, string? error)> Get(int id)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<UserRole>>($"api/userroles/{id}");
        return ok ? (true, resp?.Data, null) : (false, null, ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, int id, string? error)> Create(string roleName)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.ExecuteRequestAsync<ApiResponse<int>>(
            HttpVerb.Post, "api/userroles", new UserRole { RoleName = roleName });
        return ok ? (true, resp?.Data ?? 0, null) : (false, 0, ErrorMessage.Extract(error));
    }

    public Task<(bool ok, string? error)> Update(int id, string roleName) =>
        Mutate(HttpVerb.Patch, $"api/userroles/{id}", new UserRole { RoleId = id, RoleName = roleName });

    public Task<(bool ok, string? error)> Delete(int id) =>
        Mutate(HttpVerb.Delete, $"api/userroles/{id}", null);

    private async Task<(bool ok, string? error)> Mutate(HttpVerb verb, string url, object? body)
    {
        http.Initialize("Api");
        var (ok, _, error) = await http.ExecuteRequestAsync<ApiResponse<int>>(verb, url, body);
        return (ok, ErrorMessage.Extract(error));
    }
}
