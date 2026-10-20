using Broiler.Documents.Model;

namespace Broiler.Documents.Docx;

/// <summary>Which running band a part is being read for, or the body.</summary>
internal enum RunningBand
{
    Body,
    Header,
    Footer,
}

/// <summary>Everything a read needs to turn one element into document content.</summary>
/// <param name="TableStyle">
/// The table style governing the cell being read, null outside a table. A cell's
/// paragraphs inherit from it before their own paragraph style; see
/// <see cref="DocxStyles.ParagraphProperties"/>.
/// </param>
/// <param name="Sections">
/// The body's sections, followed as the read goes, or null for a part that is
/// not the body's flow - a header, a footer, a text box.
/// </param>
internal sealed record DocxReadContext(DocxRelationships Relationships, DocxNumbering Numbering, DocxStyles Styles,
    DocxImageLoader Images, DocxDocumentBuilder Builder, PageGeometry? Page = null, RunningBand Band = RunningBand.Body,
    string? TableStyle = null, DocxSections? Sections = null);

