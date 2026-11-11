namespace Broiler.Documents.Odt.Tests;

/// <summary>
/// Regressions from office conformance issue #234, ODT documents broilerdoc
/// drew blank where LibreOffice inked them: form check boxes and legacy form
/// fields (<c>ww8export/data/checkbox_control.odt</c>,
/// <c>globalfilter/data/checkbox_form_field.odt</c>,
/// <c>globalfilter/data/dropdown_form_field.odt</c>), a connector
/// (<c>ooxmlexport/data/tdf99810-lost-arrow.odt</c>), a text box sized by its
/// text (<c>unowriter/data/textboxInColumn2.fodt</c>), a first-page footer in
/// LibreOffice's extension namespace
/// (<c>uiwriter/data/textfield-getanchor-gettext-in-footer.odt</c>) - and one
/// inked where LibreOffice hides the paragraph
/// (<c>mailmerge/data/empty-db-field.fodt</c>).
/// </summary>
public sealed class OdtIssue234ControlTests
{
    private const string FieldNamespace = "xmlns:field=\"urn:openoffice:names:experimental:ooo-ms-interop:xmlns:field:1.0\"";
    private const string FormNamespace = "xmlns:form=\"urn:oasis:names:tc:opendocument:xmlns:form:1.0\"";

    private static RichTextDocument Read(string body, string automaticStyles = "", string? styles = null,
        IReadOnlyDictionary<string, string>? extraParts = null)
    {
        using var stream = new MemoryStream(
            OdtTestPackage.FromBody(body, automaticStyles, styles, extraParts: extraParts), writable: false);
        return new OdtDocumentCodec().Read(stream).Document;
    }

    private static string Fieldmark(string type, string parameters = "") =>
        "<text:p><field:fieldmark " + FieldNamespace + " text:name=\"f\" field:type=\"vnd.oasis.opendocument.field." + type + "\">" +
        parameters + "</field:fieldmark></text:p>";

    private static string Param(string name, string value) =>
        "<field:param field:name=\"" + name + "\" field:value=\"" + value + "\"/>";

    [Fact]
    public void A_Check_Box_Field_Is_Drawn_As_A_Ballot_Box_Crossed_When_Checked()
    {
        RichTextDocument document = Read(
            Fieldmark("FORMCHECKBOX") + Fieldmark("FORMCHECKBOX", Param("Checkbox_Checked", "true")));

        Assert.Equal("☐", document.Paragraphs[0].Text);
        Assert.Equal("☒", document.Paragraphs[1].Text);
        Assert.Equal("DejaVu Sans", document.Paragraphs[0].StyleAt(0).FontFamily);
    }

    [Fact]
    public void A_Drop_Down_Field_Shows_Its_Selected_Entry_And_Nothing_Without_Entries()
    {
        RichTextDocument document = Read(
            Fieldmark("FORMDROPDOWN") +
            Fieldmark("FORMDROPDOWN", Param("Dropdown_ListEntry", "1000") + Param("Dropdown_ListEntry", "2000") +
                Param("Dropdown_Selected", "1")) +
            Fieldmark("FORMDROPDOWN", Param("Dropdown_ListEntry", "first")));

        Assert.Equal("", document.Paragraphs[0].Text);
        Assert.Equal("2000", document.Paragraphs[1].Text);
        Assert.Equal("first", document.Paragraphs[2].Text);
    }

    [Theory]
    [InlineData("", "☐ Box")]
    [InlineData(" form:current-state=\"checked\"", "☒ Box")]
    public void A_Check_Box_Control_Draws_Its_Box_Before_Its_Label_With_No_Frame(string state, string expected)
    {
        string body =
            "<office:forms " + FormNamespace + "><form:form form:name=\"Form\">" +
            "<form:checkbox form:name=\"c\" xml:id=\"control1\" form:id=\"control1\" form:label=\"Box\"" + state + "/>" +
            "</form:form></office:forms>" +
            "<text:p><draw:control " + FormNamespace + " text:anchor-type=\"paragraph\" draw:style-name=\"gr1\" " +
            "svg:width=\"4cm\" svg:height=\"0.9cm\" svg:x=\"1cm\" svg:y=\"1cm\" draw:control=\"control1\"/></text:p>";
        string styles =
            "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties " +
            "svg:stroke-color=\"#3465a4\" draw:fill-color=\"#729fcf\" draw:textarea-vertical-align=\"middle\"/></style:style>";

        DocumentShape shape = Assert.Single(Read(body, styles).Shapes);

        Assert.Equal(expected, Assert.Single(shape.Paragraphs).Text);
        Assert.True(shape.Outline.IsEmpty);
        Assert.Null(shape.Fill);
    }

