using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Broiler.Documents.Cli.Composition;
using Broiler.Documents.Cli.Infrastructure;
using Broiler.Documents.Csv;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;

namespace Broiler.Documents.Cli.Documents;

/// <summary>
/// Preset configuration for CSV/TSV dialects.
/// </summary>
public readonly record struct CsvDialectPreset(
    string Name,
    char Delimiter,
    bool ByteOrderMark,
    bool EscapeFormulas)
{
    public static CsvDialectPreset Default { get; } = new("rfc4180", ',', false, false);
    public static CsvDialectPreset Rfc4180 { get; } = new("rfc4180", ',', false, false);
    public static CsvDialectPreset Excel { get; } = new("excel", ',', true, true);
    public static CsvDialectPreset Unix { get; } = new("unix", ',', false, false);
    public static CsvDialectPreset Tsv { get; } = new("tsv", '\t', false, false);
    public static CsvDialectPreset Semicolon { get; } = new("semicolon", ';', false, false);
    public static CsvDialectPreset Pipe { get; } = new("pipe", '|', false, false);
}

/// <summary>
/// Builds the codec read and write options from the shared options every command
/// that touches a document accepts.
/// </summary>
/// <remarks>
/// The limits are exposed rather than hidden because they are a deliberate part
/// of the contract: the defaults refuse a 64 MB document and a million
/// paragraphs, and a harness feeding a deliberately hostile corpus needs to be
/// able to say so on the command line instead of rebuilding the tool.
/// </remarks>
public static class DocumentOptions
{
    /// <summary>The options every command that reads or writes a document shares.</summary>
    public static OptionSpec[] Specs { get; } =
    [
        OptionSpec.Value("from", "format", "Read as this format instead of probing the content."),
        OptionSpec.Value("csv-read-delimiter", "separator", "CSV input separator: one character, comma, semicolon, or tab. Omit to detect."),
        OptionSpec.Flag("csv-header", "Treat the first CSV input record as a header row."),
        OptionSpec.Value("max-bytes","n","Largest document accepted, in bytes.",DocumentLimits.DefaultMaxDocumentBytes.ToString()),
        OptionSpec.Value("max-paragraphs","n","Largest paragraph count accepted.",DocumentLimits.DefaultMaxParagraphCount.ToString()),
        OptionSpec.Value("code-page","n","Fallback code page for RTF documents that declare none.",DocumentReadOptions.Windows1252CodePage.ToString()),
        OptionSpec.Value("fail-on","severity","Exit " + ExitCode.Diagnostics + " when a diagnostic reaches this severity: info, warning, error, or never.","never"),
        OptionSpec.Value("password-file","path","Open an encrypted PDF with the password on this file's first line, tried as the user and the owner password. A file rather than an argument, so the password never reaches a process list or a shell history."),
    ];

    /// <summary>The write-side options, added on top of <see cref="Specs"/> by commands that write.</summary>
    public static OptionSpec[] WriteSpecs { get; } =
    [
        OptionSpec.Value("to", "format", "Write this format instead of inferring it from the output extension."),
        OptionSpec.Flag("raw-unicode", "Ask writers to emit non-ASCII characters directly rather than escaping them."),
        OptionSpec.Value("csv-write-delimiter", "separator", "CSV output separator: one character, comma, semicolon, or tab.", "comma"),
        OptionSpec.Flag("csv-bom", "Start CSV output with a UTF-8 byte-order mark."),
        OptionSpec.Flag("csv-escape-formulas", "Prefix formula-like CSV output fields with an apostrophe; changes field values."),
    ];

    /// <summary>Options for sheet and dialect routing in commands that convert or roundtrip.</summary>
    public static OptionSpec[] RoutingSpecs { get; } =
    [
        OptionSpec.Value("sheet", "name|index", "Sheet name or 0-based index to route/convert."),
        OptionSpec.Value("table", "index", "0-based index of table to route/convert."),
        OptionSpec.Value("dialect", "name", "CSV dialect preset: rfc4180, excel, unix, tsv, semicolon, or pipe."),
        OptionSpec.Value("csv-dialect", "name", "Synonym for --dialect."),
        OptionSpec.Value("delimiter", "sep", "CSV field delimiter: comma, semicolon, tab, pipe, or one character."),
    ];

    /// <summary>Composes per-command CSV settings without passing them to other codecs.</summary>
    public static DocumentCodecCatalog CreateCatalogFrom(CommandLine line) =>
        CodecComposition.CreateCatalog(PdfPasswordFrom(line),
            CsvReadOptionsFrom(line),
            CsvWriteOptionsFrom(line));

