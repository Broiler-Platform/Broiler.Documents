using System;
using System.Collections.Generic;
using System.Threading;

namespace Broiler.Documents.Pdf;

/// <summary>
/// Raised when a PDF budget is exhausted. The codec turns this into a
/// <c>Rejected</c> result rather than a truncated document: a limit must never
/// silently downgrade into a successful-but-empty read (PDF roadmap §6.3).
/// </summary>
public sealed class PdfLimitExceededException : Exception
{
    public PdfLimitExceededException(string limitName, string message)
        : base(message)
    {
        LimitName = limitName;
    }

    /// <summary>The budget that was exhausted, for the diagnostic message.</summary>
    public string LimitName { get; }
}

/// <summary>
/// One document's running account of every bounded resource: bytes decoded,
/// objects created, operators interpreted, characters extracted, and abstract
/// work units. It is owned by a single read or write, never shared or reset, so
/// delegated work cannot restart the accounting (PDF roadmap §6.3).
/// </summary>
internal sealed class PdfWorkBudget
{
    private readonly PdfLimits _limits;
    private readonly CancellationToken _cancellation;
    private long _decodedBytes;
    private long _workUnits;
    private long _operators;
    private int _objects;
    private int _characters;
    private int _cmapEntries;
    private int _fonts;
    private int _annotations;

    public PdfWorkBudget(PdfLimits limits, CancellationToken cancellation = default)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _cancellation = cancellation;
    }

    public PdfLimits Limits => _limits;

    /// <summary>Aggregate decoded bytes charged so far, across every stream.</summary>
    public long DecodedBytes => _decodedBytes;

    /// <summary>
    /// The decoded-byte budget still available. A delegated decoder receives the
    /// minimum of its own limit and this remainder, never a fresh allowance.
    /// </summary>
    public long RemainingDecodedBytes => Math.Max(0, _limits.MaxDecodedStreamBytes - _decodedBytes);

    /// <summary>A cancellation checkpoint; call it at every documented boundary.</summary>
    public void ThrowIfCancelled() => _cancellation.ThrowIfCancellationRequested();

    public void ChargeWork(long units)
    {
        if (units <= 0)
            return;
        _workUnits = AddChecked(_workUnits, units, nameof(PdfLimits.MaxWorkUnits));
        if (_workUnits > _limits.MaxWorkUnits)
            throw Exceeded(nameof(PdfLimits.MaxWorkUnits), _limits.MaxWorkUnits);
    }

    public void ChargeDecodedBytes(long bytes)
    {
        if (bytes <= 0)
            return;
        _decodedBytes = AddChecked(_decodedBytes, bytes, nameof(PdfLimits.MaxDecodedStreamBytes));
        if (_decodedBytes > _limits.MaxDecodedStreamBytes)
            throw Exceeded(nameof(PdfLimits.MaxDecodedStreamBytes), _limits.MaxDecodedStreamBytes);
        ChargeWork(bytes);
    }

    public void ChargeObject()
    {
        if (++_objects > _limits.MaxObjectCount)
            throw Exceeded(nameof(PdfLimits.MaxObjectCount), _limits.MaxObjectCount);
    }

    public void ChargeOperator()
    {
        if (++_operators > _limits.MaxContentOperators)
            throw Exceeded(nameof(PdfLimits.MaxContentOperators), _limits.MaxContentOperators);
        // Cancellation is polled on a cadence rather than per operator so a long
        // content stream stays responsive without a syscall per token.
        if ((_operators & 0x3FFF) == 0)
            ThrowIfCancelled();
    }

    public void ChargeCharacters(int count)
    {
        if (count <= 0)
            return;
        _characters = AddChecked(_characters, count, nameof(PdfLimits.MaxExtractedCharacters));
        if (_characters > _limits.MaxExtractedCharacters)
            throw Exceeded(nameof(PdfLimits.MaxExtractedCharacters), _limits.MaxExtractedCharacters);
    }

    public void ChargeCMapEntries(int count)
    {
        if (count <= 0)
            return;
        _cmapEntries = AddChecked(_cmapEntries, count, nameof(PdfLimits.MaxCMapEntries));
        if (_cmapEntries > _limits.MaxCMapEntries)
            throw Exceeded(nameof(PdfLimits.MaxCMapEntries), _limits.MaxCMapEntries);
    }

    public void ChargeFont()
    {
        if (++_fonts > _limits.MaxFontCount)
            throw Exceeded(nameof(PdfLimits.MaxFontCount), _limits.MaxFontCount);
    }

    public void ChargeAnnotations(int count)
    {
        if (count <= 0)
            return;
        _annotations = AddChecked(_annotations, count, nameof(PdfLimits.MaxAnnotationCount));
        if (_annotations > _limits.MaxAnnotationCount)
            throw Exceeded(nameof(PdfLimits.MaxAnnotationCount), _limits.MaxAnnotationCount);
    }

    public static PdfLimitExceededException Exceeded(string limitName, long limit) =>
        new(limitName, $"The PDF {limitName} limit of {limit} was reached; the document was rejected rather than truncated.");

    private static long AddChecked(long current, long delta, string limitName)
    {
        try
        {
            return checked(current + delta);
        }
        catch (OverflowException)
        {
            throw Exceeded(limitName, long.MaxValue);
        }
    }

    private static int AddChecked(int current, int delta, string limitName)
    {
        try
        {
            return checked(current + delta);
        }
        catch (OverflowException)
        {
            throw Exceeded(limitName, int.MaxValue);
        }
    }
}

