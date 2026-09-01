using System;
using System.Diagnostics;
using System.Text;

namespace Broiler.Documents.Tests;

/// <summary>
/// Covers the pinned XMP subset (ISO 16684-1) that <see cref="XmpReader"/>
/// implements, and the ceilings that keep a packet from an untrusted document
/// costing more than reading it.
/// </summary>
/// <remarks>
/// Every packet here is written in the test. No fixture is committed, so no
/// sample carries anyone else's metadata, and each test states the exact
/// serialization it is about rather than hiding it in a file.
/// </remarks>
public sealed class XmpReaderTests
{
    private const long Ceiling = 1024 * 1024;

    // ---- the two forms a packet is written in ---------------------------------

    [Fact(Timeout = 600000)]
    public void Reads_Properties_Written_As_Child_Elements()
    {
        XmpReadResult result = XmpReader.Read(Packet(
            """
            <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Quarterly Report</rdf:li></rdf:Alt></dc:title>
            <dc:creator><rdf:Seq><rdf:li>Ada Lovelace</rdf:li><rdf:li>Grace Hopper</rdf:li></rdf:Seq></dc:creator>
            <dc:description><rdf:Alt><rdf:li xml:lang="x-default">Numbers for the quarter</rdf:li></rdf:Alt></dc:description>
            <dc:subject><rdf:Bag><rdf:li>finance</rdf:li><rdf:li>quarterly</rdf:li></rdf:Bag></dc:subject>
            <dc:language><rdf:Bag><rdf:li>en-GB</rdf:li></rdf:Bag></dc:language>
            <xmp:CreatorTool>Broiler.Writer</xmp:CreatorTool>
            <xmp:CreateDate>2026-09-01T09:30:00Z</xmp:CreateDate>
            <pdf:Producer>Broiler.Documents.Pdf</pdf:Producer>
            """), Ceiling);

        Assert.Equal(XmpReadOutcome.Read, result.Outcome);
        XmpMetadata metadata = result.Metadata;

        Assert.Equal("Quarterly Report", metadata.Title);
        Assert.Equal(["Ada Lovelace", "Grace Hopper"], metadata.Authors);
        Assert.Equal("Numbers for the quarter", metadata.Description);
        Assert.Equal(["finance", "quarterly"], metadata.Keywords);
        Assert.Equal("en-GB", metadata.Language);
        Assert.Equal("Broiler.Writer", metadata.CreatorTool);
        Assert.Equal("Broiler.Documents.Pdf", metadata.Producer);
        Assert.Equal(8, metadata.FieldCount);
    }

