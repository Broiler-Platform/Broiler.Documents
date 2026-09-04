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
        bool decodeEmbeddedObjects = false,
        DocumentResourcePolicy? resourcePolicy = null)
    {
        if (defaultCodePage <= 0)
            throw new ArgumentOutOfRangeException(nameof(defaultCodePage));

        Limits = limits ?? DocumentLimits.Default;
        DefaultCodePage = defaultCodePage;
        // The declaring type assigning its own announced member (ADR 0014). It
        // keeps working until removal, so the constructor still sets it.
#pragma warning disable CS0618
        DecodeEmbeddedObjects = decodeEmbeddedObjects;
#pragma warning restore CS0618
        ResourcePolicy = resourcePolicy ?? DocumentResourcePolicy.Default;
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
    /// <strong>Announced for removal (ADR 0014).</strong> The capability it was
    /// holding a place for arrived as <see cref="ResourcePolicy"/>, which answers
    /// the question this one could only gesture at. It keeps working until a
    /// later release removes it; embedded OLE objects are never instantiated
    /// regardless of its value (ADR 0004).
    /// </para>
    /// </remarks>
    [Obsolete(
        "A boolean cannot express what a caller needs to say about a resource. Use " +
        "DocumentReadOptions.ResourcePolicy, which decides per resource and per " +
        "operation - whether a picture may be read into the model is a different " +
        "question from whether it may be written into a file somebody else " +
        "receives, and this flag conflated them. The read result carries those " +
        "decisions forward as a DocumentConversionContext.")]
    public bool DecodeEmbeddedObjects { get; }

    /// <summary>
    /// Decides what a codec may do with each resource it finds. Defaults to
    /// <see cref="DocumentResourcePolicy.Default"/>, which reads resources into
    /// the model and grants nothing that would put them into an output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what <see cref="DecodeEmbeddedObjects"/> should have been. A
    /// boolean can only ask "may images happen"; the questions that actually
    /// matter are per-resource and per-operation — this picture may be measured
    /// but not extracted, that one may be extracted but not written into a file
    /// someone else receives — and a flag cannot express any of them.
    /// </para>
    /// <para>
    /// The default reads pictures into the model, because a caller who opened a
    /// document asked to see what is in it. What it withholds is everything that
    /// would put them into an output: acceptance by a reader is not consent to
    /// write. The read result carries the decisions back as a
    /// <see cref="DocumentConversionContext"/>, so a later write honours what
    /// this policy said rather than guessing again.
    /// </para>
    /// </remarks>
    public DocumentResourcePolicy ResourcePolicy { get; }
}
