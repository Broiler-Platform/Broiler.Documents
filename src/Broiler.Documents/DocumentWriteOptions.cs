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

    public DocumentWriteOptions(bool asciiOnly = true, DocumentConversionContext? resources = null)
    {
        AsciiOnly = asciiOnly;
        Resources = resources ?? DocumentConversionContext.Empty;
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

    /// <summary>
    /// The decisions made about the resources in the document being written.
    /// Defaults to <see cref="DocumentConversionContext.Empty"/>, which permits
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pass the context a read returned to carry its decisions into the write.
    /// That is what stops a resource laundering its permissions by changing
    /// format: a picture read out of a PDF under a policy that allowed extraction
    /// but not redistribution reaches a DOCX writer with the same entry, rather
    /// than with whatever the DOCX writer would have assumed.
    /// </para>
    /// <para>
    /// Passing nothing is not an oversight to be forgiven. A write whose caller
    /// recorded no origin for its pictures cannot say they may be redistributed,
    /// so the empty context omits them and reports each one.
    /// </para>
    /// </remarks>
    public DocumentConversionContext Resources { get; }
}
