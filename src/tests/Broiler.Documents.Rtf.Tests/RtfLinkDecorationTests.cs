using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// An RTF <c>HYPERLINK</c> field's result is drawn as its formatting says:
/// LibreOffice gives one that names no character style no colour and no
/// underline (issue #180's probes), so the document leaves its links undecorated
/// (<see cref="DocumentStyleDefaults.UndecoratedLinks"/>).
/// </summary>
public sealed class RtfLinkDecorationTests
{
    [Fact]
    public void An_Rtf_Document_Draws_Its_Links_As_Their_Formatting_Says()
    {
        RichTextDocument document = RtfReader.Read(Encoding.ASCII.GetBytes(
            @"{\rtf1 {\field{\*\fldinst HYPERLINK ""https://example.com/""}{\fldrslt link}}\par}")).Document;

        Assert.True(document.StyleDefaults.UndecoratedLinks);
    }
}
