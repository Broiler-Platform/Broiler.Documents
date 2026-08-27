using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Broiler.Documents.Pdf.Filters;
using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf;

/// <summary>
/// The immutable service graph a <see cref="PdfDocumentCodec"/> is constructed
/// with. Everything optional the codec can do arrives here.
/// </summary>
/// <remarks>
/// <para>
/// The codec discovers nothing. There is no static registry, module initializer,
/// environment variable, ambient font resolver, or platform lookup anywhere in
/// this package: a capability the composing application did not supply simply is
/// not present, and the codec reports its absence with a stable diagnostic
/// (ADR 0008, PDF roadmap §6.1).
/// </para>
/// <para>
/// That is what makes the step-by-step plan work. <see cref="Base"/> composes
/// only what this repository implements itself and can therefore ship without a
/// third-party review: the Flate, ASCIIHex, ASCII85, and RunLength filters, and
/// the approximate metric model. Each further technology — LZW, DCT/JPEG,
/// CCITT, JPX, JBIG2, embedded font programs, encryption — becomes available by
/// adding a reviewed implementation to this graph, with no change to the parser,
/// the interpreter, or the writer.
/// </para>
/// </remarks>
public sealed class PdfCodecServices
{
    /// <summary>
    /// The base composition: the filters and metrics this repository implements,
    /// and nothing that would require clearing an outside component.
    /// </summary>
    public static PdfCodecServices Base { get; } = new();

    public PdfCodecServices(
        IEnumerable<IPdfStreamFilter>? streamFilters = null,
        IPdfFontMetricsProvider? fontMetrics = null,
        PdfUriPolicy? uriPolicy = null)
    {
        var filters = new List<IPdfStreamFilter>
        {
            new FlateDecodeFilter(),
            new AsciiHexDecodeFilter(),
            new Ascii85DecodeFilter(),
            new RunLengthDecodeFilter(),
        };

        if (streamFilters is not null)
        {
            foreach (IPdfStreamFilter filter in streamFilters)
            {
                if (filter is null)
                    throw new ArgumentException("The filter collection contains a null entry.", nameof(streamFilters));

                // A caller-supplied filter replaces a built-in of the same name, so
                // a reviewed implementation can supersede one of ours deliberately.
                filters.RemoveAll(existing =>
                    string.Equals(
                        PdfFilterNames.Canonicalize(existing.Name),
                        PdfFilterNames.Canonicalize(filter.Name),
                        StringComparison.Ordinal));
                filters.Add(filter);
            }
        }

        StreamFilters = new ReadOnlyCollection<IPdfStreamFilter>(filters);
        FontMetrics = fontMetrics ?? PdfApproximateFontMetrics.Instance;
        UriPolicy = uriPolicy ?? PdfUriPolicy.Default;
    }

    /// <summary>
    /// The composed stream filters, always including the four this package
    /// implements itself.
    /// </summary>
    public IReadOnlyList<IPdfStreamFilter> StreamFilters { get; }

    /// <summary>The metrics used for writer line breaking and reader gap estimation.</summary>
    public IPdfFontMetricsProvider FontMetrics { get; }

    /// <summary>The policy that decides which URIs may become active links.</summary>
    public PdfUriPolicy UriPolicy { get; }

    /// <summary>
    /// Returns a copy of this graph with additional or replacing filters. Use it
    /// to add a reviewed decoder without restating the base composition.
    /// </summary>
    public PdfCodecServices WithStreamFilters(params IPdfStreamFilter[] filters) =>
        new(filters, FontMetrics, UriPolicy);

    /// <summary>Returns a copy of this graph with a different metrics provider.</summary>
    public PdfCodecServices WithFontMetrics(IPdfFontMetricsProvider metrics) =>
        new(CallerSuppliedFilters(), metrics ?? throw new ArgumentNullException(nameof(metrics)), UriPolicy);

    /// <summary>Returns a copy of this graph with a different URI policy.</summary>
    public PdfCodecServices WithUriPolicy(PdfUriPolicy policy) =>
        new(CallerSuppliedFilters(), FontMetrics, policy ?? throw new ArgumentNullException(nameof(policy)));

    /// <summary>True when a decoder for <paramref name="filterName"/> is composed.</summary>
    public bool SupportsFilter(string filterName)
    {
        ArgumentNullException.ThrowIfNull(filterName);
        string canonical = PdfFilterNames.Canonicalize(filterName);
        foreach (IPdfStreamFilter filter in StreamFilters)
        {
            if (string.Equals(PdfFilterNames.Canonicalize(filter.Name), canonical, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // The constructor re-adds the built-ins, so only the extras are carried over
    // when a With* method rebuilds the graph.
    private List<IPdfStreamFilter> CallerSuppliedFilters()
    {
        var extras = new List<IPdfStreamFilter>();
        foreach (IPdfStreamFilter filter in StreamFilters)
        {
            if (filter is not FlateDecodeFilter and not AsciiHexDecodeFilter and not Ascii85DecodeFilter and not RunLengthDecodeFilter)
                extras.Add(filter);
        }

        return extras;
    }
}
