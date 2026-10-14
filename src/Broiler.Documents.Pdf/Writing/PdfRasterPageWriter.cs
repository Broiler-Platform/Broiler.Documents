using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Broiler.Graphics.Color;
using Broiler.Graphics.Paging;

namespace Broiler.Documents.Pdf.Writing;

/// <summary>
/// Writes laid-out pages as a PDF of one image per page: each <see cref="BPage"/> rasterized at
/// <see cref="Dpi"/> and placed to fill its own media box.
/// </summary>
/// <remarks>
/// <para>
/// This is a spool format, not an export. A print path that hands a PDF to a spooler - CUPS,
/// a browser's print dialog - needs every page to look exactly as it was laid out, in any
/// font and with every image, and needs nothing else from the file. A raster page is that:
/// it carries no font to embed or license, no text to extract, and no structure, and so has
/// none of the gates the document codec's own writer must pass. A PDF meant to be read,
/// searched or archived belongs to that writer, which this is not a substitute for.
/// </para>
/// <para>
/// Memory stays bounded by a band of rows, whatever the page size: a page is rasterized a
/// band at a time and each band is compressed straight into the output, with the stream's
/// length written afterwards as an indirect object, so neither a page's pixels nor its
/// compressed bytes are ever held whole. The output stream need not be seekable.
/// </para>
/// </remarks>
public sealed class PdfRasterPageWriter : IBPageDocumentWriter
{
    /// <summary>The resolution used when the caller names none: what most office printers print at.</summary>
    public const double DefaultDpi = 300;

