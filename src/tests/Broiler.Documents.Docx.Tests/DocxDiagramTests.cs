using Broiler.Graphics.Color;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxDiagramTests
{
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main", D = "http://schemas.microsoft.com/office/drawing/2008/diagram";
    private const string DataType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    private const string CacheType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";
    private const string Theme = "<a:theme xmlns:a='" + A + "'><a:themeElements><a:clrScheme name='test'><a:accent1><a:srgbClr val='00FF00'/></a:accent1></a:clrScheme><a:fontScheme name='test'><a:majorFont><a:latin typeface='Major Font'/></a:majorFont><a:minorFont><a:latin typeface='Minor Font'/></a:minorFont></a:fontScheme></a:themeElements></a:theme>";
    private static string Relation(string id, string type, string target, bool external = false) => "<Relationship Id='" + id + "' Type='" + type + "' Target='" + target + "'" + (external ? " TargetMode='External'" : "") + "/>";
    private static string Relationships(string content) => "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" + content + "</Relationships>";
    private static string Model(string id) => "<dgm:dataModel xmlns:dgm='http://schemas.openxmlformats.org/drawingml/2006/diagram' xmlns:dsp='" + D + "'><dgm:extLst><dsp:dataModelExt relId='" + id + "'/></dgm:extLst></dgm:dataModel>";
    private static string Cache(string shapes, string transform = "") => "<dsp:drawing xmlns:dsp='" + D + "' xmlns:a='" + A + "'><dsp:spTree><dsp:grpSpPr>" + transform + "</dsp:grpSpPr>" + shapes + "</dsp:spTree></dsp:drawing>";
    private static string Transform(string x = "0", string y = "0", string width = "1270000", string height = "1270000", string extra = "") =>
        "<a:xfrm " + extra + "><a:off x='" + x + "' y='" + y + "'/><a:ext cx='" + width + "' cy='" + height + "'/></a:xfrm>";
    private static string Shape(string text = "node", string paint = "<a:solidFill><a:srgbClr val='FF0000'/></a:solidFill>", string geometry = "rect", string transform = "", string run = "", string paragraph = "", string frame = "", string style = "", string body = "") =>
        "<dsp:sp><dsp:spPr>" + (transform.Length > 0 ? transform : Transform()) + "<a:prstGeom prst='" + geometry + "'/>" + paint + "</dsp:spPr>" + style +
        "<dsp:txBody><a:bodyPr " + frame + "/><a:lstStyle/>" + (body.Length > 0 ? body : "<a:p><a:pPr>" + paragraph + "</a:pPr><a:r><a:rPr " + run + "/><a:t>" + text + "</a:t></a:r></a:p>") + "</dsp:txBody></dsp:sp>";
    private static string Drawing(string id = "data", bool inline = true) =>
        "<w:r><w:drawing><wp:" + (inline ? "inline" : "anchor") + " xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing'><wp:extent cx='1270000' cy='1270000'/><a:graphic xmlns:a='" + A + "'><a:graphicData uri='http://schemas.openxmlformats.org/drawingml/2006/diagram'><dgm:relIds xmlns:dgm='http://schemas.openxmlformats.org/drawingml/2006/diagram' r:dm='" + id + "'/></a:graphicData></a:graphic></wp:" + (inline ? "inline" : "anchor") + "></w:drawing></w:r>";
    private static DocumentReadResult Read(string shapes, string transform = "", string? body = null, Dictionary<string, string>? changes = null, DocumentReadOptions? options = null)
    {
        var parts = new Dictionary<string, string>
        {
            ["word/_rels/document.xml.rels"] = Relationships(Relation("data", DataType, "odd/data.xml") + Relation("saved", CacheType, "odd/saved.xml") + Relation("theme", DocxNamespaces.ThemeRelationship, "theme/theme1.xml")),
            ["word/odd/data.xml"] = Model("saved"), ["word/odd/saved.xml"] = Cache(shapes, transform), ["word/theme/theme1.xml"] = Theme,
        };
        if (changes is not null) foreach (var part in changes) parts[part.Key] = part.Value;
        body ??= "<w:p>" + Drawing() + "</w:p>";
        body += "<w:sectPr><w:pgSz w:w='12240' w:h='15840'/><w:pgMar w:left='1440' w:right='1440' w:top='1440' w:bottom='1440'/></w:sectPr>";
        using var stream = new MemoryStream(DocxTestPackage.FromBody(body, parts));
        return new DocxDocumentCodec().Read(stream, options);
    }

    [Fact]
    public void A_Nonconventional_Cache_Path_Is_Followed_Through_Its_Declared_Relationships()
    {
        Assert.Equal("node", Assert.Single(Read(Shape()).Document.Shapes).Paragraphs[0].Text);
    }

    [Fact]
    public void Each_Diagram_Resolves_Its_Own_Data_And_All_Cached_Siblings()
    {
        var changes = new Dictionary<string, string>
        {
            ["word/_rels/document.xml.rels"] = Relationships(Relation("data", DataType, "odd/data.xml") + Relation("saved", CacheType, "odd/saved.xml") + Relation("second", DataType, "other/data.xml") + Relation("otherCache", CacheType, "other/cache.xml")),
            ["word/other/data.xml"] = Model("otherCache"), ["word/other/cache.xml"] = Cache(Shape("c")),
        };
        var shapes = Read(Shape("a") + Shape("b"), body: "<w:p>" + Drawing() + "</w:p><w:p>" + Drawing("second") + "</w:p>", changes: changes).Document.Shapes;
        Assert.Equal(new[] { "a", "b", "c" }, shapes.Select(s => s.Paragraphs[0].Text));
        Assert.Equal(new[] { 0, 0, 1 }, shapes.Select(s => s.ParagraphIndex));
    }

    [Fact]
    public void Group_Child_Origins_And_Extents_Map_To_The_Outer_Diagram_Box()
    {
        string group = "<a:xfrm><a:off x='0' y='0'/><a:ext cx='1270000' cy='1270000'/><a:chOff x='100' y='200'/><a:chExt cx='2000' cy='1000'/></a:xfrm>";
        var shape = Assert.Single(Read(Shape(transform: Transform("1100", "700", "500", "250")), group).Document.Shapes);
        Assert.Equal(50, shape.OffsetX); Assert.Equal(50, shape.OffsetY); Assert.Equal(25, shape.Width); Assert.Equal(25, shape.Height);
    }

    [Fact]
    public void Nested_Groups_Apply_Each_Level_Of_The_Coordinate_Transform()
    {
        string nested = "<dsp:grpSp><dsp:grpSpPr>" + Transform("127000", "254000", "635000", "635000").Replace("</a:xfrm>", "<a:chOff x='0' y='0'/><a:chExt cx='1270000' cy='1270000'/></a:xfrm>") + "</dsp:grpSpPr>" + Shape(transform: Transform("254000", "0", "254000", "254000")) + "</dsp:grpSp>";
        var shape = Assert.Single(Read(nested).Document.Shapes);
        Assert.Equal(20, shape.OffsetX); Assert.Equal(20, shape.OffsetY); Assert.Equal(10, shape.Width);
    }

    [Fact]
    public void Ellipse_Geometry_Is_Retained() => Assert.Equal(ShapeGeometry.Ellipse, Assert.Single(Read(Shape(geometry: "ellipse")).Document.Shapes).Geometry);

    [Fact]
    public void Cached_Connectors_Retain_Line_Geometry_And_Flips()
    {
        string line = Shape(geometry: "line", transform: Transform(extra: "flipH='1'"), paint: "<a:noFill/><a:ln><a:solidFill><a:srgbClr val='000000'/></a:solidFill></a:ln>");
        Assert.Equal(ShapeGeometry.LineAscending, Assert.Single(Read(line).Document.Shapes).Geometry);
    }

    [Fact]
    public void Gradient_Stops_And_Theme_Colors_Reach_Cached_Shape_Paint()
    {
        string fill = "<a:gradFill><a:gsLst><a:gs pos='0'><a:schemeClr val='accent1'/></a:gs><a:gs pos='100000'><a:srgbClr val='0000FF'/></a:gs></a:gsLst><a:lin ang='5400000'/></a:gradFill>";
        var paint = Assert.Single(Read(Shape(paint: fill)).Document.Shapes).Fill!;
        Assert.Equal(BColor.FromArgb(0, 255, 0), paint.Start); Assert.Equal(BColor.FromArgb(0, 0, 255), paint.End); Assert.Equal(90, paint.AngleDegrees);
    }

    [Fact]
    public void Cached_Outlines_Retain_Color_And_Pen_Width()
    {
        var shape = Assert.Single(Read(Shape(paint: "<a:noFill/><a:ln w='38100'><a:solidFill><a:srgbClr val='0000FF'/></a:solidFill></a:ln>")).Document.Shapes);
        Assert.Equal(3, shape.OutlineWidth); Assert.Equal(BColor.FromArgb(0, 0, 255), shape.Outline);
    }

    [Fact]
    public void Cached_Paragraphs_Breaks_And_Field_Display_Text_Are_Retained_Inside_The_Shape()
    {
        string body = "<a:p><a:r><a:t>a</a:t></a:r><a:br/><a:fld id='field' type='slidenum'><a:t>b</a:t></a:fld></a:p><a:p><a:r><a:t>c</a:t></a:r></a:p>";
        var document = Read(Shape(body: body)).Document; var shape = Assert.Single(document.Shapes);
        Assert.Equal(new[] { "a\u2028b", "c" }, shape.Paragraphs.Select(p => p.Text)); Assert.Equal("", document.PlainText);
    }

    [Fact]
    public void Paragraph_Alignment_Indent_And_Spacing_Keep_Their_DrawingMl_Units()
    {
        string body = "<a:p><a:pPr algn='ctr' marL='127000' marR='63500' indent='-25400'><a:lnSpc><a:spcPts val='1800'/></a:lnSpc><a:spcBef><a:spcPts val='300'/></a:spcBef><a:spcAft><a:spcPts val='400'/></a:spcAft></a:pPr><a:r><a:rPr sz='1200'/><a:t>p</a:t></a:r></a:p>";
        var style = Assert.Single(Read(Shape(body: body)).Document.Shapes).Paragraphs[0].Style;
        Assert.Equal(TextAlignment.Center, style.Alignment); Assert.Equal(10, style.LeftIndent); Assert.Equal(5, style.RightIndent); Assert.Equal(-2, style.FirstLineIndent);
        Assert.Equal(3, style.SpacingBefore); Assert.Equal(4, style.SpacingAfter); Assert.Equal(18, style.LineHeight); Assert.Equal(LineHeightRule.Exact, style.LineHeightRule);
    }

    [Fact]
    public void Percentage_Spacing_Uses_The_Paragraph_Run_Size()
    {
        string body = "<a:p><a:pPr><a:lnSpc><a:spcPct val='120000'/></a:lnSpc><a:spcBef><a:spcPct val='50000'/></a:spcBef><a:spcAft><a:spcPct val='15000'/></a:spcAft></a:pPr><a:r><a:rPr sz='2000'/><a:t>p</a:t></a:r></a:p>";
        var style = Assert.Single(Read(Shape(body: body)).Document.Shapes).Paragraphs[0].Style;
        Assert.Equal(10, style.SpacingBefore); Assert.Equal(3, style.SpacingAfter); Assert.Equal(1.2f, style.LineSpacing);
    }

    [Fact]
    public void Latin_Fonts_Theme_Fonts_And_Hundredth_Point_Sizes_Are_Resolved()
    {
        string body = "<a:p><a:r><a:rPr sz='1250'><a:latin typeface='Literal Font'/></a:rPr><a:t>a</a:t></a:r><a:r><a:rPr sz='1800'><a:latin typeface='+mn-lt'/></a:rPr><a:t>b</a:t></a:r></a:p>";
        var p = Assert.Single(Read(Shape(body: body)).Document.Shapes).Paragraphs[0];
        Assert.Equal("Literal Font", p.StyleAt(0).FontFamily); Assert.Equal(12.5f, p.StyleAt(0).FontSize); Assert.Equal("Minor Font", p.StyleAt(1).FontFamily); Assert.Equal(18, p.StyleAt(1).FontSize);
    }

    [Fact]
    public void Run_Emphasis_Inherits_Defaults_And_Explicit_Off_Values_Clear_Them()
    {
        string body = "<a:p><a:pPr><a:defRPr b='1' i='1' u='sng' strike='sngStrike'/></a:pPr><a:r><a:t>a</a:t></a:r><a:r><a:rPr b='0' i='0' u='none' strike='noStrike'/><a:t>b</a:t></a:r></a:p>";
        var p = Assert.Single(Read(Shape(body: body)).Document.Shapes).Paragraphs[0]; var first = p.StyleAt(0); var second = p.StyleAt(1);
        Assert.True(first.Bold && first.Italic && first.Underline && first.Strikethrough); Assert.False(second.Bold || second.Italic || second.Underline || second.Strikethrough);
    }

    [Fact]
    public void Run_Colors_Resolve_Theme_Slots_And_Explicit_Paint_Overrides_Font_References()
    {
        string body = "<a:p><a:r><a:rPr><a:solidFill><a:schemeClr val='accent1'/></a:solidFill></a:rPr><a:t>a</a:t></a:r><a:r><a:t>b</a:t></a:r></a:p>";
        var p = Assert.Single(Read(Shape(body: body, style: "<dsp:style><a:fontRef idx='minor'><a:srgbClr val='0000FF'/></a:fontRef></dsp:style>")).Document.Shapes).Paragraphs[0];
        Assert.Equal(BColor.FromArgb(0, 255, 0), p.StyleAt(0).Foreground); Assert.Equal(BColor.FromArgb(0, 0, 255), p.StyleAt(1).Foreground);
    }

    [Fact]
    public void Text_Frame_Insets_And_Vertical_Anchor_Are_Preserved()
    {
        var shape = Assert.Single(Read(Shape(frame: "lIns='127000' tIns='25400' rIns='38100' bIns='50800' anchor='ctr'")).Document.Shapes);
        Assert.Equal(new TableCellPadding(10, 2, 3, 4), shape.TextPadding); Assert.Equal(CellVerticalAlignment.Center, shape.TextVerticalAlignment);
    }

    [Fact]
    public void A_Block_Inline_Diagram_Reserves_Its_Height_And_Paragraph_Alignment()
    {
        var document = Read(Shape(), body: "<w:p><w:pPr><w:jc w:val='center'/></w:pPr>" + Drawing() + "</w:p>").Document;
        Assert.Equal(100, document.Paragraphs[0].Style.LineHeight); Assert.Equal(LineHeightRule.Exact, document.Paragraphs[0].Style.LineHeightRule);
        Assert.Equal(184, Assert.Single(document.Shapes).OffsetX);
    }

    [Fact]
    public void Mixed_Inline_Text_And_An_Explicit_Exact_Line_Height_Are_Not_Overwritten()
    {
        var mixed = Read(Shape(), body: "<w:p><w:r><w:t>body</w:t></w:r>" + Drawing() + "</w:p>");
        Assert.Contains(mixed.Diagnostics, d => d.Code == "docx.diagram.inline"); Assert.NotEqual(100, mixed.Document.Paragraphs[0].Style.LineHeight);
        var exact = Read(Shape(), body: "<w:p><w:pPr><w:spacing w:lineRule='exact' w:line='400'/></w:pPr>" + Drawing() + "</w:p>").Document;
        Assert.Equal(20, exact.Paragraphs[0].Style.LineHeight);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void External_And_Mistyped_Cache_Relationships_Are_Not_Loaded(bool external)
    {
        var changes = new Dictionary<string, string> { ["word/_rels/document.xml.rels"] = Relationships(Relation("data", DataType, "odd/data.xml") + Relation("saved", external ? CacheType : "urn:wrong-type", "odd/saved.xml", external)) };
        var result = Read(Shape(), changes: changes); Assert.Empty(result.Document.Shapes); Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.cache");
    }

    [Fact]
    public void Malformed_Or_Missing_Caches_Do_Not_Drop_The_Following_Body_Text()
    {
        var result = Read(Shape(), body: "<w:p>" + Drawing() + "</w:p>" + DocxTestPackage.Paragraph("after"),
            changes: new() { ["word/odd/saved.xml"] = "<broken" });
        Assert.Empty(result.Document.Shapes); Assert.Contains("after", result.Document.PlainText); Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.cache");
    }

    [Fact]
    public void Cached_Text_And_Paint_Survive_Export_As_Ordinary_Shapes()
    {
        var source = Read(Shape("retained")).Document;
        using var stream = new MemoryStream(DocxDocumentCodec.WriteToArray(source)); var restored = new DocxDocumentCodec().Read(stream).Document;
        var a = Assert.Single(source.Shapes); var b = Assert.Single(restored.Shapes);
        Assert.Equal(a.Fill, b.Fill); Assert.Equal(a.OffsetX, b.OffsetX); Assert.Equal(a.Width, b.Width); Assert.Equal("retained", b.Paragraphs[0].Text);
        Assert.Equal(source.Paragraphs[0].Style.LineHeight, restored.Paragraphs[0].Style.LineHeight);
    }
    [Fact]
    public void A_Cache_Relationship_Local_To_The_Data_Part_Is_Resolved_Relative_To_That_Part()
    {
        var changes = new Dictionary<string, string>
        {
            ["word/_rels/document.xml.rels"] = Relationships(Relation("data", DataType, "odd/data.xml")),
            ["word/odd/_rels/data.xml.rels"] = Relationships(Relation("saved", CacheType, "../odd/saved.xml")),
        };
        Assert.Equal("local", Assert.Single(Read(Shape("local"), changes: changes).Document.Shapes).Paragraphs[0].Text);
    }

    [Fact]
    public void Diagram_Xml_Uses_The_Existing_Package_Part_Size_Limit()
    {
        var result = Read(Shape(new string('x', 6000)), options: new DocumentReadOptions(new DocumentLimits(maxBinBytes: 2048)));
        Assert.Empty(result.Document.Shapes); Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.cache");
    }

    [Fact]
    public void Nested_Groups_Respect_The_Read_Depth_And_Keep_Shallow_Siblings()
    {
        string nested = "<dsp:grpSp><dsp:grpSpPr/>" + Shape("deep") + "</dsp:grpSp>";
        var result = Read(Shape("shallow") + nested, options: new DocumentReadOptions(new DocumentLimits(maxGroupDepth: 1)));
        Assert.Equal("shallow", Assert.Single(result.Document.Shapes).Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.limit");
    }

    [Fact]
    public void Object_Count_Is_Bounded_Independently_Of_The_Number_Of_Body_Paragraphs()
    {
        var result = Read(Shape("first") + Shape("second"), options: new DocumentReadOptions(new DocumentLimits(maxParagraphCount: 1)));
        Assert.Equal("first", Assert.Single(result.Document.Shapes).Paragraphs[0].Text);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.limit");
    }

    [Fact]
    public void An_Empty_Cached_Diagram_Keeps_Its_Inline_Extent_Without_Inventing_Paint()
    {
        var result = Read(""); Assert.Empty(result.Document.Shapes); Assert.Equal(100, result.Document.Paragraphs[0].Style.LineHeight);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.empty");
    }

    [Fact]
    public void A_Quarter_Turn_Maps_A_Rectangular_Paint_Box_Around_Its_Center()
    {
        var result = Read(Shape(transform: Transform("0", "0", "1270000", "635000", "rot='5400000'")));
        var shape = Assert.Single(result.Document.Shapes);
        Assert.Equal(25, shape.OffsetX); Assert.Equal(-25, shape.OffsetY); Assert.Equal(50, shape.Width); Assert.Equal(100, shape.Height);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.transform");
    }

    [Fact]
    public void Repeated_Cache_References_Cannot_Expand_Beyond_The_Document_Budget()
    {
        string body = "<w:p>" + string.Concat(Enumerable.Repeat(Drawing(), 100)) + "</w:p>";
        var result = Read(Shape(new string('x', 1000)), body: body, options: new DocumentReadOptions(new DocumentLimits(maxDocumentBytes: 100000)));
        Assert.InRange(result.Document.Shapes.Count, 1, 99);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.limit");
    }

    [Fact]
    public void Cached_Root_Display_Extents_Are_Bounded_As_Well_As_The_Inline_Extent()
    {
        string transform = "<a:xfrm><a:off x='0' y='0'/><a:ext cx='12700000000' cy='1270000'/><a:chOff x='0' y='0'/><a:chExt cx='1270000' cy='1270000'/></a:xfrm>";
        var result = Read(Shape(), transform); Assert.Empty(result.Document.Shapes);
        Assert.Contains(result.Diagnostics, d => d.Code == "docx.diagram.extent");
    }

}
