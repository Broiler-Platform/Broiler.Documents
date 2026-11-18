using System;

namespace Broiler.Documents.Model.Text;

/// <summary>Tab geometry in the units supplied by the caller. The layout chooses the eligible stop.</summary>
public static class ParagraphTabGeometry
{
    /// <summary>Returns the first stop strictly past the current advance.</summary>
    public static double NextStop(double advance, double interval) =>
        (Math.Floor(Math.Max(0, advance) / interval) + 1) * interval;

    /// <summary>Distance from the field's start to the point aligned to the stop.</summary>
    public static double FieldAnchor(TabAlignment alignment, double fieldWidth, double? decimalPrefix = null) => alignment switch
    {
        TabAlignment.Center => fieldWidth / 2,
        TabAlignment.Right => fieldWidth,
        TabAlignment.Decimal => decimalPrefix ?? fieldWidth,
        _ => 0,
    };
}
