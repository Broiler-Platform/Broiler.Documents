using System;
using System.Collections.Generic;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Rendering;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// Encodes as PNG the pictures a read left as decoded samples, so the writer a
/// document goes to has bytes to write.
/// </summary>
/// <remarks>
/// <para>
/// A PDF read gives its pictures to the model as samples. The page drew each
/// one through a filter, a colour space and perhaps a mask, and the decoded
/// result is what arrives. Every writer needs an encoding, and the resource gate
/// will not make one up for a writer: the bytes would not be the document's, and
/// the format would be that writer's guess. It leaves the choice to the caller.
/// A conversion is what this tool was asked for, and a picture silently missing
/// from the output would be the worse result.
/// </para>
/// <para>
/// PNG, because it is lossless: the samples written are the samples read,
/// transparency included. Only where the conversion permits
/// <see cref="DocumentResourceOperations.Transform"/> for the picture, since a
/// re-encoding is one. A policy that withheld it is honoured by leaving the
/// picture as it was, for the writer to report.
/// </para>
/// <para>
/// The encoded picture is a new payload, so it is admitted into the conversion
/// it came from rather than borrowing the decoded one's approval: the same
/// namespace, a fresh id, the provenance the original was read with, and the
/// policy this tool reads with.
/// </para>
/// <para>
/// Only the body is searched. It is where a PDF read puts its pictures, and no
/// other reader produces decoded samples.
/// </para>
/// </remarks>
internal static class PictureEncoding
{
    /// <summary>The media type every encoding here produces.</summary>
    public const string MediaType = "image/png";

    /// <summary>
    /// Returns <paramref name="document"/> with each decoded picture in its body
    /// encoded as PNG, the conversion context that admits the encodings, and how
    /// many distinct pictures were encoded.
    /// </summary>
    public static (RichTextDocument Document, DocumentConversionContext? Resources, int Encoded) EncodeDecodedPictures(
        RichTextDocument document,
        DocumentConversionContext? resources)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (resources is null)
            return (document, resources, 0);

        DocumentConversionContextBuilder? builder = null;
        var encoded = new Dictionary<InlineImage, InlineImage>(ReferenceEqualityComparer.Instance);
        RichTextParagraph[]? paragraphs = null;

        for (int index = 0; index < document.ParagraphCount; index++)
        {
            // The runs of the paragraph as it was. An encoded picture takes the
            // place of a decoded one character for character, so the offsets
            // stay good while the paragraph is rebuilt around them.
            RichTextParagraph paragraph = document.Paragraphs[index];
            int offset = 0;
            foreach (StyleRun run in document.Paragraphs[index].Runs)
            {
                if (run.Style.Image is InlineImage image && IsEncodable(image, resources))
                {
                    if (!encoded.TryGetValue(image, out InlineImage? replacement))
                    {
                        builder ??= DocumentConversionContextBuilder.Continuing(
                            resources,
                            DocumentResourcePolicy.AllowOwnDocuments);
                        replacement = Encode(image, resources, builder);
                        encoded[image] = replacement;
                    }

                    string text = paragraph.Text.Substring(offset, run.Length);
                    paragraph = paragraph
                        .RemoveRange(offset, run.Length)
                        .InsertText(offset, text, run.Style with { Image = replacement });
                }

                offset += run.Length;
            }

            if (!ReferenceEquals(paragraph, document.Paragraphs[index]))
            {
                paragraphs ??= [.. document.Paragraphs];
                paragraphs[index] = paragraph;
            }
        }

        if (paragraphs is null)
            return (document, resources, 0);

        // The paragraph count is unchanged, so every table still spans the
        // paragraphs it did, and the rest of the document comes across whole.
        RichTextDocument rebuilt = RichTextDocument.FromParagraphs(paragraphs)
            .WithRunningContent(document.RunningContent)
            .WithShapes(document.Shapes)
            .WithPageGeometry(document.PageGeometry)
            .WithTables(document.Tables)
            .WithStyleDefaults(document.StyleDefaults);

        return (rebuilt, builder!.Build(), encoded.Count);
    }

    private static bool IsEncodable(InlineImage image, DocumentConversionContext resources) =>
        !image.TryGetEncoded(out _, out _) &&
        image.Resource.TryGetPixels(out _) &&
        resources.IsAllowed(image.ResourceId, DocumentResourceOperations.Transform, image.Resource);

    private static InlineImage Encode(
        InlineImage image,
        DocumentConversionContext resources,
        DocumentConversionContextBuilder builder)
    {
        image.Resource.TryGetPixels(out BPixelBuffer? pixels);
        CodecComposition.RegisterImageCodecs();

        byte[] png;
        using (var bitmap = new BBitmap(pixels!.Width, pixels.Height, (byte[])pixels.Rgba.Clone(), takeOwnership: true))
            png = DocumentRasterizer.EncodePng(bitmap);

        var picture = new InlineImage(
            BImageResource.FromEncoded(png, MediaType, pixels.Width, pixels.Height),
            default,
            image.Width,
            image.Height,
            image.AltText,
            image.Name,
            image.Presentation);

        resources.TryGetEntry(image.ResourceId, out DocumentResourceEntry? entry);
        return builder.AdmitImage(picture, entry!.Provenance, entry.Disposition);
    }
}
