namespace zxadocsfe.Helpers;

/// <summary>
/// Turns signature bytes into something an img src can use. The Signature column holds a server-side
/// path since ZD-111, and GET /api/users/signature stopped being anonymous, so neither value can be
/// given to the browser directly - the bytes are fetched through HttpService and inlined instead.
/// </summary>
public static class SignatureImage
{
    public static string ToDataUri(byte[] bytes, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var mime = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/png"
        };
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
