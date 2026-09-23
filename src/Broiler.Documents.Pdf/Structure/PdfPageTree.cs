using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Pdf.Syntax;
using Broiler.Documents.Pdf.Text;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>A PDF rectangle in default user space units (points).</summary>
internal readonly struct PdfRectangle(double left, double bottom, double right, double top)
{
    public double Left { get; } = Math.Min(left, right);

    public double Bottom { get; } = Math.Min(bottom, top);

    public double Right { get; } = Math.Max(left, right);

    public double Top { get; } = Math.Max(bottom, top);

    public double Width => Right - Left;

    public double Height => Top - Bottom;

    /// <summary>US Letter, the fallback when a page declares no usable MediaBox.</summary>
    public static PdfRectangle DefaultMediaBox => new(0, 0, 612, 792);

    public bool IsUsable =>
        double.IsFinite(Left) && double.IsFinite(Bottom) && double.IsFinite(Right) && double.IsFinite(Top) &&
        Width > 0 && Height > 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{Left} {Bottom} {Right} {Top}]");
}

/// <summary>One page, with its inherited attributes already applied.</summary>
/// <remarks>
/// <para>
/// Everything the reader measures on a page is measured on the page as a viewer
/// displays it, which <see cref="Display"/> maps default user space onto. PDF
/// makes a landscape page one of two ways: a wide box, or a tall box with a
/// <c>/Rotate</c> entry and the content drawn up it so that it reads across once
/// the viewer turns the page. The second used to be read in the unturned space,
/// where every line of text runs up the page, and it came back as a column of
/// scattered letters with nothing reported.
/// </para>
/// <para>
/// The same map takes <c>/UserUnit</c> into points and puts the visible box's
/// lower-left corner at the origin, so a size or a distance measured on the page
/// is in the model's unit and is a distance from the page's own edge.
/// </para>
/// </remarks>
internal sealed class PdfPage(
    PdfDictionary dictionary,
    PdfDictionary? resources,
    PdfRectangle mediaBox,
    PdfRectangle cropBox,
    int rotation,
    double userUnit)
{
    public PdfDictionary Dictionary { get; } = dictionary;

    /// <summary>The page's resource dictionary, inherited from an ancestor when absent.</summary>
    public PdfDictionary? Resources { get; } = resources;

    public PdfRectangle MediaBox { get; } = mediaBox;

    /// <summary>
    /// The visible region in default user space: the crop box, defaulting to the
    /// media box (clause 7.7.3.3), and clipped to it (clause 14.11.2).
    /// </summary>
    public PdfRectangle CropBox { get; } = cropBox;

    /// <summary>Clockwise display rotation, normalized to 0, 90, 180, or 270.</summary>
    public int Rotation { get; } = rotation;

    public double UserUnit { get; } = userUnit;

    /// <summary>
    /// Maps default user space onto the page as displayed: turned clockwise by
    /// <see cref="Rotation"/>, scaled by <see cref="UserUnit"/> into points, with
    /// the visible box running from the origin to <see cref="DisplayWidth"/> by
    /// <see cref="DisplayHeight"/>.
    /// </summary>
    public PdfMatrix Display { get; } = DisplayOf(cropBox, rotation, userUnit);

    /// <summary>The width of the page as displayed, in points.</summary>
    public double DisplayWidth => (IsTurnedSideways ? CropBox.Height : CropBox.Width) * UserUnit;

    /// <summary>The height of the page as displayed, in points.</summary>
    public double DisplayHeight => (IsTurnedSideways ? CropBox.Width : CropBox.Height) * UserUnit;

    /// <summary>A rectangle in default user space - an annotation's <c>/Rect</c> - on the page as displayed.</summary>
    public PdfRectangle ToDisplay(PdfRectangle rectangle)
    {
        (double left, double bottom) = Display.Transform(rectangle.Left, rectangle.Bottom);
        (double right, double top) = Display.Transform(rectangle.Right, rectangle.Top);
        return new PdfRectangle(left, bottom, right, top);
    }

    private bool IsTurnedSideways => Rotation is 90 or 270;

    // Each turn keeps the visible box's lower-left corner, as displayed, at the
    // origin. Turned a quarter clockwise, what ran up the page runs across it and
    // what ran across it runs down.
    private static PdfMatrix DisplayOf(PdfRectangle box, int rotation, double unit) => rotation switch
    {
        90 => new PdfMatrix(0, -unit, unit, 0, -unit * box.Bottom, unit * box.Right),
        180 => new PdfMatrix(-unit, 0, 0, -unit, unit * box.Right, unit * box.Top),
        270 => new PdfMatrix(0, unit, -unit, 0, unit * box.Top, -unit * box.Left),
        _ => new PdfMatrix(unit, 0, 0, unit, -unit * box.Left, -unit * box.Bottom),
    };
}

/// <summary>
/// Walks the Catalog's page tree, applying the four inheritable attributes
/// (<c>/Resources</c>, <c>/MediaBox</c>, <c>/CropBox</c>, <c>/Rotate</c>) down
/// the tree.
/// </summary>
/// <remarks>
/// The walk is iterative with an explicit stack and a visited set, so a page tree
/// that points back at an ancestor — a trivially constructed hostile file —
/// terminates with a diagnostic instead of recursing forever.
/// </remarks>
internal static class PdfPageTree
{
    public static List<PdfPage> Collect(PdfObjectStore store, PdfDictionary catalog)
    {
        var pages = new List<PdfPage>();
        if (store.Resolve(catalog["Pages"]) is not PdfDictionary root)
        {
            store.Diagnostics.Error(PdfDiagnosticCodes.StructureMalformed, "The document catalog has no usable page tree.");
            return pages;
        }

        var visited = new HashSet<PdfDictionary>();
        var stack = new Stack<Node>();
        stack.Push(new Node(root, Inherited.Empty, 0));

        while (stack.Count > 0)
        {
            store.Budget.ThrowIfCancelled();
            Node node = stack.Pop();

            if (node.Depth > store.Budget.Limits.MaxPageTreeDepth)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxPageTreeDepth), store.Budget.Limits.MaxPageTreeDepth);

