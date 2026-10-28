using System;

namespace Broiler.Documents.Csv;

/// <summary>
/// Handles the spreadsheet vendor convention of declaring the delimiter on the
/// first line of a CSV file using <c>sep=&lt;char&gt;</c>.
/// </summary>
internal static class CsvSepDirective
{
    /// <summary>
    /// Checks whether <paramref name="text"/> begins with a delimiter directive (<c>sep=&lt;char&gt;</c>)
    /// on its first line, and extracts the delimiter and remaining text if present.
    /// </summary>
    public static bool TryExtract(string text, out char delimiter, out string remainingText)
    {
        delimiter = '\0';
        remainingText = text;

        if (text.Length < 5 || !text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase))
            return false;

        char sep = text[4];
        if (sep is '"' or '\r' or '\n')
            return false;

        int newlineLength;
        if (text.Length == 5)
        {
            newlineLength = 0;
        }
        else if (text[5] == '\r')
        {
            newlineLength = (text.Length > 6 && text[6] == '\n') ? 2 : 1;
        }
        else if (text[5] == '\n')
        {
            newlineLength = 1;
        }
        else
        {
            return false;
        }

        delimiter = sep;
        remainingText = text[(5 + newlineLength)..];
        return true;
    }
}
