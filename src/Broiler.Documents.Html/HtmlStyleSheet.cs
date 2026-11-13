using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Dom;

namespace Broiler.Documents.Html;

/// <summary>Resolves type selectors and two-type descendant selectors from embedded CSS.</summary>
internal sealed class HtmlStyleSheet
{
    private sealed record Rule(string? Ancestor, IReadOnlyDictionary<string, string> Declarations);
    private readonly Dictionary<string, List<Rule>> _rules;

    private HtmlStyleSheet(Dictionary<string, List<Rule>> rules, bool skipped)
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
        var rules = new Dictionary<string, List<Rule>>(StringComparer.OrdinalIgnoreCase);
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
        if (!_rules.TryGetValue(element.LocalName, out List<Rule>? rules))
            return inline;

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Rule rule in rules)
            if (rule.Ancestor is null)
                ApplyDeclarations(rule);

        var ancestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (DomNode? node = element.ParentNode; node is DomElement parent; node = parent.ParentNode)
            ancestors.Add(parent.LocalName);

        foreach (Rule rule in rules)
            if (rule.Ancestor is not null && ancestors.Contains(rule.Ancestor))
                ApplyDeclarations(rule);

        void ApplyDeclarations(Rule rule)
        {
            foreach (var declaration in rule.Declarations)
                resolved[declaration.Key] = declaration.Value;
        }

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
        string css, Dictionary<string, List<Rule>> rules, ref bool skipped)
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
        Dictionary<string, List<Rule>> rules,
        ref bool skipped)
    {
        var selectors = new List<string[]>();
        foreach (string selector in prelude.Split(','))
        {
            string[] names = selector.Split([' ', '\t', '\r', '\n', '\f'], StringSplitOptions.RemoveEmptyEntries);
            if (names.Length is < 1 or > 2 || !Array.TrueForAll(names, IsTypeSelector))
            {
                skipped = true;
                return;
            }
            selectors.Add(names);
        }

        IReadOnlyDictionary<string, string> declarations =
            HtmlCss.ParseDeclarations(HtmlCss.WithoutNestedBlocks(body));
        if (declarations.Count == 0)
            return;

        foreach (string[] names in selectors)
        {
            if (!rules.TryGetValue(names[^1], out List<Rule>? matching))
                rules[names[^1]] = matching = [];
            matching.Add(new Rule(names.Length == 2 ? names[0] : null, declarations));
        }
    }

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
