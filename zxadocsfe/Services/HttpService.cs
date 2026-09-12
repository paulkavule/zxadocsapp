using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Logging;
using fedtos = zxadocsfe.Dtos;

namespace zxadocsfe.Services;

public class ProgressTrackingStreamContent : StreamContent
{
    private readonly Stream _stream;
    private readonly IProgress<double> _progress;
    private readonly long _totalBytes;
    private readonly CancellationToken _cancellationToken;

    public ProgressTrackingStreamContent(
        Stream stream,
        int bufferSize,
        IProgress<double> progress,
        CancellationToken cancellationToken) : base(stream, bufferSize)
    {
        _stream = stream;
        _progress = progress;
        _totalBytes = stream.Length;
        _cancellationToken = cancellationToken;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[4096];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cancellationToken)) != 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            await stream.WriteAsync(buffer, 0, bytesRead, _cancellationToken);
            totalBytesRead += bytesRead;

            var progressPercentage = ((double)totalBytesRead / _totalBytes) * 100;
            _progress.Report(progressPercentage);
        }
    }
}


/// <summary>
/// Represents a void/empty response type
/// </summary>
public sealed class Unit
{
    private Unit() { }
    public static Unit Value { get; } = new();
}

public enum HttpVerb
{
    Get,
    Post,
    Put,
    Patch,
    Delete
}

public interface ITokenProvider
{
    string? AccessToken { get; }
}

/// <summary>
/// The organisation a system user is currently acting on (ZD-131). Sent on every request so the
/// API can substitute it once, in PermissionGate.Resolve, rather than every screen passing an id.
/// Null means "my own organisation"; AllOrganisations is a read-only view the API refuses on writes.
/// </summary>
public interface IActingOrganisation
{
    /// <summary>The header value to send, or null to send nothing.</summary>
    string? HeaderValue { get; }
}

public interface IHttpService
{
    void Initialize(string scheme);
    Task<(bool success, T? data, string? error)> GetAsync<T>(string endpoint, List<fedtos.KeyValue>? headers = null);
    Task<(bool success, T? data, string? error)> ExecuteRequestAsync<T>(HttpVerb method, string endpoint, object? data = null, List<fedtos.KeyValue>? headers = null);
    /// <summary>Posts a signature image to /api/users/signature, keyed on the user's reference.</summary>
    /// <summary>Posts one file as multipart/form-data under the field name "file".</summary>
    Task<(bool success, T? data, string? error)> UploadFileAsync<T>(
        string endpoint, byte[] content, string fileName, string contentType);

    Task<(bool success, T? data, string? error)> UploadUserSignatureAsync<T>(
        Guid userReference, byte[] content, string fileName);

    Task<(bool success, T? data, string? error)> UploadDocumentAsync<T>(string endpoint, byte[] docContent, string userId,
    string documentRef, string fileName, string folder = "General", List<fedtos.KeyValue>? headers = null);
    Task<(bool success, byte[]? data, string? error)> AppendDocumentAsync(string endpoint, byte[] docContent, string fileName, List<fedtos.KeyValue>? headers = null);
    Task<(bool success, T? data, string? error)> UploadFileWithProgressAsync<T>(
        string endpoint,
        Stream fileStream,
        string fileName,
        Dictionary<string, string> additionalFields,
        IProgress<double> progress,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<byte[]> DownloadDocumentFileAsync(int docId, int bufferSize = 81920, bool isSignature = false, CancellationToken ct = default);

    // Raw bytes for an arbitrary GET (e.g. draft preview/download PDFs) — no JSON deserialisation.
    Task<(bool success, byte[]? data, string? error)> GetBytesAsync(string endpoint, CancellationToken ct = default);

}

public class HttpService : IHttpService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpService> _logger;
    private readonly ITokenProvider _tokenProvider;
    private readonly IActingOrganisation _actingOrganisation;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private HttpClient? _client;