    [Fact(Timeout = 600000)]
    public void Reads_Properties_Written_As_Description_Attributes()
    {
        // The abbreviated form. Producers use it freely, sometimes alongside the
        // element form in the same packet, so a reader that handles only one of
        // the two silently loses metadata from perfectly ordinary files.
        XmpReadResult result = XmpReader.Read(
            Latin1(Wrap("""<rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmlns:pdf="http://ns.adobe.com/pdf/1.3/" dc:title="Attribute Title" xmp:CreatorTool="Some Tool" pdf:Producer="Some Producer"/>""")),
            Ceiling);

        Assert.Equal(XmpReadOutcome.Read, result.Outcome);
        Assert.Equal("Attribute Title", result.Metadata.Title);
        Assert.Equal("Some Tool", result.Metadata.CreatorTool);
        Assert.Equal("Some Producer", result.Metadata.Producer);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Simple_Property_That_Skips_Its_Container()
    {
        XmpReadResult result = XmpReader.Read(Packet("<dc:title>Bare Title</dc:title>"), Ceiling);

        Assert.Equal("Bare Title", result.Metadata.Title);
    }

    [Fact(Timeout = 600000)]
    public void Reads_A_Packet_That_Has_No_Xmpmeta_Wrapper()
    {
        // x:xmpmeta is conventional, not required. A packet may start at rdf:RDF.
        XmpReadResult result = XmpReader.Read(
            Latin1($"""
            <rdf:RDF xmlns:rdf="{XmpReader.RdfNamespace}">
              <rdf:Description rdf:about="" xmlns:dc="{XmpReader.DublinCoreNamespace}">
                <dc:title>Unwrapped</dc:title>
              </rdf:Description>
            </rdf:RDF>
            """),
            Ceiling);

        Assert.Equal("Unwrapped", result.Metadata.Title);
    }

    // ---- resolving containers -------------------------------------------------

    [Fact(Timeout = 600000)]
    public void Prefers_The_Default_Language_Alternative_Over_Serialization_Order()
    {
        XmpReadResult result = XmpReader.Read(Packet(
            """
            <dc:title><rdf:Alt>
              <rdf:li xml:lang="de-DE">Titel</rdf:li>
              <rdf:li xml:lang="x-default">Title</rdf:li>
            </rdf:Alt></dc:title>
            """), Ceiling);

        // Taking the first entry would return whichever translation the producer
        // happened to serialize first, which is not a language choice at all.
        Assert.Equal("Title", result.Metadata.Title);
    }

    [Fact(Timeout = 600000)]
    public void Falls_Back_To_The_First_Alternative_When_None_Is_Default()
    {
        XmpReadResult result = XmpReader.Read(Packet(
            """
            <dc:title><rdf:Alt>
              <rdf:li xml:lang="de-DE">Titel</rdf:li>
              <rdf:li xml:lang="fr-FR">Titre</rdf:li>
            </rdf:Alt></dc:title>
            """), Ceiling);

        Assert.Equal("Titel", result.Metadata.Title);
    }

    [Fact(Timeout = 600000)]
    public void The_First_Statement_Of_A_Property_Wins()
    {
        XmpReadResult result = XmpReader.Read(Packet(
            """
            <dc:title>First</dc:title>
            <dc:title>Second</dc:title>
            """), Ceiling);

        Assert.Equal("First", result.Metadata.Title);
        Assert.Equal(1, result.IgnoredProperties);
    }

    [Fact(Timeout = 600000)]
    public void Properties_Outside_The_Allowlist_Are_Counted_And_Dropped()
    {
        XmpReadResult result = XmpReader.Read(Packet(
            """
            <dc:title>Kept</dc:title>
            <dc:rights><rdf:Alt><rdf:li xml:lang="x-default">All rights reserved</rdf:li></rdf:Alt></dc:rights>
            <dc:format>application/pdf</dc:format>
            <xmp:MetadataDate>2026-09-01T09:30:00Z</xmp:MetadataDate>
            """), Ceiling);

        Assert.Equal("Kept", result.Metadata.Title);
        Assert.Equal(1, result.Metadata.FieldCount);
        Assert.Equal(3, result.IgnoredProperties);
    }

    // ---- untrusted input ------------------------------------------------------

    [Fact(Timeout = 600000)]
    public void A_Packet_Carrying_A_Dtd_Is_Refused_Without_Expanding_It()
    {
        // The billion-laughs shape. Prohibiting the DTD is what makes this a
        // parse error in microseconds instead of gigabytes of heap, so the test
        // asserts the clock as well as the outcome.
        string bomb =
            "<?xml version=\"1.0\"?>\n" +
            "<!DOCTYPE rdf:RDF [\n" +
            "  <!ENTITY a \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\">\n" +
            "  <!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">\n" +
            "  <!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">\n" +
            "  <!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\">\n" +
            "  <!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\">\n" +
            "  <!ENTITY f \"&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;\">\n" +
            "]>\n" +
            $"<rdf:RDF xmlns:rdf=\"{XmpReader.RdfNamespace}\">" +
            $"<rdf:Description rdf:about=\"\" xmlns:dc=\"{XmpReader.DublinCoreNamespace}\">" +
            "<dc:title>&f;</dc:title></rdf:Description></rdf:RDF>";

        var clock = Stopwatch.StartNew();
        XmpReadResult result = XmpReader.Read(Latin1(bomb), Ceiling);
        clock.Stop();

        Assert.Equal(XmpReadOutcome.Unusable, result.Outcome);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact(Timeout = 600000)]
    public void A_Packet_Over_The_Byte_Ceiling_Is_Refused_Before_It_Is_Parsed()
    {
        byte[] packet = Packet("<dc:title>Small enough on its own</dc:title>");

        XmpReadResult result = XmpReader.Read(packet, packet.Length - 1);

        Assert.Equal(XmpReadOutcome.TooLarge, result.Outcome);
        Assert.True(result.Metadata.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void A_Malformed_Packet_Reports_Only_A_Structural_Reason()
    {
        XmpReadResult result = XmpReader.Read(
            Latin1("<x:xmpmeta><rdf:RDF></x:xmpmeta>"),
            Ceiling);

        Assert.Equal(XmpReadOutcome.Unusable, result.Outcome);

        // An XmlException message quotes the markup it choked on, which would put
        // document content in whatever diagnostic carries this.
        Assert.Equal("XmlException", result.Failure);
    }

    [Fact(Timeout = 600000)]
    public void A_Packet_Nested_Past_The_Depth_Ceiling_Is_Refused()
    {
        var deep = new StringBuilder();
        for (int i = 0; i < XmpReader.MaxDepth + 8; i++)
            deep.Append("<rdf:Description>");
        for (int i = 0; i < XmpReader.MaxDepth + 8; i++)
            deep.Append("</rdf:Description>");

        XmpReadResult result = XmpReader.Read(Packet(deep.ToString()), Ceiling);

        Assert.Equal(XmpReadOutcome.Unusable, result.Outcome);
        Assert.Equal(nameof(XmpReader.MaxDepth), result.Failure);
    }

    [Fact(Timeout = 600000)]
    public void A_Very_Long_Value_Is_Clamped_Rather_Than_Kept()
    {
        XmpReadResult result = XmpReader.Read(
            Packet("<dc:title>" + new string('x', XmpReader.MaxValueLength * 4) + "</dc:title>"),
            Ceiling);

        Assert.Equal(XmpReader.MaxValueLength, result.Metadata.Title?.Length);
    }

    [Fact(Timeout = 600000)]
    public void A_Utf16_Packet_Reads_Through_Its_Byte_Order_Mark()
    {
        string xml = Wrap($"""
            <rdf:Description rdf:about="" xmlns:dc="{XmpReader.DublinCoreNamespace}">
              <dc:title>Wide</dc:title>
            </rdf:Description>
            """);

        var bytes = new List<byte>(Encoding.Unicode.GetPreamble());
        bytes.AddRange(Encoding.Unicode.GetBytes(xml));

        XmpReadResult result = XmpReader.Read(bytes.ToArray(), Ceiling);

        Assert.Equal("Wide", result.Metadata.Title);
    }

    // ---- dates ----------------------------------------------------------------

    [Theory(Timeout = 600000)]
    [InlineData("2026-09-01T09:30:00Z")]
    [InlineData("2026-09-01T09:30:00+02:00")]
    [InlineData("2026-09-01T09:30-05:00")]
    [InlineData("2026-09-01T09:30:00.125Z")]
    public void A_Timestamp_That_States_An_Offset_Is_Recorded_As_Zoned(string value)
    {
        Assert.True(XmpReader.TryParseDate(value, out XmpDate date));
        Assert.True(date.HasUtcOffset);
    }

    [Theory(Timeout = 600000)]
    [InlineData("2026")]
    [InlineData("2026-09")]
    [InlineData("2026-09-01")]
    [InlineData("2026-09-01T09:30")]
    [InlineData("2026-09-01T09:30:00")]
    public void A_Timestamp_Without_An_Offset_Never_Has_One_Invented(string value)
    {
        Assert.True(XmpReader.TryParseDate(value, out XmpDate date));

        // The whole point of the distinction: a zone-less timestamp that silently
        // became UTC would be written back as a different instant.
        Assert.False(date.HasUtcOffset);
    }

    [Theory(Timeout = 600000)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yesterday")]
    [InlineData("2026-13-01")]
    [InlineData("01/09/2026")]
    [InlineData(null)]
    public void An_Unparseable_Timestamp_Is_Refused_Rather_Than_Guessed(string? value)
    {
        Assert.False(XmpReader.TryParseDate(value, out _));
    }

    [Fact(Timeout = 600000)]
    public void A_Property_Whose_Date_Does_Not_Parse_Simply_Has_No_Value()
    {
        XmpReadResult result = XmpReader.Read(
            Packet("<xmp:CreateDate>not a date</xmp:CreateDate><dc:title>Fine</dc:title>"),
            Ceiling);

        Assert.Equal(XmpReadOutcome.Read, result.Outcome);
        Assert.Null(result.Metadata.CreateDate);
        Assert.Equal("Fine", result.Metadata.Title);
    }

    // ---- fixtures -------------------------------------------------------------

    /// <summary>A packet whose single Description holds <paramref name="properties"/>.</summary>
    private static byte[] Packet(string properties) => Latin1(Wrap($"""
        <rdf:Description rdf:about=""
            xmlns:dc="{XmpReader.DublinCoreNamespace}"
            xmlns:xmp="{XmpReader.XmpBasicNamespace}"
            xmlns:pdf="{XmpReader.AdobePdfNamespace}">
          {properties}
        </rdf:Description>
        """));

    /// <summary>The xpacket and xmpmeta envelope a real packet arrives in.</summary>
    private static string Wrap(string body) =>
        $"""
        <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="{XmpReader.RdfNamespace}">
            {body}
          </rdf:RDF>
        </x:xmpmeta>
        <?xpacket end="w"?>
        """;

    private static byte[] Latin1(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = (byte)text[i];
        return bytes;
    }
}
