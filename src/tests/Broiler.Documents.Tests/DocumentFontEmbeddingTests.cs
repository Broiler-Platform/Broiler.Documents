using System;
using Broiler.Documents.Model;
using Broiler.Graphics;
using Xunit;

namespace Broiler.Documents.Tests;

/// <summary>
/// Covers the font embedding preflight: the caller's decision and the font's own
/// declaration, and why both are required.
/// </summary>
public sealed class DocumentFontEmbeddingTests
{
    private static DocumentFontResource Font(ushort fsType, string family = "Example Sans") =>
        new(new byte[] { 1, 2, 3, 4 }, family, BFontEmbeddingRights.FromFsType(fsType));

    /// <summary>A policy that grants embedding, so the font's own claim is what decides.</summary>
    private sealed class EmbeddingPolicy : DocumentResourcePolicy
    {
        public override DocumentResourceDecision Decide(DocumentResourceRequest request) =>
            new(DocumentResourceOperations.EmbedOrSubset | DocumentResourceOperations.Transform);
    }

    private static (DocumentResourceId Id, DocumentConversionContext Context) Admit(
        DocumentFontResource font,
        DocumentResourcePolicy? policy = null)
    {
        var builder = new DocumentConversionContextBuilder(policy ?? new EmbeddingPolicy());
        DocumentResourceEntry entry = builder.Admit(new DocumentResourceRequest(
            font,
            DocumentResourceProvenance.CallerSupplied,
            DocumentResourceDisposition.Embedded));

        return (entry.Id, builder.Build());
    }

    [Fact(Timeout = 600000)]
    public void An_Installable_Font_A_Caller_Approved_May_Be_Embedded()
    {
        DocumentFontResource font = Font(0);
        (DocumentResourceId id, DocumentConversionContext context) = Admit(font);

        Assert.True(DocumentFontEmbedding.MayEmbed(font, id, context, subsetting: true, out string? refusal));
        Assert.Null(refusal);
    }

    [Fact(Timeout = 600000)]
    public void A_Caller_Decision_Does_Not_Override_A_Restricted_Font()
    {
        // The policy says yes to everything it is asked about. The font still
        // says no, because a policy is written once for a conversion and the
        // declaration belongs to one particular font.
        DocumentFontResource font = Font(0x0002);
        (DocumentResourceId id, DocumentConversionContext context) = Admit(font);

        Assert.False(DocumentFontEmbedding.MayEmbed(font, id, context, subsetting: false, out string? refusal));
        Assert.Contains("restricted-licence", refusal!, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Permissive_Font_Without_A_Caller_Decision_Is_Refused()
    {
        // The other half. A font whose table permits everything is still not a
        // licence, so nothing may be embedded without the caller saying so.
        DocumentFontResource font = Font(0);
        (DocumentResourceId id, DocumentConversionContext context) =
            Admit(font, DocumentResourcePolicy.AllowOwnDocuments);

        Assert.False(DocumentFontEmbedding.MayEmbed(font, id, context, subsetting: false, out string? refusal));
        Assert.Contains("EmbedOrSubset", refusal!, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Silence_Is_Refused_Rather_Than_Read_As_Permission()
    {
        var font = new DocumentFontResource(
            new byte[] { 9, 9 },
            "Nameless",
            BFontEmbeddingRights.Unknown);
        (DocumentResourceId id, DocumentConversionContext context) = Admit(font);

        Assert.False(DocumentFontEmbedding.MayEmbed(font, id, context, subsetting: false, out string? refusal));
        Assert.Contains("no embedding permission", refusal!, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void No_Subsetting_Refuses_Only_The_Subsetting()
    {
        DocumentFontResource font = Font(0x0100);
        (DocumentResourceId id, DocumentConversionContext context) = Admit(font);

        Assert.True(DocumentFontEmbedding.MayEmbed(font, id, context, subsetting: false, out _));
        Assert.False(DocumentFontEmbedding.MayEmbed(font, id, context, subsetting: true, out string? refusal));
        Assert.Contains("forbids subsetting", refusal!, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Bitmap_Only_Embedding_Is_Refused_Because_None_Is_Emitted()
    {
        DocumentFontResource font = Font(0x0200);
        (DocumentResourceId id, DocumentConversionContext context) = Admit(font);

        Assert.False(DocumentFontEmbedding.MayEmbed(font, id, context, subsetting: false, out string? refusal));
        Assert.Contains("bitmap", refusal!, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Swapping_The_Program_For_Another_Of_The_Same_Family_Fails_The_Check()
    {
        // What binding the declaration into the entry is for. Approving a
        // permissive program does not approve a restricted one wearing the same
        // family name.
        DocumentFontResource approved = Font(0);
        (DocumentResourceId id, DocumentConversionContext context) = Admit(approved);

        var substituted = new DocumentFontResource(
            new byte[] { 5, 6, 7, 8 },
            approved.Family,
            BFontEmbeddingRights.FromFsType(0x0002));

        Assert.False(DocumentFontEmbedding.MayEmbed(substituted, id, context, subsetting: false, out string? refusal));
        Assert.Contains("not the one that was approved", refusal!, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_Font_Read_From_A_Document_Is_Not_Export_Authority()
    {
        Assert.False(DocumentFontEmbedding.MayReExport(DocumentResourceProvenance.ReadFromSource));
        Assert.False(DocumentFontEmbedding.MayReExport(DocumentResourceProvenance.Unknown));
        Assert.True(DocumentFontEmbedding.MayReExport(DocumentResourceProvenance.CallerSupplied));
    }
}
