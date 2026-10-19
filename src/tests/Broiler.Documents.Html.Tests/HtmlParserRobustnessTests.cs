using System.Text;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Html.Tests;

public sealed class HtmlParserRobustnessTests : ParserRobustnessTests
{
    protected override DocumentCodec CreateCodec() => new HtmlDocumentCodec();
    protected override byte[] Seed() => Encoding.UTF8.GetBytes("<html><body><p>Alpha &amp; café</p><ul><li><b>Beta</b></li></ul><a href=\"https://example.test\">link</a></body></html>");
}