/// <summary>
/// Collects diagnostics under the document's diagnostic cap, de-duplicating by
/// code so one malformed construct repeated on every page produces one entry
/// with a count rather than thousands of lines.
/// </summary>
internal sealed class PdfDiagnosticSink
{
    private readonly List<DocumentDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly int _maxDiagnostics;
    private int _suppressed;

    public PdfDiagnosticSink(int maxDiagnostics)
    {
        _maxDiagnostics = maxDiagnostics > 0 ? maxDiagnostics : PdfLimits.DefaultMaxDiagnostics;
    }

    /// <summary>True when at least one error-severity diagnostic was recorded.</summary>
    public bool HasErrors { get; private set; }

    /// <summary>True when a construct was skipped, making the result at best partial.</summary>
    public bool HasSkips { get; private set; }

    public void Info(string code, string message) => Add(DocumentDiagnosticSeverity.Info, code, message);

    public void Warning(string code, string message) => Add(DocumentDiagnosticSeverity.Warning, code, message);

    public void Error(string code, string message) => Add(DocumentDiagnosticSeverity.Error, code, message);

    /// <summary>
    /// Records a construct that was recognized and deliberately not interpreted.
    /// These are what make a result <c>Partial</c> instead of <c>Success</c>.
    /// </summary>
    public void Skipped(string code, string message)
    {
        HasSkips = true;
        Add(DocumentDiagnosticSeverity.Warning, code, message);
    }

    public bool Contains(string code) => _counts.ContainsKey(code);

    public IReadOnlyList<DocumentDiagnostic> Build()
    {
        if (_suppressed == 0)
            return _diagnostics;

        var all = new List<DocumentDiagnostic>(_diagnostics.Count + 1);
        all.AddRange(_diagnostics);
        all.Add(DocumentDiagnostic.Info(
            PdfDiagnosticCodes.DiagnosticsTruncated,
            $"{_suppressed} further diagnostics were suppressed by the diagnostic limit."));
        return all;
    }

    private void Add(DocumentDiagnosticSeverity severity, string code, string message)
    {
        if (severity == DocumentDiagnosticSeverity.Error)
            HasErrors = true;

        // First occurrence of a code is always kept; repeats only bump the count.
        if (_counts.TryGetValue(code, out int seen))
        {
            _counts[code] = seen + 1;
            return;
        }

        if (_diagnostics.Count >= _maxDiagnostics)
        {
            _suppressed++;
            return;
        }

        _counts[code] = 1;
        _diagnostics.Add(new DocumentDiagnostic(severity, code, message));
    }
}
