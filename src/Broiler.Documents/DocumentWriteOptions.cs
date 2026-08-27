namespace Broiler.Documents;

/// <summary>
/// Knobs for writing a document. Format-neutral at this level; format-specific
/// options derive from these (for example <c>PdfWriteOptions</c>).
/// </summary>
/// <remarks>
/// Open for derivation for the same reason as <see cref="DocumentReadOptions"/>:
/// a codec carries its own immutable options through the shared
/// <see cref="DocumentCodec.Write(Model.RichTextDocument, System.IO.Stream, DocumentWriteOptions)"/> signature (PDF roadmap §6.1).
/// </remarks>
public class DocumentWriteOptions
{
    public static DocumentWriteOptions Default { get; } = new();

    public DocumentWriteOptions(bool asciiOnly = true)
    {
        AsciiOnly = asciiOnly;
    }

    /// <summary>
    /// When true, non-ASCII characters are escaped into the format's portable
    /// representation (for RTF, <c>\uN</c> with an ASCII fallback char) rather
    /// than emitted as raw bytes.
    /// </summary>
    /// <remarks>
    /// True is the only value the RTF writer implements, and the other codecs do
    /// not consult it at all. A writer asked for the unimplemented value reports
    /// it rather than quietly doing the opposite; see <c>RtfWriteOptions</c>.
    /// </remarks>
    public bool AsciiOnly { get; }
}
