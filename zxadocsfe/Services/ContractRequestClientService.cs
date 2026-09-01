using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client for contract initiation requests (ZD-118/119/121). The organisation is taken from the
// token server-side, so nothing here passes an org id.
public interface IContractRequestClientService
{
    Task<(bool ok, ContractRequestDto[] data, string? error)> List();
    Task<(bool ok, ContractRequestDto? data, string? error)> Get(int id);
    Task<(bool ok, ContractRequestDto? data, string? error)> Create(SaveContractRequestRequest request);
    Task<(bool ok, ContractRequestDto? data, string? error)> Update(int id, SaveContractRequestRequest request);
    /// <summary>Removes the request. Refused once a draft has been raised against it.</summary>
    Task<(bool ok, string? error)> Delete(int id);

    Task<(bool ok, ContractRequestDocumentDto? data, string? error)> AddDocument(
        int requestId, byte[] content, string fileName, string contentType);
    Task<(bool ok, string? error)> RemoveDocument(int documentId);

    /// <summary>
    /// Approved templates of this request's contract type. Served by the request endpoint, not
    /// /api/templates, which needs ViewTemplates that a drafter need not hold.
    /// </summary>
    Task<(bool ok, TemplateDto[] data, string? error)> DraftableTemplates(int requestId);

    /// <summary>This request's values merged into a candidate template. Creates nothing.</summary>
    Task<(bool ok, string html, string? error)> PreviewWithTemplate(int requestId, int templateId);

    /// <summary>Raises a draft against the request from a template of its contract type.</summary>
    Task<(bool ok, DraftDto? data, string? error)> RaiseDraft(int requestId, int templateId);
}

public class ContractRequestClientService(IHttpService http) : IContractRequestClientService
{
    private const string BaseUrl = "api/contract-requests";

    public async Task<(bool ok, ContractRequestDto[] data, string? error)> List()
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<ContractRequestDto[]>>(BaseUrl);
        return ok
            ? (true, resp?.Data ?? Array.Empty<ContractRequestDto>(), null)
            : (false, Array.Empty<ContractRequestDto>(), ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, ContractRequestDto? data, string? error)> Get(int id)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<ContractRequestDto>>($"{BaseUrl}/{id}");
        return ok ? (true, resp?.Data, null) : (false, null, ErrorMessage.Extract(error));
    }

    public Task<(bool ok, ContractRequestDto? data, string? error)> Create(SaveContractRequestRequest request) =>
        Save(HttpVerb.Post, BaseUrl, request);

    public Task<(bool ok, ContractRequestDto? data, string? error)> Update(int id, SaveContractRequestRequest request) =>
        Save(HttpVerb.Put, $"{BaseUrl}/{id}", request);

    public async Task<(bool ok, string? error)> Delete(int id)
    {
        http.Initialize("Api");
        var (ok, _, error) = await http.ExecuteRequestAsync<ApiResponse<int>>(
            HttpVerb.Delete, $"{BaseUrl}/{id}");
        return (ok, ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, ContractRequestDocumentDto? data, string? error)> AddDocument(
        int requestId, byte[] content, string fileName, string contentType)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.UploadFileAsync<ApiResponse<ContractRequestDocumentDto>>(
            $"{BaseUrl}/{requestId}/documents", content, fileName, contentType);
        return ok ? (true, resp?.Data, null) : (false, null, ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, string? error)> RemoveDocument(int documentId)
    {
        http.Initialize("Api");
        var (ok, _, error) = await http.ExecuteRequestAsync<ApiResponse<int>>(
            HttpVerb.Delete, $"{BaseUrl}/documents/{documentId}");
        return (ok, ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, TemplateDto[] data, string? error)> DraftableTemplates(int requestId)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<TemplateDto[]>>(
            $"{BaseUrl}/{requestId}/templates");
        return ok
            ? (true, resp?.Data ?? Array.Empty<TemplateDto>(), null)
            : (false, Array.Empty<TemplateDto>(), ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, string html, string? error)> PreviewWithTemplate(int requestId, int templateId)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<string>>(
            $"{BaseUrl}/{requestId}/templates/{templateId}/preview");
        return ok
            ? (true, resp?.Data ?? string.Empty, null)
            : (false, string.Empty, ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, DraftDto? data, string? error)> RaiseDraft(int requestId, int templateId)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.ExecuteRequestAsync<ApiResponse<DraftDto>>(
            HttpVerb.Post, $"{BaseUrl}/{requestId}/draft", new DraftFromRequestRequest { TemplateId = templateId });
        return ok ? (true, resp?.Data, null) : (false, null, ErrorMessage.Extract(error));
    }

    private async Task<(bool ok, ContractRequestDto? data, string? error)> Save(
        HttpVerb verb, string url, SaveContractRequestRequest request)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.ExecuteRequestAsync<ApiResponse<ContractRequestDto>>(verb, url, request);
        return ok ? (true, resp?.Data, null) : (false, null, ErrorMessage.Extract(error));
    }
}
