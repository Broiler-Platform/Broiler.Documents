using System.Text.Encodings.Web;
using System.Text.Json;

namespace Broiler.Documents.Cli.Infrastructure;

/// <summary>The JSON writer settings this tool uses everywhere.</summary>
/// <remarks>
/// Indented, so a person can read a failure without a formatter, and with
/// relaxed escaping so document text arrives readable rather than as a wall of
/// <c>ä</c>. The output is UTF-8 either way; the strict default escapes
/// non-ASCII for HTML-embedding safety this tool has no use for, and it would
/// make a diff of two dumps unreadable in exactly the documents where the
/// difference matters most.
/// </remarks>
public static class JsonOutput
{
    public static JsonSerializerOptions Indented { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
