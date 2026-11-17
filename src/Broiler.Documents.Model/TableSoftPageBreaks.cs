using System.Linq;

namespace Broiler.Documents.Model;

/// <summary>Locates soft page breaks at inline table row boundaries.</summary>
public static class TableSoftPageBreaks
{
    public static bool CanBreakBeforeRow(this DocumentTable table, int row)
    {
        if (row < 0 || row >= table.Rows.Count || table.Position is not null || table.Rows[row].IsHidden)
            return false;
        if (row > 0 && (table.KeepTogether || table.Rows[row].IsHeader || table.Rows[row - 1].IsHeader ||
            table.Rows[row - 1].KeepWithNext))
            return false;
        for (int preceding = 0; preceding < row; preceding++)
        {
            int distance = row - preceding;
            if (table.Rows[preceding].Cells.Any(cell => !cell.IsRowSpanContinuation && cell.RowSpan > distance))
                return false;
        }
        return true;
    }

    public static bool CanAnchorSoftBreak(this DocumentTable table, int paragraphIndex)
    {
        if (paragraphIndex == table.ParagraphIndex && table.Position is null)
            return true;
        for (int row = 1; row < table.Rows.Count; row++)
        {
            if (FirstParagraph(table.Rows[row]) == paragraphIndex)
                return table.CanBreakBeforeRow(row);
        }
        return false;
    }

    public static int? FirstParagraph(TableRow row) =>
        row.Cells.FirstOrDefault(cell => !cell.IsRowSpanContinuation && cell.ParagraphCount > 0)?.ParagraphIndex;
}
