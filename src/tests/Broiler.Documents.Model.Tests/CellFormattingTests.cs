using System;
using System.Globalization;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;
using Xunit;

namespace Broiler.Documents.Model.Tests;

public class CellFormattingTests
{
    [Fact]
    public void CellCoercion_InferDataType_DetectsTypesAccurately()
    {
        Assert.Equal(CellDataType.Empty, CellCoercion.InferDataType(null));
        Assert.Equal(CellDataType.Empty, CellCoercion.InferDataType("   "));

        Assert.Equal(CellDataType.Error, CellCoercion.InferDataType("#DIV/0!"));
        Assert.Equal(CellDataType.Error, CellCoercion.InferDataType("#CYCLE!"));
        Assert.Equal(CellDataType.Error, CellCoercion.InferDataType("#VALUE!"));

        Assert.Equal(CellDataType.Boolean, CellCoercion.InferDataType("TRUE"));
        Assert.Equal(CellDataType.Boolean, CellCoercion.InferDataType("false"));

        Assert.Equal(CellDataType.Percentage, CellCoercion.InferDataType("15.5%"));
        Assert.Equal(CellDataType.Percentage, CellCoercion.InferDataType("100%"));

        Assert.Equal(CellDataType.Currency, CellCoercion.InferDataType("$1,250.00"));
        Assert.Equal(CellDataType.Currency, CellCoercion.InferDataType("€ 99.95"));
        Assert.Equal(CellDataType.Currency, CellCoercion.InferDataType("£10"));
        Assert.Equal(CellDataType.Currency, CellCoercion.InferDataType("¥500"));

        Assert.Equal(CellDataType.Scientific, CellCoercion.InferDataType("1.25E+04"));
        Assert.Equal(CellDataType.Scientific, CellCoercion.InferDataType("-3.14e-02"));

        Assert.Equal(CellDataType.Date, CellCoercion.InferDataType("2026-10-27"));
        Assert.Equal(CellDataType.Date, CellCoercion.InferDataType("10/27/2026"));
        Assert.Equal(CellDataType.Date, CellCoercion.InferDataType("27.10.2026"));

        Assert.Equal(CellDataType.Time, CellCoercion.InferDataType("14:30:00"));
        Assert.Equal(CellDataType.Time, CellCoercion.InferDataType("2:30 PM"));
        Assert.Equal(CellDataType.Time, CellCoercion.InferDataType("09:15"));

        Assert.Equal(CellDataType.DateTime, CellCoercion.InferDataType("2026-10-27 14:30:00"));
        Assert.Equal(CellDataType.DateTime, CellCoercion.InferDataType("2026-10-27T14:30:00"));

        Assert.Equal(CellDataType.Integer, CellCoercion.InferDataType("42"));
        Assert.Equal(CellDataType.Integer, CellCoercion.InferDataType("-1000"));
        Assert.Equal(CellDataType.Integer, CellCoercion.InferDataType("1,000,000"));

        Assert.Equal(CellDataType.Decimal, CellCoercion.InferDataType("3.14159"));
        Assert.Equal(CellDataType.Decimal, CellCoercion.InferDataType("-0.05"));
        Assert.Equal(CellDataType.Decimal, CellCoercion.InferDataType("1,234.56"));

        Assert.Equal(CellDataType.Text, CellCoercion.InferDataType("Sample arbitrary text"));
    }

    [Fact]
    public void CellDateSerial_Conversions_MatchExcelStandards()
    {
        // 1900 date system
        var dt1 = CellDateSerial.FromSerial(1);
        Assert.Equal(new DateTime(1900, 1, 1), dt1);

        // Date serial 45592 is 2024-10-27
        var dt2 = CellDateSerial.FromSerial(45592);
        Assert.Equal(new DateTime(2024, 10, 27), dt2);

        double serial = CellDateSerial.ToSerial(new DateTime(2024, 10, 27));
        Assert.Equal(45592.0, serial);

        // Fractions represent time of day: 0.5 = 12:00:00 PM
        var dtNoon = CellDateSerial.FromSerial(45592.5);
        Assert.Equal(12, dtNoon.Hour);
        Assert.Equal(0, dtNoon.Minute);

        var timeOnly = CellDateSerial.ToTimeOnly(0.75);
        Assert.Equal(18, timeOnly.Hour); // 6:00 PM

        var dateOnly = CellDateSerial.ToDateOnly(45592);
        Assert.Equal(new DateOnly(2024, 10, 27), dateOnly);

        // 1904 date system
        var dt1904 = CellDateSerial.FromSerial(0, use1904: true);
        Assert.Equal(new DateTime(1904, 1, 1), dt1904);
    }

