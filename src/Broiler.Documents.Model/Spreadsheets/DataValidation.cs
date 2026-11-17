using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model.Spreadsheets;

/// <summary>
/// The type of data validation.
/// </summary>
public enum DataValidationType
{
    None,
    WholeNumber,
    Decimal,
    List,
    Date,
    Time,
    TextLength,
    Custom
}

/// <summary>
/// Comparison operator for relational data validation rules.
/// </summary>
public enum DataValidationOperator
{
    Between,
    NotBetween,
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}

/// <summary>
/// Error alert style displayed when validation fails.
/// </summary>
public enum DataValidationErrorStyle
{
    Stop,
    Warning,
    Information
}

/// <summary>
/// Represents a data validation definition applied to one or more cell ranges.
/// </summary>
public sealed record DataValidation(
    IReadOnlyList<CellRangeAddress> Ranges,
    DataValidationType Type,
    DataValidationOperator Operator = DataValidationOperator.Between,
    string? Formula1 = null,
    string? Formula2 = null,
    bool AllowBlank = true,
    bool ShowDropdown = true,
    bool ShowInputMessage = true,
    string? InputTitle = null,
    string? InputMessage = null,
    bool ShowErrorMessage = true,
    string? ErrorTitle = null,
    string? ErrorMessage = null,
    DataValidationErrorStyle ErrorStyle = DataValidationErrorStyle.Stop);
