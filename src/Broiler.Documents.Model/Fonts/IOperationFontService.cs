using System;
using System.Collections.Generic;

namespace Broiler.Documents.Model.Fonts;

/// <summary>How a font face resolution was satisfied.</summary>
public enum FontResolutionOutcome
{
    /// <summary>The face was explicitly provisioned and matched family, weight, and slant exactly.</summary>
    ExactMatch,

    /// <summary>The family was matched; a related face variant was selected.</summary>
    StyleMatch,

    /// <summary>The face was discovered from a configured font directory.</summary>
    DirectoryMatch,

    /// <summary>The family was unmapped and resolved to the configured host fallback face.</summary>
    HostFallback,

    /// <summary>No face binary was available; synthetic metrics / approximate model used.</summary>
    ApproximateModel,

    /// <summary>The request could not be resolved.</summary>
    Unresolved,
}

/// <summary>A logged resolution step for render manifests and diagnostic logs.</summary>
public sealed record FontResolutionRecord(
    string RequestedFamily,
    bool RequestedBold,
    bool RequestedItalic,
    FontResolutionOutcome Outcome,
    ResolvedFontFace? ResolvedFace,
    string Details);

/// <summary>
/// An operation-scoped font resolution and metrics service.
/// </summary>
/// <remarks>
/// <para>
/// Instances are created per operation (per document conversion, layout pass, or CLI command run),
/// completely isolating concurrent operations from each other and eliminating process-global state contention.
/// </para>
/// </remarks>
public interface IOperationFontService : IDisposable
{
    /// <summary>All families that were explicitly mapped or provisioned.</summary>
    IReadOnlyList<string> MappedFamilies { get; }

    /// <summary>Families requested by a document that had no explicit mapping and triggered fallback.</summary>
    IReadOnlyList<string> UnmappedRequests { get; }

    /// <summary>Diagnostic notes or manifest details recorded during font resolution.</summary>
    IReadOnlyList<string> Notes { get; }

    /// <summary>Resolves a font face for the given family, weight, and slant.</summary>
    ResolvedFontFace? ResolveFace(string? family, bool bold = false, bool italic = false);

    /// <summary>Resolves a font face and reports the resolution outcome.</summary>
    (ResolvedFontFace? Face, FontResolutionOutcome Outcome) ResolveWithOutcome(string? family, bool bold = false, bool italic = false);

    /// <summary>True when a genuine italic face exists for this family.</summary>
    bool HasItalicFace(string? family);
}
