using System.Text.Json;
using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// =====================================================================================
// Client wrapper over the Legal Templates API (ZD-16 / FE-01). Wraps IHttpService, targets
// the "Api" HttpClient, and unwraps ApiResponse<T>. Every method returns the existing
// (ok, data, error) tuple shape so pages can snackbar the error verbatim.
// =====================================================================================

public interface ITemplateClientService
{
    Task<(bool ok, TemplateCategoryDto[] data, string? error)> GetCategories();
    Task<(bool ok, TemplateCategoryDto? data, string? error)> CreateCategory(string name, string description);

    Task<(bool ok, ApiPaginatedResponse<TemplateDto[]>? page, string? error)> List(
        int status, int categoryId, string search, int page, int pageSize);
    Task<(bool ok, TemplateDto? data, string? error)> Get(int id);
    Task<(bool ok, TemplateDto? data, string? error)> Create(CreateTemplateRequest req);

    Task<(bool ok, TemplateVersionDto? data, string? error)> UploadVersion(
        int templateId, Stream file, string fileName, IProgress<double> progress, CancellationToken ct = default);
    Task<(bool ok, TemplateVersionDto? data, string? error)> SetFields(int versionId, SetFieldsRequest req);

    Task<(bool ok, TemplateVersionDto? data, string? error)> Submit(int versionId);
    Task<(bool ok, TemplateVersionDto? data, string? error)> Approve(int versionId);
    Task<(bool ok, TemplateVersionDto? data, string? error)> Reject(int versionId, string reason);
    Task<(bool ok, TemplateDto? data, string? error)> Archive(int id);
}

public class TemplateClientService : ITemplateClientService
{
    private readonly IHttpService http;
    public TemplateClientService(IHttpService http) => this.http = http;

    public Task<(bool, TemplateCategoryDto[], string?)> GetCategories() =>
        GetArray<TemplateCategoryDto>("api/template-categories");

    public async Task<(bool ok, TemplateCategoryDto? data, string? error)> CreateCategory(string name, string description)
    {
        var body = new { Name = name, Description = description };
        return await Send<TemplateCategoryDto>(HttpVerb.Post, "api/template-categories", body);
    }

    public async Task<(bool ok, ApiPaginatedResponse<TemplateDto[]>? page, string? error)> List(
        int status, int categoryId, string search, int page, int pageSize)
    {
        http.Initialize("Api");
        var url = $"api/templates?status={status}&categoryId={categoryId}&search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        var (ok, resp, error) = await http.GetAsync<ApiPaginatedResponse<TemplateDto[]>>(url);
        return ok ? (true, resp, null) : (false, null, Clean(error));
    }

    public Task<(bool ok, TemplateDto? data, string? error)> Get(int id) =>
        Send<TemplateDto>(HttpVerb.Get, $"api/templates/{id}");

    public Task<(bool ok, TemplateDto? data, string? error)> Create(CreateTemplateRequest req) =>
        Send<TemplateDto>(HttpVerb.Post, "api/templates", req);

    public async Task<(bool ok, TemplateVersionDto? data, string? error)> UploadVersion(
        int templateId, Stream file, string fileName, IProgress<double> progress, CancellationToken ct = default)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.UploadFileWithProgressAsync<ApiResponse<TemplateVersionDto>>(
            $"api/templates/{templateId}/versions", file, fileName, new Dictionary<string, string>(), progress, ct);
        return ok ? (true, resp!.Data, resp.Message) : (false, null, Clean(error));
    }

    public Task<(bool ok, TemplateVersionDto? data, string? error)> SetFields(int versionId, SetFieldsRequest req) =>
        Send<TemplateVersionDto>(HttpVerb.Put, $"api/templates/versions/{versionId}/fields", req);

    public Task<(bool ok, TemplateVersionDto? data, string? error)> Submit(int versionId) =>
        Send<TemplateVersionDto>(HttpVerb.Post, $"api/templates/versions/{versionId}/submit");

    public Task<(bool ok, TemplateVersionDto? data, string? error)> Approve(int versionId) =>
        Send<TemplateVersionDto>(HttpVerb.Post, $"api/templates/versions/{versionId}/approve");

    public Task<(bool ok, TemplateVersionDto? data, string? error)> Reject(int versionId, string reason) =>
        Send<TemplateVersionDto>(HttpVerb.Post, $"api/templates/versions/{versionId}/reject", new RejectRequest { Reason = reason });

    public Task<(bool ok, TemplateDto? data, string? error)> Archive(int id) =>
        Send<TemplateDto>(HttpVerb.Post, $"api/templates/{id}/archive");

    // ---- shared plumbing ----

    private async Task<(bool ok, T? data, string? error)> Send<T>(HttpVerb verb, string url, object? body = null)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.ExecuteRequestAsync<ApiResponse<T>>(verb, url, body);
        return ok ? (true, resp!.Data, resp.Message) : (false, default, Clean(error));
    }

    private async Task<(bool, T[], string?)> GetArray<T>(string url)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<T[]>>(url);
        return ok ? (true, resp!.Data ?? Array.Empty<T>(), null) : (false, Array.Empty<T>(), Clean(error));
    }

    // Pull a human message out of an ApiResponse / ProblemDetails error body; else return it raw.
    internal static string? Clean(string? error) => ErrorMessage.Extract(error);
}
