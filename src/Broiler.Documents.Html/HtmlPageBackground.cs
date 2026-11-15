using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;
using Broiler.Dom;
using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Html;

/// <summary>
/// The picture an element paints behind the page: its CSS
/// <c>background-image</c> (or the <c>url()</c> in its <c>background</c>
/// shorthand), or failing that its HTML <c>background</c> attribute, which
/// HTML maps to the same property (HTML §15.3.1, <c>body background</c>).
/// </summary>
/// <remarks>
/// LibreOffice sets a body's background picture as the page style's
/// background and tiles it over the whole page from its top-left corner, at
/// the picture's own size in CSS pixels (PageAndParagraphFilled.html: a
/// 158 x 57 pixel picture repeats every 118.5 by 42.75 points from the page's
/// corner). <c>background-repeat: no-repeat</c> draws it once. The picture is
/// read as an <c>img</c>'s would be: from a <c>data:</c> URI, or through the
/// host's resolver where it allows local files.
/// </remarks>
internal static class HtmlPageBackground
{
    public static ShapeFill? Picture(DomElement? element, HtmlStyleSheet styleSheet,
        Func<string, ReadOnlyMemory<byte>?>? resolve, DocumentLimits limits, DocumentConversionContextBuilder resources)
    {
        if (element is null)
            return null;

        IReadOnlyDictionary<string, string> declarations = styleSheet.DeclarationsFor(element);
        string? source = Url(declarations.GetValueOrDefault("background-image")) ??
            Url(declarations.GetValueOrDefault("background")) ??
            element.GetAttribute("background")?.Trim();
        if (string.IsNullOrWhiteSpace(source))
            return null;

        ReadOnlyMemory<byte> data;
        string? mediaType;
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (!DataUriParser.TryParse(source, out string parsedType, out data, out _))
                return null;
            mediaType = parsedType;
        }
        else if (resolve?.Invoke(source) is ReadOnlyMemory<byte> resolved)
        {
            data = resolved;
            mediaType = DocumentImageFormats.ContentTypeForSignature(data.Span);
        }
        else
        {
            return null;
        }

        if (string.IsNullOrEmpty(mediaType) || data.Length == 0 || data.Length > limits.MaxBinBytes)
            return null;

        BImageResource resource;
        try
        {
            resource = BImageResource.FromEncoded(data, mediaType);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var request = new DocumentResourceRequest(resource, DocumentResourceProvenance.ReadFromSource,
            source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? DocumentResourceDisposition.Embedded
                : DocumentResourceDisposition.Linked,
            name: "background", sourceFormat: "HTML");
        if (!resources.TryAdmit(request, DocumentResourceOperations.ExtractToModel, out DocumentResourceId id, out _))
            return null;

        (double width, double height) = HtmlImagePixels.Read(data.Span) is (int pixelsWide, int pixelsHigh)
            ? (pixelsWide * 72.0 / 96.0, pixelsHigh * 72.0 / 96.0)
            : (0, 0);
        string repeat = declarations.GetValueOrDefault("background-repeat") ??
            declarations.GetValueOrDefault("background") ?? string.Empty;
        var picture = new InlineImage(resource, id, width > 0 ? width : null, height > 0 ? height : null, name: "background");
        return ShapeFill.Solid(BColor.Empty) with
        {
            Image = new ImageFill(picture,
                repeat.Contains("no-repeat", StringComparison.OrdinalIgnoreCase) ? ImageFillRepeat.Single : ImageFillRepeat.Tile,
                width, height, AlignX: 0, AlignY: 0),
        };
    }

    /// <summary>The address in a CSS <c>url(...)</c>, unquoted; null where there is none.</summary>
    private static string? Url(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        int start = value.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;
        int end = value.IndexOf(')', start + 4);
        if (end < 0)
            return null;
        string url = value[(start + 4)..end].Trim().Trim('\'', '"').Trim();
        return url.Length == 0 ? null : url;
    }
}
