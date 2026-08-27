using System;
using System.Collections.Generic;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>Where one object number's bytes live.</summary>
internal readonly struct PdfXrefEntry
{
    private PdfXrefEntry(long offset, int generation, int containerObject, int indexInContainer)
    {
        Offset = offset;
        Generation = generation;
        ContainerObject = containerObject;
        IndexInContainer = indexInContainer;
    }

    /// <summary>Byte offset of <c>n g obj</c>, for a directly stored object.</summary>
    public long Offset { get; }

    public int Generation { get; }

    /// <summary>The object stream holding this object, or -1 when stored directly.</summary>
    public int ContainerObject { get; }

    public int IndexInContainer { get; }

    public bool IsInObjectStream => ContainerObject >= 0;

    public static PdfXrefEntry Direct(long offset, int generation) => new(offset, generation, -1, 0);

    public static PdfXrefEntry Compressed(int containerObject, int index) => new(0, 0, containerObject, index);
}

/// <summary>
/// The cross-reference map plus lazy object resolution: everything between raw
/// bytes and the document structure.
/// </summary>
/// <remarks>
/// <para>
/// The chain is walked newest revision first and the first entry seen for an
/// object number wins, which is exactly the "latest effective revision" rule.
/// Earlier revisions are therefore unreachable by construction rather than by a
/// later filtering pass, and the reader reports that history was dropped rather
/// than implying it was preserved.
/// </para>
/// <para>
/// Encryption is settled before any object is resolved: <see cref="IsEncrypted"/>
/// is decided from the trailers alone, so a caller that rejects on it has done so
/// before a single string, stream, or content operator was interpreted.
/// </para>
/// </remarks>
internal sealed class PdfObjectStore
{
    private const int MaxRecoveryScanObjects = 200_000;

    private readonly byte[] _data;
    private readonly PdfWorkBudget _budget;
    private readonly PdfDiagnosticSink _diagnostics;
    private readonly PdfFilterPipeline _pipeline;
    private readonly Dictionary<int, PdfXrefEntry> _entries = new();
    private readonly Dictionary<int, PdfObject> _cache = new();
    private readonly HashSet<int> _resolving = [];
    private readonly Dictionary<int, Dictionary<int, int>> _objectStreamIndex = new();
    private readonly Dictionary<int, byte[]> _objectStreamData = new();
    private readonly int _headerOffset;

    private PdfObjectStore(
        byte[] data,
        int headerOffset,
        PdfWorkBudget budget,
        PdfDiagnosticSink diagnostics,
        PdfFilterPipeline pipeline)
    {
        _data = data;
        _headerOffset = headerOffset;
        _budget = budget;
        _diagnostics = diagnostics;
        _pipeline = pipeline;
        Trailer = new PdfDictionary();
    }

    /// <summary>The merged trailer of the newest revision.</summary>
    public PdfDictionary Trailer { get; private set; }

    /// <summary>The version from the <c>%PDF-</c> header, before the Catalog override.</summary>
    public PdfVersion HeaderVersion { get; private set; } = PdfVersion.Unknown;

    /// <summary>True when any effective trailer carries <c>/Encrypt</c>.</summary>
    public bool IsEncrypted { get; private set; }

    /// <summary>True when the cross-reference data had to be rebuilt by scanning.</summary>
    public bool WasRecovered { get; private set; }

    /// <summary>The number of cross-reference sections in the <c>/Prev</c> chain.</summary>
    public int RevisionCount { get; private set; }

    public PdfFilterPipeline Filters => _pipeline;

    public PdfWorkBudget Budget => _budget;

    public PdfDiagnosticSink Diagnostics => _diagnostics;

    /// <summary>
    /// Loads the cross-reference data. Returns null only when the input has no
    /// usable header, which the caller reports as a rejected read.
    /// </summary>
    public static PdfObjectStore? Load(
        byte[] data,
        PdfWorkBudget budget,
        PdfDiagnosticSink diagnostics,
        PdfFilterPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(data);

        int headerOffset = FindHeader(data);
        if (headerOffset < 0)
            return null;

        var store = new PdfObjectStore(data, headerOffset, budget, diagnostics, pipeline);
        store.HeaderVersion = PdfVersion.ParseHeader(data, headerOffset);
        store.LoadXref();
        return store;
    }

