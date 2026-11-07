using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// Holds a pending drop-cap paragraph (<c>w:framePr w:dropCap="drop|margin"</c>)
/// so its runs can be prepended to the subsequent host body paragraph, matching
/// Word and LibreOffice.
/// </summary>
internal sealed class DocxDropCapTracker
{
    public XElement? Pending { get; set; }
}
