using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broiler.Documents.Ods;

internal static partial class OdsNumberStyles
{
    private static readonly XNamespace Number = OdsNamespaces.Number;

    [GeneratedRegex(@"^(?<currency>[$€£¥])?(?<integer>0|#,##0)(?:\.(?<decimals>0{1,30}))?(?<percent>%)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormatPattern();

    public static readonly DateTime DefaultDateBase = new(1899, 12, 30);
    public const string IsoDate = "yyyy-mm-dd";

    public static XElement? Write(string format)
    {
        if (DateParts(format) is { } date)
            return new XElement(Number + "date-style",
                DatePart(date.First), new XElement(Number + "text", date.Separator),
                DatePart(date.Second), new XElement(Number + "text", date.Separator), DatePart(date.Third));

        Match match = FormatPattern().Match(format);
        if (!match.Success)
            return null;

        string currency = match.Groups["currency"].Value;
        bool percent = match.Groups["percent"].Success;
        if (percent && currency.Length > 0)
            return null;

        var style = new XElement(Number + (percent ? "percentage-style" :
            currency.Length > 0 ? "currency-style" : "number-style"));
        if (currency.Length > 0)
            style.Add(new XElement(Number + "currency-symbol", currency));
        style.Add(new XElement(Number + "number",
            new XAttribute(Number + "min-integer-digits", 1),
            new XAttribute(Number + "decimal-places", match.Groups["decimals"].Length),
            new XAttribute(Number + "grouping", match.Groups["integer"].Value == "#,##0" ? "true" : "false")));
        if (percent)
            style.Add(new XElement(Number + "text", "%"));
        return style;
    }

    public static string? Read(XElement style)
    {
        if (style.Name == Number + "date-style")
        {
            XElement[] parts = style.Elements().ToArray();
            foreach (string dateCode in DateCodes)
            {
                var date = DateParts(dateCode)!.Value;
                if (parts.Length == 5 && IsDatePart(parts[0], date.First) &&
                    IsSeparator(parts[1], date.Separator) && IsDatePart(parts[2], date.Second) &&
                    IsSeparator(parts[3], date.Separator) && IsDatePart(parts[4], date.Third))
                    return dateCode;
            }
            return null;
        }

        XElement? number = style.Element(Number + "number");
        if (number is null ||
            !int.TryParse((string?)number.Attribute(Number + "decimal-places") ?? "0",
                NumberStyles.None, CultureInfo.InvariantCulture, out int decimals) ||
            decimals > 30 ||
            ((string?)number.Attribute(Number + "min-integer-digits") ?? "1") != "1")
            return null;

        string code = (string?)number.Attribute(Number + "grouping") == "true" ? "#,##0" : "0";
        if (decimals > 0)
            code += "." + new string('0', decimals);

        if (style.Name.Namespace != Number || number.HasElements)
            return null;
        XElement? first = style.Elements().FirstOrDefault();
        return style.Name.LocalName switch
        {
            "number-style" when first == number && number.ElementsAfterSelf().FirstOrDefault() is null => code,
            "percentage-style" when first == number && number.ElementsAfterSelf().FirstOrDefault() is XElement suffix &&
                suffix.Name == Number + "text" && suffix.Value == "%" && suffix.ElementsAfterSelf().FirstOrDefault() is null => code + "%",
            "currency-style" when first?.Name == Number + "currency-symbol" &&
                first.Value.Length == 1 && "$€£¥".Contains(first.Value, StringComparison.Ordinal) &&
                first.ElementsAfterSelf().FirstOrDefault() == number && number.ElementsAfterSelf().FirstOrDefault() is null => first.Value + code,
            _ => null,
        };
    }

    private static readonly string[] DateCodes = [IsoDate, "dd.mm.yyyy", "mm/dd/yyyy"];

    public static bool IsDate(string? code) => DateParts(code).HasValue;

    private static (string First, string Second, string Third, string Separator)? DateParts(string? code) =>
        code?.ToLowerInvariant() switch
        {
            IsoDate => ("year", "month", "day", "-"),
            "dd.mm.yyyy" => ("day", "month", "year", "."),
            "mm/dd/yyyy" => ("month", "day", "year", "/"),
            _ => null,
        };

    private static XElement DatePart(string name) =>
        new(Number + name, new XAttribute(Number + "style", "long"));

    private static bool IsDatePart(XElement part, string name) =>
        part.Name == Number + name && (string?)part.Attribute(Number + "style") == "long" &&
        part.Attributes().Count() == 1 && part.IsEmpty;

    private static bool IsSeparator(XElement part, string separator) => part.Name == Number + "text" && part.Value == separator;
}
