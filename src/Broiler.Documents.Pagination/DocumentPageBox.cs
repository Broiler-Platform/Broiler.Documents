using System;
using Broiler.Documents.Model;
using Broiler.Graphics.Paging;

namespace Broiler.Documents.Pagination;

/// <summary>
/// The page a layout fills: its size and margins in points, and whether it paginates at all.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is about output. A resolution, a background colour or a file format belong to
/// whoever draws the laid-out pages - the CLI, a print preview, a printer - and none of them
/// changes where a line breaks, so none of them is part of the box a layout is given.
/// </para>
/// <para>
/// <see cref="Continuous"/> lays the whole document out as one page as tall as its content: no
/// page breaks, and no running headers or footers. The CLI uses it to compare two exports
/// without one extra line moving every later page.
/// </para>
/// </remarks>
public sealed class DocumentPageBox
{
    /// <summary>Points per inch.</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>CSS reference pixels per inch, the size an image that states none is drawn at.</summary>
    public const double PixelsPerInch = 96.0;

    public DocumentPageBox(double widthPoints, double heightPoints, double marginTopPoints, double marginRightPoints,
        double marginBottomPoints, double marginLeftPoints, bool continuous = false)
    {
        if (!double.IsFinite(widthPoints) || widthPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "The page width must be positive and finite.");
        if (!double.IsFinite(heightPoints) || heightPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightPoints), "The page height must be positive and finite.");

        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        MarginTopPoints = NonNegative(marginTopPoints, nameof(marginTopPoints));
        MarginRightPoints = NonNegative(marginRightPoints, nameof(marginRightPoints));
        MarginBottomPoints = NonNegative(marginBottomPoints, nameof(marginBottomPoints));
        MarginLeftPoints = NonNegative(marginLeftPoints, nameof(marginLeftPoints));
        Continuous = continuous;
    }

    /// <summary>A4 with one-inch margins.</summary>
    public static DocumentPageBox Default { get; } = new(595.276, 841.89, 72, 72, 72, 72);

    public double WidthPoints { get; }

    public double HeightPoints { get; }

    public double MarginTopPoints { get; }

    public double MarginRightPoints { get; }

    public double MarginBottomPoints { get; }

    public double MarginLeftPoints { get; }

    /// <summary>True when the page grows to fit the content instead of paginating.</summary>
    public bool Continuous { get; }

    public double ContentLeftPoints => MarginLeftPoints;

    public double ContentTopPoints => MarginTopPoints;

    public double ContentWidthPoints => Math.Max(1.0, WidthPoints - MarginLeftPoints - MarginRightPoints);

    public double ContentHeightPoints => Math.Max(1.0, HeightPoints - MarginTopPoints - MarginBottomPoints);

    /// <summary>The box a print or preview page setup describes.</summary>
    public static DocumentPageBox FromSetup(BPageSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var size = setup.PageSize;
        BPageMargins margins = setup.Margins;
        return new DocumentPageBox(size.Width, size.Height, margins.Top, margins.Right, margins.Bottom, margins.Left);
    }

    /// <summary>The box a document states, keeping this box's <see cref="Continuous"/>.</summary>
    public DocumentPageBox WithGeometry(PageGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return new DocumentPageBox(geometry.Width, geometry.Height, geometry.MarginTop, geometry.MarginRight, geometry.MarginBottom,
            geometry.MarginLeft, Continuous);
    }

    /// <summary>The same box with a different height, for the continuous single-page form.</summary>
    public DocumentPageBox WithHeight(double heightPoints) => new(WidthPoints, Math.Max(heightPoints, MarginTopPoints + MarginBottomPoints + 1),
        MarginTopPoints, MarginRightPoints, MarginBottomPoints, MarginLeftPoints, Continuous);

    private static double NonNegative(double value, string name) =>
        double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(name, "A margin must be finite and not negative.");
}
