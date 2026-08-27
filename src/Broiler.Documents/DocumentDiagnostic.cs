using System;
using System.Globalization;

namespace Broiler.Documents;

/// <summary>
/// Where in a source a diagnostic applies, in whatever terms the format has.
/// </summary>
/// <remarks>
/// Formats disagree about what a location even is — RTF has a byte offset, DOCX
/// has a part name, PDF has an object number and a page. Rather than inventing a
/// single coordinate none of them uses, this carries the few that generalize and
/// leaves the rest to the format's own diagnostic text. Every field is optional;
/// a diagnostic with no useful location simply has none.
/// </remarks>
public sealed class DocumentDiagnosticLocation
{
    public DocumentDiagnosticLocation(
        long? byteOffset = null,
        int? paragraphIndex = null,
        int? pageNumber = null,
        string? part = null)
    {
        if (byteOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        if (paragraphIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        ByteOffset = byteOffset;
        ParagraphIndex = paragraphIndex;
        PageNumber = pageNumber;
        Part = part;
    }

    /// <summary>Offset into the source bytes.</summary>
    public long? ByteOffset { get; }

    /// <summary>Index of the produced paragraph, for a projection-time problem.</summary>
    public int? ParagraphIndex { get; }

    /// <summary>One-based page number, for a paginated format.</summary>
    public int? PageNumber { get; }

    /// <summary>
    /// A named component of a container format — a DOCX part, a PDF object. It
    /// names a structure, never a filesystem path, so it stays safe to log.
    /// </summary>
    public string? Part { get; }

    public override string ToString()
    {
        var parts = new System.Collections.Generic.List<string>(4);
        if (Part is not null)
            parts.Add(Part);
        if (PageNumber is { } page)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"page {page}"));
        if (ParagraphIndex is { } paragraph)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"paragraph {paragraph}"));
        if (ByteOffset is { } offset)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"offset {offset}"));
        return parts.Count == 0 ? "unknown location" : string.Join(", ", parts);
    }
}

/// <summary>
/// A single note produced while reading or writing a document — an unsupported
/// construct that was skipped, a limit that clamped, or a recovered error.
/// Diagnostics carry a stable <see cref="Code"/> and a human-readable
/// <see cref="Message"/>, but never the document text or any payload (ADR 0004
/// privacy rule).
/// </summary>
public sealed class DocumentDiagnostic
{
    public DocumentDiagnostic(
        DocumentDiagnosticSeverity severity,
        string code,
        string message,
        DocumentDiagnosticLocation? location = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("A diagnostic needs a stable code.", nameof(code));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A diagnostic needs a message.", nameof(message));

        Severity = severity;
        Code = code;
        Message = message;
        Location = location;
    }

    public DocumentDiagnosticSeverity Severity { get; }

    public string Code { get; }

    public string Message { get; }

    /// <summary>Where the diagnostic applies, when the codec can say.</summary>
    public DocumentDiagnosticLocation? Location { get; }

    public static DocumentDiagnostic Info(string code, string message, DocumentDiagnosticLocation? location = null) =>
        new(DocumentDiagnosticSeverity.Info, code, message, location);

    public static DocumentDiagnostic Warning(string code, string message, DocumentDiagnosticLocation? location = null) =>
        new(DocumentDiagnosticSeverity.Warning, code, message, location);

    public static DocumentDiagnostic Error(string code, string message, DocumentDiagnosticLocation? location = null) =>
        new(DocumentDiagnosticSeverity.Error, code, message, location);

    public override string ToString() =>
        Location is null
            ? $"{Severity} {Code}: {Message}"
            : $"{Severity} {Code} ({Location}): {Message}";
}