    public PdfRasterPageWriter(double dpi = DefaultDpi, int bandHeight = BPageRasterizer.DefaultBandHeight)
    {
        if (!double.IsFinite(dpi) || dpi <= 0 || dpi > BPageRasterizer.MaximumDpi)
            throw new ArgumentOutOfRangeException(nameof(dpi), $"Resolution must be positive and at most {BPageRasterizer.MaximumDpi} dpi.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandHeight);

        Dpi = dpi;
        BandHeight = bandHeight;
    }

    /// <summary>The resolution pages are rasterized at, in dots per inch.</summary>
    public double Dpi { get; }

    /// <summary>Pixel rows rasterized and compressed at a time.</summary>
    public int BandHeight { get; }

    public string MediaType => "application/pdf";

    public string FileExtension => ".pdf";

    public void Write(IEnumerable<BPage> pages, string title, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
            throw new ArgumentException("The output stream must be writable.", nameof(output));

        var file = new PdfFileWriter(output);
        file.WriteRaw(PdfHeader());

        // 1 is the catalog and 2 the page tree, which is written last because its kids are only
        // known once every page has been.
        const int catalog = 1;
        const int pageTree = 2;
        file.NextObject = 3;

        file.BeginObject(catalog);
        file.WriteAscii("<< /Type /Catalog /Pages 2 0 R >>");
        file.EndObject();

        var kids = new List<int>();
        foreach (BPage page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(page, nameof(pages));
            kids.Add(WritePage(file, page, pageTree, cancellationToken));
        }

        if (kids.Count == 0)
            throw new ArgumentException("A PDF needs at least one page.", nameof(pages));

        file.BeginObject(pageTree);
        var tree = new StringBuilder("<< /Type /Pages /Kids [");
        foreach (int kid in kids)
            tree.Append(CultureInfo.InvariantCulture, $"{kid} 0 R ");
        tree.Append(CultureInfo.InvariantCulture, $"] /Count {kids.Count} >>");
        file.WriteAscii(tree.ToString());
        file.EndObject();

        int info = file.Allocate();
        file.BeginObject(info);
        file.WriteAscii($"<< /Title {TextString(title)} /Producer {TextString("Broiler")} >>");
        file.EndObject();

        file.WriteTrailer(catalog, info);
        output.Flush();
    }

    private int WritePage(PdfFileWriter file, BPage page, int pageTree, CancellationToken cancellationToken)
    {
        (int pixelWidth, int pixelHeight) = BPageRasterizer.GetPixelSize(page.Size, Dpi);
        string width = Number(page.Size.Width);
        string height = Number(page.Size.Height);

        int pageObject = file.Allocate();
        int contents = file.Allocate();
        int image = file.Allocate();
        int imageLength = file.Allocate();

        file.BeginObject(pageObject);
        file.WriteAscii($"<< /Type /Page /Parent {pageTree} 0 R /MediaBox [0 0 {width} {height}] " +
            $"/Resources << /XObject << /Im0 {image} 0 R >> /ProcSet [/PDF /ImageC] >> /Contents {contents} 0 R >>");
        file.EndObject();

        byte[] drawing = Encoding.ASCII.GetBytes($"q {width} 0 0 {height} 0 0 cm /Im0 Do Q");
        file.BeginObject(contents);
        file.WriteAscii($"<< /Length {drawing.Length} >>\nstream\n");
        file.WriteRaw(drawing);
        file.WriteAscii("\nendstream");
        file.EndObject();

        file.BeginObject(image);
        file.WriteAscii($"<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} " +
            $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {imageLength} 0 R >>\nstream\n");
        long start = file.Position;
        using (var compressor = new ZLibStream(file.Body, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] row = new byte[pixelWidth * 3];
            BPageRasterizer.RenderBands(page, Dpi, BColor.White, BandHeight, band =>
            {
                ReadOnlySpan<byte> rgba = band.Pixels.Rgba;
                for (int y = 0; y < band.Pixels.Height; y++)
                {
                    ReadOnlySpan<byte> source = rgba.Slice(y * pixelWidth * 4, pixelWidth * 4);
                    for (int x = 0, o = 0; x < source.Length; x += 4, o += 3)
                    {
                        // The page was painted on white, so what is left of alpha is the
                        // antialiasing at the edges; composite it there rather than drop it.
                        byte alpha = source[x + 3];
                        row[o] = OverWhite(source[x], alpha);
                        row[o + 1] = OverWhite(source[x + 1], alpha);
                        row[o + 2] = OverWhite(source[x + 2], alpha);
                    }

                    compressor.Write(row);
                }
            }, cancellationToken: cancellationToken);
        }

        long length = file.Position - start;
        file.WriteAscii("\nendstream");
        file.EndObject();

        file.BeginObject(imageLength);
        file.WriteAscii(length.ToString(CultureInfo.InvariantCulture));
        file.EndObject();

        return pageObject;
    }

    private static byte OverWhite(byte channel, byte alpha) =>
        alpha == 255 ? channel : (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);

    private static byte[] PdfHeader()
    {
        // A comment of four bytes above 127 on the second line tells a transfer that the file
        // is binary, as PDF 1.4 section 3.4.1 recommends.
        byte[] header = [.. "%PDF-1.4\n%"u8, 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n'];
        return header;
    }

    private static string Number(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>A PDF text string: UTF-16BE with its byte order mark, written in hex so no character needs escaping.</summary>
    private static string TextString(string? text)
    {
        var hex = new StringBuilder("<FEFF");
        foreach (char c in text ?? string.Empty)
            hex.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        return hex.Append('>').ToString();
    }

    /// <summary>Objects, their offsets, and the cross-reference table, over a stream that need not seek.</summary>
    private sealed class PdfFileWriter(Stream output)
    {
        private readonly Dictionary<int, long> _offsets = [];

        public CountingStream Body { get; } = new(output);

        public long Position => Body.Count;

        public int NextObject { get; set; } = 1;

        public int Allocate() => NextObject++;

        public void BeginObject(int number)
        {
            _offsets[number] = Position;
            WriteAscii($"{number} 0 obj\n");
        }

        public void EndObject() => WriteAscii("\nendobj\n");

        public void WriteAscii(string text) => WriteRaw(Encoding.ASCII.GetBytes(text));

        public void WriteRaw(ReadOnlySpan<byte> bytes) => Body.Write(bytes);

        public void WriteTrailer(int root, int info)
        {
            int size = NextObject;
            long xref = Position;
            var table = new StringBuilder();
            table.Append(CultureInfo.InvariantCulture, $"xref\n0 {size}\n0000000000 65535 f \n");
            for (int number = 1; number < size; number++)
            {
                // Every allocated number is written; a gap would be a writer bug, and an
                // entry pointing at offset zero would hide it.
                if (!_offsets.TryGetValue(number, out long offset))
                    throw new InvalidOperationException($"PDF object {number} was allocated but never written.");
                table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }

            table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {size} /Root {root} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            WriteAscii(table.ToString());
        }
    }

    /// <summary>A write-only pass-through that counts what went through it.</summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long Count { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => Count;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            Count += buffer.Length;
        }
    }
}