    // ---- cross-reference loading ---------------------------------------------

    private void LoadXref()
    {
        long start = FindStartXref();
        var visited = new HashSet<long>();
        var trailers = new List<PdfDictionary>();

        long? next = start >= 0 ? start : null;
        while (next is { } offset)
        {
            _budget.ThrowIfCancelled();

            if (!visited.Add(offset))
            {
                _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "The cross-reference chain loops back on itself; the repeat was cut.");
                break;
            }

            if (visited.Count > _budget.Limits.MaxXrefSections)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxXrefSections), _budget.Limits.MaxXrefSections);

            PdfDictionary? trailer = ReadXrefSection(offset);
            if (trailer is null)
                break;

            trailers.Add(trailer);
            next = ReadOffsetEntry(trailer, "Prev");
        }

        RevisionCount = trailers.Count;

        if (trailers.Count == 0 || !HasUsableRoot(trailers))
        {
            RecoverByScanning();
            if (trailers.Count == 0)
                trailers.AddRange(RecoverTrailers());
        }

        Trailer = MergeTrailers(trailers);
        IsEncrypted = Trailer.ContainsKey("Encrypt");

        if (RevisionCount > 1)
        {
            _diagnostics.Info(
                PdfDiagnosticCodes.RevisionsHistoryDropped,
                $"The document has {RevisionCount} cross-reference sections; only the latest effective revision was read.");
        }
    }

    // Reads one section: either a classic "xref" table or a cross-reference stream.
    private PdfDictionary? ReadXrefSection(long offset)
    {
        int position = ToBufferOffset(offset);
        if (position < 0)
        {
            _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "A cross-reference offset pointed outside the file.");
            return null;
        }

        var lexer = new PdfLexer(_data, _budget.Limits, position);
        PdfToken token = lexer.PeekToken();

        if (token.IsKeyword("xref"))
            return ReadClassicXref(lexer);

        // Otherwise it must be "n g obj" introducing a cross-reference stream.
        if (!TryResolveObjectPosition(offset, out _, out _, out PdfObject? candidate) || candidate is not PdfStream stream)
        {
            _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "A cross-reference offset did not point at a table or stream.");
            return null;
        }

        return ReadXrefStream(stream);
    }

    private PdfDictionary? ReadClassicXref(PdfLexer lexer)
    {
        lexer.ReadToken(); // consume "xref"

        while (true)
        {
            _budget.ChargeWork(8);
            PdfToken first = lexer.PeekToken();
            if (first.Type != PdfTokenType.Integer)
                break;

            lexer.ReadToken();
            PdfToken countToken = lexer.ReadToken();
            if (countToken.Type != PdfTokenType.Integer)
            {
                _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "A cross-reference subsection header was malformed.");
                return null;
            }

            long startNumber = (long)first.Number;
            long count = (long)countToken.Number;
            if (startNumber < 0 || count < 0 || count > _budget.Limits.MaxObjectCount)
            {
                _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "A cross-reference subsection declared an out-of-range object range.");
                return null;
            }

            for (long i = 0; i < count; i++)
            {
                PdfToken offsetToken = lexer.ReadToken();
                PdfToken generationToken = lexer.ReadToken();
                PdfToken kindToken = lexer.ReadToken();

                if (offsetToken.Type != PdfTokenType.Integer || generationToken.Type != PdfTokenType.Integer)
                {
                    _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "A cross-reference entry was malformed.");
                    return null;
                }

                long objectNumber = startNumber + i;
                if (objectNumber > int.MaxValue)
                    break;

                // 'f' marks a free entry: the object number is unused in this revision.
                if (kindToken.IsKeyword("f"))
                    continue;

                AddEntry((int)objectNumber, PdfXrefEntry.Direct((long)offsetToken.Number, (int)generationToken.Number));
                _budget.ChargeWork(2);
            }
        }

        PdfToken trailerToken = lexer.ReadToken();
        if (!trailerToken.IsKeyword("trailer"))
        {
            _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "A cross-reference table had no trailer.");
            return null;
        }

        var parser = new PdfObjectParser(lexer, _budget);
        if (parser.ParseObject() is not PdfDictionary trailer)
        {
            _diagnostics.Warning(PdfDiagnosticCodes.XrefMalformed, "A trailer dictionary could not be parsed.");
            return null;
        }

        // A hybrid-reference file keeps entries the classic table cannot express in
        // a companion stream. Those entries are loaded first so they win the
        // first-seen-wins merge, which is what an xref-stream-aware reader must do.
        if (ReadOffsetEntry(trailer, "XRefStm") is { } hybrid)
        {
            int position = ToBufferOffset(hybrid);
            if (position >= 0 &&
                TryReadIndirectObjectAt(position, out _, out _, out PdfObject? hybridObject) &&
                hybridObject is PdfStream hybridStream)
            {
                ReadXrefStream(hybridStream);
            }
        }

        return trailer;
    }

    private PdfDictionary? ReadXrefStream(PdfStream stream)
    {
        PdfDictionary dictionary = stream.Dictionary;

        // Encryption is settled here, before the stream is decoded: an encrypted
        // document must never reach a filter, an object stream, or the Catalog.
        if (dictionary.ContainsKey("Encrypt"))
            IsEncrypted = true;

        PdfStreamDecodeResult decoded = _pipeline.Decode(stream, ResolveDirect, _budget);
        if (!decoded.Succeeded)
        {
            _diagnostics.Error(
                decoded.DiagnosticCode ?? PdfDiagnosticCodes.XrefMalformed,
                decoded.Message ?? "A cross-reference stream could not be decoded.");
            return null;
        }

        if (ResolveDirect(dictionary["W"]) is not PdfArray widths || widths.Count < 3)
        {
            _diagnostics.Error(PdfDiagnosticCodes.XrefMalformed, "A cross-reference stream had no usable /W field-width array.");
            return null;
        }

        var fieldWidths = new int[widths.Count];
        int rowWidth = 0;
        for (int i = 0; i < widths.Count; i++)
        {
            fieldWidths[i] = ResolveDirect(widths[i]) is PdfNumber number ? number.ToInt32() : 0;
            if (fieldWidths[i] is < 0 or > 8)
            {
                _diagnostics.Error(PdfDiagnosticCodes.XrefMalformed, "A cross-reference stream declared an out-of-range field width.");
                return null;
            }

            rowWidth += fieldWidths[i];
        }

        if (rowWidth <= 0)
        {
            _diagnostics.Error(PdfDiagnosticCodes.XrefMalformed, "A cross-reference stream declared zero-width rows.");
            return null;
        }

        List<(long Start, long Count)> ranges = ReadIndexRanges(dictionary, decoded.Data!.Length / rowWidth);
        byte[] payload = decoded.Data!;
        int cursor = 0;

        foreach ((long start, long count) in ranges)
        {
            for (long i = 0; i < count; i++)
            {
                if (cursor + rowWidth > payload.Length)
                    break;

                _budget.ChargeWork(2);
                long type = fieldWidths[0] == 0 ? 1 : ReadField(payload, ref cursor, fieldWidths[0]);
                long second = ReadField(payload, ref cursor, fieldWidths[1]);
                long third = ReadField(payload, ref cursor, fieldWidths[2]);
                for (int extra = 3; extra < fieldWidths.Length; extra++)
                    ReadField(payload, ref cursor, fieldWidths[extra]);

                long objectNumber = start + i;
                if (objectNumber is < 0 or > int.MaxValue)
                    continue;

                switch (type)
                {
                    case 1:
                        AddEntry((int)objectNumber, PdfXrefEntry.Direct(second, (int)Math.Clamp(third, 0, ushort.MaxValue)));
                        break;
                    case 2:
                        if (second is >= 0 and <= int.MaxValue && third is >= 0 and <= int.MaxValue)
                            AddEntry((int)objectNumber, PdfXrefEntry.Compressed((int)second, (int)third));
                        break;
                    default:
                        // Type 0 is a free entry; any other type is reserved and skipped.
                        break;
                }
            }
        }

        return dictionary;
    }

    private List<(long Start, long Count)> ReadIndexRanges(PdfDictionary dictionary, int rowCount)
    {
        var ranges = new List<(long, long)>();
        if (ResolveDirect(dictionary["Index"]) is PdfArray index && index.Count >= 2)
        {
            for (int i = 0; i + 1 < index.Count; i += 2)
            {
                long start = ResolveDirect(index[i]) is PdfNumber s ? s.ToInt64() : 0;
                long count = ResolveDirect(index[i + 1]) is PdfNumber c ? c.ToInt64() : 0;
                if (count > 0 && count <= _budget.Limits.MaxObjectCount)
                    ranges.Add((start, count));
            }
        }

        if (ranges.Count == 0)
        {
            long size = ResolveDirect(dictionary["Size"]) is PdfNumber number ? number.ToInt64() : rowCount;
            ranges.Add((0, Math.Min(size, rowCount)));
        }

        return ranges;
    }

    private static long ReadField(byte[] data, ref int cursor, int width)
    {
        long value = 0;
        for (int i = 0; i < width; i++)
            value = (value << 8) | data[cursor + i];
        cursor += width;
        return value;
    }

    private void AddEntry(int objectNumber, PdfXrefEntry entry)
    {
        // First seen wins: the walk starts at the newest revision, so an earlier
        // revision can never displace a later definition of the same object.
        if (_entries.ContainsKey(objectNumber))
            return;
        if (_entries.Count >= _budget.Limits.MaxObjectCount)
            throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxObjectCount), _budget.Limits.MaxObjectCount);
        _entries[objectNumber] = entry;
    }

    private bool HasUsableRoot(List<PdfDictionary> trailers)
    {
        foreach (PdfDictionary trailer in trailers)
        {
            if (trailer["Root"] is null)
                continue;
            if (Resolve(trailer["Root"]) is PdfDictionary root && root.Count > 0)
                return true;
        }

        return false;
    }

    // ---- recovery -------------------------------------------------------------

    /// <summary>
    /// Rebuilds the cross-reference map by scanning for <c>n g obj</c> headers.
    /// </summary>
    /// <remarks>
    /// This is the one recovery path in the reader and it runs only when the
    /// declared cross-reference data cannot produce a Catalog. It never runs
    /// speculatively mid-parse, and it is reported, so a recovered document is
    /// never presented as a cleanly parsed one.
    /// </remarks>
    private void RecoverByScanning()
    {
        WasRecovered = true;
        _diagnostics.Warning(
            PdfDiagnosticCodes.XrefRecovered,
            "The cross-reference data was unusable; object offsets were rebuilt by scanning the file.");

        _entries.Clear();
        _cache.Clear();

        int found = 0;
        for (int i = 0; i + 3 < _data.Length && found < MaxRecoveryScanObjects; i++)
        {
            if (_data[i] != (byte)'o' || _data[i + 1] != (byte)'b' || _data[i + 2] != (byte)'j')
                continue;
            if (i + 3 < _data.Length && PdfLexer.IsRegular(_data[i + 3]))
                continue;

            if (!TryReadObjectHeaderBackwards(i, out int objectNumber, out int generation, out int headerStart))
                continue;

            _budget.ChargeWork(4);

            // Later definitions win here: without a cross-reference chain, file
            // order is the only revision signal available.
            _entries[objectNumber] = PdfXrefEntry.Direct(headerStart + _headerOffset, generation);
            found++;
        }

        // Objects inside object streams are only reachable once their containers
        // are known, so expand every recovered /Type /ObjStm now.
        foreach (int objectNumber in new List<int>(_entries.Keys))
        {
            if (GetObject(objectNumber) is PdfStream stream &&
                (stream.Dictionary["Type"] as PdfName)?.Value == "ObjStm")
            {
                RegisterObjectStreamMembers(objectNumber, stream);
            }
        }
    }

    private void RegisterObjectStreamMembers(int containerNumber, PdfStream stream)
    {
        if (!TryLoadObjectStream(containerNumber, stream, out Dictionary<int, int>? offsets, out _))
            return;

        int index = 0;
        foreach (int member in offsets!.Keys)
        {
            if (!_entries.ContainsKey(member))
                _entries[member] = PdfXrefEntry.Compressed(containerNumber, index);
            index++;
        }
    }

    private bool TryReadObjectHeaderBackwards(int objKeywordStart, out int objectNumber, out int generation, out int headerStart)
    {
        objectNumber = 0;
        generation = 0;
        headerStart = 0;

        int i = objKeywordStart - 1;
        while (i >= 0 && PdfLexer.IsWhitespace(_data[i]))
            i--;

        int generationEnd = i + 1;
        while (i >= 0 && _data[i] >= (byte)'0' && _data[i] <= (byte)'9')
            i--;
        int generationStart = i + 1;
        if (generationStart == generationEnd)
            return false;

        while (i >= 0 && PdfLexer.IsWhitespace(_data[i]))
            i--;

        int numberEnd = i + 1;
        while (i >= 0 && _data[i] >= (byte)'0' && _data[i] <= (byte)'9')
            i--;
        int numberStart = i + 1;
        if (numberStart == numberEnd)
            return false;

        if (!int.TryParse(PdfLexer.Latin1(_data, numberStart, numberEnd - numberStart), out objectNumber) ||
            !int.TryParse(PdfLexer.Latin1(_data, generationStart, generationEnd - generationStart), out generation))
            return false;

        headerStart = numberStart;
        return true;
    }

    private List<PdfDictionary> RecoverTrailers()
    {
        var trailers = new List<PdfDictionary>();

        // Prefer a real trailer dictionary if one survives anywhere in the file.
        for (int i = _data.Length - 7; i >= 0; i--)
        {
            if (!MatchesAscii(i, "trailer"))
                continue;

            var lexer = new PdfLexer(_data, _budget.Limits, i + 7);
            var parser = new PdfObjectParser(lexer, _budget);
            if (parser.ParseObject() is PdfDictionary trailer && trailer.ContainsKey("Root"))
            {
                trailers.Add(trailer);
                return trailers;
            }
        }

        // Otherwise synthesize one from whichever object is the Catalog.
        foreach (int objectNumber in _entries.Keys)
        {
            if (GetObject(objectNumber) is not PdfDictionary candidate)
                continue;
            if ((candidate["Type"] as PdfName)?.Value != "Catalog")
                continue;

            var synthesized = new PdfDictionary
            {
                ["Root"] = new PdfReference(objectNumber, _entries[objectNumber].Generation),
            };
            trailers.Add(synthesized);
            break;
        }

        return trailers;
    }

    private static PdfDictionary MergeTrailers(List<PdfDictionary> trailers)
    {
        // Newest first: a key already set by a later revision is never overwritten.
        var merged = new PdfDictionary();
        foreach (PdfDictionary trailer in trailers)
        {
            foreach (KeyValuePair<string, PdfObject> entry in trailer)
            {
                if (entry.Key is "Prev" or "XRefStm")
                    continue;
                if (!merged.ContainsKey(entry.Key))
                    merged[entry.Key] = entry.Value;
            }
        }

        return merged;
    }

    // ---- object resolution ----------------------------------------------------

    /// <summary>Follows indirect references until a direct object is reached.</summary>
    public PdfObject? Resolve(PdfObject? value)
    {
        int hops = 0;
        while (value is PdfReference reference)
        {
            if (++hops > _budget.Limits.MaxNestingDepth)
            {
                _diagnostics.Warning(PdfDiagnosticCodes.ObjectCycle, "An indirect reference chain was too long and was cut.");
                return null;
            }

            value = GetObject(reference.ObjectNumber);
        }

        return value is PdfNull ? null : value;
    }

    // The resolver handed to the filter pipeline while the cross-reference map is
    // still being built. Following a reference at that point could recurse back
    // into loading, so only direct values are honoured.
    private PdfObject? ResolveDirect(PdfObject? value) => value is PdfReference or PdfNull ? null : value;

    public PdfObject? GetObject(int objectNumber)
    {
        if (_cache.TryGetValue(objectNumber, out PdfObject? cached))
            return cached is PdfNull ? null : cached;

        if (!_entries.TryGetValue(objectNumber, out PdfXrefEntry entry))
            return null;

        if (!_resolving.Add(objectNumber))
        {
            _diagnostics.Warning(PdfDiagnosticCodes.ObjectCycle, "An object referred to itself while being loaded.");
            return null;
        }

        try
        {
            _budget.ThrowIfCancelled();
            _budget.ChargeObject();

            PdfObject? loaded = entry.IsInObjectStream
                ? LoadFromObjectStream(objectNumber, entry)
                : LoadDirect(objectNumber, entry);

            _cache[objectNumber] = loaded ?? PdfObject.Null;
            return loaded;
        }
        finally
        {
            _resolving.Remove(objectNumber);
        }
    }

    private PdfObject? LoadDirect(int objectNumber, PdfXrefEntry entry)
    {
        if (!TryResolveObjectPosition(entry.Offset, out int foundNumber, out _, out PdfObject? value))
        {
            _diagnostics.Warning(PdfDiagnosticCodes.ObjectMalformed, "An indirect object header could not be parsed.");
            return null;
        }

        if (foundNumber != objectNumber)
        {
            // The offset points at the wrong object: a broken but repairable file.
            // Trust the header, not the table, and report it once.
            _diagnostics.Warning(
                PdfDiagnosticCodes.XrefMalformed,
                "A cross-reference entry pointed at a different object number than it declared.");
        }

        return value;
    }

    private PdfObject? LoadFromObjectStream(int objectNumber, PdfXrefEntry entry)
    {
        if (GetObject(entry.ContainerObject) is not PdfStream container)
        {
            _diagnostics.Warning(PdfDiagnosticCodes.ObjectMissing, "An object stream named by the cross-reference table is missing.");
            return null;
        }

        if (!TryLoadObjectStream(entry.ContainerObject, container, out Dictionary<int, int>? offsets, out byte[]? payload))
            return null;

        if (!offsets!.TryGetValue(objectNumber, out int offset) || offset < 0 || offset >= payload!.Length)
        {
            _diagnostics.Warning(PdfDiagnosticCodes.ObjectMissing, "An object stream did not contain the object the table promised.");
            return null;
        }

        var lexer = new PdfLexer(payload!, _budget.Limits, offset);
        var parser = new PdfObjectParser(lexer, _budget);
        PdfObject value = parser.ParseObject();
        if (parser.LastObjectWasMalformed)
            _diagnostics.Warning(PdfDiagnosticCodes.ObjectMalformed, "An object inside an object stream was malformed.");
        return value;
    }

    private bool TryLoadObjectStream(
        int containerNumber,
        PdfStream container,
        out Dictionary<int, int>? offsets,
        out byte[]? payload)
    {
        if (_objectStreamIndex.TryGetValue(containerNumber, out offsets) &&
            _objectStreamData.TryGetValue(containerNumber, out payload))
            return true;

        offsets = null;
        payload = null;

        PdfStreamDecodeResult decoded = _pipeline.Decode(container, Resolve, _budget);
        if (!decoded.Succeeded)
        {
            _diagnostics.Error(
                decoded.DiagnosticCode ?? PdfDiagnosticCodes.FilterMalformed,
                decoded.Message ?? "An object stream could not be decoded.");
            return false;
        }

        payload = decoded.Data!;
        int count = Resolve(container.Dictionary["N"]) is PdfNumber n ? n.ToInt32() : 0;
        int first = Resolve(container.Dictionary["First"]) is PdfNumber f ? f.ToInt32() : 0;

        if (count < 0 || count > _budget.Limits.MaxObjectCount || first < 0 || first > payload.Length)
        {
            _diagnostics.Error(PdfDiagnosticCodes.ObjectMalformed, "An object stream declared an out-of-range /N or /First.");
            return false;
        }

        offsets = new Dictionary<int, int>(count);
        var headerLexer = new PdfLexer(payload, _budget.Limits, 0, first);
        for (int i = 0; i < count; i++)
        {
            PdfToken numberToken = headerLexer.ReadToken();
            PdfToken offsetToken = headerLexer.ReadToken();
            if (numberToken.Type != PdfTokenType.Integer || offsetToken.Type != PdfTokenType.Integer)
                break;

            long member = (long)numberToken.Number;
            long relative = (long)offsetToken.Number;
            if (member is < 0 or > int.MaxValue || relative < 0 || first + relative > payload.Length)
                continue;

            offsets[(int)member] = first + (int)relative;
            _budget.ChargeWork(2);
        }

        _objectStreamIndex[containerNumber] = offsets;
        _objectStreamData[containerNumber] = payload;
        return true;
    }

    /// <summary>Parses <c>n g obj … endobj</c> at a byte offset.</summary>
    private bool TryReadIndirectObjectAt(int position, out int objectNumber, out int generation, out PdfObject? value)
    {
        objectNumber = 0;
        generation = 0;
        value = null;

        var lexer = new PdfLexer(_data, _budget.Limits, position);
        PdfToken numberToken = lexer.ReadToken();
        PdfToken generationToken = lexer.ReadToken();
        PdfToken objToken = lexer.ReadToken();

        if (numberToken.Type != PdfTokenType.Integer ||
            generationToken.Type != PdfTokenType.Integer ||
            !objToken.IsKeyword("obj"))
            return false;

        objectNumber = (int)Math.Clamp(numberToken.Number, 0, int.MaxValue);
        generation = (int)Math.Clamp(generationToken.Number, 0, ushort.MaxValue);

        var parser = new PdfObjectParser(lexer, _budget);
        PdfObject parsed = parser.ParseObject();
        if (parser.LastObjectWasMalformed)
            _diagnostics.Warning(PdfDiagnosticCodes.ObjectMalformed, "An indirect object could not be fully parsed.");

        // The parser may hold lookahead; give it back before reading the lexer.
        parser.Rewind();
        PdfToken next = lexer.ReadToken();
        if (next.IsKeyword("stream") && parsed is PdfDictionary streamDictionary)
        {
            value = ReadStreamBody(lexer, streamDictionary);
            return true;
        }

        value = parsed;
        return true;
    }

    private PdfStream ReadStreamBody(PdfLexer lexer, PdfDictionary dictionary)
    {
        // "stream" must be followed by CRLF or LF, never by CR alone (clause 7.3.8.1).
        int start = lexer.Position;
        if (start < _data.Length && _data[start] == 13)
            start++;
        if (start < _data.Length && _data[start] == 10)
            start++;

        int available = _data.Length - start;
        int length = -1;
        if (Resolve(dictionary["Length"]) is PdfNumber number)
        {
            long declared = number.ToInt64();
            if (declared >= 0 && declared <= available)
                length = (int)declared;
        }

        if (length < 0 || !EndstreamFollows(start + length))
        {
            // A wrong or indirect-and-broken /Length is common. Scanning forward for
            // "endstream" is bounded by the remaining buffer, so it terminates.
            int found = IndexOfAscii(start, "endstream");
            if (found < 0)
            {
                _diagnostics.Warning(PdfDiagnosticCodes.ObjectMalformed, "A stream had no endstream keyword; it was truncated at the end of the file.");
                length = available;
            }
            else
            {
                int end = found;
                // Trim the EOL that precedes "endstream".
                if (end > start && _data[end - 1] == 10)
                    end--;
                if (end > start && _data[end - 1] == 13)
                    end--;
                length = end - start;
                if (dictionary.ContainsKey("Length"))
                    _diagnostics.Warning(PdfDiagnosticCodes.ObjectMalformed, "A stream's /Length disagreed with its endstream keyword; the keyword was used.");
            }
        }

        if (length > _budget.Limits.MaxSingleStreamBytes)
            throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxSingleStreamBytes), _budget.Limits.MaxSingleStreamBytes);

        var raw = new byte[length];
        Array.Copy(_data, start, raw, 0, length);
        _budget.ChargeWork(length / 64 + 1);
        lexer.Position = Math.Min(_data.Length, start + length);
        return new PdfStream(dictionary, raw);
    }

    private bool EndstreamFollows(int position)
    {
        for (int i = position; i < Math.Min(_data.Length, position + 4); i++)
        {
            if (MatchesAscii(i, "endstream"))
                return true;
            if (!PdfLexer.IsWhitespace(_data[i]))
                return false;
        }

        return false;
    }

    // ---- byte helpers ---------------------------------------------------------

    /// <summary>
    /// Turns a cross-reference offset into a buffer position.
    /// </summary>
    /// <remarks>
    /// Offsets are defined relative to the <c>%PDF-</c> header, which is not
    /// always at byte zero. Producers disagree in practice — some write offsets
    /// from the start of the file instead — so a file with a preamble is tried
    /// both ways, and <see cref="TryResolveObjectPosition"/> picks whichever one
    /// actually lands on an object header.
    /// </remarks>
    private int ToBufferOffset(long offset)
    {
        long adjusted = offset + _headerOffset;
        if (adjusted >= 0 && adjusted < _data.Length)
            return (int)adjusted;
        if (offset >= 0 && offset < _data.Length)
            return (int)offset;
        return -1;
    }

    // Yields the candidate positions for an offset, header-relative first.
    private IEnumerable<int> CandidatePositions(long offset)
    {
        long adjusted = offset + _headerOffset;
        if (adjusted >= 0 && adjusted < _data.Length)
            yield return (int)adjusted;
        if (_headerOffset != 0 && offset >= 0 && offset < _data.Length)
            yield return (int)offset;
    }

    private bool TryResolveObjectPosition(
        long offset,
        out int objectNumber,
        out int generation,
        out PdfObject? value)
    {
        foreach (int position in CandidatePositions(offset))
        {
            if (TryReadIndirectObjectAt(position, out objectNumber, out generation, out value))
                return true;
        }

        objectNumber = 0;
        generation = 0;
        value = null;
        return false;
    }

    private long? ReadOffsetEntry(PdfDictionary dictionary, string key) =>
        dictionary[key] is PdfNumber number && number.ToInt64() > 0 ? number.ToInt64() : null;

    private long FindStartXref()
    {
        int window = Math.Min(_data.Length, 2048);
        for (int i = _data.Length - window; i <= _data.Length - 9; i++)
        {
            if (i < 0)
                continue;
            if (!MatchesAscii(i, "startxref"))
                continue;

            // Keep looking: the last startxref in the file names the newest revision.
            int candidate = i;
            for (int j = i + 1; j <= _data.Length - 9; j++)
            {
                if (MatchesAscii(j, "startxref"))
                    candidate = j;
            }

            var lexer = new PdfLexer(_data, _budget.Limits, candidate + 9);
            PdfToken token = lexer.ReadToken();
            return token.Type == PdfTokenType.Integer ? (long)token.Number : -1;
        }

        return -1;
    }

    private static int FindHeader(byte[] data)
    {
        int window = Math.Min(data.Length, 1024);
        for (int i = 0; i + 5 <= window; i++)
        {
            if (data[i] == (byte)'%' &&
                data[i + 1] == (byte)'P' &&
                data[i + 2] == (byte)'D' &&
                data[i + 3] == (byte)'F' &&
                data[i + 4] == (byte)'-')
                return i;
        }

        return -1;
    }

    private bool MatchesAscii(int position, string text)
    {
        if (position < 0 || position + text.Length > _data.Length)
            return false;
        for (int i = 0; i < text.Length; i++)
        {
            if (_data[position + i] != (byte)text[i])
                return false;
        }

        return true;
    }

    private int IndexOfAscii(int start, string text)
    {
        for (int i = Math.Max(0, start); i + text.Length <= _data.Length; i++)
        {
            if (MatchesAscii(i, text))
                return i;
        }

        return -1;
    }
}
