using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfUnicodeFallbackTests
{
    [Theory]
    [InlineData(@"\uc1\u233\tab tail")]
    [InlineData(@"\uc1\u233\emdash tail")]
    [InlineData(@"\uc1\u233\b tail")]
    [InlineData(@"\uc1\u233\line tail")]
    public void Control_Words_Count_As_One_Fallback_Character(string content)
    {
        RichTextDocument document = Read(@"{\rtf1 " + content + @"\par}");
        Assert.Equal("étail", document.PlainText);
        Assert.False(document.Paragraphs[0].Runs[0].Style.Bold);
    }

    [Theory]
    [InlineData(@"{\rtf1\uc2\u233?{tail}\par}")]
    [InlineData(@"{\rtf1{\uc2\u233?}tail\par}")]
    [InlineData(@"{\rtf1\uc1\u233{tail}\par}")]
    public void Group_Boundaries_End_Unfinished_Fallback_Skipping(string rtf)
    {
        Assert.Equal("étail", Read(rtf).PlainText);
    }

    [Fact]
    public void Unfinished_Header_Fallback_Does_Not_Consume_Body_Text()
    {
        RichTextDocument document = Read(@"{\rtf1{\header\uc1\u233}body\par}");
        Assert.Equal("é", Assert.Single(document.RunningContent.Header(PageSelection.Default)).Text);
        Assert.Equal("body", document.PlainText);
    }

    [Theory]
    [InlineData(@"\uc1\u233\{tail", "étail")]
    [InlineData(@"\uc1\u233\}tail", "étail")]
    [InlineData(@"\uc1\u233\\tail", "étail")]
    [InlineData(@"\uc1\u233\~tail", "étail")]
    [InlineData(@"\uc1\u233\_tail", "étail")]
    [InlineData(@"\uc2\u233\{?tail", "étail")]
    [InlineData(@"\uc0\u233\{tail", "é{tail")]
    public void Escaped_Fallback_Characters_Do_Not_Consume_Following_Text(string content, string expected)
    {
        RichTextDocument document = Read(@"{\rtf1 " + content + @"\par}");
        Assert.Equal(expected, document.PlainText);
    }

    [Fact]
    public void Escaped_Fallback_In_A_Header_Stays_Out_Of_The_Body()
    {
        RichTextDocument document = Read(@"{\rtf1{\header\uc1\u233\{tail\par}body\par}");
        Assert.Equal("étail", Assert.Single(document.RunningContent.Header(PageSelection.Default)).Text);
        Assert.Equal("body", document.PlainText);
    }

    private static RichTextDocument Read(string rtf) => RtfReader.Read(Encoding.ASCII.GetBytes(rtf)).Document;
}