    [Fact]
    public void A_Connector_Is_Drawn_As_The_Line_Between_Its_Ends()
    {
        string body =
            "<text:p><draw:connector text:anchor-type=\"paragraph\" draw:style-name=\"gr1\" draw:type=\"line\" " +
            "svg:x1=\"1cm\" svg:y1=\"3cm\" svg:x2=\"4cm\" svg:y2=\"2cm\"><text:p/></draw:connector></text:p>";
        string styles =
            "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties " +
            "draw:stroke=\"solid\" svg:stroke-color=\"#000000\"/></style:style>";

        DocumentShape shape = Assert.Single(Read(body, styles).Shapes);

        Assert.Equal(ShapeGeometry.LineAscending, shape.Geometry);
        Assert.Equal(3 * 72 / 2.54, shape.Width, 3);
        Assert.Equal(1 * 72 / 2.54, shape.Height, 3);
    }

    [Fact]
    public void A_Custom_Shape_Text_Box_With_No_Height_Grows_Down_To_Its_Text()
    {
        string body =
            "<text:p><draw:custom-shape text:anchor-type=\"char\" draw:style-name=\"gr1\" svg:width=\"30mm\" svg:x=\"6mm\" svg:y=\"5mm\">" +
            "<text:p>hello</text:p><draw:enhanced-geometry draw:type=\"ooxml-rect\"/></draw:custom-shape></text:p>";
        string styles =
            "<style:style style:name=\"gr1\" style:family=\"graphic\"><style:graphic-properties draw:stroke=\"solid\" " +
            "svg:stroke-color=\"#000000\" draw:fill=\"none\" draw:auto-grow-height=\"true\"/></style:style>";

        DocumentShape shape = Assert.Single(Read(body, styles).Shapes);

        Assert.True(shape.GrowsToText);
        Assert.False(shape.FitsText);
        Assert.Equal(30 * 72 / 25.4, shape.Width, 3);
        Assert.Equal("hello", Assert.Single(shape.Paragraphs).Text);
    }

    [Fact]
    public void A_First_Page_Footer_In_The_LibreOffice_Namespace_Is_Read()
    {
        string styles =
            "<office:automatic-styles><style:page-layout style:name=\"pm1\"><style:page-layout-properties " +
            "fo:page-width=\"595pt\" fo:page-height=\"842pt\" fo:margin=\"56pt\"/></style:page-layout></office:automatic-styles>" +
            "<office:master-styles><style:master-page style:name=\"Standard\" style:page-layout-name=\"pm1\" " +
            "xmlns:loext=\"urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0\">" +
            "<style:footer><text:p/></style:footer>" +
            "<loext:footer-first><text:p>first footer</text:p></loext:footer-first>" +
            "</style:master-page></office:master-styles>";

        RichTextDocument document = Read(OdtTestPackage.Paragraph("body"), styles: styles);

        Assert.Equal("first footer", Assert.Single(document.RunningContent.EffectiveFooter(PageSelection.First)).Text);
    }

    private static Dictionary<string, string> Settings(string value) => new(StringComparer.Ordinal)
    {
        ["settings.xml"] =
            "<office:document-settings xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:config=\"urn:oasis:names:tc:opendocument:xmlns:config:1.0\"><office:settings>" +
            "<config:config-item-set config:name=\"ooo:configuration-settings\">" +
            "<config:config-item config:name=\"EmptyDbFieldHidesPara\" config:type=\"boolean\">" + value +
            "</config:config-item></config:config-item-set></office:settings></office:document-settings>",
    };

    private const string DatabaseBody =
        "<text:p>Text <text:database-display text:table-name=\"\" text:column-name=\"\" text:database-name=\"\"/></text:p>" +
        "<text:p>Name <text:database-display text:table-name=\"t\" text:column-name=\"n\" text:database-name=\"d\">Ada</text:database-display></text:p>";

    [Fact]
    public void An_Empty_Database_Field_Hides_Its_Paragraph_When_The_Settings_Say_So()
    {
        RichTextDocument document = Read(DatabaseBody, extraParts: Settings("true"));

        Assert.Equal("Name Ada", Assert.Single(document.Paragraphs).Text);
    }

    [Fact]
    public void An_Empty_Database_Field_Keeps_Its_Paragraph_Otherwise()
    {
        Assert.Equal(2, Read(DatabaseBody, extraParts: Settings("false")).ParagraphCount);
        Assert.Equal(2, Read(DatabaseBody).ParagraphCount);
    }
}
