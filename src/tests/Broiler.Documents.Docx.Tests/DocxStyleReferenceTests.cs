using System.IO.Compression;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// <c>STYLEREF</c> fields naming a character style, and the character style
/// names they find text by.
/// </summary>
/// <remarks>
/// <c>tdf163894.docx</c> and <c>tdf163894_hidden.docx</c> head a dictionary's
/// pages with <c>STYLEREF "Main Index Entry"</c> and <c>STYLEREF "Main Index
/// Entry" \l</c>: the first and the last entry word on each page.
/// </remarks>
public sealed class DocxStyleReferenceTests
{
    private static readonly string Styles =
        "<w:style w:type=\"character\" w:styleId=\"MainIndexEntry\"><w:name w:val=\"Main Index Entry\"/>" +
        "<w:rPr><w:b/></w:rPr></w:style>" +
        DocxTestPackage.Style("Heading1", "<w:pPr/>") +
        "<w:style w:type=\"character\" w:styleId=\"Heading1Char\"><w:name w:val=\"Heading 1 Char\"/></w:style>";

    /// <summary>A paragraph holding a complex field spelled <paramref name="instruction"/> whose saved result is <paramref name="saved"/>.</summary>
    private static string ComplexField(string instruction, string saved = "saved") =>
        "<w:p>" +
        "<w:r><w:t xml:space=\"preserve\">before </w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\">" + instruction + "</w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        "<w:r><w:t>" + saved + "</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
        "<w:r><w:t xml:space=\"preserve\"> after</w:t></w:r>" +
        "</w:p>";

    private static RichTextParagraph Read(string bodyXml) =>
        DocxTestPackage.ReadStyled(bodyXml, Styles).Document.Paragraphs[0];

    private static StyleReferenceField? FieldAt(RichTextParagraph paragraph, string text) =>
        paragraph.StyleAt(paragraph.Text.IndexOf(text, StringComparison.Ordinal)).StyleReference;

    [Fact]
    public void A_Styleref_Naming_A_Character_Style_Marks_Its_Saved_Result()
    {
        RichTextParagraph paragraph = Read(ComplexField(" STYLEREF \"Main Index Entry\" ", "mitosis"));

        Assert.Equal("before mitosis after", paragraph.Text);
        Assert.Equal(new StyleReferenceField("Main Index Entry"), FieldAt(paragraph, "mitosis"));
        Assert.Null(FieldAt(paragraph, "before"));
        Assert.Null(FieldAt(paragraph, "after"));
    }

    [Fact]
    public void The_L_Switch_Searches_From_The_Bottom_Of_The_Page()
    {
        RichTextParagraph paragraph = Read(ComplexField(" STYLEREF \"Main Index Entry\" \\l ", "modicum"));

        Assert.Equal(new StyleReferenceField("Main Index Entry", FromBottom: true), FieldAt(paragraph, "modicum"));
    }

    [Fact]
    public void A_Style_Name_Is_Matched_Ignoring_Case_And_Kept_As_The_Styles_Spell_It()
    {
        RichTextParagraph paragraph = Read(ComplexField(" STYLEREF \"main index entry\" \\* MERGEFORMAT ", "x"));

        Assert.Equal(new StyleReferenceField("Main Index Entry"), FieldAt(paragraph, "x"));
    }

    [Theory]
    [InlineData(" STYLEREF \"Heading1\" ")]
    [InlineData(" STYLEREF 1 ")]
    [InlineData(" STYLEREF \"Main Index Entry\" \\n ")]
    [InlineData(" STYLEREF \"Main Index Entry\" \\* Upper ")]
    [InlineData(" STYLEREF \"No Such Style\" ")]
    [InlineData(" PAGE ")]
    public void Any_Other_Field_Keeps_Its_Saved_Result_As_Text(string instruction)
    {
        RichTextParagraph paragraph = Read(ComplexField(instruction));

        Assert.Equal("before saved after", paragraph.Text);
        Assert.Null(FieldAt(paragraph, "saved"));
    }

