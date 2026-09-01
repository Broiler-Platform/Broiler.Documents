using System;
using System.Collections.Generic;
using System.Threading;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Filters;

/// <summary>The outcome of decoding one stream's whole filter chain.</summary>
internal readonly struct PdfStreamDecodeResult
{
    private PdfStreamDecodeResult(byte[]? data, string? code, string? message, string? filter, bool imageData)
    {
        Data = data;
        DiagnosticCode = code;
        Message = message;
        Filter = filter;
        IsImageData = imageData;
    }

    public byte[]? Data { get; }

    public string? DiagnosticCode { get; }

    public string? Message { get; }

    /// <summary>The filter the chain stopped on, when it did not complete.</summary>
    public string? Filter { get; }

    /// <summary>
    /// True when the chain ends in an image codec: the bytes are samples for an
    /// image service, never a byte stream for the object layer to parse.
    /// </summary>
    public bool IsImageData { get; }

    public bool Succeeded => Data is not null;

    public static PdfStreamDecodeResult Success(byte[] data) => new(data, null, null, null, false);

    public static PdfStreamDecodeResult Failed(string code, string message, string? filter = null, bool imageData = false) =>
        new(null, code, message, filter, imageData);
}

/// <summary>
/// Runs a stream's declared filter chain through the composed filters, charging
/// every stage against the document's budgets.
/// </summary>
/// <remarks>
/// One pipeline serves structural streams (xref streams, object streams) and
/// content streams alike. There is deliberately no second, looser decoder for
/// bootstrap use: a cross-reference stream that names an uncleared filter is
/// rejected by exactly the same rule as a page's content (PDF roadmap §8.1).
/// </remarks>
internal sealed class PdfFilterPipeline
{
    private readonly Dictionary<string, IPdfStreamFilter> _filters;
    private readonly CancellationToken _cancellation;

    public PdfFilterPipeline(IEnumerable<IPdfStreamFilter> filters, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _filters = new Dictionary<string, IPdfStreamFilter>(StringComparer.Ordinal);
        _cancellation = cancellation;

        foreach (IPdfStreamFilter filter in filters)
        {
            if (filter is null)
                continue;
            _filters[PdfFilterNames.Canonicalize(filter.Name)] = filter;
            if (!string.IsNullOrEmpty(filter.Abbreviation))
                _filters[PdfFilterNames.Canonicalize(filter.Abbreviation)] = filter;
        }
    }

    public bool IsComposed(string filterName) => _filters.ContainsKey(PdfFilterNames.Canonicalize(filterName));

