using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Documents;

/// <summary>
/// What a writer did with the metadata it was given, said out loud.
/// </summary>
/// <remarks>
/// <para>
/// PDF roadmap §6.2 requires the write result to report what was emitted,
/// normalized, or stripped. The reason is that the envelope is deliberately
/// narrower than any one format: a field a caller set can fail to appear in the
/// output because the target format states no such thing, and a caller who is not
/// told cannot tell that from the writer having ignored them. Silence would make
/// the two indistinguishable.
/// </para>
/// <para>
/// Reporting is per-write and in-process. Nothing here is logged or persisted,
/// and the values themselves never appear in a diagnostic — a title can carry a
/// person's name or a case number, and a message that quoted it would leak into
/// whatever collects diagnostics. Field names are named; contents are not.
/// </para>
/// </remarks>
public static class DocumentMetadataReport
{
    /// <summary>
    /// Adds one diagnostic for the fields that reached the output and, when the
    /// format could not express something the caller set, one for those.
    /// </summary>
    /// <param name="metadata">The envelope the caller supplied.</param>
    /// <param name="unsupportedFields">
    /// Field names this format cannot state. Only those the caller actually set
    /// are reported: a format that cannot express a field nobody asked for has
    /// stripped nothing.
    /// </param>
    /// <param name="diagnostics">The write's diagnostic list.</param>
    public static void Describe(
        DocumentMetadata metadata,
        IEnumerable<string> unsupportedFields,
        ICollection<DocumentDiagnostic> diagnostics) =>
        Describe(metadata, unsupportedFields, [], diagnostics);

    /// <summary>
    /// The same, for a format that can state a field but not all of it.
    /// </summary>
    /// <param name="narrowedFields">
    /// Fields that reached the output in a reduced form — the third of §6.2's
    /// three outcomes, and the one silence hides best. A field that is simply
    /// missing is visible to anyone who looks at the output; a list that arrived
    /// with three entries and left with one looks exactly like a list that only
    /// ever had one.
    /// </param>
    public static void Describe(
        DocumentMetadata metadata,
        IEnumerable<string> unsupportedFields,
        IEnumerable<string> narrowedFields,
        ICollection<DocumentDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(unsupportedFields);
        ArgumentNullException.ThrowIfNull(narrowedFields);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (metadata.IsEmpty)
            return;

        var stated = new HashSet<string>(StatedFields(metadata), StringComparer.Ordinal);
        string[] stripped = unsupportedFields
            .Where(stated.Contains)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] narrowed = narrowedFields
            .Where(stated.Contains)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] emitted = stated.Except(stripped, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (emitted.Length > 0)
        {
            diagnostics.Add(DocumentDiagnostic.Info(
                DocumentDiagnosticCodes.MetadataEmitted,
                "Document properties written: " + string.Join(", ", emitted) + "."));
        }

        if (narrowed.Length > 0)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                DocumentDiagnosticCodes.MetadataNarrowed,
                "The target format states a narrower form of these document properties, " +
                "so part of what the caller supplied did not reach the output: " +
                string.Join(", ", narrowed) + "."));
        }

        if (stripped.Length > 0)
        {
            diagnostics.Add(DocumentDiagnostic.Warning(
                DocumentDiagnosticCodes.MetadataDropped,
                "The target format states no equivalent for these document properties, " +
                "so they were dropped rather than written somewhere they do not belong: " +
                string.Join(", ", stripped) + "."));
        }
    }

    /// <summary>The names of the fields this envelope actually states.</summary>
    public static IEnumerable<string> StatedFields(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Title is not null)
            yield return nameof(metadata.Title);
        if (metadata.Authors.Count > 0)
            yield return nameof(metadata.Authors);
        if (metadata.Subject is not null)
            yield return nameof(metadata.Subject);
        if (metadata.Keywords.Count > 0)
            yield return nameof(metadata.Keywords);
        if (metadata.Language is not null)
            yield return nameof(metadata.Language);
        if (metadata.CreatorApplication is not null)
            yield return nameof(metadata.CreatorApplication);
        if (metadata.Producer is not null)
            yield return nameof(metadata.Producer);
        if (metadata.CreationDate is not null)
            yield return nameof(metadata.CreationDate);
        if (metadata.ModificationDate is not null)
            yield return nameof(metadata.ModificationDate);
    }
}
