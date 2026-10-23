using Broiler.Graphics.Color;

namespace Broiler.Documents.Model.Tests;

public sealed class FillGeometryTests
{
    private static readonly InlineImage Picture = new(new byte[] { 1 }, "image/png", 12, 8);

    [Theory]
    [InlineData(ImageFillRepeat.Stretch, 0, 0, 100, 60)]
    [InlineData(ImageFillRepeat.Single, 88, 52, 12, 8)]
    public void Single_And_Stretched_Images_Have_Different_Bounds(ImageFillRepeat repeat, double x, double y, double width, double height)
    {
        Assert.Equal(new FillGeometry.Tile(x, y, width, height), Assert.Single(FillGeometry.Tiles(new ImageFill(Picture, repeat, AlignX: 1, AlignY: 1), 100, 60, 12, 8)));
    }

    [Fact]
    public void Natural_Tiles_Cover_The_Bounds_Without_Stretching()
    {
        var tiles = FillGeometry.Tiles(new ImageFill(Picture, AlignX: 0, AlignY: 0), 25, 17, 12, 8).ToArray();
        Assert.Equal(9, tiles.Length);
        Assert.Equal(new FillGeometry.Tile(0, 0, 12, 8), tiles[0]);
        Assert.Equal(new FillGeometry.Tile(24, 16, 12, 8), tiles[^1]);
    }

    [Fact]
    public void Hostile_Tiny_Tiles_Are_Bounded_And_Finite()
    {
        var tiles = FillGeometry.Tiles(new ImageFill(Picture, Width: 1e-300, Height: 1e-300), 1000, 1000, 12, 8).ToArray();
        Assert.InRange(tiles.Length, 1, 16384);
        Assert.All(tiles, tile => Assert.True(double.IsFinite(tile.Width) && double.IsFinite(tile.Height) && tile.Width > 0 && tile.Height > 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-45)]
    [InlineData(90)]
    public void Hatching_Is_Clipped_To_The_Box(double angle)
    {
        var strokes = FillGeometry.Hatching(new HatchFill(BColor.Black, 6, angle, 3), 100, 60).ToArray();
        Assert.NotEmpty(strokes);
        Assert.All(strokes, stroke =>
        {
            Assert.InRange(stroke.X1, -0.001, 100.001); Assert.InRange(stroke.X2, -0.001, 100.001);
            Assert.InRange(stroke.Y1, -0.001, 60.001); Assert.InRange(stroke.Y2, -0.001, 60.001);
        });
    }

    [Fact]
    public void Radial_Gradient_Samples_Match_The_Authored_LibreOffice_Fixture()
    {
        var fill = new ShapeFill(BColor.FromArgb(255, 0, 0), BColor.FromArgb(0, 0, 255), 0)
            { Geometry = GradientGeometry.Radial, CenterX = .66, CenterY = .33, Border = .25 };
        Assert.Equal(fill.End, fill.ColorAt(264, 79.2, 400, 240));
        BColor middle = fill.ColorAt(200, 120, 400, 240);
        Assert.InRange(middle.R, (byte)107, (byte)111);
        Assert.InRange(middle.B, (byte)143, (byte)147);
        Assert.Equal(fill.Start, fill.ColorAt(399, 239, 400, 240));
    }

    [Fact]
    public void Border_Reserves_The_Start_Colour_And_Then_Blends()
    {
        var fill = new ShapeFill(BColor.Black, BColor.White, 0) { Border = .5 };
        Assert.Equal(BColor.Black, fill.ColorAt(.5));
        Assert.Equal(new BColor(128, 128, 128), fill.ColorAt(.75));
        Assert.Equal(BColor.White, fill.ColorAt(1));
    }
}