    [Fact]
    public void CellCoercion_TryCoerceToDouble_HandlesAllRepresentations()
    {
        Assert.True(CellCoercion.TryCoerceToDouble(123, out double d1));
        Assert.Equal(123.0, d1);

        Assert.True(CellCoercion.TryCoerceToDouble(true, out double d2));
        Assert.Equal(1.0, d2);

        Assert.True(CellCoercion.TryCoerceToDouble(false, out double d3));
        Assert.Equal(0.0, d3);

        Assert.True(CellCoercion.TryCoerceToDouble("25%", out double d4));
        Assert.Equal(0.25, d4);

        Assert.True(CellCoercion.TryCoerceToDouble("$1,250.50", out double d5));
        Assert.Equal(1250.50, d5);

        Assert.True(CellCoercion.TryCoerceToDouble("1.5e3", out double d6));
        Assert.Equal(1500.0, d6);

        Assert.True(CellCoercion.TryCoerceToDouble(CellValue.FromNumber(99.9), out double d7));
        Assert.Equal(99.9, d7);

        Assert.False(CellCoercion.TryCoerceToDouble("not-a-number", out _));
    }

    [Fact]
    public void CellCoercion_TryCoerceToBoolean_HandlesVariousTruthValues()
    {
        Assert.True(CellCoercion.TryCoerceToBoolean(true, out bool b1) && b1);
        Assert.True(CellCoercion.TryCoerceToBoolean(false, out bool b2) && !b2);

        Assert.True(CellCoercion.TryCoerceToBoolean(1, out bool b3) && b3);
        Assert.True(CellCoercion.TryCoerceToBoolean(0, out bool b4) && !b4);

        Assert.True(CellCoercion.TryCoerceToBoolean("true", out bool b5) && b5);
        Assert.True(CellCoercion.TryCoerceToBoolean("FALSE", out bool b6) && !b6);
        Assert.True(CellCoercion.TryCoerceToBoolean("yes", out bool b7) && b7);
        Assert.True(CellCoercion.TryCoerceToBoolean("NO", out bool b8) && !b8);
    }

    [Fact]
    public void CellNumberFormatter_GeneralAndBasicNumericPatterns_FormatAccurately()
    {
        var cult = CultureInfo.InvariantCulture;

        // General
        Assert.Equal("1234.5", CellNumberFormatter.Format(1234.5, "General", cult).Text);

        // Thousands grouping and 2 decimal places
        Assert.Equal("1,234.50", CellNumberFormatter.Format(1234.5, "#,##0.00", cult).Text);
        Assert.Equal("1,234,567.89", CellNumberFormatter.Format(1234567.89, "#,##0.00", cult).Text);

        // Integer with grouping
        Assert.Equal("1,235", CellNumberFormatter.Format(1234.56, "#,##0", cult).Text);

        // Minimum zero padding
        Assert.Equal("005", CellNumberFormatter.Format(5, "000", cult).Text);
        Assert.Equal(".50", CellNumberFormatter.Format(0.5, "#.00", cult).Text);

        // Optional decimals (#)
        Assert.Equal("12.3", CellNumberFormatter.Format(12.3, "0.0##", cult).Text);
        Assert.Equal("12.345", CellNumberFormatter.Format(12.345, "0.0##", cult).Text);
    }

