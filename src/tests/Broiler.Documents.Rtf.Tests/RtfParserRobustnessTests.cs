using System.Text;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Rtf.Tests;

public sealed class RtfParserRobustnessTests : ParserRobustnessTests
{
    protected override DocumentCodec CreateCodec() => new RtfDocumentCodec();
    protected override byte[] Seed() => Encoding.UTF8.GetBytes("{\\rtf1\\ansi{\\fonttbl{\\f0 Serif;}} Alpha\\par {\\b Beta} {\\field{\\*\\fldinst HYPERLINK \"https://example.test\"}{\\fldrslt link}}}");
}

