using System;

namespace Broiler.Documents;

/// <summary>A hard failure while reading or writing a document (not a recoverable
/// malformed-input case, which is reported via diagnostics instead).</summary>
public class DocumentException : Exception
{
    /// <summary>Initializes a new instance of <see cref="DocumentException"/> with a message.</summary>
    /// <param name="message">The error message.</param>
    public DocumentException(string message) : base(message) { }

    /// <summary>Initializes a new instance of <see cref="DocumentException"/> with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DocumentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when input exceeds a <see cref="DocumentLimits"/> bound.</summary>
public sealed class DocumentLimitExceededException(string message) : DocumentException(message) { }
