using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client wrapper over the document activity report API. Same (ok, data, error) tuple shape
// as the other client services so pages can snackbar the error verbatim.
public interface IActivityClientService
{
    Task<(bool ok, ApiPaginatedResponse<DocumentActivityDto[]>? page, string? error)> ListDocumentActivity(
        int documentId, string actor, int page, int pageSize);

    // Key = yyyy-MM-dd, Name = action, Value = count. Days with no activity come back as a
    // single row with an empty Name so the chart keeps a stable x-axis.
    Task<(bool ok, List<ListValue> data, string? error)> GetDailyActionMix(int days);
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

    public async Task<(bool ok, List<ListValue> data, string? error)> GetDailyActionMix(int days)
    {
        http.Initialize("Api");
        var (ok, resp, error) = await http.GetAsync<ApiResponse<List<ListValue>>>(
            $"api/document-activity/summary?days={days}");
        return ok
            ? (true, resp?.Data ?? new List<ListValue>(), null)
            : (false, new List<ListValue>(), ErrorMessage.Extract(error));
    }
}
