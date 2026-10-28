using System;
using System.Text;
using Broiler.Documents.Text;

namespace Broiler.Documents.Csv;

/// <summary>
/// CSV-specific read options.
/// </summary>
/// <remarks>
/// <para>
/// CSV has no header that names its dialect, so the two things a reader cannot
/// know from the bytes alone are settable here. Everything else - limits - is
/// shared and stays on <see cref="DocumentLimits"/>.
/// </para>
/// <para>
/// Passing the plain <see cref="DocumentReadOptions"/> is fine and reads with
/// these defaults; passing another codec's option type is a structured
/// rejection.
/// </para>
/// </remarks>
public sealed class CsvReadOptions : DocumentReadOptions
{
    public static new CsvReadOptions Default { get; } = new();

    /// <param name="limits">The shared read limits.</param>
    /// <param name="delimiter">
    /// The field separator, or null to detect it from the first records: comma,
    /// semicolon or tab. A quote, a carriage return or a line feed cannot
    /// separate fields and is refused.
    /// </param>
    /// <param name="hasHeaderRow">
    /// True when the first record names the columns, which RFC 4180 lets a
    /// sender state with the <c>header=present</c> media-type parameter and the
    /// bytes themselves never do. The first row is then marked as a header
    /// row, which repeats at the top of each page.
    /// </param>
    public CsvReadOptions(DocumentLimits? limits = null, char? delimiter = null, bool hasHeaderRow = false)
        : base(limits)
    {
        if (delimiter is char value)
            CsvDialect.ThrowIfInvalidDelimiter(value, nameof(delimiter));

        Delimiter = delimiter;
        HasHeaderRow = hasHeaderRow;
    }

    /// <summary>The field separator, or null when the reader detects it.</summary>
    public char? Delimiter { get; }

    /// <summary>True when the first record is read as the table's header row.</summary>
    public bool HasHeaderRow { get; }

    internal static CsvReadOptions From(DocumentReadOptions? options) => options switch
    {
        null => Default,
        CsvReadOptions typed => typed,
        _ => new CsvReadOptions(options.Limits),
    };
}

/// <summary>
/// CSV-specific write options.
/// </summary>
/// <remarks>
/// The defaults are RFC 4180's: comma-separated, CRLF line breaks, UTF-8 with
/// no byte-order mark. The other values exist for the consumers that need them:
/// a spreadsheet in a locale whose decimal separator is a comma expects
/// semicolons, and one that guesses the encoding of a file without a
/// byte-order mark guesses a legacy code page.
/// </remarks>
public sealed class CsvWriteOptions : DocumentWriteOptions
{
    public static new CsvWriteOptions Default { get; } = new();

    /// <param name="delimiter">The field separator. A quote, carriage return or line feed is refused.</param>
    /// <param name="byteOrderMark">True to start the output with a UTF-8 byte-order mark.</param>
    /// <param name="escapeFormulas">
    /// True to write a leading apostrophe before any field that starts with
    /// <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return, so a
    /// spreadsheet opening the file shows the text instead of running it as a
    /// formula. It changes the data - <c>-5</c> is written as <c>'-5</c> - so it
    /// is off unless asked for.
    /// </param>
    /// <param name="metadata">
    /// Document properties. CSV has nowhere to put any, so whatever is stated
    /// here is reported as dropped.
    /// </param>
    public CsvWriteOptions(
        char delimiter = ',',
        bool byteOrderMark = false,
        bool escapeFormulas = false,
        DocumentMetadata? metadata = null)
        : base(metadata: metadata)
    {
        CsvDialect.ThrowIfInvalidDelimiter(delimiter, nameof(delimiter));

        Delimiter = delimiter;
        ByteOrderMark = byteOrderMark;
        EscapeFormulas = escapeFormulas;
    }

    /// <summary>The field separator.</summary>
    public char Delimiter { get; }

    /// <summary>True when the output starts with a UTF-8 byte-order mark.</summary>
    public bool ByteOrderMark { get; }

    /// <summary>True when fields a spreadsheet would run as formulas are written with a leading apostrophe.</summary>
    public bool EscapeFormulas { get; }

    internal static CsvWriteOptions From(DocumentWriteOptions? options) => options switch
    {
        null => Default,
        CsvWriteOptions typed => typed,
        _ => new CsvWriteOptions(metadata: options.Metadata),
    };
}

/// <summary>The rules both directions share about what a CSV dialect may be.</summary>
internal static class CsvDialect
{
    /// <summary>The separators the reader tries, in the order a tie is settled.</summary>
    /// <remarks>
    /// A tie goes to the rarer character. Commas turn up inside values on their
    /// own - prose, and every number in a locale with a decimal comma - so a
    /// file whose every record splits evenly on a tab or a semicolon is far more
    /// likely to be separated by it than one that splits evenly on a comma.
    /// </remarks>
    public static readonly char[] Candidates = ['\t', ';', ','];

    public static void ThrowIfInvalidDelimiter(char delimiter, string parameterName)
    {
        if (delimiter is '"' or '\r' or '\n')
        {
            throw new ArgumentException(
                "A quote, carriage return or line feed cannot separate fields.", parameterName);
        }
    }
}
