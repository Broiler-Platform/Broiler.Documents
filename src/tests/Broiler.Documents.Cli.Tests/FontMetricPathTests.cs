using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Cli.Rendering;
using Broiler.Graphics.Text;

namespace Broiler.Documents.Cli.Tests;

/// <summary>
/// Which file a run's line is measured from. It has to be the file the run is
/// drawn with: a family the mapping does not name is drawn with the host's
/// fallback face, and was measured with an estimate of 125% instead of that
/// face's own metrics, so tdf168017.docx, set in an unmapped Arial Narrow, drew
/// thirteen-point lines where the face it was drawn in makes them 11.64.
/// </summary>
public sealed class FontMetricPathTests
{
    private static readonly CommandSpec Spec = new(
        "render",
        string.Empty,
        string.Empty,
        [OptionSpec.Many("font-file", "family=path", "A face."), OptionSpec.Many("font-dir", "dir", "A directory.")]);

    private static BFontStyle Font(string family, bool bold = false) =>
        new(family, 10, bold ? BFontWeight.Bold : BFontWeight.Normal, BFontSlant.Normal);

    [Fact]
    public void A_Mapped_Family_Is_Measured_From_Its_Own_Face()
    {
        string mapped = Path.GetTempFileName();
        string fallback = Path.GetTempFileName();
        try
        {
            FontResolution fonts = FontResolution.FromCommandLine(
                CommandLine.Parse(Spec, ["--font-file", "Arial=" + mapped]), fallback);

            Assert.Equal(mapped, fonts.MetricPath(Font("Arial")));
        }
        finally
        {
            File.Delete(mapped);
            File.Delete(fallback);
        }
    }

    [Fact]
    public void An_Unmapped_Family_Is_Measured_From_The_Host_Fallback()
    {
        string mapped = Path.GetTempFileName();
        string fallback = Path.GetTempFileName();
        try
        {
            FontResolution fonts = FontResolution.FromCommandLine(
                CommandLine.Parse(Spec, ["--font-file", "Arial=" + mapped]), fallback);

            Assert.Equal(fallback, fonts.MetricPath(Font("Arial Narrow")));
            Assert.Equal(fallback, fonts.MetricPath(Font("Arial Narrow", bold: true)));
            Assert.Contains("Arial Narrow", fonts.UnmappedRequests);
        }
        finally
        {
            File.Delete(mapped);
            File.Delete(fallback);
        }
    }

    [Fact]
    public void With_No_Mapping_Every_Family_Is_Measured_From_The_Host_Fallback()
    {
        // No resolver is installed, so the renderer draws every family with the
        // one host face, and that face is what every line is as tall as.
        string fallback = Path.GetTempFileName();
        try
        {
            FontResolution fonts = FontResolution.FromCommandLine(CommandLine.Parse(Spec, []), fallback);

            Assert.Equal(fallback, fonts.MetricPath(Font("Times New Roman")));
        }
        finally
        {
            File.Delete(fallback);
        }
    }

    [Fact]
    public void A_Host_With_No_Fallback_File_Leaves_The_Line_To_The_Estimate()
    {
        FontResolution fonts = FontResolution.FromCommandLine(CommandLine.Parse(Spec, []), hostFallback: null);

        Assert.Null(fonts.MetricPath(Font("Arial Narrow")));
        Assert.Null(fonts.VerticalMetrics(Font("Arial Narrow")));
    }
}
