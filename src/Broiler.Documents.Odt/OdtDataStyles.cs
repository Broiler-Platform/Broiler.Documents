using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Broiler.Documents.Odt;

/// <summary>
/// Formats a value through an ODF data style (<c>number:number-style</c>,
/// <c>number:currency-style</c>, <c>number:percentage-style</c>,
/// <c>number:date-style</c> or <c>number:boolean-style</c>) the way
/// LibreOffice's number formatter shows it: a table cell that holds a value, or a
/// date field.
/// </summary>
/// <remarks>
/// <para>
/// A style formats in its own language (<c>number:language</c> and
/// <c>number:country</c>). A style that states none formats in the system
/// locale, not the document's language: LibreOffice reads it as
/// <c>LANGUAGE_SYSTEM</c> (<c>SvXMLNumFormatContext</c>). The system locale is
/// the application's culture, read as en-US when that is the invariant one.
/// </para>
/// <para>
/// A date style with <c>number:automatic-order="true"</c> whose elements are
/// those of one of the formatter's built-in date formats is replaced by that
/// format of the style's locale, with the locale's order and separators
/// (<c>aDefaultDateFormats</c> in xmloff's <c>xmlnumfi.cxx</c>). With
/// <c>number:format-source="language"</c> only the system short and long date
/// formats are candidates. Every other style is read in the order and with the
/// separators it states.
/// </para>
/// <para>
/// The method answers false, and the caller keeps the text the producer cached,
/// for anything it cannot show as LibreOffice would: a locale this table does
/// not hold where the locale matters, scientific and fraction numbers, General
/// numbers, eras, quarters and weeks, and an automatically ordered date and
/// time.
/// </para>
/// </remarks>
internal static class OdtDataStyles
{
    /// <summary>How many <c>style:map</c> steps a lookup follows before giving up.</summary>
    private const int MaxMapDepth = 4;

    /// <summary>Formats a number (<c>office:value</c>) through <paramref name="style"/>.</summary>
    public static bool TryFormatNumber(
        XElement style, double value, IReadOnlyDictionary<string, XElement> formats, out string text) =>
        TryFormatNumber(style, value, formats, signed: true, depth: 0, out text);

