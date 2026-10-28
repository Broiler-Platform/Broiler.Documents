using System;

namespace Broiler.Documents;

/// <summary>
/// A codec's verdict on a byte prefix. Mirrors <c>Broiler.Media</c>'s probe
/// result: <see cref="DocumentProbeConfidence.None"/> means "not my format".
/// </summary>
public sealed class DocumentProbeResult
{
    private DocumentProbeResult(DocumentProbeConfidence confidence, string? formatName, string? mimeType,
        long? bytesConsumed, string? diagnostic)
    {
        if (confidence is < DocumentProbeConfidence.None or > DocumentProbeConfidence.Certain)
            throw new ArgumentOutOfRangeException(nameof(confidence));

        if (bytesConsumed < 0)
            throw new ArgumentOutOfRangeException(nameof(bytesConsumed));

        Confidence = confidence;
        FormatName = formatName;
        MimeType = mimeType;
        BytesConsumed = bytesConsumed;
        Diagnostic = diagnostic;
    }

    /// <summary>The level of confidence that the byte prefix matches the format.</summary>
    public DocumentProbeConfidence Confidence { get; }

    /// <summary>The uppercase format name claimed by the probing codec.</summary>
    public string? FormatName { get; }

    /// <summary>The MIME media type matched, if identified.</summary>
    public string? MimeType { get; }

    /// <summary>The number of bytes from the prefix examined to establish the match.</summary>
    public long? BytesConsumed { get; }

    /// <summary>Optional diagnostic message describing why the prefix matched or failed to match.</summary>
    public string? Diagnostic { get; }

    /// <summary>True when <see cref="Confidence"/> is greater than <see cref="DocumentProbeConfidence.None"/>.</summary>
    /// <summary>True when <see cref="Confidence"/> is greater than <see cref="DocumentProbeConfidence.None"/>.</summary>
    public bool IsMatch => Confidence != DocumentProbeConfidence.None;

    /// <summary>Creates a negative probe result indicating the bytes do not match the format.</summary>
    public static DocumentProbeResult NoMatch(string? diagnostic = null) =>
        new(DocumentProbeConfidence.None, null, null, null, diagnostic);

    /// <summary>Creates a positive probe result with the specified confidence and format name.</summary>
    public static DocumentProbeResult Match(DocumentProbeConfidence confidence, string formatName, 
        string? mimeType = null, long? bytesConsumed = null, string? diagnostic = null)
    {
        if (confidence == DocumentProbeConfidence.None)
            throw new ArgumentException("Matched probe results need positive confidence.", nameof(confidence));
        if (string.IsNullOrWhiteSpace(formatName))
            throw new ArgumentException("Matched probe results need a format name.", nameof(formatName));

        return new DocumentProbeResult(confidence, formatName, mimeType, bytesConsumed, diagnostic);
    }
}
