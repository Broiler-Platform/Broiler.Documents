using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Broiler.Documents.Pdf;

/// <summary>
/// How a dropped path was drawn, as far as its own geometry can say.
/// </summary>
/// <remarks>
/// The classification is deliberately coarse and deliberately not acted on. It
/// exists because "vector artwork was dropped" answers a different question
/// depending on whether the artwork was a page border, a table's cell rules, or
/// a chart: the first two say the logical model lost structure it might one day
/// reconstruct, the third says it lost a picture it never could. Nothing in the
/// reader branches on this — it only reports it.
/// </remarks>
internal enum PdfArtworkKind
{
    /// <summary>A thin axis-aligned bar: a rule, an underline, or a table's cell border.</summary>
    Rule,

    /// <summary>An axis-aligned area: a cell shade, a frame, a background panel.</summary>
    Block,

    /// <summary>A smooth shading, from the <c>sh</c> operator or a shading pattern.</summary>
    Shading,

    /// <summary>Anything else — curves, diagonals, compound paths. Genuine artwork.</summary>
    Path,
}

/// <summary>
/// The PDF-side description of a raster image this build detected and skipped.
/// </summary>
/// <remarks>
/// Every field comes from the image dictionary, never from the sample data: this
/// build composes no decoder, so it cannot and does not look inside. That is
/// precisely why the tuple is worth reporting — the register row for
/// <c>DCTDecode</c> (IP-005) has to approve exact tuples, and this is the part of
/// one that is knowable without a decoder.
/// </remarks>
internal readonly record struct PdfImageShape(
    int Width,
    int Height,
    int BitsPerComponent,
    string ColorSpace,
    string Filters,
    bool IsInline)
{
    /// <summary>The tuple as one short token, for grouping and for the message.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        if (Width > 0 && Height > 0)
            text.Append(CultureInfo.InvariantCulture, $"{Width}x{Height}");
        else
            text.Append("unstated size");

        if (BitsPerComponent > 0)
            text.Append(CultureInfo.InvariantCulture, $" {BitsPerComponent}bpc");
        if (ColorSpace.Length > 0)
            text.Append(' ').Append(ColorSpace);
        if (Filters.Length > 0)
            text.Append(' ').Append(Filters);

        return text.ToString();
    }
}

/// <summary>
/// What became of an embedded font program, and why.
/// </summary>
/// <remarks>
/// The distinction the report needs is not "read or not read" but which of three
/// different things stopped it, because they call for three different responses.
/// A build with no reader composed can compose one. A build whose reader was
/// never asked lost nothing — the font already said what its codes mean. A build
/// whose reader was asked and recovered nothing has met the limit of the parser's
/// surface, and composing more changes nothing.
/// </remarks>
internal enum PdfFontProgramInspection
{
    /// <summary>No font-program reader is composed, so nothing could open it.</summary>
    NotComposed,

    /// <summary>
    /// A reader is composed and this program was never offered to it: the font
    /// supplied a <c>ToUnicode</c> map, which outranks anything recovered from a
    /// program, or the font is a simple one, whose program this codec never
    /// recovers text from by design.
    /// </summary>
    NotOffered,

    /// <summary>
    /// A composed reader was offered the program and recovered nothing — a format
    /// past its parser surface, a program past its byte ceiling, or one that
    /// faulted.
    /// </summary>
    Unread,

    /// <summary>A composed reader read the program and its glyph map supplied text.</summary>
    Read,
}

/// <summary>
/// The PDF-side description of an embedded font program this build detected.
/// </summary>
/// <remarks>
/// <see cref="Format"/> is the descriptor key, and the subtype where the key
/// carries one, that names the program's format: <c>FontFile2</c> is TrueType and
/// <c>FontFile3</c> declares its own <c>/Subtype</c>. It is never the font's
/// name, which is a value rather than a construct.
/// </remarks>
internal readonly record struct PdfFontProgram(
    string Format,
    bool Composite,
    bool Symbolic,
    bool HasToUnicode,
    PdfFontProgramInspection Inspection);

/// <summary>
/// Accumulates the constructs this build recognizes but does not implement, so
/// they are reported once per document as an inventory rather than once per
/// occurrence as a repeated sentence.
/// </summary>
/// <remarks>
/// <para>
/// The diagnostic sink already collapses repeats of a code into one entry with a
/// count. That is the right answer for a condition that is the same every time —
/// a malformed xref entry is a malformed xref entry. It is the wrong answer for
/// these three, because the <em>variation</em> is the information: which JPEG
/// tuples a file actually uses decides what an IP-005 approval would have to
/// cover, and forty rules plus two charts is a different document from forty
/// charts.
/// </para>
/// <para>
/// So the interpreter and the font loader record here, and the reader drains this
/// into diagnostics once the last page is done. Draining happens before the
/// result status is computed, so a skipped construct still makes the read
/// <c>Partial</c> exactly as an immediate report did.
/// </para>
/// <para>
/// Every group is bounded. A file with ten thousand distinct image tuples
/// summarizes the tail rather than growing a list with it, and nothing recorded
/// here is document text, a metadata value, or a path (ADR 0009).
/// </para>
/// </remarks>
internal sealed class PdfFeatureTally
{
    /// <summary>Distinct variants one group names before it summarizes the rest.</summary>
    private const int MaxDistinctVariants = 8;

