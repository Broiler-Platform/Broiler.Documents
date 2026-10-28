using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Broiler.Documents.Cli.Documents;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.FormatCodes;
using Broiler.Documents.Model;

namespace Broiler.Documents.Cli.Comparison;

/// <summary>What to leave out of a structural comparison.</summary>
public sealed class ComparisonOptions
{
    /// <summary>Collapse runs of whitespace and trim before comparing text.</summary>
    public bool IgnoreWhitespace { get; init; }

    /// <summary>Compare text without regard to letter case.</summary>
    public bool IgnoreCase { get; init; }

    /// <summary>Compare text and paragraph structure only, not run formatting.</summary>
    public bool IgnoreInlineStyle { get; init; }

    /// <summary>Compare text and run formatting only, not alignment, lists, indents, or spacing.</summary>
    public bool IgnoreParagraphStyle { get; init; }

    /// <summary>Compare tabular data and structure only, ignoring cell widths, padding, and borders.</summary>
    public bool IgnoreTableLayout { get; init; }

    /// <summary>
    /// When specified, floating-point tolerance delta for cell value comparisons.
    /// Numeric cells differing by at most this delta are considered equal.
    /// </summary>
    public double? NumericTolerance { get; init; }

    /// <summary>Stop listing differences after this many.</summary>
    public int MaxDifferences { get; init; } = 50;

    /// <summary>
    /// Largest paragraph-pair product to align with a longest-common-subsequence
    /// pass before falling back to comparing by index.
    /// </summary>
    public int MaxAlignmentCells { get; init; } = 4_000_000;
}

/// <summary>One way in which two documents disagree.</summary>
public sealed class DocumentDifference(
    string kind,
    int? leftParagraph,
    int? rightParagraph,
    string detail,
    int? tableIndex = null,
    string? cellCoordinate = null)
{
    /// <summary>What sort of difference: <c>text</c>, <c>paragraph-style</c>, <c>inline-style</c>, <c>missing</c>, <c>extra</c>, or table difference kinds.</summary>
    public string Kind { get; } = kind;

    /// <summary>The paragraph on the left, or null when the paragraph exists only on the right.</summary>
    public int? LeftParagraph { get; } = leftParagraph;

    /// <summary>The paragraph on the right, or null when the paragraph exists only on the left.</summary>
    public int? RightParagraph { get; } = rightParagraph;

    public string Detail { get; } = detail;

    /// <summary>The 0-based index of the table this difference occurred in, or null if outside a table or table count mismatch.</summary>
    public int? TableIndex { get; } = tableIndex;

    /// <summary>The spreadsheet-style cell coordinate (e.g. "A1", "C4") or column name ("A"), or null if at table level.</summary>
    public string? CellCoordinate { get; } = cellCoordinate;

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["kind"] = Kind,
            ["leftParagraph"] = LeftParagraph,
            ["rightParagraph"] = RightParagraph,
            ["detail"] = Detail,
        };

        if (TableIndex.HasValue)
            json["tableIndex"] = TableIndex.Value;

        if (CellCoordinate is not null)
            json["cellCoordinate"] = CellCoordinate;

        return json;
    }

    public override string ToString()
    {
        string where;
        if (TableIndex.HasValue)
        {
            where = "table " + TableIndex.Value.ToString(CultureInfo.InvariantCulture);
            if (CellCoordinate is not null)
                where += ", cell " + CellCoordinate;
        }
        else
        {
            where = (LeftParagraph, RightParagraph) switch
            {
                (int l, int r) when l == r => "paragraph " + l.ToString(CultureInfo.InvariantCulture),
                (int l, int r) => "paragraph " + l.ToString(CultureInfo.InvariantCulture) + "/" +
                    r.ToString(CultureInfo.InvariantCulture),
                (int l, null) => "paragraph " + l.ToString(CultureInfo.InvariantCulture) + " (left only)",
                (null, int r) => "paragraph " + r.ToString(CultureInfo.InvariantCulture) + " (right only)",
                _ => "document",
            };
        }

        return where + ": " + Detail;
    }
}

