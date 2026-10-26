using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf;

/// <summary>The RTF 1.5 drawing-object destination, distinct from modern shape instructions.</summary>
internal sealed class RtfLegacyDrawing(int ownerDepth)
{
    public int OwnerDepth { get; } = ownerDepth;
    private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
    private string? _primitive;
    private bool _unsupported;
    private string _horizontal = "dobxcolumn", _vertical = "dobymargin";

    public void Read(string keyword, int value, bool hasValue)
    {
        if (keyword is "dobxpage" or "dobxmargin" or "dobxcolumn") _horizontal = keyword;
        else if (keyword is "dobypage" or "dobymargin" or "dobypara") _vertical = keyword;
        else if (keyword is "dprect" or "dpline" or "dpellipse")
        {
            if (_primitive is not null) _unsupported = true;
            _primitive = keyword;
        }
        else if (keyword is "dpgroup" or "dpendgroup" or "dppolyline" or "dppolygon" or "dptxbx" or "dparc" or "dpcallout")
            _unsupported = true;
        else if (keyword.StartsWith("dp", StringComparison.Ordinal) || keyword == "dodhgt")
            _values[keyword] = hasValue ? value : 1;
    }

    public DocumentShape? Build(int paragraph, double marginLeft, double marginTop, List<DocumentDiagnostic> diagnostics)
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
        if (_values.ContainsKey("dproundr") || _values.GetValueOrDefault("dpfillpat") > 1 ||
            _values.ContainsKey("dplinedash") || _values.ContainsKey("dplinedot"))
            Report("Legacy RTF rounded corners, patterned paint or line decoration were approximated by solid rectangular geometry.");
        double x = _values.GetValueOrDefault("dpx") / 20d, y = _values.GetValueOrDefault("dpy") / 20d;
        double width = _values.GetValueOrDefault("dpxsize") / 20d, height = _values.GetValueOrDefault("dpysize") / 20d;
        if (_horizontal == "dobxpage") x -= marginLeft;
        if (_vertical == "dobymargin") y += marginTop;
        if (width < 0) x += width;
        if (height < 0) y += height;
        bool line = _primitive == "dpline";
        bool hollow = _values.ContainsKey("dplinehollow");
        BColor Color(string prefix) => BColor.FromArgb(
            (byte)Math.Clamp(_values.GetValueOrDefault(prefix + "r"), 0, 255),
            (byte)Math.Clamp(_values.GetValueOrDefault(prefix + "g"), 0, 255),
            (byte)Math.Clamp(_values.GetValueOrDefault(prefix + "b"), 0, 255));
        bool hasFill = !line && (_values.GetValueOrDefault("dpfillpat") > 0 ||
            _values.ContainsKey("dpfillbgcr") || _values.ContainsKey("dpfillbgcg") || _values.ContainsKey("dpfillbgcb"));
        if (width == 0 && height == 0 || !line && (width == 0 || height == 0)) return null;
        return new DocumentShape(paragraph, x, y, Math.Abs(width), Math.Abs(height),
            fill: hasFill ? ShapeFill.Solid(Color("dpfillbgc")) : null,
            outline: hollow ? BColor.Empty :
                _values.ContainsKey("dplinecor") || _values.ContainsKey("dplinecog") || _values.ContainsKey("dplinecob")
                    ? Color("dplineco") : BColor.FromArgb(52, 101, 164), paragraphs: null,
            offsetFromPage: _vertical != "dobypara", zOrder: _values.GetValueOrDefault("dodhgt"),
            geometry: line ? (width * height < 0 ? ShapeGeometry.LineAscending : ShapeGeometry.Line) :
                _primitive == "dpellipse" ? ShapeGeometry.Ellipse : ShapeGeometry.Rectangle,
            outlineWidth: _values.TryGetValue("dplinew", out int pen) && pen >= 0 ? pen / 20d : 0.75);
    }
}
