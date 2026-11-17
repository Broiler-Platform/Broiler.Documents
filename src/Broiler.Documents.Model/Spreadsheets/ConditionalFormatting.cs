using System;
using System.Collections.Generic;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Model.Spreadsheets;

/// <summary>
/// The type of conditional formatting rule.
/// </summary>
public enum ConditionalFormatRuleType
{
    CellIs,
    ContainsText,
    NotContainsText,
    BeginsWith,
    EndsWith,
    Expression,
    DuplicateValues,
    UniqueValues,
    Blanks,
    NoBlanks,
    ColorScale
}

/// <summary>
/// Comparison operator for cellIs conditional formatting rules.
/// </summary>
public enum ConditionalFormatOperator
{
    None,
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    NotBetween
}

/// <summary>
/// Styling attributes applied to a cell when a conditional formatting rule matches.
/// </summary>
public sealed record ConditionalFormatStyle(
    BColor? Shading = null,
    BColor? Foreground = null,
    bool? Bold = null,
    bool? Italic = null,
    bool? Underline = null,
    bool? Strikethrough = null,
    CellBorders? Borders = null);

/// <summary>
/// Configuration for a 2-color or 3-color gradient conditional format.
/// </summary>
public sealed record ColorScaleConfiguration(
    BColor MinColor,
    BColor MaxColor,
    BColor? MidColor = null,
    double? MinValue = null,
    double? MaxValue = null,
    double? MidValue = null);

/// <summary>
/// Represents a single rule within a conditional format definition.
/// </summary>
public sealed record ConditionalFormatRule(
    ConditionalFormatRuleType Type,
    ConditionalFormatOperator Operator = ConditionalFormatOperator.None,
    string? Formula1 = null,
    string? Formula2 = null,
    ConditionalFormatStyle? Style = null,
    ColorScaleConfiguration? ColorScale = null,
    int Priority = 0,
    bool StopIfTrue = false);

/// <summary>
/// Represents a conditional format applied across one or more cell ranges.
/// </summary>
public sealed record ConditionalFormat(
    IReadOnlyList<CellRangeAddress> Ranges,
    IReadOnlyList<ConditionalFormatRule> Rules);
