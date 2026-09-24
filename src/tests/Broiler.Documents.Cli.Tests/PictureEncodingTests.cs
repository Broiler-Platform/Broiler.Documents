using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Resources;
using Broiler.Graphics.Imaging;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Covers the step that gives a picture held as decoded samples - what a PDF
/// read produces - the encoded bytes every writer needs.
/// </summary>
/// <remarks>
/// The assertions decode what was written and compare samples, because the
/// promise is losslessness: a picture that arrived but changed on the way would
/// pass a test that only counted pictures.
/// </remarks>
public sealed class PictureEncodingTests
{
    // Opaque red and a half-transparent blue: the second is what a masked logo
    // is made of, and straight alpha is what has to survive.
    private static readonly byte[] Samples = [255, 0, 0, 255, 0, 64, 255, 128];

    [Fact]
    public void A_Decoded_Picture_Is_Written_As_Png_Without_Losing_A_Sample()
    {
        (RichTextDocument document, DocumentConversionContext resources) = DocumentWith(DocumentResourcePolicy.AllowOwnDocuments);

        (RichTextDocument encoded, DocumentConversionContext? admitted, int count) =
            PictureEncoding.EncodeDecodedPictures(document, resources);

        Assert.Equal(1, count);
        InlineImage picture = Assert.Single(ImagesIn(encoded));
        Assert.True(picture.TryGetEncoded(out ReadOnlyMemory<byte> png, out string? contentType));
        Assert.Equal("image/png", contentType);

        CodecComposition.RegisterImageCodecs();
        using BBitmap decoded = BBitmap.Decode(png.Span);
        Assert.Equal(Samples, decoded.ToPixelBuffer().Rgba);

        // What the picture was drawn at and called, and the text around it,
        // are the picture's own and stay.
        Assert.Equal(24.0, picture.Width);
        Assert.Equal(12.0, picture.Height);
        Assert.Equal("logo", picture.AltText);
        Assert.Equal(document.PlainText, encoded.PlainText);
        Assert.True(encoded.Paragraphs[0].StyleAt(0).Bold);
    }

    [Fact]
    public void The_Encoding_Is_Admitted_Afresh_Into_The_Same_Conversion()
    {
        // A new payload does not borrow the decoded picture's approval: it has
        // its own id, beside the original's, and that id permits the write.
        (RichTextDocument document, DocumentConversionContext resources) = DocumentWith(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage original = Assert.Single(ImagesIn(document));

        (RichTextDocument encoded, DocumentConversionContext? admitted, _) =
            PictureEncoding.EncodeDecodedPictures(document, resources);

        InlineImage picture = Assert.Single(ImagesIn(encoded));
        Assert.NotEqual(original.ResourceId, picture.ResourceId);
        Assert.Equal(resources.Namespace, admitted!.Namespace);
        Assert.True(admitted.IsAllowed(picture.ResourceId, DocumentResourceOperations.ByteTransfer, picture.Resource));
        Assert.True(admitted.TryGetEntry(original.ResourceId, out _));
    }

    [Fact]
    public void A_Picture_Used_Twice_Is_Encoded_Once()
    {
        (RichTextDocument document, DocumentConversionContext resources) = DocumentWith(DocumentResourcePolicy.AllowOwnDocuments, twice: true);

        (RichTextDocument encoded, _, int count) = PictureEncoding.EncodeDecodedPictures(document, resources);

        Assert.Equal(1, count);
        List<InlineImage> pictures = ImagesIn(encoded);
        Assert.Equal(2, pictures.Count);
        Assert.Same(pictures[0], pictures[1]);
    }

    [Fact]
    public void Without_Leave_To_Transform_The_Picture_Is_Left_For_The_Writer_To_Report()
    {
        // The read default puts a picture in the model and grants nothing that
        // takes it out again. Re-encoding is a transformation, so the picture
        // stays as it was, and the writer says why it was not written.
        (RichTextDocument document, DocumentConversionContext resources) = DocumentWith(DocumentResourcePolicy.Default);

        (RichTextDocument encoded, DocumentConversionContext? admitted, int count) =
            PictureEncoding.EncodeDecodedPictures(document, resources);

        Assert.Equal(0, count);
        Assert.Same(document, encoded);
        Assert.Same(resources, admitted);
    }

    [Fact]
    public void A_Picture_That_Already_Has_Bytes_Is_Left_As_It_Is()
    {
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage png = builder.AdmitImage(
            new InlineImage(BImageResource.FromEncoded(new byte[] { 1, 2, 3 }, "image/png", 1, 1)),
            DocumentResourceProvenance.ReadFromSource,
            DocumentResourceDisposition.Embedded);
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create(InlineImage.PlaceholderText, InlineStyle.Default with { Image = png })]);

        (RichTextDocument encoded, _, int count) = PictureEncoding.EncodeDecodedPictures(document, builder.Build());

        Assert.Equal(0, count);
        Assert.Same(png, Assert.Single(ImagesIn(encoded)));
    }

    /// <summary>
    /// "Logo: " in bold, then a decoded two-pixel picture, admitted under
    /// <paramref name="policy"/> as a read would admit it.
    /// </summary>
    private static (RichTextDocument Document, DocumentConversionContext Resources) DocumentWith(
        DocumentResourcePolicy policy,
        bool twice = false)
    {
        var builder = new DocumentConversionContextBuilder(policy);
        InlineImage picture = builder.AdmitImage(
            new InlineImage(
                BImageResource.FromPixels(new BPixelBuffer(2, 1, (byte[])Samples.Clone())),
                width: 24,
                height: 12,
                altText: "logo"),
            DocumentResourceProvenance.ReadFromSource,
            DocumentResourceDisposition.Embedded);

        RichTextParagraph paragraph = RichTextParagraph.Create("Logo: ", InlineStyle.Default with { Bold = true });
        paragraph = paragraph.InsertText(paragraph.Length, InlineImage.PlaceholderText, InlineStyle.Default with { Image = picture });
        if (twice)
        {
            paragraph = paragraph.InsertText(paragraph.Length, " and ", InlineStyle.Default);
            paragraph = paragraph.InsertText(paragraph.Length, InlineImage.PlaceholderText, InlineStyle.Default with { Image = picture });
        }

        return (RichTextDocument.FromParagraphs([paragraph]), builder.Build());
    }

    private static List<InlineImage> ImagesIn(RichTextDocument document) =>
        [.. document.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Style.Image)
            .OfType<InlineImage>()];
}
