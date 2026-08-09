using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client wrapper over the document activity report API. Same (ok, data, error) tuple shape
// as the other client services so pages can snackbar the error verbatim.
public interface IActivityClientService
{
    Task<(bool ok, ApiPaginatedResponse<DocumentActivityDto[]>? page, string? error)> ListDocumentActivity(
        int documentId, string actor, int page, int pageSize);
}

public class ActivityClientService : IActivityClientService
{
    private readonly IHttpService http;
    public ActivityClientService(IHttpService http) => this.http = http;

    public async Task<(bool ok, ApiPaginatedResponse<DocumentActivityDto[]>? page, string? error)> ListDocumentActivity(
        int documentId, string actor, int page, int pageSize)
    {
        http.Initialize("Api");
        var url = $"api/document-activity?documentId={documentId}" +
                  $"&actor={Uri.EscapeDataString(actor ?? string.Empty)}" +
                  $"&page={page}&pageSize={pageSize}";
        var (ok, resp, error) = await http.GetAsync<ApiPaginatedResponse<DocumentActivityDto[]>>(url);
        return ok ? (true, resp, null) : (false, null, ErrorMessage.Extract(error));
    }
}
