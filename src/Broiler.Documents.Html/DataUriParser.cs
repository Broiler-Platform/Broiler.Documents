using System;
using System.Text;

namespace Broiler.Documents.Html;

internal static class DataUriParser
{
    public static bool TryParse(
        string uri,
        out string mediaType,
        out ReadOnlyMemory<byte> data,
        out string? error)
    {
        mediaType = string.Empty;
        data = default;
        error = null;

        if (string.IsNullOrWhiteSpace(uri))
        {
            error = "Empty URI.";
            return false;
        }

        string trimmed = uri.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            error = "URI does not start with data:.";
            return false;
        }

        int commaIndex = trimmed.IndexOf(',');
        if (commaIndex < 0)
        {
            error = "data URI has no comma separating metadata from payload.";
            return false;
        }

        string meta = trimmed[5..commaIndex].Trim();
        string payload = trimmed[(commaIndex + 1)..].Trim();

        bool isBase64 = false;
        string type = string.Empty;

        if (meta.Length > 0)
        {
            string[] parts = meta.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string part in parts)
            {
                if (part.Equals("base64", StringComparison.OrdinalIgnoreCase))
                {
                    isBase64 = true;
                }
                else if (type.Length == 0 && part.Contains('/'))
                {
                    type = part.ToLowerInvariant();
                }
            }
        }

        if (type.Length == 0)
        {
            type = "image/png";
        }

        mediaType = type;

        if (payload.Length == 0)
        {
            error = "Data URI payload is empty.";
            return false;
        }

        if (isBase64)
        {
            string cleaned = CleanBase64(payload);
            try
            {
                byte[] bytes = Convert.FromBase64String(cleaned);
                if (bytes.Length == 0)
                {
                    error = "Base64 payload decoded to zero bytes.";
                    return false;
                }
                data = bytes;
                return true;
            }
            catch (FormatException)
            {
                error = "Malformed base64 payload.";
                return false;
            }
        }
        else
        {
            try
            {
                string unescaped = Uri.UnescapeDataString(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(unescaped);
                if (bytes.Length == 0)
                {
                    error = "Percent-encoded payload decoded to zero bytes.";
                    return false;
                }
                data = bytes;
                return true;
            }
            catch (Exception ex)
            {
                error = "Malformed data URI payload: " + ex.Message;
                return false;
            }
        }
    }

    private static string CleanBase64(string s)
    {
        if (s.IndexOfAny([' ', '\t', '\r', '\n']) < 0)
            return s;

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is not (' ' or '\t' or '\r' or '\n'))
                sb.Append(c);
        }
        return sb.ToString();
    }
}
