using System.Net.Http.Headers;
using zxadocsapp.State;
using zxadocsfe.Services;

namespace zxadocsapp.Infrastructure.Http;

public class HttpCoreIntercetpor : DelegatingHandler
{
    private readonly ILogger<HttpCoreIntercetpor> logger;
    private IAuthService authSvc;
    private readonly RequestContext session;
    // private readonly ISnackbar snackbar;

    public HttpCoreIntercetpor(RequestContext session, ILogger<HttpCoreIntercetpor> logger,
        IAuthService authSvc)
    {
        this.logger = logger;
        this.authSvc = authSvc;
        this.session = session;
        // this.snackbar = snackbar;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Sending request to {Url}", request.RequestUri);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", Guid.NewGuid().ToString());
        // token = session.GetItem<string>("token");
        // refreshToken = session.GetItem<string>("refreshToken");
        var data = session.TenantId;
        var token = session.Token;
        var refreshToken = session.RefreshToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            logger.LogDebug("Added Authorization header to request");
        }

        var response = await base.SendAsync(request, cancellationToken);

        logger.LogInformation("⬅️ {StatusCode} for {Url}", (int)response.StatusCode, request.RequestUri);

        if (!response.IsSuccessStatusCode && !string.IsNullOrEmpty(refreshToken))
        {
            Console.WriteLine("Handling unauthorized response...");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var (status, newToken) = await authSvc.GenerateNewCoreToken(refreshToken);
                if (status)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken.Token);
                    response = await base.SendAsync(request, cancellationToken);
                }

                logger.LogWarning("Unauthorized request to {Url}", request.RequestUri);
            }
            // central error handling/logging
        }
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = $"Error {(int)response.StatusCode}: {response.ReasonPhrase}";
            // snackbar.Add(errorMessage, Severity.Error);
            logger.LogError("Request to {Url} failed with {StatusCode}", request.RequestUri, (int)response.StatusCode);
        }
        return response;
    }
}
