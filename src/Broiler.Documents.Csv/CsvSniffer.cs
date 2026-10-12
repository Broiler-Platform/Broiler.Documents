using System.Collections.Generic;

namespace Broiler.Documents.Csv;

/// <summary>
/// Works out from the text what a CSV file does not say about itself: which
/// character separates its fields, and whether it is laid out as a grid at all.
/// </summary>
internal static class CsvSniffer
{
    /// <summary>How many records the detection looks at.</summary>
    internal const int SampleRecords = 20;

    /// <summary>
    /// The delimiter that splits the first records most consistently. Comma when
    /// none splits them at all, which is a file of one column.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each candidate is tried as the separator over the first
    /// <see cref="SampleRecords"/> records, quotes respected, and the field count
    /// most of them share is taken as its width. A candidate whose width is one
    /// field is out. Of the rest, the one under which the larger share of records
    /// has that width wins; then the wider; then the rarer character
    /// (<see cref="CsvDialect.Candidates"/>).
    /// </para>
    /// <para>
    /// The common width rather than the first record's, because exports often
    /// open with a title line above the data, and a title has one field under
    /// every candidate. Blank lines are passed over for the same reason.
    /// </para>
    /// </remarks>
    public static char DetectDelimiter(string text)
    {
        char best = ',';
        int bestShare = 0;
        int bestTotal = 1;
        int bestWidth = 0;

        foreach (char candidate in CsvDialect.Candidates)
        {
            List<int> counts = FieldCounts(text, candidate, SampleRecords, out _);
            (int width, int share) = CommonWidth(counts);
            if (width < 2)
                continue;

            // share / total against the best ratio, without division.
            long ours = (long)share * bestTotal;
            long theirs = (long)bestShare * counts.Count;
            if (ours > theirs || (ours == theirs && width > bestWidth))
            {
                best = candidate;
                bestShare = share;
                bestTotal = counts.Count;
                bestWidth = width;
            }
        }

        return best;
    }

    /// <summary>The field count most records have, the wider on a tie, and how many have it.</summary>
    private static (int Width, int Share) CommonWidth(List<int> counts)
    {
        var frequencies = new Dictionary<int, int>();
        foreach (int count in counts)
            frequencies[count] = frequencies.GetValueOrDefault(count) + 1;

        (int width, int share) = (0, 0);
        foreach ((int count, int frequency) in frequencies)
        {
            if (frequency > share || (frequency == share && count > width))
                (width, share) = (count, frequency);
        }

        return (width, share);
    }

    /// <summary>
    /// True when the text reads as a grid: at least two records, every one with
    /// the same number of fields, and more than one of them.
    /// </summary>
    public static bool LooksTabular(string text)
    {
        char delimiter = DetectDelimiter(text);
        List<int> counts = FieldCounts(text, delimiter, SampleRecords, out bool unterminated);
        if (unterminated || counts.Count < 2 || counts[0] < 2)
            return false;

        foreach (int count in counts)
        {
            if (count != counts[0])
                return false;
        }

        return true;
    }

    private static List<int> FieldCounts(string text, char delimiter, int records, out bool unterminated)
    {
        var parser = new CsvParser(text, delimiter);
        var counts = new List<int>();
        while (counts.Count < records && parser.ReadRecord() is List<string> record)
        {
            if (record.Count == 1 && record[0].Length == 0)
                continue;

            counts.Add(record.Count);
        }

        unterminated = parser.SawUnterminatedQuote;
        return counts;
    }
}
