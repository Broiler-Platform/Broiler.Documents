namespace Broiler.Documents.FormatCodes.Tests;

public sealed class FormatCodeProjectionMappingTests
{
    private readonly FormatCodeProjector _projector = new();

    [Fact]
    public void Document_Boundary_Affinity_Selects_Earliest_And_Latest_Code_Carets()
    {
        RichTextDocument document = RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("Hi", new InlineStyle { Bold = true })]);
        FormatCodeProjection projection = _projector.Project(document);

        FormatCodeCaret before = projection.MapDocumentPosition(
            document.Start,
            FormatCodeBoundaryAffinity.Before);
        FormatCodeCaret after = projection.MapDocumentPosition(
            document.Start,
            FormatCodeBoundaryAffinity.After);

        Assert.Equal(0, before.TokenIndex);
        Assert.Equal(0, before.OffsetWithinToken);
        Assert.Equal(1, after.TokenIndex);
        Assert.Equal(0, after.OffsetWithinToken);
    }

    [Fact]
    public void Literal_Text_Maps_Linearly_In_Both_Directions()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("abc");
        FormatCodeProjection projection = _projector.Project(document);
        RichTextPosition afterA = document.PositionRightOf(document.Start);

        FormatCodeCaret caret = projection.MapDocumentPosition(afterA);
        FormatCodeMappedPosition mapped = projection.MapProjectedOffset(1);

        Assert.Equal(0, caret.TokenIndex);
        Assert.Equal(1, caret.OffsetWithinToken);
        Assert.Equal(afterA, mapped.DocumentPosition);
    }

    [Fact]
    public void Expanded_Escape_Maps_First_Half_Before_And_Second_Half_After()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("[");
        FormatCodeProjection projection = _projector.Project(document);

        Assert.Equal(document.Start, projection.MapProjectedOffset(0).DocumentPosition);
        Assert.Equal(document.Start, projection.MapProjectedOffset(1).DocumentPosition);
        Assert.Equal(document.End, projection.MapProjectedOffset(2).DocumentPosition);
    }

    [Fact]
    public void Literal_Surrogate_Interior_Collapses_To_The_Model_Boundary()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("😀");
        FormatCodeProjection projection = _projector.Project(document);

        Assert.Equal(document.Start, projection.MapProjectedOffset(1).DocumentPosition);
        Assert.Equal(document.End, projection.MapProjectedOffset(2).DocumentPosition);
    }

    [Fact]
    public void Every_Projected_Offset_Maps_To_A_Valid_Document_Position()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("a[\t]\nb\u2028c");
        FormatCodeProjection projection = _projector.Project(document);

        for (int offset = 0; offset <= projection.Text.Length; offset++)
        {
            FormatCodeMappedPosition mapped = projection.MapProjectedOffset(offset);
            Assert.True(document.IsValid(mapped.DocumentPosition), $"Projected offset {offset} was invalid.");
        }
    }

    [Fact]
    public void Empty_Paragraph_And_End_Of_Document_Are_Addressable()
    {
        RichTextDocument document = RichTextDocument.Empty;
        FormatCodeProjection projection = _projector.Project(document);

        Assert.Equal("[Empty Paragraph]", projection.Text);
        Assert.Equal(document.Start, projection.MapProjectedOffset(0).DocumentPosition);
        Assert.Equal(document.End, projection.MapProjectedOffset(projection.Text.Length).DocumentPosition);
        Assert.Equal(0, projection.MapDocumentPosition(document.End).TokenIndex);
    }

    [Fact]
    public void Paragraph_Break_Exposes_Both_Source_Boundaries()
    {
        RichTextDocument document = RichTextDocument.FromPlainText("a\nb");
        FormatCodeProjection projection = _projector.Project(document);
        FormatCodeToken boundary = Assert.Single(
            projection.Tokens.Where(token => token.DisplayText == "[Paragraph Break]\n"));
        RichTextPosition firstEnd = document.PositionRightOf(document.Start);
        RichTextPosition secondStart = document.PositionRightOf(firstEnd);

        Assert.Equal(firstEnd, boundary.SourceBefore);
        Assert.Equal(secondStart, boundary.SourceAfter);
        Assert.NotNull(boundary.AffectedRange);
    }
}
