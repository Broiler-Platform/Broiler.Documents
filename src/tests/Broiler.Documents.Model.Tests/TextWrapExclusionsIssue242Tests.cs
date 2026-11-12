using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Model.Tests;

/// <summary>
/// Issue #242, DOCX layout cluster: a top-and-bottom frame keeps text off the
/// lines of the columns it reaches across, and only those (fdo43573-2-min.docx,
/// whose 323-point picture in the left column of two leaves the right column's
/// text where it is in LibreOffice 24.2).
/// </summary>
public sealed class TextWrapExclusionsIssue242Tests
{
    private static TextWrapExclusions Frame()
    {
        var wrap = new TextWrapExclusions();
        wrap.Add(new DocumentShape(0, 0, 0, 200, 100, wrap: ShapeWrap.TopAndBottom), 50, 1, null, null);
        return wrap;
    }

    [Fact]
    public void A_Top_And_Bottom_Frame_Moves_A_Line_In_A_Column_It_Reaches_Below_It()
    {
        double top = 60;
        TextBand band = Frame().Resolve(ref top, 12, 220, out double skip, columnLeft: 0);

        Assert.Equal(150, top, 3);
        Assert.Equal(90, skip, 3);
        Assert.Equal(220, band.Width, 3);
    }

    [Fact]
    public void A_Top_And_Bottom_Frame_Leaves_A_Column_It_Does_Not_Reach_Alone()
    {
        double top = 60;
        TextBand band = Frame().Resolve(ref top, 12, 220, out double skip, columnLeft: 240);

        Assert.Equal(60, top, 3);
        Assert.Equal(0, skip, 3);
        Assert.Equal(220, band.Width, 3);
    }
}
