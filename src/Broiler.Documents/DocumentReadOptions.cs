using System;

namespace Broiler.Documents;

/// <summary>
/// Knobs for reading a document. Format-neutral at this level; format-specific
/// options derive from these (for example <c>PdfReadOptions</c>). Defaults are
/// safe: embedded binary objects are <b>not</b> decoded (ADR 0004).
/// </summary>
/// <remarks>
/// The type is open for derivation so a codec can carry its own immutable option
/// object through the shared <see cref="DocumentCodec.Read(System.IO.Stream, DocumentReadOptions)"/> signature. Only
/// behavior genuinely shared by several codecs belongs here; a codec validates
/// the concrete option type it was handed rather than downcasting opportunistically
/// (PDF roadmap §6.1).
/// </remarks>
public class DocumentReadOptions
{
    /// <summary>Windows-1252, the RTF default when no <c>\ansicpg</c> is present.</summary>
    public const int Windows1252CodePage = 1252;

    public static DocumentReadOptions Default { get; } = new();

    public DocumentReadOptions(
        DocumentLimits? limits = null,
        int defaultCodePage = Windows1252CodePage,
        bool decodeEmbeddedObjects = false)
    {
        if (defaultCodePage <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultCodePage));

        Limits = limits ?? DocumentLimits.Default;
        DefaultCodePage = defaultCodePage;
        DecodeEmbeddedObjects = decodeEmbeddedObjects;
    }

    public DocumentLimits Limits { get; }

    /// <summary>
    /// Fallback code page for <c>\'hh</c> bytes when the document declares none.
    /// </summary>
    /// <remarks>
    /// Consumed by the RTF codec only; every other codec ignores it. New callers
    /// should set it through <c>RtfReadOptions</c>, which is where it belongs —
    /// it stays here because moving the storage would break existing callers
    /// without changing any behavior.
    /// </remarks>
    public int DefaultCodePage { get; }

    /// <summary>
    /// Asks a codec to decode embedded images through a delegated image codec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No codec in this release acts on it.</b> The RTF reader skips
    /// <c>\pict</c> and object destinations whichever way it is set, and the PDF
    /// codec composes no image decoder at all. A codec that is asked for it and
    /// cannot provide it emits
    /// <see cref="DocumentDiagnosticCodes.CapabilityNotComposed"/>, so a caller
    /// that expected images finds out rather than receiving a document silently
    /// missing them.
    /// </para>
    /// <para>
    /// The setting is kept, rather than removed, because the eventual
    /// caller-composed image path is a planned capability with a designed shape:
    /// bounded, delegated, and explicitly permitted. Embedded OLE objects are
    /// never instantiated regardless of this value (ADR 0004).
    /// </para>
    /// </remarks>
    public bool DecodeEmbeddedObjects { get; }
}