/// <summary>
/// Compares two documents through the model rather than through their bytes.
/// </summary>
/// <remarks>
/// <para>
/// This is the comparison to reach for first when hunting a codec gap. A pixel
/// diff tells you two exports look different; this tells you that paragraph 14
/// lost its bold run or that the second document has three paragraphs the first
/// does not, which is a sentence you can turn into a test.
/// </para>
/// <para>
/// Paragraphs are aligned with a longest-common-subsequence pass over their
/// text before anything is compared, so a single dropped paragraph reports as
/// one missing paragraph instead of as every following paragraph differing. The
/// alignment falls back to index-wise comparison on documents large enough for
/// the quadratic pass to matter, and says so when it does.
/// </para>
/// <para>
/// The Formatting Codes projection is compared alongside the model because it is
/// the component's own canonical, versioned rendering of a document's semantics
/// (ADR 0006). Two documents whose projections match are equal in every property
/// that grammar covers, and the projection's diagnostics name model values -
/// non-finite sizes, over-long quoted values - that the model itself will hold
/// without complaint.
/// </para>
/// </remarks>
public sealed class DocumentComparison
{
    private readonly List<DocumentDifference> _differences = [];

    private DocumentComparison(ComparisonOptions options) => Options = options;

    public ComparisonOptions Options { get; }

    public bool TextEqual { get; private set; }

    public bool FormatCodesEqual { get; private set; }

    /// <summary>True when nothing the options asked about differs.</summary>
    public bool Equal => _differences.Count == 0 && !Truncated;

    /// <summary>True when the difference list hit <see cref="ComparisonOptions.MaxDifferences"/>.</summary>
    public bool Truncated { get; private set; }

    /// <summary>True when the documents were compared by index because alignment was too expensive.</summary>
    public bool AlignedByIndex { get; private set; }

    public IReadOnlyList<DocumentDifference> Differences => _differences;

    public DocumentStatistics LeftStatistics { get; private set; } = new();

    public DocumentStatistics RightStatistics { get; private set; } = new();

    /// <summary>The first character offset at which the two plain texts diverge, or null.</summary>
    public int? FirstTextDifference { get; private set; }

    public static DocumentComparison Compare(
        RichTextDocument left,
        RichTextDocument right,
        ComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(options);

        var comparison = new DocumentComparison(options)
        {
            LeftStatistics = DocumentReport.Measure(left),
            RightStatistics = DocumentReport.Measure(right),
        };

        string leftText = comparison.Normalize(left.PlainText);
        string rightText = comparison.Normalize(right.PlainText);
        comparison.TextEqual = string.Equals(leftText, rightText, StringComparison.Ordinal);
        comparison.FirstTextDifference = comparison.TextEqual ? null : FirstDifference(leftText, rightText);

        var projector = new FormatCodeProjector();
        comparison.FormatCodesEqual = string.Equals(
            FormatCodeProjector.Project(left).Text,
            FormatCodeProjector.Project(right).Text,
            StringComparison.Ordinal);

        comparison.CompareParagraphs(left, right);
        comparison.CompareTables(left, right);
        return comparison;
    }

    private void CompareParagraphs(RichTextDocument left, RichTextDocument right)
    {
        string[] leftKeys = [.. left.Paragraphs.Select(p => Normalize(p.Text))];
        string[] rightKeys = [.. right.Paragraphs.Select(p => Normalize(p.Text))];

        IReadOnlyList<(int? Left, int? Right)> pairs;
        if ((long)leftKeys.Length * rightKeys.Length > Options.MaxAlignmentCells)
        {
            AlignedByIndex = true;
            pairs = AlignByIndex(leftKeys.Length, rightKeys.Length);
        }
        else
        {
            pairs = Coalesce(Align(leftKeys, rightKeys));
        }

        int[] leftTableMap = BuildParagraphTableMap(left);
        int[] rightTableMap = BuildParagraphTableMap(right);

        foreach ((int? leftIndex, int? rightIndex) in pairs)
        {
            if (_differences.Count >= Options.MaxDifferences)
            {
                Truncated = true;
                return;
            }

            if (leftIndex is null)
            {
                Add("extra", null, rightIndex, "paragraph only in the right document: " +
                    Preview(right.Paragraphs[rightIndex!.Value].Text));
                continue;
            }

            if (rightIndex is null)
            {
                Add("missing", leftIndex, null, "paragraph only in the left document: " +
                    Preview(left.Paragraphs[leftIndex.Value].Text));
                continue;
            }

            ComparePair(
                left.Paragraphs[leftIndex.Value],
                right.Paragraphs[rightIndex.Value],
                leftIndex.Value,
                rightIndex.Value,
                leftTableMap,
                rightTableMap);
        }
    }