    [Fact]
    public void CellNumberFormatter_PercentagesAndScientific_FormatAccurately()
    {
        var cult = CultureInfo.InvariantCulture;

        // Percentage multiplies by 100
        Assert.Equal("15.5%", CellNumberFormatter.Format(0.155, "0.0%", cult).Text);
        Assert.Equal("25%", CellNumberFormatter.Format(0.25, "0%", cult).Text);
        Assert.Equal("0.75%", CellNumberFormatter.Format(0.0075, "0.00%", cult).Text);

        // Scientific notation
        Assert.Equal("1.23E+04", CellNumberFormatter.Format(12345, "0.00E+00", cult).Text);
        Assert.Equal("5.00E-03", CellNumberFormatter.Format(0.005, "0.00E+00", cult).Text);
    }

    [Fact]
    public void CellNumberFormatter_ScalingCommas_DividesByThousands()
    {
        var cult = CultureInfo.InvariantCulture;

        // One trailing comma divides by 1,000
        Assert.Equal("1,500", CellNumberFormatter.Format(1500000, "#,##0,", cult).Text);

        // Two trailing commas divide by 1,000,000
        Assert.Equal("1.5 M", CellNumberFormatter.Format(1500000, "#,##0.0,, \"M\"", cult).Text);
    }

    [Fact]
    public void CellNumberFormatter_MultiSectionFormatting_PositiveNegativeZeroText()
    {
        var cult = CultureInfo.InvariantCulture;
        string fmt = "$#,##0.00;($#,##0.00);\"-\";\"Total: \"@";

        // Positive
        Assert.Equal("$1,234.56", CellNumberFormatter.Format(1234.56, fmt, cult).Text);

        // Negative
        Assert.Equal("($1,234.56)", CellNumberFormatter.Format(-1234.56, fmt, cult).Text);

        // Zero
        Assert.Equal("-", CellNumberFormatter.Format(0, fmt, cult).Text);

        // Text
        Assert.Equal("Total: Q3 Sales", CellNumberFormatter.Format("Q3 Sales", fmt, cult).Text);
    }

    [Fact]
    public void CellNumberFormatter_ConditionalSections_SelectsCorrectFormat()
    {
        var cult = CultureInfo.InvariantCulture;
        string fmt = "[>100]0.00\" High\";[<=100]0.00\" Low\"";

        Assert.Equal("150.00 High", CellNumberFormatter.Format(150, fmt, cult).Text);
        Assert.Equal("50.00 Low", CellNumberFormatter.Format(50, fmt, cult).Text);
    }

    [Fact]
    public void CellNumberFormatter_ColorTags_ReturnsExpectedColor()
    {
        var cult = CultureInfo.InvariantCulture;
        string fmt = "[Red][<0](0.00);[Green][>0]0.00;0.00";

        var negRes = CellNumberFormatter.Format(-42.5, fmt, cult);
        Assert.Equal("(42.50)", negRes.Text);
        Assert.Equal(BColor.FromArgb(255, 255, 0, 0), negRes.Color);

        var posRes = CellNumberFormatter.Format(42.5, fmt, cult);
        Assert.Equal("42.50", posRes.Text);
        Assert.Equal(BColor.FromArgb(255, 0, 128, 0), posRes.Color);

        var zeroRes = CellNumberFormatter.Format(0, fmt, cult);
        Assert.Equal("0.00", zeroRes.Text);
        Assert.Null(zeroRes.Color);
    }

    [Fact]
    public void CellNumberFormatter_DateTimeFormatting_DisambiguatesMonthsAndMinutes()
    {
        var cult = CultureInfo.InvariantCulture;

        // 45592 is 2024-10-27, 0.625 is 15:00:00 (3:00 PM)
        double serial = 45592.625;

        Assert.Equal("2024-10-27", CellNumberFormatter.Format(serial, "yyyy-mm-dd", cult).Text);
        Assert.Equal("27.10.2024", CellNumberFormatter.Format(serial, "dd.mm.yyyy", cult).Text);
        Assert.Equal("15:00:00", CellNumberFormatter.Format(serial, "hh:mm:ss", cult).Text);
        Assert.Equal("3:00 PM", CellNumberFormatter.Format(serial, "h:mm AM/PM", cult).Text);

        // Combined pattern: first mm is month (10), second mm is minute (00)
        Assert.Equal("2024-10-27 15:00:00", CellNumberFormatter.Format(serial, "yyyy-mm-dd hh:mm:ss", cult).Text);

        // Elapsed hours [h] exceeding 24 hours: 1.5 days = 36 hours
        Assert.Equal("36:00:00", CellNumberFormatter.Format(1.5, "[h]:mm:ss", cult).Text);
    }

