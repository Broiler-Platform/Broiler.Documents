namespace Broiler.Documents.Model.Tests;

public sealed class ParagraphTabStopTests
{
    [Fact]
    public void Stops_Are_Snapshotted_Sorted_And_Value_Comparable()
    {
        var source = new List<ParagraphTabStop> { new(72), new(36), new(72, TabAlignment.Right) };
        var tabs = new ParagraphTabStops(source);
        source.Clear();
        Assert.Equal(new ParagraphTabStops([new(36), new(72, TabAlignment.Right)]), tabs);
        Assert.Equal(tabs.GetHashCode(), new ParagraphTabStops(tabs).GetHashCode());
        Assert.Equal(ParagraphStyle.Default with { TabStops = tabs },
            ParagraphStyle.Default with { TabStops = new ParagraphTabStops(tabs) });
    }

    [Fact]
    public void Nonfinite_And_Out_Of_Range_Stops_Cannot_Reach_Layout()
    {
        var tabs = new ParagraphTabStops([new(double.NaN), new(double.PositiveInfinity), new(-1), new(20001), new(72)]);
        Assert.Equal(new ParagraphTabStop(72), Assert.Single(tabs));
    }
}
