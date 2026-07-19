using System.Text.Json;

namespace zxadocsfe.Services;

// Extracts a human-friendly message from an API error body, which may be an ApiResponse
// ({ "message": ... }) or a ProblemDetails ({ "title"/"detail": ... }). Falls back to the
// raw body so nothing is ever swallowed.
public static class ErrorMessage
{
    public static string? Extract(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var key in new[] { "message", "detail", "title" })
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
        }
        catch (JsonException) { /* not JSON — return the raw body */ }
        return body;
    }
}
