using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfTokenizerTests
{
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private static RtfTokenizeResult Tokenize(string s) => RtfTokenizer.Tokenize(Bytes(s));

    [Fact]
    public void Tokenizes_Groups_Control_Words_And_Text()
    {
        RtfTokenizeResult result = Tokenize("{\\rtf1\\b Hello}");

        Assert.Equal(5, result.Tokens.Count);
        Assert.Equal(RtfTokenType.GroupStart, result.Tokens[0].Type);

        RtfToken rtf = result.Tokens[1];
        Assert.Equal(RtfTokenType.ControlWord, rtf.Type);
        Assert.Equal("rtf", rtf.Keyword);
        Assert.True(rtf.HasParameter);
        Assert.Equal(1, rtf.Parameter);

        RtfToken bold = result.Tokens[2];
        Assert.Equal("b", bold.Keyword);
        Assert.False(bold.HasParameter);

        RtfToken text = result.Tokens[3];
        Assert.Equal(RtfTokenType.Text, text.Type);
        Assert.Equal("Hello", text.Text);

        Assert.Equal(RtfTokenType.GroupEnd, result.Tokens[4].Type);
    }

    [Fact]
    public void A_Single_Trailing_Space_Is_The_Control_Word_Delimiter()
    {
        RtfTokenizeResult result = Tokenize("\\b Hello");

        Assert.Equal(2, result.Tokens.Count);
        Assert.Equal("Hello", result.Tokens[1].Text);
    }

    [Fact]
    public void Extra_Spaces_After_A_Control_Word_Are_Literal_Text()
    {
        RtfTokenizeResult result = Tokenize("\\b  Hello");

        Assert.Equal(" Hello", result.Tokens[^1].Text);
    }

    [Fact]
    public void Negative_Control_Word_Parameters_Are_Parsed()
    {
        RtfTokenizeResult result = Tokenize("\\u-1 x");

        RtfToken u = result.Tokens[0];
        Assert.Equal("u", u.Keyword);
        Assert.True(u.HasParameter);
        Assert.Equal(-1, u.Parameter);
        Assert.Equal("x", result.Tokens[1].Text);
    }

    [Fact]
    public void Control_Symbols_Are_Recognized()
    {
        // Three escaped symbols: \\  \{  \}
        RtfTokenizeResult result = Tokenize("\\\\\\{\\}");

        Assert.Equal(3, result.Tokens.Count);
        Assert.All(result.Tokens, token => Assert.Equal(RtfTokenType.ControlSymbol, token.Type));
        Assert.Equal('\\', result.Tokens[0].Symbol);
        Assert.Equal('{', result.Tokens[1].Symbol);
        Assert.Equal('}', result.Tokens[2].Symbol);
    }

    [Fact]
    public void Hex_Escapes_Decode_To_A_Byte_Value()
    {
        RtfTokenizeResult result = Tokenize("\\'41");

        RtfToken hex = Assert.Single(result.Tokens);
        Assert.Equal(RtfTokenType.HexByte, hex.Type);
        Assert.Equal(0x41, hex.Parameter);
    }

    [Fact]
    public void Malformed_Hex_Is_Reported_Not_Thrown()
    {
        RtfTokenizeResult result = Tokenize("\\'zz");

        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.hex");
    }

    [Fact]
    public void Stream_Line_Breaks_Are_Dropped()
    {
        RtfTokenizeResult result = Tokenize("a\r\nb");

        RtfToken text = Assert.Single(result.Tokens);
        Assert.Equal("ab", text.Text);
    }

    [Fact]
    public void Group_Depth_Limit_Stops_Tokenizing_Without_Throwing()
    {
        RtfTokenizeResult result = RtfTokenizer.Tokenize(Bytes("{{{}}}"), new DocumentLimits(maxGroupDepth: 2));

        Assert.True(result.Truncated);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.depth");
    }

    [Fact]
    public void Document_Size_Limit_Truncates_Input()
    {
        RtfTokenizeResult result = RtfTokenizer.Tokenize(Bytes("abcdefgh"), new DocumentLimits(maxDocumentBytes: 4));

        Assert.True(result.Truncated);
        Assert.Contains(result.Diagnostics, d => d.Code == "rtf.size");
        Assert.Equal("abcd", result.Tokens[0].Text);
    }

    [Fact]
    public void Long_Runs_Are_Split_But_Preserved()
    {
        RtfTokenizeResult result = RtfTokenizer.Tokenize(Bytes("aaaaaaaaaa"), new DocumentLimits(maxRunLength: 4));

        string joined = string.Concat(result.Tokens
            .Where(t => t.Type == RtfTokenType.Text)
            .Select(t => t.Text));

        Assert.Equal("aaaaaaaaaa", joined);
        Assert.True(result.Tokens.Count > 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\\")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("}}}}}}")]
    [InlineData("{{{{{{{{")]
    [InlineData("\\'")]
    [InlineData("\\'z")]
    [InlineData("\\bin999999999 ")]
    [InlineData("{\\rtf1{{{\\b unclosed")]
    public void Hostile_Input_Never_Throws(string input)
    {
        RtfTokenizeResult result = RtfTokenizer.Tokenize(Bytes(input));

        Assert.NotNull(result.Tokens);
    }

    [Fact]
    public void Random_Bytes_Never_Throw_And_Always_Make_Progress()
    {
        var random = new Random(20260705);
        for (int trial = 0; trial < 50; trial++)
        {
            byte[] payload = new byte[256];
            random.NextBytes(payload);

            RtfTokenizeResult result = RtfTokenizer.Tokenize(payload);

            Assert.NotNull(result.Tokens);
        }
    }
}
