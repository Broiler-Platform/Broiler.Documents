using System;
using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf;

/// <summary>
/// A decoded image and the box the page draws it in, in PDF user space.
/// </summary>
/// <remarks>
/// <para>
/// The box comes from the current transformation matrix rather than from the
/// image dictionary. A PDF image declares its size in samples and is drawn by
/// mapping the unit square through the CTM, so the pixels say how much detail
/// there is and the matrix says how large it appears — and only the second is a
/// display size.
/// </para>
/// <para>
/// User space is points, so <see cref="Width"/> and <see cref="Height"/> are
/// already in the model's unit. <see cref="Top"/> is the upper edge with Y
/// increasing upward, matching the ordering the reading-order pass uses for text,
/// so an image sorts among lines rather than needing a second convention.
/// </para>
/// </remarks>
internal sealed class PdfPlacedImage
{
    public PdfPlacedImage(InlineImage image, double left, double top, double width, double height)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    /// <summary>The image, already admitted by the caller's resource policy.</summary>
    public InlineImage Image { get; }

    /// <summary>Left edge of the drawn box, in points.</summary>
    public double Left { get; }

    /// <summary>Upper edge of the drawn box, in points, Y increasing upward.</summary>
    public double Top { get; }

    /// <summary>Drawn width in points.</summary>
    public double Width { get; }

    /// <summary>Drawn height in points.</summary>
    public double Height { get; }
}
