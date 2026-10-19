using System.Text;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Docx.Tests;

public sealed class DocxParserRobustnessTests : PackageParserRobustnessTests
{
    protected override DocumentCodec CreateCodec() => new DocxDocumentCodec();
    protected override string ContentEntry => "word/document.xml";
}