    public HttpService(IHttpClientFactory httpClientFactory, ILogger<HttpService> logger,
        ITokenProvider tokenProvider, IActingOrganisation actingOrganisation)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _tokenProvider = tokenProvider;
        _actingOrganisation = actingOrganisation;
    }

    public void Initialize(string scheme)
    {
        _client = _httpClientFactory?.CreateClient(scheme);
    }

    private void ApplyAuthorization()
    {
        if (_client is null) return;

        var token = _tokenProvider.AccessToken;
        _client.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);

        // Attached here rather than at each call site: ApplyAuthorization already runs before every
        // request, so this is the one place that cannot be forgotten.
        _client.DefaultRequestHeaders.Remove(ActingOrganisationHeader);
        var acting = _actingOrganisation.HeaderValue;
        if (!string.IsNullOrWhiteSpace(acting))
            _client.DefaultRequestHeaders.TryAddWithoutValidation(ActingOrganisationHeader, acting);
    }

    public const string ActingOrganisationHeader = "X-Acting-Organisation";
    public Task<(bool success, T? data, string? error)> GetAsync<T>(string endpoint, List<fedtos.KeyValue>? headers = null)
    {
        return ExecuteRequestAsync<T>(HttpVerb.Get, endpoint, null, headers);
    }

    public async Task<(bool success, T? data, string? error)> ExecuteRequestAsync<T>(HttpVerb method, string endpoint, object? data = null, List<fedtos.KeyValue>? headers = null)
    {
        ApplyAuthorization();
        try
        {
            // Apply any custom headers
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _client?.DefaultRequestHeaders.TryAddWithoutValidation(header.Name, header.Value);
                }
            }

            HttpResponseMessage response = method switch
            {
                HttpVerb.Get => await _client!.GetAsync(endpoint),
                HttpVerb.Post => await _client!.PostAsJsonAsync(endpoint, data, _jsonOptions),
                HttpVerb.Put => await _client!.PutAsJsonAsync(endpoint, data, _jsonOptions),
                HttpVerb.Patch => await _client!.PatchAsJsonAsync(endpoint, data, _jsonOptions),
                HttpVerb.Delete => await _client!.DeleteAsync(endpoint),
                _ => throw new ArgumentException($"Unsupported HTTP method: {method}")
            };

            if (response.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(Unit)) // Handle void/empty responses
                {
                    return (true, default, null);
                }
                var resultStr = response.Content.ReadAsStringAsync();
                Console.WriteLine("Response String: " + resultStr.Result);
                var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
                return (true, result, null);
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("{Method} {Endpoint} failed with status {Status}: {Error}",
                method, endpoint, response.StatusCode, error);
            return (false, default(T), error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Method} {Endpoint} failed with exception", method, endpoint);
            return (false, default(T), ex.Message);
        }
        finally
        {
            // Clear any custom headers we added
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _client!.DefaultRequestHeaders.Remove(header.Name);
                }
            }
        }
    }

    public async Task<(bool success, T? data, string? error)> UploadFileAsync<T>(
        string endpoint, byte[] content, string fileName, string contentType)
    {
        ApplyAuthorization();
        try
        {
            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(content);
            if (!string.IsNullOrWhiteSpace(contentType))
                part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(part, "file", fileName);

            var response = await _client!.PostAsync(endpoint, form);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return (false, default, body);

            var result = string.IsNullOrWhiteSpace(body)
                ? default
                : System.Text.Json.JsonSerializer.Deserialize<T>(body, _jsonOptions);
            return (true, result, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File upload to {Endpoint} failed", endpoint);
            return (false, default, ex.Message);
        }
    }

    public async Task<(bool success, T? data, string? error)> UploadUserSignatureAsync<T>(
        Guid userReference, byte[] content, string fileName)
    {
        ApplyAuthorization();
        try
        {
            // The endpoint binds UserReference and File; it is not the /api/upload shape.
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(content), "file", fileName);
            form.Add(new StringContent(userReference.ToString()), "userReference");

            var response = await _client!.PostAsync("api/users/signature", form);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (false, default, body);

            var result = string.IsNullOrWhiteSpace(body)
                ? default
                : System.Text.Json.JsonSerializer.Deserialize<T>(body, _jsonOptions);
            return (true, result, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signature upload failed");
            return (false, default, ex.Message);
        }
    }

    public async Task<(bool success, T? data, string? error)> UploadDocumentAsync<T>(string endpoint, byte[] docContent, string userId,
    string documentRef, string fileName, string folder = "General", List<fedtos.KeyValue>? headers = null)
    {
        ApplyAuthorization();
        try
        {
            var content = new MultipartFormDataContent();
            var byteArrayContent = new ByteArrayContent(docContent);
            content.Add(byteArrayContent, "file", fileName);
            content.Add(new StringContent(userId), "userId");
            content.Add(new StringContent(documentRef), "documentReference");
            content.Add(new StringContent(folder), "folder");

            var response = await _client!.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(Unit))
                {
                    return (true, default, null);
                }
                var resultStr = response.Content.ReadAsStringAsync();
                Console.WriteLine("Response String: " + resultStr.Result);
                var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
                return (true, result, null);
            }

            var error = await response.Content.ReadAsStringAsync();
            return (false, default(T), error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadDocumentAsync to {Endpoint} failed with exception", endpoint);
            throw;
        }
        finally
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _client!.DefaultRequestHeaders.Remove(header.Name);
                }
            }
        }
    }

    public async Task<(bool success, byte[]? data, string? error)> AppendDocumentAsync(string endpoint, byte[] docContent, string fileName, List<fedtos.KeyValue>? headers = null)
    {
        ApplyAuthorization();
        try
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _client?.DefaultRequestHeaders.TryAddWithoutValidation(header.Name, header.Value);
                }
            }

            using var content = new MultipartFormDataContent();
            using var byteArrayContent = new ByteArrayContent(docContent);
            byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(byteArrayContent, "file", fileName);

            var response = await _client!.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                return (true, fileBytes, null);
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("AppendDocumentAsync to {Endpoint} failed with status {Status}: {Error}",
                endpoint, response.StatusCode, error);
            return (false, null, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AppendDocumentAsync to {Endpoint} failed with exception", endpoint);
            return (false, null, ex.Message);
        }
        finally
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _client!.DefaultRequestHeaders.Remove(header.Name);
                }
            }
        }
    }

    public async Task<(bool success, T? data, string? error)> UploadFileWithProgressAsync<T>(
        string endpoint,
        Stream fileStream,
        string fileName,
        Dictionary<string, string> additionalFields,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        ApplyAuthorization();
        try
        {
            // Create a wrapper stream that can track progress
            var streamContent = new ProgressTrackingStreamContent(
                fileStream,
                4096, // 4KB buffer size
                progress,
                cancellationToken
            );

            using var content = new MultipartFormDataContent();

            // Add the file content
            content.Add(streamContent, "file", fileName);

            // Add additional fields
            foreach (var field in additionalFields)
            {
                content.Add(new StringContent(field.Value), field.Key);
            }

            var response = await _client!.PostAsync(endpoint, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(Unit))
                {
                    return (true, default, null);
                }

                var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
                return (true, result, null);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Upload to {Endpoint} failed with status {Status}: {Error}",
                endpoint, response.StatusCode, error);
            return (false, default, error);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Upload to {Endpoint} was cancelled", endpoint);
            return (false, default, "Upload cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload to {Endpoint} failed with exception", endpoint);
            return (false, default, ex.Message);
        }
    }


    public async Task<(bool success, byte[]? data, string? error)> GetBytesAsync(string endpoint, CancellationToken ct = default)
    {
        ApplyAuthorization();
        try
        {
            var response = await _client!.GetAsync(endpoint, ct);
            if (response.IsSuccessStatusCode)
                return (true, await response.Content.ReadAsByteArrayAsync(ct), null);

            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("GET(bytes) {Endpoint} failed with status {Status}: {Error}", endpoint, response.StatusCode, error);
            return (false, null, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET(bytes) {Endpoint} failed with exception", endpoint);
            return (false, null, ex.Message);
        }
    }

    public async IAsyncEnumerable<byte[]> DownloadDocumentFileAsync(int docId, int bufferSize = 81920, bool isSignature = false, CancellationToken ct = default)
    {
        ApplyAuthorization();
        var endpoint = isSignature ? $"api/users/signature?ref={docId.ToString()}" : $"api/documents/content/{docId}";
        var response = await _client!.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[bufferSize];

        int bytesRead;
        while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            // copy exact-sized chunk
            var chunk = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
            yield return chunk;
        }
    }
}
