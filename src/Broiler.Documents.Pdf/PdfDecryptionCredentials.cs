using System;

namespace Broiler.Documents.Pdf;

/// <summary>
/// The secret a caller holds for an encrypted document: a password, which the
/// standard security handler tries as the owner password and then as the user
/// password.
/// </summary>
/// <remarks>
/// <para>
/// A read carries its credentials in <see cref="PdfReadOptions"/> rather than in
/// the service graph, because a password belongs to one document and a service
/// graph to an application. A document encrypted for certificate recipients is
/// opened by a composed <see cref="Security.IPdfRecipientDecryptor"/> instead,
/// which holds the keys of whoever the application runs for.
/// </para>
/// <para>
/// The password is never handed back. It is not a property, <see cref="ToString"/>
/// withholds it, and no diagnostic carries it (ADR 0009). Passing one is the
/// caller's statement that it may open the document with it: the codec cannot
/// tell whose password it was given, only whether the document accepts it.
/// </para>
/// </remarks>
public sealed class PdfDecryptionCredentials
{
    private PdfDecryptionCredentials(string password)
    {
        Password = password;
    }

    /// <summary>
    /// Credentials holding <paramref name="password"/>. An empty password is a
    /// real one - a document whose user password is empty opens without any -
    /// but passing it changes nothing, because that password is tried anyway.
    /// </summary>
    public static PdfDecryptionCredentials FromPassword(string password) =>
        new(password ?? throw new ArgumentNullException(nameof(password)));

    internal string Password { get; }

    public override string ToString() => "PdfDecryptionCredentials (password withheld)";
}
