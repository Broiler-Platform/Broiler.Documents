using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Broiler.Documents.Office;

/// <summary>
/// The plain text both readers are reduced to before they are compared, and
/// the line by line difference that comparison reports.
/// </summary>
/// <remarks>
/// <para>
/// The strongest check in this suite hands the same file on disk to two
/// readers - LibreOffice, through
/// <c>soffice --convert-to 'txt:Text (encoded):UTF8'</c>, and this
/// repository's own reader, through <c>broilerdoc dump &lt;file&gt; --as
/// text</c> - and compares what the two of them say the file says. A
/// divergence means one of the two dropped, duplicated, reordered or
/// invented content. No formatting survives the projection, which is
/// exactly why it is the headline check: it cannot fail over a margin, a
/// font or a style name, so when it fails it is about the text.
/// </para>
/// <para>
/// The two sides also disagree for reasons that have nothing to do with
/// content, and every one of those is handled here rather than at the call
/// sites. LibreOffice's txt export is UTF-8 with a BOM and CRLF line
/// endings. <c>broilerdoc dump --as text</c> is UTF-8 without a BOM and
/// uses LF, except that a paragraph which was empty in the model can come
/// back as a lone CR. And LibreOffice writes an empty paragraph in HTML as
/// <c>&lt;p&gt;&lt;br/&gt;&lt;/p&gt;</c>, which Broiler's HTML reader turns
/// into a single space - so the two sides legitimately disagree about how
/// many blank lines a paragraph break is worth, and about trailing spaces.
/// Those are encoder habits, not content, and a check that reported them
/// would report them on every single document and be switched off within a
/// week.
/// </para>
/// <para>
/// Both members here are pure functions over strings, and that is
/// deliberate. Deciding whether LibreOffice is installed, and skipping the
/// check with a reason when it is not, belongs to the runner; a comparison
/// that was never reached must not be the thing that throws.
/// </para>
/// </remarks>
internal static class TextProjection
{
    /// <summary>How much of one line a difference report quotes.</summary>
    private const int LineTextLimit = 120;

    /// <summary>
    /// The line count above which the alignment is abandoned. See the guard
    /// in <see cref="Differences"/> for why this is a corpus limit rather
    /// than a diff limit.
    /// </summary>
    private const int AlignmentLineLimit = 4000;

