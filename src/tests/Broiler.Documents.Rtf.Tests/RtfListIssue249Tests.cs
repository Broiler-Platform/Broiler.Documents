using System.Text;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Office conformance issue 249: an RTF level numbered "Arabic with a leading
/// zero" (<c>\levelnfc22</c>) writes 01, 02, as LibreOffice 24.2 draws
/// listWithLgl.rtf's "Sect I.01".
/// </summary>
public sealed class RtfListIssue249Tests
{
    [Fact]
    public void A_Leading_Zero_Level_Writes_Two_Digits()
    {
        DocumentReadResult result = RtfReader.Read(Encoding.Latin1.GetBytes(
            @"{\rtf1{\*\listtable{\list" +
            @"{\listlevel\levelnfc1\levelstartat1{\leveltext \'02\'00.;}{\levelnumbers\'01;}}" +
            @"{\listlevel\levelnfc22\levelstartat1{\leveltext \'03\'00.\'01;}{\levelnumbers\'01\'03;}}\listid1}}" +
            @"{\*\listoverridetable{\listoverride\listid1\listoverridecount0\ls1}}" +
            @"\pard\ls1\ilvl0 One\par\pard\ls1\ilvl1 Two\par}"), new DocumentReadOptions());

        var numberer = new ListNumberer();
        Assert.Equal("I.", numberer.Advance(result.Document.Paragraphs[0].Style));
        Assert.Equal("I.01", numberer.Advance(result.Document.Paragraphs[1].Style));
    }
}
