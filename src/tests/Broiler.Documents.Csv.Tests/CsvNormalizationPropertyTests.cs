namespace Broiler.Documents.Csv.Tests;

public sealed class CsvNormalizationPropertyTests
{
    [Theory]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData('\t')]
    public void Generated_Grids_Preserve_Values_And_Reach_Stable_Output(char delimiter)
    {
        // Authored values exercise quoting, multiline cells, Unicode and values
        // that would change if formula escaping were accidentally enabled.
        string[] pieces = ["", "plain", "éλ", "😀", ",", ";", "\t", "\n", "\"", "=1+1", "-5", " "];
        var random = new Random(32452843);
        for (int trial = 0; trial < 100; trial++)
        {
            string[][] grid = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, 3)
                .Select(_ => string.Concat(Enumerable.Range(0, random.Next(1, 6))
                    .Select(_ => pieces[random.Next(pieces.Length)]))).ToArray()).ToArray();
            RichTextDocument document = CsvFixtures.Table(grid);
            var write = new CsvWriteOptions(delimiter: delimiter);
            byte[] first = CsvFixtures.WriteBytes(document, write).Bytes;
            DocumentReadResult read = CsvFixtures.Read(first, new CsvReadOptions(delimiter: delimiter));
            Assert.Equal(DocumentResultStatus.Success, read.Status);
            string[][] actual = CsvFixtures.Grid(read.Document);
            Assert.Equal(grid.Length, actual.Length);
            for (int row = 0; row < grid.Length; row++)
                Assert.Equal(grid[row], actual[row]);
            Assert.Equal(first, CsvFixtures.WriteBytes(read.Document, write).Bytes);
        }
    }
}
