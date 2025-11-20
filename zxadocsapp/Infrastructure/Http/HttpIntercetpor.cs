using System;
using System.Net.Http.Headers;
using Microsoft.JSInterop;
using zxadocsapp.State;
using zxadocsapp.States;
using zxadocsfe.Services;
using static zxadocsfe.Helpers.AppConstants;

namespace zxadocsapp.Infrastructure.Http;

public class HttpCoreIntercetpor : DelegatingHandler
{
    private readonly ILogger<HttpCoreIntercetpor> logger;
    private IAuthService authSvc;
    private readonly RequestContext session;

    public HttpCoreIntercetpor(ILogger<HttpCoreIntercetpor> logger,
        IAuthService authSvc, RequestContext session)
    {
        this.logger = logger;
        this.authSvc = authSvc;
        this.session = session;
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
}
