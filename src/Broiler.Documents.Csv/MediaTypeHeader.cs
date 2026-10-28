using System;
using System.Text;
using Broiler.Documents.Text;

namespace Broiler.Documents.Csv;

/// <summary>Parses MIME media-type parameters (such as <c>header=present</c> and <c>charset=utf-8</c>).</summary>
internal static class MediaTypeHeader
{
    public static bool TryGetParameter(string? mimeType, string parameterName, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        string[] parts = mimeType.Split(';');
        foreach (string part in parts)
        {
            int eq = part.IndexOf('=');
            if (eq < 0)
                continue;

            string name = part[..eq].Trim();
            if (string.Equals(name, parameterName, StringComparison.OrdinalIgnoreCase))
            {
                string val = part[(eq + 1)..].Trim();
                if (val.Length >= 2 && val.StartsWith('"') && val.EndsWith('"'))
                    val = val[1..^1];
                value = val;
                return true;
            }
        }

        return false;
    }

    public static Encoding? ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return null;

        string trimmed = charset.Trim();
        if (string.Equals(trimmed, "utf-8", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "utf8", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8;

        if (string.Equals(trimmed, "windows-1252", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "cp1252", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "1252", StringComparison.OrdinalIgnoreCase))
            return Windows1252.Instance;

        if (string.Equals(trimmed, "iso-8859-1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "latin1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "latin-1", StringComparison.OrdinalIgnoreCase))
            return Encoding.Latin1;

        if (string.Equals(trimmed, "us-ascii", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "ascii", StringComparison.OrdinalIgnoreCase))
            return Encoding.ASCII;

        if (string.Equals(trimmed, "utf-16", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "utf-16le", StringComparison.OrdinalIgnoreCase))
            return Encoding.Unicode;

        if (string.Equals(trimmed, "utf-16be", StringComparison.OrdinalIgnoreCase))
            return Encoding.BigEndianUnicode;

        if (string.Equals(trimmed, "utf-32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "utf-32le", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF32;

        try
        {
            return Encoding.GetEncoding(trimmed);
        }
        catch
        {
            return null;
        }
    }
}
