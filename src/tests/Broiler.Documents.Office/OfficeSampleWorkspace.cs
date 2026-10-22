using System;
using System.Globalization;
using System.IO;

namespace Broiler.Documents.Office;

/// <summary>Owns only one sample's generated files, never its source document.</summary>
internal sealed class OfficeSampleWorkspace : IDisposable
{
    private readonly bool keep;
    private int profileSlot;

    public OfficeSampleWorkspace(string workspace, bool keep)
    {
        Root = Path.Combine(Path.GetFullPath(workspace), "samples", Guid.NewGuid().ToString("N"));
        this.keep = keep;
    }

    public string Root { get; }

    public string NextProfile() => Path.Combine(Root, "profile",
        (profileSlot++).ToString(CultureInfo.InvariantCulture));

    public void Dispose()
    {
        // Root is an absolute, uniquely generated child of the supplied workspace;
        // no caller-supplied sample name can escape it or collide with another job.
        if (!keep && Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
