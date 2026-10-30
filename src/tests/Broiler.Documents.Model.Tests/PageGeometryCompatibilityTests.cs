using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public sealed class PageGeometryCompatibilityTests
{
    [Fact]
    public void LegacyEightArgumentConstructorRemainsBinaryCompatible()
    {
        var signature = Enumerable.Repeat(typeof(double), 8).ToArray();
        var constructor = typeof(PageGeometry).GetConstructor(signature);
        Assert.NotNull(constructor);
        var page = Assert.IsType<PageGeometry>(constructor.Invoke(new object[] { 600d, 800d, 40d, 50d, 60d, 70d, 20d, 30d }));
        Assert.Equal(new PageGeometry(600, 800, 40, 50, 60, 70, 20, 30, 1, 0, false, false), page);
        Assert.Equal(510, page.ContentWidth);
        Assert.Equal(670, page.ContentHeight);
        Assert.Equal(1, page.ColumnCount);
        Assert.False(page.FixedHeaderHeight);
        Assert.False(page.FixedFooterHeight);
    }

    [Fact]
    public void LegacyColumnConstructorKeepsColumnSettings()
    {
        var signature = Enumerable.Repeat(typeof(double), 8).Concat(new[] { typeof(int), typeof(double) }).ToArray();
        var constructor = typeof(PageGeometry).GetConstructor(signature);
        Assert.NotNull(constructor);
        var page = Assert.IsType<PageGeometry>(constructor.Invoke(new object[] { 600d, 800d, 40d, 50d, 60d, 70d, 20d, 30d, 2, 10d }));
        Assert.Equal(2, page.ColumnCount);
        Assert.Equal(250, page.ColumnWidth);
        Assert.False(page.FixedHeaderHeight);
        Assert.False(page.FixedFooterHeight);
    }

    [Fact]
    public void CurrentOptionalAndNamedArgumentsRemainAvailable()
    {
        var page = new PageGeometry(600, 800, 40, 50, 60, 70, FixedHeaderHeight: true, ColumnCount: 2, ColumnGap: 10);
        Assert.True(page.FixedHeaderHeight);
        Assert.Equal(2, page.ColumnCount);
    }
}
