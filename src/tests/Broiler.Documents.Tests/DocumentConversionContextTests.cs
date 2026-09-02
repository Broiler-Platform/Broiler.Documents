using System;
using Broiler.Documents.Model;
using Broiler.Graphics;
using Xunit;

namespace Broiler.Documents.Tests;

/// <summary>
/// Covers the conversion context: what an id is worth on its own, what happens
/// when a resource crosses into another conversion, and the two ways a writer's
/// request for bytes can fail.
/// </summary>
public sealed class DocumentConversionContextTests
{
    private static BImageResource Pixels(int width, int height, byte tint = 0x40) =>
        BImageResource.FromPixels(new BPixelBuffer(width, height, Fill(width, height, tint)));

    private static byte[] Fill(int width, int height, byte tint)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = tint;
            rgba[i + 1] = tint;
            rgba[i + 2] = tint;
            rgba[i + 3] = 255;
        }

        return rgba;
    }

    private static DocumentResourceRequest Request(
        BImageResource resource,
        DocumentResourceProvenance provenance = DocumentResourceProvenance.ReadFromSource,
        DocumentResourceDisposition disposition = DocumentResourceDisposition.Embedded) =>
        new(resource, provenance, disposition);

    [Fact(Timeout = 600000)]
    public void An_Id_Alone_Does_Not_Authorize_Anything()
    {
        // The property the whole design rests on. An entry is bound to the
        // payload it was approved for, so presenting a real id with different
        // bytes borrows nothing.
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        BImageResource approved = Pixels(4, 4, 0x10);
        DocumentResourceEntry entry = builder.Admit(Request(approved));
        DocumentConversionContext context = builder.Build();

        Assert.True(context.IsAllowed(entry.Id, DocumentResourceOperations.ByteTransfer, approved));
        Assert.False(context.IsAllowed(entry.Id, DocumentResourceOperations.ByteTransfer, Pixels(4, 4, 0x20)));
        Assert.Contains("not the one that was approved", context.ExplainDenial(
            entry.Id,
            DocumentResourceOperations.ByteTransfer,
            Pixels(4, 4, 0x20)), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void An_Id_From_Another_Conversion_Matches_Nothing()
    {
        // What makes "authorization never transfers automatically" a property of
        // the type rather than a rule to remember: paste a picture into another
        // document and its id names nothing there.
        BImageResource resource = Pixels(3, 3);

        var first = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        DocumentResourceEntry entry = first.Admit(Request(resource));

        var second = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        second.Admit(Request(resource));
        DocumentConversionContext destination = second.Build();

        Assert.False(destination.IsAllowed(entry.Id, DocumentResourceOperations.ByteTransfer, resource));
        Assert.Contains("no entry for", destination.ExplainDenial(
            entry.Id,
            DocumentResourceOperations.ByteTransfer,
            resource), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void The_Same_Payload_Is_Admitted_Once()
    {
        // Asking a policy twice about identical bytes invites two answers inside
        // one document, and a picture permitted in one place and denied in
        // another is not something a reader can reason about.
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);

        DocumentResourceEntry first = builder.Admit(Request(Pixels(5, 5)));
        DocumentResourceEntry second = builder.Admit(Request(Pixels(5, 5)));

        Assert.Equal(first.Id, second.Id);
        Assert.Single(builder.Build().Entries);
    }

    [Fact(Timeout = 600000)]
    public void Unknown_Provenance_Denies_Even_Under_A_Permissive_Policy()
    {
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);

        DocumentResourceEntry entry = builder.Admit(
            Request(Pixels(2, 2), DocumentResourceProvenance.Unknown));

        Assert.Equal(DocumentResourceOperations.None, entry.Permitted);
    }

    [Fact(Timeout = 600000)]
    public void Reading_Into_The_Model_Is_Not_Permission_To_Write()
    {
        // The roadmap's asymmetry, which is why the read default is not the write
        // default: acceptance by a reader grants no later writer authorization.
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.Default);
        DocumentResourceEntry entry = builder.Admit(Request(Pixels(2, 2)));

        Assert.True(entry.Allows(DocumentResourceOperations.ExtractToModel));
        Assert.False(entry.Allows(DocumentResourceOperations.ByteTransfer));
        Assert.False(entry.Allows(DocumentResourceOperations.Redistribute));
    }

    [Fact(Timeout = 600000)]
    public void An_Empty_Context_Permits_Nothing()
    {
        BImageResource resource = Pixels(2, 2);
        var id = new DocumentResourceId("somewhere", "1");

        Assert.False(DocumentConversionContext.Empty.IsAllowed(
            id,
            DocumentResourceOperations.SemanticProjection,
            resource));
    }

    [Fact(Timeout = 600000)]
    public void A_Continued_Context_Keeps_Its_Ids_And_Mints_Past_Them()
    {
        // An edit is one conversion with two sources of resources. Re-minting
        // would invalidate every image already in the model to admit one.
        var first = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        DocumentResourceEntry original = first.Admit(Request(Pixels(6, 6, 0x11)));
        DocumentConversionContext context = first.Build();

        DocumentConversionContextBuilder continued = DocumentConversionContextBuilder.Continuing(
            context,
            DocumentResourcePolicy.AllowOwnDocuments);
        DocumentResourceEntry added = continued.Admit(Request(Pixels(6, 6, 0x22)));
        DocumentConversionContext combined = continued.Build();

        Assert.Equal(context.Namespace, combined.Namespace);
        Assert.NotEqual(original.Id, added.Id);
        Assert.True(combined.TryGetEntry(original.Id, out _));
        Assert.True(combined.TryGetEntry(added.Id, out _));
        Assert.Equal(2, combined.Entries.Count);
    }

    [Fact(Timeout = 600000)]
    public void The_Gate_Tells_A_Refusal_Apart_From_An_Absent_Encoding()
    {
        // Both drop the picture and only one is about permission. A host that
        // cannot tell them apart cannot fix either.
        var builder = new DocumentConversionContextBuilder(DocumentResourcePolicy.AllowOwnDocuments);
        InlineImage decoded = builder.AdmitImage(
            new InlineImage(Pixels(4, 4)),
            DocumentResourceProvenance.ReadFromSource,
            DocumentResourceDisposition.Embedded);
        DocumentConversionContext permitted = builder.Build();

        Assert.False(DocumentResourceGate.TryTakeEncodedBytes(
            decoded,
            permitted,
            DocumentResourceOperations.ByteTransfer,
            out _,
            out _,
            out string? noEncoding));
        Assert.Contains("decoded samples", noEncoding!, StringComparison.Ordinal);

        var unadmitted = new InlineImage(Pixels(4, 4));
        Assert.False(DocumentResourceGate.TryTakeEncodedBytes(
            unadmitted,
            permitted,
            DocumentResourceOperations.ByteTransfer,
            out _,
            out _,
            out string? refused));
        Assert.Contains("no context id", refused!, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void An_Image_Resolves_Its_Display_Size_From_The_Resource()
    {
        // 96 pixels per inch into 72 points per inch: a 96-pixel picture is one
        // inch, which is 72 points.
        var auto = new InlineImage(Pixels(96, 48));
        Assert.True(auto.TryGetDisplaySize(out double width, out double height));
        Assert.Equal(72, width, 3);
        Assert.Equal(36, height, 3);

        // One stated dimension keeps the intrinsic aspect ratio.
        InlineImage half = auto.WithSize(36, null);
        Assert.True(half.TryGetDisplaySize(out double halfWidth, out double halfHeight));
        Assert.Equal(36, halfWidth, 3);
        Assert.Equal(18, halfHeight, 3);
    }

    [Fact(Timeout = 600000)]
    public void An_Unplaceable_Image_Reports_Rather_Than_Defaulting()
    {
        // Bytes no codec recognizes: the picture is still carried, and the size
        // question has no answer rather than a made-up one.
        var image = new InlineImage(
            new byte[] { 9, 9, 9, 9 },
            "application/octet-stream",
            0,
            0);

        Assert.False(image.TryGetDisplaySize(out _, out _));
        Assert.False(image.HasExplicitSize);
    }

    [Fact(Timeout = 600000)]
    public void A_Dimension_Is_Positive_Finite_Or_Absent()
    {
        BImageResource resource = Pixels(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => new InlineImage(resource, width: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InlineImage(resource, width: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InlineImage(resource, height: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InlineImage(resource, height: double.PositiveInfinity));
    }
}
