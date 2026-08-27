using System;

namespace Broiler.Documents;

/// <summary>A hard failure while reading or writing a document (not a recoverable
/// malformed-input case, which is reported via diagnostics instead).</summary>
public class DocumentException : Exception
{
    public DocumentException(string message)
        : base(message)
    {
    }

    public DocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown when input exceeds a <see cref="DocumentLimits"/> bound.</summary>
public sealed class DocumentLimitExceededException : DocumentException
{
    public DocumentLimitExceededException(string message)
        : base(message)
    {
    }
}