    /// <summary>Distinct page numbers one group names before it stops listing them.</summary>
    private const int MaxNamedPages = 6;

    /// <summary>Font programs held individually before the tail is only counted.</summary>
    private const int MaxRecordedFontPrograms = 64;

    private readonly Dictionary<PdfArtworkKind, int> _artwork = [];
    private readonly PageSet _artworkPages = new();
    private readonly PageSet _tablePages = new();
    private readonly SortedSet<string> _tableShapes = new(StringComparer.Ordinal);
    private int _tables;
    private int _artworkReadAsTableRules;
    private int _artworkReadAsTableBlocks;
    private int _inferredTables;
    private int _continuedTables;
    private int _tableCells;
    private readonly Dictionary<string, ImageGroup> _images = new(StringComparer.Ordinal);
    private readonly DecodedImageGroup _decodedImages = new();
    private readonly SortedSet<int> _notProjectedPages = [];
    private readonly SortedSet<string> _notProjectedReasons = new(StringComparer.Ordinal);
    private readonly SortedSet<int> _deniedPages = [];
    private int _notProjected;
    private int _denied;
    private string? _deniedReason;
    private readonly List<PdfFontProgram> _fontPrograms = [];
    private readonly Dictionary<string, int> _fontProgramDeclined = new(StringComparer.Ordinal);
    private int _fontProgramOverflow;
    private readonly PageSet _uriRejectedPages = new();
    private readonly Dictionary<string, int> _uriRejectedReasons = new(StringComparer.Ordinal);
    private int _uriRejected;
    private int _uriRejectedOverflow;
    private readonly PageSet _activeContentPages = new();
    private int _activeContent;
    private readonly PageSet _droppedDestinationPages = new();
    private int _droppedDestinations;
    private readonly PageSet _hiddenLayerPages = new();
    private int _hiddenLayers;
    private int _undecidableLayers;
    private int _keptLayers;
    private int _optionalContentGroups;
    private int _optionalContentOff;

    /// <summary>
    /// Records one run of content the default optional-content configuration
    /// puts outside the presentation.
    /// </summary>
    public void NoteOptionalContentHidden(int? page)
    {
        _hiddenLayers++;
        _hiddenLayerPages.Add(page);
    }

    /// <summary>
    /// Records one membership dictionary whose visibility expression this build
    /// does not evaluate, and whose content was therefore kept.
    /// </summary>
    public void NoteOptionalContentUndecidable(int? page)
    {
        _undecidableLayers++;
        _hiddenLayerPages.Add(page);
    }

    /// <summary>
    /// Records one run of content the default configuration turns off that was
    /// extracted anyway, because the caller asked for every layer.
    /// </summary>
    public void NoteOptionalContentKept(int? page)
    {
        _keptLayers++;
        _hiddenLayerPages.Add(page);
    }

    /// <summary>
    /// Records what the catalog declared, so the report can say how much of the
    /// document is layered even where nothing was omitted.
    /// </summary>
    public void NoteOptionalContentConfiguration(int groups, int off)
    {
        _optionalContentGroups = groups;
        _optionalContentOff = off;
    }

    /// <summary>
    /// Records one link target the active URI policy refused, and the reason it
    /// gave for refusing it.
    /// </summary>
    /// <remarks>
    /// Accumulated for the document rather than reported per page, because the
    /// sink keeps one entry per code and the first message wins. A page-by-page
    /// report of "1 link target" on two pages produced one sentence saying 1
    /// with "Seen 2 times" appended after it, which reads as one target met
    /// twice rather than two targets refused. The reason had nowhere to go for
    /// the same reason - two pages refusing on different grounds could not both
    /// be said - so the call site discarded it. Both fit here.
    /// </remarks>
    public void NoteUriRejected(int? page, string? reason)
    {
        _uriRejected++;
        _uriRejectedPages.Add(page);

        if (reason is null)
            return;

        // Distinct reasons are kept and counted rather than collapsed: "the
        // mailto scheme is not admitted" is answered by configuring a policy and
        // "the value is not an absolute URI" is answered by nothing at all.
        if (_uriRejectedReasons.TryGetValue(reason, out int seen))
            _uriRejectedReasons[reason] = seen == int.MaxValue ? seen : seen + 1;
        else if (_uriRejectedReasons.Count < MaxDistinctVariants)
            _uriRejectedReasons[reason] = 1;
        else
            _uriRejectedOverflow++;
    }

    /// <summary>
    /// Records one annotation or action that would reach outside this document if
    /// anything executed it.
    /// </summary>
    public void NoteActiveContent(int? page)
    {
        _activeContent++;
        _activeContentPages.Add(page);
    }

    /// <summary>
    /// Records one annotation naming a place inside this document that the
    /// logical model has no anchor for.
    /// </summary>
    public void NoteLinkDestinationDropped(int? page)
    {
        _droppedDestinations++;
        _droppedDestinationPages.Add(page);
    }

    /// <summary>
    /// Records one table read back out of a page's rules.
    /// </summary>
    public void NoteTable(int rows, int columns, bool inferred, int? page)
    {
        _tables++;
        if (inferred)
            _inferredTables++;
        _tableCells += rows * columns;
        _tablePages.Add(page);

        if (_tableShapes.Count < MaxDistinctVariants)
            _tableShapes.Add(string.Create(CultureInfo.InvariantCulture, $"{rows}x{columns}"));
    }

