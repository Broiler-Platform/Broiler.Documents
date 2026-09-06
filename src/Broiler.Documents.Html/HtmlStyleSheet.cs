using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Dom;

namespace Broiler.Documents.Html;

/// <summary>
/// The type-selector rules of a document's own stylesheet, resolved as a base
/// beneath each element's <c>style</c> attribute.
/// </summary>
/// <remarks>
/// <para>
/// This codec applied CSS from <c>style</c> attributes and nowhere else, and the
/// office conformance suite reported what that costs: HTML scoring below its own
/// DOCX and ODT twins on documents carrying the same formatting. Converting DOCX
/// to HTML, LibreOffice writes both a <c>p { line-height: 115% }</c> rule and an
/// inline style on every paragraph that overrides it, so for those files the
/// stylesheet does not matter. Converting HTML to HTML it writes bare
/// <c>&lt;p&gt;</c> elements and leaves the rule to say everything - and every
/// one of those read back here with no line spacing and no spacing after, in
/// silence, because a declaration that was never looked at leaves nothing behind
/// to report.
/// </para>
/// <para>
/// What is implemented is one selector: a bare element name. A rule selecting
/// <c>p</c>, or a list every one of whose items is such a name, contributes its
/// declarations to those elements underneath whatever the element's own
/// <c>style</c> attribute says. Nothing else is - not a class, an id, an
/// attribute, a pseudo-class, a combinator, a descendant, <c>*</c>,
/// <c>!important</c>, <c>@media</c>, or an external sheet.
/// </para>
/// <para>
/// The line is drawn there because everything on the far side of it is the
/// cascade, and the cascade is a browser. Specificity here is one comparison
/// with one answer - the element beats the sheet - which can be stated in a
/// sentence and applied to an element without knowing anything about the rest of
/// the document. Class matching is not: a class rule, an id rule and a
/// descendant rule all aimed at one paragraph have to be ordered against each
/// other before any of them can be applied, and building the thing that orders
/// them is not a smaller job than building the whole engine. A document reader
/// wants the formatting a producer stated in the only way that producer states
/// it; it does not want to become a rendering engine to get it.
/// </para>
/// <para>
/// A selector list is all or nothing. <c>h1, .lead { ... }</c> applies to
/// neither rather than to the <c>h1</c> alone, because applying the half this
/// codec understands would return a document in which one of two elements the
/// author deliberately styled together came back styled and the other did not,
/// with nothing in the result to say which had happened. Skipping the whole rule
/// is the answer that stays honest, and it is reported rather than silent.
/// </para>
/// <para>
/// Two type rules stating the same property for the same element resolve by
/// source order, the later winning, which is what CSS says for two rules of
/// equal specificity - and unlike the class case it needs no specificity
/// comparison to say so.
/// </para>
/// </remarks>
internal sealed class HtmlStyleSheet
{
    private readonly Dictionary<string, Dictionary<string, string>> _rules;

    private HtmlStyleSheet(Dictionary<string, Dictionary<string, string>> rules, bool skipped)
    {
        _rules = rules;
        HasSkippedRule = skipped;
    }

    /// <summary>
    /// Whether any rule was passed over because this codec does not implement it.
    /// </summary>
    /// <remarks>
    /// The reader turns this into one diagnostic per read. It is worth reporting
    /// for the reason the page break in a stylesheet was not: that break was
    /// never parsed, so there was genuinely nothing to notice, whereas a rule
    /// skipped here was read, understood to be formatting aimed at this document,
    /// and dropped anyway. A loss the codec can see is a loss the codec has to
    /// name.
    /// </remarks>
    public bool HasSkippedRule { get; }

    /// <summary>Reads the type rules out of a document's stylesheet text.</summary>
    public static HtmlStyleSheet Parse(string? css)
    {
        var rules = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        bool skipped = false;
        if (!string.IsNullOrWhiteSpace(css))
            Collect(WithoutComments(css), rules, ref skipped);

        return new HtmlStyleSheet(rules, skipped);
    }

    /// <summary>
    /// The declarations that apply to one element: its type rule with its own
    /// <c>style</c> attribute laid over the top.
    /// </summary>
    /// <remarks>
    /// One call site per question the reader asks of an element's style, and all
    /// of them go through here. Resolving the sheet for the paragraph properties
    /// and not for the inline ones - or for either and not for the page break -
    /// would be a distinction no document makes and no reader of this code could
    /// predict.
    /// </remarks>
    public IReadOnlyDictionary<string, string> DeclarationsFor(DomElement element)
    {
        IReadOnlyDictionary<string, string> inline =
            HtmlCss.ParseDeclarations(element.GetAttribute("style"));
        if (_rules.Count == 0 ||
            !_rules.TryGetValue(element.LocalName, out Dictionary<string, string>? type))
        {
            return inline;
        }

        if (inline.Count == 0)
            return type;

        var resolved = new Dictionary<string, string>(type, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> declaration in inline)
            resolved[declaration.Key] = declaration.Value;

        return resolved;
    }

