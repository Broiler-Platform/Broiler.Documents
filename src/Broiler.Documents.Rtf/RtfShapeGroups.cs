using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;

namespace Broiler.Documents.Rtf;

/// <summary>Expands supported RTF drawing groups into independent positioned shapes.
/// Child relative coordinates remain local until every parent scale/origin is applied.</summary>
internal static class RtfShapeGroups
{
    private sealed record Shape(int Start, int End, int Instructions, int InstructionEnd,
        Dictionary<string, long> Properties, Dictionary<string, int> Controls);
    private sealed record Frame(double Left, double Top, double Right, double Bottom,
        double OriginX, double OriginY, double ScaleX, double ScaleY, int Z, int Horizontal, int Vertical,
        int Behind, int Wrap, int Side);

    public static IReadOnlyList<RtfToken> Expand(IReadOnlyList<RtfToken> tokens, int maxDepth, List<DocumentDiagnostic> diagnostics)
    {
        if (!tokens.Any(t => t.Type == RtfTokenType.ControlWord && t.Keyword == "shpgrp")) return tokens;
        var ends = new Dictionary<int, int>();
        var starts = new Stack<int>();
        for (int i = 0; i < tokens.Count; i++)
            if (tokens[i].Type == RtfTokenType.GroupStart) starts.Push(i);
            else if (tokens[i].Type == RtfTokenType.GroupEnd && starts.Count > 0) ends[starts.Pop()] = i;
        var output = new List<RtfToken>(tokens.Count);
        Visit(0, tokens.Count);
        return output;

        void Report(string message)
        {
            if (!diagnostics.Any(d => d.Code == "rtf.shape.group"))
                diagnostics.Add(DocumentDiagnostic.Warning("rtf.shape.group", message));
        }
        string Head(int start, int end)
        {
            for (int i = start + 1; i < end; i++)
            {
                if (tokens[i].Type == RtfTokenType.ControlSymbol && tokens[i].Symbol == '*') continue;
                if (tokens[i].Type == RtfTokenType.Text && string.IsNullOrWhiteSpace(tokens[i].Text)) continue;
                return tokens[i].Type == RtfTokenType.ControlWord ? tokens[i].Keyword : "";
            }
            return "";
        }
        IEnumerable<int> Children(int start, int end)
        {
            for (int i = start + 1; i < end; i++)
                if (tokens[i].Type == RtfTokenType.GroupStart && ends.TryGetValue(i, out int last))
                { yield return i; i = last; }
        }
        Shape? Read(int start)
        {
            int end = ends[start];
            int instruction = Children(start, end).FirstOrDefault(i => Head(i, ends[i]) == "shpinst", -1);
            if (instruction < 0) return null;
            int last = ends[instruction];
            var properties = new Dictionary<string, long>(StringComparer.Ordinal);
            var controls = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = instruction + 1; i < last; i++)
            {
                if (tokens[i].Type == RtfTokenType.GroupStart && ends.TryGetValue(i, out int close))
                {
                    if (Head(i, close) == "sp")
                    {
                        string name = "", value = "";
                        foreach (int part in Children(i, close))
                        {
                            var text = new StringBuilder();
                            for (int j = part + 1; j < ends[part]; j++) if (tokens[j].Type == RtfTokenType.Text) text.Append(tokens[j].Text);
                            if (Head(part, ends[part]) == "sn") name = text.ToString().Trim();
                            if (Head(part, ends[part]) == "sv") value = text.ToString().Trim();
                        }
                        if (name.Length > 0 && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)) properties[name] = number;
                    }
                    i = close;
                }
                else if (tokens[i].Type == RtfTokenType.ControlWord)
                    controls[tokens[i].Keyword] = tokens[i].HasParameter ? tokens[i].Parameter : 1;
            }
            return new(start, end, instruction, last, properties, controls);
        }
        void Visit(int begin, int end)
        {
            for (int i = begin; i < end; i++)
            {
                if (tokens[i].Type == RtfTokenType.GroupStart && ends.TryGetValue(i, out int close))
                {
                    string head = Head(i, close);
                    if (head is "info" or "pict" or "object" or "stylesheet" or "listtable" or "listoverridetable" or "fldinst" or "footnote" or
                        "header" or "headerf" or "headerl" or "headerr" or "footer" or "footerf" or "footerl" or "footerr" ||
                        i + 1 < close && tokens[i + 1].Type == RtfTokenType.ControlSymbol && tokens[i + 1].Symbol == '*' &&
                        head is not ("shpinst" or "background"))
                    {
                        for (int j = i; j <= close; j++) output.Add(tokens[j]);
                    }
                    else if (head == "shpgrp")
                    {
                        var flattened = Read(i) is Shape group ? Flatten(group, null, 0) : null;
                        if (flattened is not null) output.AddRange(flattened);
                        else for (int j = i; j <= close; j++) output.Add(tokens[j]);
                    }
                    else
                    {
                        output.Add(tokens[i]); Visit(i + 1, close); output.Add(tokens[close]);
                    }
                    i = close;
                }
                else output.Add(tokens[i]);
            }
        }
        List<RtfToken>? Flatten(Shape group, Frame? parent, int depth)
        {
            var p = group.Properties; var c = group.Controls;
            if (depth >= Math.Min(maxDepth, 16) || p.GetValueOrDefault("rotation") != 0 ||
                p.GetValueOrDefault("fFlipH") != 0 || p.GetValueOrDefault("fFlipV") != 0 ||
                p.GetValueOrDefault("posh") != 0 || p.GetValueOrDefault("posv") != 0)
            { Report("An RTF group used an unsupported transform or exceeded the drawing depth limit; its existing fallback was retained."); return null; }
            if (parent is null && c.GetValueOrDefault("shpwr", 3) is not (3 or 5) ||
                Children(group.Instructions, group.InstructionEnd).Any(i => Head(i, ends[i]) is "pict" or "object" or "shptxt" or "shprslt"))
            { Report("An RTF group used aggregate wrapping or unsupported group-level content; its existing fallback was retained."); return null; }
            double left, top, right, bottom;
            if (parent is null)
            {
                left = c.GetValueOrDefault("shpleft"); top = c.GetValueOrDefault("shptop");
                right = c.GetValueOrDefault("shpright"); bottom = c.GetValueOrDefault("shpbottom");
            }
            else if (c.Keys.Any(key => key is "shpleft" or "shptop" or "shpright" or "shpbottom"))
            { Report("An RTF nested group combined absolute and relative bounds; its existing fallback was retained."); return null; }
            else if (!Bounds(p, parent, out left, out top, out right, out bottom)) return null;
            double ox = p.GetValueOrDefault("groupLeft"), oy = p.GetValueOrDefault("groupTop");
            if (!p.TryGetValue("groupRight", out long gx) || !p.TryGetValue("groupBottom", out long gy) || gx <= ox || gy <= oy || right <= left || bottom <= top)
            { Report("An RTF group lacked usable frame or coordinate dimensions; its existing fallback was retained."); return null; }
            int z = parent?.Z ?? (c.TryGetValue("shpz", out int order) ? order : (int)Math.Clamp(p.GetValueOrDefault("dhgt"), int.MinValue, int.MaxValue));
            int horizontal = parent?.Horizontal ?? (p.TryGetValue("posrelh", out long h) ? (int)h : c.ContainsKey("shpbxpage") ? 1 : 2);
            int vertical = parent?.Vertical ?? (p.TryGetValue("posrelv", out long v) ? (int)v : c.ContainsKey("shpbypage") ? 1 : c.ContainsKey("shpbymargin") ? 0 : 2);
            var frame = new Frame(left, top, right, bottom, ox, oy, (right - left) / (gx - ox), (bottom - top) / (gy - oy), z, horizontal, vertical,
                parent?.Behind ?? (p.TryGetValue("fBehindDocument", out long behind) ? (int)behind : c.GetValueOrDefault("shpfblwtxt")),
                parent?.Wrap ?? c.GetValueOrDefault("shpwr", 3), parent?.Side ?? c.GetValueOrDefault("shpwrk", 3));
            var result = new List<RtfToken>();
            var children = Children(group.Instructions, group.InstructionEnd)
                .Where(i => Head(i, ends[i]) is "shp" or "shpgrp").Select(Read).Where(s => s is not null).Select(s => s!)
                .OrderBy(s => s.Controls.TryGetValue("shpz", out int value) ? value : s.Properties.GetValueOrDefault("dhgt")).ToArray();
            foreach (Shape child in children)
            {
                if (Head(child.Start, child.End) == "shpgrp")
                {
                    var nested = Flatten(child, frame, depth + 1);
                    if (nested is null) return null;
                    result.AddRange(nested); continue;
                }
                if (child.Controls.Keys.Any(key => key is "shpleft" or "shptop" or "shpright" or "shpbottom"))
                { Report("An RTF group child combined absolute and relative bounds; its existing fallback was retained."); return null; }
                if (!Bounds(child.Properties, frame, out double x1, out double y1, out double x2, out double y2)) return null;
                double[] values = [x1, y1, x2, y2];
                if (values.Any(value => !double.IsFinite(value) || value < int.MinValue || value > int.MaxValue))
                { Report("An RTF group mapped a child outside supported coordinates; its existing fallback was retained."); return null; }
                for (int i = child.Start; i <= child.End; i++)
                {
                    if (i == child.InstructionEnd)
                    {
                        string[] names = ["shpleft", "shptop", "shpright", "shpbottom"];
                        for (int j = 0; j < 4; j++) result.Add(RtfToken.ControlWord(names[j], true, (int)Math.Round(values[j])));
                        result.Add(RtfToken.ControlWord("shpz", true, frame.Z));
                        result.Add(RtfToken.ControlWord("shpfblwtxt", true, frame.Behind));
                        result.Add(RtfToken.ControlWord("shpwr", true, frame.Wrap));
                        result.Add(RtfToken.ControlWord("shpwrk", true, frame.Side));
                        AddProperty(result, "posrelh", frame.Horizontal); AddProperty(result, "posrelv", frame.Vertical);
                        AddProperty(result, "posh", 0); AddProperty(result, "posv", 0);
                    }
                    result.Add(tokens[i]);
                }
            }
            if (children.Length == 0) { Report("An RTF drawing group had no supported shape children; its existing fallback was retained."); return null; }
            return result;
        }
        bool Bounds(Dictionary<string, long> properties, Frame frame, out double left, out double top, out double right, out double bottom)
        {
            left = top = right = bottom = 0;
            if (!properties.TryGetValue("relLeft", out long l) || !properties.TryGetValue("relTop", out long t) ||
                !properties.TryGetValue("relRight", out long r) || !properties.TryGetValue("relBottom", out long b))
            { Report("An RTF group child lacked relative bounds; its existing fallback was retained."); return false; }
            left = frame.Left + (l - frame.OriginX) * frame.ScaleX; right = frame.Left + (r - frame.OriginX) * frame.ScaleX;
            top = frame.Top + (t - frame.OriginY) * frame.ScaleY; bottom = frame.Top + (b - frame.OriginY) * frame.ScaleY;
            return true;
        }
    }
    private static void AddProperty(List<RtfToken> output, string name, int value)
    {
        output.Add(RtfToken.GroupStart); output.Add(RtfToken.ControlWord("sp", false, 0));
        output.Add(RtfToken.GroupStart); output.Add(RtfToken.ControlWord("sn", false, 0)); output.Add(RtfToken.TextRun(name)); output.Add(RtfToken.GroupEnd);
        output.Add(RtfToken.GroupStart); output.Add(RtfToken.ControlWord("sv", false, 0)); output.Add(RtfToken.TextRun(value.ToString(CultureInfo.InvariantCulture))); output.Add(RtfToken.GroupEnd);
        output.Add(RtfToken.GroupEnd);
    }
}
