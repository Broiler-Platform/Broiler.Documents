using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pagination.Drawing;

/// <summary>Severity level for graphics drawing and fixed-page diagnostics.</summary>
public enum DrawingDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A diagnostic recorded during display list translation or fixed-page replaying.</summary>
public readonly record struct DrawingDiagnostic(
    string Code,
    DrawingDiagnosticSeverity Severity,
    string Message,
    string? ItemKind = null);

/// <summary>Collects diagnostics for unsupported commands, fallback conversions, and stack imbalances.</summary>
public sealed class DrawingDiagnosticSink
{
    private readonly List<DrawingDiagnostic> _diagnostics = [];

    public IReadOnlyList<DrawingDiagnostic> Diagnostics => _diagnostics;

    public bool HasWarnings => _diagnostics.Exists(d => d.Severity == DrawingDiagnosticSeverity.Warning);
    public bool HasErrors => _diagnostics.Exists(d => d.Severity == DrawingDiagnosticSeverity.Error);

    public void Report(string code, DrawingDiagnosticSeverity severity, string message, string? itemKind = null)
    {
        _diagnostics.Add(new DrawingDiagnostic(code, severity, message, itemKind));
    }

    public void Info(string code, string message, string? itemKind = null) =>
        Report(code, DrawingDiagnosticSeverity.Info, message, itemKind);

    public void Warning(string code, string message, string? itemKind = null) =>
        Report(code, DrawingDiagnosticSeverity.Warning, message, itemKind);

    public void Error(string code, string message, string? itemKind = null) =>
        Report(code, DrawingDiagnosticSeverity.Error, message, itemKind);
}
