using System.IO.Compression;
using System.Text;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Reading encrypted documents: the standard security handler at revisions 2,
/// 3, 4 and 6, what opens them and what does not, the permissions the codec
/// enforces, and the order in which it does any of it.
/// </summary>
/// <remarks>
/// Every fixture is encrypted by <see cref="PdfTestEncryption"/>, which proves
/// the codec agrees with this suite's own producer. That the two agree with
/// other producers rests on documents LibreOffice encrypted at revisions 3 and
/// 6, which were read outside the tree and are recorded in the approved-sources
/// similarity log; no third-party file is committed (IP-020).
/// </remarks>
public sealed class PdfDecryptionTests
{
    private const string Body = "Quarterly figures";
    private const string Title = "Board minutes";

    public static TheoryData<string> Kinds => ["rc4-40", "rc4-128", "aes-128", "rc4-128-filter", "aes-256"];

    private static PdfTestEncryption Make(string kind, string user, string owner, int permissions = PdfTestEncryption.AllPermissions) => kind switch
    {
        "rc4-40" => PdfTestEncryption.Rc4(2, 40, user, owner, permissions),
        "rc4-128" => PdfTestEncryption.Rc4(3, 128, user, owner, permissions),
        "aes-128" => PdfTestEncryption.Revision4(aes: true, user, owner, permissions),
        "rc4-128-filter" => PdfTestEncryption.Revision4(aes: false, user, owner, permissions),
        "aes-256" => PdfTestEncryption.Revision6(user, owner, permissions),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>A one-page document with an Info title, encrypted as stated.</summary>
    private static byte[] Document(PdfTestEncryption encryption, Action<PdfFileBuilder, int>? more = null)
    {
        var builder = new PdfFileBuilder().Encrypt(encryption);
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText(Body));
        int info = builder.AddObject($"<< /Title ({Title}) /Author <4D2E204D65696572> >>");
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        more?.Invoke(builder, catalog);
        return builder.Build(catalog, $"/Info {info} 0 R");
    }

    private static PdfReadResult Read(byte[] pdf, string? password = null, PdfCodecServices? services = null)
    {
        PdfReadOptions options = password is null
            ? PdfReadOptions.Default
            : PdfReadOptions.Default.WithCredentials(PdfDecryptionCredentials.FromPassword(password));
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec(services ?? PdfCodecServices.Base).ReadPdf(stream, options);
    }

    private static void AssertNothingLeaked(PdfReadResult result, params string[] secrets)
    {
        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(0, result.PageCount);
        Assert.Equal(string.Empty, result.Document.PlainText.Trim());
        Assert.Null(result.Metadata.Title);
        foreach (string secret in secrets.Append(Body).Append(Title))
            Assert.All(result.Diagnostics, d => Assert.DoesNotContain(secret, d.Message, StringComparison.Ordinal));
    }

