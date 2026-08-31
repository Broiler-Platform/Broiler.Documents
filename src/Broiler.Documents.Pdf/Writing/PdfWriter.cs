using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Documents.Pdf.Text;
using Broiler.Graphics;

namespace Broiler.Documents.Pdf.Writing;

/// <summary>
/// Serializes a laid-out document as a new PDF 1.7 file.
/// </summary>
/// <remarks>
/// <para>
/// The writer emits new files only. It never rewrites an input, never saves
/// incrementally, and never carries a source document's objects, fonts, images,
/// identifiers, or raw metadata forward — every byte it writes is generated from
/// the model and the caller's options.
/// </para>
/// <para>
/// Output is deterministic: the same document and options produce identical
/// bytes. Nothing reads the clock, the machine name, the locale, or the installed
/// fonts, and the file identifier is derived from the content when the caller
/// supplies none.
/// </para>
/// <para>
/// Text uses the fourteen standard font names with WinAnsi encoding and no
/// embedded font program, which is what lets the base build ship with no font
/// asset and no embedding-rights question of its own. Embedded and subset fonts,
/// Unicode composite fonts, and raster images are the writer's declared extension
/// points, not omissions to be worked around.
/// </para>
/// </remarks>
internal sealed class PdfWriter
{
    private const string BodyEncodingNote = "%âãÏÓ";

    private readonly PdfWriteOptions _options;
    private readonly PdfCodecServices _services;
    private readonly PdfDiagnosticSink _diagnostics;
    private readonly List<long> _offsets = [];
    private readonly MemoryStream _buffer = new();
    private readonly CancellationToken _cancellationToken;

    /// <summary>The page the layout used, which the MediaBox has to agree with.</summary>
    private PdfPageSetup _pageSetup = PdfPageSetup.Letter;

    private PdfWriter(
        PdfWriteOptions options,
        PdfCodecServices services,
        PdfDiagnosticSink diagnostics,
        CancellationToken cancellationToken)
    {
        _options = options;
        _services = services;
        _diagnostics = diagnostics;
        _cancellationToken = cancellationToken;
    }

    public static PdfWriteResult Write(
        RichTextDocument document,
        Stream destination,
        PdfWriteOptions options,
        PdfCodecServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);

        var diagnostics = new PdfDiagnosticSink(options.PdfLimits.MaxDiagnostics);
        var writer = new PdfWriter(options, services, diagnostics, cancellationToken);

        byte[] bytes;
        int pageCount;
        try
        {
            // Everything that can reject the document happens before a byte reaches
            // the destination: layout, policy, encoding, and the output budget.
            (bytes, pageCount) = writer.Build(document);
        }
        catch (PdfLimitExceededException e)
        {
            diagnostics.Error(PdfDiagnosticCodes.Limit, e.Message);
            return new PdfWriteResult(0, DocumentResultStatus.Rejected, DocumentDestinationState.NotStarted, 0, diagnostics.Build());
        }
        catch (OperationCanceledException)
        {
            diagnostics.Error(PdfDiagnosticCodes.Cancelled, "The write was cancelled before any byte reached the destination.");
            return new PdfWriteResult(0, DocumentResultStatus.Rejected, DocumentDestinationState.NotStarted, 0, diagnostics.Build());
        }

        long written = 0;
        try
        {
            destination.Write(bytes, 0, bytes.Length);
            written = bytes.Length;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException)
        {
            diagnostics.Error(
                PdfDiagnosticCodes.WritePartialDestination,
                "The destination stream failed part-way through the write. The bytes already written are not a usable PDF and must be discarded.");
            return new PdfWriteResult(written, DocumentResultStatus.Rejected, DocumentDestinationState.PartialDestination, pageCount, diagnostics.Build());
        }

        DocumentResultStatus status = diagnostics.HasSkips || diagnostics.HasErrors
            ? DocumentResultStatus.Partial
            : DocumentResultStatus.Success;

