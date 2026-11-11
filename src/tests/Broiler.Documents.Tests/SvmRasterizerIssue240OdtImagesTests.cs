using Broiler.Documents.Resources;

namespace Broiler.Documents.Tests;

/// <summary>
/// Office conformance issue 240, ODT pictures: a replacement metafile that only
/// sets state draws nothing, as the one stored for fdo68839.odt's empty
/// embedded spreadsheet does, and is told apart from one this subset cannot draw.
/// </summary>
public sealed class SvmRasterizerIssue240OdtImagesTests
{
    [Fact]
    public void A_Metafile_That_Only_Sets_State_Is_Blank()
    {
        var svm = new SvmRasterizerTests.SvmBuilder(unit: 0, width: 1000, height: 500);
        svm.LineColor(0, set: true);
        svm.FillColor(0xFF0000, set: true);
        svm.MapMode(unit: 0, originX: 10, originY: 10, scaleNum: 1, scaleDen: 1);

        byte[] data = svm.ToArray();
        Assert.True(SvmRasterizer.IsBlank(data));
        Assert.Null(SvmRasterizer.RenderPng(data));
        Assert.True(SvmRasterizer.IsBlank(new SvmRasterizerTests.SvmBuilder(unit: 0, width: 10, height: 10).ToArray()));
    }

    [Fact]
    public void A_Metafile_That_Draws_Or_Carries_Comments_Is_Not_Blank()
    {
        var drawn = new SvmRasterizerTests.SvmBuilder(unit: 0, width: 1000, height: 500);
        drawn.LineColor(0, set: true);
        drawn.PolyLine((0, 0), (1000, 500));
        Assert.False(SvmRasterizer.IsBlank(drawn.ToArray()));

        // EMF+ rides in comments, which LibreOffice plays.
        var comments = new SvmRasterizerTests.SvmBuilder(unit: 0, width: 1000, height: 500);
        comments.Comment("EMF_PLUS");
        Assert.False(SvmRasterizer.IsBlank(comments.ToArray()));
    }

    [Fact]
    public void A_Truncated_Or_Foreign_Stream_Is_Not_Blank()
    {
        var svm = new SvmRasterizerTests.SvmBuilder(unit: 0, width: 1000, height: 500);
        svm.FillColor(0xFF0000, set: true);
        byte[] data = svm.ToArray();

        Assert.False(SvmRasterizer.IsBlank(data[..^3]));
        Assert.False(SvmRasterizer.IsBlank("VCLMTF"u8.ToArray()));
        Assert.False(SvmRasterizer.IsBlank("not a metafile at all"u8.ToArray()));
    }
}