    /// <summary>
    /// The canonical form both sides are reduced to before they are
    /// compared: no byte order mark, LF endings, no blank lines, a single
    /// space wherever a run of spaces or a tab stood, and NFC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The steps run in the order written and the order matters. Line
    /// endings are unified before anything is asked about lines, the
    /// invisible characters are resolved before spaces are collapsed - a
    /// non-breaking space next to an ordinary one has to become one space,
    /// not two - and the normalisation is last, so that it sees the string
    /// that will actually be compared.
    /// </para>
    /// <para>
    /// This canonicalisation is a policy, not a fact about documents. It is
    /// deliberately blunt, and the price of that bluntness is that a codec
    /// bug which only manifests as whitespace, a soft hyphen, a non-breaking
    /// space or a choice of line separator is invisible to it. A reader that
    /// flattens every non-breaking space to an ordinary one, that swallows
    /// soft hyphens, or that emits U+2028 where the document said paragraph,
    /// passes this check. That is a known blind spot, written down here so
    /// that nobody has to rediscover it from a green run.
    /// </para>
    /// <para>
    /// The correct response to one noisy document is therefore a declared
    /// per-document exception carrying a written reason, not another rule
    /// added to this method. A wider global rule quietens one document by
    /// making every other document in the corpus blinder, and does it
    /// silently. An exception costs somebody a sentence and leaves the loss
    /// where the next reader can see it; that honesty is the house style.
    /// </para>
    /// </remarks>
    public static string Canonicalise(string text)
    {
        if (text.Length == 0)
            return string.Empty;

        // 1. A leading U+FEFF is a byte order mark, and only the leading one
        //    is. LibreOffice writes one and broilerdoc does not, so it is a
        //    property of the encoder rather than of the document. A U+FEFF
        //    anywhere else is a zero width no-break space sitting in the
        //    content; step 4 removes those with the other invisibles.
        if (text[0] == '\uFEFF')
            text = text[1..];

        // 2. CRLF, CR and LF all become LF. This is also where the lone CR
        //    that broilerdoc emits for a paragraph that was empty in the
        //    model becomes an empty line, which step 7 then drops.
        text = text.ReplaceLineEndings("\n");

        // 3, 4 and 5 in one pass, because each character maps to exactly one
        //    result and no rule here can feed another. ReplaceLineEndings
        //    above already recognises U+000B, U+000C, U+2028 and U+2029 as
        //    line endings; they are listed again anyway, because this check
        //    must not quietly change meaning if that framework set ever
        //    does.
        var translated = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            switch (character)
            {
                // 3. Every other thing a writer may have meant by "new
                //    line".
                case '\u000B':
                case '\u000C':
                case '\u2028':
                case '\u2029':
                    translated.Append('\n');
                    break;

                // 4a. Spaces that are only a space to a reader. Which of
                //     them a writer reaches for is a layout decision, and
                //     the two sides make it differently for the same
                //     document.
                case '\u00A0':
                case '\u2007':
                case '\u202F':
                // 5. A tab is one space here. Its width is a formatting
                //    question and this projection has no formatting.
                case '\t':
                    translated.Append(' ');
                    break;

                // 4b. Characters with no width. A soft hyphen is a
                //     hyphenation hint, a zero width space is a break
                //     opportunity, and a stray U+FEFF is neither of those
                //     and nothing else either.
                case '\u00AD':
                case '\u200B':
                case '\uFEFF':
                    break;

                default:
                    translated.Append(character);
                    break;
            }
        }

        // 6 and 7. Collapse and trim each line, then keep the ones that
        //    still have something in them. Dropping empty lines is what
        //    settles the <p><br/></p> disagreement above: neither side gets
        //    to be right about how many blank lines a paragraph break is
        //    worth, because after this neither side has any.
        string[] rawLines = translated.ToString().Split('\n');
        var canonical = new StringBuilder(translated.Length);
        bool first = true;
        foreach (string rawLine in rawLines)
        {
            string line = CollapseSpaces(rawLine);
            if (line.Length == 0)
                continue;

            if (!first)
                canonical.Append('\n');

            canonical.Append(line);
            first = false;
        }

