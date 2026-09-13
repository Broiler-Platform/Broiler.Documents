using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Broiler.Documents.Packaging;

namespace Broiler.Documents.Tests;

public sealed class DocumentPackageTests
{
    [Theory]
    [InlineData("A DOCX XML part", "docx.document")]
    [InlineData("An ODT XML part", "odt.content")]
    public void Exact_Limit_Is_Accepted_And_One_Byte_Less_Is_Rejected(string description, string code)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<root>text</root>");
        using var archive = CreateArchive(bytes);
        var diagnostics = new List<DocumentDiagnostic>();
        Assert.NotNull(DocumentPackage.LoadEntryXml(archive.Entries[0], bytes.Length, diagnostics, code, description));
        Assert.Empty(diagnostics);
        Assert.Null(DocumentPackage.LoadEntryXml(archive.Entries[0], bytes.Length - 1, diagnostics, code, description));
        Assert.Equal(code + ".limit", Assert.Single(diagnostics).Code);
    }

    [Theory]
    [InlineData("<!DOCTYPE root [<!ENTITY text 'expanded'>]><root>&text;</root>")]
    [InlineData("<!DOCTYPE root SYSTEM 'https://example.invalid/external.dtd'><root/>")]
    [InlineData("<root>")]
    public void Dtds_And_Invalid_Xml_Are_Rejected_With_The_Callers_Diagnostic(string xml)
    {
        using var archive = CreateArchive(Encoding.UTF8.GetBytes(xml));
        var diagnostics = new List<DocumentDiagnostic>();
        Assert.Null(DocumentPackage.LoadEntryXml(archive.Entries[0], 4096, diagnostics, "format.part", "A part"));
        Assert.Equal("format.part", Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void Whitespace_Options_Preserve_The_Gap_Between_Spans()
    {
        using var archive = CreateArchive(Encoding.UTF8.GetBytes("<root><span>a</span> <span>b</span></root>"));
        var diagnostics = new List<DocumentDiagnostic>();
        XDocument? preserved = DocumentPackage.LoadEntryXml(archive.Entries[0], 4096, diagnostics,
            "format.part", "A part", LoadOptions.PreserveWhitespace);
        XDocument? normalized = DocumentPackage.LoadEntryXml(archive.Entries[0], 4096, diagnostics, "format.part", "A part");
        Assert.Equal("a b", preserved?.Root?.Value);
        Assert.Equal("ab", normalized?.Root?.Value);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_Highly_Compressed_Entry_Is_Limited_By_Decompressed_Bytes()
    {
        using var archive = CreateArchive(new byte[100_000]);
        Assert.True(archive.Entries[0].CompressedLength < 1024);
        Assert.Null(DocumentPackage.ReadEntryBytes(archive.Entries[0], 1024));
        Assert.Equal(100_000, DocumentPackage.ReadEntryBytes(archive.Entries[0], 100_000)!.Length);
    }

    private static ZipArchive CreateArchive(byte[] bytes)
    {
        var memory = new MemoryStream();
        using (var writer = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        using (Stream entry = writer.CreateEntry("part.xml", CompressionLevel.SmallestSize).Open())
            entry.Write(bytes);
        memory.Position = 0;
        return new ZipArchive(memory, ZipArchiveMode.Read);
    }
}
