using System.Text;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// ActiveX form controls (<c>w:object</c> &gt; <c>w:control</c>): a Forms 2.0
/// check box reads as its glyph and caption, as LibreOffice draws it.
/// </summary>
public sealed class DocxActiveXTests
{
    private const string CheckBoxClassId = "{8BD21D40-EC42-11CE-9e0d-00aa006002f3}";

    [Theory]
    [InlineData("1", "☒")]
    [InlineData("0", "☐")]
    public void A_Check_Box_Control_Reads_As_Its_Glyph_And_Caption(string value, string glyph)
    {
        // tdf118169: the caption lives only in the control's persisted
        // compound file, not in document.xml.
        string body =
            "<w:p><w:r><w:object xmlns:v=\"urn:schemas-microsoft-com:vml\">" +
            "<v:shape id=\"control_shape_0\" style=\"width:195.75pt;height:58.4pt\"/>" +
            "<w:control r:id=\"rId2\" w:name=\"Check Box 1\" w:shapeid=\"control_shape_0\"/>" +
            "</w:object></w:r></w:p>";
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/_rels/document.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/control\" " +
                "Target=\"activeX/activeX1.xml\"/></Relationships>",
            ["word/activeX/activeX1.xml"] =
                "<ax:ocx xmlns:ax=\"http://schemas.microsoft.com/office/2006/activeX\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "ax:classid=\"" + CheckBoxClassId + "\" ax:persistence=\"persistStorage\" r:id=\"rId1\"/>",
            ["word/activeX/_rels/activeX1.xml.rels"] =
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.microsoft.com/office/2006/relationships/activeXControlBinary\" " +
                "Target=\"activeX1.bin\"/></Relationships>",
        };
        byte[] package = DocxTestPackage.FromBody(body, parts,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["word/activeX/activeX1.bin"] = CompoundFile(MorphData(value, "Yes")) });

        DocumentReadResult result = new DocxDocumentCodec().Read(new MemoryStream(package));

        Assert.Equal(glyph + " Yes", Assert.Single(result.Document.Paragraphs).Text);
    }

    [Fact]
    public void A_Control_Of_Another_Class_Is_Not_Read()
    {
        Assert.Null(DocxActiveX.Read("{8BD21D10-EC42-11CE-9E0D-00AA006002F3}", CompoundFile(MorphData("1", "Yes"))));
    }

    /// <summary>A MorphDataControl stating DisplayStyle (check box), Value and Caption.</summary>
    private static byte[] MorphData(string value, string caption)
    {
        byte[] valueBytes = Encoding.Unicode.GetBytes(value), captionBytes = Encoding.Unicode.GetBytes(caption);
        var data = new List<byte> { 0, 2, 0, 0 };
        data.AddRange(BitConverter.GetBytes(0x00C00040UL));          // DisplayStyle, Value, Caption
        data.AddRange([4, 0, 0, 0]);                                   // DisplayStyle = check box, padded
        data.AddRange(BitConverter.GetBytes(valueBytes.Length));
        data.AddRange(BitConverter.GetBytes(captionBytes.Length));
        foreach (byte[] text in new[] { valueBytes, captionBytes })
        {
            data.AddRange(text);
            while (data.Count % 4 != 0)
                data.Add(0);
        }
        byte[] bytes = [.. data];
        BitConverter.GetBytes((ushort)(bytes.Length - 4)).CopyTo(bytes, 2);
        return bytes;
    }

    /// <summary>
    /// A version 3 compound file of 512-byte sectors holding one small stream,
    /// <c>contents</c>, in the mini stream: FAT, directory, mini FAT and mini
    /// stream in sectors 0 to 3.
    /// </summary>
    private static byte[] CompoundFile(byte[] contents)
    {
        byte[] file = new byte[512 * 5];
        void Int(int at, int value) => BitConverter.GetBytes(value).CopyTo(file, at);
        void Short(int at, short value) => BitConverter.GetBytes(value).CopyTo(file, at);
        BitConverter.GetBytes(0xE11AB1A1E011CFD0UL).CopyTo(file, 0);
        Short(0x18, 0x3E); Short(0x1A, 3); Short(0x1C, unchecked((short)0xFFFE)); Short(0x1E, 9); Short(0x20, 6);
        Int(0x2C, 1); Int(0x30, 1); Int(0x38, 4096); Int(0x3C, 2); Int(0x40, 1); Int(0x44, -2);
        for (int i = 0; i < 109; i++)
            Int(0x4C + i * 4, i == 0 ? 0 : -1);

        int Sector(int index) => (index + 1) * 512;
        for (int i = 0; i < 128; i++)
            Int(Sector(0) + i * 4, i == 0 ? -3 : i < 4 ? -2 : -1);

        void Entry(int index, string name, byte type, int start, int size)
        {
            int at = Sector(1) + index * 128;
            byte[] encoded = Encoding.Unicode.GetBytes(name + "\0");
            encoded.CopyTo(file, at);
            Short(at + 0x40, (short)encoded.Length);
            file[at + 0x42] = type;
            Int(at + 0x44, -1); Int(at + 0x48, -1); Int(at + 0x4C, index == 0 ? 1 : -1);
            Int(at + 0x74, start); Int(at + 0x78, size);
        }
        int miniSectors = (contents.Length + 63) / 64;
        Entry(0, "Root Entry", 5, 3, miniSectors * 64);
        Entry(1, "contents", 2, 0, contents.Length);

        for (int i = 0; i < 128; i++)
            Int(Sector(2) + i * 4, i < miniSectors - 1 ? i + 1 : i == miniSectors - 1 ? -2 : -1);
        contents.CopyTo(file, Sector(3));
        return file;
    }
}