    /// <summary>
    /// Walks the stylesheet rule by rule.
    /// </summary>
    /// <remarks>
    /// A scan rather than a CSS parser, and it stays one on purpose: what it has
    /// to survive is real stylesheet punctuation, not arbitrary CSS. Blocks are
    /// consumed by counting depth so that an <c>@media</c> takes its nested rules
    /// with it when it goes, and a statement at-rule is ended at its semicolon
    /// rather than at the next brace - <c>@charset "utf-8"; p { ... }</c> read
    /// the other way would swallow the <c>p</c> selector into the at-rule's
    /// prelude and lose the rule after it, which is the class of bug this file
    /// was written to end rather than to add.
    /// </remarks>
    private static void Collect(
        string css, Dictionary<string, Dictionary<string, string>> rules, ref bool skipped)
    {
        int index = 0;
        while (index < css.Length)
        {
            int open = css.IndexOf('{', index);
            int semicolon = css.IndexOf(';', index);
            if (semicolon >= 0 && (open < 0 || semicolon < open))
            {
                if (css[index..semicolon].Trim().Length > 0)
                    skipped = true;

                index = semicolon + 1;
                continue;
            }

            if (open < 0)
                return;

            string prelude = css[index..open].Trim();
            int close = EndOfBlock(css, open);
            string body = css[(open + 1)..close];
            index = close + 1;

            if (prelude.StartsWith('@'))
            {
                // @page is the one at-rule this codec reads, and HtmlPage reads
                // it from this same text. Everything else - @media, @supports,
                // @font-face - is skipped whole, its nested rules with it.
                if (!prelude.StartsWith("@page", StringComparison.OrdinalIgnoreCase))
                    skipped = true;

                continue;
            }

            if (prelude.Length > 0)
                Apply(prelude, body, rules, ref skipped);
        }
    }

    /// <summary>
    /// The index of the brace closing the block that opens at <paramref name="open"/>,
    /// or the end of the text when the document never closes it.
    /// </summary>
    private static int EndOfBlock(string css, int open)
    {
        int depth = 0;
        for (int index = open; index < css.Length; index++)
        {
            if (css[index] == '{')
                depth++;
            else if (css[index] == '}' && --depth == 0)
                return index;
        }

        return css.Length;
    }

    /// <summary>
    /// Records one rule against every element its selector list names, or
    /// against none of them.
    /// </summary>
    private static void Apply(
        string prelude,
        string body,
        Dictionary<string, Dictionary<string, string>> rules,
        ref bool skipped)
    {
        string[] selectors = prelude.Split(',');
        foreach (string selector in selectors)
        {
            if (IsTypeSelector(selector.Trim()))
                continue;

            skipped = true;
            return;
        }

        IReadOnlyDictionary<string, string> declarations =
            HtmlCss.ParseDeclarations(HtmlCss.WithoutNestedBlocks(body));
        if (declarations.Count == 0)
            return;

        foreach (string selector in selectors)
        {
            string name = selector.Trim();
            if (!rules.TryGetValue(name, out Dictionary<string, string>? declared))
            {
                declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                rules[name] = declared;
            }

            foreach (KeyValuePair<string, string> declaration in declarations)
                declared[declaration.Key] = declaration.Value;
        }
    }

    /// <summary>
    /// Whether a selector is a bare element name and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately stricter than "does not obviously contain a class". An HTML
    /// element name is an ASCII letter followed by letters, digits, hyphens and
    /// underscores, so anything carrying a dot, a hash, a bracket, a colon, a
    /// combinator, a space or a <c>*</c> falls out here and takes its rule with
    /// it. Testing for the characters this codec cannot handle rather than
    /// accepting the ones it can would let the next piece of selector syntax
    /// through as a type name, and it would be applied to an element nobody
    /// selected.
    /// </remarks>
    private static bool IsTypeSelector(string selector)
    {
        if (selector.Length == 0 || !char.IsAsciiLetter(selector[0]))
            return false;

        foreach (char character in selector)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The stylesheet with its comments removed, including the <c>&lt;!--</c> and
    /// <c>--&gt;</c> pair.
    /// </summary>
    /// <remarks>
    /// The HTML comment delimiters are not decoration. A word processor wraps the
    /// contents of a <c>style</c> element in them - it is the twenty-year-old
    /// habit of hiding a stylesheet from a browser too old to know the element,
    /// and CSS keeps the two tokens defined so that it stays legal. Left in
    /// place, the first rule of every LibreOffice stylesheet has a selector
    /// reading <c>&lt;!-- p</c>, which is not a type selector, so the one case
    /// this whole file was written for would have been skipped as unimplemented.
    /// </remarks>
    private static string WithoutComments(string css)
    {
        if (css.IndexOf("/*", StringComparison.Ordinal) < 0 &&
            css.IndexOf("<!--", StringComparison.Ordinal) < 0 &&
            css.IndexOf("-->", StringComparison.Ordinal) < 0)
        {
            return css;
        }

        var kept = new StringBuilder(css.Length);
        for (int index = 0; index < css.Length; index++)
        {
            if (css[index] == '/' && index + 1 < css.Length && css[index + 1] == '*')
            {
                int end = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? css.Length : end + 1;
                continue;
            }

            if (StartsAt(css, index, "<!--"))
            {
                index += 3;
                continue;
            }

            if (StartsAt(css, index, "-->"))
            {
                index += 2;
                continue;
            }

            kept.Append(css[index]);
        }

        return kept.ToString();
    }

    private static bool StartsAt(string css, int index, string token) =>
        index + token.Length <= css.Length &&
        string.CompareOrdinal(css, index, token, 0, token.Length) == 0;
}
