using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Broiler.Documents.Docx;
using Broiler.Documents.Html;
using Broiler.Documents.Markdown;
using Broiler.Documents.Model;
using Broiler.Documents.Odt;
using Broiler.Documents.Pdf;
using Broiler.Documents.Pdf.Images;
using Broiler.Documents.Rtf;
using Broiler.Graphics.Imaging;
using Broiler.Media;
using Broiler.Media.Image.Managed;

namespace Broiler.Documents.Cli.Composition;

/// <summary>
/// This tool's composition root. Everything the process can do with a format is
/// decided here, once, in code you can read - which is the whole point of ADR
/// 0001/0003's "no hidden global registration" rule.
/// </summary>
/// <remarks>
/// <para>
/// <b>PDF is read and never written.</b> <c>Broiler.Documents.Pdf</c> is
/// composed through <see cref="ReadOnlyCodec"/>, so this tool opens, converts
/// from and renders PDF files and produces none: <c>docs/pdf-support-roadmap.md</c>
/// §4.1 lets an application read PDF before it lets one write it, and a CLI is
/// the one surface an automated system would come to depend on.
/// </para>
/// <para>
/// <b>Of the codec's optional providers, only the ICC colour-profile reader is
/// composed</b> (<see cref="CreatePdfServices"/>). What this tool does with a
/// PDF's pictures is show them: <c>render</c> draws them into an image, and
/// <c>convert</c> carries them into another format. A picture in an
/// <c>ICCBased</c> colour space - what a colour-managed producer writes - is
/// refused without a reader, and so is missing from both. IP-024 approved an
/// independent implementation of the profile functionality a PDF uses, and the
/// package it lives in depends on nothing this tool did not already reference.
/// The image filters in that package, and the font-program reader, stay uncomposed:
/// each is a decoder with a register row of its own, so what they would decode
/// is reported as skipped rather than read.
/// </para>
/// <para>
/// The image codecs are a separate registration with a separate reason. The
/// graphics core deliberately carries no default codec catalog, so
/// <c>BBitmap.Save</c> and <c>BBitmap.Decode</c> do nothing until a composition
/// root names one; <see cref="RegisterImageCodecs"/> is where this process does.
/// </para>
/// </remarks>
public static class CodecComposition
{
    /// <summary>The formats this tool reads and writes, in the order help lists them.</summary>
    /// <param name="pdfPassword">
    /// The password <c>--password-file</c> gave this run, handed to the PDF codec
    /// alone; null reads an encrypted PDF only when it needs no password.
    /// </param>
    public static DocumentCodecCatalog CreateCatalog(string? pdfPassword = null)
    {
        DocumentCodec pdf = new ReadOnlyCodec(new PdfDocumentCodec(CreatePdfServices()));
        if (pdfPassword is not null)
            pdf = new PasswordedPdfCodec(pdf, PdfDecryptionCredentials.FromPassword(pdfPassword));

        return new([
            new DocxDocumentCodec(),
            new OdtDocumentCodec(),
            new RtfDocumentCodec(),
            new HtmlDocumentCodec(),
            new MarkdownDocumentCodec(),
            pdf,
        ]);
    }

    /// <summary>
    /// The PDF service graph this tool reads with: the base graph, and an ICC
    /// colour-profile reader so that a picture in ICC-based colour is converted
    /// to the sRGB the renderer and every other format expect.
    /// </summary>
    public static PdfCodecServices CreatePdfServices() =>
        PdfCodecServices.Base.WithColorProfileReader(new IccColorProfileReader());

    /// <summary>
    /// Registers the managed image codecs with Broiler.Graphics. Idempotent, and
    /// cheap enough to call from every command that might touch a bitmap.
    /// </summary>
    public static void RegisterImageCodecs()
    {
        if (!BImageCodecs.IsRegistered)
            BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs()));
    }

    /// <summary>
    /// Resolves a format name, a file extension, or a MIME type to a codec.
    /// Accepts what a person would type: <c>docx</c>, <c>.docx</c>, <c>DOCX</c>,
    /// or the package content type.
    /// </summary>
    public static DocumentCodec? Resolve(DocumentCodecCatalog catalog, string token)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(token))
            return null;

        token = token.Trim();
        return catalog.FindByName(token)
            ?? catalog.FindByExtension(token)
            ?? catalog.FindByMimeType(token)
            ?? AliasFor(catalog, token);
    }

    /// <summary>Every spelling <see cref="Resolve"/> accepts, for help and error text.</summary>
    public static IEnumerable<string> FormatNames(DocumentCodecCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Codecs.Select(codec => codec.Descriptor.Name.ToLowerInvariant());
    }

    /// <summary>
    /// The read-only PDF codec with the password this run was given, for the
    /// reads that reach it with the shared options every format takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A password is a PDF read option, and it has to stay one: the other codecs
    /// have no use for it, and <c>DocumentReadOptions</c> has been the place
    /// format-specific settings went to linger before. But this tool reads through
    /// the catalog with one option object for whatever format the file turns out
    /// to be, and handing every codec <see cref="PdfReadOptions"/> would have the
    /// others refuse it. So the composition root, which knows the password, gives
    /// it to the one codec that takes it.
    /// </para>
    /// <para>
    /// It wraps the read-only codec rather than the codec itself, so writing is
    /// refused by the same object as before. The PDF codec maps the shared
    /// options onto its own the same way when none are given - limits and
    /// resource policy - so a read through here differs from one without it by
    /// the credentials and nothing else.
    /// </para>
    /// </remarks>
    private sealed class PasswordedPdfCodec(DocumentCodec inner, PdfDecryptionCredentials credentials) : DocumentCodec(inner.Descriptor)
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanWrite => inner.CanWrite;

        public override DocumentProbeResult Probe(DocumentProbeRequest request) => inner.Probe(request);

        public override DocumentReadResult Read(Stream source, DocumentReadOptions? options = null) =>
            inner.Read(source, WithCredentials(options));

        public override DocumentReadResult Read(DocumentReadRequest request) =>
            inner.Read(new DocumentReadRequest(request.Input, WithCredentials(request.Options), request.CancellationToken));

        public override ValueTask<DocumentReadResult> ReadAsync(DocumentReadRequest request) =>
            inner.ReadAsync(new DocumentReadRequest(request.Input, WithCredentials(request.Options), request.CancellationToken));

        public override DocumentWriteResult Write(RichTextDocument document, Stream destination, DocumentWriteOptions? options = null) =>
            inner.Write(document, destination, options);

        private DocumentReadOptions WithCredentials(DocumentReadOptions? options) => options switch
        {
            null => PdfReadOptions.Default.WithCredentials(credentials),
            PdfReadOptions { Credentials: null } typed => typed.WithCredentials(credentials),
            PdfReadOptions typed => typed,
            _ when options.GetType() == typeof(DocumentReadOptions) =>
                new PdfReadOptions(options.Limits, resourcePolicy: options.ResourcePolicy).WithCredentials(credentials),
            _ => options,
        };
    }

    private static DocumentCodec? AliasFor(DocumentCodecCatalog catalog, string token)
    {
        // Extension lookup already normalizes a bare word to ".word", so "docx",
        // "md", and "htm" all resolve without help. Only genuine aliases - a name
        // for the format that is not one of its own extensions - belong here.
        string alias = token.ToLowerInvariant() switch
        {
            "word" => ".docx",
            "openxml" => ".docx",
            "commonmark" => ".md",
            "odf" => ".odt",
            "opendocument" => ".odt",
            _ => string.Empty,
        };

        return alias.Length == 0 ? null : catalog.FindByExtension(alias);
    }
}
