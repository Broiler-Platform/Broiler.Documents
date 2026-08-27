using Broiler.Documents.Model;

namespace Broiler.Documents.FormatCodes;

/// <summary>A typed span in the canonical projection or pending overlay.</summary>
public sealed class FormatCodeToken
{
    internal FormatCodeToken(
        FormatCodeTokenKind kind,
        string displayText,
        int projectedStart,
        int projectedLength,
        RichTextPosition sourceBefore,
        RichTextPosition sourceAfter,
        RichTextRange? affectedRange,
        FormatCodeEditCapabilities editCapabilities,
        FormatCodeTokenEditDescriptor? editDescriptor,
        FormatCodeMappingMode mappingMode)
    {
        Kind = kind;
        DisplayText = displayText;
        ProjectedStart = projectedStart;
        ProjectedLength = projectedLength;
        SourceBefore = sourceBefore;
        SourceAfter = sourceAfter;
        AffectedRange = affectedRange;
        EditCapabilities = editCapabilities;
        EditDescriptor = editDescriptor;
        MappingMode = mappingMode;
    }

    public FormatCodeTokenKind Kind { get; }

    public string DisplayText { get; }

    public int ProjectedStart { get; }

    /// <summary>Length contributed to canonical text; pending overlays contribute zero.</summary>
    public int ProjectedLength { get; }

    public RichTextPosition SourceBefore { get; }

    public RichTextPosition SourceAfter { get; }

    public RichTextRange? AffectedRange { get; }

    public FormatCodeEditCapabilities EditCapabilities { get; }

    public FormatCodeTokenEditDescriptor? EditDescriptor { get; }

    internal FormatCodeMappingMode MappingMode { get; }
}

internal enum FormatCodeMappingMode
{
    Boundary = 0,
    Linear,
    Expanded,
}
