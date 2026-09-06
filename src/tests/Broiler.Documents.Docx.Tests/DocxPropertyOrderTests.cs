using System.IO.Compression;
using System.Xml.Linq;
using Broiler.Graphics;

namespace Broiler.Documents.Docx.Tests;

/// <summary>
/// Covers the order the writer puts a paragraph's and a run's properties in.
/// <c>CT_PPr</c> and <c>CT_RPr</c> are both <c>xsd:sequence</c>, so a property
/// is not merely present or absent - it has exactly one legal position, and
/// Word does not shrug at an out-of-order property container the way it does at
/// an element it has never heard of. It can refuse the whole file, which costs
/// the document rather than the one property.
/// <para>
/// The two schema sequences are written out below in full rather than as the
/// short list this writer happens to emit today, and each test walks whatever
/// children are actually present and asserts their positions ascend. That is
/// deliberately weaker than asserting an exact list: a property added to the
/// writer later must not fail a test that had no way to know about it, and
/// because the tables are complete it will be checked in its right place the
/// day it is added rather than needing the test updated first. A child the
/// tables do not name fails loudly, because it is either a misspelling or an
/// element of a schema these tables no longer describe.
/// </para>
/// </summary>
public sealed class DocxPropertyOrderTests
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// The <c>CT_PPr</c> sequence entire, in the order ECMA-376 Part 1 section
    /// 17.3.1.26 fixes: the <c>CT_PPrBase</c> children first, then the three
    /// <c>CT_PPr</c> adds on the end.
    /// </summary>
    private static readonly string[] ParagraphPropertySequence =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr",
        "widowControl", "numPr", "suppressLineNumbers", "pBdr", "shd", "tabs",
        "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct",
        "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents",
        "suppressOverlap", "jc", "textDirection", "textAlignment",
        "textboxTightWrap", "outlineLvl", "divId", "cnfStyle",
        "rPr", "sectPr", "pPrChange",
    ];

    /// <summary>
    /// The <c>CT_RPr</c> sequence entire, in the order ECMA-376 Part 1 section
    /// 17.3.2.28 fixes. Worth reading once for how little it resembles the
    /// order a person names formatting in: <c>w:u</c> is 27th and sits nowhere
    /// near the <c>w:b</c> and <c>w:i</c> it is always spoken beside.
    /// </summary>
    private static readonly string[] RunPropertySequence =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps",
        "strike", "dstrike", "outline", "shadow", "emboss", "imprint",
        "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing",
        "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect",
        "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang",
        "eastAsianLayout", "specVanish", "oMath", "rPrChange",
    ];

    [Fact(Timeout = 600000)]
    public void A_Paragraph_Carrying_Every_Property_Writes_Them_In_Ct_PPr_Order()
    {
        // Every paragraph property this writer emits, on one paragraph, because
        // the defect only shows where two of them meet: a centred paragraph is
        // fine alone and so is an indented one, and it was the heading that is
        // both which left here out of sequence.
        XElement properties = ParagraphProperties(WriteParagraph(
            InlineStyle.Default,
            ParagraphStyle.Default with
            {
                PageBreakBefore = true,
                ListKind = ListKind.Numbered,
                IndentLevel = 2,
                LineSpacing = 1.5f,
                SpacingBefore = 6f,
                SpacingAfter = 12f,
                Alignment = TextAlignment.Center,
            }));

        AssertPresent(properties, "pageBreakBefore", "numPr", "spacing", "ind", "jc");
        AssertSequenceOrder(properties, ParagraphPropertySequence, "CT_PPr");
    }

    // Both capitalizations, because the model makes them exclusive and only one
    // of the two can be in any single written paragraph. They are adjacent in
    // the sequence, so neither placement is harder than the other - but neither
    // is covered by a test that only ever writes the other one.
    [Theory(Timeout = 600000)]
    [InlineData(TextCapitalization.AllCaps, "caps")]
    [InlineData(TextCapitalization.SmallCaps, "smallCaps")]
    public void A_Run_Carrying_Every_Property_Writes_Them_In_Ct_RPr_Order(
        TextCapitalization capitalization, string expectedElement)
    {
        XElement properties = RunProperties(WriteParagraph(
            InlineStyle.Default with
            {
                FontFamily = "Georgia",
                FontSize = 11f,
                Bold = true,
                Italic = true,
                Underline = true,
                Strikethrough = true,
                Capitalization = capitalization,
                Foreground = BColor.Blue,
                Background = BColor.FromArgb(240, 240, 240),
            },
            ParagraphStyle.Default));

        AssertPresent(
            properties,
            "rFonts", "b", "i", expectedElement, "strike", "color", "sz", "u", "shd");
        AssertSequenceOrder(properties, RunPropertySequence, "CT_RPr");
    }

    /// <summary>
    /// Fails unless every named child is there. The order assertion alone would
    /// pass an empty container and a container missing the very property whose
    /// placement is in question, so it is worth nothing without this beside it.
    /// </summary>
    private static void AssertPresent(XElement properties, params string[] names)
    {
        foreach (string name in names)
        {
            Assert.True(
                properties.Element(W + name) is not null,
                $"w:{properties.Name.LocalName} was expected to carry w:{name}, and carries " +
                $"only: {string.Join(", ", properties.Elements().Select(child => "w:" + child.Name.LocalName))}.");
        }
    }

    /// <summary>
    /// Fails unless the children present ascend through the schema sequence.
    /// What is present is what is checked, so a property the writer does not
    /// emit yet costs nothing here.
    /// </summary>
    private static void AssertSequenceOrder(
        XElement properties, string[] schemaOrder, string typeName)
    {
        var written = new List<(string Name, int Position)>();
        foreach (XElement child in properties.Elements())
        {
            int index = Array.IndexOf(schemaOrder, child.Name.LocalName);
            Assert.True(
                index >= 0,
                $"w:{child.Name.LocalName} is not in this test's copy of the {typeName} " +
                "sequence, so its legal position is unknown. Either the name is wrong or " +
                "the table needs the element adding at its position in the standard.");

            written.Add((child.Name.LocalName, index + 1));
        }

        for (int i = 1; i < written.Count; i++)
        {
            Assert.True(
                written[i].Position > written[i - 1].Position,
                $"{typeName} is a sequence, but w:{written[i - 1].Name} (position " +
                $"{written[i - 1].Position}) is written before w:{written[i].Name} " +
                $"(position {written[i].Position}).");
        }
    }

    /// <summary>One paragraph of one run, written to a package.</summary>
    private static byte[] WriteParagraph(InlineStyle style, ParagraphStyle paragraphStyle) =>
        DocxDocumentCodec.WriteToArray(RichTextDocument.FromParagraphs(
            [RichTextParagraph.Create("Chapter Two", style, paragraphStyle)]));

    private static XElement ParagraphProperties(byte[] package) =>
        Body(package).Descendants(W + "pPr").First();

    private static XElement RunProperties(byte[] package) =>
        Body(package).Descendants(W + "rPr").First();

    private static XElement Body(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read);
        using Stream stream = archive.GetEntry("word/document.xml")!.Open();
        using var reader = new StreamReader(stream);
        return XDocument.Parse(reader.ReadToEnd()).Root!;
    }
}
