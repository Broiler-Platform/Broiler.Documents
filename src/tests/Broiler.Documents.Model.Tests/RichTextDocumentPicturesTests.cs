using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Model.Tests;

public sealed class RichTextDocumentPicturesTests
{
    [Fact]
    public void Rewrites_All_References_And_Preserves_New_Layout_Properties()
    {
        InlineImage original = Picture(1);
        InlineImage replacement = original.WithResource(BImageResource.FromPixels(new BPixelBuffer(1, 1, [2, 0, 0, 255])));
        RichTextParagraph picture = RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = original })
            .WithMarkStyle(InlineStyle.Default with { Bold = true });
        var table = new DocumentTable(0, 1, [new TableRow([new TableCell(0, 1, 0)])]);
        var footnotes = new SectionFootnotes(3, ListNumberFormat.UpperRoman);
        var note = new DocumentNote(NoteKind.Footnote, [picture], "*") { Tables = [table], Section = footnotes };
        var fill = ShapeFill.Solid(BColor.FromArgb(255, 1, 2, 3)) with { Image = new ImageFill(original, ImageFillRepeat.Single) };
        (double X, double Y)[] polygon = [(0, 0), (1, 0), (1, 1)];
        var shape = new DocumentShape(0, 4, 5, 80, 40, fill: fill, paragraphs: [picture], image: original, tables: [table])
        {
            WrapMargins = new TableCellPadding(1, 2, 3, 4),
            WrapPolygon = polygon,
            RotationRadians = 0.75,
            OffsetFromBandParagraph = 6,
            IsTextFrame = true,
        };
        var running = RunningContent.Empty.WithHeader(PageSelection.Even, [picture], [shape], [table])
            .WithDifferentFirstPage(true);
        var styles = new PageStyleSequence([new PageStyle(PageGeometry.A4, running, fill)], 0);
        var section = new DocumentSection(0, 2, 2, 20, runningContent: running, pageStyles: styles, pageNumberStart: 5)
        {
            PageNumberFormat = ListNumberFormat.UpperRoman,
            ColumnSeparator = true,
            FootnotesEnding = [footnotes],
        };
        RichTextDocument document = RichTextDocument.FromParagraphs([
            picture,
            RichTextParagraph.Create(DocumentNote.PlaceholderText, InlineStyle.Default with { Note = note }),
        ]).WithRunningContent(running).WithSections([section]).WithShapes([shape]).WithTables([table])
            .WithPageBackground(fill).WithPageGeometry(PageGeometry.A4).WithShowsRevisions(false)
            .WithNoteNumbering(new NoteNumbering(FootnoteStart: 3) { EndnoteRunningContent = running });

        int callbacks = 0;
        RichTextDocument mapped = document.MapPictures(image =>
        {
            callbacks++;
            Assert.Same(original, image);
            return replacement;
        });

        Assert.Equal(1, callbacks);
        Assert.Equal(document.PlainText, mapped.PlainText);
        Assert.True(mapped.Paragraphs[0].MarkStyle.Bold);
        Assert.Same(replacement, mapped.Paragraphs[0].StyleAt(0).Image);
        Assert.Same(table, Assert.Single(mapped.Tables));
        Assert.Same(document.PageGeometry, mapped.PageGeometry);
        Assert.False(mapped.ShowsRevisions);
        Assert.Same(replacement, mapped.PageBackground!.Image!.Picture);
        Assert.Equal(ImageFillRepeat.Single, mapped.PageBackground.Image.Repeat);

        DocumentSection mappedSection = Assert.Single(mapped.Sections);
        Assert.Equal(5, mappedSection.PageNumberStart);
        Assert.Equal(ListNumberFormat.UpperRoman, mappedSection.PageNumberFormat);
        Assert.True(mappedSection.ColumnSeparator);
        Assert.Same(footnotes, Assert.Single(mappedSection.FootnotesEnding));
        Assert.Same(mapped.RunningContent, mappedSection.RunningContent);
        Assert.Same(mapped.RunningContent, mapped.NoteNumbering.EndnoteRunningContent);
        Assert.Equal(3, mapped.NoteNumbering.FootnoteStart);
        PageStyle mappedPageStyle = Assert.Single(mappedSection.PageStyles!.Styles);
        Assert.Same(mapped.RunningContent, mappedPageStyle.RunningContent);
        Assert.Same(replacement, mappedPageStyle.Background!.Image!.Picture);
        Assert.True(mapped.RunningContent.DifferentFirstPage);
        Assert.Same(table, Assert.Single(mapped.RunningContent.EffectiveHeaderTables(PageSelection.Even)));
        Assert.Same(replacement, Assert.Single(mapped.RunningContent.Header(PageSelection.Even)).StyleAt(0).Image);

        DocumentShape mappedShape = Assert.Single(mapped.Shapes);
        Assert.Same(mappedShape, Assert.Single(mapped.RunningContent.HeaderShapes(PageSelection.Even)));
        Assert.Equal(shape.WrapMargins, mappedShape.WrapMargins);
        Assert.Same(polygon, mappedShape.WrapPolygon);
        Assert.Equal(shape.RotationRadians, mappedShape.RotationRadians);
        Assert.Equal(shape.OffsetFromBandParagraph, mappedShape.OffsetFromBandParagraph);
        Assert.True(mappedShape.IsTextFrame);
        Assert.Same(table, Assert.Single(mappedShape.Tables));
        Assert.Same(replacement, mappedShape.Image);
        Assert.Same(replacement, mappedShape.Fill!.Image!.Picture);
        Assert.Same(replacement, Assert.Single(mappedShape.Paragraphs).StyleAt(0).Image);

        DocumentNote mappedNote = Assert.Single(mapped.Notes);
        Assert.Equal("*", mappedNote.CustomMark);
        Assert.Same(footnotes, mappedNote.Section);
        Assert.Same(table, Assert.Single(mappedNote.Tables));
        Assert.Same(replacement, Assert.Single(mappedNote.Paragraphs).StyleAt(0).Image);
        Assert.Same(original, document.Paragraphs[0].StyleAt(0).Image);
        Assert.Same(original, shape.Image);
        Assert.Same(note, Assert.Single(document.Notes));
    }

    [Fact]
    public void No_Changes_Return_The_Same_Snapshot_And_Replacement_Normalizes_Runs()
    {
        InlineImage first = Picture(1);
        InlineImage second = Picture(2);
        RichTextParagraph paragraph = RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = first })
            .InsertText(1, InlineImage.PlaceholderText, InlineStyle.Default with { Image = second });
        var document = RichTextDocument.FromParagraphs([paragraph]);

        Assert.Same(document, document.MapPictures(image => image));

        RichTextDocument mapped = document.MapPictures(_ => first);
        StyleRun run = Assert.Single(mapped.Paragraphs[0].Runs);
        Assert.Equal(2, run.Length);
        Assert.Same(first, run.Style.Image);
        Assert.Equal(2, paragraph.Runs.Count);
    }

    [Fact]
    public void Replacing_The_Payload_Keeps_Presentation_And_Clears_The_Old_Resource_Id()
    {
        var presentation = new ImagePresentation { Mask = ImageMask.Ellipse };
        InlineImage picture = new InlineImage(Picture(1).Resource, new DocumentResourceId("source", "picture"),
            width: 12, height: 8, altText: "Logo", name: "Logo image", presentation: presentation, marksMissingPayload: false);

        InlineImage replacement = picture.WithResource(Picture(2).Resource);

        Assert.True(replacement.ResourceId.IsNone);
        Assert.Equal(picture.Width, replacement.Width);
        Assert.Equal(picture.Height, replacement.Height);
        Assert.Equal(picture.Name, replacement.Name);
        Assert.Equal(picture.AltText, replacement.AltText);
        Assert.Same(presentation, replacement.Presentation);
        Assert.False(replacement.MarksMissingPayload);
    }

    private static InlineImage Picture(byte red) =>
        new(BImageResource.FromPixels(new BPixelBuffer(1, 1, [red, 0, 0, 255])));
}