    // ---- what opens --------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Opens_With_The_User_Password_And_Decrypts_Strings_And_Streams(string kind)
    {
        PdfReadResult result = Read(Document(Make(kind, "user-pw", "owner-pw")), "user-pw");

        Assert.NotEqual(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(Body, result.Document.PlainText);
        Assert.Equal(Title, result.Metadata.Title);
        Assert.Equal(new[] { "M. Meier" }, result.Metadata.Authors);

        PdfEncryptionInfo encryption = Assert.IsType<PdfEncryptionInfo>(result.Encryption);
        Assert.Equal(PdfSecurityHandlerKind.Standard, encryption.Handler);
        Assert.Equal(PdfAccessLevel.User, encryption.Access);
        Assert.False(encryption.UserPasswordEmpty);
        (int version, int revision, PdfCipher cipher, int bits) = kind switch
        {
            "rc4-40" => (1, 2, PdfCipher.Rc4, 40),
            "rc4-128" => (2, 3, PdfCipher.Rc4, 128),
            "aes-128" => (4, 4, PdfCipher.Aes128, 128),
            "rc4-128-filter" => (4, 4, PdfCipher.Rc4, 128),
            _ => (5, 6, PdfCipher.Aes256, 256),
        };
        Assert.Equal((version, revision, cipher, bits), (encryption.Version, encryption.Revision, encryption.Cipher, encryption.KeyBits));

        DocumentDiagnostic decrypted = Assert.Single(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionDecrypted);
        Assert.Equal(DocumentDiagnosticSeverity.Info, decrypted.Severity);
        Assert.Contains("written in the clear", decrypted.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_Owner_Password_Opens_With_Every_Permission(string kind)
    {
        PdfReadResult result = Read(Document(Make(kind, "user-pw", "owner-pw", PdfTestEncryption.NoExtraction)), "owner-pw");

        Assert.Contains(Body, result.Document.PlainText);
        Assert.Equal(PdfAccessLevel.Owner, result.Encryption!.Access);
        Assert.Equal(PdfPermissions.All, result.Encryption.Permissions);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_Document_Whose_User_Password_Is_Empty_Opens_Without_One(string kind)
    {
        PdfReadResult result = Read(Document(Make(kind, string.Empty, "owner-pw")));

        Assert.Contains(Body, result.Document.PlainText);
        Assert.Equal(PdfAccessLevel.User, result.Encryption!.Access);
        Assert.True(result.Encryption.UserPasswordEmpty);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionDecrypted &&
            d.Message.Contains("opens for anyone", StringComparison.Ordinal));
    }

    [Fact]
    public void An_Unencrypted_Document_Reports_No_Encryption()
    {
        PdfReadResult result = Read(PdfFileBuilder.SinglePage(PdfFileBuilder.ShowText(Body)));

        Assert.Null(result.Encryption);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("pdf.encryption", StringComparison.Ordinal));
    }

    // ---- what does not, and what it says -------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_Missing_Password_Is_Asked_For_And_Nothing_Is_Read(string kind)
    {
        PdfReadResult result = Read(Document(Make(kind, "user-pw", "owner-pw")));

        AssertNothingLeaked(result);
        DocumentDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PdfDiagnosticCodes.EncryptionPasswordRequired, diagnostic.Code);
        Assert.Null(result.Encryption);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_Wrong_Password_Is_Refused(string kind)
    {
        PdfReadResult result = Read(Document(Make(kind, "user-pw", "owner-pw")), "not-it");

        AssertNothingLeaked(result, "not-it");
        Assert.Equal(PdfDiagnosticCodes.EncryptionPasswordIncorrect, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_Password_Never_Reaches_A_Diagnostic(string kind)
    {
        const string Password = "Needle-7fQ";
        byte[] pdf = Document(Make(kind, Password, "owner-pw"));

        foreach (string? attempt in new[] { Password, "Needle-7fQ-wrong", null })
        {
            PdfReadResult result = Read(pdf, attempt);
            Assert.All(result.Diagnostics, d => Assert.DoesNotContain("Needle", d.Message, StringComparison.Ordinal));
        }

        Assert.DoesNotContain("Needle", PdfDecryptionCredentials.FromPassword(Password).ToString(), StringComparison.Ordinal);
    }

    // ---- permissions --------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Extraction_Is_Refused_When_The_Permissions_Withhold_It(string kind)
    {
        byte[] pdf = Document(Make(kind, string.Empty, "owner-pw", PdfTestEncryption.NoExtraction));

        PdfReadResult refused = Read(pdf);

        AssertNothingLeaked(refused);
        Assert.Equal(PdfDiagnosticCodes.EncryptionExtractionNotPermitted, Assert.Single(refused.Diagnostics).Code);
        PdfEncryptionInfo encryption = Assert.IsType<PdfEncryptionInfo>(refused.Encryption);
        Assert.False(encryption.MayExtract);
        Assert.False(encryption.Permissions.HasFlag(PdfPermissions.CopyOrExtract));

        // The owner lifts it.
        Assert.Contains(Body, Read(pdf, "owner-pw").Document.PlainText);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_Permission_The_Codec_Does_Not_Need_Is_Reported_Rather_Than_Enforced(string kind)
    {
        const int NoPrinting = PdfTestEncryption.AllPermissions & ~(1 << 2) & ~(1 << 11);
        PdfReadResult result = Read(Document(Make(kind, string.Empty, "owner-pw", NoPrinting)));

        Assert.Contains(Body, result.Document.PlainText);
        Assert.False(result.Encryption!.Permissions.HasFlag(PdfPermissions.Print));
        Assert.True(result.Encryption.Permissions.HasFlag(PdfPermissions.CopyOrExtract));
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionDecrypted &&
            d.Message.Contains("print no", StringComparison.Ordinal));
    }

    [Fact]
    public void Revision_2_Answers_Its_Later_Permission_Bits_From_The_Bits_They_Refine()
    {
        // Revision 2 has only bits 3 to 6. Withholding bit 5 withholds
        // accessibility extraction with it, which bit 10 refines.
        PdfReadResult result = Read(Document(Make("rc4-40", string.Empty, "owner-pw", PdfTestEncryption.NoExtraction)));

        Assert.False(result.Encryption!.Permissions.HasFlag(PdfPermissions.ExtractForAccessibility));
        Assert.True(result.Encryption.Permissions.HasFlag(PdfPermissions.FillForms));
    }

    [Fact]
    public void Tampered_Revision_6_Permissions_Grant_Only_What_Both_Copies_Grant()
    {
        // /P, outside the encryption, grants everything; the sealed copy inside
        // it withholds extraction. Someone changed one of them.
        var encryption = PdfTestEncryption.Revision6(string.Empty, "owner-pw", PdfTestEncryption.AllPermissions, sealedPermissions: PdfTestEncryption.NoExtraction);

        PdfReadResult result = Read(Document(encryption));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPermissionsInconsistent);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionExtractionNotPermitted);
    }

    // ---- passwords in more than one byte form ---------------------------------------

    [Fact]
    public void A_Revision_6_Password_Is_Prepared_With_Saslprep()
    {
        // Protected as the composed, visible form; typed as a keyboard might
        // produce it: decomposed, with a no-break space, a soft hyphen, and the
        // variation selector an emoji picker appends.
        string stored = "Gr" + (char)0x00FC + "ße sesame " + (char)0x2764;
        string typed = "Gru" + (char)0x0308 + "ße" + (char)0x00A0 + "ses" + (char)0x00AD + "ame " + (char)0x2764 + (char)0xFE0F;

        byte[] pdf = Document(PdfTestEncryption.Revision6(stored, "owner-pw"));

        Assert.Contains(Body, Read(pdf, typed).Document.PlainText);
    }

    [Theory]
    [InlineData("windows-1252")]
    [InlineData("utf-8")]
    public void A_Legacy_Password_Is_Tried_In_The_Encodings_Producers_Use(string producerEncoding)
    {
        // "Kennwort-EUR" with a euro sign: PDFDocEncoding has no slot this codec
        // maps it from, LibreOffice writes Windows-1252, and some producers UTF-8.
        string password = "Kennwort-" + (char)0x20AC;
        byte[] stored = producerEncoding == "utf-8"
            ? Encoding.UTF8.GetBytes(password)
            : [.. Encoding.ASCII.GetBytes("Kennwort-"), 0x80];

        byte[] pdf = Document(PdfTestEncryption.Rc4(3, 128, stored, stored));

        Assert.Contains(Body, Read(pdf, password).Document.PlainText);
    }

    // ---- what is and is not encrypted ----------------------------------------------

    private static readonly string XmpPacket =
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
        "<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Packet title</rdf:li></rdf:Alt></dc:title>" +
        "</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";

    [Fact]
    public void Metadata_The_Document_Leaves_Unencrypted_Is_Read_As_It_Stands()
    {
        var encryption = PdfTestEncryption.Revision4(aes: true, "user-pw", "owner-pw", encryptMetadata: false);
        byte[] pdf = Document(encryption, (builder, catalog) =>
        {
            int metadata = builder.AddStream("/Type /Metadata /Subtype /XML", Encoding.UTF8.GetBytes(XmpPacket));
            AddToCatalog(builder, catalog, $" /Metadata {metadata} 0 R");
        });

        PdfReadResult result = Read(pdf, "user-pw");

        Assert.Equal("Packet title", result.Metadata.Title);
        Assert.False(result.Encryption!.MetadataEncrypted);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPartiallyUnencrypted);
    }

    [Fact]
    public void A_Metadata_Packet_Left_In_The_Clear_Against_The_Declaration_Is_Read_And_Reported()
    {
        // LibreOffice writes its RC4 documents' XMP packets this way.
        byte[] pdf = Document(Make("rc4-128", "user-pw", "owner-pw"), (builder, catalog) =>
        {
            int metadata = builder.AddPlainStream("/Type /Metadata /Subtype /XML", Encoding.UTF8.GetBytes(XmpPacket));
            AddToCatalog(builder, catalog, $" /Metadata {metadata} 0 R");
        });

        PdfReadResult result = Read(pdf, "user-pw");

        Assert.Equal("Packet title", result.Metadata.Title);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPartiallyUnencrypted);
    }

