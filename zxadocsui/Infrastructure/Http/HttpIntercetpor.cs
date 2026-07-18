using System.Net;
using System.Net.Http.Headers;

namespace zxadocui.Infrastructure.Http;

public class HttpCoreIntercetpor : DelegatingHandler
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key"
    };

    private readonly ILogger<HttpCoreIntercetpor> logger;

    public HttpCoreIntercetpor(ILogger<HttpCoreIntercetpor> logger) => this.logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        logger.LogInformation("➡️ {Method} {Url} [{CorrelationId}] {Headers}",
            request.Method, request.RequestUri, correlationId, Mask(request.Headers));

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var level = response.IsSuccessStatusCode ? LogLevel.Information : LogLevel.Warning;
            logger.Log(level, "⬅️ {Status} {Reason} {Url} [{CorrelationId}]",
                (int)response.StatusCode, response.ReasonPhrase, request.RequestUri, correlationId);
            return response;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Request to {Url} timed out [{CorrelationId}]", request.RequestUri, correlationId);
            return new HttpResponseMessage(HttpStatusCode.RequestTimeout) { RequestMessage = request, ReasonPhrase = "Request timed out" };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error for {Url} [{CorrelationId}]", request.RequestUri, correlationId);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request, ReasonPhrase = "Network error" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected client error for {Url} [{CorrelationId}]", request.RequestUri, correlationId);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = request, ReasonPhrase = "Unexpected client error" };
        }
    }

    private static string Mask(HttpHeaders headers) =>
        string.Join("; ", headers.Select(h =>
            $"{h.Key}={(SensitiveHeaders.Contains(h.Key) ? "***" : string.Join(",", h.Value))}"));
}
