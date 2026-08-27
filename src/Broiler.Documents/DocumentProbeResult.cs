using System;

namespace Broiler.Documents;

/// <summary>
/// A codec's verdict on a byte prefix. Mirrors <c>Broiler.Media</c>'s probe
/// result: <see cref="DocumentProbeConfidence.None"/> means "not my format".
/// </summary>
public sealed class DocumentProbeResult
{
    private DocumentProbeResult(
        DocumentProbeConfidence confidence,
        string? formatName,
        string? mimeType,
        long? bytesConsumed,
        string? diagnostic)
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

    public DocumentProbeConfidence Confidence { get; }

    public string? FormatName { get; }

    public string? MimeType { get; }

    public long? BytesConsumed { get; }

    public string? Diagnostic { get; }

    public bool IsMatch => Confidence != DocumentProbeConfidence.None;

    public static DocumentProbeResult NoMatch(string? diagnostic = null) =>
        new(DocumentProbeConfidence.None, null, null, null, diagnostic);

    public static DocumentProbeResult Match(
        DocumentProbeConfidence confidence,
        string formatName,
        string? mimeType = null,
        long? bytesConsumed = null,
        string? diagnostic = null)
    {
        if (confidence == DocumentProbeConfidence.None)
            throw new ArgumentException("Matched probe results need positive confidence.", nameof(confidence));
        if (string.IsNullOrWhiteSpace(formatName))
            throw new ArgumentException("Matched probe results need a format name.", nameof(formatName));

        return new DocumentProbeResult(confidence, formatName, mimeType, bytesConsumed, diagnostic);
    }
}
