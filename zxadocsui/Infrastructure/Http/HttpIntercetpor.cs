using System.Net;
using System.Net.Http.Headers;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocsui.State;

namespace zxadocui.Infrastructure.Http;

public class HttpCoreIntercetpor : DelegatingHandler
{
    private readonly ILogger<HttpCoreIntercetpor> logger;
    private IAuthService authSvc;
    private IUserSession session;
    // private readonly ISnackbar snackbar;

    public HttpCoreIntercetpor(IUserSession session, ILogger<HttpCoreIntercetpor> logger,
        IAuthService authSvc)
    {
        this.logger = logger;
        this.authSvc = authSvc;
        this.session = session;
        // this.snackbar = snackbar;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string usertoken = session.GetItem<string>(AppConstants.SessionVariables.TOKEN);
        string refreshToken = session.GetItem<string>(AppConstants.SessionVariables.REFRESH_TOKEN);
        logger.LogInformation("➡️ {Method} {Url}", request.Method, request.RequestUri);

        request.Headers.TryAddWithoutValidation("X-Correlation-Id", Guid.NewGuid().ToString());

        if (!string.IsNullOrWhiteSpace(usertoken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", usertoken);

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(refreshToken))
            {
                logger.LogWarning("401 received for {Url}. Refreshing token...", request.RequestUri);

                var (success, token) = await authSvc.GenerateNewCoreToken(refreshToken);

                if (success && !string.IsNullOrWhiteSpace(token?.Token))
                {
                    response.Dispose();
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                    response = await base.SendAsync(request, cancellationToken);
                }
            }

            if (!response.IsSuccessStatusCode)
                logger.LogError("Request to {Url} failed with {StatusCode} ({Reason})", request.RequestUri, (int)response.StatusCode, response.ReasonPhrase);

            return response;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Request to {Url} timed out", request.RequestUri);

            return new HttpResponseMessage(HttpStatusCode.RequestTimeout)
            {
                RequestMessage = request,
                ReasonPhrase = "Request timed out"
            };
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error calling {Url}", request.RequestUri);

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                ReasonPhrase = "Network error"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while sending request to {Url}.", request.RequestUri);

            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                RequestMessage = request,
                ReasonPhrase = "Unexpected client error"
            };
        }
    }

    protected async Task<HttpResponseMessage> SendAsync2(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var userData = await session.GetCurrentUser();
        logger.LogInformation("Sending request to {Url}", request.RequestUri);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", Guid.NewGuid().ToString());
        // token = session.GetItem<string>("token");
        // refreshToken = session.GetItem<string>("refreshToken");
        var token = userData.Token;
        var refreshToken = userData.RefreshToken;

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