        // 8. Last, so that it sees the exact string the comparison will.
        return Normalise(canonical.ToString());
    }

    /// <summary>
    /// A capped list of difference lines in the shape a baseline row stores:
    /// <c>"- &lt;leftLabel&gt;: text"</c> for a line only the left side has,
    /// <c>"+ &lt;rightLabel&gt;: text"</c> for a line only the right side
    /// has, in document order. Empty when the two canonical forms are equal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both arguments are canonicalised here rather than being assumed to
    /// have been canonicalised already. <see cref="Canonicalise"/> is
    /// idempotent, so a caller who did it costs itself a copy and nothing
    /// else, while a caller who forgot would otherwise get a difference list
    /// made entirely of BOMs, CRs and trailing spaces - which is the failure
    /// mode this whole file exists to prevent.
    /// </para>
    /// <para>
    /// The list is capped because a difference list nobody reads is worth
    /// the same as no difference list, and because a baseline row carries
    /// these verbatim. When the cap bites, the last entry says how many
    /// lines were left out, so the report never pretends the diff was
    /// shorter than it was.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Differences(
        string left, string right, string leftLabel, string rightLabel, int cap = 20)
    {
        string canonicalLeft = Canonicalise(left);
        string canonicalRight = Canonicalise(right);
        if (string.Equals(canonicalLeft, canonicalRight, StringComparison.Ordinal))
            return [];

        string[] leftLines = SplitLines(canonicalLeft);
        string[] rightLines = SplitLines(canonicalRight);

        // A cap of zero would produce a report that says there are
        // differences without naming one, which is not a report. One line is
        // the floor.
        int limit = Math.Max(1, cap);

        // Above this the alignment table stops being cheap, and - more to
        // the point - a plain-text projection with more than four thousand
        // non-empty lines is a corpus problem rather than a diff problem. A
        // sample that large is not diagnosable by eye when it fails and
        // should be cut down to the part that reproduces, so the honest
        // answer here is to name the first line that diverged, give both
        // lengths, and say out loud that the alignment did not run.
        if (leftLines.Length > AlignmentLineLimit || rightLines.Length > AlignmentLineLimit)
            return Truncate(FirstDivergence(leftLines, rightLines, leftLabel, rightLabel), limit);

        return Truncate(Align(leftLines, rightLines, leftLabel, rightLabel), limit);
    }

    /// <summary>
    /// Aligns the two line lists with a longest common subsequence and emits
    /// the lines that found no partner.
    /// </summary>
    /// <remarks>
    /// The alignment is what makes the report worth reading. Zipping the two
    /// lists positionally would report a document that gained one line at
    /// the top as one where every line differs, which reads as a reader that
    /// lost everything, and a reviewer handed that once stops trusting the
    /// difference list at all. The classic O(n*m) table is more than enough
    /// for the few hundred lines a corpus sample runs to.
    /// </remarks>
    private static List<string> Align(
        string[] leftLines, string[] rightLines, string leftLabel, string rightLabel)
    {
        int leftCount = leftLines.Length;
        int rightCount = rightLines.Length;

        // table[i, j] is the length of the longest common subsequence of the
        // two tails starting at i and j. It is bounded by the guard in
        // Differences, so no entry can exceed 4000 and a ushort holds every
        // value that can land in it - which halves the worst case from 64 MB
        // to 32 MB, the difference between a runner that allocates something
        // noticeable and one that allocates something alarming.
        var table = new ushort[leftCount + 1, rightCount + 1];
        for (int i = leftCount - 1; i >= 0; i--)
        {
            for (int j = rightCount - 1; j >= 0; j--)
            {
                table[i, j] = string.Equals(leftLines[i], rightLines[j], StringComparison.Ordinal)
                    ? (ushort)(table[i + 1, j + 1] + 1)
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        // Walking forward keeps the output in document order, which is the
        // order somebody reading the two files side by side will be in.
        var differences = new List<string>();
        int left = 0;
        int right = 0;
        while (left < leftCount && right < rightCount)
        {
            if (string.Equals(leftLines[left], rightLines[right], StringComparison.Ordinal))
            {
                left++;
                right++;
            }
            else if (table[left + 1, right] >= table[left, right + 1])
            {
                differences.Add("- " + leftLabel + ": " + Clip(leftLines[left]));
                left++;
            }
            else
            {
                differences.Add("+ " + rightLabel + ": " + Clip(rightLines[right]));
                right++;
            }
        }

        while (left < leftCount)
            differences.Add("- " + leftLabel + ": " + Clip(leftLines[left++]));

        while (right < rightCount)
            differences.Add("+ " + rightLabel + ": " + Clip(rightLines[right++]));

        return differences;
    }

    /// <summary>
    /// What a document too large to align gets instead: the first line at
    /// which the two sides part company, both lengths, and a statement that
    /// this is not the full difference.
    /// </summary>
    private static List<string> FirstDivergence(
        string[] leftLines, string[] rightLines, string leftLabel, string rightLabel)
    {
        int index = 0;
        while (index < leftLines.Length && index < rightLines.Length &&
               string.Equals(leftLines[index], rightLines[index], StringComparison.Ordinal))
        {
            index++;
        }

        var differences = new List<string>
        {
            "... alignment skipped above " + Number(AlignmentLineLimit) + " lines: " +
            leftLabel + " has " + Number(leftLines.Length) + " line(s), " +
            rightLabel + " has " + Number(rightLines.Length) + " line(s); " +
            "first divergence at line " + Number(index + 1),
        };

        if (index < leftLines.Length)
            differences.Add("- " + leftLabel + ": " + Clip(leftLines[index]));

        if (index < rightLines.Length)
            differences.Add("+ " + rightLabel + ": " + Clip(rightLines[index]));

        return differences;
    }

    /// <summary>
    /// Runs of spaces become one space and the line is trimmed at both ends.
    /// </summary>
    /// <remarks>
    /// Only U+0020 is collapsed, because by this point it is the only space
    /// the translation pass produces. A space this method has never heard of
    /// - an ideographic space, say - survives into the comparison on
    /// purpose: an unknown character showing up as a difference is a
    /// question somebody can answer, and one silently folded away is not.
    /// </remarks>
    private static string CollapseSpaces(string line)
    {
        var collapsed = new StringBuilder(line.Length);
        bool previousWasSpace = false;
        foreach (char character in line)
        {
            if (character == ' ')
            {
                if (!previousWasSpace)
                    collapsed.Append(' ');

                previousWasSpace = true;
            }
            else
            {
                collapsed.Append(character);
                previousWasSpace = false;
            }
        }

        return collapsed.ToString().Trim();
    }

    /// <summary>NFC, or the text unchanged when NFC cannot be applied.</summary>
    /// <remarks>
    /// <para>
    /// A lone surrogate - exactly the sort of thing a reader emits when it
    /// has mangled a character - makes Normalize throw, and that is a defect
    /// this check should report rather than a reason to end the run.
    /// Comparing the text unnormalised can only invent a difference, never
    /// hide one, so the failure stays in the safe direction.
    /// </para>
    /// <para>
    /// Worth writing down rather than rediscovering: this runner sets
    /// InvariantGlobalization, and in that mode Normalize is a no-op that
    /// hands the string straight back instead of composing it. So step 8 is
    /// a statement of intent that a globalized host honours and this one
    /// does not, and a document where the two readers disagree only about a
    /// composed versus a decomposed accent shows up here as a difference
    /// instead of passing quietly. That is the direction to be wrong in, and
    /// when it happens it is a per-document exception with a reason - not a
    /// reason to widen the canonicalisation.
    /// </para>
    /// </remarks>
    private static string Normalise(string text)
    {
        try
        {
            return text.Normalize(NormalizationForm.FormC);
        }
        catch (Exception exception)
            when (exception is ArgumentException or PlatformNotSupportedException)
        {
            return text;
        }
    }

    /// <summary>
    /// The lines of a canonical form. An empty canonical form has no lines
    /// at all, where <c>Split</c> would have claimed one empty one.
    /// </summary>
    private static string[] SplitLines(string canonical) =>
        canonical.Length == 0 ? [] : canonical.Split('\n');

    /// <summary>One reported line, shortened when it is long.</summary>
    private static string Clip(string line)
    {
        if (line.Length <= LineTextLimit)
            return line;

        // Cutting between the halves of a surrogate pair would leave a lone
        // surrogate in the report, which a terminal draws as a replacement
        // character - and that looks exactly like the codec defect these
        // reports exist to describe. Stopping one character earlier is
        // cheaper than that confusion.
        int cut = LineTextLimit;
        if (char.IsHighSurrogate(line[cut - 1]))
            cut--;

        return line[..cut] + " ...";
    }

    /// <summary>
    /// The cap, and the count of what it left out. The trailing line is not
    /// decoration: a list that stopped at twenty and did not say so reads as
    /// a document with twenty problems.
    /// </summary>
    private static IReadOnlyList<string> Truncate(List<string> differences, int limit)
    {
        if (differences.Count <= limit)
            return differences;

        List<string> capped = differences.GetRange(0, limit);
        capped.Add("... and " + Number(differences.Count - limit) + " more difference(s)");
        return capped;
    }

    /// <summary>
    /// Digits that do not depend on the machine's culture, because these
    /// strings are compared against a baseline file written on some other
    /// machine.
    /// </summary>
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
