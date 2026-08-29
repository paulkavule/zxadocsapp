using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client for the contract type catalogue (/api/contract-types, ZD-115). The organisation is
// taken from the token server-side, so nothing here passes an org id.
public interface IContractTypeClientService
{
    Task<(bool ok, ContractTypeDto[] data, string? error)> List(bool includeInactive = false);
    Task<(bool ok, ContractTypeDto? data, string? error)> Get(int id);
    Task<(bool ok, ContractTypeDto? data, string? error)> Create(SaveContractTypeRequest request);
    Task<(bool ok, ContractTypeDto? data, string? error)> Update(int id, SaveContractTypeRequest request);
    Task<(bool ok, string? error)> Delete(int id);
}

public class ContractTypeClientService(IHttpService http) : IContractTypeClientService
{
    private const string BaseUrl = "api/contract-types";

    public async Task<(bool ok, ContractTypeDto[] data, string? error)> List(bool includeInactive = false)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<ContractTypeDto[]>>(
            $"{BaseUrl}?includeInactive={includeInactive.ToString().ToLowerInvariant()}");
        return ok
            ? (true, resp?.Data ?? Array.Empty<ContractTypeDto>(), null)
            : (false, Array.Empty<ContractTypeDto>(), ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, ContractTypeDto? data, string? error)> Get(int id)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<ContractTypeDto>>($"{BaseUrl}/{id}");
        return ok ? (true, resp?.Data, null) : (false, null, ErrorMessage.Extract(error));
    }

    public Task<(bool ok, ContractTypeDto? data, string? error)> Create(SaveContractTypeRequest request) =>
        Save(HttpVerb.Post, BaseUrl, request);

    public Task<(bool ok, ContractTypeDto? data, string? error)> Update(int id, SaveContractTypeRequest request) =>
        Save(HttpVerb.Put, $"{BaseUrl}/{id}", request);

    public async Task<(bool ok, string? error)> Delete(int id)
    {
        http.Initialize("Api");
        var (ok, _, error) = await http.ExecuteRequestAsync<ApiResponse<ContractTypeDto>>(
            HttpVerb.Delete, $"{BaseUrl}/{id}");
        return (ok, ErrorMessage.Extract(error));
    }

    private async Task<(bool ok, ContractTypeDto? data, string? error)> Save(
        HttpVerb verb, string url, SaveContractTypeRequest request)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.ExecuteRequestAsync<ApiResponse<ContractTypeDto>>(verb, url, request);
        return ok ? (true, resp?.Data, null) : (false, null, ErrorMessage.Extract(error));
    }
}