    private void ComparePair(
        RichTextParagraph left,
        RichTextParagraph right,
        int leftIndex,
        int rightIndex,
        int[] leftTableMap,
        int[] rightTableMap)
    {
        bool inSameTable = leftIndex < leftTableMap.Length &&
                           rightIndex < rightTableMap.Length &&
                           leftTableMap[leftIndex] >= 0 &&
                           leftTableMap[leftIndex] == rightTableMap[rightIndex];

        if (!string.Equals(Normalize(left.Text), Normalize(right.Text), StringComparison.Ordinal))
        {
            if (inSameTable)
            {
                // In matching tables, cell value differences and numeric tolerance
                // are reported by CompareTables under "table.cell-value".
                return;
            }

            Add("text", leftIndex, rightIndex,
                "text differs: " + Preview(left.Text) + " vs " + Preview(right.Text));
            return;
        }

        if (!Options.IgnoreParagraphStyle)
        {
            foreach (string difference in CompareParagraphStyle(left.Style, right.Style))
                Add("paragraph-style", leftIndex, rightIndex, difference);
        }

        if (Options.IgnoreInlineStyle)
            return;

        string? inline = FirstInlineDifference(left, right);
        if (inline is not null)
            Add("inline-style", leftIndex, rightIndex, inline);
    }

