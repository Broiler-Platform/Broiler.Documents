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
/// works out again on each page: <c>STYLEREF</c> naming a character style
/// (<see cref="StyleReferenceField"/>), and <c>PAGE</c> and <c>NUMPAGES</c>
/// (<see cref="PageField"/>). Their result runs carry the field.
/// </para>
/// <para>
/// A page field matters in a header or footer, which is drawn again on every
/// page: the result Word saved is the number of whichever page it last drew
/// the footer on, so a report whose footer read "Seite 14 von 19" said so on
/// all nineteen pages.
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

    /// <summary>
    /// A run's style, carrying the field whose result it is part of. A page
    /// field's result is drawn as the run holding its separator is formatted,
    /// not as the saved result's runs are: LibreOffice inserts the field there
    /// (writerfilter's <c>CloseFieldCommand</c>) and never reads the result. A
    /// footer whose field characters state 12 points around a result saved
    /// without a size shows its page numbers in 12 points.
    /// </summary>
    public InlineStyle Apply(InlineStyle style)
    {
        if (_open.Count > 0 && _open[^1].Separated && _open[^1].PageStyle is InlineStyle page)
            return page;
        if (style.StyleReference is null && Result is StyleReferenceField field)
            style = style with { StyleReference = field };
        return style;
    }

    /// <summary>
    /// Whether what is being read is the result of a <c>TOC</c> field. LibreOffice
    /// gives the runs there no character style, Word's <c>Hyperlink</c> on each
    /// entry included: they "will receive later another style references from
    /// TOC" (writerfilter's <c>DomainMapper</c>, <c>LN_EG_RPrBase_rStyle</c>), and
    /// the entry's link gets the unformatted <c>Index Link</c>. A table of
    /// contents Word saved in blue, underlined links is drawn in its paragraph
    /// styles' colour.
    /// </summary>
    public bool InTableOfContents => _open.Exists(static open => open.Separated && open.TableOfContents);

    /// <summary>Notes that the innermost field's result has text of its own.</summary>
    public void NoteResultText()
    {
        if (_open.Count > 0 && _open[^1].Separated)
            _open[^1].HasResultText = true;
    }

    /// <summary>
    /// Follows a <c>w:fldChar</c>. Returns the page field a field that ends
    /// here without any result text stands for, which the caller writes as
    /// the first page's number for the layout to replace; null otherwise.
    /// </summary>
    public PageField? Read(XElement fieldCharacter, DocxStyles styles, InlineStyle runStyle)
    {
        switch ((string?)fieldCharacter.Attribute(DocxNamespaces.Wordprocessing + "fldCharType"))
        {
            case "begin":
                _open.Add(new Field());
                break;
            case "separate" when _open.Count > 0 && !_open[^1].Separated:
                _open[^1].Separated = true;
                _open[^1].StyleReference = StyleReference(_open[^1].Code.ToString(), styles);
                _open[^1].Page = Page(_open[^1].Code.ToString());
                if (_open[^1].Page is PageField separated)
                    _open[^1].PageStyle = runStyle with { PageField = separated };
                _open[^1].TableOfContents = Tokens(_open[^1].Code.ToString()) is [string name, ..] &&
                    name.Equals("TOC", StringComparison.OrdinalIgnoreCase);
                break;
            case "end" when _open.Count > 0:
                Field ended = _open[^1];
                _open.RemoveAt(_open.Count - 1);
                if (!ended.HasResultText && (ended.Page ?? Page(ended.Code.ToString())) is PageField empty)
                    return empty;
                break;
        }

        return null;
    }

    /// <summary>
    /// The page field an instruction spells: <c>PAGE</c> or <c>NUMPAGES</c>,
    /// optionally with a <c>\*</c> switch that writes the number in roman
    /// numerals or letters. Null for any other field and for a switch this
    /// does not work out, which keeps the saved result.
    /// </summary>
    public static PageField? Page(string instruction)
    {
        List<string> tokens = Tokens(instruction);
        if (tokens.Count == 0)
            return null;

        PageFieldKind kind;
        if (tokens[0].Equals("PAGE", StringComparison.OrdinalIgnoreCase))
            kind = PageFieldKind.PageNumber;
        else if (tokens[0].Equals("NUMPAGES", StringComparison.OrdinalIgnoreCase))
            kind = PageFieldKind.PageCount;
        else
            return null;

        ListNumberFormat format = ListNumberFormat.Decimal;
        for (int i = 1; i < tokens.Count; i++)
        {
            if (tokens[i] != "\\*" || i + 1 >= tokens.Count)
                return null;

            // Word reads the case of the format's first letters: roman is
            // i, ii, iii and ROMAN is I, II, III.
            string name = tokens[++i];
            switch (name.ToUpperInvariant())
            {
                case "MERGEFORMAT" or "CHARFORMAT":
                    break;
                case "ARABIC":
                    format = ListNumberFormat.Decimal;
                    break;
                case "ROMAN":
                    format = name.StartsWith("RO", StringComparison.Ordinal) ? ListNumberFormat.UpperRoman : ListNumberFormat.LowerRoman;
                    break;
                case "ALPHABETIC":
                    format = name.StartsWith("AL", StringComparison.Ordinal) ? ListNumberFormat.UpperLetter : ListNumberFormat.LowerLetter;
                    break;
                default:
                    return null;
            }
        }

        return new PageField(kind, format);
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

        public PageField? Page { get; set; }

        public InlineStyle? PageStyle { get; set; }

        public bool HasResultText { get; set; }

        public bool TableOfContents { get; set; }
    }
}
