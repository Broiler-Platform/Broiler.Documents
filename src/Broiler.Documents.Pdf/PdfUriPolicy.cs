using System;

namespace Broiler.Documents.Pdf;

/// <summary>
/// Decides whether a URI found in, or destined for, a PDF may become an active
/// link. The policy is deliberately small, allow-list shaped, and identical on
/// the read and write sides.
/// </summary>
/// <remarks>
/// <para>
/// Two rules matter more than the details. First, a URI that a reader admitted
/// is <em>not</em> thereby authorized for output: every writer revalidates under
/// the policy in force at the moment it emits an annotation, so a document that
/// was read under a permissive policy cannot launder a link into one written
/// under a strict one. Second, validation performs no I/O of any kind — no DNS,
/// no file probing, no preflight request — so checking a link can never itself
/// contact the target (ADR 0009).
/// </para>
/// <para>
/// <c>https</c> is admitted by default. <c>http</c> and <c>mailto</c> require an
/// explicit caller opt-in. Everything else — <c>javascript</c>, <c>file</c>,
/// <c>data</c>, local and UNC paths, protocol-handler schemes, and unknown custom
/// schemes — is rejected, and the value stays inert source data with a
/// <see cref="PdfDiagnosticCodes.UriRejected"/> diagnostic.
/// </para>
/// </remarks>
public sealed class PdfUriPolicy
{
    public const int DefaultMaxLength = 2048;

    /// <summary>The default policy: absolute <c>https</c> only.</summary>
    public static PdfUriPolicy Default { get; } = new();

    public PdfUriPolicy(
        bool allowHttp = false,
        bool allowMailto = false,
        int maxLength = DefaultMaxLength)
    {
        if (maxLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxLength));

        AllowHttp = allowHttp;
        AllowMailto = allowMailto;
        MaxLength = maxLength;
    }

    /// <summary>When true, plain <c>http</c> targets are admitted as well as <c>https</c>.</summary>
    public bool AllowHttp { get; }

    /// <summary>When true, <c>mailto</c> targets are admitted.</summary>
    public bool AllowMailto { get; }

    /// <summary>Maximum admitted length, counted in characters.</summary>
    public int MaxLength { get; }

    /// <summary>
    /// Admits a URI, returning the canonical form to store. A rejection reason is
    /// returned instead of thrown, because a denied link is ordinary content, not
    /// an error.
    /// </summary>
    public bool TryAdmit(string? value, out string canonical, out string? reason)
    {
        canonical = string.Empty;
        reason = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            reason = "the value is empty";
            return false;
        }

        if (value.Length > MaxLength)
        {
            reason = "the value is longer than the policy permits";
            return false;
        }

        foreach (char c in value)
        {
            // Control characters, NUL, CR, and LF are how a link value turns into
            // a second header, a second command, or a second URI.
            if (char.IsControl(c))
            {
                reason = "the value contains a control character";
                return false;
            }
        }

        string trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            reason = "the value is not an absolute URI";
            return false;
        }

        string scheme = uri.Scheme.ToLowerInvariant();
        switch (scheme)
        {
            case "https":
                break;
            case "http" when AllowHttp:
                break;
            case "mailto" when AllowMailto:
                // A mailto target carries no authority, so the host checks below
                // do not apply to it.
                canonical = uri.AbsoluteUri;
                return true;
            default:
                reason = $"the {scheme} scheme is not admitted by the active policy";
                return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            // User information in an http(s) URI is a long-standing spoofing
            // vector and is never needed by a document link.
            reason = "the value carries user information";
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            reason = "the value has no host";
            return false;
        }

        canonical = uri.AbsoluteUri;
        return canonical.Length <= MaxLength;
    }

    /// <summary>Convenience form for callers that only need the decision.</summary>
    public bool IsAdmitted(string? value) => TryAdmit(value, out _, out _);
}
