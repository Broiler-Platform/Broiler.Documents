using System.Text;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfFuzzTests
{
    private const string ValidSample =
        "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Calibri;}}" +
        "{\\colortbl ;\\red255\\green0\\blue0;}\\pard\\f0\\fs22 Hello \\b bold\\b0  and " +
        "{\\field{\\*\\fldinst{HYPERLINK \"https://x.com\"}}{\\fldrslt \\cf1 link}}.\\par More.\\par}";

    [Fact]
    public void Random_Bytes_Never_Throw()
    {
        var random = new Random(1234567);
        for (int trial = 0; trial < 300; trial++)
        {
            byte[] payload = new byte[random.Next(0, 512)];
            random.NextBytes(payload);

            RichTextDocument document = RtfReader.Read(payload).Document;

            Assert.NotNull(document);
            Assert.True(document.ParagraphCount >= 1);
        }
    }

    [Fact]
    public void Rtf_Flavoured_Random_Bytes_Never_Throw()
    {
        // Bias the alphabet toward RTF structure so more control paths are exercised.
        byte[] alphabet = Encoding.ASCII.GetBytes("\\{}bicfsul0123456789 abpar\\'uc*;");
        var random = new Random(7654321);
        for (int trial = 0; trial < 300; trial++)
        {
            byte[] payload = new byte[random.Next(0, 400)];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = alphabet[random.Next(alphabet.Length)];

            Assert.NotNull(RtfReader.Read(payload).Document);
        }
    }

    [Fact]
    public void Read_Write_Read_Is_Stable_On_Random_Inputs()
    {
        byte[] alphabet = Encoding.ASCII.GetBytes("\\{}bicfsul0123456789 abpar\\'uc*;xyz");
        var random = new Random(24680);
        for (int trial = 0; trial < 200; trial++)
        {
            byte[] payload = new byte[random.Next(0, 300)];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = alphabet[random.Next(alphabet.Length)];

            RichTextDocument first = RtfReader.Read(payload).Document;
            RichTextDocument second = RtfReader.Read(RtfWriter.WriteToArray(first)).Document;

            DocumentAssert.Equivalent(first, second);
        }
    }

    [Fact]
    public void Truncations_Of_Valid_Rtf_Never_Throw()
    {
        byte[] full = Encoding.Latin1.GetBytes(ValidSample);
        for (int length = 0; length <= full.Length; length++)
        {
            byte[] prefix = full.AsSpan(0, length).ToArray();
            Assert.NotNull(RtfReader.Read(prefix).Document);
        }
    }

    [Fact]
    public void Single_Byte_Flips_Of_Valid_Rtf_Never_Throw()
    {
        byte[] full = Encoding.Latin1.GetBytes(ValidSample);
        var random = new Random(13572468);
        for (int trial = 0; trial < 400; trial++)
        {
            byte[] mutated = (byte[])full.Clone();
            mutated[random.Next(mutated.Length)] = (byte)random.Next(256);

            Assert.NotNull(RtfReader.Read(mutated).Document);
        }
    }

    [Fact]
    public void Nesting_Bomb_Is_Bounded_And_Does_Not_Overflow()
    {
        byte[] bomb = Encoding.ASCII.GetBytes("{\\rtf1" + new string('{', 200_000) + "x");

        RtfTokenizeResult tokens = RtfTokenizer.Tokenize(bomb);
        RichTextDocument document = RtfReader.Read(bomb).Document;

        Assert.True(tokens.Truncated);
        Assert.True(tokens.Tokens.Count <= DocumentLimits.Default.MaxGroupDepth + 2);
        Assert.NotNull(document);
    }

    [Fact]
    public void Huge_Control_Word_Parameter_Does_Not_Overflow()
    {
        RichTextDocument document = RtfReader.Read(
            Encoding.ASCII.GetBytes("{\\rtf1\\fs999999999999999 x\\li88888888888 y}")).Document;

        Assert.NotNull(document);
        Assert.Contains("x", document.PlainText);
    }

    [Fact]
    public void Very_Long_Text_Run_Is_Handled()
    {
        byte[] payload = Encoding.ASCII.GetBytes("{\\rtf1 " + new string('a', 500_000) + "}");

        RichTextDocument document = RtfReader.Read(payload).Document;

        Assert.Equal(500_000, document.PlainText.Length);
    }
}
