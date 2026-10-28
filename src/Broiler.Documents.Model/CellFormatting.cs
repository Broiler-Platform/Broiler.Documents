using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model;

/// <summary>
/// The logical data type inferred from or applied to a table cell value.
/// </summary>
public enum CellDataType
{
    Empty,
    Text,
    Boolean,
    Integer,
    Decimal,
    Percentage,
    Currency,
    Date,
    Time,
    DateTime,
    Scientific,
    Error
}

/// <summary>
/// Represents the formatted visual presentation of a cell value, including optional text color.
/// </summary>
public readonly record struct FormattedCellResult(
    string Text,
    BColor? Color = null,
    CellDataType InferredType = CellDataType.Text)
{
    public override string ToString() => Text;
    public static implicit operator string(FormattedCellResult res) => res.Text;
}

/// <summary>
/// Predefined standard spreadsheet format strings.
/// </summary>
public static class CellFormatPresets
{
    public const string General = "General";
    public const string Integer = "#,##0";
    public const string Decimal2 = "#,##0.00";
    public const string Currency = "$#,##0.00;($#,##0.00);\"-\"";
    public const string Percentage = "0.0%";
    public const string Scientific = "0.00E+00";
    public const string ShortDate = "yyyy-mm-dd";
    public const string LongDate = "dddd, mmmm dd, yyyy";
    public const string Time = "hh:mm:ss";
    public const string DateTime = "yyyy-mm-dd hh:mm:ss";
}

/// <summary>
/// Utilities for converting between .NET <see cref="DateTime"/> and spreadsheet Julian/serial date numbers.
/// </summary>
public static class CellDateSerial
{
    // Excel 1900 date system base: 1899-12-30 (day 1 is 1900-01-01, day 60 is Lotus leap-year bug 1900-02-29)
    private static readonly DateTime Base1900 = new(1899, 12, 30);
    private static readonly DateTime Base1904 = new(1904, 1, 1);

    /// <summary>
    /// Converts a spreadsheet serial number into a <see cref="DateTime"/>.
    /// </summary>
    public static DateTime FromSerial(double serial, bool use1904 = false)
    {
        if (double.IsNaN(serial) || double.IsInfinity(serial))
            return DateTime.MinValue;

        DateTime baseDate = use1904 ? Base1904 : Base1900;

        if (!use1904 && serial < 60)
        {
            // Lotus 1-2-3 leap year bug adjustment for dates before March 1, 1900
            baseDate = new DateTime(1899, 12, 31);
        }

        try
        {
            return baseDate.AddDays(serial);
        }
        catch
        {
            return serial < 0 ? DateTime.MinValue : DateTime.MaxValue;
        }
    }

    /// <summary>
    /// Converts a <see cref="DateTime"/> into a spreadsheet serial number.
    /// </summary>
    public static double ToSerial(DateTime dateTime, bool use1904 = false)
    {
        DateTime baseDate = use1904 ? Base1904 : Base1900;
        double days = (dateTime - baseDate).TotalDays;

        if (!use1904 && dateTime < new DateTime(1900, 3, 1))
        {
            days -= 1.0;
        }

        return days;
    }

    /// <summary>
    /// Converts a spreadsheet serial number into a <see cref="DateOnly"/>.
    /// </summary>
    public static DateOnly ToDateOnly(double serial, bool use1904 = false) =>
        DateOnly.FromDateTime(FromSerial(serial, use1904));

    /// <summary>
    /// Converts a spreadsheet serial number fraction into a <see cref="TimeOnly"/>.
    /// </summary>
    public static TimeOnly ToTimeOnly(double serial) =>
        TimeOnly.FromDateTime(FromSerial(serial));
}

/// <summary>
/// Type inference, coercion, and parsing for spreadsheet and table cell values.
/// </summary>
public static class CellCoercion
{
    private static readonly Regex ScientificRegex = new(@"^-?[0-9]+(?:\.[0-9]+)?[eE][+-]?[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex IsoDateTimeRegex = new(@"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}(?::\d{2})?$", RegexOptions.Compiled);
    private static readonly Regex IsoDateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex CommonDateRegex = new(@"^\d{1,2}[./-]\d{1,2}[./-]\d{4}$", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"^\d{1,2}:\d{2}(?::\d{2})?(?:\s*(?:AM|PM|am|pm))?$", RegexOptions.Compiled);

    /// <summary>
    /// Infers the logical <see cref="CellDataType"/> from raw string text.
    /// </summary>
    public static CellDataType InferType(string? raw, CultureInfo? culture = null) =>
        InferDataType(raw, culture);

