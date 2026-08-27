using System;
using System.Collections.Generic;
using System.Threading;

namespace Broiler.Documents.Pdf.Filters;

/// <summary>
/// A PDF stream filter (ISO 32000-1 clause 7.4). This is the codec's primary
/// extension point: the base package composes only filters whose algorithm and
/// implementation are Broiler-authored and carry no third-party dependency, and
/// a caller adds a reviewed implementation of any remaining filter by putting it
/// into <see cref="PdfCodecServices"/>.
/// </summary>
/// <remarks>
/// <para>
/// A filter that is not composed is <em>detected and skipped</em>, never guessed
/// at: the reader emits the filter's stable diagnostic and leaves the stream
/// undecoded. That is what lets LZW, DCT, CCITT, JPX, and JBIG2 arrive one at a
/// time as their IP-register rows clear, without any change to the parser.
/// </para>
/// <para>
/// Implementations must be pure, instance-owned, free of ambient state, and must
/// respect <see cref="PdfFilterContext.MaxDecodedBytes"/> before allocating an
/// output buffer. A filter that cannot bound its output must fail rather than
/// allocate optimistically.
/// </para>
/// </remarks>
public interface IPdfStreamFilter
{
    /// <summary>The full PDF filter name, for example <c>FlateDecode</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The inline-image abbreviation for this filter (for example <c>Fl</c>), or
    /// <see langword="null"/> when the filter has none.
    /// </summary>
    string? Abbreviation { get; }

    /// <summary>
    /// True when this filter produces bytes that the reader may interpret. An
    /// image-only filter (a JPEG decoder, say) reports <see langword="false"/>:
    /// its output is pixels for an image service, not a byte stream the object
    /// layer can parse.
    /// </summary>
    bool ProducesByteStream { get; }

    /// <summary>Decodes one stage of a filter chain.</summary>
    PdfFilterResult Decode(ReadOnlySpan<byte> input, PdfFilterParameters parameters, PdfFilterContext context);
}

/// <summary>
/// The budget and cancellation handed to one filter stage. The byte ceiling is
/// already the minimum of the per-stream limit and the document's remaining
/// aggregate allowance, so a filter never gets a fresh allocation of budget.
/// </summary>
public sealed class PdfFilterContext
{
    public PdfFilterContext(long maxDecodedBytes, int maxExpansionRatio, CancellationToken cancellationToken = default)
    {
        if (maxDecodedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));
        if (maxExpansionRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExpansionRatio));

        MaxDecodedBytes = maxDecodedBytes;
        MaxExpansionRatio = maxExpansionRatio;
        CancellationToken = cancellationToken;
    }

    /// <summary>Hard ceiling on the bytes this stage may produce.</summary>
    public long MaxDecodedBytes { get; }

    /// <summary>Hard ceiling on this stage's decoded:encoded size ratio.</summary>
    public int MaxExpansionRatio { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// The output ceiling for a stage, being the stricter of the byte budget and
    /// the expansion ratio applied to <paramref name="inputLength"/>.
    /// </summary>
    public long CeilingFor(int inputLength)
    {
        if (inputLength <= 0)
            return Math.Min(MaxDecodedBytes, MaxExpansionRatio);

        long byRatio = (long)inputLength * MaxExpansionRatio;
        return Math.Min(MaxDecodedBytes, byRatio < 0 ? MaxDecodedBytes : byRatio);
    }
}

/// <summary>
/// A filter's <c>DecodeParms</c> entry, exposed as typed lookups so an
/// extension author never sees the internal PDF object types. Values are already
/// resolved: an indirect reference has been followed before the filter runs.
/// </summary>
public sealed class PdfFilterParameters
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    /// <summary>An empty parameter set, used when a stream declares no DecodeParms.</summary>
    public static PdfFilterParameters Empty { get; } = new(new Dictionary<string, object?>(StringComparer.Ordinal));

    internal PdfFilterParameters(IReadOnlyDictionary<string, object?> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public int GetInt32(string key, int fallback)
    {
        if (!_values.TryGetValue(key, out object? value))
            return fallback;

        return value switch
        {
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            double d when d >= int.MinValue && d <= int.MaxValue => (int)d,
            _ => fallback,
        };
    }

    public bool GetBoolean(string key, bool fallback) =>
        _values.TryGetValue(key, out object? value) && value is bool b ? b : fallback;

    public string? GetName(string key) =>
        _values.TryGetValue(key, out object? value) ? value as string : null;
}

/// <summary>The outcome of one filter stage.</summary>
public readonly struct PdfFilterResult
{
    private PdfFilterResult(byte[]? data, string? diagnosticCode, string? message)
    {
        Data = data;
        DiagnosticCode = diagnosticCode;
        Message = message;
    }

    /// <summary>The decoded bytes when <see cref="Succeeded"/>; otherwise null.</summary>
    public byte[]? Data { get; }

    /// <summary>The stable diagnostic code to report when the stage failed.</summary>
    public string? DiagnosticCode { get; }

    public string? Message { get; }

    public bool Succeeded => Data is not null;

    public static PdfFilterResult Success(byte[] data) =>
        new(data ?? throw new ArgumentNullException(nameof(data)), null, null);

    /// <summary>The input was structurally invalid for this filter.</summary>
    public static PdfFilterResult Malformed(string message) =>
        new(null, PdfDiagnosticCodes.FilterMalformed, message);

    /// <summary>The stage would have exceeded its byte, ratio, or work budget.</summary>
    public static PdfFilterResult LimitExceeded(string message) =>
        new(null, PdfDiagnosticCodes.FilterLimit, message);

    /// <summary>
    /// The filter recognized its input but will not decode it — the state every
    /// not-yet-cleared technology reports until its review completes.
    /// </summary>
    public static PdfFilterResult Unsupported(string diagnosticCode, string message) =>
        new(null, diagnosticCode ?? PdfDiagnosticCodes.FilterNotComposed, message);
}
