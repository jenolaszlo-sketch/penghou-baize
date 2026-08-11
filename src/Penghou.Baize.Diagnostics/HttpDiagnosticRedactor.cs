using System.Net.Http.Headers;
using System.Text;

namespace Penghou.Baize.Diagnostics;

internal static class HttpDiagnosticRedactor
{
    private static readonly HashSet<string> SensitiveHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Proxy-Authorization",
            "Cookie",
            "Set-Cookie",
            "X-Api-Key",
            "Api-Key",
            "X-Goog-Api-Key"
        };

    private static readonly HashSet<string> SensitiveQueryParameters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "key",
            "api_key",
            "apikey",
            "access_token",
            "token",
            "secret",
            "signature",
            "sig",
            // Gemini resumable upload URLs are bearer-like capabilities.
            "upload_id"
        };

    public static string RedactUri(Uri? uri)
    {
        if (uri is null || string.IsNullOrEmpty(uri.Query))
            return uri?.ToString() ?? "[No URI]";

        var builder = new UriBuilder(uri);
        var query = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(RedactQueryPart);
        builder.Query = string.Join('&', query);
        return builder.Uri.ToString();
    }

    public static void AppendHeaders(StringBuilder builder, HttpHeaders headers)
    {
        foreach (var header in headers)
        {
            var value = SensitiveHeaders.Contains(header.Key)
                ? "[REDACTED]"
                : string.Join(", ", header.Value);
            builder.AppendLine($"{header.Key}: {value}");
        }
    }

    private static string RedactQueryPart(string part)
    {
        var separator = part.IndexOf('=');
        var encodedName = separator < 0 ? part : part[..separator];
        var name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));
        return SensitiveQueryParameters.Contains(name)
            ? $"{encodedName}=[REDACTED]"
            : part;
    }
}