    public static CellDataType InferDataType(string? raw, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CellDataType.Empty;

        string trimmed = raw.Trim();

        if (trimmed.StartsWith('#') && FormulaErrorExtensions.TryParse(trimmed, out _))
            return CellDataType.Error;

        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
            return CellDataType.Boolean;

        culture ??= CultureInfo.InvariantCulture;

        if (trimmed.EndsWith('%'))
        {
            string numPart = trimmed[..^1].Trim();
            if (double.TryParse(numPart, NumberStyles.Float | NumberStyles.AllowThousands, culture, out _) ||
                double.TryParse(numPart, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
                return CellDataType.Percentage;
        }

        string currSymbol = culture.NumberFormat.CurrencySymbol;
        if (trimmed.StartsWith(currSymbol, StringComparison.Ordinal) || trimmed.EndsWith(currSymbol, StringComparison.Ordinal) ||
            trimmed.StartsWith('$') || trimmed.StartsWith('€') || trimmed.StartsWith('£') || trimmed.StartsWith('¥'))
        {
            string clean = trimmed.Replace(currSymbol, "").Replace("$", "").Replace("€", "").Replace("£", "").Replace("¥", "").Trim();
            if (double.TryParse(clean, NumberStyles.Float | NumberStyles.AllowThousands, culture, out _) ||
                double.TryParse(clean, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
                return CellDataType.Currency;
        }

        if (ScientificRegex.IsMatch(trimmed))
            return CellDataType.Scientific;

        if (IsoDateTimeRegex.IsMatch(trimmed))
            return CellDataType.DateTime;

        if (IsoDateRegex.IsMatch(trimmed) || CommonDateRegex.IsMatch(trimmed))
            return CellDataType.Date;

        if (TimeRegex.IsMatch(trimmed))
            return CellDataType.Time;

        if (long.TryParse(trimmed, NumberStyles.Integer | NumberStyles.AllowThousands, culture, out _) ||
            long.TryParse(trimmed, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
            return CellDataType.Integer;

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, culture, out _) ||
            double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
            return CellDataType.Decimal;

        return CellDataType.Text;
    }

    /// <summary>
    /// Attempts to coerce any value (number, boolean, date, percentage string, etc.) into a double.
    /// </summary>
    public static bool TryCoerceToDouble(object? value, out double result, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        result = 0;

        switch (value)
        {
            case null:
                result = 0;
                return true;
            case CellValue cv:
                return cv.AsDouble(out result);
            case double d:
                result = d;
                return !double.IsNaN(d) && !double.IsInfinity(d);
            case float f:
                result = f;
                return !float.IsNaN(f) && !float.IsInfinity(f);
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case decimal dec:
                result = (double)dec;
                return true;
            case short s:
                result = s;
                return true;
            case byte b:
                result = b;
                return true;
            case bool bo:
                result = bo ? 1.0 : 0.0;
                return true;
            case DateTime dt:
                result = CellDateSerial.ToSerial(dt);
                return true;
            case DateOnly dOnly:
                result = CellDateSerial.ToSerial(dOnly.ToDateTime(TimeOnly.MinValue));
                return true;
            case TimeOnly tOnly:
                result = tOnly.ToTimeSpan().TotalDays;
                return true;
            case string str:
            {
                string trimmed = str.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    result = 0;
                    return true;
                }

                if (trimmed.EndsWith('%'))
                {
                    string pct = trimmed[..^1].Trim();
                    if (double.TryParse(pct, NumberStyles.Float | NumberStyles.AllowThousands, culture, out double pVal) ||
                        double.TryParse(pct, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out pVal))
                    {
                        result = pVal / 100.0;
                        return true;
                    }
                }

                string curr = culture.NumberFormat.CurrencySymbol;
                if (trimmed.StartsWith(curr) || trimmed.EndsWith(curr) ||
                    trimmed.StartsWith('$') || trimmed.StartsWith('€') || trimmed.StartsWith('£') || trimmed.StartsWith('¥'))
                {
                    string clean = trimmed.Replace(curr, "").Replace("$", "").Replace("€", "").Replace("£", "").Replace("¥", "").Trim();
                    if (double.TryParse(clean, NumberStyles.Float | NumberStyles.AllowThousands, culture, out double cVal) ||
                        double.TryParse(clean, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out cVal))
                    {
                        result = cVal;
                        return true;
                    }
                }

                if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, culture, out double dVal) ||
                    double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out dVal))
                {
                    result = dVal;
                    return true;
                }

                if (DateTime.TryParse(trimmed, culture, DateTimeStyles.None, out var dtVal) ||
                    DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out dtVal))
                {
                    result = CellDateSerial.ToSerial(dtVal);
                    return true;
                }

                return false;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to coerce a value into a boolean.
    /// </summary>
    public static bool TryCoerceToBoolean(object? value, out bool result, CultureInfo? culture = null)
    {
        result = false;
        if (value is null)
            return false;

        if (value is CellValue cv)
            return cv.AsBoolean(out result);

        if (value is bool b)
        {
            result = b;
            return true;
        }

        if (TryCoerceToDouble(value, out double num, culture))
        {
            result = num != 0;
            return true;
        }

        string s = value.ToString()?.Trim() ?? string.Empty;
        if (string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "YES", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "Y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "1", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "NO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "N", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "0", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to coerce a value into a <see cref="DateTime"/>.
    /// </summary>
    public static bool TryCoerceToDateTime(object? value, out DateTime result, CultureInfo? culture = null, bool use1904 = false)
    {
        culture ??= CultureInfo.InvariantCulture;
        result = DateTime.MinValue;

        if (value is null)
            return false;

        if (value is DateTime dt)
        {
            result = dt;
            return true;
        }

        if (value is DateOnly dOnly)
        {
            result = dOnly.ToDateTime(TimeOnly.MinValue);
            return true;
        }

        if (value is TimeOnly tOnly)
        {
            result = DateTime.Today.Add(tOnly.ToTimeSpan());
            return true;
        }

        if (value is CellValue cv)
        {
            if (cv.IsNumber)
            {
                result = CellDateSerial.FromSerial(cv.Number, use1904);
                return true;
            }
            if (cv.IsText && TryCoerceToDateTime(cv.Text, out result, culture, use1904))
                return true;
            return false;
        }

        if (value is double or float or int or long or decimal)
        {
            double serial = Convert.ToDouble(value);
            result = CellDateSerial.FromSerial(serial, use1904);
            return true;
        }

        string s = value.ToString()?.Trim() ?? string.Empty;
        if (DateTime.TryParse(s, culture, DateTimeStyles.None, out result) ||
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double sVal))
        {
            result = CellDateSerial.FromSerial(sVal, use1904);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Coerces a <see cref="CellValue"/> into a target <see cref="CellDataType"/>.
    /// </summary>
    public static CellValue Coerce(CellValue source, CellDataType targetType, CultureInfo? culture = null)
    {
        if (source.IsError)
            return source;

        switch (targetType)
        {
            case CellDataType.Empty:
                return CellValue.Empty;

            case CellDataType.Boolean:
                return source.AsBoolean(out bool b) ? CellValue.FromBoolean(b) : CellValue.FromError(FormulaError.Value);

            case CellDataType.Integer:
            case CellDataType.Decimal:
            case CellDataType.Percentage:
            case CellDataType.Currency:
            case CellDataType.Scientific:
                return source.AsDouble(out double d) ? CellValue.FromNumber(d) : CellValue.FromError(FormulaError.Value);

            case CellDataType.Date:
            case CellDataType.Time:
            case CellDataType.DateTime:
                return TryCoerceToDateTime(source, out var dt, culture)
                    ? CellValue.FromNumber(CellDateSerial.ToSerial(dt))
                    : CellValue.FromError(FormulaError.Value);

            case CellDataType.Text:
                return CellValue.FromText(source.Format());

            default:
                return source;
        }
    }
}

/// <summary>
/// A locale-aware Excel and OpenDocument format string engine supporting multi-section formats,
/// conditions, colors, currencies, dates, times, scientific notation, and padding.
/// </summary>
public static class CellNumberFormatter
{
    private enum ConditionOp { Greater, GreaterEqual, Less, LessEqual, Equal, NotEqual }

    private sealed class FormatSection
    {
        public ConditionOp? ConditionOperator { get; set; }
        public double? ConditionValue { get; set; }
        public BColor? Color { get; set; }
        public string? CurrencySymbol { get; set; }
        public string RawPattern { get; set; } = string.Empty;
        public bool IsGeneral { get; set; }
        public bool IsText { get; set; }
        public bool IsDateTime { get; set; }
        public bool IsPercentage { get; set; }
        public int ScalingCommas { get; set; }
        public int MinIntDigits { get; set; } = 1;
        public int MinDecDigits { get; set; } = 0;
        public int MaxDecDigits { get; set; } = 0;
        public bool HasThousandsGrouping { get; set; }
        public bool HasScientific { get; set; }
        public int ExpDigits { get; set; } = 2;
        public string Prefix { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;

        public bool Matches(double value)
        {
            if (!ConditionOperator.HasValue || !ConditionValue.HasValue)
                return true;

            double thresh = ConditionValue.Value;
            return ConditionOperator.Value switch
            {
                ConditionOp.Greater => value > thresh,
                ConditionOp.GreaterEqual => value >= thresh,
                ConditionOp.Less => value < thresh,
                ConditionOp.LessEqual => value <= thresh,
                ConditionOp.Equal => Math.Abs(value - thresh) < 1e-9,
                ConditionOp.NotEqual => Math.Abs(value - thresh) >= 1e-9,
                _ => true
            };
        }
    }

    /// <summary>
    /// Formats a <see cref="CellValue"/> using an Excel or spreadsheet format string and culture.
    /// </summary>
    public static FormattedCellResult Format(
        CellValue value,
        string? formatString = null,
        CultureInfo? culture = null,
        bool use1904 = false)
    {
        culture ??= CultureInfo.InvariantCulture;

        if (value.IsError)
            return new FormattedCellResult(value.Error.ToErrorString(), null, CellDataType.Error);

        if (string.IsNullOrWhiteSpace(formatString) || string.Equals(formatString, "General", StringComparison.OrdinalIgnoreCase))
        {
            CellDataType inferred = value.IsEmpty ? CellDataType.Empty :
                value.IsNumber ? (value.Number == Math.Floor(value.Number) ? CellDataType.Integer : CellDataType.Decimal) :
                value.IsBoolean ? CellDataType.Boolean : CellDataType.Text;
            return new FormattedCellResult(value.Format(), null, inferred);
        }

        var sections = ParseSections(formatString);

        if (value.IsText)
        {
            // 4th section is text section
            if (sections.Count >= 4)
                return FormatTextSection(sections[3], value.Text ?? string.Empty);
            return new FormattedCellResult(value.Text ?? string.Empty, sections[0].Color, CellDataType.Text);
        }

        if (value.IsEmpty)
            return new FormattedCellResult(string.Empty, null, CellDataType.Empty);

        if (!value.AsDouble(out double num))
            return new FormattedCellResult(value.Format(), null, CellDataType.Text);

        // Section selection
        FormatSection selected = SelectSection(sections, num, out double valueToFormat);
        return FormatNumericSection(selected, valueToFormat, num < 0, culture, use1904);
    }

    /// <summary>Formats a double value.</summary>
    public static FormattedCellResult Format(double number, string? formatString = null, CultureInfo? culture = null) =>
        Format(CellValue.FromNumber(number), formatString, culture);

    /// <summary>Formats a DateTime value.</summary>
    public static FormattedCellResult Format(DateTime dateTime, string? formatString = null, CultureInfo? culture = null) =>
        Format(CellValue.FromNumber(CellDateSerial.ToSerial(dateTime)), formatString ?? CellFormatPresets.ShortDate, culture);

    /// <summary>Formats a text string.</summary>
    public static FormattedCellResult Format(string? text, string? formatString = null, CultureInfo? culture = null) =>
        Format(CellValue.FromText(text ?? string.Empty), formatString, culture);

    private static FormatSection SelectSection(List<FormatSection> sections, double originalVal, out double valueToFormat)
    {
        valueToFormat = originalVal;

        // Check explicit conditions
        foreach (var s in sections)
        {
            if (s.ConditionOperator.HasValue && s.Matches(originalVal))
            {
                valueToFormat = (s.ConditionOperator == ConditionOp.Less && originalVal < 0) ? Math.Abs(originalVal) : originalVal;
                return s;
            }
        }

        // Standard positive / negative / zero rules
        if (sections.Count == 1)
        {
            valueToFormat = originalVal;
            return sections[0];
        }

        if (sections.Count == 2)
        {
            if (originalVal >= 0)
            {
                valueToFormat = originalVal;
                return sections[0];
            }
            valueToFormat = Math.Abs(originalVal);
            return sections[1];
        }

        // 3 or 4 sections: Pos; Neg; Zero[; Text]
        if (originalVal > 0)
        {
            valueToFormat = originalVal;
            return sections[0];
        }
        if (originalVal < 0)
        {
            valueToFormat = Math.Abs(originalVal);
            return sections[1];
        }
        valueToFormat = 0;
        return sections[2];
    }

    private static FormattedCellResult FormatTextSection(FormatSection section, string text)
    {
        string pat = section.RawPattern;
        string result = pat.Contains('@') ? pat.Replace("@", text) : (pat + text);
        result = CleanLiteralString(result);
        return new FormattedCellResult(result, section.Color, CellDataType.Text);
    }

    private static FormattedCellResult FormatNumericSection(
        FormatSection section,
        double val,
        bool wasOriginallyNegative,
        CultureInfo culture,
        bool use1904)
    {
        if (section.IsGeneral)
            return new FormattedCellResult(val.ToString("G15", culture), section.Color, CellDataType.Decimal);

        if (section.IsDateTime)
        {
            DateTime dt = CellDateSerial.FromSerial(val, use1904);
            string formattedDate = FormatDateTime(dt, val, section.RawPattern, culture);
            return new FormattedCellResult(formattedDate, section.Color, CellDataType.DateTime);
        }

        double displayVal = val;
        if (section.IsPercentage)
            displayVal *= 100.0;

        if (section.ScalingCommas > 0)
            displayVal /= Math.Pow(1000.0, section.ScalingCommas);

        string numText;
        if (section.HasScientific)
        {
            string fmt = "E" + Math.Max(0, section.MaxDecDigits);
            string rawSci = displayVal.ToString(fmt, culture);
            // Normalize 3-digit exponent to 2-digit standard e.g. E+004 -> E+04
            numText = Regex.Replace(rawSci, @"(E[+-])0(\d{2})$", "$1$2");
        }
        else
        {
            double rounded = Math.Round(Math.Abs(displayVal), section.MaxDecDigits, MidpointRounding.AwayFromZero);
            long intPart = (long)Math.Truncate(rounded);
            double fracPart = rounded - intPart;

            string intStr;
            if (intPart == 0 && section.MinIntDigits == 0)
            {
                intStr = string.Empty;
            }
            else
            {
                intStr = intPart.ToString("D" + section.MinIntDigits, CultureInfo.InvariantCulture);
                if (section.HasThousandsGrouping && intStr.Length > 3)
                {
                    string groupSep = culture.NumberFormat.NumberGroupSeparator;
                    var sb = new StringBuilder();
                    int count = 0;
                    for (int i = intStr.Length - 1; i >= 0; i--)
                    {
                        sb.Insert(0, intStr[i]);
                        count++;
                        if (count % 3 == 0 && i > 0)
                            sb.Insert(0, groupSep);
                    }
                    intStr = sb.ToString();
                }
            }

            string fracStr = string.Empty;
            if (section.MaxDecDigits > 0)
            {
                long scaledFrac = (long)Math.Round(fracPart * Math.Pow(10, section.MaxDecDigits), MidpointRounding.AwayFromZero);
                string rawFrac = scaledFrac.ToString("D" + section.MaxDecDigits, CultureInfo.InvariantCulture);

                while (rawFrac.Length > section.MinDecDigits && rawFrac.EndsWith('0'))
                    rawFrac = rawFrac[..^1];

                if (rawFrac.Length > 0)
                    fracStr = culture.NumberFormat.NumberDecimalSeparator + rawFrac;
            }

            numText = intStr + fracStr;
        }

        var sbFinal = new StringBuilder();
        if (wasOriginallyNegative && !section.RawPattern.Contains('-') && !section.RawPattern.Contains('('))
            sbFinal.Append('-');

        sbFinal.Append(section.Prefix);
        sbFinal.Append(numText);
        sbFinal.Append(section.Suffix);

        CellDataType dtResult = section.IsPercentage ? CellDataType.Percentage :
            section.CurrencySymbol != null || section.Prefix.Contains('$') || section.Prefix.Contains('€') ? CellDataType.Currency :
            section.HasScientific ? CellDataType.Scientific :
            section.MaxDecDigits > 0 ? CellDataType.Decimal : CellDataType.Integer;

        return new FormattedCellResult(sbFinal.ToString(), section.Color, dtResult);
    }

    private static string FormatDateTime(DateTime dt, double rawSerial, string pattern, CultureInfo culture)
    {
        var tokens = TokenizeDateTimePattern(pattern);
        var sb = new StringBuilder();

        bool hasAmPm = tokens.Any(t => t.Type == DtTokenType.AmPm);

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t.Type)
            {
                case DtTokenType.Year4:
                    sb.Append(dt.ToString("yyyy", culture));
                    break;
                case DtTokenType.Year2:
                    sb.Append(dt.ToString("yy", culture));
                    break;
                case DtTokenType.MonthLong:
                    sb.Append(culture.DateTimeFormat.GetMonthName(dt.Month));
                    break;
                case DtTokenType.MonthShort:
                    sb.Append(culture.DateTimeFormat.GetAbbreviatedMonthName(dt.Month));
                    break;
                case DtTokenType.Month2:
                    sb.Append(dt.Month.ToString("D2", CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.Month1:
                    sb.Append(dt.Month.ToString(CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.DayLong:
                    sb.Append(culture.DateTimeFormat.GetDayName(dt.DayOfWeek));
                    break;
                case DtTokenType.DayShort:
                    sb.Append(culture.DateTimeFormat.GetAbbreviatedDayName(dt.DayOfWeek));
                    break;
                case DtTokenType.Day2:
                    sb.Append(dt.Day.ToString("D2", CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.Day1:
                    sb.Append(dt.Day.ToString(CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.Hour2:
                {
                    int h = hasAmPm ? ((dt.Hour % 12 == 0) ? 12 : dt.Hour % 12) : dt.Hour;
                    sb.Append(h.ToString("D2", CultureInfo.InvariantCulture));
                    break;
                }
                case DtTokenType.Hour1:
                {
                    int h = hasAmPm ? ((dt.Hour % 12 == 0) ? 12 : dt.Hour % 12) : dt.Hour;
                    sb.Append(h.ToString(CultureInfo.InvariantCulture));
                    break;
                }
                case DtTokenType.Minute2:
                    sb.Append(dt.Minute.ToString("D2", CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.Minute1:
                    sb.Append(dt.Minute.ToString(CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.Second2:
                    sb.Append(dt.Second.ToString("D2", CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.Second1:
                    sb.Append(dt.Second.ToString(CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.AmPm:
                    sb.Append(dt.Hour >= 12 ? (t.Text.Contains("pm", StringComparison.Ordinal) ? "pm" : "PM") :
                                              (t.Text.Contains("am", StringComparison.Ordinal) ? "am" : "AM"));
                    break;
                case DtTokenType.ElapsedHours:
                    sb.Append(((long)(rawSerial * 24)).ToString(CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.ElapsedMinutes:
                    sb.Append(((long)(rawSerial * 1440)).ToString(CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.ElapsedSeconds:
                    sb.Append(((long)(rawSerial * 86400)).ToString(CultureInfo.InvariantCulture));
                    break;
                case DtTokenType.Literal:
                    sb.Append(t.Text);
                    break;
            }
        }

        return sb.ToString();
    }

    private enum DtTokenType
    {
        Year4, Year2, MonthLong, MonthShort, Month2, Month1,
        DayLong, DayShort, Day2, Day1, Hour2, Hour1,
        Minute2, Minute1, Second2, Second1, AmPm,
        ElapsedHours, ElapsedMinutes, ElapsedSeconds, Literal
    }

    private readonly record struct DtToken(DtTokenType Type, string Text);

    private static List<DtToken> TokenizeDateTimePattern(string pattern)
    {
        var rawTokens = new List<DtToken>();
        int i = 0;
        int n = pattern.Length;

        while (i < n)
        {
            char ch = pattern[i];

            if (ch == '"')
            {
                i++;
                var sbLit = new StringBuilder();
                while (i < n && pattern[i] != '"')
                {
                    sbLit.Append(pattern[i]);
                    i++;
                }
                if (i < n) i++;
                rawTokens.Add(new DtToken(DtTokenType.Literal, sbLit.ToString()));
                continue;
            }

            if (ch == '\\' && i + 1 < n)
            {
                rawTokens.Add(new DtToken(DtTokenType.Literal, pattern[i + 1].ToString()));
                i += 2;
                continue;
            }

            if (ch == '[' && i + 2 < n && (pattern[i + 1] == 'h' || pattern[i + 1] == 'H') && pattern[i + 2] == ']')
            {
                rawTokens.Add(new DtToken(DtTokenType.ElapsedHours, "[h]"));
                i += 3;
                continue;
            }
            if (ch == '[' && i + 2 < n && (pattern[i + 1] == 'm' || pattern[i + 1] == 'M') && pattern[i + 2] == ']')
            {
                rawTokens.Add(new DtToken(DtTokenType.ElapsedMinutes, "[m]"));
                i += 3;
                continue;
            }
            if (ch == '[' && i + 2 < n && (pattern[i + 1] == 's' || pattern[i + 1] == 'S') && pattern[i + 2] == ']')
            {
                rawTokens.Add(new DtToken(DtTokenType.ElapsedSeconds, "[s]"));
                i += 3;
                continue;
            }

            if (i + 4 < n && string.Equals(pattern.Substring(i, 5), "AM/PM", StringComparison.OrdinalIgnoreCase))
            {
                rawTokens.Add(new DtToken(DtTokenType.AmPm, pattern.Substring(i, 5)));
                i += 5;
                continue;
            }
            if (i + 2 < n && string.Equals(pattern.Substring(i, 3), "A/P", StringComparison.OrdinalIgnoreCase))
            {
                rawTokens.Add(new DtToken(DtTokenType.AmPm, pattern.Substring(i, 3)));
                i += 3;
                continue;
            }

            int count = 1;
            char lower = char.ToLowerInvariant(ch);
            while (i + count < n && char.ToLowerInvariant(pattern[i + count]) == lower)
                count++;

            switch (lower)
            {
                case 'y':
                    rawTokens.Add(new DtToken(count >= 3 ? DtTokenType.Year4 : DtTokenType.Year2, pattern.Substring(i, count)));
                    i += count;
                    break;
                case 'm':
                    if (count >= 4) rawTokens.Add(new DtToken(DtTokenType.MonthLong, pattern.Substring(i, count)));
                    else if (count == 3) rawTokens.Add(new DtToken(DtTokenType.MonthShort, pattern.Substring(i, count)));
                    else rawTokens.Add(new DtToken(count == 2 ? DtTokenType.Month2 : DtTokenType.Month1, pattern.Substring(i, count)));
                    i += count;
                    break;
                case 'd':
                    if (count >= 4) rawTokens.Add(new DtToken(DtTokenType.DayLong, pattern.Substring(i, count)));
                    else if (count == 3) rawTokens.Add(new DtToken(DtTokenType.DayShort, pattern.Substring(i, count)));
                    else rawTokens.Add(new DtToken(count == 2 ? DtTokenType.Day2 : DtTokenType.Day1, pattern.Substring(i, count)));
                    i += count;
                    break;
                case 'h':
                    rawTokens.Add(new DtToken(count >= 2 ? DtTokenType.Hour2 : DtTokenType.Hour1, pattern.Substring(i, count)));
                    i += count;
                    break;
                case 's':
                    rawTokens.Add(new DtToken(count >= 2 ? DtTokenType.Second2 : DtTokenType.Second1, pattern.Substring(i, count)));
                    i += count;
                    break;
                default:
                    rawTokens.Add(new DtToken(DtTokenType.Literal, pattern.Substring(i, count)));
                    i += count;
                    break;
            }
        }

        // Second pass: disambiguate 'm'/'mm' between Month and Minute
        for (int idx = 0; idx < rawTokens.Count; idx++)
        {
            var tok = rawTokens[idx];
            if (tok.Type is DtTokenType.Month1 or DtTokenType.Month2)
            {
                bool precededByHour = false;
                for (int p = idx - 1; p >= 0; p--)
                {
                    if (rawTokens[p].Type is DtTokenType.Hour1 or DtTokenType.Hour2)
                    {
                        precededByHour = true;
                        break;
                    }
                    if (rawTokens[p].Type is DtTokenType.Year2 or DtTokenType.Year4 or DtTokenType.Day1 or DtTokenType.Day2)
                        break;
                }

                bool followedBySecond = false;
                for (int f = idx + 1; f < rawTokens.Count; f++)
                {
                    if (rawTokens[f].Type is DtTokenType.Second1 or DtTokenType.Second2)
                    {
                        followedBySecond = true;
                        break;
                    }
                    if (rawTokens[f].Type is DtTokenType.Year2 or DtTokenType.Year4 or DtTokenType.Day1 or DtTokenType.Day2)
                        break;
                }

                if (precededByHour || followedBySecond)
                {
                    rawTokens[idx] = new DtToken(tok.Type == DtTokenType.Month2 ? DtTokenType.Minute2 : DtTokenType.Minute1, tok.Text);
                }
            }
        }

        return rawTokens;
    }

    private static List<FormatSection> ParseSections(string formatString)
    {
        var rawSections = SplitSections(formatString);
        var parsed = new List<FormatSection>(rawSections.Count);

        foreach (var raw in rawSections)
            parsed.Add(ParseSingleSection(raw));

        return parsed;
    }

    private static List<string> SplitSections(string formatString)
    {
        var sections = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;
        bool inBracket = false;

        for (int i = 0; i < formatString.Length; i++)
        {
            char ch = formatString[i];
            if (ch == '"')
            {
                inQuote = !inQuote;
                current.Append(ch);
            }
            else if (ch == '[' && !inQuote)
            {
                inBracket = true;
                current.Append(ch);
            }
            else if (ch == ']' && !inQuote)
            {
                inBracket = false;
                current.Append(ch);
            }
            else if (ch == ';' && !inQuote && !inBracket)
            {
                sections.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        sections.Add(current.ToString());
        return sections;
    }

    private static FormatSection ParseSingleSection(string raw)
    {
        var section = new FormatSection();
        string clean = raw.Trim();

        // 1. Parse bracketed tags: [Red], [>100], [$$-409]
        while (clean.StartsWith('['))
        {
            int closeIdx = clean.IndexOf(']');
            if (closeIdx < 0) break;

            string tag = clean.Substring(1, closeIdx - 1).Trim();

            // Elapsed time patterns like [h], [m], [s] are part of datetime pattern
            if (string.Equals(tag, "h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "hh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "m", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "mm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "s", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "ss", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Color tag
            if (TryParseColor(tag, out var color))
            {
                section.Color = color;
                clean = clean[(closeIdx + 1)..].Trim();
                continue;
            }

            // Condition tag: [>100], [<=50]
            if (TryParseCondition(tag, out var op, out var thresh))
            {
                section.ConditionOperator = op;
                section.ConditionValue = thresh;
                clean = clean[(closeIdx + 1)..].Trim();
                continue;
            }

            // Currency tag: [$$-409], [$€-407]
            if (tag.StartsWith('$'))
            {
                string currPart = tag[1..];
                int dashIdx = currPart.IndexOf('-');
                section.CurrencySymbol = dashIdx >= 0 ? currPart[..dashIdx] : currPart;
                clean = clean[(closeIdx + 1)..].Trim();
                continue;
            }

            break;
        }

        section.RawPattern = clean;

        if (string.Equals(clean, "General", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(clean))
        {
            section.IsGeneral = true;
            return section;
        }

        if (clean.Contains('@'))
        {
            section.IsText = true;
            return section;
        }

        // Check if date / time pattern
        if (IsDateTimePattern(clean))
        {
            section.IsDateTime = true;
            return section;
        }

        // Numeric pattern
        section.IsPercentage = clean.Contains('%');

        // Check scaling commas immediately following digit placeholders
        int lastDigit = -1;
        for (int i = 0; i < clean.Length; i++)
        {
            if (clean[i] is '0' or '#' or '?')
                lastDigit = i;
        }

        int scaling = 0;
        if (lastDigit >= 0)
        {
            int commaIdx = lastDigit + 1;
            while (commaIdx < clean.Length && clean[commaIdx] == ',')
            {
                scaling++;
                commaIdx++;
            }
            if (scaling > 0)
            {
                clean = clean[..(lastDigit + 1)] + clean[commaIdx..];
            }
        }
        section.ScalingCommas = scaling;

        // Scientific notation
        if (clean.Contains("E+", StringComparison.OrdinalIgnoreCase) || clean.Contains("E-", StringComparison.OrdinalIgnoreCase))
        {
            section.HasScientific = true;
        }

        // Decimal and Integer parts
        int dotIdx = clean.IndexOf('.');
        string intPart = dotIdx >= 0 ? clean[..dotIdx] : clean;
        string decPart = dotIdx >= 0 ? clean[(dotIdx + 1)..] : string.Empty;

        section.HasThousandsGrouping = intPart.Contains(',');
        section.MinIntDigits = intPart.Count(c => c == '0');

        int minDec = 0;
        int maxDec = 0;
        foreach (char ch in decPart)
        {
            if (ch == '0') { minDec++; maxDec++; }
            else if (ch is '#' or '?') { maxDec++; }
            else if (ch is 'e' or 'E' or '%' or '"' or ' ' or ')' or ']') break;
        }
        section.MinDecDigits = minDec;
        section.MaxDecDigits = maxDec;

        // Prefix and Suffix literals
        ExtractPrefixAndSuffix(clean, section);

        return section;
    }

    private static void ExtractPrefixAndSuffix(string clean, FormatSection section)
    {
        int firstDigit = -1;
        int lastDigit = -1;

        for (int i = 0; i < clean.Length; i++)
        {
            char c = clean[i];
            if (c is '0' or '#' or '?')
            {
                if (firstDigit == -1) firstDigit = i;
                lastDigit = i;
            }
        }

        if (firstDigit >= 0)
        {
            section.Prefix = CleanLiteralString(clean[..firstDigit]);
            if (lastDigit + 1 < clean.Length)
            {
                string rawSuffix = clean[(lastDigit + 1)..];
                if (rawSuffix.StartsWith('.'))
                {
                    int endDec = 1;
                    while (endDec < rawSuffix.Length && rawSuffix[endDec] is '0' or '#' or '?')
                        endDec++;
                    rawSuffix = rawSuffix[endDec..];
                }
                section.Suffix = CleanLiteralString(rawSuffix);
            }
        }
        else
        {
            section.Prefix = CleanLiteralString(clean);
        }

        if (section.CurrencySymbol != null && !section.Prefix.Contains(section.CurrencySymbol))
            section.Prefix = section.CurrencySymbol + section.Prefix;
    }

    private static string CleanLiteralString(string raw)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == '"')
            {
                i++;
                while (i < raw.Length && raw[i] != '"')
                {
                    sb.Append(raw[i]);
                    i++;
                }
                if (i < raw.Length) i++;
            }
            else if (c == '\\' && i + 1 < raw.Length)
            {
                sb.Append(raw[i + 1]);
                i += 2;
            }
            else if (c == '_')
            {
                sb.Append(' ');
                i += 2; // skip _ and following char
            }
            else if (c == '*')
            {
                i += 2; // skip * and fill char
            }
            else if (c != ',')
            {
                sb.Append(c);
                i++;
            }
            else
            {
                i++;
            }
        }
        return sb.ToString();
    }

    private static bool IsDateTimePattern(string pattern)
    {
        string unquoted = Regex.Replace(pattern, "\"[^\"]*\"", "");
        unquoted = Regex.Replace(unquoted, @"\[[^\]]*\]", "");
        return unquoted.Any(c => c is 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 's' or 'S') ||
               (unquoted.Contains('m', StringComparison.OrdinalIgnoreCase) && !unquoted.Contains('#') && !unquoted.Contains('0'));
    }

    private static bool TryParseCondition(string tag, out ConditionOp op, out double thresh)
    {
        op = ConditionOp.Equal;
        thresh = 0;

        var m = Regex.Match(tag, @"^([<>=!]+)\s*(-?[0-9]+(?:\.[0-9]+)?)$");
        if (!m.Success) return false;

        string opStr = m.Groups[1].Value;
        string numStr = m.Groups[2].Value;

        op = opStr switch
        {
            ">" => ConditionOp.Greater,
            ">=" => ConditionOp.GreaterEqual,
            "<" => ConditionOp.Less,
            "<=" => ConditionOp.LessEqual,
            "=" => ConditionOp.Equal,
            "<>" or "!=" => ConditionOp.NotEqual,
            _ => ConditionOp.Equal
        };

        return double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out thresh);
    }

    private static bool TryParseColor(string tag, out BColor color)
    {
        color = default;
        if (string.Equals(tag, "Red", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 255, 0, 0); return true; }
        if (string.Equals(tag, "Green", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 0, 128, 0); return true; }
        if (string.Equals(tag, "Blue", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 0, 0, 255); return true; }
        if (string.Equals(tag, "Yellow", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 255, 255, 0); return true; }
        if (string.Equals(tag, "Cyan", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 0, 255, 255); return true; }
        if (string.Equals(tag, "Magenta", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 255, 0, 255); return true; }
        if (string.Equals(tag, "White", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 255, 255, 255); return true; }
        if (string.Equals(tag, "Black", StringComparison.OrdinalIgnoreCase)) { color = BColor.FromArgb(255, 0, 0, 0); return true; }
        return false;
    }
}