    /// <summary>
    /// Formats a date and time (<c>office:date-value</c>, <c>text:date-value</c>)
    /// through a <c>number:date-style</c>.
    /// </summary>
    public static bool TryFormatDate(XElement style, DateTime value, out string text)
    {
        text = string.Empty;
        if (style.Name != OdtNamespaces.Number + "date-style" || LocaleOf(style) is not OdtLocale locale ||
            locale.MonthsLong.Length != 12 || locale.DaysLong.Length != 7)
            return false;

        var parts = new List<XElement>();
        foreach (XElement element in style.Elements())
        {
            if (element.Name == OdtNamespaces.Style + "text-properties")
                continue;
            if (element.Name.Namespace != OdtNamespaces.Number || !IsDatePart(element.Name.LocalName))
                return false;
            parts.Add(element);
        }

        if (parts.Count == 0)
            return false;

        if (Attribute(style, "automatic-order") == "true")
        {
            // An automatically ordered date and time stands for one of the
            // locale's date and time formats, which this does not hold.
            if (parts.Any(part => part.Name.LocalName is "hours" or "minutes" or "seconds" or "am-pm"))
                return false;
            if (DefaultDateFormat(parts, Attribute(style, "format-source") == "language") is OdtDateFormat format)
                return locale.DateFormats.TryGetValue(format, out string? code) && TryFormatDateCode(code, value, locale, out text);
        }

        bool twelveHour = parts.Any(part => part.Name.LocalName == "am-pm");
        var builder = new StringBuilder();
        foreach (XElement part in parts)
        {
            bool isLong = Attribute(part, "style") == "long";
            switch (part.Name.LocalName)
            {
                case "text":
                    builder.Append(part.Value);
                    break;
                case "day":
                    builder.Append(Digits(value.Day, isLong ? 2 : 1));
                    break;
                case "month":
                    if (Attribute(part, "textual") == "true")
                    {
                        if (Attribute(part, "possessive-form") == "true")
                            return false;
                        builder.Append((isLong ? locale.MonthsLong : locale.MonthsShort)[value.Month - 1]);
                    }
                    else
                        builder.Append(Digits(value.Month, isLong ? 2 : 1));
                    break;
                case "year":
                    builder.Append(isLong ? Digits(value.Year, 4) : Digits(value.Year % 100, 2));
                    break;
                case "day-of-week":
                    builder.Append((isLong ? locale.DaysLong : locale.DaysShort)[(int)value.DayOfWeek]);
                    break;
                case "hours":
                    int hour = twelveHour ? (value.Hour % 12 == 0 ? 12 : value.Hour % 12) : value.Hour;
                    builder.Append(Digits(hour, isLong ? 2 : 1));
                    break;
                case "minutes":
                    builder.Append(Digits(value.Minute, isLong ? 2 : 1));
                    break;
                case "seconds":
                    if (int.TryParse(Attribute(part, "decimal-places"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int places) && places > 0)
                        return false;
                    builder.Append(Digits(value.Second, isLong ? 2 : 1));
                    break;
                case "am-pm":
                    if (locale.Am is null || locale.Pm is null)
                        return false;
                    builder.Append(value.Hour < 12 ? locale.Am : locale.Pm);
                    break;
                default:
                    return false;
            }
        }

        text = builder.ToString();
        return true;
    }

    /// <summary>
    /// Parses an ODF date and time value (<c>2008-11-10T11:47:15.98</c>,
    /// <c>2012-02-18T00:00:00.0Z</c>, <c>2012-02-18</c>) as the wall-clock time
    /// it states. A zone designator is not applied: LibreOffice shows the
    /// stated date.
    /// </summary>
    public static bool TryParseDateValue(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string trimmed = value.Trim();
        if (trimmed.EndsWith('Z'))
            trimmed = trimmed[..^1];
        else if (trimmed.Length > 19 && trimmed.LastIndexOfAny(['+', '-']) is int sign && sign > 18)
            trimmed = trimmed[..sign];
        int dot = trimmed.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
            trimmed = trimmed[..dot];
        return DateTime.TryParseExact(trimmed, ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryFormatNumber(
        XElement style, double value, IReadOnlyDictionary<string, XElement> formats, bool signed, int depth, out string text)
    {
        text = string.Empty;
        if (!double.IsFinite(value) || depth > MaxMapDepth)
            return false;

        // style:map picks another style for the values its condition holds for.
        // The style that maps is the last section of the code - the one for the
        // values no condition takes, which LibreOffice shows without a sign of
        // its own: "#,##0 €;-#,##0 €" writes the minus as text.
        bool mapped = false;
        foreach (XElement map in style.Elements(OdtNamespaces.Style + "map"))
        {
            if (!TryEvaluateCondition((string?)map.Attribute(OdtNamespaces.Style + "condition"), value, out bool holds))
                return false;
            mapped = true;
            if (!holds)
                continue;
            return (string?)map.Attribute(OdtNamespaces.Style + "apply-style-name") is string name &&
                formats.TryGetValue(name, out XElement? target) &&
                TryFormatNumber(target, value, formats, signed: true, depth + 1, out text);
        }

        if (mapped)
        {
            value = Math.Abs(value);
            signed = false;
        }

        string kind = style.Name.LocalName;
        if (style.Name.Namespace != OdtNamespaces.Number)
            return false;
        if (kind == "boolean-style")
            return TryFormatBoolean(style, value, out text);
        if (kind is not ("number-style" or "currency-style" or "percentage-style"))
            return false;
        if (LocaleOf(style) is not OdtLocale locale)
            return false;
        if (kind == "percentage-style")
            value *= 100;

        var builder = new StringBuilder();
        bool negative = false;
        bool hasNumber = false;
        foreach (XElement element in style.Elements())
        {
            if (element.Name == OdtNamespaces.Style + "text-properties" || element.Name == OdtNamespaces.Style + "map")
                continue;
            if (element.Name.Namespace != OdtNamespaces.Number)
                return false;
            switch (element.Name.LocalName)
            {
                case "text":
                    builder.Append(element.Value);
                    break;
                case "currency-symbol":
                    if (kind != "currency-style")
                        return false;
                    builder.Append(element.Value);
                    break;
                case "number":
                    if (hasNumber || !TryFormatFixed(element, value, locale, out string digits, out negative))
                        return false;
                    hasNumber = true;
                    builder.Append(digits);
                    break;
                default:
                    return false;
            }
        }

        if (!hasNumber)
            return false;
        text = (signed && negative ? "-" : string.Empty) + builder;
        return true;
    }

    /// <summary>
    /// A <c>number:number</c> with its stated decimals: rounded half away from
    /// zero, the integer digits padded to <c>number:min-integer-digits</c> and
    /// grouped in thousands with <c>number:grouping</c>, and trailing zeros
    /// dropped down to <c>number:min-decimal-places</c>. A number with no
    /// <c>number:decimal-places</c> is General, which this does not format.
    /// </summary>
    private static bool TryFormatFixed(XElement number, double value, OdtLocale locale, out string text, out bool negative)
    {
        text = string.Empty;
        negative = false;
        if (number.HasElements ||
            number.Attribute(OdtNamespaces.Number + "decimal-replacement") is not null ||
            (Attribute(number, "display-factor") is string factor && factor != "1") ||
            !int.TryParse(Attribute(number, "decimal-places"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int places) ||
            places is < 0 or > 15)
            return false;

        int minPlaces = int.TryParse(Attribute(number, "min-decimal-places"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min)
            ? Math.Clamp(min, 0, places) : places;
        int minInteger = int.TryParse(Attribute(number, "min-integer-digits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int digits)
            ? Math.Clamp(digits, 0, 30) : 1;
        bool grouping = Attribute(number, "grouping") == "true";

        // decimal holds the 15 significant digits a double shows, so 1.005
        // rounds up as it does in the formatter, and not down as its binary
        // value would.
        decimal rounded;
        try
        {
            rounded = Math.Round((decimal)Math.Abs(value), places, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return false;
        }

        negative = value < 0 && rounded != 0;
        string fixedText = rounded.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        int point = fixedText.IndexOf('.', StringComparison.Ordinal);
        string integer = point < 0 ? fixedText : fixedText[..point];
        string fraction = point < 0 ? string.Empty : fixedText[(point + 1)..];
        if (fraction.Length > minPlaces)
            fraction = fraction.TrimEnd('0').PadRight(minPlaces, '0');

        integer = integer.TrimStart('0');
        if (integer.Length < minInteger)
            integer = integer.PadLeft(minInteger, '0');
        if (grouping && integer.Length > 3)
        {
            var grouped = new StringBuilder();
            for (int i = 0; i < integer.Length; i++)
            {
                if (i > 0 && (integer.Length - i) % 3 == 0)
                    grouped.Append(locale.Group);
                grouped.Append(integer[i]);
            }
            integer = grouped.ToString();
        }

        text = fraction.Length == 0 ? integer : integer + locale.Decimal + fraction;
        return true;
    }

    private static bool TryFormatBoolean(XElement style, double value, out string text)
    {
        text = string.Empty;
        if (LocaleOf(style) is not OdtLocale { True: string yes, False: string no } ||
            style.Elements().Any(element => element.Name != OdtNamespaces.Number + "boolean" && element.Name != OdtNamespaces.Number + "text" &&
                element.Name != OdtNamespaces.Style + "text-properties"))
            return false;
        text = string.Concat(style.Elements().Select(element =>
            element.Name == OdtNamespaces.Number + "boolean" ? value != 0 ? yes : no :
            element.Name == OdtNamespaces.Number + "text" ? element.Value : string.Empty));
        return true;
    }

    /// <summary>
    /// A <c>style:condition</c> of the form LibreOffice writes for a section of a
    /// number format: <c>value()</c>, a comparison and a number.
    /// </summary>
    private static bool TryEvaluateCondition(string? condition, double value, out bool holds)
    {
        holds = false;
        if (condition is null)
            return false;
        string text = condition.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!text.StartsWith("value()", StringComparison.Ordinal))
            return false;
        text = text["value()".Length..];
        string[] operators = ["<=", ">=", "!=", "<>", "=", "<", ">"];
        string? op = operators.FirstOrDefault(candidate => text.StartsWith(candidate, StringComparison.Ordinal));
        if (op is null || !double.TryParse(text[op.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double operand))
            return false;
        holds = op switch
        {
            "<=" => value <= operand,
            ">=" => value >= operand,
            "!=" or "<>" => value != operand,
            "=" => value == operand,
            "<" => value < operand,
            _ => value > operand,
        };
        return true;
    }

    private static bool IsDatePart(string name) =>
        name is "text" or "day" or "month" or "year" or "day-of-week" or "hours" or "minutes" or "seconds" or "am-pm";

    /// <summary>
    /// The built-in date format whose elements are the style's, as LibreOffice
    /// matches them (<c>aDefaultDateFormats</c>): the day of the week, day,
    /// month and year each absent, short or long, and the month numeric or
    /// textual. The system rows are tried only for
    /// <c>number:format-source="language"</c>, the others only without it.
    /// </summary>
    private static OdtDateFormat? DefaultDateFormat(List<XElement> parts, bool system)
    {
        Element dow = Element.None, day = Element.None, month = Element.None, year = Element.None;
        foreach (XElement part in parts)
        {
            bool isLong = Attribute(part, "style") == "long";
            switch (part.Name.LocalName)
            {
                case "day-of-week": dow = isLong ? Element.Long : Element.Short; break;
                case "day": day = isLong ? Element.Long : Element.Short; break;
                case "year": year = isLong ? Element.Long : Element.Short; break;
                case "month":
                    month = Attribute(part, "textual") == "true"
                        ? isLong ? Element.TextLong : Element.TextShort
                        : isLong ? Element.Long : Element.Short;
                    break;
            }
        }

        foreach ((OdtDateFormat format, Element d, Element dd, Element m, Element y, bool fromSystem) in DefaultDateRows)
        {
            if (fromSystem == system && Matches(dow, d) && Matches(day, dd) && Matches(month, m) && Matches(year, y))
                return format;
        }

        return null;

        static bool Matches(Element stated, Element required) =>
            stated == required || (required == Element.Any && stated != Element.None);
    }

    private enum Element { None, Any, Short, Long, TextShort, TextLong }

    private static readonly (OdtDateFormat Format, Element DayOfWeek, Element Day, Element Month, Element Year, bool System)[] DefaultDateRows =
    [
        (OdtDateFormat.SystemShort, Element.None, Element.Any, Element.Any, Element.Any, true),
        (OdtDateFormat.SystemLong, Element.Any, Element.Any, Element.Any, Element.Any, true),
        (OdtDateFormat.MMYY, Element.None, Element.None, Element.Long, Element.Short, false),
        (OdtDateFormat.DDMMM, Element.None, Element.Long, Element.TextShort, Element.None, false),
        (OdtDateFormat.DDMMYY, Element.None, Element.Long, Element.Long, Element.Short, false),
        (OdtDateFormat.DDMMYYYY, Element.None, Element.Long, Element.Long, Element.Long, false),
        (OdtDateFormat.DMMMYY, Element.None, Element.Short, Element.TextShort, Element.Short, false),
        (OdtDateFormat.DMMMYYYY, Element.None, Element.Short, Element.TextShort, Element.Long, false),
        (OdtDateFormat.DMMMMYYYY, Element.None, Element.Short, Element.TextLong, Element.Long, false),
        (OdtDateFormat.NNDMMMYY, Element.Short, Element.Short, Element.TextShort, Element.Short, false),
        (OdtDateFormat.NNDMMMMYYYY, Element.Short, Element.Short, Element.TextLong, Element.Long, false),
        (OdtDateFormat.NNNNDMMMMYYYY, Element.Long, Element.Short, Element.TextLong, Element.Long, false),
    ];

    /// <summary>
    /// Formats a date through a format code of the formatter's date keywords:
    /// <c>D</c>, <c>DD</c>, <c>M</c>, <c>MM</c>, <c>MMM</c>, <c>MMMM</c>,
    /// <c>YY</c>, <c>YYYY</c>, <c>NN</c> and <c>NNNN</c>. Anything else is
    /// copied.
    /// </summary>
    internal static bool TryFormatDateCode(string code, DateTime value, OdtLocale locale, out string text)
    {
        var builder = new StringBuilder();
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            int run = 1;
            while (i + run < code.Length && code[i + run] == c)
                run++;
            switch (c)
            {
                case 'D':
                    builder.Append(Digits(value.Day, Math.Min(run, 2)));
                    break;
                case 'M':
                    builder.Append(run switch
                    {
                        1 => Digits(value.Month, 1),
                        2 => Digits(value.Month, 2),
                        3 => locale.MonthsShort[value.Month - 1],
                        _ => locale.MonthsLong[value.Month - 1],
                    });
                    break;
                case 'Y':
                    builder.Append(run <= 2 ? Digits(value.Year % 100, 2) : Digits(value.Year, 4));
                    break;
                case 'N':
                    builder.Append(run <= 3 ? locale.DaysShort[(int)value.DayOfWeek] : locale.DaysLong[(int)value.DayOfWeek]);
                    break;
                default:
                    builder.Append(c, run);
                    break;
            }
            i += run;
        }

        text = builder.ToString();
        return true;
    }

    private static string Digits(int value, int width) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    private static string? Attribute(XElement element, string name) =>
        (string?)element.Attribute(OdtNamespaces.Number + name);

    /// <summary>
    /// The locale a style formats in: its own <c>number:language</c> and
    /// <c>number:country</c>, or the system's when it states no language. Null
    /// when this table does not hold it.
    /// </summary>
    internal static OdtLocale? LocaleOf(XElement style)
    {
        string? language = Attribute(style, "language");
        string? country = Attribute(style, "country");
        if (string.IsNullOrEmpty(language) || language == "none")
            return OdtLocale.SystemLocale;
        return OdtLocale.Find(language, country);
    }
}

/// <summary>The built-in date formats an automatically ordered date style stands for.</summary>
internal enum OdtDateFormat
{
    SystemShort,
    SystemLong,
    MMYY,
    DDMMM,
    DDMMYY,
    DDMMYYYY,
    DMMMYY,
    DMMMYYYY,
    DMMMMYYYY,
    NNDMMMYY,
    NNDMMMMYYYY,
    NNNNDMMMMYYYY,
}

/// <summary>
/// The parts of LibreOffice 24.2's locale data (i18npool) that data styles
/// format with: separators, month and day names and the built-in date formats.
/// Each entry was read back from soffice, one format at a time.
/// </summary>
internal sealed record OdtLocale(
    string Name,
    string Decimal,
    string Group,
    string[] MonthsShort,
    string[] MonthsLong,
    string[] DaysShort,
    string[] DaysLong,
    IReadOnlyDictionary<OdtDateFormat, string> DateFormats,
    string? Am = null,
    string? Pm = null,
    string? True = null,
    string? False = null)
{
    private static readonly string[] EnglishMonthsShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly string[] EnglishMonthsLong = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] EnglishDaysShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] EnglishDaysLong = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] GermanMonthsShort = ["Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez"];
    private static readonly string[] GermanMonthsLong = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];
    private static readonly string[] GermanDaysShort = ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"];
    private static readonly string[] GermanDaysLong = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"];
    private static readonly string[] FrenchMonthsShort = ["janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc."];
    private static readonly string[] FrenchMonthsLong = ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];
    private static readonly string[] FrenchDaysShort = ["dim.", "lun.", "mar.", "mer.", "jeu.", "ven.", "sam."];
    private static readonly string[] FrenchDaysLong = ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"];

    private static Dictionary<OdtDateFormat, string> Formats(
        string mmyy, string ddmmm, string ddmmyy, string ddmmyyyy, string dmmmyy, string dmmmyyyy, string dmmmmyyyy,
        string nndmmmyy, string nndmmmmyyyy, string nnnndmmmmyyyy, string systemShort, string systemLong) => new()
        {
            [OdtDateFormat.MMYY] = mmyy,
            [OdtDateFormat.DDMMM] = ddmmm,
            [OdtDateFormat.DDMMYY] = ddmmyy,
            [OdtDateFormat.DDMMYYYY] = ddmmyyyy,
            [OdtDateFormat.DMMMYY] = dmmmyy,
            [OdtDateFormat.DMMMYYYY] = dmmmyyyy,
            [OdtDateFormat.DMMMMYYYY] = dmmmmyyyy,
            [OdtDateFormat.NNDMMMYY] = nndmmmyy,
            [OdtDateFormat.NNDMMMMYYYY] = nndmmmmyyyy,
            [OdtDateFormat.NNNNDMMMMYYYY] = nnnndmmmmyyyy,
            [OdtDateFormat.SystemShort] = systemShort,
            [OdtDateFormat.SystemLong] = systemLong,
        };

    private static readonly OdtLocale EnglishUs = new("en-US", ".", ",",
        EnglishMonthsShort, EnglishMonthsLong, EnglishDaysShort, EnglishDaysLong,
        Formats("MM/YY", "MMM DD", "MM/DD/YY", "MM/DD/YYYY", "MMM D, YY", "MMM D, YYYY", "MMMM D, YYYY",
            "NN, MMM D, YY", "NN, MMMM D, YYYY", "NNNN, MMMM D, YYYY", "M/D/YY", "NNNN, MMMM D, YYYY"),
        "AM", "PM", "TRUE", "FALSE");

    private static readonly OdtLocale EnglishGb = new("en-GB", ".", ",",
        EnglishMonthsShort, EnglishMonthsLong, EnglishDaysShort, EnglishDaysLong,
        Formats("MM/YY", "DD/MMM", "DD/MM/YY", "DD/MM/YYYY", "D MMM YY", "D MMM YYYY", "D MMMM YYYY",
            "NN D MMM YY", "NN D MMMM YYYY", "NNNN D MMMM YYYY", "DD/MM/YY", "NNNN D MMMM YYYY"),
        "AM", "PM", "TRUE", "FALSE");

    private static readonly Dictionary<OdtDateFormat, string> GermanFormats =
        Formats("MM.YY", "DD.MMM", "DD.MM.YY", "DD.MM.YYYY", "D. MMM YY", "D. MMM YYYY", "D. MMMM YYYY",
            "NN, D. MMM YY", "NN, D. MMMM YYYY", "NNNN, D. MMMM YYYY", "DD.MM.YY", "NNNN, D. MMMM YYYY");

    private static readonly OdtLocale GermanDe = new("de-DE", ",", ".",
        GermanMonthsShort, GermanMonthsLong, GermanDaysShort, GermanDaysLong, GermanFormats);

    private static readonly OdtLocale GermanAt = new("de-AT", ",", ".",
        ["Jän", .. GermanMonthsShort[1..]], ["Jänner", .. GermanMonthsLong[1..]], GermanDaysShort, GermanDaysLong,
        new Dictionary<OdtDateFormat, string>(GermanFormats)
        {
            [OdtDateFormat.SystemShort] = "DD.MM.YYYY",
            [OdtDateFormat.SystemLong] = "NNNN, DD. MMMM YYYY",
        });

    private static readonly Dictionary<OdtDateFormat, string> FrenchFormats =
        Formats("MM/YY", "DD/MMM", "DD/MM/YY", "DD/MM/YYYY", "D MMM YY", "D MMM YYYY", "D MMMM YYYY",
            "NN D MMM YY", "NN D MMMM YYYY", "NNNN D MMMM YYYY", "DD/MM/YYYY", "NNNN D MMMM YYYY");

    private static readonly OdtLocale FrenchFr = new("fr-FR", ",", " ",
        FrenchMonthsShort, FrenchMonthsLong, FrenchDaysShort, FrenchDaysLong, FrenchFormats);

    private static readonly OdtLocale FrenchLu = FrenchFr with { Name = "fr-LU" };

    /// <summary>
    /// Hungarian numbers. Its names and date formats were not read back, so a
    /// date style in it keeps the text the producer cached.
    /// </summary>
    private static readonly OdtLocale HungarianHu = new("hu-HU", ",", " ", [], [], [], [], new Dictionary<OdtDateFormat, string>());

    private static readonly Dictionary<string, OdtLocale> Locales = new[]
    {
        EnglishUs, EnglishGb, GermanDe, GermanAt, FrenchFr, FrenchLu, HungarianHu,
    }.ToDictionary(locale => locale.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The country a language means when a style states none.</summary>
    private static readonly Dictionary<string, string> DefaultCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "US",
        ["de"] = "DE",
        ["fr"] = "FR",
        ["hu"] = "HU",
    };

    /// <summary>The locale for a language and country, or null when the table does not hold it.</summary>
    public static OdtLocale? Find(string language, string? country)
    {
        if (string.IsNullOrEmpty(country) && !DefaultCountry.TryGetValue(language, out country))
            return null;
        return Locales.GetValueOrDefault(language + "-" + country);
    }

    /// <summary>
    /// The system locale a style without a language formats in: the
    /// application's culture, or en-US where that is the invariant culture.
    /// </summary>
    public static OdtLocale? SystemLocale
    {
        get
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            if (string.IsNullOrEmpty(culture.Name))
                return EnglishUs;
            string[] parts = culture.Name.Split('-');
            return parts.Length >= 2 ? Find(parts[0], parts[^1]) : Find(parts[0], null);
        }
    }
}
