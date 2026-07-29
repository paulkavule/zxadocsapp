using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// =====================================================================================
// Client wrapper over the Drafts API (ZD-16 / FE-01). Same shape as TemplateClientService.
// Preview/download return raw PDF bytes (via GetBytesAsync) for the DocumentEditor viewer.
// =====================================================================================

public interface IDraftClientService
{
    Task<(bool ok, DraftDto? data, string? error)> Create(CreateDraftRequest req);
    Task<(bool ok, DraftDto? data, string? error)> Update(int id, UpdateDraftRequest req);
    Task<(bool ok, DraftDto? data, string? error)> Get(int id);

    // Field defs + body of the draft's snapshotted template, under draft permissions — the
    // template endpoints are closed to an approver who holds only ApproveDraft.
    Task<(bool ok, DraftTemplateDto? data, string? error)> GetTemplate(int id);
    Task<(bool ok, ApiPaginatedResponse<DraftDto[]>? page, string? error)> List(int status, int page, int pageSize);

    Task<(bool ok, DraftDto? data, string? error)> Submit(int id);
    Task<(bool ok, DraftDto? data, string? error)> Approve(int id);
    Task<(bool ok, DraftDto? data, string? error)> Reject(int id, string reason);

    Task<(bool ok, byte[]? pdf, string? error)> Preview(int id, CancellationToken ct = default);
    Task<(bool ok, byte[]? pdf, string? error)> Download(int id, CancellationToken ct = default);
    Task<(bool ok, GenerateForSigningResponse? data, string? error)> GenerateForSigning(int id);
}

public class DraftClientService : IDraftClientService
{
    private readonly IHttpService http;
    public DraftClientService(IHttpService http) => this.http = http;

    public Task<(bool ok, DraftDto? data, string? error)> Create(CreateDraftRequest req) =>
        Send<DraftDto>(HttpVerb.Post, "api/drafts", req);

    public Task<(bool ok, DraftDto? data, string? error)> Update(int id, UpdateDraftRequest req) =>
        Send<DraftDto>(HttpVerb.Put, $"api/drafts/{id}", req);

    public Task<(bool ok, DraftDto? data, string? error)> Get(int id) =>
        Send<DraftDto>(HttpVerb.Get, $"api/drafts/{id}");

    public Task<(bool ok, DraftTemplateDto? data, string? error)> GetTemplate(int id) =>
        Send<DraftTemplateDto>(HttpVerb.Get, $"api/drafts/{id}/template");

    public async Task<(bool ok, ApiPaginatedResponse<DraftDto[]>? page, string? error)> List(int status, int page, int pageSize)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiPaginatedResponse<DraftDto[]>>(
            $"api/drafts?status={status}&page={page}&pageSize={pageSize}");
        return ok ? (true, resp, null) : (false, null, ErrorMessage.Extract(error));
    }

    public Task<(bool ok, DraftDto? data, string? error)> Submit(int id) =>
        Send<DraftDto>(HttpVerb.Post, $"api/drafts/{id}/submit");

    public Task<(bool ok, DraftDto? data, string? error)> Approve(int id) =>
        Send<DraftDto>(HttpVerb.Post, $"api/drafts/{id}/approve");

    public Task<(bool ok, DraftDto? data, string? error)> Reject(int id, string reason) =>
        Send<DraftDto>(HttpVerb.Post, $"api/drafts/{id}/reject", new RejectRequest { Reason = reason });

    public async Task<(bool ok, byte[]? pdf, string? error)> Preview(int id, CancellationToken ct = default)
    {
        http.Initialize("Api");
        var (ok, bytes, error) = await http.GetBytesAsync($"api/drafts/{id}/preview", ct);
        return ok ? (true, bytes, null) : (false, null, ErrorMessage.Extract(error));
    }

    public async Task<(bool ok, byte[]? pdf, string? error)> Download(int id, CancellationToken ct = default)
    {
        http.Initialize("Api");
        var (ok, bytes, error) = await http.GetBytesAsync($"api/drafts/{id}/download", ct);
        return ok ? (true, bytes, null) : (false, null, ErrorMessage.Extract(error));
    }

    public Task<(bool ok, GenerateForSigningResponse? data, string? error)> GenerateForSigning(int id) =>
        Send<GenerateForSigningResponse>(HttpVerb.Post, $"api/drafts/{id}/generate-for-signing");

    private async Task<(bool ok, T? data, string? error)> Send<T>(HttpVerb verb, string url, object? body = null)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.ExecuteRequestAsync<ApiResponse<T>>(verb, url, body);
        return ok ? (true, resp!.Data, resp.Message) : (false, default, ErrorMessage.Extract(error));
    }
}
