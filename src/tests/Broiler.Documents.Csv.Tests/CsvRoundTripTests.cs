using static Broiler.Documents.Csv.Tests.CsvFixtures;

namespace Broiler.Documents.Csv.Tests;

/// <summary>
/// What survives a trip through CSV. A grid of text must come back exactly, in
/// both directions; that is the whole of what the format promises.
/// </summary>
public sealed class CsvRoundTripTests
{
    public static TheoryData<string[][]> Grids => new()
    {
        new[] { new[] { "a", "b" }, new[] { "c", "d" } },
        new[] { new[] { "" } },
        new[] { new[] { "a" }, new[] { "" }, new[] { "" } },
        new[] { new[] { "", "", "" }, new[] { "x", "", "" } },
        new[] { new[] { "with,comma", "with;semicolon", "with\ttab" } },
        new[] { new[] { "\"quoted\"", "say \"hi\"", "\"" } },
        new[] { new[] { "line 1\nline 2", "\n", "trailing\n" } },
        new[] { new[] { " leading", "trailing ", " both " } },
        new[] { new[] { "Grüße", "€", "日本語", "emoji 🙂" } },
        new[] { new[] { "=SUM(A1:A2)", "-5", "007" } },
    };

    [Theory]
    [MemberData(nameof(Grids))]
    public void A_Grid_Written_And_Read_Back_Is_The_Same_Grid(string[][] grid)
    {
        string csv = Write(Table(grid));

        DocumentReadResult result = Read(csv, new CsvReadOptions(delimiter: ','));

        Assert.Equal(grid, Grid(result.Document));
        Assert.Equal(DocumentResultStatus.Success, result.Status);
    }

    [Theory]
    [InlineData(';')]
    [InlineData('\t')]
    public void A_Grid_Survives_Any_Delimiter_And_Detection_Finds_It(char delimiter)
    {
        string[][] grid = [["Name", "Preis"], ["Apfel", "1,50"], ["Birne", "2,00"]];

        string csv = Write(Table(grid), new CsvWriteOptions(delimiter: delimiter));

        Assert.Equal(grid, Grid(Read(csv).Document));
    }

    [Theory]
    [InlineData("a,b\r\nc,d\r\n")]
    [InlineData("\"x,y\",\"say \"\"hi\"\"\"\r\n")]
    [InlineData(",\r\n,\r\n")]
    [InlineData("single\r\n\r\ncolumn\r\n")]
    public void Canonical_Csv_Read_And_Written_Is_The_Same_Bytes(string csv)
    {
        Assert.Equal(csv, Write(Read(csv).Document));
    }

    [Fact]
    public void A_Byte_Order_Mark_Survives_When_Asked_For_Again()
    {
        (byte[] bytes, _) = WriteBytes(Table(["a"]), new CsvWriteOptions(byteOrderMark: true));

        DocumentReadResult result = Read(bytes);

        Assert.Equal([["a"]], Grid(result.Document));
        Assert.Empty(result.Diagnostics);
    }
}
