namespace Broiler.Documents;

/// <summary>
/// Optional out-of-band hints about a document source (a filename or a declared
/// MIME type) that a codec probe may use to raise or lower confidence. Hints are
/// advisory only; content signatures remain authoritative.
/// </summary>
public sealed class DocumentSourceHints(string? fileName = null, string? mimeType = null)
{
    public static DocumentSourceHints Empty { get; } = new();

    public string? FileName { get; } = fileName;

    public string? MimeType { get; } = mimeType;
}
