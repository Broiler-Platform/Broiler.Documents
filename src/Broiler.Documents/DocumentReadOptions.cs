using Broiler.Documents.Resources;
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

    /// <summary>Default document read options with safe defaults: standard limits, Windows-1252 default code page, and default resource policy.</summary>
    public static DocumentReadOptions Default { get; } = new();

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentReadOptions"/>.
    /// </summary>
    /// <param name="limits">Hard limits enforced during reading. Defaults to <see cref="DocumentLimits.Default"/>.</param>
    /// <param name="defaultCodePage">Fallback code page for <c>\'hh</c> bytes when the document declares none. Consumed by RTF; defaults to Windows-1252.</param>
    /// <param name="resourcePolicy">Policy deciding what may be done with resources encountered in the document. Defaults to <see cref="DocumentResourcePolicy.Default"/>.</param>
    public DocumentReadOptions(DocumentLimits? limits = null, int defaultCodePage = Windows1252CodePage,
        DocumentResourcePolicy? resourcePolicy = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultCodePage);

        Limits = limits ?? DocumentLimits.Default;
        DefaultCodePage = defaultCodePage;
        ResourcePolicy = resourcePolicy ?? DocumentResourcePolicy.Default;
    }

    /// <summary>The hard limits enforced while reading a document (nesting depth, run lengths, input and binary payload byte budgets).</summary>
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
    /// Decides what a codec may do with each resource it finds. Defaults to
    /// <see cref="DocumentResourcePolicy.Default"/>, which reads resources into
    /// the model and grants nothing that would put them into an output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A resource policy decides per resource and per operation — this picture
    /// may be measured but not extracted, that one may be extracted but not written
    /// into a file someone else receives.
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
