using System;

namespace zxadocsui.Helpers;

public static class CustomExtentions
{
    public static string GetInitials(this string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString().ToUpper();

        return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
    }
}