    /// <summary>CSV input settings; omitted flags retain the codec defaults or autodetect.</summary>
    public static CsvReadOptions CsvReadOptionsFrom(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        char? delimiter = null;
        if (line.Has("csv-read-delimiter"))
        {
            delimiter = CsvDelimiterFrom(line, "csv-read-delimiter");
        }
        else
        {
            string? fromFormat = line.Get("from");
            if (string.Equals(fromFormat, "tsv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fromFormat, ".tsv", StringComparison.OrdinalIgnoreCase))
            {
                delimiter = '\t';
            }
            else if (line.Positionals.Count > 0 && line.Positionals[0].EndsWith(".tsv", StringComparison.OrdinalIgnoreCase))
            {
                delimiter = '\t';
            }
        }

        bool hasHeader = line.Has("csv-header");
        return new CsvReadOptions(delimiter: delimiter, hasHeaderRow: hasHeader);
    }

    /// <summary>CSV output settings; omitted flags retain the codec defaults.</summary>
    public static CsvWriteOptions CsvWriteOptionsFrom(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        CsvDialectPreset preset = (line.Has("dialect") || line.Has("csv-dialect"))
            ? ResolveDialect((line.Get("dialect") ?? line.Get("csv-dialect"))!)
            : CsvDialectPreset.Default;

        string? toFormat = line.Get("to");
        string? outPath = line.Get("out");
        bool targetIsTsv = string.Equals(toFormat, "tsv", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(toFormat, ".tsv", StringComparison.OrdinalIgnoreCase) ||
                           (outPath != null && outPath.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase));

        char delimiter = preset.Delimiter;
        if (targetIsTsv && !line.Has("dialect") && !line.Has("csv-dialect"))
            delimiter = '\t';

        if (line.Has("csv-write-delimiter"))
        {
            delimiter = CsvDelimiterFrom(line, "csv-write-delimiter") ?? ',';
        }
        else if (line.Has("delimiter"))
        {
            delimiter = CsvDelimiterFrom(line, "delimiter") ?? ',';
        }

        bool bom = line.Has("csv-bom") || preset.ByteOrderMark;
        bool escapeFormulas = line.Has("csv-escape-formulas") || preset.EscapeFormulas;

        return new CsvWriteOptions(delimiter: delimiter, byteOrderMark: bom, escapeFormulas: escapeFormulas);
    }

    public static CsvDialectPreset ResolveDialect(string dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        return dialect.Trim().ToLowerInvariant() switch
        {
            "rfc4180" or "default" or "csv" => CsvDialectPreset.Rfc4180,
            "excel" => CsvDialectPreset.Excel,
            "unix" => CsvDialectPreset.Unix,
            "tsv" or "tab" or "excel-tab" => CsvDialectPreset.Tsv,
            "semicolon" or "european" or "posix" => CsvDialectPreset.Semicolon,
            "pipe" => CsvDialectPreset.Pipe,
            _ => throw new UsageException($"Unknown dialect \"{dialect}\". Supported dialects: rfc4180, excel, unix, tsv, semicolon, pipe.")
        };
    }

    internal static char? CsvDelimiterFrom(CommandLine line, string name)
    {
        string? value = line.Get(name);
        if (value is null)
            return null;
        return ParseDelimiter(value, name);
    }

    internal static char ParseDelimiter(string value, string optionName = "delimiter")
    {
        string mapped = value.ToLowerInvariant() switch
        {
            "comma" => ",",
            "semicolon" => ";",
            "tab" => "\t",
            "pipe" => "|",
            "colon" => ":",
            _ => value,
        };
        if (mapped.Length != 1 || mapped[0] is '"' or '\r' or '\n')
            throw new UsageException("--" + optionName + " expects one character other than a quote or line break, or comma, semicolon, tab, pipe.");
        return mapped[0];
    }

    /// <summary>
    /// Resolves the selected table index from <c>--sheet</c> or <c>--table</c>, or returns -1 if neither was given.
    /// </summary>
    public static int ResolveSheetIndex(RichTextDocument document, CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(line);

        if (line.Has("sheet") && line.Has("table"))
            throw new UsageException("--sheet and --table are alternatives; give one.");

        if (line.Has("table"))
        {
            string tableVal = line.Require("table");
            if (!int.TryParse(tableVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                throw new UsageException("The --table option requires an integer index.");

            if (document.Tables.Count == 0)
                throw new UsageException("Document contains no tables or sheets.");

            if (index < 0 || index >= document.Tables.Count)
                throw new UsageException($"Table index {index} is out of range. Document contains {document.Tables.Count} table(s).");

            return index;
        }

        if (line.Has("sheet"))
        {
            string sheetVal = line.Require("sheet").Trim();

            if (document.Tables.Count == 0)
                throw new UsageException("Document contains no tables or sheets.");

            // 1. Direct integer (0-based index)
            if (int.TryParse(sheetVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                if (index < 0 || index >= document.Tables.Count)
                    throw new UsageException($"Table index {index} is out of range. Document contains {document.Tables.Count} table(s).");
                return index;
            }

            // 2. "SheetN" or "Sheet N" or "TableN" or "Table N" (1-based sheet numbering convention)
            string normalized = sheetVal;
            if (normalized.StartsWith("sheet", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..].Trim();
            else if (normalized.StartsWith("table", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[5..].Trim();

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sheetNum) && sheetNum >= 1)
            {
                int targetIndex = sheetNum - 1;
                if (targetIndex >= document.Tables.Count)
                    throw new UsageException($"Sheet '{sheetVal}' (index {targetIndex}) is out of range. Document contains {document.Tables.Count} table(s).");
                return targetIndex;
            }

            // 3. Name matching: check table Name, or heading immediately preceding table, or first cell text
            for (int i = 0; i < document.Tables.Count; i++)
            {
                DocumentTable t = document.Tables[i];
                if (!string.IsNullOrEmpty(t.Name) && string.Equals(t.Name, sheetVal, StringComparison.OrdinalIgnoreCase))
                    return i;

                if (t.ParagraphIndex > 0)
                {
                    string heading = document.Paragraphs[t.ParagraphIndex - 1].Text.Trim();
                    if (string.Equals(heading, sheetVal, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                if (t.Rows.Count > 0 && t.Rows[0].Cells.Count > 0)
                {
                    TableCell cell = t.Rows[0].Cells[0];
                    if (cell.ParagraphCount > 0 && cell.ParagraphIndex < document.ParagraphCount)
                    {
                        string cellText = document.Paragraphs[cell.ParagraphIndex].Text.Trim();
                        if (string.Equals(cellText, sheetVal, StringComparison.OrdinalIgnoreCase))
                            return i;
                    }
                }
            }

            throw new UsageException($"Sheet '{sheetVal}' not found. Document contains {document.Tables.Count} table(s).");
        }

        return -1;
    }

    /// <summary>
    /// Isolates a single table / sheet from <paramref name="document"/> into a standalone document
    /// whose paragraphs and table start at index 0.
    /// </summary>
    public static RichTextDocument ExtractSheet(RichTextDocument document, int tableIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Tables.Count == 0)
            throw new UsageException("Document contains no tables or sheets.");

        if (tableIndex < 0 || tableIndex >= document.Tables.Count)
            throw new UsageException($"Table index {tableIndex} is out of range. Document contains {document.Tables.Count} table(s).");

        DocumentTable table = document.Tables[tableIndex];
        int start = table.ParagraphIndex;
        int count = table.ParagraphCount;

        var paragraphs = document.Paragraphs.Skip(start).Take(count).ToList();
        if (paragraphs.Count == 0)
            paragraphs.Add(RichTextParagraph.Empty);

        DocumentTable shiftedTable = table.ShiftedBy(-start);

        return RichTextDocument.FromParagraphs(paragraphs)
            .WithTables([shiftedTable])
            .WithPageGeometry(document.PageGeometry)
            .WithStyleDefaults(document.StyleDefaults)
            .WithPageBackground(document.PageBackground)
            .WithShowsRevisions(document.ShowsRevisions);
    }

    public static DocumentReadOptions ReadOptionsFrom(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var limits = new DocumentLimits(
            maxDocumentBytes: PositiveLong(line, "max-bytes", DocumentLimits.DefaultMaxDocumentBytes),
            maxParagraphCount: PositiveInt(line, "max-paragraphs", DocumentLimits.DefaultMaxParagraphCount));

        int codePage = PositiveInt(line, "code-page", DocumentReadOptions.Windows1252CodePage);
        return new DocumentReadOptions(limits, codePage, DocumentResourcePolicy.AllowOwnDocuments);
    }

    /// <summary>
    /// The password on the file <c>--password-file</c> names, or null when the
    /// run was given none. The composition root hands it to the PDF codec.
    /// </summary>
    public static string? PdfPasswordFrom(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string? path = line.Get("password-file");
        if (string.IsNullOrEmpty(path))
            return null;

        string password;
        try
        {
            using var reader = new StreamReader(path);
            password = reader.ReadLine() ?? string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new UsageException("--password-file could not be read: " + e.Message);
        }

        return password;
    }

    /// <summary>
    /// The write options for a run, carrying the decisions the read made about
    /// the document's resources.
    /// </summary>
    public static DocumentWriteOptions WriteOptionsFrom(CommandLine line, DocumentConversionContext? resources = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new DocumentWriteOptions(asciiOnly: !line.Has("raw-unicode"), resources: resources);
    }

    /// <summary>
    /// The severity at which <c>--fail-on</c> turns a completed run into a
    /// non-zero exit, or null when the caller did not ask for one.
    /// </summary>
    public static DocumentDiagnosticSeverity? FailOnFrom(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string value = line.Get("fail-on", "never")!;
        return value.ToLowerInvariant() switch
        {
            "never" or "none" or "" => null,
            "info" => DocumentDiagnosticSeverity.Info,
            "warning" or "warn" => DocumentDiagnosticSeverity.Warning,
            "error" => DocumentDiagnosticSeverity.Error,
            _ => throw new UsageException(
                "--fail-on expects never, info, warning, or error, not \"" + value + "\"."),
        };
    }

    private static int PositiveInt(CommandLine line, string name, int fallback)
    {
        int value = line.GetInt32(name, fallback);
        if (value <= 0)
            throw new UsageException("--" + name + " must be greater than zero.");
        return value;
    }

    private static long PositiveLong(CommandLine line, string name, long fallback)
    {
        double value = line.GetDouble(name, fallback);
        if (value <= 0 || value > long.MaxValue)
            throw new UsageException("--" + name + " must be greater than zero.");
        return (long)value;
    }
}
