namespace Broiler.Documents;

/// <summary>
/// Optional out-of-band hints about a document source (a filename or a declared
/// MIME type) that a codec probe may use to raise or lower confidence. Hints are
/// advisory only; content signatures remain authoritative.
/// </summary>
/// <param name="fileName">The filename or path of the document, if known.</param>
/// <param name="mimeType">The declared MIME type, if known.</param>
public sealed class DocumentSourceHints(string? fileName = null, string? mimeType = null)
{
    /// <summary>Empty hints specifying neither filename nor MIME type.</summary>
    public static DocumentSourceHints Empty { get; } = new();

    /// <summary>The file name or path of the document, if provided.</summary>
    public string? FileName { get; } = fileName;

    /// <summary>The declared MIME media type, if provided.</summary>
    public string? MimeType { get; } = mimeType;
}
