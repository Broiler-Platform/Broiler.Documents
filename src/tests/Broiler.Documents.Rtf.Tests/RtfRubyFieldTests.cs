using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfRubyFieldTests
{
    private static DocumentReadResult Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf));
    private const string Ruby = @"{\field{\*\fldinst EQ \\o\\ar(\\s\\up 10(phonetic),\uc2\u39740\'b0\'ad\u38272\'aa\'f9)}{\fldrslt }}";

    [Fact]
    public void Empty_Ruby_Results_Keep_Their_Unicode_Base_Text()
    {
        var result = Read(@"{\rtf1 " + Ruby + @"\uc1\u12398?\par}");
        Assert.Equal("鬼門の", result.Document.PlainText);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.field.ruby");
    }

    [Fact]
    public void A_Format_Group_Before_The_Cache_Does_Not_Trigger_Early_Fallback()
    {
        var result = Read(@"{\rtf1 " + Ruby.Replace(@"{\fldrslt }", @"{\b}{\fldrslt cached}") + @"\par}");
        Assert.Equal("cached", result.Document.PlainText);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.field.ruby");
    }

    [Fact]
    public void A_Nested_Field_Does_Not_Replace_The_Parents_Instruction_Or_Duplicate_Its_Base()
    {
        string nested = Ruby.Replace(@"{\fldrslt }", @"{\fldrslt {\field{\*\fldinst UNKNOWN}{\fldrslt child}}}");
        Assert.Equal("child", Read(@"{\rtf1 " + nested + @"\par}").Document.PlainText);
    }

    [Fact]
    public void A_Cached_Result_Is_Preferred_And_Is_Not_Duplicated()
    {
        var result = Read(@"{\rtf1 " + Ruby.Replace(@"\fldrslt ", @"\fldrslt cached") + @"\par}");
        Assert.Equal("cached", result.Document.PlainText);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "rtf.field.ruby");
    }

    [Fact]
    public void Ruby_In_A_Header_Stays_In_The_Header()
    {
        var result = Read(@"{\rtf1{\header " + Ruby + @"\par}body\par}");
        Assert.Equal("body", result.Document.PlainText);
        Assert.Equal("鬼門", Assert.Single(result.Document.RunningContent.Header(PageSelection.Default)).Text);
    }

    [Fact]
    public void An_Unrecognised_Equation_Is_Not_Evaluated_As_Ruby()
    {
        Assert.Equal("after", Read(@"{\rtf1{\field{\*\fldinst EQ \\o(a,b)}{\fldrslt }}after\par}").Document.PlainText);
    }

    [Fact]
    public void Unicode_Field_Instructions_Retain_Unicode_Hyperlink_Targets()
    {
        var paragraph = Assert.Single(Read(@"{\rtf1{\field{\*\fldinst HYPERLINK \\l ""\uc1\u233?""}{\fldrslt target}}\par}").Document.Paragraphs);
        Assert.Equal("#é", paragraph.Runs[0].Style.LinkHref);
    }
}
