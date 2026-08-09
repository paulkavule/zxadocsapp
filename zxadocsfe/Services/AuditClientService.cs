using zxadocslib.Dtos;

namespace zxadocsfe.Services;

// Client wrapper over the audit trail API. Same (ok, data, error) tuple shape as the other
// client services so pages can snackbar the error verbatim.
public interface IAuditClientService
{
    Task<(bool ok, ApiPaginatedResponse<AuditEntryDto[]>? page, string? error)> List(
        string entityType, int entityId, string action, string actor, int page, int pageSize);
}

public class AuditClientService : IAuditClientService
{
    private readonly IHttpService http;
    public AuditClientService(IHttpService http) => this.http = http;

    public async Task<(bool ok, ApiPaginatedResponse<AuditEntryDto[]>? page, string? error)> List(
        string entityType, int entityId, string action, string actor, int page, int pageSize)
    {
        http.Initialize("Api");
        var url = $"api/audit?entityType={Uri.EscapeDataString(entityType ?? string.Empty)}" +
                  $"&entityId={entityId}" +
                  $"&action={Uri.EscapeDataString(action ?? string.Empty)}" +
                  $"&actor={Uri.EscapeDataString(actor ?? string.Empty)}" +
                  $"&page={page}&pageSize={pageSize}";
        var (ok, resp, error) = await http.GetAsync<ApiPaginatedResponse<AuditEntryDto[]>>(url);
        return ok ? (true, resp, null) : (false, null, ErrorMessage.Extract(error));
    }
}
