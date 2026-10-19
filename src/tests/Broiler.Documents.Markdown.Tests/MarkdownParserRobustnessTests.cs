using System.Text;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Markdown.Tests;

public sealed class MarkdownParserRobustnessTests : ParserRobustnessTests
{
    protected override DocumentCodec CreateCodec() => new MarkdownDocumentCodec();
    protected override byte[] Seed() => Encoding.UTF8.GetBytes("# Alpha\n\n**Beta** and [link](https://example.test)\n\n- item\n\n```\ncode\n```");
}

