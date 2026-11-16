using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// Shows each <c>DOCVARIABLE</c> field in the body as the value of the document
/// variable it names (<c>w:settings/w:docVars</c>, ECMA-376 §17.15.1.32), not
/// as the result Word saved.
/// </summary>
/// <remarks>
/// LibreOffice imports the field as a user field whose content is the
/// variable's value: tdf150542.docx's field, saved as "JUNK VALUE", shows
/// "Correct value (árvízturo tükörfúrógép)" in LibreOffice 24.2. A field that
/// names a variable the settings do not hold keeps its saved result, and so
/// does one whose result runs over a paragraph mark. The value's
/// <c>_xHHHH_</c> escapes (ST_Xstring) are decoded; LibreOffice 24.2 shows them
/// raw, a defect later releases fixed (tdf164176.docx).
/// </remarks>
internal static class DocxDocumentVariables
{
    private static readonly XNamespace Word = DocxNamespaces.Wordprocessing;

    public static void Apply(XDocument document, XElement? settings, DocumentLimits limits)
    {
        XElement? variables = settings?.Element(Word + "docVars");
        if (variables is null)
            return;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement variable in variables.Elements(Word + "docVar"))
        {
            if ((string?)variable.Attribute(Word + "name") is { Length: > 0 } name && (string?)variable.Attribute(Word + "val") is string value)
                values.TryAdd(name, Decode(value));
        }

        if (values.Count == 0)
            return;

        foreach (XElement simple in document.Descendants(Word + "fldSimple").ToArray())
        {
            if (Value((string?)simple.Attribute(Word + "instr"), values) is string value && value.Length <= limits.MaxRunLength)
            {
                XElement? properties = simple.Element(Word + "r")?.Element(Word + "rPr");
                simple.Elements(Word + "r").Remove();
                simple.Add(Run(properties, value));
            }
        }

        foreach (XElement paragraph in document.Descendants(Word + "p").ToArray())
            ApplyComplexFields(paragraph, values, limits);
    }

    /// <summary>
    /// Replaces the result runs of each complex <c>DOCVARIABLE</c> field that
    /// begins, separates and ends among <paramref name="paragraph"/>'s own runs.
    /// </summary>
    private static void ApplyComplexFields(XElement paragraph, Dictionary<string, string> values, DocumentLimits limits)
    {
        List<XElement> runs = [.. paragraph.Elements(Word + "r")];
        for (int begin = 0; begin < runs.Count; begin++)
        {
            if (FieldCharacter(runs[begin]) != "begin")
                continue;

            var instruction = new StringBuilder();
            int separate = -1, end = -1;
            for (int i = begin + 1; i < runs.Count; i++)
            {
                string? character = FieldCharacter(runs[i]);
                if (character == "begin")
                    break; // a nested field: left as it is
                if (character == "separate")
                    separate = i;
                else if (character == "end")
                {
                    end = i;
                    break;
                }
                else if (separate < 0)
                    instruction.Append(string.Concat(runs[i].Elements(Word + "instrText").Select(text => text.Value)));
            }

            if (separate < 0 || end < 0 || Value(instruction.ToString(), values) is not string value || value.Length > limits.MaxRunLength)
                continue;

            XElement? properties = runs.Skip(separate + 1).Take(end - separate - 1).FirstOrDefault()?.Element(Word + "rPr");
            for (int i = separate + 1; i < end; i++)
                runs[i].Remove();
            runs[separate].AddAfterSelf(Run(properties, value));
            begin = end;
        }
    }

    private static string? FieldCharacter(XElement run) =>
        (string?)run.Element(Word + "fldChar")?.Attribute(Word + "fldCharType");

    private static string? Value(string? instruction, Dictionary<string, string> values)
    {
        if (instruction is null)
            return null;
        Match match = Regex.Match(instruction, @"\A\s*DOCVARIABLE\s+(""[^""]*""|\S+)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
        return match.Success && values.TryGetValue(match.Groups[1].Value.Trim('"'), out string? value) ? value : null;
    }

    private static XElement Run(XElement? properties, string value)
    {
        var run = new XElement(Word + "r", properties is null ? null : new XElement(properties));
        string[] lines = value.ReplaceLineEndings("\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                run.Add(new XElement(Word + "br"));
            run.Add(new XElement(Word + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), lines[i]));
        }

        return run;
    }

    /// <summary>Decodes ST_Xstring's <c>_xHHHH_</c> escapes.</summary>
    private static string Decode(string value) =>
        Regex.Replace(value, "_x([0-9A-Fa-f]{4})_", match =>
        {
            char character = (char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return char.IsSurrogate(character) || (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
                ? string.Empty
                : character.ToString();
        });
}
