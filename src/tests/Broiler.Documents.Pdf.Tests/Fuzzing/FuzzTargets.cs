using System;
using System.Collections.Generic;
using System.IO;
using Broiler.Documents.Model;

namespace Broiler.Documents.Pdf.Tests.Fuzzing;

/// <summary>
/// One entry point per parser surface, each taking arbitrary bytes and returning
/// nothing (PDF roadmap §6.6).
/// </summary>
/// <remarks>
/// <para>
/// A target's contract is narrow and total: for <em>any</em> input it either
/// returns or throws something on the expected list. A reader is allowed to
/// reject; it is not allowed to hang, to exhaust memory, or to fail in a way its
/// callers cannot name. Anything else is the finding.
/// </para>
/// <para>
/// These are deliberately separate from the reader's own tests. Those assert what
/// a well-formed document produces; these assert what any byte string does, which
/// is a different question and the one an attacker asks.
/// </para>
/// </remarks>
public static class FuzzTargets
{
    /// <summary>Every target, by the name a failure record cites.</summary>
    public static IReadOnlyDictionary<string, Action<ReadOnlySpan<byte>>> All { get; } =
        new Dictionary<string, Action<ReadOnlySpan<byte>>>(StringComparer.Ordinal)
        {
            ["pdf.read"] = Read,
            ["pdf.probe"] = Probe,
        };

    /// <summary>
    /// The whole read path: tokenizer, cross-reference resolution, filters,
    /// structure, content interpretation.
    /// </summary>
    private static void Read(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        DocumentReadResult result = new PdfDocumentCodec().Read(stream, LimitProfile);

        // ADR 0003/0004: a read does not throw on malformed input, it reports.
        // So what a campaign checks here is what the result says, not whether an
        // exception escaped — a harness that only watched for exceptions would
        // pass every input this reader has ever seen and prove nothing.
        if (!Enum.IsDefined(result.Status))
            throw new FuzzContractException("The read returned a status outside the enumeration.");

        foreach (DocumentDiagnostic diagnostic in result.Diagnostics)
        {
            if (string.IsNullOrEmpty(diagnostic.Code))
                throw new FuzzContractException("A diagnostic carried no code, so no host can branch on it.");
        }

        if (result.Status == DocumentResultStatus.Rejected)
            return;

        // A result a host may present has to be presentable. A reader that
        // returns Success over a document whose paragraphs throw on enumeration
        // has handed its caller a landmine.
        foreach (RichTextParagraph paragraph in result.Document.Paragraphs)
        {
            _ = paragraph.Text.Length;
            for (int i = 0; i < paragraph.Length; i++)
                _ = paragraph.StyleAt(i);
        }
    }

    /// <summary>Format detection, which runs on bytes nobody has vouched for.</summary>
    private static void Probe(ReadOnlySpan<byte> data)
    {
        DocumentProbeResult result = new PdfDocumentCodec().Probe(new DocumentProbeRequest(data.ToArray()));
        if (!Enum.IsDefined(result.Confidence))
            throw new FuzzContractException("The probe returned a confidence outside the enumeration.");
    }

    /// <summary>
    /// The limits a campaign runs under, tight enough that a run which would
    /// otherwise take minutes fails fast instead.
    /// </summary>
    /// <remarks>
    /// Named, because a failure record has to say which profile produced it: the
    /// same input under a looser profile is a different result, and a reproduction
    /// that guesses the profile reproduces nothing.
    /// </remarks>
    public static PdfReadOptions LimitProfile { get; } = BoundedProfile();

    private static PdfReadOptions BoundedProfile() => new(
        limits: new DocumentLimits(maxDocumentBytes: 4 * 1024 * 1024),
        pdfLimits: new PdfLimits());

    /// <summary>The name of the limit profile, for the record.</summary>
    public const string LimitProfileName = "bounded-4mib";
}

/// <summary>
/// A target's contract broken. Never on the expected-exception list, which is
/// what makes it a finding rather than a refusal.
/// </summary>
public sealed class FuzzContractException(string message) : Exception(message);