    /// <summary>
    /// Records one table joined to the one it continued from the page before.
    /// </summary>
    public void NoteTableContinued(int? page)
    {
        _continuedTables++;
        _tablePages.Add(page);
    }

    /// <summary>
    /// Records how many painted paths a page's grids were read from, so the
    /// artwork note can report what was dropped rather than what was drawn.
    /// </summary>
    public void NoteArtworkReadAsTable(int rules, int blocks)
    {
        _artworkReadAsTableRules += rules;
        _artworkReadAsTableBlocks += blocks;
    }

    /// <summary>
    /// Records why the composed reader declined one embedded font program.
    /// </summary>
    /// <remarks>
    /// Distinct reasons are kept and counted rather than collapsed, for the
    /// reason the URI refusals are: "the composed reader did not read it" is the
    /// same sentence for a program with no character map, which nothing can fix,
    /// and a format this reader does not parse, which composing a different one
    /// would.
    /// </remarks>
    public void NoteFontProgramDeclined(string reason)
    {
        if (_fontProgramDeclined.TryGetValue(reason, out int seen))
            _fontProgramDeclined[reason] = seen == int.MaxValue ? seen : seen + 1;
        else if (_fontProgramDeclined.Count < MaxDistinctVariants)
            _fontProgramDeclined[reason] = 1;
    }

    /// <summary>Records one dropped path-painting operation.</summary>
    public void NoteArtwork(PdfArtworkKind kind, int? page)
    {
        _artwork.TryGetValue(kind, out int seen);
        _artwork[kind] = seen == int.MaxValue ? seen : seen + 1;
        _artworkPages.Add(page);
    }

    /// <summary>
    /// Records one skipped image under the diagnostic code its filter maps to, so
    /// a JPEG and an unfiltered image are inventoried separately even though both
    /// were skipped for the same reason.
    /// </summary>
    /// <param name="undecoded">
    /// True when a decoder for this image's whole filter chain is composed and
    /// the read declined to run it. It is the difference between a capability
    /// this build does not have and one it has and did not spend here, and only
    /// the caller knows which, so the tally is told rather than inferring it from
    /// the code.
    /// </param>
    public void NoteImage(string code, in PdfImageShape shape, int? page, string? reason = null, bool undecoded = false)
    {
        if (!_images.TryGetValue(code, out ImageGroup? group))
        {
            group = new ImageGroup();
            _images[code] = group;
        }

        group.Add(shape, page, reason, undecoded);
    }

    /// <summary>
    /// Records one image a composed filter decoded successfully. The sample count
    /// is carried rather than the samples: this is an inventory, and holding a
    /// document's worth of decoded pixels to describe them would cost more than
    /// the decode did.
    /// </summary>
    /// <param name="codec">
    /// True when an image codec produced the samples, false when a byte-stream
    /// chain did. The two produce different sample layouts, and only the
    /// declaration's own arithmetic can say whether either matches it.
    /// </param>
    public void NoteDecodedImage(in PdfImageShape declared, long sampleBytes, int? page, bool codec) =>
        _decodedImages.Add(declared, sampleBytes, page, codec);

    /// <summary>
    /// Records a decoded image the model could not take, and what stopped it.
    /// </summary>
    /// <param name="reason">
    /// A short noun phrase naming the construct met. The distinct reasons are
    /// reported together, because a document whose pictures are all CMYK and one
    /// whose pictures are all stencil masks need different work, and a count
    /// alone cannot tell a caller which they have.
    /// </param>
    public void NoteImageNotProjected(int? page, string reason)
    {
        _notProjected++;
        if (page is int number)
            _notProjectedPages.Add(number);

        if (reason.Length > 0 && _notProjectedReasons.Count < MaxDistinctVariants)
            _notProjectedReasons.Add(reason);
    }

    /// <summary>
    /// Records a decoded image the caller's policy refused. Counted apart from
    /// <see cref="NoteImageNotProjected"/> because a decision someone made and a
    /// limit of this build are answered by entirely different work.
    /// </summary>
    public void NoteImageDenied(int? page, string? denial)
    {
        _denied++;
        _deniedReason ??= denial;
        if (page is int number)
            _deniedPages.Add(number);
    }

    /// <summary>
    /// Records one embedded font program that was detected, and what became of
    /// it — including the one case where a composed reader did open it, so the
    /// report can say the build inspected rather than skipped.
    /// </summary>
    public void NoteFontProgram(in PdfFontProgram program)
    {
        if (_fontPrograms.Count >= MaxRecordedFontPrograms)
        {
            _fontProgramOverflow++;
            return;
        }

        _fontPrograms.Add(program);
    }

    /// <summary>
    /// Reports everything accumulated. Called once, after the last page, and
    /// before the reader decides the result status.
    /// </summary>
    public void Report(PdfDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        ReportOptionalContent(diagnostics);
        ReportArtwork(diagnostics);
        ReportImages(diagnostics);
        ReportDecodedImages(diagnostics);
        ReportFontPrograms(diagnostics);
        ReportAnnotations(diagnostics);
        ReportTables(diagnostics);
    }

