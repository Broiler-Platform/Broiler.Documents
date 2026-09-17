using System;

namespace Broiler.Documents.FormatCodes;

/// <summary>Thrown before configured projection resource limits are exceeded.</summary>
public sealed class FormatCodeProjectionLimitException(string message) : InvalidOperationException(message) { }
