using System;
using System.Collections.Generic;
using System.Globalization;
using Broiler.Documents.Model;

namespace Broiler.Documents.Html;

/// <summary>
/// The CSS <c>@page</c> rule, read and written.
/// </summary>
/// <remarks>
/// <para>
/// This is where HTML keeps the paper, and it is the one part of a stylesheet
/// this codec looks at. Everything else about an HTML document's appearance is
/// a rendering question that belongs to whoever is rendering it; the page is a
/// property of the document, the way a <c>w:sectPr</c> is in DOCX and a
/// <c>style:page-layout</c> is in ODF, and a reader that ignored it turned a
/// document stating US Letter into one stating nothing at all.
/// </para>
/// <para>
/// Both directions live together on purpose. They were written apart in the
/// other codecs and drifted, and the shapes here are narrow enough that having
/// the parser and the formatter read each other is worth more than the
/// separation would be.
/// </para>
/// <para>
/// Only <c>size</c> and the margin properties are read. A real <c>@page</c> can
/// carry margin boxes, named pages, and <c>:first</c> and <c>:left</c>
/// selectors; none of them has anywhere to go in a model that holds one page per
/// document, and inventing a mapping would be worse than the gap.
/// </para>
/// </remarks>
internal static class HtmlPage
{
    /// <summary>
    /// The named page sizes CSS defines, in points, portrait.
    /// </summary>
    /// <remarks>
    /// The set is CSS's own rather than every paper a printer has heard of.
    /// A name outside it leaves the size unread rather than guessed at, which is
    /// the same answer this codec gives for every other value it does not know.
    /// </remarks>
    private static readonly Dictionary<string, (double Width, double Height)> NamedSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["a3"] = (841.89, 1190.55),
            ["a4"] = (595.276, 841.89),
            ["a5"] = (419.528, 595.276),
            ["b4"] = (708.661, 1000.63),
            ["b5"] = (498.898, 708.661),
            ["jis-b4"] = (728.504, 1031.81),
            ["jis-b5"] = (515.906, 728.504),
            ["letter"] = (612, 792),
            ["legal"] = (612, 1008),
            ["ledger"] = (1224, 792),
        };

    /// <summary>
    /// Reads the first <c>@page</c> rule out of a stylesheet, or returns false
    /// when there is none this codec can use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first rule wins. CSS would cascade several together and a document
    /// may state <c>@page</c> once for the size and again inside a media query
    /// for print; taking the first keeps the answer predictable and matches what
    /// every producer this codec has met actually writes, which is one rule at
    /// the top of one stylesheet.
    /// </para>
    /// <para>
    /// A rule stating margins and no size is honoured, with the size left to the
    /// caller's default. That is not a corner case: it is what a document says
    /// when it cares about its margins and not its paper.
    /// </para>
    /// </remarks>
    public static bool TryRead(string? css, PageGeometry fallback, out PageGeometry page)
    {
        page = fallback;
        if (string.IsNullOrWhiteSpace(css))
            return false;

        if (!TryFindRule(css, out string body))
            return false;

        IReadOnlyDictionary<string, string> declarations = HtmlCss.ParseDeclarations(body);
        bool any = false;

        double width = fallback.Width;
        double height = fallback.Height;
        if (declarations.TryGetValue("size", out string? size) && TryParseSize(size, ref width, ref height))
            any = true;

        double top = fallback.MarginTop;
        double right = fallback.MarginRight;
        double bottom = fallback.MarginBottom;
        double left = fallback.MarginLeft;

        if (declarations.TryGetValue("margin", out string? margin) &&
            TryParseMarginShorthand(margin, ref top, ref right, ref bottom, ref left))
        {
            any = true;
        }

        // The longhands are applied after the shorthand, which is the order CSS
        // gives them when both appear in one rule.
        any |= TryParseSide(declarations, "margin-top", ref top);
        any |= TryParseSide(declarations, "margin-right", ref right);
        any |= TryParseSide(declarations, "margin-bottom", ref bottom);
        any |= TryParseSide(declarations, "margin-left", ref left);

        if (!any)
            return false;

        var candidate = new PageGeometry(
            width, height, left, right, top, bottom, fallback.HeaderDistance, fallback.FooterDistance);

        // A page with no column to write in is a producer stating nonsense, and
        // this codec would rather have no page than an unusable one - the same
        // judgement OdtReader makes about a page layout that leaves no room.
        if (!candidate.IsUsable)
            return false;

        page = candidate;
        return true;
    }

    /// <summary>The <c>@page</c> rule for a geometry, as a stylesheet body.</summary>
    /// <remarks>
    /// Written in points because the model is in points, so a round trip through
    /// this codec converts nothing and cannot lose anything to rounding. The
    /// margins are written as four values rather than collapsed to the shortest
    /// shorthand: a reader diffing two exports should see a margin change where
    /// the margin changed, not where the shorthand happened to fold differently.
    /// </remarks>
    public static string Format(PageGeometry page) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"@page {{ size: {Length(page.Width)} {Length(page.Height)}; " +
            $"margin: {Length(page.MarginTop)} {Length(page.MarginRight)} " +
            $"{Length(page.MarginBottom)} {Length(page.MarginLeft)}; }}");

    private static string Length(double points) =>
        points.ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    /// <summary>
    /// Finds the body of the first <c>@page</c> rule.
    /// </summary>
    /// <remarks>
    /// A scan rather than a stylesheet parser, and the boundary is worth stating:
    /// this codec does not own a CSS engine and should not grow one to read two
    /// properties. What it does have to survive is the brace nesting a real
    /// <c>@page</c> allows - margin boxes such as <c>@top-center</c> are nested
    /// rules - so the scan counts depth rather than stopping at the first closing
    /// brace, and the nested blocks are simply not read.
    /// </remarks>
    private static bool TryFindRule(string css, out string body)
    {
        body = string.Empty;

        int at = css.IndexOf("@page", StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            int open = css.IndexOf('{', at);
            if (open < 0)
                return false;

            // Anything between the at-rule name and the brace is a selector this
            // codec does not implement - a named page, or :first. Such a rule
            // states the page for some of the document, and reading it as the
            // page for all of it would be a guess. Skip to the next one.
            string selector = css[(at + "@page".Length)..open].Trim();
            if (selector.Length == 0)
            {
                int depth = 0;
                for (int index = open; index < css.Length; index++)
                {
                    if (css[index] == '{')
                    {
                        depth++;
                    }
                    else if (css[index] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            body = WithoutNestedBlocks(css[(open + 1)..index]);
                            return true;
                        }
                    }
                }

                return false;
            }

            at = css.IndexOf("@page", open, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// The rule body with its nested blocks taken out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The margin boxes have to go before the declarations are split, not after.
    /// <see cref="HtmlCss.ParseDeclarations"/> divides on semicolons and then on
    /// the first colon, and a nested block carries both - so
    /// <c>@top-center { content: "x" } margin: 18pt</c> parses as one declaration
    /// whose name is everything up to <c>content</c>, and the margin after it
    /// disappears. The rule was read, the size came back, and the margin
    /// silently did not: the failure mode this whole file exists to stop.
    /// </para>
    /// <para>
    /// A block takes the text before it back to the previous semicolon, because
    /// what precedes it is the box's own selector rather than a declaration.
    /// </para>
    /// </remarks>
    private static string WithoutNestedBlocks(string body)
    {
        if (body.IndexOf('{') < 0)
            return body;

        var kept = new System.Text.StringBuilder(body.Length);
        for (int index = 0; index < body.Length; index++)
        {
            if (body[index] != '{')
            {
                kept.Append(body[index]);
                continue;
            }

            int selector = kept.Length;
            while (selector > 0 && kept[selector - 1] != ';')
                selector--;

            kept.Length = selector;

            int depth = 0;
            for (; index < body.Length; index++)
            {
                if (body[index] == '{')
                    depth++;
                else if (body[index] == '}' && --depth == 0)
                    break;
            }
        }

        return kept.ToString();
    }

    /// <summary>
    /// Reads a <c>size</c> value: a named page, one length, or two.
    /// </summary>
    /// <remarks>
    /// <c>portrait</c> and <c>landscape</c> are read as the orientation they
    /// name rather than ignored, because a document saying <c>size: a4
    /// landscape</c> means something a document saying <c>size: a4</c> does not.
    /// With no name beside it the keyword orients whatever size is already in
    /// hand, which is how CSS defines it.
    /// </remarks>
    private static bool TryParseSize(string value, ref double width, ref double height)
    {
        string[] parts = value.Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        double? named = null;
        double namedHeight = 0;
        var lengths = new List<double>();
        bool landscape = false;
        bool portrait = false;
        bool understood = false;

        foreach (string part in parts)
        {
            if (part.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return false;

            if (part.Equals("landscape", StringComparison.OrdinalIgnoreCase))
            {
                landscape = true;
                understood = true;
                continue;
            }

            if (part.Equals("portrait", StringComparison.OrdinalIgnoreCase))
            {
                portrait = true;
                understood = true;
                continue;
            }

            if (NamedSizes.TryGetValue(part, out (double Width, double Height) size))
            {
                named = size.Width;
                namedHeight = size.Height;
                understood = true;
                continue;
            }

            if (HtmlCss.TryParsePoints(part, out float points) && points > 0)
            {
                lengths.Add(points);
                understood = true;
                continue;
            }

            // A token this codec does not know. Refusing the whole value is the
            // conservative answer: half a size is not a size.
            return false;
        }

        if (!understood)
            return false;

        double resolvedWidth;
        double resolvedHeight;
        if (named is not null)
        {
            resolvedWidth = named.Value;
            resolvedHeight = namedHeight;
        }
        else if (lengths.Count == 1)
        {
            resolvedWidth = lengths[0];
            resolvedHeight = lengths[0];
        }
        else if (lengths.Count == 2)
        {
            resolvedWidth = lengths[0];
            resolvedHeight = lengths[1];
        }
        else if (landscape || portrait)
        {
            resolvedWidth = width;
            resolvedHeight = height;
        }
        else
        {
            return false;
        }

        if (landscape && resolvedWidth < resolvedHeight)
            (resolvedWidth, resolvedHeight) = (resolvedHeight, resolvedWidth);
        else if (portrait && resolvedWidth > resolvedHeight)
            (resolvedWidth, resolvedHeight) = (resolvedHeight, resolvedWidth);

        width = resolvedWidth;
        height = resolvedHeight;
        return true;
    }

    /// <summary>The CSS margin shorthand, in its one, two, three and four value forms.</summary>
    private static bool TryParseMarginShorthand(
        string value, ref double top, ref double right, ref double bottom, ref double left)
    {
        string[] parts = value.Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > 4)
            return false;

        var points = new double[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!HtmlCss.TryParsePoints(parts[index], out float parsed))
                return false;

            points[index] = parsed;
        }

        switch (points.Length)
        {
            case 1:
                top = right = bottom = left = points[0];
                return true;
            case 2:
                top = bottom = points[0];
                right = left = points[1];
                return true;
            case 3:
                top = points[0];
                right = left = points[1];
                bottom = points[2];
                return true;
            default:
                top = points[0];
                right = points[1];
                bottom = points[2];
                left = points[3];
                return true;
        }
    }

    private static bool TryParseSide(
        IReadOnlyDictionary<string, string> declarations, string name, ref double side)
    {
        if (!declarations.TryGetValue(name, out string? value) ||
            !HtmlCss.TryParsePoints(value, out float points))
        {
            return false;
        }

        side = points;
        return true;
    }
}