    private void CompareTables(RichTextDocument left, RichTextDocument right)
    {
        if (_differences.Count >= Options.MaxDifferences)
        {
            Truncated = true;
            return;
        }

        if (left.Tables.Count != right.Tables.Count)
        {
            Add("table.grid-mismatch", null, null,
                $"table count differs: {left.Tables.Count} vs {right.Tables.Count}");
        }

        int tableCount = Math.Min(left.Tables.Count, right.Tables.Count);
        for (int i = 0; i < tableCount; i++)
        {
            if (_differences.Count >= Options.MaxDifferences)
            {
                Truncated = true;
                return;
            }

            DocumentTable tableL = left.Tables[i];
            DocumentTable tableR = right.Tables[i];
            TableGrid gridL = tableL.Grid;
            TableGrid gridR = tableR.Grid;

            // 1. Grid mismatch (row/column counts)
            if (gridL.RowCount != gridR.RowCount || gridL.ColumnCount != gridR.ColumnCount)
            {
                Add("table.grid-mismatch", tableL.ParagraphIndex, tableR.ParagraphIndex,
                    $"grid dimensions differ: {gridL.RowCount}x{gridL.ColumnCount} vs {gridR.RowCount}x{gridR.ColumnCount}",
                    tableIndex: i);
            }

            // 2. Table-level layout
            if (!Options.IgnoreTableLayout)
            {
                if (tableL.Alignment != tableR.Alignment)
                {
                    Add("table.cell-layout", tableL.ParagraphIndex, tableR.ParagraphIndex,
                        $"alignment {tableL.Alignment} vs {tableR.Alignment}",
                        tableIndex: i);
                }

                if (!Near(tableL.CellPadding, tableR.CellPadding))
                {
                    Add("table.cell-layout", tableL.ParagraphIndex, tableR.ParagraphIndex,
                        $"padding {Number(tableL.CellPadding)} vs {Number(tableR.CellPadding)}",
                        tableIndex: i);
                }

                int minCols = Math.Min(tableL.ColumnWidths.Count, tableR.ColumnWidths.Count);
                for (int c = 0; c < minCols; c++)
                {
                    double wL = tableL.ColumnWidths[c];
                    double wR = tableR.ColumnWidths[c];
                    if (!Near(wL, wR))
                    {
                        string colLetter = CellAddress.ColumnToLetters(c);
                        Add("table.cell-layout", tableL.ParagraphIndex, tableR.ParagraphIndex,
                            $"column {colLetter} width {Number(wL)} vs {Number(wR)}",
                            tableIndex: i,
                            cellCoordinate: colLetter);
                    }
                }
                if (tableL.ColumnWidths.Count != tableR.ColumnWidths.Count)
                {
                    Add("table.cell-layout", tableL.ParagraphIndex, tableR.ParagraphIndex,
                        $"column width count {tableL.ColumnWidths.Count} vs {tableR.ColumnWidths.Count}",
                        tableIndex: i);
                }
            }

            // 3. Cell-level comparison
            int rows = Math.Min(gridL.RowCount, gridR.RowCount);
            int cols = Math.Min(gridL.ColumnCount, gridR.ColumnCount);
            var reportedSpanMismatchesL = new HashSet<(int, int)>();
            var reportedSpanMismatchesR = new HashSet<(int, int)>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (_differences.Count >= Options.MaxDifferences)
                    {
                        Truncated = true;
                        return;
                    }

                    TableGridSlot slotL = gridL.GetSlot(r, c);
                    TableGridSlot slotR = gridR.GetSlot(r, c);
                    string coord = new CellAddress(r, c).Format();

                    // Span mismatch
                    bool spanDiffers = slotL.RowSpan != slotR.RowSpan || slotL.ColumnSpan != slotR.ColumnSpan;
                    if (spanDiffers && (slotL.IsRoot || slotR.IsRoot))
                    {
                        bool alreadyReported =
                            (!slotL.IsRoot && reportedSpanMismatchesL.Contains((slotL.RootRow, slotL.RootColumn))) ||
                            (!slotR.IsRoot && reportedSpanMismatchesR.Contains((slotR.RootRow, slotR.RootColumn)));

                        if (!alreadyReported)
                        {
                            string detail;
                            if (slotL.RowSpan != slotR.RowSpan && slotL.ColumnSpan != slotR.ColumnSpan)
                                detail = $"row span {slotL.RowSpan} vs {slotR.RowSpan}, column span {slotL.ColumnSpan} vs {slotR.ColumnSpan}";
                            else if (slotL.RowSpan != slotR.RowSpan)
                                detail = $"row span {slotL.RowSpan} vs {slotR.RowSpan}";
                            else
                                detail = $"column span {slotL.ColumnSpan} vs {slotR.ColumnSpan}";

                            Add("table.cell-span-mismatch",
                                slotL.RootCell?.ParagraphIndex,
                                slotR.RootCell?.ParagraphIndex,
                                detail,
                                tableIndex: i,
                                cellCoordinate: coord);

                            reportedSpanMismatchesL.Add((slotL.RootRow, slotL.RootColumn));
                            reportedSpanMismatchesR.Add((slotR.RootRow, slotR.RootColumn));
                        }
                    }

                    if (slotL.IsRoot && slotR.IsEmpty)
                    {
                        Add("table.cell-value",
                            slotL.RootCell?.ParagraphIndex,
                            null,
                            "cell value present only on left: " + Preview(CellText(left, slotL.RootCell!)),
                            tableIndex: i,
                            cellCoordinate: coord);
                    }
                    else if (slotL.IsEmpty && slotR.IsRoot)
                    {
                        Add("table.cell-value",
                            null,
                            slotR.RootCell?.ParagraphIndex,
                            "cell value present only on right: " + Preview(CellText(right, slotR.RootCell!)),
                            tableIndex: i,
                            cellCoordinate: coord);
                    }
                    else if (slotL.IsRoot && slotR.IsRoot)
                    {
                        TableCell cellL = slotL.RootCell!;
                        TableCell cellR = slotR.RootCell!;

                        if (!Options.IgnoreTableLayout)
                        {
                            if (cellL.Borders != cellR.Borders)
                            {
                                Add("table.cell-layout", cellL.ParagraphIndex, cellR.ParagraphIndex,
                                    "borders differ",
                                    tableIndex: i,
                                    cellCoordinate: coord);
                            }

                            TableCellPadding padL = cellL.PaddingFor(tableL);
                            TableCellPadding padR = cellR.PaddingFor(tableR);
                            if (!Near(padL.Left, padR.Left) || !Near(padL.Top, padR.Top) ||
                                !Near(padL.Right, padR.Right) || !Near(padL.Bottom, padR.Bottom))
                            {
                                Add("table.cell-layout", cellL.ParagraphIndex, cellR.ParagraphIndex,
                                    "cell padding differs",
                                    tableIndex: i,
                                    cellCoordinate: coord);
                            }

                            if (cellL.Shading != cellR.Shading)
                            {
                                Add("table.cell-layout", cellL.ParagraphIndex, cellR.ParagraphIndex,
                                    $"shading {ColorText.Format(cellL.Shading)} vs {ColorText.Format(cellR.Shading)}",
                                    tableIndex: i,
                                    cellCoordinate: coord);
                            }

                            if (cellL.VerticalAlignment != cellR.VerticalAlignment)
                            {
                                Add("table.cell-layout", cellL.ParagraphIndex, cellR.ParagraphIndex,
                                    $"vertical alignment {cellL.VerticalAlignment} vs {cellR.VerticalAlignment}",
                                    tableIndex: i,
                                    cellCoordinate: coord);
                            }
                        }

                        string textL = CellText(left, cellL);
                        string textR = CellText(right, cellR);
                        string normL = Normalize(textL);
                        string normR = Normalize(textR);

                        if (!string.Equals(normL, normR, StringComparison.Ordinal))
                        {
                            bool tolerated = false;
                            if (Options.NumericTolerance is double tol &&
                                TryParseNumber(normL, out double valL) &&
                                TryParseNumber(normR, out double valR))
                            {
                                if (Math.Abs(valL - valR) <= tol)
                                    tolerated = true;
                            }

                            if (!tolerated)
                            {
                                bool bothNumeric = TryParseNumber(normL, out _) && TryParseNumber(normR, out _);
                                string detail = (bothNumeric ? "value differs: " : "text differs: ") +
                                    Preview(textL) + " vs " + Preview(textR);

                                Add("table.cell-value",
                                    cellL.ParagraphIndex,
                                    cellR.ParagraphIndex,
                                    detail,
                                    tableIndex: i,
                                    cellCoordinate: coord);
                            }
                        }
                        else if (!string.Equals(cellL.Formula, cellR.Formula, StringComparison.Ordinal))
                        {
                            Add("table.cell-value",
                                cellL.ParagraphIndex,
                                cellR.ParagraphIndex,
                                $"formula {Quote(cellL.Formula)} vs {Quote(cellR.Formula)}",
                                tableIndex: i,
                                cellCoordinate: coord);
                        }
                    }
                }
            }
        }
    }

    private static int[] BuildParagraphTableMap(RichTextDocument doc)
    {
        int[] map = new int[doc.Paragraphs.Count];
        Array.Fill(map, -1);
        for (int t = 0; t < doc.Tables.Count; t++)
        {
            var table = doc.Tables[t];
            int end = Math.Min(doc.Paragraphs.Count, table.ParagraphEnd);
            for (int p = table.ParagraphIndex; p < end; p++)
                map[p] = t;
        }
        return map;
    }

    private static string CellText(RichTextDocument doc, TableCell cell)
    {
        if (cell.ParagraphCount == 0)
            return string.Empty;

        if (cell.ParagraphCount == 1)
        {
            return cell.ParagraphIndex < doc.Paragraphs.Count
                ? doc.Paragraphs[cell.ParagraphIndex].Text
                : string.Empty;
        }

        var sb = new StringBuilder();
        int end = Math.Min(doc.Paragraphs.Count, cell.ParagraphEnd);
        for (int p = cell.ParagraphIndex; p < end; p++)
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(doc.Paragraphs[p].Text);
        }
        return sb.ToString();
    }

    private static bool TryParseNumber(string text, out double value)
    {
        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
            return true;
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out value))
            return true;
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value))
            return true;
        if (text.EndsWith('%') &&
            (double.TryParse(text[..^1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double pct) ||
             double.TryParse(text[..^1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out pct)))
        {
            value = pct / 100.0;
            return true;
        }

        value = 0;
        return false;
    }

    private static IEnumerable<string> CompareParagraphStyle(ParagraphStyle left, ParagraphStyle right)
    {
        if (left.Alignment != right.Alignment)
            yield return "alignment " + left.Alignment + " vs " + right.Alignment;
        if (left.ListKind != right.ListKind)
            yield return "list " + left.ListKind + " vs " + right.ListKind;
        if (left.IndentLevel != right.IndentLevel)
            yield return "indent level " + left.IndentLevel + " vs " + right.IndentLevel;
        if (!Near((float)left.LeftIndentIn(), (float)right.LeftIndentIn()))
            yield return "left indent " + Number((float)left.LeftIndentIn()) + " vs " + Number((float)right.LeftIndentIn());
        if (!Near((float)left.RightIndentIn, (float)right.RightIndentIn))
            yield return "right indent " + Number((float)left.RightIndentIn) + " vs " + Number((float)right.RightIndentIn);
        if (!Near((float)left.FirstLineIndentIn, (float)right.FirstLineIndentIn))
            yield return "first line indent " + Number((float)left.FirstLineIndentIn) + " vs " + Number((float)right.FirstLineIndentIn);
        if (!Near(left.LineSpacing, right.LineSpacing))
            yield return "line spacing " + Number(left.LineSpacing) + " vs " + Number(right.LineSpacing);
        if (left.LineHeightRule != right.LineHeightRule)
            yield return "line height rule " + left.LineHeightRule + " vs " + right.LineHeightRule;
        if (!Near(left.LineHeight, right.LineHeight))
            yield return "line height " + Number(left.LineHeight) + " vs " + Number(right.LineHeight);
        if (left.KeepWithNext != right.KeepWithNext)
            yield return "keep with next " + left.KeepWithNext + " vs " + right.KeepWithNext;
        if (left.KeepTogether != right.KeepTogether)
            yield return "keep together " + left.KeepTogether + " vs " + right.KeepTogether;
        if (left.OrphanLines != right.OrphanLines || left.WidowLines != right.WidowLines)
            yield return "widow/orphan line counts differ";
        if (!Near(left.SpacingBefore, right.SpacingBefore))
            yield return "spacing before " + Number(left.SpacingBefore) + " vs " + Number(right.SpacingBefore);
        if (!Near(left.SpacingAfter, right.SpacingAfter))
            yield return "spacing after " + Number(left.SpacingAfter) + " vs " + Number(right.SpacingAfter);
        if (((left.TabStops?.Count ?? 0) > 0 || (right.TabStops?.Count ?? 0) > 0) && !Equals(left.TabStops, right.TabStops))
            yield return "tab stops differ";
    }

    /// <summary>
    /// Walks both run lists together and names the first character offset whose
    /// resolved style differs. Comparing run lists directly would report a
    /// difference for a document that merely split its runs differently while
    /// resolving to identical formatting at every character.
    /// </summary>
    private static string? FirstInlineDifference(RichTextParagraph left, RichTextParagraph right)
    {
        int leftRun = 0;
        int rightRun = 0;
        int leftRemaining = left.Runs.Count > 0 ? left.Runs[0].Length : 0;
        int rightRemaining = right.Runs.Count > 0 ? right.Runs[0].Length : 0;
        int offset = 0;
        int length = Math.Min(left.Length, right.Length);

        while (offset < length)
        {
            while (leftRemaining == 0 && leftRun + 1 < left.Runs.Count)
                leftRemaining = left.Runs[++leftRun].Length;
            while (rightRemaining == 0 && rightRun + 1 < right.Runs.Count)
                rightRemaining = right.Runs[++rightRun].Length;

            if (leftRun >= left.Runs.Count || rightRun >= right.Runs.Count)
                break;

            InlineStyle a = left.Runs[leftRun].Style;
            InlineStyle b = right.Runs[rightRun].Style;
            string? difference = DescribeInlineDifference(a, b);
            if (difference is not null)
            {
                return "formatting differs from character " +
                    offset.ToString(CultureInfo.InvariantCulture) + ": " + difference;
            }

            int step = Math.Max(1, Math.Min(leftRemaining, rightRemaining));
            step = Math.Min(step, length - offset);
            offset += step;
            leftRemaining -= step;
            rightRemaining -= step;
        }

        return null;
    }

    private static string DescribeRevision(TextRevision? revision) =>
        revision is null
            ? "none"
            : revision.Kind.ToString().ToLowerInvariant() + (revision.Author is string author ? " by " + Quote(author) : string.Empty);

    private static string? DescribeInlineDifference(InlineStyle left, InlineStyle right)
    {
        if (left.Bold != right.Bold)
            return "bold " + left.Bold + " vs " + right.Bold;
        if (left.Italic != right.Italic)
            return "italic " + left.Italic + " vs " + right.Italic;
        if (left.Underline != right.Underline)
            return "underline " + left.Underline + " vs " + right.Underline;
        if (left.Strikethrough != right.Strikethrough)
            return "strikethrough " + left.Strikethrough + " vs " + right.Strikethrough;
        if (left.Capitalization != right.Capitalization)
            return "capitalization " + left.Capitalization + " vs " + right.Capitalization;
        if (!string.Equals(left.FontFamily, right.FontFamily, StringComparison.Ordinal))
            return "font family " + Quote(left.FontFamily) + " vs " + Quote(right.FontFamily);
        if (!NullableNear(left.FontSize, right.FontSize))
            return "font size " + OptionalNumber(left.FontSize) + " vs " + OptionalNumber(right.FontSize);
        if (left.Foreground != right.Foreground)
            return "foreground " + ColorText.Format(left.Foreground) + " vs " + ColorText.Format(right.Foreground);
        if (left.Background != right.Background)
            return "background " + ColorText.Format(left.Background) + " vs " + ColorText.Format(right.Background);
        if (!string.Equals(left.LinkHref, right.LinkHref, StringComparison.Ordinal))
            return "link " + Quote(left.LinkHref) + " vs " + Quote(right.LinkHref);
        if (!Equals(left.Revision, right.Revision))
            return "tracked change " + DescribeRevision(left.Revision) + " vs " + DescribeRevision(right.Revision);

        return DescribeImageDifference(left.Image, right.Image);
    }

    /// <summary>
    /// Compares two possibly-automatic dimensions. Two automatic sizes agree:
    /// both mean "take it from the picture", and reporting them as a difference
    /// would flag every unsized image against every other one.
    /// </summary>
    private static bool NearOrBothAuto(double? left, double? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (double l, double r) => Near(l, r),
            _ => false,
        };

    private static string Dimension(double? value) => value is double v ? Number(v) : "auto";

    private static string? DescribeImageDifference(InlineImage? left, InlineImage? right)
    {
        if (left is null && right is null)
            return null;
        if (left is null)
            return "image present only on the right (" + right!.ContentType + ")";
        if (right is null)
            return "image present only on the left (" + left.ContentType + ")";

        if (!string.Equals(left.ContentType, right.ContentType, StringComparison.OrdinalIgnoreCase))
            return "image content type " + left.ContentType + " vs " + right.ContentType;
        if (left.Data.Length != right.Data.Length)
            return "image byte length " + left.Data.Length + " vs " + right.Data.Length;
        if (!NearOrBothAuto(left.Width, right.Width) || !NearOrBothAuto(left.Height, right.Height))
        {
            return "image size " + Dimension(left.Width) + "x" + Dimension(left.Height) + " vs " +
                Dimension(right.Width) + "x" + Dimension(right.Height);
        }

        return null;
    }

    /// <summary>
    /// Aligns two paragraph sequences by longest common subsequence, so a single
    /// insertion does not make everything after it look different.
    /// </summary>
    private static List<(int? Left, int? Right)> Align(string[] left, string[] right)
    {
        int[,] lengths = new int[left.Length + 1, right.Length + 1];
        for (int i = left.Length - 1; i >= 0; i--)
        {
            for (int j = right.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int?, int?)>();
        int x = 0;
        int y = 0;

        while (x < left.Length && y < right.Length)
        {
            if (string.Equals(left[x], right[y], StringComparison.Ordinal))
            {
                pairs.Add((x++, y++));
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                pairs.Add((x++, null));
            }
            else
            {
                pairs.Add((null, y++));
            }
        }

        while (x < left.Length)
            pairs.Add((x++, null));
        while (y < right.Length)
            pairs.Add((null, y++));

        return pairs;
    }

    /// <summary>
    /// Pairs a deletion immediately followed by an insertion back into one
    /// changed paragraph.
    /// </summary>
    /// <remarks>
    /// A longest-common-subsequence pass has no concept of "changed": a
    /// paragraph whose text was edited comes out as a delete and an insert.
    /// Reported that way, one reworded sentence reads as two findings and says
    /// nothing about what actually changed. Pairing them back up turns it into
    /// the one comparison a reader wants - old text against new - and also
    /// restores the style comparison, which only runs on a matched pair.
    /// </remarks>
    private static List<(int? Left, int? Right)> Coalesce(List<(int? Left, int? Right)> pairs)
    {
        var coalesced = new List<(int?, int?)>(pairs.Count);

        for (int i = 0; i < pairs.Count; i++)
        {
            bool changed = i + 1 < pairs.Count &&
                pairs[i].Left is not null && pairs[i].Right is null &&
                pairs[i + 1].Left is null && pairs[i + 1].Right is not null;

            if (changed)
            {
                coalesced.Add((pairs[i].Left, pairs[i + 1].Right));
                i++;
                continue;
            }

            coalesced.Add(pairs[i]);
        }

        return coalesced;
    }

    private static List<(int? Left, int? Right)> AlignByIndex(int leftCount, int rightCount)
    {
        var pairs = new List<(int?, int?)>(Math.Max(leftCount, rightCount));
        for (int i = 0; i < Math.Max(leftCount, rightCount); i++)
        {
            pairs.Add((i < leftCount ? i : null, i < rightCount ? i : null));
        }

        return pairs;
    }

    private void Add(string kind, int? leftIndex, int? rightIndex, string detail, int? tableIndex = null, string? cellCoordinate = null)
    {
        if (_differences.Count >= Options.MaxDifferences)
        {
            Truncated = true;
            return;
        }

        _differences.Add(new DocumentDifference(kind, leftIndex, rightIndex, detail, tableIndex, cellCoordinate));
    }

    private string Normalize(string text)
    {
        if (Options.IgnoreWhitespace)
        {
            var builder = new StringBuilder(text.Length);
            bool pendingSpace = false;

            foreach (char character in text)
            {
                if (char.IsWhiteSpace(character) && character != '\n')
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace && character != '\n')
                    builder.Append(' ');
                pendingSpace = false;
                builder.Append(character);
            }

            text = builder.ToString();
        }

        return Options.IgnoreCase ? text.ToUpperInvariant() : text;
    }

    private static int FirstDifference(string left, string right)
    {
        int limit = Math.Min(left.Length, right.Length);
        for (int i = 0; i < limit; i++)
        {
            if (left[i] != right[i])
                return i;
        }

        return limit;
    }

    private static bool Near(double left, double right) => Math.Abs(left - right) < 0.0005;

    private static bool NullableNear(float? left, float? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;
        return Near(left.Value, right.Value);
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string OptionalNumber(float? value) =>
        value is null ? "default" : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Quote(string? value) => value is null ? "default" : "\"" + value + "\"";

    private static string Preview(string text)
    {
        string collapsed = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        return collapsed.Length <= 60
            ? "\"" + collapsed + "\""
            : "\"" + collapsed[..57] + "...\"";
    }

    public JsonObject ToJson()
    {
        var differences = new JsonArray();
        foreach (DocumentDifference difference in _differences)
            differences.Add(difference.ToJson());

        return new JsonObject
        {
            ["equal"] = Equal,
            ["plainTextEqual"] = TextEqual,
            ["formatCodesEqual"] = FormatCodesEqual,
            ["firstTextDifference"] = FirstTextDifference,
            ["alignedByIndex"] = AlignedByIndex,
            ["differenceCount"] = _differences.Count,
            ["differencesTruncated"] = Truncated,
            ["differences"] = differences,
            ["left"] = LeftStatistics.ToJson(),
            ["right"] = RightStatistics.ToJson(),
        };
    }

    /// <summary>The counts that differ between the two documents, as report lines.</summary>
    public IEnumerable<string> DescribeStatistics()
    {
        Dictionary<string, long> right = RightStatistics.Counts().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        foreach (KeyValuePair<string, long> entry in LeftStatistics.Counts())
        {
            long other = right[entry.Key];
            if (entry.Value == other)
                continue;

            yield return $"  {entry.Key,-20} {entry.Value,10} {other,10}   {other - entry.Value:+#;-#;0}";
        }
    }
}
