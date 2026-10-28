using System;
using System.Collections.Generic;
using Broiler.Documents.Model;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public class TableFormulaTests
{
    [Fact]
    public void CellValue_PrimitiveTypesAndParsing_WorkCorrectly()
    {
        var num = CellValue.FromNumber(42.5);
        Assert.True(num.IsNumber);
        Assert.Equal(42.5, num.Number);
        Assert.True(num.AsDouble(out double d));
        Assert.Equal(42.5, d);
        Assert.Equal("42.5", num.Format());

        var integerVal = CellValue.FromNumber(100);
        Assert.Equal("100", integerVal.Format());

        var txt = CellValue.FromText("Hello");
        Assert.True(txt.IsText);
        Assert.Equal("Hello", txt.Text);
        Assert.Equal("Hello", txt.Format());

        var bTrue = CellValue.True;
        Assert.True(bTrue.IsBoolean);
        Assert.True(bTrue.Boolean);
        Assert.True(bTrue.AsBoolean(out bool b));
        Assert.True(b);
        Assert.Equal("TRUE", bTrue.Format());

        var err = CellValue.FromError(FormulaError.Div0);
        Assert.True(err.IsError);
        Assert.Equal(FormulaError.Div0, err.Error);
        Assert.Equal("#DIV/0!", err.Format());

        Assert.Equal(42.0, CellValue.Parse("42").Number);
        Assert.Equal(0.75, CellValue.Parse("75%").Number);
        Assert.Equal(120.0, CellValue.Parse("$120").Number);
        Assert.True(CellValue.Parse("TRUE").Boolean);
        Assert.False(CellValue.Parse("false").Boolean);
        Assert.Equal(FormulaError.Cycle, CellValue.Parse("#CYCLE!").Error);
        Assert.Equal("Some String", CellValue.Parse("Some String").Text);
    }

    [Fact]
    public void CellAddress_AndRange_ParseAndFormatCorrectly()
    {
        Assert.Equal("A", CellAddress.ColumnToLetters(0));
        Assert.Equal("Z", CellAddress.ColumnToLetters(25));
        Assert.Equal("AA", CellAddress.ColumnToLetters(26));
        Assert.Equal("AB", CellAddress.ColumnToLetters(27));
        Assert.Equal("BA", CellAddress.ColumnToLetters(52));

        Assert.True(CellAddress.TryLettersToColumn("A", out int c0));
        Assert.Equal(0, c0);
        Assert.True(CellAddress.TryLettersToColumn("Z", out int c25));
        Assert.Equal(25, c25);
        Assert.True(CellAddress.TryLettersToColumn("AA", out int c26));
        Assert.Equal(26, c26);

        Assert.True(CellAddress.TryParse("A1", out var a1));
        Assert.Equal(0, a1.Row);
        Assert.Equal(0, a1.Column);
        Assert.Equal("A1", a1.Format());

        Assert.True(CellAddress.TryParse("$B$12", out var b12));
        Assert.Equal(11, b12.Row);
        Assert.Equal(1, b12.Column);

        Assert.True(CellRangeAddress.TryParse("A1:B3", out var range));
        Assert.Equal(new CellAddress(0, 0), range.Start);
        Assert.Equal(new CellAddress(2, 1), range.End);

        var cells = new List<CellAddress>(range.EnumerateCells(3, 2));
        Assert.Equal(6, cells.Count);
        Assert.Contains(new CellAddress(0, 0), cells);
        Assert.Contains(new CellAddress(2, 1), cells);
    }

    [Fact]
    public void BasicArithmeticAndPrecedence_EvaluatesAccurately()
    {
        var table = CreateTable(1, 1);
        var res1 = EvaluateFormula(table, "1 + 2 * 3");
        Assert.Equal(7.0, res1.Number);

        var res2 = EvaluateFormula(table, "(1 + 2) * 3");
        Assert.Equal(9.0, res2.Number);

        var res3 = EvaluateFormula(table, "2 ^ 3 ^ 2");
        Assert.Equal(512.0, res3.Number);

        var res4 = EvaluateFormula(table, "50% * 200");
        Assert.Equal(100.0, res4.Number);

        var res5 = EvaluateFormula(table, "10 - 4 - 2");
        Assert.Equal(4.0, res5.Number);

        var res6 = EvaluateFormula(table, "\"Total: \" & (10 + 20)");
        Assert.Equal("Total: 30", res6.Text);
    }

    [Fact]
    public void ComparisonsAndLogicalOperators_WorkCorrectly()
    {
        var table = CreateTable(1, 1);
        Assert.True(EvaluateFormula(table, "10 > 5").Boolean);
        Assert.False(EvaluateFormula(table, "10 < 5").Boolean);
        Assert.True(EvaluateFormula(table, "10 >= 10").Boolean);
        Assert.True(EvaluateFormula(table, "10 <= 10").Boolean);
        Assert.True(EvaluateFormula(table, "10 = 10").Boolean);
        Assert.True(EvaluateFormula(table, "10 <> 5").Boolean);
        Assert.True(EvaluateFormula(table, "10 != 5").Boolean);

        Assert.True(EvaluateFormula(table, "\"hello\" = \"HELLO\"").Boolean);
        Assert.True(EvaluateFormula(table, "TRUE = (1 < 2)").Boolean);
    }

    [Fact]
    public void MathFunctions_EvaluateAccurately()
    {
        var table = CreateTable(1, 1);
        Assert.Equal(42.0, EvaluateFormula(table, "ABS(-42)").Number);
        Assert.Equal(3.14, EvaluateFormula(table, "ROUND(3.14159, 2)").Number);
        Assert.Equal(1300.0, EvaluateFormula(table, "ROUND(1250, -2)").Number);
        Assert.Equal(3.0, EvaluateFormula(table, "INT(3.7)").Number);
        Assert.Equal(-4.0, EvaluateFormula(table, "INT(-3.7)").Number);
        Assert.Equal(3.0, EvaluateFormula(table, "TRUNC(3.7)").Number);
        Assert.Equal(-3.0, EvaluateFormula(table, "TRUNC(-3.7)").Number);
        Assert.Equal(1.0, EvaluateFormula(table, "MOD(10, 3)").Number);
        Assert.Equal(1.0, EvaluateFormula(table, "MOD(-7, 4)").Number);
        Assert.Equal(4.0, EvaluateFormula(table, "SQRT(16)").Number);
        Assert.Equal(FormulaError.Num, EvaluateFormula(table, "SQRT(-1)").Error);
        Assert.Equal(256.0, EvaluateFormula(table, "POWER(2, 8)").Number);
        Assert.Equal(-1.0, EvaluateFormula(table, "SIGN(-99)").Number);
        Assert.Equal(0.0, EvaluateFormula(table, "SIGN(0)").Number);
        Assert.Equal(1.0, EvaluateFormula(table, "SIGN(99)").Number);
    }

    [Fact]
    public void LogicalFunctions_AndShortCircuit_WorkCorrectly()
    {
        var table = CreateTable(1, 1);

        // Short-circuit: division by zero in unselected branch must not fail
        Assert.Equal("Safe", EvaluateFormula(table, "IF(1 > 0, \"Safe\", 1/0)").Text);
        Assert.Equal("Safe", EvaluateFormula(table, "IF(1 < 0, 1/0, \"Safe\")").Text);

        Assert.Equal("Fallback", EvaluateFormula(table, "IFERROR(10 / 0, \"Fallback\")").Text);
        Assert.Equal(5.0, EvaluateFormula(table, "IFERROR(10 / 2, \"Fallback\")").Number);

        Assert.True(EvaluateFormula(table, "AND(TRUE, 1 > 0, 5 = 5)").Boolean);
        Assert.False(EvaluateFormula(table, "AND(TRUE, FALSE)").Boolean);
        Assert.True(EvaluateFormula(table, "OR(FALSE, 1 > 0)").Boolean);
        Assert.False(EvaluateFormula(table, "OR(FALSE, 0 > 1)").Boolean);

        Assert.False(EvaluateFormula(table, "NOT(TRUE)").Boolean);
        Assert.True(EvaluateFormula(table, "NOT(FALSE)").Boolean);

        Assert.True(EvaluateFormula(table, "ISNUMBER(123)").Boolean);
        Assert.False(EvaluateFormula(table, "ISNUMBER(\"text\")").Boolean);
        Assert.True(EvaluateFormula(table, "ISTEXT(\"text\")").Boolean);
        Assert.True(EvaluateFormula(table, "ISERROR(1/0)").Boolean);
    }

    [Fact]
    public void TextFunctions_WorkCorrectly()
    {
        var table = CreateTable(1, 1);
        Assert.Equal("Hello World", EvaluateFormula(table, "CONCAT(\"Hello\", \" \", \"World\")").Text);
        Assert.Equal("Hello World", EvaluateFormula(table, "CONCATENATE(\"Hello\", \" \", \"World\")").Text);
        Assert.Equal(7.0, EvaluateFormula(table, "LEN(\"Broiler\")").Number);
        Assert.Equal("HELLO", EvaluateFormula(table, "UPPER(\"hello\")").Text);
        Assert.Equal("world", EvaluateFormula(table, "LOWER(\"WORLD\")").Text);
        Assert.Equal("trimmed", EvaluateFormula(table, "TRIM(\"   trimmed   \")").Text);
    }

    [Fact]
    public void AggregateFunctions_OverRange_ComputeAccurately()
    {
        // 4x3 table: 3 data rows (A1:C3) and 1 summary row (Row 3, A4:C4)
        // [ 10, 20, 30 ]
        // [ 40, 50, 60 ]
        // [ 70, 80, 90 ]
        // [ summary formulas ]
        var table = CreateTable(4, 3);
        var initial = new Dictionary<CellAddress, CellValue>
        {
            [new CellAddress(0, 0)] = CellValue.FromNumber(10),
            [new CellAddress(0, 1)] = CellValue.FromNumber(20),
            [new CellAddress(0, 2)] = CellValue.FromNumber(30),
            [new CellAddress(1, 0)] = CellValue.FromNumber(40),
            [new CellAddress(1, 1)] = CellValue.FromNumber(50),
            [new CellAddress(1, 2)] = CellValue.FromNumber(60),
            [new CellAddress(2, 0)] = CellValue.FromNumber(70),
            [new CellAddress(2, 1)] = CellValue.FromNumber(80),
            [new CellAddress(2, 2)] = CellValue.FromNumber(90)
        };

        var sumRes = TableFormulaEngine.Evaluate(
            SetFormula(table, 3, 0, "=SUM(A1:C3)"),
            initialValues: initial);
        Assert.Equal(450.0, sumRes[3, 0].Number);

        var avgRes = TableFormulaEngine.Evaluate(
            SetFormula(table, 3, 0, "=AVERAGE(A1:C3)"),
            initialValues: initial);
        Assert.Equal(50.0, avgRes[3, 0].Number);

        var countRes = TableFormulaEngine.Evaluate(
            SetFormula(table, 3, 0, "=COUNT(A1:C3)"),
            initialValues: initial);
        Assert.Equal(9.0, countRes[3, 0].Number);

        var minRes = TableFormulaEngine.Evaluate(
            SetFormula(table, 3, 0, "=MIN(A1:C3)"),
            initialValues: initial);
        Assert.Equal(10.0, minRes[3, 0].Number);

        var maxRes = TableFormulaEngine.Evaluate(
            SetFormula(table, 3, 0, "=MAX(A1:C3)"),
            initialValues: initial);
        Assert.Equal(90.0, maxRes[3, 0].Number);

        var prodRes = TableFormulaEngine.Evaluate(
            SetFormula(table, 3, 0, "=PRODUCT(A1:A3)"),
            initialValues: initial);
        Assert.Equal(10.0 * 40.0 * 70.0, prodRes[3, 0].Number);

        var medRes = TableFormulaEngine.Evaluate(
            SetFormula(table, 3, 0, "=MEDIAN(A1:C3)"),
            initialValues: initial);
        Assert.Equal(50.0, medRes[3, 0].Number);
    }

    [Fact]
    public void DirectionalKeywords_AboveBelowLeftRight_EvaluateAccurately()
    {
        // 3 rows, 3 cols:
        // Row 0: [ 10, 20, =SUM(LEFT) ] -> Col 2 = 30
        // Row 1: [ 30, 40, =SUM(LEFT) ] -> Col 2 = 70
        // Row 2: [ =SUM(ABOVE), =AVERAGE(ABOVE), =SUM(ABOVE) ]
        // Col 0 Row 2 = 10 + 30 = 40
        // Col 1 Row 2 = (20 + 40) / 2 = 30
        // Col 2 Row 2 = 30 + 70 = 100
        var table = CreateTable(3, 3);
        var initial = new Dictionary<CellAddress, CellValue>
        {
            [new CellAddress(0, 0)] = CellValue.FromNumber(10),
            [new CellAddress(0, 1)] = CellValue.FromNumber(20),
            [new CellAddress(1, 0)] = CellValue.FromNumber(30),
            [new CellAddress(1, 1)] = CellValue.FromNumber(40)
        };

        table = SetFormula(table, 0, 2, "=SUM(LEFT)");
        table = SetFormula(table, 1, 2, "=SUM(LEFT)");
        table = SetFormula(table, 2, 0, "=SUM(ABOVE)");
        table = SetFormula(table, 2, 1, "=AVERAGE(ABOVE)");
        table = SetFormula(table, 2, 2, "=SUM(ABOVE)");

        var res = TableFormulaEngine.Evaluate(table, initialValues: initial);

        Assert.Equal(30.0, res[0, 2].Number);
        Assert.Equal(70.0, res[1, 2].Number);
        Assert.Equal(40.0, res[2, 0].Number);
        Assert.Equal(30.0, res[2, 1].Number);
        Assert.Equal(100.0, res[2, 2].Number);
    }

    [Fact]
    public void ChainedDependencies_TopologicalSort_EvaluatesInCorrectOrder()
    {
        // A1: 5
        // B1: =A1 * 2   (10)
        // C1: =B1 + 3   (13)
        // D1: =C1 * 2   (26)
        var table = CreateTable(1, 4);
        var initial = new Dictionary<CellAddress, CellValue>
        {
            [new CellAddress(0, 0)] = CellValue.FromNumber(5)
        };

        table = SetFormula(table, 0, 3, "=C1 * 2"); // D1
        table = SetFormula(table, 0, 2, "=B1 + 3"); // C1
        table = SetFormula(table, 0, 1, "=A1 * 2"); // B1

        var res = TableFormulaEngine.Evaluate(table, initialValues: initial);
        Assert.Equal(10.0, res[0, 1].Number);
        Assert.Equal(13.0, res[0, 2].Number);
        Assert.Equal(26.0, res[0, 3].Number);
    }

    [Fact]
    public void CircularDependency_CycleDetection_FlagsCycleError()
    {
        // Direct cycle: A1 = B1, B1 = A1
        // Transitive: C1 = A1 + 10
        // Independent: D1 = 100
        var table = CreateTable(1, 4);
        table = SetFormula(table, 0, 0, "=B1");
        table = SetFormula(table, 0, 1, "=A1");
        table = SetFormula(table, 0, 2, "=A1 + 10");
        table = SetFormula(table, 0, 3, "=50 * 2");

        var res = TableFormulaEngine.Evaluate(table);

        Assert.True(res.HasCycles);
        Assert.Equal(FormulaError.Cycle, res[0, 0].Error);
        Assert.Equal(FormulaError.Cycle, res[0, 1].Error);
        Assert.Equal(FormulaError.Cycle, res[0, 2].Error);
        Assert.Equal(100.0, res[0, 3].Number);
    }

    [Fact]
    public void SelfReference_CycleDetection_FlagsCycleError()
    {
        var table = CreateTable(1, 1);
        table = SetFormula(table, 0, 0, "=A1 + 1");

        var res = TableFormulaEngine.Evaluate(table);
        Assert.True(res.HasCycles);
        Assert.Equal(FormulaError.Cycle, res[0, 0].Error);
    }

    [Fact]
    public void ErrorPropagation_AndHandling_BehavesAsExpected()
    {
        var table = CreateTable(1, 1);
        Assert.Equal(FormulaError.Div0, EvaluateFormula(table, "10 / 0").Error);
        Assert.Equal(FormulaError.Name, EvaluateFormula(table, "UNKNOWN_FUNC(1, 2)").Error);
        Assert.Equal(FormulaError.Ref, EvaluateFormula(table, "Z99 + 1").Error);
        Assert.Equal(FormulaError.Syntax, EvaluateFormula(table, "SUM(1, +)").Error);
    }

    [Fact]
    public void MergedCell_InFormulas_IsTreatedAsEmptyInContinuations()
    {
        // 2x2 table where (0, 0) has ColSpan=2 (A1:B1)
        var row0 = new TableRow([new TableCell(0, 1, 0, columnSpan: 2, isRowSpanContinuation: false)]);
        var row1 = new TableRow([new TableCell(1, 1, 0), new TableCell(2, 1, 1)]);
        var table = new DocumentTable(0, 3, [row0, row1], [100, 100]);

        var initial = new Dictionary<CellAddress, CellValue>
        {
            [new CellAddress(0, 0)] = CellValue.FromNumber(100),
            [new CellAddress(1, 0)] = CellValue.FromNumber(10),
            [new CellAddress(1, 1)] = CellValue.FromNumber(20)
        };

        // SUM(A1:B1): A1 is 100, B1 is continuation (empty), sum = 100
        var evalTable = SetFormula(table, 1, 0, "=SUM(A1:B1)");
        var res = TableFormulaEngine.Evaluate(evalTable, initialValues: initial);
        Assert.Equal(100.0, res[1, 0].Number);
    }

    [Fact]
    public void RichTextDocument_EvaluateTableFormulas_UpdatesParagraphTextAndPreservesStyle()
    {
        var boldStyle = new InlineStyle { Bold = true };
        var p0 = RichTextParagraph.Create("10", InlineStyle.Default);
        var p1 = RichTextParagraph.Create("20", InlineStyle.Default);
        var p2 = RichTextParagraph.Create("=SUM(A1:B1)", boldStyle);

        var row = new TableRow([
            new TableCell(0, 1, 0),
            new TableCell(1, 1, 1),
            new TableCell(2, 1, 2)
        ]);
        var table = new DocumentTable(0, 3, [row], [100, 100, 100]);

        var doc = RichTextDocument.FromParagraphs([p0, p1, p2]).WithTables([table]);

        var (updatedDoc, result) = doc.EvaluateTableFormulas(table);

        // Result value
        Assert.Equal(30.0, result[0, 2].Number);

        // Paragraph text in updated doc
        Assert.Equal("30", updatedDoc.Paragraphs[2].Text);

        // Style in updated doc paragraph preserved
        Assert.True(updatedDoc.Paragraphs[2].Runs[0].Style.Bold);

        // Formula was recorded on cell.Formula
        Assert.Equal("=SUM(A1:B1)", updatedDoc.Tables[0].Rows[0].Cells[2].Formula);
    }

    [Fact]
    public void RichTextDocument_EvaluateAllTableFormulas_RecalculatesMultipleTables()
    {
        var p0 = RichTextParagraph.Plain("10");
        var p1 = RichTextParagraph.Plain("=A1 * 2");
        var p2 = RichTextParagraph.Plain("100");
        var p3 = RichTextParagraph.Plain("=A1 + 50");

        var table1 = new DocumentTable(0, 2, [new TableRow([new TableCell(0, 1, 0), new TableCell(1, 1, 1)])], [100, 100]);
        var table2 = new DocumentTable(2, 2, [new TableRow([new TableCell(2, 1, 0), new TableCell(3, 1, 1)])], [100, 100]);

        var doc = RichTextDocument.FromParagraphs([p0, p1, p2, p3]).WithTables([table1, table2]);

        var (updatedDoc, results) = doc.EvaluateAllTableFormulas();

        Assert.Equal(2, results.Count);
        Assert.Equal(20.0, results[0][0, 1].Number);
        Assert.Equal(150.0, results[1][0, 1].Number);

        Assert.Equal("20", updatedDoc.Paragraphs[1].Text);
        Assert.Equal("150", updatedDoc.Paragraphs[3].Text);
    }

    // Helper utilities
    private static DocumentTable CreateTable(int rows, int cols)
    {
        var tableRows = new List<TableRow>(rows);
        int p = 0;
        for (int r = 0; r < rows; r++)
        {
            var cells = new List<TableCell>(cols);
            for (int c = 0; c < cols; c++)
            {
                cells.Add(new TableCell(p++, 1, c));
            }
            tableRows.Add(new TableRow(cells));
        }
        var widths = new double[cols];
        Array.Fill(widths, 100.0);
        return new DocumentTable(0, rows * cols, tableRows, widths);
    }

    private static DocumentTable SetFormula(DocumentTable table, int row, int col, string formula)
    {
        var rows = new List<TableRow>(table.Rows.Count);
        for (int r = 0; r < table.Rows.Count; r++)
        {
            var oldRow = table.Rows[r];
            if (r != row)
            {
                rows.Add(oldRow);
                continue;
            }

            var cells = new List<TableCell>(oldRow.Cells.Count);
            for (int c = 0; c < oldRow.Cells.Count; c++)
            {
                var oldCell = oldRow.Cells[c];
                if (c == col)
                {
                    cells.Add(oldCell.WithFormula(formula));
                }
                else
                {
                    cells.Add(oldCell);
                }
            }
            rows.Add(new TableRow(cells, oldRow.IsHeader, oldRow.MinHeight, oldRow.CanSplit, oldRow.ExactHeight, oldRow.KeepWithNext));
        }

        return new DocumentTable(
            table.ParagraphIndex,
            table.ParagraphCount,
            rows,
            table.ColumnWidths,
            table.CellPadding,
            table.Position,
            table.KeepWithNext,
            table.PreferredWidth,
            table.WidthPercent,
            table.Alignment,
            table.LeftIndent,
            table.PageBreakBefore,
            table.ColumnBreakBefore,
            table.AutoFit,
            table.KeepTogether);
    }

    private static CellValue EvaluateFormula(DocumentTable table, string formula)
    {
        var modified = SetFormula(table, 0, 0, formula);
        var result = TableFormulaEngine.Evaluate(modified);
        return result[0, 0];
    }
}
