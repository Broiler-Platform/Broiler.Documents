using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf;

/// <summary>The RTF 1.5 drawing-object destination, distinct from modern shape instructions.</summary>
internal sealed class RtfLegacyDrawing(int ownerDepth)
{
    public int OwnerDepth { get; } = ownerDepth;
    public int TextDepth { get; set; }
    public CellVerticalAlignment TextAlignment { get; set; }
    private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
    private readonly List<int> _pointX = [];
    private readonly List<int> _pointY = [];
    private string? _primitive;
    private bool _unsupported;
    private string _horizontal = "dobxcolumn", _vertical = "dobymargin";

    public void Read(string keyword, int value, bool hasValue)
    {
        if (keyword is "dobxpage" or "dobxmargin" or "dobxcolumn") _horizontal = keyword;
        else if (keyword is "dobypage" or "dobymargin" or "dobypara") _vertical = keyword;
        else if (keyword is "dprect" or "dpline" or "dpellipse" or "dppolyline" or "dptxbx")
        {
            if (_primitive is not null) _unsupported = true;
            _primitive = keyword;
        }
        else if (keyword is "dpgroup" or "dpendgroup" or "dppolygon" or "dparc" or "dpcallout")
            _unsupported = true;
        else if (keyword == "dpptx" && hasValue)
            _pointX.Add(value);
        else if (keyword == "dppty" && hasValue)
            _pointY.Add(value);
        else if (keyword.StartsWith("dp", StringComparison.Ordinal) || keyword == "dodhgt")
            _values[keyword] = hasValue ? value : 1;
    }

    public DocumentShape? Build(int paragraph, double marginLeft, double marginTop,
        IReadOnlyList<RichTextParagraph>? paragraphs, List<DocumentDiagnostic> diagnostics)
    {
        void Report(string message)
        {
            if (!diagnostics.Exists(d => d.Code == "rtf.drawing.legacy"))
                diagnostics.Add(DocumentDiagnostic.Warning("rtf.drawing.legacy", message));
        }
        if (_unsupported || _primitive is null)
        {
            Report("An unsupported legacy RTF drawing primitive or aggregate was skipped.");
            return null;
        }
        bool isPolyline = _primitive == "dppolyline";
        if (isPolyline && (_pointX.Count != 2 || _pointY.Count != 2))
        {
            Report("An unsupported legacy RTF drawing primitive or aggregate was skipped.");
            return null;
        }
        if (_values.ContainsKey("dproundr") || _values.GetValueOrDefault("dpfillpat") > 1 ||
            _values.ContainsKey("dplinedash") || _values.ContainsKey("dplinedot"))
            Report("Legacy RTF rounded corners, patterned paint or line decoration were approximated by solid rectangular geometry.");
        double x = _values.GetValueOrDefault("dpx") / 20d, y = _values.GetValueOrDefault("dpy") / 20d;
        double width = _values.GetValueOrDefault("dpxsize") / 20d, height = _values.GetValueOrDefault("dpysize") / 20d;
        if (_horizontal == "dobxpage") x -= marginLeft;
        if (_vertical == "dobymargin") y += marginTop;
        if (width < 0) x += width;
        if (height < 0) y += height;
        bool line = _primitive is "dpline" or "dppolyline";
        bool hollow = _values.ContainsKey("dplinehollow");
        BColor Color(string prefix) => BColor.FromArgb(
            (byte)Math.Clamp(_values.GetValueOrDefault(prefix + "r"), 0, 255),
            (byte)Math.Clamp(_values.GetValueOrDefault(prefix + "g"), 0, 255),
            (byte)Math.Clamp(_values.GetValueOrDefault(prefix + "b"), 0, 255));
        bool hasFill = !line && (_values.GetValueOrDefault("dpfillpat") > 0 ||
            _values.ContainsKey("dpfillbgcr") || _values.ContainsKey("dpfillbgcg") || _values.ContainsKey("dpfillbgcb"));
        if (width == 0 && height == 0 || !line && (width == 0 || height == 0)) return null;

        ShapeGeometry geometry;
        if (_primitive == "dpellipse")
            geometry = ShapeGeometry.Ellipse;
        else if (_primitive == "dpline")
            geometry = width * height < 0 ? ShapeGeometry.LineAscending : ShapeGeometry.Line;
        else if (isPolyline)
        {
            long dx = _pointX[1] - _pointX[0];
            long dy = _pointY[1] - _pointY[0];
            geometry = dx * dy < 0 ? ShapeGeometry.LineAscending : ShapeGeometry.Line;
        }
        else
            geometry = ShapeGeometry.Rectangle;

        bool textBox = _primitive == "dptxbx";
        double padding = Math.Max(0, _values.GetValueOrDefault("dptxbxmar")) / 20d;
        return new DocumentShape(paragraph, x, y, Math.Abs(width), Math.Abs(height),
            fill: hasFill ? ShapeFill.Solid(Color("dpfillbgc")) : null,
            outline: hollow ? BColor.Empty :
                _values.ContainsKey("dplinecor") || _values.ContainsKey("dplinecog") || _values.ContainsKey("dplinecob")
                    ? Color("dplineco") : BColor.FromArgb(52, 101, 164),
            paragraphs: textBox ? paragraphs : null,
            offsetFromPage: _vertical != "dobypara", zOrder: _values.GetValueOrDefault("dodhgt"),
            textPadding: new TableCellPadding(padding, padding, padding, padding),
            textVerticalAlignment: TextAlignment,
            textOverflow: textBox,
            geometry: geometry,
            outlineWidth: _values.TryGetValue("dplinew", out int pen) && pen >= 0 ? pen / 20d : 0.75);
    }
}
