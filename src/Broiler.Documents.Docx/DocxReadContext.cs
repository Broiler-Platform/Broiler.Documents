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
internal sealed record DocxReadContext(
    DocxRelationships Relationships,
    DocxNumbering Numbering,
    DocxStyles Styles,
    DocxImageLoader Images,
    DocxDocumentBuilder Builder,
    PageGeometry? Page = null,
    RunningBand Band = RunningBand.Body);

