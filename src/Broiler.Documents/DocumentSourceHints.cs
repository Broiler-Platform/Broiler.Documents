namespace Broiler.Documents;

/// <summary>
/// Optional out-of-band hints about a document source (a filename or a declared
/// MIME type) that a codec probe may use to raise or lower confidence. Hints are
/// advisory only; content signatures remain authoritative.
/// </summary>
public sealed class DocumentSourceHints
{
    public static DocumentSourceHints Empty { get; } = new();

    public DocumentSourceHints(string? fileName = null, string? mimeType = null)
    {
        FileName = fileName;
        MimeType = mimeType;
    }

    public string? FileName { get; }

    public string? MimeType { get; }
}
