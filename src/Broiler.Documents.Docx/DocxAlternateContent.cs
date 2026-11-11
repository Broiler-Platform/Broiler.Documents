using System;
using System.Linq;
using System.Xml.Linq;

namespace Broiler.Documents.Docx;

/// <summary>
/// Chooses the branch of an <c>mc:AlternateContent</c> block this reader reads.
/// </summary>
internal static class DocxAlternateContent
{
    /// <summary>
    /// The drawing canvas namespace (<c>wpc:wpc</c>). The reader does not lay out
    /// a canvas: it would find the first text box inside it and lose the rest.
    /// </summary>
    internal static readonly XNamespace WordCanvas = "http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas";

    /// <summary>
    /// The first <c>mc:Choice</c> whose <c>Requires</c> names no namespace the
    /// reader cannot read, otherwise the <c>mc:Fallback</c>. A consumer that does
    /// not understand a required namespace skips the choice (ECMA-376 part 3,
    /// §10.2.1). Only the drawing canvas is treated as not understood, and only
    /// when a fallback exists: Word writes a canvas's VML twin there, a group of
    /// the same boxes and lines, which the reader draws (groupshape-fontname.docx).
    /// </summary>
    internal static XElement? Branch(XElement alternate)
    {
        XElement? fallback = alternate.Element(DocxNamespaces.MarkupCompatibility + "Fallback");
        foreach (XElement choice in alternate.Elements(DocxNamespaces.MarkupCompatibility + "Choice"))
        {
            if (fallback is null || !RequiresCanvas(choice))
                return choice;
        }

        return fallback;
    }

    private static bool RequiresCanvas(XElement choice)
    {
        string? requires = (string?)choice.Attribute("Requires");
        if (string.IsNullOrWhiteSpace(requires))
            return false;

        return requires.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(prefix => choice.GetNamespaceOfPrefix(prefix) == WordCanvas);
    }
}
