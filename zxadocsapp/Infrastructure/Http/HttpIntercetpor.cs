using System;
using System.Net.Http.Headers;
using Microsoft.JSInterop;
using zxadocsfe.Services;

namespace zxadocsapp.Infrastructure.Http;

public class HttpCoreIntercetpor : DelegatingHandler
{
    private readonly ILogger<HttpCoreIntercetpor> logger;
    private readonly IJSRuntime jsRuntime;
    private IAuthService authSvc;
    private string token = "", refreshToken = "";
    public HttpCoreIntercetpor(ILogger<HttpCoreIntercetpor> logger, IJSRuntime jsRuntime,
    IAuthService authSvc)
    {
        this.logger = logger;
        this.jsRuntime = jsRuntime;
        this.authSvc = authSvc;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Sending request to {Url}", request.RequestUri);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", Guid.NewGuid().ToString());
        // Attempt to read a bearer token from browser storage (try common keys)
        try
        {
            // try a few common keys
            token ??= await jsRuntime.InvokeAsync<string>("localStorage.getItem", "token");
            refreshToken ??= await jsRuntime.InvokeAsync<string>("localStorage.getItem", "access_token");

            if (!string.IsNullOrWhiteSpace(token))
            {
                // if the stored value is a JSON object (e.g. { token: '...' }) the caller should store raw token string
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                logger.LogDebug("Added Authorization header to request");
            }
        }
        catch (JSException jsEx)
        {
            // If JS interop fails (e.g. server-side), ignore gracefully
            logger.LogDebug(jsEx, "Could not read token from localStorage");
        }

        var response = await base.SendAsync(request, cancellationToken);

        logger.LogInformation("⬅️ {StatusCode} for {Url}", (int)response.StatusCode, request.RequestUri);

        if (!response.IsSuccessStatusCode)
        {
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
        return response;
    }


    private void GenerateToken()
    {

    }

}
