using System.Text;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Odt.Tests;

public sealed class OdtParserRobustnessTests : PackageParserRobustnessTests
{
    protected override DocumentCodec CreateCodec() => new OdtDocumentCodec();
    protected override string ContentEntry => "content.xml";
}

