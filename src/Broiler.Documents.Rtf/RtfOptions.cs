using System;

namespace Broiler.Documents.Rtf;

/// <summary>
/// RTF-specific read options.
/// </summary>
/// <remarks>
/// <para>
/// Only settings no other codec can use belong to a format. That turns out to be
/// a shorter list than the roadmap assumed: the group-depth and binary-payload
/// limits look like RTF concepts, but DOCX enforces both as well — style
/// inheritance depth and part size — so under the repository's own containment
/// rule they are genuinely shared and stay on <see cref="DocumentLimits"/>.
/// The default code page is the one read setting with a single consumer.
/// </para>
/// <para>
/// This type does not redeclare that setting; it fixes where callers should set
/// it. Passing the plain <see cref="DocumentReadOptions"/> still works and still
/// carries the code page, because moving the storage would have broken every
/// existing caller for no behavioral gain. Passing <em>another</em> codec's
/// option type is a structured rejection rather than a silent fallback.
/// </para>
/// </remarks>
public sealed class RtfReadOptions : DocumentReadOptions
{
    /// <summary>Windows-1252, the RTF default when no <c>\ansicpg</c> is present.</summary>
    public new const int Windows1252CodePage = DocumentReadOptions.Windows1252CodePage;

    public static new RtfReadOptions Default { get; } = new();

    public RtfReadOptions(
        DocumentLimits? limits = null,
        int defaultCodePage = Windows1252CodePage)
        : base(limits, defaultCodePage)
    {
    }
}

/// <summary>
/// RTF-specific write options.
/// </summary>
/// <remarks>
/// The RTF writer always escapes non-ASCII as <c>\uN?</c> under <c>\uc1</c>, so
/// <see cref="DocumentWriteOptions.AsciiOnly"/> has exactly one implemented
/// value. This type pins it, and the writer reports a caller that asks for the
/// other one instead of silently ignoring the request — a public knob that reads
/// as configurable but is not is precisely the contract defect the component
/// roadmap asks this migration to clear.
/// </remarks>
public sealed class RtfWriteOptions : DocumentWriteOptions
{
    public static new RtfWriteOptions Default { get; } = new();

    public RtfWriteOptions()
        : base(asciiOnly: true)
    {
    }
}
