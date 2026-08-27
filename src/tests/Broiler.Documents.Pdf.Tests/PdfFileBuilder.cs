using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Assembles small PDFs byte by byte for the tests.
/// </summary>
/// <remarks>
/// Every fixture in this suite is generated here rather than committed. That
/// keeps the corpus rule simple to honour — no in-tree sample carries anyone
/// else's fonts, images, metadata, or personal data — and it makes each test
/// state the exact structure it is about instead of hiding it in a binary.
/// </remarks>
internal sealed class PdfFileBuilder
{
    private readonly List<byte[]?> _objects = [null]; // index 0 is the free head
    private string _version = "1.7";
    private string _preamble = string.Empty;

    /// <summary>Sets the version in the <c>%PDF-</c> header.</summary>
    public PdfFileBuilder WithVersion(string version)
    {
        _version = version;
        return this;
    }

    /// <summary>Puts bytes in front of the header, as a file with a preamble has.</summary>
    public PdfFileBuilder WithPreamble(string preamble)
    {
        _preamble = preamble;
        return this;
    }

    /// <summary>Reserves an object number without defining it yet.</summary>
    public int Reserve()
    {
        _objects.Add(null);
        return _objects.Count - 1;
    }

    public int AddObject(string body)
    {
        _objects.Add(Latin1(body));
        return _objects.Count - 1;
    }

    public void SetObject(int number, string body) => _objects[number] = Latin1(body);

    /// <summary>Adds a stream object, filling in <c>/Length</c> from the data.</summary>
    public int AddStream(string dictionaryBody, byte[] data, string? filter = null)
    {
        var header = new StringBuilder("<< ").Append(dictionaryBody);
        if (filter is not null)
            header.Append(" /Filter /").Append(filter);
        header.Append(" /Length ").Append(data.Length).Append(" >>\nstream\n");

        var bytes = new List<byte>();
        bytes.AddRange(Latin1(header.ToString()));
        bytes.AddRange(data);
        bytes.AddRange(Latin1("\nendstream"));
        _objects.Add(bytes.ToArray());
        return _objects.Count - 1;
    }

    public int AddStream(string dictionaryBody, string content, string? filter = null) =>
        AddStream(dictionaryBody, Latin1(content), filter);

    /// <summary>
    /// Emits the file with a classic cross-reference table and a trailer naming
    /// <paramref name="rootObject"/> as the catalog.
    /// </summary>
    public byte[] Build(int rootObject, string? extraTrailerEntries = null)
    {
        var output = new MemoryStream();
        Append(output, _preamble);
        // Cross-reference offsets are measured from the header, not from byte
        // zero, which is what a producer that writes a preamble emits.
        long headerOrigin = output.Length;
        Append(output, $"%PDF-{_version}\n");

        var offsets = new long[_objects.Count];
        for (int i = 1; i < _objects.Count; i++)
        {
            byte[]? body = _objects[i];
            if (body is null)
                continue;

            offsets[i] = output.Length - headerOrigin;
            Append(output, $"{i} 0 obj\n");
            output.Write(body, 0, body.Length);
            Append(output, "\nendobj\n");
        }

        long xref = output.Length - headerOrigin;
        Append(output, $"xref\n0 {_objects.Count}\n0000000000 65535 f \n");
        for (int i = 1; i < _objects.Count; i++)
            Append(output, $"{offsets[i]:D10} 00000 n \n");

        Append(output, $"trailer\n<< /Size {_objects.Count} /Root {rootObject} 0 R");
        if (extraTrailerEntries is not null)
            Append(output, " " + extraTrailerEntries);
        Append(output, $" >>\nstartxref\n{xref}\n%%EOF\n");

        return output.ToArray();
    }

    /// <summary>Emits the file without any cross-reference table, to test recovery.</summary>
    public byte[] BuildWithoutXref()
    {
        var output = new MemoryStream();
        Append(output, $"%PDF-{_version}\n");
        for (int i = 1; i < _objects.Count; i++)
        {
            byte[]? body = _objects[i];
            if (body is null)
                continue;
            Append(output, $"{i} 0 obj\n");
            output.Write(body, 0, body.Length);
            Append(output, "\nendobj\n");
        }

        Append(output, "%%EOF\n");
        return output.ToArray();
    }

    /// <summary>
    /// A one-page document whose single content stream is <paramref name="content"/>,
    /// using one WinAnsi-encoded Helvetica resource named <c>/F1</c>.
    /// </summary>
    public static byte[] SinglePage(string content, string? extraPageEntries = null, string? extraCatalogEntries = null)
    {
        var builder = new PdfFileBuilder();
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int stream = builder.AddStream(string.Empty, content);

        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R{extraCatalogEntries}>>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(
            page,
            $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] " +
            $"/Resources << /Font << /F1 {font} 0 R >> >> /Contents {stream} 0 R{extraPageEntries} >>");

        return builder.Build(catalog);
    }

    /// <summary>Content that shows <paramref name="text"/> once at a fixed position.</summary>
    public static string ShowText(string text, double x = 72, double y = 720, double size = 12) =>
        $"BT /F1 {size} Tf 1 0 0 1 {x} {y} Tm ({Escape(text)}) Tj ET\n";

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    internal static byte[] Latin1(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = (byte)text[i];
        return bytes;
    }

    private static void Append(MemoryStream stream, string text)
    {
        byte[] bytes = Latin1(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
