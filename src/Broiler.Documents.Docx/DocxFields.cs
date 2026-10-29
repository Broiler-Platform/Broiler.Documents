using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>
/// The complex fields open in one flow: ECMA-376 §17.16.18's
/// <c>w:fldChar</c> begin, the <c>w:instrText</c> that spells the field, the
/// separator, the runs of its last result and the end, which may lie in
/// different runs and nest.
/// </summary>
/// <remarks>
/// <para>
/// The reader draws every field from the result Word saved, which is the text
/// between the separator and the end; the instruction is not text. What this
/// adds is knowing which field a result belongs to, for the fields the layout
/// works out again on each page. One is recognised so far: <c>STYLEREF</c>
/// naming a character style (<see cref="StyleReferenceField"/>). Its result runs
/// carry the field.
/// </para>
/// <para>
/// A <c>STYLEREF</c> naming a paragraph style, Word's heading-number shorthand
/// (<c>STYLEREF 1</c>), or one with a switch that numbers, positions or recases
/// its text keeps its saved result as every other field does.
/// </para>
/// </remarks>
internal sealed class DocxFields
{
    private readonly List<Field> _open = [];

    /// <summary>
    /// The style reference whose result is being read, or null where the
    /// innermost open field is not one or is still being spelled.
    /// </summary>
    public StyleReferenceField? Result => _open.Count > 0 && _open[^1].Separated ? _open[^1].StyleReference : null;

    /// <summary>A run's style, carrying the field whose result it is part of.</summary>
    public InlineStyle Apply(InlineStyle style) =>
        style.StyleReference is null && Result is StyleReferenceField field ? style with { StyleReference = field } : style;

    /// <summary>Follows a <c>w:fldChar</c>.</summary>
    public void Read(XElement fieldCharacter, DocxStyles styles)
    {
        switch ((string?)fieldCharacter.Attribute(DocxNamespaces.Wordprocessing + "fldCharType"))
        {
            case "begin":
                _open.Add(new Field());
                break;
            case "separate" when _open.Count > 0 && !_open[^1].Separated:
                _open[^1].Separated = true;
                _open[^1].StyleReference = StyleReference(_open[^1].Code.ToString(), styles);
                break;
            case "end" when _open.Count > 0:
                _open.RemoveAt(_open.Count - 1);
                break;
        }
    }

    /// <summary>Adds a <c>w:instrText</c> to the instruction of the field being spelled.</summary>
    public void Instruction(string text)
    {
        if (_open.Count > 0 && !_open[^1].Separated)
            _open[^1].Code.Append(text);
    }

    /// <summary>
    /// The style reference an instruction spells, or null for any other field
    /// and for a <c>STYLEREF</c> this does not work out.
    /// </summary>
    public static StyleReferenceField? StyleReference(string instruction, DocxStyles styles)
    {
        List<string> tokens = Tokens(instruction);
        if (tokens.Count < 2 || !tokens[0].Equals("STYLEREF", StringComparison.OrdinalIgnoreCase))
            return null;

        bool fromBottom = false;
        for (int i = 2; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (token.Equals("\\l", StringComparison.OrdinalIgnoreCase))
            {
                fromBottom = true;
            }
            else if (token == "\\*" && i + 1 < tokens.Count &&
                tokens[i + 1].ToUpperInvariant() is "MERGEFORMAT" or "CHARFORMAT")
            {
                // Says only where the result's formatting comes from.
                i++;
            }
            else
            {
                return null;
            }
        }

        return styles.CharacterStyleCalled(tokens[1]) is string name ? new StyleReferenceField(name, fromBottom) : null;
    }

    /// <summary>
    /// An instruction's words: a quoted argument is one, with its quotes taken
    /// off, and a switch is one.
    /// </summary>
    private static List<string> Tokens(string instruction)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < instruction.Length)
        {
            if (char.IsWhiteSpace(instruction[i]))
            {
                i++;
                continue;
            }

            if (instruction[i] == '"')
            {
                int close = instruction.IndexOf('"', i + 1);
                if (close < 0)
                    close = instruction.Length;
                tokens.Add(instruction[(i + 1)..close]);
                i = close + 1;
                continue;
            }

            int start = i;
            while (i < instruction.Length && !char.IsWhiteSpace(instruction[i]) && instruction[i] != '"')
                i++;
            tokens.Add(instruction[start..i]);
        }

        return tokens;
    }

    private sealed class Field
    {
        public StringBuilder Code { get; } = new();

        public bool Separated { get; set; }

        public StyleReferenceField? StyleReference { get; set; }
    }
}
