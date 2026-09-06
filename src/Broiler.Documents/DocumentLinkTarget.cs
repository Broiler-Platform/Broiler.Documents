using System;

namespace Broiler.Documents;

/// <summary>
/// Which link targets the interchange codecs admit, reading and writing.
/// </summary>
/// <remarks>
/// <para>
/// One rule, in one place, because it was seven and they had drifted. Each codec
/// carried its own copy, and they disagreed about fragments: DOCX admitted any
/// <c>#anchor</c>, ODT required a non-empty one, and HTML, RTF and Markdown
/// admitted none — so an internal link survived a DOCX round trip and vanished
/// through the other three. Nobody decided that; it accumulated.
/// </para>
/// <para>
/// The same predicate answers on both sides. A target a reader admitted is not
/// thereby authorized for output: a writer asks again, so a document read under
/// one policy cannot launder a link into one written under another. That is the
/// position <c>PdfUriPolicy</c> states for the PDF codec, and it holds here for
/// the same reason.
/// </para>
/// <para>
/// <b>Admitted:</b> an absolute <c>http</c>, <c>https</c> or <c>mailto</c> URL,
/// and a non-empty fragment such as <c>#chapter</c>. <b>Refused:</b> every other
/// scheme, every relative target, and a bare <c>#</c>.
/// </para>
/// <para>
/// Another scheme — <c>javascript</c>, <c>vbscript</c>, <c>data</c>,
/// <c>file</c> — is refused because a link is inert metadata here, and a codec
/// that wrote one would be handing an active target to whatever opens the
/// document. A relative target is refused because there is nothing to resolve it
/// against: a document is not a page, it has no base, and nothing in this
/// component fetches (ADR 0004). A bare <c>#</c> names no destination, so there
/// is nothing for a round trip to preserve.
/// </para>
/// <para>
/// A fragment is admitted, and what it can promise is worth stating exactly. It
/// preserves <em>a reference the source document made</em>, not a jump that
/// works: no codec here reads or writes a bookmark, and the model has nowhere to
/// put one, so the name a fragment points at is not carried and the link lands
/// nowhere. Keeping it is still right — dropping it would discard something the
/// source said, and two of the five codecs have always kept it — but the
/// conformance documents say "reference", not "working link", and the gap
/// belongs to the missing bookmark rather than to this rule.
/// </para>
/// <para>
/// A control character is refused, which is correctness rather than policy: the
/// package formats put the target in an XML attribute, XML 1.0 cannot represent
/// one, and handing it over threw — so the tool reported an internal error where
/// a diagnostic belonged.
/// </para>
/// <para>
/// Deliberately absent: a length cap, user-information rejection, and
/// canonicalization. <c>PdfUriPolicy</c> has all three because a PDF annotation
/// is a different risk surface and its policy is configurable. Here each would
/// refuse or rewrite links that work today, and no defect argues for one, so
/// they are a decision for whoever wants them rather than a side effect of
/// removing duplication.
/// </para>
/// </remarks>
public static class DocumentLinkTarget
{
    /// <summary>
    /// Whether a codec may carry <paramref name="target"/> as a live link.
    /// </summary>
    /// <remarks>
    /// A refusal is not an error. The caller writes the run as plain text,
    /// keeping its other formatting, and reports its own <c>*.link</c>
    /// diagnostic — the loss is content, and content losses are reported rather
    /// than thrown.
    /// </remarks>
    public static bool IsAllowed(string? target)
    {
        if (string.IsNullOrEmpty(target))
            return false;

        for (int i = 0; i < target.Length; i++)
        {
            char character = target[i];

            // Checked before anything else parses it. Uri admits several of
            // these, and the failure would then arrive from an XML writer
            // several layers away from the value that caused it. An unpaired
            // surrogate is rejected here too, for the same reason: it is not a
            // character XML can carry.
            if (char.IsControl(character))
                return false;

            if (char.IsSurrogate(character) &&
                !(char.IsHighSurrogate(character) &&
                  i + 1 < target.Length &&
                  char.IsLowSurrogate(target[i + 1]) &&
                  ++i > 0))
            {
                return false;
            }
        }

        // A same-document reference. Not parsed as a URI: Uri.TryCreate
        // rejects a bare fragment as absolute, and what matters here is only
        // that it names something.
        //
        // The name it carries is constrained, because the formats that spell
        // one do not agree on how to quote it. RTF writes it as a quoted field
        // argument and has no escape for a quote inside one, so admitting a
        // quote truncated the target at it and still reported success -- the
        // silent loss this predicate exists to prevent. Whitespace is refused
        // because it is HTML's own rule for the id a fragment resolves
        // against, and a second '#' because RFC 3986 does not put one in a
        // fragment. Everything else a reader found is carried.
        if (target[0] == '#')
        {
            if (target.Length == 1)
                return false;

            foreach (char character in target.AsSpan(1))
            {
                if (character is '"' or '#' || char.IsWhiteSpace(character))
                    return false;
            }

            return true;
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }
}