            if (!visited.Add(node.Dictionary))
            {
                store.Diagnostics.Warning(PdfDiagnosticCodes.ObjectCycle, "The page tree contained a cycle; the repeated branch was skipped.");
                continue;
            }

            Inherited inherited = node.Inherited.Merge(store, node.Dictionary);
            string? type = (store.Resolve(node.Dictionary["Type"]) as PdfName)?.Value;
            PdfObject? kids = store.Resolve(node.Dictionary["Kids"]);

            // Treat a node as a leaf when it has no /Kids, whatever its /Type says:
            // producers mislabel both directions, and content is the better signal.
            if (type == "Page" || kids is not PdfArray kidArray)
            {
                if (pages.Count >= store.Budget.Limits.MaxPageCount)
                    throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxPageCount), store.Budget.Limits.MaxPageCount);
                pages.Add(inherited.ToPage(node.Dictionary));
                continue;
            }

            if (kidArray.Count > store.Budget.Limits.MaxContainerEntries)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxContainerEntries), store.Budget.Limits.MaxContainerEntries);

            // Push in reverse so the stack yields kids in document order.
            for (int i = kidArray.Count - 1; i >= 0; i--)
            {
                if (store.Resolve(kidArray[i]) is PdfDictionary kid)
                    stack.Push(new Node(kid, inherited, node.Depth + 1));
            }
        }

        return pages;
    }

    private readonly struct Node(PdfDictionary dictionary, Inherited inherited, int depth)
    {
        public PdfDictionary Dictionary { get; } = dictionary;

        public Inherited Inherited { get; } = inherited;

        public int Depth { get; } = depth;
    }

    private readonly struct Inherited
    {
        private Inherited(PdfDictionary? resources, PdfRectangle? mediaBox, PdfRectangle? cropBox, int? rotation, double userUnit)
        {
            Resources = resources;
            MediaBox = mediaBox;
            CropBox = cropBox;
            Rotation = rotation;
            UserUnit = userUnit;
        }

        public static Inherited Empty => new(null, null, null, null, 1d);

        public PdfDictionary? Resources { get; }

        public PdfRectangle? MediaBox { get; }

        public PdfRectangle? CropBox { get; }

        public int? Rotation { get; }

        public double UserUnit { get; }

        public Inherited Merge(PdfObjectStore store, PdfDictionary dictionary)
        {
            PdfDictionary? resources = store.Resolve(dictionary["Resources"]) as PdfDictionary ?? Resources;
            PdfRectangle? mediaBox = ReadRectangle(store, dictionary["MediaBox"]) ?? MediaBox;
            PdfRectangle? cropBox = ReadRectangle(store, dictionary["CropBox"]) ?? CropBox;
            int? rotation = store.Resolve(dictionary["Rotate"]) is PdfNumber number ? number.ToInt32() : Rotation;

            // UserUnit is a page attribute, not an inheritable one, but carrying the
            // parent value costs nothing and a nested override still wins.
            double userUnit = store.Resolve(dictionary["UserUnit"]) is PdfNumber unit && unit.Value > 0 && double.IsFinite(unit.Value)
                ? unit.Value
                : UserUnit;

            return new Inherited(resources, mediaBox, cropBox, rotation, userUnit);
        }

        public PdfPage ToPage(PdfDictionary dictionary)
        {
            PdfRectangle media = MediaBox is { IsUsable: true } box ? box : PdfRectangle.DefaultMediaBox;

            // A crop box reaching past the media box shows only what the two share
            // (clause 14.11.2), and one that shares nothing with it shows nothing
            // a reader could measure, so the media box stands in.
            PdfRectangle crop = CropBox is { IsUsable: true } cropped ? Clip(cropped, media) ?? media : media;
            return new PdfPage(dictionary, Resources, media, crop, NormalizeRotation(Rotation ?? 0), UserUnit);
        }

        private static PdfRectangle? Clip(PdfRectangle box, PdfRectangle within)
        {
            var clipped = new PdfRectangle(
                Math.Max(box.Left, within.Left),
                Math.Max(box.Bottom, within.Bottom),
                Math.Min(box.Right, within.Right),
                Math.Min(box.Top, within.Top));

            return box.Left < within.Right && within.Left < box.Right &&
                box.Bottom < within.Top && within.Bottom < box.Top
                ? clipped
                : null;
        }

        private static int NormalizeRotation(int rotation)
        {
            int normalized = rotation % 360;
            if (normalized < 0)
                normalized += 360;
            // Rotation must be a multiple of 90; anything else is treated as none.
            return normalized % 90 == 0 ? normalized : 0;
        }

        private static PdfRectangle? ReadRectangle(PdfObjectStore store, PdfObject? value)
        {
            if (store.Resolve(value) is not PdfArray array || array.Count < 4)
                return null;

            Span<double> coordinates = stackalloc double[4];
            for (int i = 0; i < 4; i++)
            {
                if (store.Resolve(array[i]) is not PdfNumber number || !double.IsFinite(number.Value))
                    return null;
                coordinates[i] = number.Value;
            }

            var rectangle = new PdfRectangle(coordinates[0], coordinates[1], coordinates[2], coordinates[3]);
            return rectangle.IsUsable ? rectangle : null;
        }
    }
}