    /// <summary>
    /// Reports what the document's layers did to the extraction. Silent where a
    /// document declares none, and silent where it declares them and its default
    /// configuration shows them all: a layered document that hid nothing is not
    /// news, and the extraction is exactly what it would have been.
    /// </summary>
    private void ReportOptionalContent(PdfDiagnosticSink diagnostics)
    {
        if (_hiddenLayers == 0 && _undecidableLayers == 0 && _keptLayers == 0)
            return;

        var text = new StringBuilder(
            "The document declares optional content, and its own default configuration ");

        text.Append(CultureInfo.InvariantCulture,
            $"turns {_optionalContentOff} of {_optionalContentGroups} group{S(_optionalContentGroups)} off. ");

        if (_hiddenLayers > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{_hiddenLayers} run{S(_hiddenLayers)} of content {Were(_hiddenLayers)} omitted for belonging to one. ");
            text.Append(
                "That is the catalog's statement about which layers make up the default presentation, not a judgement " +
                "about what a reader displays. ");
        }

        if (_keptLayers > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{_keptLayers} run{S(_keptLayers)} of content belonging to one {Were(_keptLayers)} extracted anyway, ");
            text.Append(
                "because this read asked for every layer. That is not a claim the content is displayed: it is what " +
                "the file carries, with the document's own configuration reported beside it. ");
        }

        if (_undecidableLayers > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{_undecidableLayers} further run{S(_undecidableLayers)} named a visibility expression, ");
            text.Append("which this release does not evaluate; that content was kept rather than guessed at. ");
        }

        text.Append("Alternate configurations and usage applications are not applied.");
        _hiddenLayerPages.Append(text);

        diagnostics.Skipped(PdfDiagnosticCodes.OptionalContentOmitted, text.ToString());
    }

    /// <summary>
    /// Reports what a page's annotations carried: targets a policy refused,
    /// constructs that would leave the document, and jumps with nowhere to land.
    /// </summary>
    private void ReportAnnotations(PdfDiagnosticSink diagnostics)
    {
        if (_activeContent > 0)
        {
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture,
                $"{_activeContent} active annotation{S(_activeContent)} or action{S(_activeContent)} {Were(_activeContent)} detected. ");
            text.Append("None was executed, fetched, or projected into the document.");
            _activeContentPages.Append(text);

            diagnostics.Skipped(PdfDiagnosticCodes.ActiveContentRemoved, text.ToString());
        }

        if (_uriRejected > 0)
        {
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture,
                $"{_uriRejected} link target{S(_uriRejected)} did not pass the active URI policy and {Were(_uriRejected)} left as inert source data");

            if (_uriRejectedReasons.Count > 0)
            {
                var reasons = new List<string>(_uriRejectedReasons.Keys);
                reasons.Sort(StringComparer.Ordinal);

                var parts = new List<string>(reasons.Count + 1);
                foreach (string reason in reasons)
                    parts.Add(string.Create(CultureInfo.InvariantCulture, $"{_uriRejectedReasons[reason]} because {reason}"));

                if (_uriRejectedOverflow > 0)
                    parts.Add(string.Create(CultureInfo.InvariantCulture, $"{_uriRejectedOverflow} on further grounds not listed here"));

                text.Append(": ").Append(string.Join("; ", parts));
            }

            text.Append('.');
            _uriRejectedPages.Append(text);

