using System;
using System.Collections.Generic;
using System.Text;
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

    /// <summary>The token this read was started with, for a composed extension to observe.</summary>
    public CancellationToken Cancellation => _cancellation;

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
/// <remarks>
/// <para>
/// The count is part of the entry, not bookkeeping the sink keeps to itself. A
/// note that a raster image was skipped answers a different question depending
/// on whether it happened once on the cover or four hundred times throughout,
/// and a reader that collapses both into the same line has hidden the answer.
/// The pages a code was seen on are carried the same way, up to
/// <see cref="MaxNamedPages"/> of them, so a reviewer can open the file at the
/// right place instead of hunting for it.
/// </para>
/// <para>
/// Aggregation never adds a payload: a count is a count and a page number is a
/// page number, so the ADR 0009 rule that a diagnostic names the construct and
/// the reason — never document text, a metadata value, or a path — still holds.
/// </para>
/// </remarks>
internal sealed class PdfDiagnosticSink
{
    /// <summary>
    /// How many distinct page numbers one entry names before it stops listing
    /// them. A construct on every page of a thousand-page file is described by
    /// its count; enumerating the pages adds nothing but length.
    /// </summary>
    private const int MaxNamedPages = 6;

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, Entry> _byCode = new(StringComparer.Ordinal);
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

    /// <summary>
    /// The one-based page currently being interpreted, or null outside the page
    /// loop. The reader sets it so a diagnostic raised deep inside the content
    /// interpreter or the font loader carries a location without every call site
    /// having to thread a page number it does not otherwise need.
    /// </summary>
    public int? CurrentPage { get; set; }

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

    public bool Contains(string code) => _byCode.ContainsKey(code);

    public IReadOnlyList<DocumentDiagnostic> Build()
    {
        var all = new List<DocumentDiagnostic>(_entries.Count + 1);
        foreach (Entry entry in _entries)
            all.Add(entry.ToDiagnostic());

        if (_suppressed > 0)
        {
            all.Add(DocumentDiagnostic.Info(
                PdfDiagnosticCodes.DiagnosticsTruncated,
                $"{_suppressed} further diagnostics were suppressed by the diagnostic limit."));
        }

        return all;
    }

    private void Add(DocumentDiagnosticSeverity severity, string code, string message)
    {
        if (severity == DocumentDiagnosticSeverity.Error)
            HasErrors = true;

        // First occurrence of a code is always kept; repeats bump its count and
        // widen the set of pages it names.
        if (_byCode.TryGetValue(code, out Entry? seen))
        {
            seen.Repeat(CurrentPage);
            return;
        }

        if (_entries.Count >= _maxDiagnostics)
        {
            _suppressed++;
            return;
        }

        var entry = new Entry(severity, code, message, CurrentPage);
        _byCode[code] = entry;
        _entries.Add(entry);
    }

    /// <summary>
    /// One code's accumulated occurrences. Held mutable until <see cref="Build"/>
    /// because the count is only final once the document is done, and a
    /// <see cref="DocumentDiagnostic"/> is immutable by design.
    /// </summary>
    private sealed class Entry
    {
        private readonly SortedSet<int> _pages = [];
        private readonly DocumentDiagnosticSeverity _severity;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _firstPage;
        private bool _morePages;

        public Entry(DocumentDiagnosticSeverity severity, string code, string message, int? page)
        {
            _severity = severity;
            _code = code;
            _message = message;
            _firstPage = page;
            Count = 1;
            NotePage(page);
        }

        public int Count { get; private set; }

        public void Repeat(int? page)
        {
            // Saturating rather than checked: a count is a description, and a
            // hostile file must not turn one into an overflow.
            if (Count < int.MaxValue)
                Count++;
            NotePage(page);
        }

        public DocumentDiagnostic ToDiagnostic()
        {
            string? occurrences = DescribeOccurrences();
            string message = occurrences is null ? _message : _message + " " + occurrences;
            DocumentDiagnosticLocation? location = _firstPage is int page
                ? new DocumentDiagnosticLocation(pageNumber: page)
                : null;
            return new DocumentDiagnostic(_severity, _code, message, location);
        }

        private void NotePage(int? page)
        {
            if (page is not int number || _pages.Contains(number))
                return;

            if (_pages.Count >= MaxNamedPages)
            {
                _morePages = true;
                return;
            }

            _pages.Add(number);
        }

        /// <summary>
        /// The aggregate sentence, or null when there is nothing to aggregate —
        /// a single occurrence on a single page is fully described by the message
        /// and the location already.
        /// </summary>
        private string? DescribeOccurrences()
        {
            if (Count == 1 && _pages.Count <= 1)
                return null;

            var text = new StringBuilder();
            text.Append(Count == 1 ? "Seen once" : $"Seen {Count} times");

            if (_pages.Count > 0)
            {
                text.Append(_pages.Count == 1 ? ", on page " : ", on pages ");
                text.Append(string.Join(", ", _pages));
                if (_morePages)
                    text.Append(" and others");
            }

            return text.Append('.').ToString();
        }
    }
}
