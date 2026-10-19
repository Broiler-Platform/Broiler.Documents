using System.Text;
using Broiler.Documents.TestSupport;

namespace Broiler.Documents.Csv.Tests;

public sealed class CsvParserRobustnessTests : ParserRobustnessTests
{
    protected override DocumentCodec CreateCodec() => new CsvDocumentCodec();
    protected override byte[] Seed() => Encoding.UTF8.GetBytes("Alpha,Value\r\n\"quoted, field\",\"two\nlines\"\r\n\"say \"\"hello\"\"\",42\r\n");
}

