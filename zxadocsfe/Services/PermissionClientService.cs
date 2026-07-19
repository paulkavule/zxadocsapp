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
}

public class PermissionClientService : IPermissionClientService
{
    private readonly IHttpService http;
    private HashSet<Permission>? cache;

    public PermissionClientService(IHttpService http) => this.http = http;

    public async Task<IReadOnlySet<Permission>> GetPermissions(bool forceRefresh = false)
    {
        if (cache is not null && !forceRefresh) return cache;

        http.Initialize("Api");
        var (ok, resp, _) = await http.GetAsync<ApiResponse<PermissionItem[]>>("api/me/permissions");
        cache = ok && resp?.Data is not null
            ? resp.Data.Select(p => (Permission)p.Value).ToHashSet()
            : new HashSet<Permission>();
        return cache;
    }

    public async Task<bool> Has(Permission permission) =>
        (await GetPermissions()).Contains(permission);

    public async Task<bool> HasAny(params Permission[] permissions)
    {
        var set = await GetPermissions();
        return permissions.Any(set.Contains);
    }
}
