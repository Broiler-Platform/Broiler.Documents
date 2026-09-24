using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using Broiler.Documents.Pdf.Security;

namespace Broiler.Documents.Pdf.Tests;

/// <summary>
/// Documents encrypted for certificate recipients (the public-key security
/// handler): the codec's PDF half, opened through a recipient decryptor composed
/// the way a host would compose one.
/// </summary>
/// <remarks>
/// <b>What these tests prove is agreement, not interoperability.</b> The
/// envelopes are made with the platform's <c>EnvelopedCms</c>, the keys derived
/// the way this suite reads ISO 32000-1 §7.6.4, and the documents encrypted by
/// <see cref="PdfTestEncryption"/>. No document another producer encrypted for a
/// certificate has been read, which the approved-sources record states; the byte
/// order of the permissions in an envelope is the reading most exposed to that.
/// </remarks>
public sealed class PdfPublicKeyDecryptionTests
{
    private const string Body = "Recipient only";
    private const string Title = "Sealed memo";

    private static readonly Lazy<X509Certificate2> Alice = new(() => Certificate("Alice"));
    private static readonly Lazy<X509Certificate2> Mallory = new(() => Certificate("Mallory"));

    private static readonly byte[] Seed = [.. Enumerable.Range(1, 20).Select(i => (byte)(i * 7))];

    public static TheoryData<string> Kinds => ["s4-rc4-128", "s5-aes-128", "s5-aes-256"];

    // ---- what opens --------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Opens_With_A_Recipient_Key_The_Host_Composed(string kind)
    {
        byte[] pdf = Document(kind, [Envelope(PdfTestEncryption.AllPermissions, Alice.Value)]);

        PdfReadResult result = Read(pdf, new EnvelopeDecryptor(Alice.Value));

        Assert.Contains(Body, result.Document.PlainText);
        Assert.Equal(Title, result.Metadata.Title);

        PdfEncryptionInfo encryption = Assert.IsType<PdfEncryptionInfo>(result.Encryption);
        Assert.Equal(PdfSecurityHandlerKind.PublicKey, encryption.Handler);
        Assert.Equal(PdfAccessLevel.Recipient, encryption.Access);
        Assert.Equal(0, encryption.Revision);
        Assert.Equal(kind switch
        {
            "s4-rc4-128" => PdfCipher.Rc4,
            "s5-aes-128" => PdfCipher.Aes128,
            _ => PdfCipher.Aes256,
        }, encryption.Cipher);
    }

    [Fact]
    public void A_Key_For_Nobody_On_The_Lists_Is_Refused()
    {
        byte[] pdf = Document("s5-aes-128", [Envelope(PdfTestEncryption.AllPermissions, Alice.Value)]);

        PdfReadResult result = Read(pdf, new EnvelopeDecryptor(Mallory.Value));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(PdfDiagnosticCodes.EncryptionRecipientNotFound, Assert.Single(result.Diagnostics).Code);
        Assert.DoesNotContain(Body, result.Document.PlainText);
    }

    [Fact]
    public void A_Decryptor_That_Fails_Is_Taken_As_Holding_No_Key()
    {
        byte[] pdf = Document("s5-aes-128", [Envelope(PdfTestEncryption.AllPermissions, Alice.Value)]);

        PdfReadResult result = Read(pdf, new ThrowingDecryptor());

        Assert.Equal(PdfDiagnosticCodes.EncryptionRecipientNotFound, Assert.Single(result.Diagnostics).Code);
    }

    // ---- permissions --------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_Recipient_List_That_Withholds_Extraction_Is_Honoured(string kind)
    {
        byte[] pdf = Document(kind, [Envelope(PdfTestEncryption.NoExtraction, Alice.Value)]);

        PdfReadResult result = Read(pdf, new EnvelopeDecryptor(Alice.Value));

        Assert.Equal(DocumentResultStatus.Rejected, result.Status);
        Assert.Equal(PdfDiagnosticCodes.EncryptionExtractionNotPermitted, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.Encryption!.MayExtract);
    }