    /// <summary>The canonical filter names this pipeline can decode, ordered for stable reporting.</summary>
    public IReadOnlyCollection<string> ComposedFilters
    {
        get
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (IPdfStreamFilter filter in _filters.Values)
                names.Add(PdfFilterNames.Canonicalize(filter.Name));
            return names;
        }
    }

    /// <summary>
    /// Decodes a stream. <paramref name="resolve"/> follows indirect references,
    /// because <c>/Filter</c> and <c>/DecodeParms</c> are both legally indirect.
    /// </summary>
    public PdfStreamDecodeResult Decode(PdfStream stream, Func<PdfObject?, PdfObject?> resolve, PdfWorkBudget budget) =>
        DecodeCore(stream, resolve, budget, wantSamples: false, expandStreamParameters: true);

    /// <summary>
    /// Decodes a stream whose chain ends in an image filter, returning that
    /// filter's samples.
    /// </summary>
    /// <remarks>
    /// The same pipeline and the same budgets as <see cref="Decode"/>; the only
    /// difference is what the caller is entitled to receive. The object layer
    /// asks for bytes and must never be handed pixels, so it uses
    /// <see cref="Decode"/> and an image filter ends the chain in a refusal. An
    /// image service asks for samples and gets them. Splitting the two keeps
    /// "pixels are not PDF syntax" a property of the type system rather than a
    /// rule every caller has to remember.
    /// </remarks>
    public PdfStreamDecodeResult DecodeImage(PdfStream stream, Func<PdfObject?, PdfObject?> resolve, PdfWorkBudget budget) =>
        DecodeCore(stream, resolve, budget, wantSamples: true, expandStreamParameters: true);

    private PdfStreamDecodeResult DecodeCore(
        PdfStream stream,
        Func<PdfObject?, PdfObject?> resolve,
        PdfWorkBudget budget,
        bool wantSamples,
        bool expandStreamParameters)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(budget);

        List<string> filterNames = ReadFilterNames(stream.Dictionary, resolve);
        if (filterNames.Count > budget.Limits.MaxFilterChainDepth)
        {
            return PdfStreamDecodeResult.Failed(
                PdfDiagnosticCodes.FilterLimit,
                $"A stream declared {filterNames.Count} chained filters, past the limit of {budget.Limits.MaxFilterChainDepth}.");
        }

        List<PdfDictionary?> parameters = ReadDecodeParms(stream.Dictionary, resolve, filterNames.Count);
        byte[] data = stream.RawData;

        for (int stage = 0; stage < filterNames.Count; stage++)
        {
            budget.ThrowIfCancelled();
            string name = filterNames[stage];

            if (!_filters.TryGetValue(name, out IPdfStreamFilter? filter))
            {
                string code = PdfFilterNames.UnsupportedDiagnosticFor(name);
                string reason = PdfFilterNames.IsKnown(name)
                    ? $"The stream uses the {name} filter, which this build does not compose; the stream was detected and skipped."
                    : $"The stream declares the unknown filter {name}; the stream was skipped.";
                return PdfStreamDecodeResult.Failed(code, reason, name, PdfFilterNames.IsImageFilter(name));
            }

            bool imageStage = !filter.ProducesByteStream;
            if (imageStage)
            {
                // An image codec is the end of the chain by definition. Its output
                // belongs to an image service, so a caller after bytes stops here.
                if (!wantSamples)
                {
                    return PdfStreamDecodeResult.Failed(
                        PdfDiagnosticCodes.ImageNotComposed,
                        $"The stream ends in the image filter {name}; its samples are not a byte stream.",
                        name,
                        imageData: true);
                }

                if (stage != filterNames.Count - 1)
                {
                    return PdfStreamDecodeResult.Failed(
                        PdfDiagnosticCodes.FilterMalformed,
                        $"The image filter {name} is followed by further filters; samples cannot be filtered again.",
                        name,
                        imageData: true);
                }
            }

            long ceiling = Math.Min(budget.Limits.MaxSingleStreamBytes, budget.RemainingDecodedBytes);
            if (ceiling <= 0)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxDecodedStreamBytes), budget.Limits.MaxDecodedStreamBytes);

            var context = new PdfFilterContext(ceiling, budget.Limits.MaxStreamExpansionRatio, _cancellation);
            PdfFilterParameters typed = PdfFilterParameters.Empty;
            PdfDictionary? parms = parameters.Count > stage ? parameters[stage] : null;
            if (parms is not null)
                typed = ToParameters(parms, resolve, budget, expandStreamParameters);

            PdfFilterResult result = filter.Decode(data, typed, context);
            if (!result.Succeeded)
            {
                return PdfStreamDecodeResult.Failed(
                    result.DiagnosticCode ?? PdfDiagnosticCodes.FilterMalformed,
                    result.Message ?? $"The {name} filter could not decode the stream.",
                    name,
                    imageData: imageStage);
            }

            data = result.Data!;
            budget.ChargeDecodedBytes(data.Length);

            // Samples are the result, not an input to a predictor: a predictor
            // reconstructs byte rows, and there are no byte rows left to
            // reconstruct once a codec has produced pixels.
            if (imageStage)
                return PdfStreamDecodeResult.Success(data);

            if (parms is not null && !TryApplyPredictor(ref data, parms, resolve, out string? predictorError))
                return PdfStreamDecodeResult.Failed(PdfDiagnosticCodes.FilterMalformed, predictorError!, name);
        }

        return PdfStreamDecodeResult.Success(data);
    }

    private static bool TryApplyPredictor(
        ref byte[] data,
        PdfDictionary parms,
        Func<PdfObject?, PdfObject?> resolve,
        out string? error)
    {
        int predictor = ReadInt(parms, "Predictor", PdfPredictor.None, resolve);
        if (predictor <= PdfPredictor.None)
        {
            error = null;
            return true;
        }

        int colors = ReadInt(parms, "Colors", 1, resolve);
        int bits = ReadInt(parms, "BitsPerComponent", 8, resolve);
        int columns = ReadInt(parms, "Columns", 1, resolve);

        if (!PdfPredictor.TryReverse(data, predictor, colors, bits, columns, out byte[] undone, out error))
            return false;

        data = undone;
        return true;
    }

    private static List<string> ReadFilterNames(PdfDictionary dictionary, Func<PdfObject?, PdfObject?> resolve)
    {
        var names = new List<string>();
        PdfObject? filter = resolve(dictionary["Filter"] ?? dictionary["F"]);

        switch (filter)
        {
            case PdfName single:
                names.Add(PdfFilterNames.Canonicalize(single.Value));
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                {
                    if (resolve(entry) is PdfName name)
                        names.Add(PdfFilterNames.Canonicalize(name.Value));
                }

                break;
        }

        return names;
    }

    private static List<PdfDictionary?> ReadDecodeParms(
        PdfDictionary dictionary,
        Func<PdfObject?, PdfObject?> resolve,
        int stageCount)
    {
        var parameters = new List<PdfDictionary?>(stageCount);
        PdfObject? parms = resolve(dictionary["DecodeParms"] ?? dictionary["DP"]);

        switch (parms)
        {
            case PdfDictionary single:
                parameters.Add(single);
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                    parameters.Add(resolve(entry) as PdfDictionary);
                break;
        }

        while (parameters.Count < stageCount)
            parameters.Add(null);

        return parameters;
    }

    private static int ReadInt(PdfDictionary dictionary, string key, int fallback, Func<PdfObject?, PdfObject?> resolve) =>
        resolve(dictionary[key]) is PdfNumber number ? number.ToInt32() : fallback;

    // Projects a DecodeParms dictionary into the public parameter view. Only the
    // scalar kinds an extension can act on cross the boundary; nested objects stay
    // internal so no extension point ever sees a PdfObject.
    /// <summary>
    /// Resolves a <c>DecodeParms</c> dictionary into the typed values a filter
    /// sees. A stream-valued entry is decoded here rather than handed over raw,
    /// so an extension is never asked to run the pipeline itself.
    /// </summary>
    /// <remarks>
    /// <paramref name="expandStreams"/> is false for the nested decode of a
    /// stream parameter, which bounds the recursion at one level. A parameter of
    /// a parameter is not a case the format has, and allowing it would turn a
    /// hostile dictionary into unbounded work.
    /// </remarks>
    private PdfFilterParameters ToParameters(
        PdfDictionary dictionary,
        Func<PdfObject?, PdfObject?> resolve,
        PdfWorkBudget budget,
        bool expandStreams)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, PdfObject> entry in dictionary)
        {
            switch (resolve(entry.Value))
            {
                case PdfNumber number:
                    values[entry.Key] = number.IsInteger ? number.ToInt64() : number.Value;
                    break;
                case PdfBoolean boolean:
                    values[entry.Key] = boolean.Value;
                    break;
                case PdfName name:
                    values[entry.Key] = name.Value;
                    break;
                case PdfStream nested when expandStreams:
                {
                    PdfStreamDecodeResult inner = DecodeCore(
                        nested, resolve, budget, wantSamples: false, expandStreamParameters: false);
                    if (inner.Succeeded)
                        values[entry.Key] = inner.Data;
                    break;
                }
            }
        }

        return new PdfFilterParameters(values);
    }
}
