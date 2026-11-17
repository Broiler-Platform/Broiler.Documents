using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model;

public sealed partial class RichTextDocument
{
    /// <summary>
    /// Copies this document with its pictures replaced by <paramref name="map"/>,
    /// including pictures in running content, notes, shapes, and fills.
    /// </summary>
    /// <remarks>
    /// Each distinct picture instance is mapped once. Return the original picture
    /// to keep it. Paragraph coordinates, formatting, and layout metadata are
    /// preserved, and an unchanged mapping returns this document. The caller
    /// owns resource policy decisions and the provenance of any replacement.
    /// </remarks>
    public RichTextDocument MapPictures(Func<InlineImage, InlineImage> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var pictures = new Dictionary<InlineImage, InlineImage>(ReferenceEqualityComparer.Instance);
        var notes = new Dictionary<DocumentNote, DocumentNote>(ReferenceEqualityComparer.Instance);
        var runningSets = new Dictionary<RunningContent, RunningContent>(ReferenceEqualityComparer.Instance);
        var shapes = new Dictionary<DocumentShape, DocumentShape>(ReferenceEqualityComparer.Instance);
        PageSelection[] selections = Enum.GetValues<PageSelection>();

        var paragraphs = MapItems(Paragraphs, MapParagraph);
        var running = MapRunning(RunningContent);
        var bodyShapes = MapItems(Shapes, MapShape);
        var sections = MapItems(Sections, MapSection);
        var background = MapFill(PageBackground);
        NoteNumbering numbering = NoteNumbering;
        if (numbering.EndnoteRunningContent is { } endnoteContent)
        {
            RunningContent mapped = MapRunning(endnoteContent);
            if (!ReferenceEquals(mapped, endnoteContent))
                numbering = numbering with { EndnoteRunningContent = mapped };
        }

        if (ReferenceEquals(paragraphs, Paragraphs) && ReferenceEquals(running, RunningContent) &&
            ReferenceEquals(bodyShapes, Shapes) && ReferenceEquals(sections, Sections) &&
            ReferenceEquals(background, PageBackground) && ReferenceEquals(numbering, NoteNumbering))
            return this;

        return new RichTextDocument(paragraphs, running, bodyShapes, PageGeometry, Tables, StyleDefaults,
            sections, background, numbering, ShowsRevisions);

        InlineImage? MapImage(InlineImage? image)
        {
            if (image is null)
                return null;
            if (!pictures.TryGetValue(image, out InlineImage? mapped))
            {
                mapped = map(image) ?? throw new InvalidOperationException("A picture replacement must return a picture.");
                pictures.Add(image, mapped);
            }
            return mapped;
        }

        InlineStyle MapStyle(InlineStyle style) => style with
        {
            Image = MapImage(style.Image),
            Note = style.Note is { } note ? MapNote(note) : null,
        };

        RichTextParagraph MapParagraph(RichTextParagraph paragraph) => paragraph.MapInlineStyles(MapStyle);

        DocumentNote MapNote(DocumentNote source)
        {
            if (notes.TryGetValue(source, out DocumentNote? mapped))
                return mapped;

            // A malformed model can refer to an in-progress note.
            notes.Add(source, source);
            mapped = source.WithMappedPictures(MapItems(source.Paragraphs, MapParagraph));
            notes[source] = mapped;
            return mapped;
        }

        DocumentShape MapShape(DocumentShape source)
        {
            if (!shapes.TryGetValue(source, out DocumentShape? mapped))
            {
                mapped = source.WithMappedPictures(MapImage(source.Image), MapFill(source.Fill),
                    MapItems(source.Paragraphs, MapParagraph));
                shapes.Add(source, mapped);
            }
            return mapped;
        }

        ShapeFill? MapFill(ShapeFill? fill)
        {
            if (fill?.Image is not { } imageFill)
                return fill;

            InlineImage image = MapImage(imageFill.Picture)!;
            return ReferenceEquals(image, imageFill.Picture) ? fill
                : fill with { Image = imageFill with { Picture = image } };
        }

        RunningContent MapRunning(RunningContent source)
        {
            if (runningSets.TryGetValue(source, out RunningContent? cached))
                return cached;

            RunningContent mapped = source;
            foreach (PageSelection selection in selections)
            {
                MapBand(selection, isHeader: true, source.Header(selection), source.HeaderShapes(selection));
                MapBand(selection, isHeader: false, source.Footer(selection), source.FooterShapes(selection));
            }
            runningSets.Add(source, mapped);
            return mapped;

            void MapBand(PageSelection selection, bool isHeader, IReadOnlyList<RichTextParagraph> text,
                IReadOnlyList<DocumentShape> bandShapes)
            {
                var mappedText = MapItems(text, MapParagraph);
                var mappedShapes = MapItems(bandShapes, MapShape);
                if (!ReferenceEquals(mappedText, text) || !ReferenceEquals(mappedShapes, bandShapes))
                    mapped = mapped.WithMappedPictures(selection, isHeader, mappedText, mappedShapes);
            }
        }

        DocumentSection MapSection(DocumentSection section)
        {
            RunningContent? content = section.RunningContent is { } original ? MapRunning(original) : null;
            PageStyleSequence? styles = section.PageStyles;
            if (styles is not null)
            {
                var updated = MapItems(styles.Styles, MapPageStyle);
                if (!ReferenceEquals(updated, styles.Styles))
                    styles = new PageStyleSequence(updated, styles.RepeatFrom);
            }
            return section.WithMappedPictures(content, styles);
        }

        PageStyle MapPageStyle(PageStyle style)
        {
            RunningContent mapped = MapRunning(style.RunningContent);
            ShapeFill? fill = MapFill(style.Background);
            return ReferenceEquals(mapped, style.RunningContent) && ReferenceEquals(fill, style.Background)
                ? style : style with { RunningContent = mapped, Background = fill };
        }
    }

    private static IReadOnlyList<T> MapItems<T>(IReadOnlyList<T> source, Func<T, T> map) where T : class
    {
        T[]? updated = null;
        for (int index = 0; index < source.Count; index++)
        {
            T original = source[index];
            T mapped = map(original);
            if (!ReferenceEquals(mapped, original))
            {
                updated ??= [.. source];
                updated[index] = mapped;
            }
        }
        return updated ?? source;
    }
}
