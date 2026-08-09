using zxadocslib.Dtos;
using zxadocslib.Helpers;

namespace zxadocsfe.Services;

// =====================================================================================
// Caches the signed-in user's effective permissions from GET /api/me/permissions (ZD-16 /
// ZD-60). Scoped (per user/circuit), so it loads once and answers Has(...) for nav + page
// gating. The server enforces regardless; this only drives what the UI shows.
// =====================================================================================

public record PermissionItem
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
}

public interface IPermissionClientService
{
    Task<IReadOnlySet<Permission>> GetPermissions(bool forceRefresh = false);
    Task<bool> Has(Permission permission);
    Task<bool> HasAny(params Permission[] permissions);

    // Role administration (ZD-93). The catalogue is every Permission the server knows, so a
    // new enum member appears in the UI without a client change.
    Task<(bool ok, PermissionItem[] data, string? error)> GetCatalogue();
    Task<(bool ok, int[] values, string? error)> GetRolePermissions(int roleId);
    Task<(bool ok, string? error)> SetRolePermissions(int roleId, IEnumerable<int> values);
}

public class PermissionClientService : IPermissionClientService, IScopedUserState
{
    private readonly IHttpService http;
    private HashSet<Permission>? cache;

    public PermissionClientService(IHttpService http) => this.http = http;

    // Without this the next user on the same circuit inherits the previous user's menu and page
    // gates until the browser is refreshed.
    public void ClearUserState() => cache = null;

    public async Task<IReadOnlySet<Permission>> GetPermissions(bool forceRefresh = false)
    {
        if (cache is not null && !forceRefresh) return cache;

        http.Initialize("Api");
        var (ok, resp, _) = await http.GetAsync<ApiResponse<PermissionItem[]>>("api/me/permissions");

        // Only cache a SUCCESSFUL fetch. A failed call (e.g. a tokenless request before the auth
        // token is hydrated on a cold load) must not be cached — otherwise the empty set would
        // poison the whole circuit and every permission-gated button would stay hidden. Returning
        // an un-cached empty set lets the next call retry once the token is available.
        if (ok && resp?.Data is not null)
        {
            cache = resp.Data.Select(p => (Permission)p.Value).ToHashSet();
            return cache;
        }
        return new HashSet<Permission>();
    }

    public async Task<bool> Has(Permission permission) =>
        (await GetPermissions()).Contains(permission);

    public async Task<bool> HasAny(params Permission[] permissions)
    {
        var set = await GetPermissions();
        return permissions.Any(set.Contains);
    }

    public async Task<(bool ok, PermissionItem[] data, string? error)> GetCatalogue()
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<PermissionItem[]>>("api/permissions");
        return ok
            ? (true, resp?.Data ?? Array.Empty<PermissionItem>(), null)
            : (false, Array.Empty<PermissionItem>(), ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, int[] values, string? error)> GetRolePermissions(int roleId)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<PermissionItem[]>>($"api/roles/{roleId}/permissions");
        return ok
            ? (true, (resp?.Data ?? Array.Empty<PermissionItem>()).Select(p => p.Value).ToArray(), null)
            : (false, Array.Empty<int>(), ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, string? error)> SetRolePermissions(int roleId, IEnumerable<int> values)
    {
        http.Initialize("Api");
        var (ok, _, error) = await http.ExecuteRequestAsync<ApiResponse<int>>(
            HttpVerb.Put, $"api/roles/{roleId}/permissions", new { Permissions = values.ToArray() });
        return (ok, ErrorMessage.Extract(error));
    }
}
