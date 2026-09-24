using System.Globalization;
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
    private readonly Dictionary<int, (string Dictionary, byte[] Data)> _streams = [];
    private readonly HashSet<int> _plain = [];
    private string _version = "1.7";
    private string _preamble = string.Empty;
    private PdfTestEncryption? _encryption;

    /// <summary>
    /// Encrypts the file as <paramref name="encryption"/> states: every string
    /// and stream under its object's key, the <c>/Encrypt</c> dictionary added as
    /// an object of its own, and the file identifier written to the trailer.
    /// </summary>
    public PdfFileBuilder Encrypt(PdfTestEncryption encryption)
    {
        _encryption = encryption;
        return this;
    }

    /// <summary>
    /// Adds a stream that stays unencrypted in an encrypted file, as a producer
    /// that leaves one in the clear writes it.
    /// </summary>
    public int AddPlainStream(string dictionaryBody, byte[] data)
    {
        int number = AddStream(dictionaryBody, data);
        _plain.Add(number);
        return number;
    }

    /// <summary>Leaves an object's strings as they are written in an encrypted file.</summary>
    public void LeavePlain(int number) => _plain.Add(number);

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
        string dictionary = filter is null ? dictionaryBody : dictionaryBody + " /Filter /" + filter;
        _objects.Add(null);
        int number = _objects.Count - 1;
        _streams[number] = (dictionary, data);
        return number;
    }

    // The stream as written: encrypted when the file is, with /Length measured
    // after encryption, which is the length the file actually holds.
    private byte[] StreamBytes(int number)
    {
        (string dictionary, byte[] data) = _streams[number];
        if (_encryption is not null && !_plain.Contains(number) && !IsExempt(dictionary))
        {
            dictionary = EncryptStrings(dictionary, number);
            data = _encryption.EncryptStream(data, number, 0);
        }

        var bytes = new List<byte>();
        bytes.AddRange(Latin1("<< " + dictionary + " /Length " + data.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n"));
        bytes.AddRange(data);
        bytes.AddRange(Latin1("\nendstream"));
        return [.. bytes];
    }

    // What a producer leaves unencrypted: a stream that selects the Identity
    // crypt filter, and the metadata stream of a document that says so.
    private bool IsExempt(string dictionary) =>
        (dictionary.Contains("/Crypt", StringComparison.Ordinal) &&
         (dictionary.Contains("/Name /Identity", StringComparison.Ordinal) || !dictionary.Contains("/Name", StringComparison.Ordinal))) ||
        (!_encryption!.EncryptMetadata && dictionary.Contains("/Type /Metadata", StringComparison.Ordinal));

    /// <summary>
    /// Rewrites every string in an object's body as a hex string of its
    /// encryption under that object's key. Literal strings are decoded first,
    /// escapes and all, because what is encrypted is the string's bytes.
    /// </summary>
    private string EncryptStrings(string body, int number)
    {
        const char Backslash = (char)92;
        var output = new StringBuilder(body.Length * 2);
        int i = 0;
        while (i < body.Length)
        {
            char c = body[i];
            if (c == '<' && i + 1 < body.Length && body[i + 1] == '<')
            {
                output.Append("<<");
                i += 2;
                continue;
            }

            if (c == '<')
            {
                int end = body.IndexOf('>', i);
                string hex = new([.. body[(i + 1)..end].Where(Uri.IsHexDigit)]);
                if (hex.Length % 2 == 1)
                    hex += "0";
                byte[] plain = Convert.FromHexString(hex);
                output.Append('<').Append(PdfTestEncryption.Hex(_encryption!.EncryptString(plain, number, 0))).Append('>');
                i = end + 1;
                continue;
            }

            if (c != '(')
            {
                output.Append(c);
                i++;
                continue;
            }

            var bytes = new List<byte>();
            int depth = 1;
            i++;
            while (i < body.Length && depth > 0)
            {
                char d = body[i++];
                if (d == Backslash && i < body.Length)
                {
                    char e = body[i++];
                    bytes.Add(e switch
                    {
                        'n' => 10,
                        'r' => 13,
                        't' => 9,
                        'b' => 8,
                        'f' => 12,
                        _ => (byte)e,
                    });
                    continue;
                }

                if (d == '(')
                    depth++;
                else if (d == ')' && --depth == 0)
                    break;

                bytes.Add((byte)d);
            }

            output.Append('<').Append(PdfTestEncryption.Hex(_encryption!.EncryptString([.. bytes], number, 0))).Append('>');
        }

        return output.ToString();
    }

    public int AddStream(string dictionaryBody, string content, string? filter = null) =>
        AddStream(dictionaryBody, Latin1(content), filter);

    /// <summary>
    /// Emits the file with a classic cross-reference table and a trailer naming
    /// <paramref name="rootObject"/> as the catalog.
    /// </summary>
    public byte[] Build(int rootObject, string? extraTrailerEntries = null)
    {
        int encryptObject = AddEncryptionDictionary();

        var output = new MemoryStream();
        Append(output, _preamble);
        // Cross-reference offsets are measured from the header, not from byte
        // zero, which is what a producer that writes a preamble emits.
        long headerOrigin = output.Length;
        Append(output, $"%PDF-{_version}\n");

        var offsets = new long[_objects.Count];
        for (int i = 1; i < _objects.Count; i++)
        {
            byte[]? body = BodyOf(i, encryptObject);
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
        if (encryptObject > 0)
            Append(output, " " + EncryptionTrailerEntries(encryptObject));
        if (extraTrailerEntries is not null)
            Append(output, " " + extraTrailerEntries);
        Append(output, $" >>\nstartxref\n{xref}\n%%EOF\n");

        return output.ToArray();
    }

    /// <summary>Emits the file without any cross-reference table, to test recovery.</summary>
    public byte[] BuildWithoutXref()
    {
        int encryptObject = AddEncryptionDictionary();

        var output = new MemoryStream();
        Append(output, $"%PDF-{_version}\n");
        for (int i = 1; i < _objects.Count; i++)
        {
            byte[]? body = BodyOf(i, encryptObject);
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
    /// The trailer entries an encrypted file carries: its encryption dictionary
    /// and, unless the fixture leaves it out, its file identifier.
    /// </summary>
    public string EncryptionTrailerEntries(int encryptObject)
    {
        string id = PdfTestEncryption.Hex(_encryption!.FileIdentifier);
        return _encryption.WriteIdentifier
            ? $"/Encrypt {encryptObject} 0 R /ID [<{id}> <{id}>]"
            : $"/Encrypt {encryptObject} 0 R";
    }

    // The /Encrypt dictionary is an object of its own, written as it stands:
    // the format encrypts none of its strings.
    private int AddEncryptionDictionary()
    {
        if (_encryption is null)
            return 0;

        _objects.Add(Latin1(_encryption.Dictionary));
        return _objects.Count - 1;
    }

    private byte[]? BodyOf(int number, int encryptObject)
    {
        if (_streams.ContainsKey(number))
            return StreamBytes(number);

        byte[]? body = _objects[number];
        if (body is null || _encryption is null || number == encryptObject || _plain.Contains(number))
            return body;

        return Latin1(EncryptStrings(Encoding.Latin1.GetString(body), number));
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
    /// <remarks>
    /// Numbers are written invariantly: under a culture with a decimal comma,
    /// 595.5 came out as <c>595,5</c>, which is not a PDF number, and the
    /// <c>Tm</c> it belonged to was dropped - so the text was drawn at the
    /// origin and a test could pass for a reason it never stated.
    /// </remarks>
    public static string ShowText(string text, double x = 72, double y = 720, double size = 12) =>
        string.Create(CultureInfo.InvariantCulture, $"BT /F1 {size} Tf 1 0 0 1 {x} {y} Tm ({Escape(text)}) Tj ET\n");

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