        return new PdfWriteResult(written, status, DocumentDestinationState.Committed, pageCount, diagnostics.Build());
    }

    private (byte[] Bytes, int PageCount) Build(RichTextDocument document)
    {
        PdfUriPolicy policy = _options.UriPolicy ?? _services.UriPolicy;
        IPdfFontMetricsProvider metrics = _services.FontMetrics;

        if (metrics.IsApproximate)
        {
            _diagnostics.Info(
                PdfDiagnosticCodes.WriteMetricsApproximate,
                "Line breaking used the built-in approximate metric model. Pagination is deterministic and reproducible, but glyph advances in a viewer will differ slightly from the measured ones.");
        }

        var layout = new PdfPageLayout(_options, metrics, policy, _diagnostics, _cancellationToken);
        List<PdfLayoutPage> pages = layout.Build(document);
        // The MediaBox has to be the page the layout actually used, which is the
        // document's when it states one.
        _pageSetup = layout.SetupFor(document);

        if (pages.Count > _options.PdfLimits.MaxPageCount)
            throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxPageCount), _options.PdfLimits.MaxPageCount);

        var fonts = new FontTable();
        foreach (PdfLayoutPage page in pages)
        {
            foreach (PdfPlacedRun run in page.Runs)
                fonts.Use(run.Font);
        }

        // Object numbering: 1 catalog, 2 page tree, then three per page (page,
        // content, annotations are inline), then the fonts, then Info.
        int nextObject = 3;
        var pageObjects = new List<PageObjects>();
        foreach (PdfLayoutPage page in pages)
        {
            var links = new List<PdfPlacedRun>();
            foreach (PdfPlacedRun run in page.Runs)
            {
                if (run.LinkHref is not null)
                    links.Add(run);
            }

            var objects = new PageObjects(nextObject++, nextObject++, links);
            foreach (PdfPlacedRun _ in links)
                nextObject++;
            objects.FirstAnnotationObject = objects.ContentObject + 1;
            pageObjects.Add(objects);
        }

        var fontObjects = new Dictionary<PdfStandardFont, int>();
        foreach (PdfStandardFont font in fonts.Used)
            fontObjects[font] = nextObject++;

        bool hasInfo = !_options.Metadata.IsEmpty;
        int infoObject = hasInfo ? nextObject++ : 0;
        int objectCount = nextObject;

        _offsets.Clear();
        for (int i = 0; i < objectCount; i++)
            _offsets.Add(0);

        WriteAscii("%PDF-1.7\n");
        // A comment of high bytes tells transfer tools the file is binary.
        WriteAscii(BodyEncodingNote);
        WriteAscii("\n");

        WriteCatalog();
        WritePageTree(pageObjects);

        for (int i = 0; i < pages.Count; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            WritePage(pages[i], pageObjects[i], fonts, fontObjects, policy);
        }

        foreach (KeyValuePair<PdfStandardFont, int> entry in fontObjects)
            WriteFont(entry.Value, entry.Key);

        if (hasInfo)
            WriteInfo(infoObject);

        long xrefOffset = _buffer.Length;
        WriteXref(objectCount);
        WriteTrailer(objectCount, infoObject, xrefOffset);

        if (_buffer.Length > _options.PdfLimits.MaxOutputBytes)
            throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxOutputBytes), _options.PdfLimits.MaxOutputBytes);

        return (_buffer.ToArray(), pages.Count);
    }

    // ---- document objects -----------------------------------------------------

    private void WriteCatalog()
    {
        BeginObject(1);
        var builder = new StringBuilder("<< /Type /Catalog /Pages 2 0 R");
        if (!string.IsNullOrEmpty(_options.Metadata.Language))
            builder.Append(" /Lang ").Append(LiteralString(_options.Metadata.Language!));
        builder.Append(" >>\n");
        WriteAscii(builder.ToString());
        EndObject();
    }

    private void WritePageTree(List<PageObjects> pages)
    {
        BeginObject(2);
        var builder = new StringBuilder("<< /Type /Pages /Kids [");
        foreach (PageObjects page in pages)
            builder.Append(' ').Append(page.PageObject).Append(" 0 R");
        builder.Append(" ] /Count ").Append(pages.Count).Append(" >>\n");
        WriteAscii(builder.ToString());
        EndObject();
    }

    private void WritePage(
        PdfLayoutPage page,
        PageObjects objects,
        FontTable fonts,
        Dictionary<PdfStandardFont, int> fontObjects,
        PdfUriPolicy policy)
    {
        PdfPageSetup setup = _pageSetup;

        BeginObject(objects.PageObject);
        var builder = new StringBuilder();
        builder.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ")
            .Append(Number(setup.Width)).Append(' ').Append(Number(setup.Height))
            .Append("] /Resources << /Font <<");

        var used = new HashSet<PdfStandardFont>();
        foreach (PdfPlacedRun run in page.Runs)
            used.Add(run.Font);

        foreach (PdfStandardFont font in fonts.Used)
        {
            if (!used.Contains(font))
                continue;
            builder.Append(' ').Append('/').Append(fonts.NameOf(font)).Append(' ')
                .Append(fontObjects[font]).Append(" 0 R");
        }

        builder.Append(" >> >> /Contents ").Append(objects.ContentObject).Append(" 0 R");

        if (objects.Links.Count > 0)
        {
            builder.Append(" /Annots [");
            for (int i = 0; i < objects.Links.Count; i++)
                builder.Append(' ').Append(objects.FirstAnnotationObject + i).Append(" 0 R");
            builder.Append(" ]");
        }

        builder.Append(" >>\n");
        WriteAscii(builder.ToString());
        EndObject();

        WriteContent(objects.ContentObject, page, fonts);

        for (int i = 0; i < objects.Links.Count; i++)
            WriteLinkAnnotation(objects.FirstAnnotationObject + i, objects.Links[i], policy);
    }

    private void WriteContent(int objectNumber, PdfLayoutPage page, FontTable fonts)
    {
        byte[] content = BuildContentStream(page, fonts);
        byte[] payload = content;
        bool compressed = false;

        if (_options.CompressStreams && content.Length > 0)
        {
            payload = Deflate(content);
            compressed = true;
        }

        BeginObject(objectNumber);
        var header = new StringBuilder("<< /Length ").Append(payload.Length);
        if (compressed)
            header.Append(" /Filter /FlateDecode");
        header.Append(" >>\nstream\n");
        WriteAscii(header.ToString());
        _buffer.Write(payload, 0, payload.Length);
        WriteAscii("\nendstream\n");
        EndObject();
    }

    private byte[] BuildContentStream(PdfLayoutPage page, FontTable fonts)
    {
        var content = new MemoryStream();

        // Shapes before run backgrounds, and both before text: a letterhead's
        // stripe is the bottom layer of the page.
        foreach (PdfPlacedShape shape in page.Shapes)
            AppendShape(content, shape);

        // Backgrounds next so text and decorations paint over them.
        foreach (PdfPlacedRun run in page.Runs)
        {
            if (run.Background.IsEmpty || run.Background.A == 0)
                continue;

            double descent = _services.FontMetrics.GetDescent(run.Font) / 1000d * run.FontSize;
            double ascent = _services.FontMetrics.GetAscent(run.Font) / 1000d * run.FontSize;
            AppendAscii(content, FillColor(run.Background));
            AppendAscii(content, Rectangle(run.X, run.Baseline - descent, run.Width, ascent + descent));
        }

        if (page.Runs.Count > 0)
        {
            AppendAscii(content, "BT\n");

            PdfStandardFont? currentFont = null;
            double currentSize = double.NaN;
            BColor currentColor = BColor.Empty;
            double currentWordSpacing = 0;

            foreach (PdfPlacedRun run in page.Runs)
            {
                if (currentFont != run.Font || !currentSize.Equals(run.FontSize))
                {
                    AppendAscii(content, $"/{fonts.NameOf(run.Font)} {Number(run.FontSize)} Tf\n");
                    currentFont = run.Font;
                    currentSize = run.FontSize;
                }

                if (currentColor != run.Color)
                {
                    AppendAscii(content, FillColor(run.Color));
                    currentColor = run.Color;
                }

                // Tw is graphics state, so it persists until it is set again. A
                // justified line sets it; the next unjustified run has to put it
                // back, or it would inherit a stretch that is not its own.
                if (!currentWordSpacing.Equals(run.WordSpacing))
                {
                    AppendAscii(content, $"{Number(run.WordSpacing)} Tw\n");
                    currentWordSpacing = run.WordSpacing;
                }

                AppendAscii(content, $"1 0 0 1 {Number(run.X)} {Number(run.Baseline)} Tm\n");
                AppendBytes(content, LiteralStringBytes(run.Text));
                AppendAscii(content, " Tj\n");
            }

            AppendAscii(content, "ET\n");
        }

        // Decorations are rectangles rather than font features, which is what the
        // standard fonts can express without an embedded program.
        foreach (PdfPlacedRun run in page.Runs)
        {
            if (!run.Underline && !run.Strikethrough)
                continue;

            AppendAscii(content, FillColor(run.Color));
            double thickness = Math.Max(0.4, run.FontSize * 0.055);

            if (run.Underline)
                AppendAscii(content, Rectangle(run.X, run.Baseline - (run.FontSize * 0.12), run.Width, thickness));
            if (run.Strikethrough)
                AppendAscii(content, Rectangle(run.X, run.Baseline + (run.FontSize * 0.26), run.Width, thickness));
        }

        return content.ToArray();
    }

    private void WriteLinkAnnotation(int objectNumber, PdfPlacedRun run, PdfUriPolicy policy)
    {
        // Revalidated here, immediately before emission. Admission during layout is
        // not authorization to write: the policy in force at this point decides.
        if (!policy.TryAdmit(run.LinkHref, out string canonical, out string? reason))
        {
            _diagnostics.Skipped(
                PdfDiagnosticCodes.UriRejected,
                $"A link annotation was not emitted because {reason ?? "the target failed revalidation"}.");
            BeginObject(objectNumber);
            WriteAscii("<< /Type /Annot /Subtype /Square /Rect [0 0 0 0] /F 2 >>\n");
            EndObject();
            return;
        }

        double descent = _services.FontMetrics.GetDescent(run.Font) / 1000d * run.FontSize;
        double ascent = _services.FontMetrics.GetAscent(run.Font) / 1000d * run.FontSize;

        BeginObject(objectNumber);
        var builder = new StringBuilder("<< /Type /Annot /Subtype /Link /Rect [")
            .Append(Number(run.X)).Append(' ')
            .Append(Number(run.Baseline - descent)).Append(' ')
            .Append(Number(run.X + run.Width)).Append(' ')
            .Append(Number(run.Baseline + ascent))
            .Append("] /Border [0 0 0] /A << /S /URI /URI ")
            .Append(LiteralString(canonical))
            .Append(" >> >>\n");
        WriteAscii(builder.ToString());
        EndObject();
    }

    private void WriteFont(int objectNumber, PdfStandardFont font)
    {
        BeginObject(objectNumber);
        var builder = new StringBuilder("<< /Type /Font /Subtype /Type1 /BaseFont /")
            .Append(PdfStandardFonts.NameOf(font));

        // Symbol and ZapfDingbats have built-in encodings; naming WinAnsi for them
        // would misdeclare what their codes mean.
        if (font is not (PdfStandardFont.Symbol or PdfStandardFont.ZapfDingbats))
            builder.Append(" /Encoding /WinAnsiEncoding");

        builder.Append(" >>\n");
        WriteAscii(builder.ToString());
        EndObject();
    }

    private void WriteInfo(int objectNumber)
    {
        PdfDocumentMetadata metadata = _options.Metadata;
        BeginObject(objectNumber);

        var builder = new StringBuilder("<<");
        AppendInfoEntry(builder, "Title", metadata.Title);
        AppendInfoEntry(builder, "Author", Join(metadata.Authors));
        AppendInfoEntry(builder, "Subject", metadata.Subject);
        AppendInfoEntry(builder, "Keywords", Join(metadata.Keywords));
        AppendInfoEntry(builder, "Creator", metadata.CreatorApplication);
        AppendInfoEntry(builder, "Producer", metadata.Producer);

        if (metadata.CreationDate is { } created)
            builder.Append(" /CreationDate ").Append(LiteralString(FormatDate(created)));
        if (metadata.ModificationDate is { } modified)
            builder.Append(" /ModDate ").Append(LiteralString(FormatDate(modified)));

        builder.Append(" >>\n");
        WriteAscii(builder.ToString());
        EndObject();
    }

    private static string? Join(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return null;
        return string.Join("; ", values);
    }

    private void AppendInfoEntry(StringBuilder builder, string key, string? value)
    {
        if (value is null)
            return;
        builder.Append(" /").Append(key).Append(' ').Append(LiteralString(value));
    }

    /// <summary>
    /// Formats a PDF date. A value that arrived without a UTC offset is written
    /// back without one: the writer does not invent a zone it was never told.
    /// </summary>
    internal static string FormatDate(PdfDate date)
    {
        DateTimeOffset value = date.Value;
        var builder = new StringBuilder("D:")
            .Append(value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));

        if (!date.HasUtcOffset)
            return builder.ToString();

        TimeSpan offset = value.Offset;
        if (offset == TimeSpan.Zero)
            return builder.Append('Z').ToString();

        char sign = offset < TimeSpan.Zero ? '-' : '+';
        TimeSpan magnitude = offset.Duration();
        return builder
            .Append(sign)
            .Append(magnitude.Hours.ToString("D2", CultureInfo.InvariantCulture))
            .Append('\'')
            .Append(magnitude.Minutes.ToString("D2", CultureInfo.InvariantCulture))
            .Append('\'')
            .ToString();
    }

    // ---- cross-reference and trailer ------------------------------------------

    private void WriteXref(int objectCount)
    {
        var builder = new StringBuilder("xref\n0 ").Append(objectCount).Append('\n');
        // Object zero heads the free list, with the generation the format fixes.
        builder.Append("0000000000 65535 f \n");
        for (int i = 1; i < objectCount; i++)
        {
            builder.Append(_offsets[i].ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        WriteAscii(builder.ToString());
    }

    private void WriteTrailer(int objectCount, int infoObject, long xrefOffset)
    {
        string identifier = FileIdentifier();
        var builder = new StringBuilder("trailer\n<< /Size ").Append(objectCount).Append(" /Root 1 0 R");
        if (infoObject > 0)
            builder.Append(" /Info ").Append(infoObject).Append(" 0 R");
        builder.Append(" /ID [<").Append(identifier).Append("> <").Append(identifier).Append(">] >>\n")
            .Append("startxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        WriteAscii(builder.ToString());
    }

    /// <summary>
    /// Produces the file identifier. A caller-supplied value wins; otherwise it is
    /// a digest of the bytes written so far, which keeps output deterministic
    /// without consulting a clock or a machine identity.
    /// </summary>
    private string FileIdentifier()
    {
        if (!string.IsNullOrWhiteSpace(_options.FileIdentifier))
        {
            var normalized = new StringBuilder(32);
            foreach (char c in _options.FileIdentifier!)
            {
                if (Uri.IsHexDigit(c))
                    normalized.Append(char.ToUpperInvariant(c));
                if (normalized.Length == 32)
                    break;
            }

            while (normalized.Length < 32)
                normalized.Append('0');
            return normalized.ToString();
        }

        byte[] digest = SHA256.HashData(_buffer.ToArray());
        var hex = new StringBuilder(32);
        for (int i = 0; i < 16; i++)
            hex.Append(digest[i].ToString("X2", CultureInfo.InvariantCulture));
        return hex.ToString();
    }

    // ---- low-level emission ---------------------------------------------------

    private void BeginObject(int objectNumber)
    {
        _offsets[objectNumber] = _buffer.Length;
        WriteAscii($"{objectNumber} 0 obj\n");
    }

    private void EndObject() => WriteAscii("endobj\n");

    private void WriteAscii(string text) => AppendAscii(_buffer, text);

    private static void AppendAscii(MemoryStream stream, string text)
    {
        foreach (char c in text)
            stream.WriteByte((byte)c);
    }

    private static void AppendBytes(MemoryStream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    /// <summary>
    /// Paints one shape's box.
    /// </summary>
    /// <remarks>
    /// A gradient is emitted as bands of solid colour rather than as a shading
    /// pattern. A pattern would be the smaller file, but it needs a pattern
    /// dictionary in the page resources and a second object per shape; bands need
    /// nothing the writer does not already emit, and at about a point each the
    /// seams fall below what a reader resolves. The angle is snapped to the axis
    /// it runs closer to, because a band is a rectangle.
    /// </remarks>
    private void AppendShape(MemoryStream content, PdfPlacedShape shape)
    {
        if (shape.Width <= 0 || shape.Height <= 0)
            return;

        if (shape.Fill is ShapeFill fill)
        {
            if (!fill.IsGradient)
            {
                AppendAscii(content, FillColor(fill.Start));
                AppendAscii(content, Rectangle(shape.X, shape.Y, shape.Width, shape.Height));
            }
            else
            {
                double radians = fill.AngleDegrees * Math.PI / 180.0;
                bool vertical = Math.Abs(Math.Sin(radians)) >= Math.Abs(Math.Cos(radians));
                double extent = vertical ? shape.Height : shape.Width;
                int bands = (int)Math.Clamp(Math.Round(extent), 2, 512);

                for (int i = 0; i < bands; i++)
                {
                    double t = bands == 1 ? 0 : (double)i / (bands - 1);
                    AppendAscii(content, FillColor(Mix(fill.Start, fill.End, t)));

                    double offset = extent * i / bands;
                    double size = (extent / bands) + 0.5;
                    AppendAscii(content, vertical
                        // PDF y grows upward, so the first stop is the top band.
                        ? Rectangle(shape.X, shape.Y + extent - offset - size, shape.Width, size)
                        : Rectangle(shape.X + offset, shape.Y, size, shape.Height));
                }
            }
        }

        if (shape.Outline.IsEmpty || shape.Outline.A == 0)
            return;

        AppendAscii(content, StrokeColor(shape.Outline));
        AppendAscii(content, FormattableString.Invariant(
            $"{Number(shape.X)} {Number(shape.Y)} {Number(shape.Width)} {Number(shape.Height)} re S\n"));
    }

    private static BColor Mix(BColor from, BColor to, double t) =>
        new(
            (byte)Math.Round(from.R + ((to.R - from.R) * t)),
            (byte)Math.Round(from.G + ((to.G - from.G) * t)),
            (byte)Math.Round(from.B + ((to.B - from.B) * t)),
            (byte)Math.Round(from.A + ((to.A - from.A) * t)));

    private static string StrokeColor(BColor color) =>
        FormattableString.Invariant(
            $"{Number(color.R / 255d)} {Number(color.G / 255d)} {Number(color.B / 255d)} RG\n");

    private static string FillColor(BColor color) =>
        $"{Number(color.Rf)} {Number(color.Gf)} {Number(color.Bf)} rg\n";

    private static string Rectangle(double x, double y, double width, double height) =>
        $"{Number(x)} {Number(y)} {Number(width)} {Number(height)} re f\n";

    /// <summary>
    /// Formats a number for the file. Four decimals is finer than a typographic
    /// point can show, and rounding here rather than at use keeps the output
    /// byte-identical across runs.
    /// </summary>
    internal static string Number(double value)
    {
        if (!double.IsFinite(value))
            return "0";

        double rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            return "0";

        string text = rounded.ToString("0.####", CultureInfo.InvariantCulture);
        return text.Length == 0 ? "0" : text;
    }

    private static string LiteralString(string text)
    {
        var builder = new StringBuilder("(");
        foreach (byte b in EncodeTextString(text))
        {
            switch (b)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    builder.Append('\\').Append((char)b);
                    break;
                case 10:
                    builder.Append("\\n");
                    break;
                case 13:
                    builder.Append("\\r");
                    break;
                case 9:
                    builder.Append("\\t");
                    break;
                default:
                    if (b < 32 || b > 126)
                        builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    else
                        builder.Append((char)b);
                    break;
            }
        }

        return builder.Append(')').ToString();
    }

    private static byte[] LiteralStringBytes(string text)
    {
        byte[] encoded = PdfWinAnsiEncoder.Encode(text);
        var builder = new List<byte> { (byte)'(' };

        foreach (byte b in encoded)
        {
            switch (b)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    builder.Add((byte)'\\');
                    builder.Add(b);
                    break;
                case 10:
                case 13:
                    // A literal newline inside a string would end the line the
                    // content stream is on; escape it rather than emitting it raw.
                    builder.Add((byte)'\\');
                    builder.Add(b == 10 ? (byte)'n' : (byte)'r');
                    break;
                default:
                    builder.Add(b);
                    break;
            }
        }

        builder.Add((byte)')');
        return builder.ToArray();
    }

    /// <summary>
    /// Encodes a metadata text string. Pure Latin-1 text is written as bytes;
    /// anything else takes the UTF-16BE form the format defines, so a title with
    /// non-Latin characters survives even though page text does not yet.
    /// </summary>
    private static byte[] EncodeTextString(string text)
    {
        bool latin = true;
        foreach (char c in text)
        {
            if (c > 0xFF)
            {
                latin = false;
                break;
            }
        }

        if (latin)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
                bytes[i] = (byte)text[i];
            return bytes;
        }

        var utf16 = new byte[(text.Length * 2) + 2];
        utf16[0] = 0xFE;
        utf16[1] = 0xFF;
        for (int i = 0; i < text.Length; i++)
        {
            utf16[2 + (i * 2)] = (byte)(text[i] >> 8);
            utf16[3 + (i * 2)] = (byte)text[i];
        }

        return utf16;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new System.IO.Compression.ZLibStream(
                   output,
                   System.IO.Compression.CompressionLevel.Optimal,
                   leaveOpen: true))
        {
            compressor.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <summary>The fonts a document uses, and the resource name each gets.</summary>
    private sealed class FontTable
    {
        private readonly List<PdfStandardFont> _used = [];

        public IReadOnlyList<PdfStandardFont> Used => _used;

        public void Use(PdfStandardFont font)
        {
            if (!_used.Contains(font))
                _used.Add(font);
        }

        /// <summary>
        /// The resource name, assigned in first-use order so the same document
        /// always produces the same names.
        /// </summary>
        public string NameOf(PdfStandardFont font)
        {
            int index = _used.IndexOf(font);
            return "F" + (index < 0 ? 0 : index + 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    private sealed class PageObjects
    {
        public PageObjects(int pageObject, int contentObject, List<PdfPlacedRun> links)
        {
            PageObject = pageObject;
            ContentObject = contentObject;
            Links = links;
        }

        public int PageObject { get; }

        public int ContentObject { get; }

        public int FirstAnnotationObject { get; set; }

        public List<PdfPlacedRun> Links { get; }
    }
}
