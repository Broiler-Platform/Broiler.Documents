using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Structure;

/// <summary>A PDF rectangle in default user space units (points).</summary>
internal readonly struct PdfRectangle
{
    public PdfRectangle(double left, double bottom, double right, double top)
    {
        // PDF permits either corner order; normalize so callers can rely on
        // Left <= Right and Bottom <= Top.
        Left = Math.Min(left, right);
        Bottom = Math.Min(bottom, top);
        Right = Math.Max(left, right);
        Top = Math.Max(bottom, top);
    }

    public double Left { get; }

    public double Bottom { get; }

    public double Right { get; }

    public double Top { get; }

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
internal sealed class PdfPage
{
    public PdfPage(
        PdfDictionary dictionary,
        PdfDictionary? resources,
        PdfRectangle mediaBox,
        PdfRectangle cropBox,
        int rotation,
        double userUnit)
    {
        Dictionary = dictionary;
        Resources = resources;
        MediaBox = mediaBox;
        CropBox = cropBox;
        Rotation = rotation;
        UserUnit = userUnit;
    }

    public PdfDictionary Dictionary { get; }

    /// <summary>The page's resource dictionary, inherited from an ancestor when absent.</summary>
    public PdfDictionary? Resources { get; }

    public PdfRectangle MediaBox { get; }

    /// <summary>The crop box, defaulting to the media box (clause 7.7.3.3).</summary>
    public PdfRectangle CropBox { get; }

    /// <summary>Clockwise display rotation, normalized to 0, 90, 180, or 270.</summary>
    public int Rotation { get; }

    public double UserUnit { get; }
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

    private readonly struct Node
    {
        public Node(PdfDictionary dictionary, Inherited inherited, int depth)
        {
            Dictionary = dictionary;
            Inherited = inherited;
            Depth = depth;
        }

        public PdfDictionary Dictionary { get; }

        public Inherited Inherited { get; }

        public int Depth { get; }
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
            PdfRectangle crop = CropBox is { IsUsable: true } cropped ? cropped : media;
            return new PdfPage(dictionary, Resources, media, crop, NormalizeRotation(Rotation ?? 0), UserUnit);
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
