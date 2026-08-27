using Broiler.Documents.Pdf.Syntax;

namespace Broiler.Documents.Pdf.Tests;

public sealed class PdfLexerTests
{
    private static PdfLexer Lex(string source, PdfLimits? limits = null) =>
        new(PdfFileBuilder.Latin1(source), limits ?? PdfLimits.Default);

    [Fact]
    public void Reads_Integers_And_Reals_Distinctly()
    {
        PdfLexer lexer = Lex("42 -17 3.5 -.002 4.");

        Assert.Equal(PdfTokenType.Integer, lexer.ReadToken().Type);
        Assert.Equal(PdfTokenType.Integer, lexer.ReadToken().Type);

        PdfToken real = lexer.ReadToken();
        Assert.Equal(PdfTokenType.Real, real.Type);
        Assert.Equal(3.5, real.Number);

        Assert.Equal(-0.002, lexer.ReadToken().Number, 6);
        Assert.Equal(4d, lexer.ReadToken().Number);
    }

    [Fact]
    public void Recovers_A_Numeric_Prefix_From_A_Malformed_Number()
    {
        // Producers emit forms like "--5"; recovering the value beats failing the
        // whole object it sits in.
        Assert.Equal(-5d, Lex("--5").ReadToken().Number);
    }

    [Fact]
    public void Resolves_Hash_Escapes_In_Names()
    {
        PdfToken token = Lex("/A#20Name").ReadToken();
        Assert.Equal(PdfTokenType.Name, token.Type);
        Assert.Equal("A Name", token.Text);
    }

    [Fact]
    public void Decodes_Literal_String_Escapes_And_Nesting()
    {
        PdfToken token = Lex(@"(a\(b\)c \n \101 (nested))").ReadToken();
        Assert.Equal(PdfTokenType.LiteralString, token.Type);
        Assert.Equal("a(b)c \n A (nested)", Latin1(token.Bytes!));
    }

    [Fact]
    public void Treats_A_Bare_Carriage_Return_In_A_String_As_One_Newline()
    {
        PdfToken token = Lex("(a\r\nb)").ReadToken();
        Assert.Equal("a\nb", Latin1(token.Bytes!));
    }

    [Fact]
    public void Pads_An_Odd_Final_Hex_Digit_With_Zero()
    {
        PdfToken token = Lex("<4A5>").ReadToken();
        Assert.Equal(PdfTokenType.HexString, token.Type);
        Assert.Equal(new byte[] { 0x4A, 0x50 }, token.Bytes);
    }

    [Fact]
    public void Skips_Comments_Between_Tokens()
    {
        PdfLexer lexer = Lex("1 % a comment\n2");
        Assert.Equal(1d, lexer.ReadToken().Number);
        Assert.Equal(2d, lexer.ReadToken().Number);
    }

    [Fact]
    public void An_Unterminated_String_Ends_At_End_Of_Data_Instead_Of_Looping()
    {
        PdfLexer lexer = Lex("(unterminated");
        Assert.Equal(PdfTokenType.LiteralString, lexer.ReadToken().Type);
        Assert.Equal(PdfTokenType.EndOfData, lexer.ReadToken().Type);
    }

    [Fact]
    public void Rejects_A_Token_Longer_Than_The_Limit()
    {
        var limits = new PdfLimits(maxTokenLength: 8);
        PdfLexer lexer = Lex(new string('a', 64), limits);
        Assert.Throws<PdfLimitExceededException>(() => lexer.ReadToken());
    }

    private static string Latin1(byte[] bytes)
    {
        var builder = new System.Text.StringBuilder(bytes.Length);
        foreach (byte b in bytes)
            builder.Append((char)b);
        return builder.ToString();
    }
}

public sealed class PdfObjectParserTests
{
    private static PdfObject Parse(string source, PdfLimits? limits = null)
    {
        PdfLimits effective = limits ?? PdfLimits.Default;
        var budget = new PdfWorkBudget(effective);
        var lexer = new PdfLexer(PdfFileBuilder.Latin1(source), effective);
        return new PdfObjectParser(lexer, budget).ParseObject();
    }

    [Fact]
    public void Parses_A_Dictionary_With_Mixed_Values()
    {
        var dictionary = Assert.IsType<PdfDictionary>(
            Parse("<< /Type /Page /Count 3 /Flag true /Nothing null /Box [0 0 612 792] >>"));

        Assert.Equal("Page", Assert.IsType<PdfName>(dictionary["Type"]).Value);
        Assert.Equal(3, Assert.IsType<PdfNumber>(dictionary["Count"]).ToInt32());
        Assert.True(Assert.IsType<PdfBoolean>(dictionary["Flag"]).Value);
        Assert.IsType<PdfNull>(dictionary["Nothing"]);
        Assert.Equal(4, Assert.IsType<PdfArray>(dictionary["Box"]).Count);
    }

    [Fact]
    public void Folds_Three_Tokens_Into_An_Indirect_Reference()
    {
        var reference = Assert.IsType<PdfReference>(Parse("12 0 R"));
        Assert.Equal(12, reference.ObjectNumber);
        Assert.Equal(0, reference.Generation);
    }

    [Fact]
    public void Keeps_Adjacent_Integers_As_Numbers_When_No_R_Follows()
    {
        var array = Assert.IsType<PdfArray>(Parse("[12 0 5]"));
        Assert.Equal(3, array.Count);
        Assert.All(array, item => Assert.IsType<PdfNumber>(item));
    }

    [Fact]
    public void Rejects_Nesting_Past_The_Depth_Limit_Without_Exhausting_The_Stack()
    {
        var limits = new PdfLimits(maxNestingDepth: 8);
        string deep = new string('[', 5000);
        Assert.Throws<PdfLimitExceededException>(() => Parse(deep, limits));
    }

    [Fact]
    public void Rejects_A_Container_With_Too_Many_Entries()
    {
        var limits = new PdfLimits(maxContainerEntries: 4);
        Assert.Throws<PdfLimitExceededException>(() => Parse("[1 2 3 4 5 6 7 8]", limits));
    }

    [Fact]
    public void Drops_A_Non_Name_Dictionary_Key_Instead_Of_Shifting_The_Rest()
    {
        // A malformed key must not make every following pair land on the wrong key.
        var dictionary = Assert.IsType<PdfDictionary>(Parse("<< 5 /Ignored /Type /Page >>"));
        Assert.Equal("Page", Assert.IsType<PdfName>(dictionary["Type"]).Value);
    }

    [Fact]
    public void Returns_The_Partial_Container_From_Truncated_Input()
    {
        var dictionary = Assert.IsType<PdfDictionary>(Parse("<< /Type /Page /Count 3"));
        Assert.Equal("Page", Assert.IsType<PdfName>(dictionary["Type"]).Value);
    }
}
