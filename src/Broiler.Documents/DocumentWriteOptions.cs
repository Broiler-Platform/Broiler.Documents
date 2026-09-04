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

    public DocumentWriteOptions(
        bool asciiOnly = true,
        DocumentConversionContext? resources = null,
        DocumentFontSet? fonts = null,
        DocumentMetadata? metadata = null)
    {
        AsciiOnly = asciiOnly;
        Resources = resources ?? DocumentConversionContext.Empty;
        Fonts = fonts ?? DocumentFontSet.None;
        Metadata = metadata ?? DocumentMetadata.Empty;
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

    /// <summary>
    /// The fonts the caller provisioned for this write. Defaults to
    /// <see cref="DocumentFontSet.None"/>.
    /// </summary>
    /// <remarks>
    /// PDF roadmap §11.3's chosen path: a writer takes fonts from here or from
    /// nowhere. This project bundles no fallback and holds no font licence, so a
    /// caller that provisions nothing gets a writer that reports what it could
    /// not write rather than one that reaches for whatever the machine has
    /// installed.
    /// </remarks>
    public DocumentFontSet Fonts { get; }

    /// <summary>
    /// The metadata to emit. Defaults to <see cref="DocumentMetadata.Empty"/>,
    /// which emits none.
    /// </summary>
    /// <remarks>
    /// It comes from the caller, never from a read result: having read what a
    /// document says about itself is not authority to republish it under someone
    /// else's name, and a writer that quietly carried a source's author and
    /// producer forward would do exactly that. A caller who wants the transfer
    /// writes it out, and may correct any field with
    /// <see cref="DocumentMetadata.With"/> first.
    /// </remarks>
    public DocumentMetadata Metadata { get; }
}