            diagnostics.Skipped(PdfDiagnosticCodes.UriRejected, text.ToString());
        }

        if (_droppedDestinations > 0)
        {
            // Skipped, not Info: the same visible loss - a run that stays plain
            // text - is Skipped when the policy refuses a URI, and a table of
            // contents that came back with every entry as text used to report
            // Success. The destination value is never read, so nothing the file
            // named reaches the message.
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture,
                $"{_droppedDestinations} annotation{S(_droppedDestinations)} named a place inside the document rather than a URI. ");
            text.Append("The text was kept and no link was projected: this build carries no bookmark or anchor for an internal jump to land on.");
            _droppedDestinationPages.Append(text);

            diagnostics.Skipped(PdfDiagnosticCodes.LinkDestinationDropped, text.ToString());
        }
    }

    /// <summary>
    /// Reports what was read back out of the artwork, as information rather than
    /// a skip: nothing was lost here, and the sentence exists because a
    /// reconstruction is a claim a reader should be able to check.
    /// </summary>
    private void ReportTables(PdfDiagnosticSink diagnostics)
    {
        if (_tables == 0)
            return;

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"{_tables} fully ruled grid{S(_tables)} {Were(_tables)} read as {(_tables == 1 ? "a table" : "tables")} and carried into the document, ");
        text.Append(CultureInfo.InvariantCulture, $"{_tableCells} cell{S(_tableCells)} in all ({string.Join(", ", _tableShapes)}). ");
        text.Append(
            "PDF draws a table as lines and text at coordinates and says nowhere that it is one, so this is a " +
            "reconstruction: the text was arranged into the cells the grid bounds, and each cell's borders and " +
            "shading are the paths that were painted, so an unruled edge carries no border. ");

        // The split a host acts on. One of these is the document's own lattice;
        // the other is the document's rules plus this build's reading of the
        // text between them, and they are not the same claim.
        if (_inferredTables == 0)
        {
            text.Append("Every one was fully ruled: each cell edge was painted.");
        }
        else if (_inferredTables == _tables)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{_inferredTables} of them {Were(_inferredTables)} only partly ruled - the document stacked enough rules to divide the region in one direction, and the remaining divisions were read off the alignment of the text inside it.");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{_tables - _inferredTables} {Were(_tables - _inferredTables)} fully ruled and {_inferredTables} only partly, the remaining divisions there being read off the alignment of the text inside the region.");
        }
        if (_continuedTables > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $" {_continuedTables} grid{S(_continuedTables)} continued a table from the page before and {Were(_continuedTables)} joined to it: the columns matched and neither page drew anything between them. A mapped page break is not inserted inside a table that was joined.");
        }

        _tablePages.Append(text);

        diagnostics.Info(PdfDiagnosticCodes.TableReconstructed, text.ToString());
    }

    private void ReportArtwork(PdfDiagnosticSink diagnostics)
    {
        if (_artwork.Count == 0)
            return;

        int rules = Count(PdfArtworkKind.Rule);
        int blocks = Count(PdfArtworkKind.Block);
        int shadings = Count(PdfArtworkKind.Shading);
        int paths = Count(PdfArtworkKind.Path);
        int total = rules + blocks + shadings + paths;

        // Only bars and areas can be a grid's; a shading and a curve never are.
        // Subtracting per kind is what lets the breakdown describe the dropped
        // paths rather than every path painted - which is the number a reader
        // wants, and the one the sentence claimed to be giving all along.
        int takenRules = Math.Min(_artworkReadAsTableRules, rules);
        int takenBlocks = Math.Min(_artworkReadAsTableBlocks, blocks);
        int taken = takenRules + takenBlocks;
        int lost = total - taken;

        rules -= takenRules;
        blocks -= takenBlocks;

        var text = new StringBuilder();
        text.Append(
            lost == 0 ? "The page draws vector artwork, and all of it formed a grid that was read as a table. "
            : taken > 0 ? "The page draws vector artwork. What formed a grid was read as a table; the rest, which a logical rich-text document cannot represent, was dropped. "
            : "The page draws vector artwork, which a logical rich-text document cannot represent. It was dropped. ");

        if (lost == 0)
        {
            // Every path the page painted turned out to be a table's. There is
            // no breakdown to give, because nothing was dropped to break down.
            text.Append(CultureInfo.InvariantCulture,
                $"All {total} path-painting operation{S(total)} {Were(total)} read as a table's rules and shades; none was dropped.");
            text.Append(" The tables are reported under pdf.import.table-reconstructed.");
            _artworkPages.Append(text);
            diagnostics.Skipped(PdfDiagnosticCodes.VectorArtworkDropped, text.ToString());
            return;
        }

        if (taken > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"Of {total} path-painting operation{S(total)}, {taken} {Were(taken)} read as a table's rules and shades. ");
            text.Append(CultureInfo.InvariantCulture, $"The other {lost} {Were(lost)} dropped: ");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"{total} path-painting operation{S(total)} {Were(total)} dropped: ");
        }

        var parts = new List<string>(4);
        if (rules > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{rules} thin axis-aligned bar{S(rules)}, the shape of a rule, an underline, or a table border"));
        if (blocks > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{blocks} axis-aligned area{S(blocks)}, the shape of a cell shade, a frame, or a panel"));
        if (shadings > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{shadings} smooth shading{S(shadings)}"));
        if (paths > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{paths} general path{S(paths)} carrying curves or diagonals"));

        // Each class names the structure it usually stood for, so the split
        // between "lost a table's rules" and "lost a chart" reads off the
        // sentence without a paragraph of rationale attached to every document.
        text.Append(string.Join("; ", parts)).Append('.');

        // Said here rather than only in the other note, because this is the
        // sentence a reader reaches first and "it was dropped" is no longer the
        // whole truth once some of those bars turned out to bound cells.
        if (_tables > 0)
            text.Append(" The tables are reported under pdf.import.table-reconstructed.");

        _artworkPages.Append(text);

        diagnostics.Skipped(PdfDiagnosticCodes.VectorArtworkDropped, text.ToString());

        int Count(PdfArtworkKind kind) => _artwork.TryGetValue(kind, out int value) ? value : 0;
    }

    private void ReportImages(PdfDiagnosticSink diagnostics)
    {
        // Ordered by code so a document with both a JPEG and a Flate image
        // reports them in the same order on every run.
        var codes = new List<string>(_images.Keys);
        codes.Sort(StringComparer.Ordinal);

        foreach (string code in codes)
            diagnostics.Skipped(code, _images[code].Describe(code));
    }

    private void ReportDecodedImages(PdfDiagnosticSink diagnostics)
    {
        // Only the images that did not reach the model are reported now. A
        // decoded image that became an InlineImage is a success, and saying it
        // was "decoded but not projected" was true only while nothing could be.
        if (_notProjected > 0)
        {
            // The tally's own description carries what the decode learned —
            // including a dictionary that disagrees with its samples, which is
            // one of the reasons an image is not carried. Appending the reasons
            // rather than replacing that keeps both halves of the story.
            var why = new StringBuilder();
            why.Append(CultureInfo.InvariantCulture,
                $" {_notProjected} of them {Were(_notProjected)} not carried into the document");

            if (_notProjectedReasons.Count > 0)
                why.Append(", having met: ").Append(string.Join("; ", _notProjectedReasons));

            why.Append(". The samples remain reachable through the filter pipeline.");
            why.Append(PagesPhrase(_notProjectedPages));

            diagnostics.Skipped(
                PdfDiagnosticCodes.ImageDecodedNotProjected,
                _decodedImages.Describe() + why);
        }

        if (_denied > 0)
        {
            string where = PagesPhrase(_deniedPages);
            string why = _deniedReason is null ? "." : ": " + _deniedReason + ".";
            diagnostics.Skipped(
                PdfDiagnosticCodes.ImageExtractionDenied,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{_denied} decoded image(s) were refused by the resource policy{where}{why}"));
        }
    }

    /// <summary>The pages a tally was seen on, as a phrase, or nothing.</summary>
    private static string PagesPhrase(SortedSet<int> pages) =>
        pages.Count == 0
            ? string.Empty
            : " (page" + (pages.Count == 1 ? " " : "s ") + string.Join(", ", pages) + ")";

    private void ReportFontPrograms(PdfDiagnosticSink diagnostics)
    {
        if (_fontPrograms.Count == 0)
            return;

        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        int symbolic = 0;
        int withoutToUnicode = 0;
        int composite = 0;
        int inspected = 0;
        int unread = 0;
        int notOffered = 0;
        int notComposed = 0;

        foreach (PdfFontProgram program in _fontPrograms)
        {
            formats.TryGetValue(program.Format, out int seen);
            formats[program.Format] = seen + 1;
            if (program.Symbolic)
                symbolic++;
            if (!program.HasToUnicode)
                withoutToUnicode++;
            if (program.Composite)
                composite++;

            switch (program.Inspection)
            {
                case PdfFontProgramInspection.Read:
                    inspected++;
                    break;
                case PdfFontProgramInspection.Unread:
                    unread++;
                    break;
                case PdfFontProgramInspection.NotOffered:
                    notOffered++;
                    break;
                case PdfFontProgramInspection.NotComposed:
                    notComposed++;
                    break;
            }
        }

        int total = _fontPrograms.Count + _fontProgramOverflow;
        var text = new StringBuilder();

        // Four outcomes, and the lead sentence has to name the one that happened.
        // Whether a reader is composed is a fact about the build; whether it was
        // offered a program is a fact about the font. Saying "this build does not
        // inspect" where one is composed named a gap the build did not have, and
        // saying "did not read" where the reader was never asked describes an
        // attempt that was never made - which is the shape a reader acts on, and
        // would send them looking for a parser that was never the obstacle.
        text.Append(inspected > 0
            ? "A font embeds a program a composed reader inspected for the text its glyphs stand for. "
            : notComposed == _fontPrograms.Count
                ? "A font embeds a program this build does not inspect; text was mapped from ToUnicode and the declared encoding only. "
                : unread > 0
                    ? "A font embeds a program the composed reader did not read; text was mapped from ToUnicode and the declared encoding only. "
                    : "A font embeds a program the composed reader was never offered: the font's own ToUnicode map already says what its codes mean, so the program was left alone rather than consulted. ");
        text.Append(CultureInfo.InvariantCulture, $"{total} embedded font program{S(total)} {Were(total)} detected");
        if (composite > 0)
            text.Append(CultureInfo.InvariantCulture, $", {composite} of them on a composite font");
        text.Append(": ").Append(DescribeCounts(formats));
        if (_fontProgramOverflow > 0)
            text.Append(CultureInfo.InvariantCulture, $", and {_fontProgramOverflow} further program{S(_fontProgramOverflow)} counted but not classified");
        text.Append('.');

        if (inspected > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $" {inspected} of them {Were(inspected)} read for a glyph-to-text map, which is where the text of those fonts came from.");
        }

        if (notOffered > 0 && (inspected > 0 || unread > 0 || notComposed > 0))
        {
            text.Append(CultureInfo.InvariantCulture,
                $" {notOffered} of them {Were(notOffered)} never offered to it, their own ToUnicode map having already answered.");
        }

        // Offered and refused is not the same as never offered, and only this
        // sentence separates them: it says the composition was not the obstacle,
        // so a caller reading the note knows the next move is a reader that
        // covers the format rather than any reader at all.
        if (unread > 0)
        {
            text.Append(CultureInfo.InvariantCulture,
                $" {unread} {Were(unread)} offered to the composed reader, which recovered nothing from {(unread == 1 ? "it" : "them")}.");
        }

        // The combination that actually costs text: no program to read glyph
        // names from, no ToUnicode to fall back on, and a symbolic flag saying
        // the standard encodings do not apply either.
        if (withoutToUnicode > 0)
        {
            int stranded = withoutToUnicode - inspected;
            if (stranded <= 0)
            {
                Report(diagnostics, text);
                return;
            }

            text.Append(CultureInfo.InvariantCulture, $" {stranded} {Is(stranded)} without a ToUnicode map and uninspected");
            text.Append(symbolic > 0
                ? string.Create(CultureInfo.InvariantCulture, $", and {symbolic} {Is(symbolic)} marked symbolic, so the text rests on the declared encoding alone and may be wrong.")
                : ", so the text rests on the declared encoding alone.");
        }
        else if (symbolic > 0 && inspected == 0)
        {
            text.Append(CultureInfo.InvariantCulture, $" {symbolic} {Is(symbolic)} marked symbolic, but every one supplies a ToUnicode map, so the text was mapped from it rather than guessed.");
        }

        if (unread > 0 && _fontProgramDeclined.Count > 0)
        {
            var reasons = new List<string>(_fontProgramDeclined.Keys);
            reasons.Sort(StringComparer.Ordinal);

            var parts = new List<string>(reasons.Count);
            foreach (string reason in reasons)
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{_fontProgramDeclined[reason]} because {reason}"));

            text.Append(" The reader declined them: ").Append(string.Join("; ", parts)).Append('.');
        }

        Report(diagnostics, text);
    }

    private static void Report(PdfDiagnosticSink diagnostics, StringBuilder text) =>
        diagnostics.Skipped(PdfDiagnosticCodes.FontProgramNotComposed, text.ToString());

    /// <summary>The plural "s" for a count, so an inventory reads as English.</summary>
    private static string S(int count) => count == 1 ? string.Empty : "s";

    /// <summary>"was" or "were" for a count.</summary>
    private static string Were(int count) => count == 1 ? "was" : "were";

    /// <summary>"is" or "are" for a count.</summary>
    private static string Is(int count) => count == 1 ? "is" : "are";

    /// <summary>Renders a name-to-count map as "2 FontFile2 (TrueType), 1 FontFile3 /Type1C".</summary>
    private static string DescribeCounts(Dictionary<string, int> counts)
    {
        var names = new List<string>(counts.Keys);
        names.Sort(StringComparer.Ordinal);

        var parts = new List<string>(names.Count);
        foreach (string name in names)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{counts[name]} {name}"));

        return string.Join(", ", parts);
    }

    /// <summary>One diagnostic code's worth of skipped images.</summary>
    private sealed class ImageGroup
    {
        private readonly Dictionary<string, int> _variants = new(StringComparer.Ordinal);
        private readonly List<string> _reasons = [];
        private readonly PageSet _pages = new();
        private int _total;
        private int _inline;
        private int _undecoded;
        private int _unnamedVariants;

        public void Add(in PdfImageShape shape, int? page, string? reason, bool undecoded = false)
        {
            _total++;
            if (shape.IsInline)
                _inline++;
            if (undecoded)
                _undecoded++;
            _pages.Add(page);

            // A reason exists only where a decoder was composed and declined. It
            // is the part the dictionary cannot supply — which tuple it met, or
            // which pending row the image ran into — so distinct reasons are kept
            // rather than counted.
            if (reason is not null && _reasons.Count < MaxDistinctVariants && !_reasons.Contains(reason))
                _reasons.Add(reason);

            string variant = shape.Describe();
            if (_variants.TryGetValue(variant, out int seen))
            {
                _variants[variant] = seen + 1;
                return;
            }

            if (_variants.Count >= MaxDistinctVariants)
            {
                _unnamedVariants++;
                return;
            }

            _variants[variant] = 1;
        }

        /// <summary>
        /// Describes the group under the code it was filed against.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The code decides the reason, because the codes mean different things.
        /// The generic code is what an image reports when a decoder was never the
        /// obstacle: an inline image, which is read from its declaration by
        /// design, or a stream this build could decode and has nowhere to put.
        /// Telling that second group it wanted a decoder named a gap the build
        /// did not have and hid the one it did.
        /// </para>
        /// <para>
        /// A tuple code — <c>DCTDecode</c> and its relatives — does not decide it
        /// alone, and used to. The code names the filter an image would need
        /// decoded; it does not say whether this build composes one, because
        /// since a decoder became composable an image can carry a tuple code and
        /// still have met a composed decoder. Reading "composes no image decoder"
        /// off the code alone therefore told a host to go and compose a decoder
        /// it already had, which is why the count of images the read declined to
        /// decode picks that sentence now instead.
        /// </para>
        /// </remarks>
        public string Describe(string code)
        {
            var text = new StringBuilder();
            text.Append(_reasons.Count > 0
                ? "The page draws a raster image that the composed image decoder would not decode. "
                : code == PdfDiagnosticCodes.ImageNotComposed
                    ? "The page draws a raster image. The logical model carries no images, so the image was detected and skipped. "
                    : _undecoded == _total
                        ? "The page draws a raster image. A decoder for its filter is composed; this read's allowance for describing images did not stretch to decoding it, so it was reported from its dictionary. "
                        : "The page draws a raster image. This build composes no image decoder, so the image was detected and skipped. ");
            text.Append(CultureInfo.InvariantCulture, $"{_total} image{S(_total)}");
            if (_inline == _total)
                text.Append(", all inline,");
            else if (_inline > 0)
                text.Append(CultureInfo.InvariantCulture, $", {_inline} of them inline,");

            text.Append(CultureInfo.InvariantCulture, $" {Were(_total)} skipped.");
            _pages.Append(text);

            if (_variants.Count > 0)
            {
                text.Append(_total == 1 ? " Its dictionary declares: " : " Their dictionaries declare: ");
                var names = new List<string>(_variants.Keys);
                names.Sort(StringComparer.Ordinal);

                var parts = new List<string>(names.Count);
                foreach (string name in names)
                {
                    int count = _variants[name];
                    parts.Add(count == 1
                        ? name
                        : string.Create(CultureInfo.InvariantCulture, $"{name} (x{count})"));
                }

                text.Append(string.Join("; ", parts));
                if (_unnamedVariants > 0)
                    text.Append(CultureInfo.InvariantCulture, $"; and {_unnamedVariants} further variant{S(_unnamedVariants)}");
                text.Append('.');
            }

            foreach (string reason in _reasons)
                text.Append(' ').Append(reason);

            return text.ToString();
        }
    }

    /// <summary>
    /// The images a composed filter decoded, which the logical model then had
    /// nowhere to put.
    /// </summary>
    /// <remarks>
    /// Reported as a skip because that is what it is: the pixels existed, they
    /// were correct, and the result does not carry them. What makes the note
    /// worth reading is the comparison — a dictionary that declared a size the
    /// samples do not match is a document disagreeing with itself, and only a
    /// build that actually decoded can notice.
    /// </remarks>
    private sealed class DecodedImageGroup
    {
        private readonly Dictionary<string, int> _variants = new(StringComparer.Ordinal);
        private readonly PageSet _pages = new();
        private int _unnamedVariants;
        private int _agreed;
        private int _disagreed;
        private long _sampleBytes;

        public int Total { get; private set; }

        public void Add(in PdfImageShape declared, long sampleBytes, int? page, bool codec)
        {
            Total++;
            _pages.Add(page);
            _sampleBytes += sampleBytes;

            if (ExpectedSampleBytes(declared, codec) is long expected)
            {
                if (expected == sampleBytes)
                    _agreed++;
                else
                    _disagreed++;
            }

            string variant = declared.Describe();
            if (_variants.TryGetValue(variant, out int seen))
            {
                _variants[variant] = seen + 1;
                return;
            }

            if (_variants.Count >= MaxDistinctVariants)
            {
                _unnamedVariants++;
                return;
            }

            _variants[variant] = 1;
        }

        /// <summary>
        /// The sample bytes a decode of this image should have produced, or null
        /// where the declaration does not fix one and a mismatch would be an
        /// arithmetic artefact rather than a disagreement.
        /// </summary>
        /// <remarks>
        /// A composed image codec normalizes to 8-bit RGBA, so a pixel count
        /// fixes a byte count. A byte-stream chain — Flate, LZW, or no filter at
        /// all — yields the image's own samples instead, packed at the declared
        /// depth and padded to a byte boundary at the end of each row. The two
        /// are different arithmetic, and holding raw samples to the codec's
        /// would report every stencil mask as disagreeing with its own
        /// dictionary.
        /// </remarks>
        private static long? ExpectedSampleBytes(in PdfImageShape declared, bool codec)
        {
            if (declared.Width <= 0 || declared.Height <= 0)
                return null;

            if (codec)
                return (long)declared.Width * declared.Height * 4;

            // Only the device spaces fix a component count from the family name.
            // Indexed, ICCBased, Separation and the rest carry theirs elsewhere,
            // and guessing one would invent a disagreement.
            int components = declared.ColorSpace switch
            {
                "ImageMask" or "DeviceGray" => 1,
                "DeviceRGB" => 3,
                "DeviceCMYK" => 4,
                _ => 0,
            };

            // A stencil mask is one bit per sample by definition, whether or not
            // the dictionary bothered to say so.
            int bits = declared.ColorSpace == "ImageMask" ? 1 : declared.BitsPerComponent;
            if (components == 0 || bits <= 0)
                return null;

            long rowBytes = ((long)declared.Width * components * bits + 7) / 8;
            return rowBytes * declared.Height;
        }

        public string Describe()
        {
            var text = new StringBuilder("The page draws raster images this build can decode. ");
            text.Append(CultureInfo.InvariantCulture,
                $"{Total} image{S(Total)} {Were(Total)} decoded to {_sampleBytes} bytes of samples.");
            _pages.Append(text);

            if (_variants.Count > 0)
            {
                text.Append(Total == 1 ? " Its dictionary declares: " : " Their dictionaries declare: ");
                var names = new List<string>(_variants.Keys);
                names.Sort(StringComparer.Ordinal);

                var parts = new List<string>(names.Count);
                foreach (string name in names)
                {
                    int count = _variants[name];
                    parts.Add(count == 1
                        ? name
                        : string.Create(CultureInfo.InvariantCulture, $"{name} (x{count})"));
                }

                text.Append(string.Join("; ", parts));
                if (_unnamedVariants > 0)
                    text.Append(CultureInfo.InvariantCulture, $"; and {_unnamedVariants} further variant{S(_unnamedVariants)}");
                text.Append('.');
            }

            if (_disagreed > 0)
            {
                text.Append(CultureInfo.InvariantCulture,
                    $" {_disagreed} of them declared a pixel size the decoded samples do not match, so the dictionary and the image data disagree");
                text.Append(_agreed > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"; the other {_agreed} agree.")
                    : ".");
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// A bounded set of page numbers, rendered as a sentence. Bounded because a
    /// construct that appears on every page of a large file has to describe
    /// itself in constant space.
    /// </summary>
    private sealed class PageSet
    {
        private readonly SortedSet<int> _pages = [];
        private bool _more;

        public void Add(int? page)
        {
            if (page is not int number || _pages.Contains(number))
                return;

            if (_pages.Count >= MaxNamedPages)
            {
                _more = true;
                return;
            }

            _pages.Add(number);
        }

        public void Append(StringBuilder text)
        {
            if (_pages.Count == 0)
                return;

            text.Append(_pages.Count == 1 ? " On page " : " On pages ");
            text.Append(string.Join(", ", _pages));
            if (_more)
                text.Append(" and others");
            text.Append('.');
        }
    }
}
