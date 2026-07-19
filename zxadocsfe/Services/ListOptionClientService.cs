using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client for the generic list-values management endpoints (/api/listoptions/manage). Used by
// the Organisation Settings "List values" manager. The server derives the org from the JWT
// and gates mutations on the Admin role.
public interface IListOptionClientService
{
    Task<(bool ok, List<ListOption> data, string? error)> Get(string type);
    Task<(bool ok, string? error)> Create(string name, string type, string category = "");
    Task<(bool ok, string? error)> Update(int id, string name, string type, string category = "");
    Task<(bool ok, string? error)> Delete(int id);
}

public class ListOptionClientService : IListOptionClientService
{
    private readonly IHttpService http;
    public ListOptionClientService(IHttpService http) => this.http = http;

    public async Task<(bool ok, List<ListOption> data, string? error)> Get(string type)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<List<ListOption>>>(
            $"api/listoptions/manage?type={Uri.EscapeDataString(type)}");
        return ok
            ? (true, resp?.Data ?? new List<ListOption>(), null)
            : (false, new List<ListOption>(), ErrorMessage.Extract(error));
    }

    public Task<(bool ok, string? error)> Create(string name, string type, string category = "") =>
        Mutate(HttpVerb.Post, "api/listoptions/manage", new ListOptionRequest { Name = name, Type = type, Category = category });

    public Task<(bool ok, string? error)> Update(int id, string name, string type, string category = "") =>
        Mutate(HttpVerb.Put, $"api/listoptions/manage/{id}", new ListOptionRequest { Id = id, Name = name, Type = type, Category = category });

    public Task<(bool ok, string? error)> Delete(int id) =>
        Mutate(HttpVerb.Delete, $"api/listoptions/manage/{id}", null);

    private async Task<(bool ok, string? error)> Mutate(HttpVerb verb, string url, object? body)
    {
        http.Initialize("Api");
        var (ok, _, error) = await http.ExecuteRequestAsync<ApiResponse<string>>(verb, url, body);
        return (ok, ErrorMessage.Extract(error));
    }
}