    [Fact]
    public void A_Stream_Selecting_The_Identity_Crypt_Filter_Is_Read_Unencrypted_And_Reported()
    {
        var builder = new PdfFileBuilder().Encrypt(Make("aes-128", "user-pw", "owner-pw"));
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int clear = builder.AddStream("/Filter [/Crypt] /DecodeParms [<< /Type /CryptFilterDecodeParms /Name /Identity >>]", PdfFileBuilder.ShowText("Left in the clear", y: 700));
        int named = builder.AddStream("/Filter [/Crypt] /DecodeParms [<< /Type /CryptFilterDecodeParms /Name /StdCF >>]", PdfFileBuilder.ShowText(Body));
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents [{named} 0 R {clear} 0 R] >>");

        PdfReadResult result = Read(builder.Build(catalog), "user-pw");

        Assert.Contains(Body, result.Document.PlainText);
        Assert.Contains("Left in the clear", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPartiallyUnencrypted);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.FilterCryptUnsupported);
    }

    [Fact]
    public void A_Document_That_Encrypts_Only_Its_Strings_Says_Its_Streams_Are_Unprotected()
    {
        byte[] pdf = Document(PdfTestEncryption.Revision4(aes: true, "user-pw", "owner-pw", streams: "Identity"));

        PdfReadResult result = Read(pdf, "user-pw");

        Assert.Contains(Body, result.Document.PlainText);
        Assert.Equal(Title, result.Metadata.Title);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPartiallyUnencrypted &&
            d.Message.Contains("streams", StringComparison.Ordinal));
    }

    [Fact]
    public void A_String_The_Cipher_Cannot_Have_Produced_Costs_That_String_Only()
    {
        PdfTestEncryption encryption = Make("aes-128", "user-pw", "owner-pw");
        var builder = new PdfFileBuilder().Encrypt(encryption);
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText(Body));
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        // The title is encrypted properly; the subject is 21 bytes, a vector
        // and five bytes of a block that never ended.
        int info = builder.Reserve();
        string title = PdfTestEncryption.Hex(encryption.EncryptString(PdfFileBuilder.Latin1(Title), info, 0));
        builder.SetObject(info, $"<< /Title <{title}> /Subject <000102030405060708090A0B0C0D0E0F1011121314> >>");
        builder.LeavePlain(info);

        PdfReadResult result = Read(builder.Build(catalog, $"/Info {info} 0 R"), "user-pw");

        Assert.Contains(Body, result.Document.PlainText);
        Assert.Equal(Title, result.Metadata.Title);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionObjectMalformed);
    }

    // ---- the order things happen in --------------------------------------------------

    /// <summary>
    /// A cross-reference-stream file with its Catalog, page tree and page inside
    /// an object stream, as most current producers write one, encrypted. The
    /// page carries a link whose URI lives inside the object stream.
    /// </summary>
    private static byte[] StreamedDocument(PdfTestEncryption encryption, bool breakStartXref = false)
    {
        const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 7 0 R >> >> /Contents 4 0 R " +
                            "/Annots [<< /Type /Annot /Subtype /Link /Rect [70 710 400 740] /A << /S /URI /URI (https://example.org/inside) >> >>] >>";

        string[] bodies = [Catalog, Pages, Page];
        var offsets = new StringBuilder();
        var payload = new StringBuilder();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Append(i + 1).Append(' ').Append(payload.Length).Append(' ');
            payload.Append(bodies[i]).Append(' ');
        }

        string header = offsets.ToString();
        byte[] objectStream = encryption.EncryptStream(Deflate(PdfFileBuilder.Latin1(header + payload)), 5, 0);
        byte[] content = encryption.EncryptStream(PdfFileBuilder.Latin1(PdfFileBuilder.ShowText(Body)), 4, 0);
        string title = PdfTestEncryption.Hex(encryption.EncryptString(PdfFileBuilder.Latin1(Title), 8, 0));

        var output = new MemoryStream();
        Write(output, "%PDF-1.7\n");
        var positions = new Dictionary<int, long>();

        positions[4] = output.Length;
        Write(output, $"4 0 obj\n<< /Length {content.Length} >>\nstream\n");
        output.Write(content);
        Write(output, "\nendstream\nendobj\n");

        positions[7] = output.Length;
        Write(output, "7 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        positions[8] = output.Length;
        Write(output, $"8 0 obj\n<< /Title <{title}> >>\nendobj\n");

        positions[9] = output.Length;
        Write(output, $"9 0 obj\n{encryption.Dictionary}\nendobj\n");

        positions[5] = output.Length;
        Write(output, $"5 0 obj\n<< /Type /ObjStm /N {bodies.Length} /First {header.Length} /Filter /FlateDecode /Length {objectStream.Length} >>\nstream\n");
        output.Write(objectStream);
        Write(output, "\nendstream\nendobj\n");

        long xrefPosition = output.Length;
        var rows = new List<byte>();
        void Row(byte type, long second, int third)
        {
            rows.Add(type);
            rows.Add((byte)(second >> 24));
            rows.Add((byte)(second >> 16));
            rows.Add((byte)(second >> 8));
            rows.Add((byte)second);
            rows.Add((byte)(third >> 8));
            rows.Add((byte)third);
        }

        Row(0, 0, 65535);
        Row(2, 5, 0);
        Row(2, 5, 1);
        Row(2, 5, 2);
        Row(1, positions[4], 0);
        Row(1, positions[5], 0);
        Row(1, xrefPosition, 0);
        Row(1, positions[7], 0);
        Row(1, positions[8], 0);
        Row(1, positions[9], 0);

        string id = PdfTestEncryption.Hex(encryption.FileIdentifier);
        byte[] xref = Deflate([.. rows]);
        Write(output, $"6 0 obj\n<< /Type /XRef /Size 10 /W [1 4 2] /Root 1 0 R /Info 8 0 R /Encrypt 9 0 R /ID [<{id}> <{id}>] /Filter /FlateDecode /Length {xref.Length} >>\nstream\n");
        output.Write(xref);
        Write(output, $"\nendstream\nendobj\nstartxref\n{(breakStartXref ? 999_999 : xrefPosition)}\n%%EOF\n");

        return output.ToArray();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Object_Stream_Members_Are_Decrypted_Once_With_Their_Container(string kind)
    {
        PdfReadResult result = Read(StreamedDocument(Make(kind, "user-pw", "owner-pw")), "user-pw");

        Assert.Contains(Body, result.Document.PlainText);
        Assert.Equal(Title, result.Metadata.Title);

        // The link's URI sat inside the object stream: decrypted a second time,
        // it would be noise the URI policy refuses.
        Assert.Contains("https://example.org/inside", Links(result));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Nothing_Behind_The_Trailer_Is_Touched_Before_The_Document_Is_Open(string kind)
    {
        // The defect this closes: the Catalog was resolved before encryption was
        // known, which decoded the encrypted object stream and, when that failed,
        // ran a recovery scan - all before the rejection.
        PdfReadResult result = Read(StreamedDocument(Make(kind, "user-pw", "owner-pw")));

        AssertNothingLeaked(result);
        Assert.Equal(PdfDiagnosticCodes.EncryptionPasswordRequired, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_Damaged_Encrypted_File_Is_Still_Read_As_Encrypted(string kind)
    {
        // The defect this closes: with the cross-reference stream unreachable,
        // recovery rebuilt a trailer without /Encrypt, and the ciphertext was
        // read as a plain document that "needed OCR".
        byte[] pdf = StreamedDocument(Make(kind, "user-pw", "owner-pw"), breakStartXref: true);

        PdfReadResult without = Read(pdf);
        Assert.Equal(DocumentResultStatus.Rejected, without.Status);
        Assert.Contains(without.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPasswordRequired);
        Assert.DoesNotContain(without.Diagnostics, d => d.Code == PdfDiagnosticCodes.TextOcrRequired);

        PdfReadResult with = Read(pdf, "user-pw");
        Assert.Contains(Body, with.Document.PlainText);
        Assert.Equal(Title, with.Metadata.Title);
        Assert.Contains(with.Diagnostics, d => d.Code == PdfDiagnosticCodes.XrefRecovered);
    }

    [Fact]
    public void A_File_That_Lost_Its_Trailer_Is_Recognized_By_Its_Encryption_Dictionary()
    {
        // No cross-reference data and no trailer at all: only the encryption
        // dictionary's own shape says the file is encrypted. Revision 6 needs no
        // file identifier, so the lost trailer costs it nothing else.
        var builder = new PdfFileBuilder().Encrypt(PdfTestEncryption.Revision6("user-pw", "owner-pw"));
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText(Body));
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        byte[] pdf = builder.BuildWithoutXref();

        Assert.Contains(Read(pdf).Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPasswordRequired);
        Assert.Contains(Body, Read(pdf, "user-pw").Document.PlainText);
    }

    [Fact]
    public void An_Encryption_Introduced_By_An_Incremental_Update_Is_Seen()
    {
        // The update's trailer names /Encrypt; the original's does not. The
        // effective trailer is the merge, so the file is encrypted.
        var encryption = Make("rc4-128", "user-pw", "owner-pw");
        byte[] pdf = Document(encryption);
        string text = Encoding.Latin1.GetString(pdf);
        int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        long previous = long.Parse(text[(startxref + 9)..].Trim().Split('\n')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);

        // Strip /Encrypt from the original trailer, and restate it in an update.
        string withoutEncrypt = System.Text.RegularExpressions.Regex.Replace(text, @"/Encrypt \d+ 0 R", "/Encrypt null");
        var update = new StringBuilder(withoutEncrypt);
        int encryptObject = int.Parse(System.Text.RegularExpressions.Regex.Match(text, @"/Encrypt (\d+) 0 R").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        string id = PdfTestEncryption.Hex(encryption.FileIdentifier);
        long xref = update.Length;
        update.Append($"xref\n0 0\ntrailer\n<< /Size {encryptObject + 1} /Root 1 0 R /Encrypt {encryptObject} 0 R /ID [<{id}> <{id}>] /Prev {previous} >>\nstartxref\n{xref}\n%%EOF\n");
        byte[] updated = Encoding.Latin1.GetBytes(update.ToString());

        PdfReadResult result = Read(updated);
        Assert.Contains(result.Diagnostics, d => d.Code == PdfDiagnosticCodes.EncryptionPasswordRequired);
        Assert.Contains(Body, Read(updated, "user-pw").Document.PlainText);
    }

    // ---- what is refused by name -----------------------------------------------------

    [Fact]
    public void Revision_5_Is_Refused_By_Name()
    {
        PdfTestEncryption encryption = PdfTestEncryption.Revision6("user-pw", "owner-pw");
        encryption.WithDictionary(encryption.Dictionary.Replace("/R 6", "/R 5", StringComparison.Ordinal));

        PdfReadResult result = Read(Document(encryption), "user-pw");

        AssertNothingLeaked(result);
        DocumentDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PdfDiagnosticCodes.EncryptionUnsupported, diagnostic.Code);
        Assert.Contains("revision 5", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<< /Filter /FOPN_fLock /V 1 /R 2 /O <00> /U <00> >>", "/FOPN_fLock")]
    [InlineData("<< /Filter /Standard /V 3 /R 3 /Length 128 /P -4 /O <00> /U <00> >>", "/V 3 /R 3")]
    [InlineData("<< /Filter /Standard /V 4 /R 4 /P -4 /O <00> /U <00> /CF << /StdCF << /CFM /AESV3 >> >> /StmF /StdCF /StrF /StdCF >>", "/AESV3")]
    public void An_Unknown_Handler_Revision_Or_Method_Is_Refused_By_Name(string dictionary, string named)
    {
        PdfReadResult result = Read(Document(Make("rc4-128", "user-pw", "owner-pw").WithDictionary(dictionary)), "user-pw");

        AssertNothingLeaked(result);
        DocumentDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PdfDiagnosticCodes.EncryptionUnsupported, diagnostic.Code);
        Assert.Contains(named, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Document_For_Certificate_Recipients_Needs_A_Composed_Decryptor()
    {
        const string Dictionary = "<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 4 " +
                                  "/CF << /DefaultCryptFilter << /CFM /AESV2 /Recipients [<3000>] >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>";
        PdfReadResult result = Read(Document(Make("aes-128", "user-pw", "owner-pw").WithDictionary(Dictionary)));

        AssertNothingLeaked(result);
        Assert.Equal(PdfDiagnosticCodes.EncryptionRecipientNotComposed, Assert.Single(result.Diagnostics).Code);
    }

    // ---- helpers ---------------------------------------------------------------------

    private static void AddToCatalog(PdfFileBuilder builder, int catalog, string entries) =>
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {catalog + 1} 0 R{entries} >>");

    private static List<string> Links(PdfReadResult result)
    {
        var links = new List<string>();
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            foreach (StyleRun run in paragraph.Runs)
            {
                if (run.Style.LinkHref is { } href)
                    links.Add(href);
            }
        }

        return links;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    private static void Write(Stream stream, string text)
    {
        byte[] bytes = PdfFileBuilder.Latin1(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
