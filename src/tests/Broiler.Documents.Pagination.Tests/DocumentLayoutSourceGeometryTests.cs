using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;

namespace Broiler.Documents.Pagination.Tests;

public sealed class DocumentLayoutSourceGeometryTests
{
    private static readonly DocumentPageBox Page = new(240, 180, 18, 18, 18, 18);
    private static readonly LayoutSettings Settings = new() { IncludeSourceGeometry = true };

    [Fact]
    public void Renderer_Provides_Directed_Advances_For_Logical_Source_Ranges()
    {
        const string text = "א\u05B8ב";
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain(text)]);
        using var pages = new DocumentPaginator(Settings with
        {
            VisualTextMeasurer = (displayText, _, ranges) =>
            {
                Assert.Equal(text, displayText);
                Assert.Equal([new LayoutDisplayRange(0, 2), new LayoutDisplayRange(2, 1)], ranges);
                return [new(20, 11), new(11, 0)];
            },
        }).Paginate(document, Page, TestContext.Current.CancellationToken);
        var piece = Assert.Single(Assert.Single(pages.Layout.Pages[0].Lines).Pieces);
        Assert.True(piece.HasVisualSourceGeometry);
        Assert.Equal([new LayoutSourceCluster(0, 2, 20, 11), new LayoutSourceCluster(2, 1, 11, 0)], piece.SourceClusters);

        using var unsupported = new DocumentPaginator(Settings).Paginate(document, Page, TestContext.Current.CancellationToken);
        var unresolved = Assert.Single(Assert.Single(unsupported.Layout.Pages[0].Lines).Pieces);
        Assert.False(unresolved.HasVisualSourceGeometry);
        Assert.Equal(piece.Width, unresolved.Width);
        Assert.Equal([(0, 2), (2, 1)], unresolved.SourceClusters.Select(cluster => (cluster.SourceOffset, cluster.SourceLength)));
    }

    [Fact]
    public void Symbol_And_Small_Caps_Keep_Source_Clusters_Across_Pieces()
    {
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Create("ab", InlineStyle.Default with { FontFamily = "Symbol" }),
            RichTextParagraph.Create("a\u0301B", InlineStyle.Default with { Capitalization = TextCapitalization.SmallCaps }),
        ]);
        using var pages = new DocumentPaginator(Settings).Paginate(document, Page, TestContext.Current.CancellationToken);
        var lines = pages.Layout.Pages[0].Lines;
        var symbol = Assert.Single(lines[0].Pieces);
        Assert.Equal("αβ", symbol.Text);
        Assert.Equal(new[] { (0, 1), (1, 1) }, symbol.SourceClusters.Select(cluster => (cluster.SourceOffset, cluster.SourceLength)));
        Assert.Equal(symbol.Width, symbol.SourceClusters[^1].EndAdvance);

        var smallCaps = lines[1].Pieces;
        Assert.True(smallCaps.Count > 1);
        var clusters = smallCaps.SelectMany(piece => piece.SourceClusters).ToArray();
        Assert.Equal(2, clusters.Count(cluster => cluster.SourceOffset == 0 && cluster.SourceLength == 2));
        Assert.DoesNotContain(clusters, cluster => cluster.SourceOffset == 1);
        Assert.Contains(clusters, cluster => cluster.SourceOffset == 2 && cluster.SourceLength == 1);
    }

    [Fact]
    public void Joined_Emoji_Retains_One_Selectable_Cluster_Without_A_Bidi_Renderer()
    {
        const string text = "👩\u200D💻";
        using var pages = new DocumentPaginator(Settings).Paginate(
            RichTextDocument.FromParagraphs([RichTextParagraph.Plain(text)]), Page, TestContext.Current.CancellationToken);
        var piece = Assert.Single(Assert.Single(pages.Layout.Pages[0].Lines).Pieces);
        Assert.True(piece.HasVisualSourceGeometry);
        Assert.Equal(new LayoutSourceCluster(0, text.Length, 0, piece.Width), Assert.Single(piece.SourceClusters));
    }

    [Fact]
    public void Tabs_And_Pictures_Are_Atomic_And_Empty_Break_Lines_Have_A_Caret()
    {
        var imageSource = BImageResource.FromPixels(new BPixelBuffer(1, 1, [0, 0, 0, 255]));
        var picture = RichTextParagraph.Create(InlineImage.PlaceholderText,
            InlineStyle.Default with { Image = new InlineImage(imageSource, width: 20, height: 12) });
        var document = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("a\tB\u2028\u2028c\u2028"), picture]);
        using var pages = new DocumentPaginator(Settings).Paginate(document, Page, TestContext.Current.CancellationToken);
        var lines = pages.Layout.Pages[0].Lines;
        var tab = Assert.Single(lines[0].Pieces, piece => piece.IsTab);
        Assert.Equal(new LayoutSourceCluster(1, 1, 0, tab.Width), Assert.Single(tab.SourceClusters));
        Assert.Equal(4, lines[1].EmptyLineSourceOffset);
        Assert.Equal(7, lines[3].EmptyLineSourceOffset);
        var image = Assert.Single(lines[4].Pieces, piece => piece.IsImage);
        Assert.Equal(new LayoutSourceCluster(0, 1, 0, image.Width), Assert.Single(image.SourceClusters));
    }

    [Fact]
    public void Repeated_Clipped_Header_Lines_Retain_Their_Body_Source_Identity()
    {
        var paragraphs = Enumerable.Range(0, 30).Select(i => RichTextParagraph.Plain(i == 0 ? "Title" : $"Row {i}")).ToArray();
        var rows = Enumerable.Range(0, paragraphs.Length)
            .Select(i => new TableRow([new TableCell(i, 1, 0)], isHeader: i == 0, minHeight: i == 0 ? 10 : 24,
                exactHeight: i == 0))
            .ToArray();
        var document = RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([new DocumentTable(0, paragraphs.Length, rows, [100.0])]);
        using var pages = new DocumentPaginator(Settings).Paginate(document, Page, TestContext.Current.CancellationToken);
        Assert.True(pages.PageCount > 1);
        foreach (var page in pages.Layout.Pages)
        {
            var heading = Assert.Single(page.Lines, line => line.ParagraphIndex == 0);
            Assert.NotNull(heading.ClipHeight);
            var clusters = heading.Pieces.SelectMany(piece => piece.SourceClusters).ToArray();
            Assert.Equal(Enumerable.Range(0, 5), clusters.Select(cluster => cluster.SourceOffset));
        }
    }

    [Fact]
    public void Shared_Composition_Preserves_Print_Commands_And_Source_Maps()
    {
        var style = ParagraphStyle.Default with { FirstLineIndent = 24, Alignment = TextAlignment.Center };
        var document = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("   words with   gaps and enough material to wrap to another row  ").WithParagraphStyle(style),
            RichTextParagraph.Plain("   " + string.Concat(Enumerable.Repeat("e\u0301", 60)) + " after the long word"),
            RichTextParagraph.Plain("   "),
            RichTextParagraph.Plain("before\u2028\u2028after\u2028"),
            RichTextParagraph.Plain(string.Concat(Enumerable.Repeat("Reinraum\u00ADklasse ", 15))),
            RichTextParagraph.Plain("first styled list item with more words").WithParagraphStyle(style with { ListKind = ListKind.Bullet })
                .ApplyInlineStyle(6, 6, InlineStyleDelta.ToggleBold(true)),
        ]);
        using var shared = new DocumentPaginator(Settings).Paginate(document, Page, TestContext.Current.CancellationToken);
        using var legacy = new DocumentPaginator(Settings with { UseSharedParagraphComposition = false })
            .Paginate(document, Page, TestContext.Current.CancellationToken);
        Assert.True(shared.Layout.Pages.Sum(page => page.Lines.Count) > document.ParagraphCount);
        Assert.Equal(legacy.PageCount, shared.PageCount);
        for (int i = 0; i < shared.PageCount; i++)
        {
            Assert.Equal(legacy.GetPage(i).Commands.ToArray(), shared.GetPage(i).Commands.ToArray());
            Assert.Equal(legacy.Layout.Pages[i].Lines.SelectMany(line => line.Pieces).SelectMany(piece => piece.SourceClusters),
                shared.Layout.Pages[i].Lines.SelectMany(line => line.Pieces).SelectMany(piece => piece.SourceClusters));
        }
    }

    [Fact]
    public void Reflowed_Columns_Map_Back_To_The_Original_Paragraph_Without_Changing_Draw_Commands()
    {
        string source = string.Join(" ", Enumerable.Range(0, 110).Select(i => $"item{i:000}"));
        var document = RichTextDocument.FromPlainText(source).WithPageGeometry(PageGeometry.A4 with
        {
            Width = 240, Height = 180, MarginLeft = 18, MarginRight = 18, MarginTop = 18, MarginBottom = 18,
            ColumnCount = 2, ColumnGap = 12,
        });
        using var editing = new DocumentPaginator(Settings).Paginate(document, Page, TestContext.Current.CancellationToken);
        using var printing = new DocumentPaginator().Paginate(document, Page, TestContext.Current.CancellationToken);
        Assert.True(editing.PageCount > 1);
        Assert.Equal(printing.PageCount, editing.PageCount);

        var words = editing.Layout.Pages.SelectMany(page => page.Lines).SelectMany(line => line.Pieces)
            .Where(piece => piece.Text.StartsWith("item", StringComparison.Ordinal)).ToArray();
        Assert.Equal(110, words.Length);
        Assert.Contains(words, piece => piece.SourceClusters[0].SourceOffset != piece.SourceOffset);
        foreach (var piece in words)
        {
            int start = piece.SourceClusters[0].SourceOffset;
            Assert.Equal(piece.Text, source.Substring(start, piece.Text.Length));
        }
        Assert.Equal(source.LastIndexOf("item109", StringComparison.Ordinal), words[^1].SourceClusters[0].SourceOffset);

        for (int i = 0; i < editing.PageCount; i++)
        {
            Assert.Equal(printing.GetPage(i).Size, editing.GetPage(i).Size);
            Assert.Equal(printing.GetPage(i).Commands.OfType<BRenderCommand.DrawText>(),
                editing.GetPage(i).Commands.OfType<BRenderCommand.DrawText>());
        }
        Assert.All(printing.Layout.Pages.SelectMany(page => page.Lines).SelectMany(line => line.Pieces),
            piece => Assert.Empty(piece.SourceClusters));
    }
}
