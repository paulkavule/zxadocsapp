using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client for the existing list-values endpoints (/api/listoptions/{orgId}). Used by the
// Organisation Settings "List values" manager. Get/Create already existed; Update/Delete were
// added alongside them on the same route convention (org id in the path).
public interface IListOptionClientService
{
    Task<(bool ok, List<ListOption> data, string? error)> Get(int orgId, string type);
    Task<(bool ok, string? error)> Create(int orgId, string name, string type, string category = "");
    Task<(bool ok, string? error)> Update(int orgId, int id, string name, string type, string category = "");
    Task<(bool ok, string? error)> Delete(int orgId, int id);
}

public class ListOptionClientService : IListOptionClientService
{
    private readonly IHttpService http;
    public ListOptionClientService(IHttpService http) => this.http = http;

    public async Task<(bool ok, List<ListOption> data, string? error)> Get(int orgId, string type)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<List<ListOption>>>(
            $"api/listoptions/{orgId}?type={Uri.EscapeDataString(type)}");
        return ok
            ? (true, resp?.Data ?? new List<ListOption>(), null)
            : (false, new List<ListOption>(), ErrorMessage.Extract(error));
    }

    public Task<(bool ok, string? error)> Create(int orgId, string name, string type, string category = "") =>
        Mutate(HttpVerb.Post, $"api/listoptions/{orgId}", new ListOptionRequest { Name = name, Type = type, Category = category });

    public Task<(bool ok, string? error)> Update(int orgId, int id, string name, string type, string category = "") =>
        Mutate(HttpVerb.Put, $"api/listoptions/{orgId}/{id}", new ListOptionRequest { Id = id, Name = name, Type = type, Category = category });

    public Task<(bool ok, string? error)> Delete(int orgId, int id) =>
        Mutate(HttpVerb.Delete, $"api/listoptions/{orgId}/{id}", null);

    private async Task<(bool ok, string? error)> Mutate(HttpVerb verb, string url, object? body)
    {
        http.Initialize("Api");
        var (ok, _, error) = await http.ExecuteRequestAsync<ApiResponse<string>>(verb, url, body);
        return (ok, ErrorMessage.Extract(error));
    }
}
