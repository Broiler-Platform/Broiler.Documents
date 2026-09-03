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
    private readonly Dictionary<string, ImageGroup> _images = new(StringComparer.Ordinal);
    private readonly DecodedImageGroup _decodedImages = new();
    private readonly SortedSet<int> _notProjectedPages = [];
    private readonly SortedSet<string> _notProjectedReasons = new(StringComparer.Ordinal);
    private readonly SortedSet<int> _deniedPages = [];
    private int _notProjected;
    private int _denied;
    private string? _deniedReason;
    private readonly List<PdfFontProgram> _fontPrograms = [];
    private int _fontProgramOverflow;

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
    public void NoteImage(string code, in PdfImageShape shape, int? page, string? reason = null)
    {
        if (!_images.TryGetValue(code, out ImageGroup? group))
        {
            group = new ImageGroup();
            _images[code] = group;
        }

        group.Add(shape, page, reason);
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

        ReportArtwork(diagnostics);
        ReportImages(diagnostics);
        ReportDecodedImages(diagnostics);
        ReportFontPrograms(diagnostics);
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

        var text = new StringBuilder();
        text.Append("The page draws vector artwork, which a logical rich-text document cannot represent. It was dropped. ");
        text.Append(CultureInfo.InvariantCulture, $"{total} path-painting operation{S(total)} {Were(total)} dropped: ");

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
                case PdfFontProgramInspection.NotComposed:
                    notComposed++;
                    break;
            }
        }

        int total = _fontPrograms.Count + _fontProgramOverflow;
        var text = new StringBuilder();

        // Whether a reader is composed is a fact about the build, so it is the
        // same for every program a read classified. Saying "this build does not
        // inspect" where one is composed named a gap the build did not have, and
        // hid the real reason the program went unread.
        text.Append(inspected > 0
            ? "A font embeds a program a composed reader inspected for the text its glyphs stand for. "
            : notComposed == _fontPrograms.Count
                ? "A font embeds a program this build does not inspect; text was mapped from ToUnicode and the declared encoding only. "
                : "A font embeds a program the composed reader did not read; text was mapped from ToUnicode and the declared encoding only. ");
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
        private int _unnamedVariants;

        public void Add(in PdfImageShape shape, int? page, string? reason)
        {
            _total++;
            if (shape.IsInline)
                _inline++;
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
        /// The code decides the reason, because the codes mean different things.
        /// A tuple code — <c>DCTDecode</c> and its relatives — was filed because
        /// this build composes no decoder for that filter, which is the sentence
        /// it has always carried. The generic code is what an image reports when
        /// a decoder was never the obstacle: an inline image, which is read from
        /// its declaration by design, or a stream this build could decode and has
        /// nowhere to put. Telling that second group it wanted a decoder named a
        /// gap the build did not have and hid the one it did.
        /// </remarks>
        public string Describe(string code)
        {
            var text = new StringBuilder();
            text.Append(_reasons.Count > 0
                ? "The page draws a raster image that the composed image decoder would not decode. "
                : code == PdfDiagnosticCodes.ImageNotComposed
                    ? "The page draws a raster image. The logical model carries no images, so the image was detected and skipped. "
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
