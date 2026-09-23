using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Docx;
using Broiler.Documents.Html;
using Broiler.Documents.Markdown;
using Broiler.Documents.Odt;
using Broiler.Documents.Pdf;
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
/// the one surface an automated system would come to depend on. Its optional
/// font and image providers are not composed, so the codec reads with its base
/// capability and says what it skipped.
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
    public static DocumentCodecCatalog CreateCatalog() =>
        new([
            new DocxDocumentCodec(),
            new OdtDocumentCodec(),
            new RtfDocumentCodec(),
            new HtmlDocumentCodec(),
            new MarkdownDocumentCodec(),
            new ReadOnlyCodec(new PdfDocumentCodec()),
        ]);

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