    [Fact]
    public void TableCell_NumberFormatProperty_PreservedAcrossMutations()
    {
        var cell = new TableCell(0, 1, 0, formula: "=A1*2", numberFormat: "$#,##0.00");
        Assert.Equal("=A1*2", cell.Formula);
        Assert.Equal("$#,##0.00", cell.NumberFormat);

        var mutated = cell.WithNumberFormat("0.0%");
        Assert.Equal("0.0%", mutated.NumberFormat);
        Assert.Equal("=A1*2", mutated.Formula);

        var shifted = cell.ShiftedBy(5);
        Assert.Equal("$#,##0.00", shifted.NumberFormat);
        Assert.Equal(5, shifted.ParagraphIndex);
    }

    [Fact]
    public void RichTextDocument_EvaluateTableFormulas_AppliesCellNumberFormatAndColor()
    {
        var p0 = RichTextParagraph.Plain("50");
        var p1 = RichTextParagraph.Plain("-100");
        var p2 = RichTextParagraph.Plain("=A1+B1"); // Evaluates to -50

        // Cell 2 has a format string that turns negative numbers Red with parentheses
        var row = new TableRow([
            new TableCell(0, 1, 0),
            new TableCell(1, 1, 1),
            new TableCell(2, 1, 2, numberFormat: "$#,##0.00;[Red]($#,##0.00);\"-\"")
        ]);
        var table = new DocumentTable(0, 3, [row], [100, 100, 100]);

        var doc = RichTextDocument.FromParagraphs([p0, p1, p2]).WithTables([table]);

        var (updatedDoc, evalResult) = doc.EvaluateTableFormulas(table);

        // Value is numeric -50
        Assert.Equal(-50.0, evalResult[0, 2].Number);

        // Formatted display text in paragraph
        Assert.Equal("($50.00)", updatedDoc.Paragraphs[2].Text);

        // Paragraph run foreground is Red
        Assert.Equal(BColor.FromArgb(255, 255, 0, 0), updatedDoc.Paragraphs[2].Runs[0].Style.Foreground);

        // Formatted value via evaluation result
        var formatted = evalResult.GetFormattedValue(0, 2, CultureInfo.InvariantCulture);
        Assert.Equal("($50.00)", formatted.Text);
        Assert.Equal(BColor.FromArgb(255, 255, 0, 0), formatted.Color);
    }

    [Fact]
    public void RichTextDocument_FormatTableCells_AppliesFormattingToStaticCells()
    {
        var p0 = RichTextParagraph.Plain("1234.5");
        var p1 = RichTextParagraph.Plain("-42.0");

        var row = new TableRow([
            new TableCell(0, 1, 0, numberFormat: "#,##0.00"),
            new TableCell(1, 1, 1, numberFormat: "[Red](0.00)")
        ]);
        var table = new DocumentTable(0, 2, [row], [100, 100]);

        var doc = RichTextDocument.FromParagraphs([p0, p1]).WithTables([table]);

        var formattedDoc = doc.FormatTableCells(table, CultureInfo.InvariantCulture);

        Assert.Equal("1,234.50", formattedDoc.Paragraphs[0].Text);
        Assert.Equal("(42.00)", formattedDoc.Paragraphs[1].Text);
        Assert.Equal(BColor.FromArgb(255, 255, 0, 0), formattedDoc.Paragraphs[1].Runs[0].Style.Foreground);
    }
}