    [Fact]
    public void A_Recipient_On_Two_Lists_Takes_The_Permissions_Of_The_First()
    {
        // ISO 32000-1 Table 27: a recipient in more than one list gets the
        // first list's permissions. Every list's bytes go into the key either way.
        byte[] pdf = Document("s5-aes-128", [
            Envelope(PdfTestEncryption.NoExtraction, Alice.Value),
            Envelope(PdfTestEncryption.AllPermissions, Alice.Value),
        ]);

        PdfReadResult result = Read(pdf, new EnvelopeDecryptor(Alice.Value));

        Assert.Equal(PdfDiagnosticCodes.EncryptionExtractionNotPermitted, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void The_Second_List_Opens_For_A_Recipient_Only_On_It()
    {
        byte[] pdf = Document("s5-aes-256", [
            Envelope(PdfTestEncryption.NoExtraction, Mallory.Value),
            Envelope(PdfTestEncryption.AllPermissions, Alice.Value),
        ]);

        PdfReadResult result = Read(pdf, new EnvelopeDecryptor(Alice.Value));

        Assert.Contains(Body, result.Document.PlainText);
    }

    // ---- fixtures -----------------------------------------------------------------

    private static PdfReadResult Read(byte[] pdf, IPdfRecipientDecryptor decryptor)
    {
        using var stream = new MemoryStream(pdf);
        return new PdfDocumentCodec(PdfCodecServices.Base.WithRecipientDecryptor(decryptor)).ReadPdf(stream);
    }

    /// <summary>
    /// One recipient list: the seed and the permissions, big-endian, enveloped
    /// for <paramref name="recipient"/>.
    /// </summary>
    private static byte[] Envelope(int permissions, X509Certificate2 recipient)
    {
        byte[] content = new byte[24];
        Seed.CopyTo(content, 0);
        BinaryPrimitives.WriteInt32BigEndian(content.AsSpan(20), permissions);

        var cms = new EnvelopedCms(new ContentInfo(content));
        cms.Encrypt(new CmsRecipient(recipient));
        return cms.Encode();
    }

    private static byte[] Document(string kind, byte[][] lists)
    {
        string recipients = "[" + string.Join(" ", lists.Select(list => "<" + PdfTestEncryption.Hex(list) + ">")) + "]";
        byte[] digestInput = [.. Seed, .. lists.SelectMany(list => list)];

        (string dictionary, byte[] key, PdfTestEncryption.Method method) = kind switch
        {
            "s4-rc4-128" => (
                $"<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s4 /V 2 /Length 128 /Recipients {recipients} >>",
                SHA1.HashData(digestInput)[..16],
                PdfTestEncryption.Method.Rc4),
            "s5-aes-128" => (
                $"<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 4 /CF << /DefaultCryptFilter << /CFM /AESV2 /Recipients {recipients} >> >> " +
                "/StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
                SHA1.HashData(digestInput)[..16],
                PdfTestEncryption.Method.Aes128),
            _ => (
                $"<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Recipients {recipients} >> >> " +
                "/StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
                SHA256.HashData(digestInput),
                PdfTestEncryption.Method.Aes256),
        };

        var builder = new PdfFileBuilder().Encrypt(PdfTestEncryption.PublicKey(dictionary, key, method));
        int catalog = builder.Reserve();
        int pages = builder.Reserve();
        int page = builder.Reserve();
        int font = builder.AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        int content = builder.AddStream(string.Empty, PdfFileBuilder.ShowText(Body));
        int info = builder.AddObject($"<< /Title ({Title}) >>");
        builder.SetObject(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        builder.SetObject(pages, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        builder.SetObject(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        return builder.Build(catalog, $"/Info {info} 0 R");
    }

    private static X509Certificate2 Certificate(string name)
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    /// <summary>
    /// A recipient decryptor over <c>EnvelopedCms</c>, the few lines a host
    /// composes: find the recipient entry naming the certificate, and open it
    /// with that certificate's private key - never a certificate store.
    /// </summary>
    private sealed class EnvelopeDecryptor(X509Certificate2 certificate) : IPdfRecipientDecryptor
    {
        public byte[]? Open(ReadOnlySpan<byte> envelope, PdfRecipientContext context)
        {
            var cms = new EnvelopedCms();
            cms.Decode(envelope);

            foreach (RecipientInfo recipient in cms.RecipientInfos)
            {
                if (recipient.RecipientIdentifier.Value is not X509IssuerSerial issuerSerial ||
                    issuerSerial.IssuerName != certificate.Issuer ||
                    !string.Equals(issuerSerial.SerialNumber, certificate.SerialNumber, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using RSA? key = certificate.GetRSAPrivateKey();
                if (key is null)
                    return null;

                cms.Decrypt(recipient, key);
                byte[] content = cms.ContentInfo.Content;
                return content.Length <= context.MaxContentBytes ? content : null;
            }

            return null;
        }
    }

    private sealed class ThrowingDecryptor : IPdfRecipientDecryptor
    {
        public byte[]? Open(ReadOnlySpan<byte> envelope, PdfRecipientContext context) =>
            throw new CryptographicException("A decryptor that fails.");
    }
}