    [Fact]
    public void A_Field_Nested_In_Another_Marks_Only_Its_Own_Result()
    {
        RichTextParagraph paragraph = Read(
            "<w:p>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText>STYLEREF \"Main Index Entry\"</w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            "<w:r><w:t xml:space=\"preserve\">outer </w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText>PAGE</w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            "<w:r><w:t>7</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "<w:r><w:t xml:space=\"preserve\"> tail</w:t></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
            "</w:p>");

        Assert.Equal("outer 7 tail", paragraph.Text);
        Assert.NotNull(FieldAt(paragraph, "outer"));
        Assert.Null(FieldAt(paragraph, "7"));
        Assert.NotNull(FieldAt(paragraph, "tail"));
    }

    [Fact]
    public void A_Simple_Field_Marks_The_Runs_It_Holds()
    {
        RichTextParagraph paragraph = Read(
            "<w:p><w:fldSimple w:instr=\" STYLEREF &quot;Main Index Entry&quot; \\l \">" +
            "<w:r><w:t>hank</w:t></w:r></w:fldSimple></w:p>");

        Assert.Equal(new StyleReferenceField("Main Index Entry", FromBottom: true), FieldAt(paragraph, "hank"));
    }

    [Fact]
    public void A_Run_Keeps_The_Name_Of_Its_Character_Style()
    {
        RichTextParagraph paragraph = Read(
            "<w:p><w:r><w:rPr><w:rStyle w:val=\"MainIndexEntry\"/></w:rPr><w:t>handbooks</w:t></w:r>" +
            "<w:r><w:t xml:space=\"preserve\">, and more</w:t></w:r></w:p>");

        InlineStyle styled = paragraph.StyleAt(0);
        Assert.Equal("Main Index Entry", styled.CharacterStyle);
        Assert.True(styled.Bold);
        Assert.Null(paragraph.StyleAt(paragraph.Text.IndexOf("more", StringComparison.Ordinal)).CharacterStyle);
    }

    [Theory]
    [InlineData("15", true)]
    [InlineData("14", false)]
    public void Compatibility_Mode_15_Shrinks_Justified_Lines(string mode, bool shrinks)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["word/settings.xml"] =
                "<w:settings " + DocxTestPackage.BodyNamespaceDeclarations + "><w:compat>" +
                "<w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"" + mode + "\"/>" +
                "</w:compat></w:settings>",
        };

        using var stream = new MemoryStream(DocxTestPackage.FromBody(DocxTestPackage.Paragraph("body"), parts), writable: false);
        RichTextDocument document = new DocxDocumentCodec().Read(stream).Document;

        Assert.Equal(shrinks, document.StyleDefaults.ShrinksJustifiedLines);
    }

    [Fact]
    public void A_Package_With_No_Compatibility_Mode_Does_Not_Shrink()
    {
        Assert.False(DocxTestPackage.ReadBody(DocxTestPackage.Paragraph("body")).Document.StyleDefaults.ShrinksJustifiedLines);
    }

    [Fact]
    public void Shrinking_Justified_Lines_Survives_A_Round_Trip()
    {
        RichTextDocument source = RichTextDocument.FromPlainText("body")
            .WithStyleDefaults(DocumentStyleDefaults.Default with { ShrinksJustifiedLines = true });

        byte[] bytes = DocxDocumentCodec.WriteToArray(source);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            using var settings = new StreamReader(archive.GetEntry("word/settings.xml")!.Open());
            Assert.Contains("w:name=\"compatibilityMode\"", settings.ReadToEnd());
        }

        using var stream = new MemoryStream(bytes, writable: false);
        Assert.True(new DocxDocumentCodec().Read(stream).Document.StyleDefaults.ShrinksJustifiedLines);
    }
}
