using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Documents.Model;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Resources;
using Xunit;

namespace Broiler.Documents.Pagination.Tests;

public sealed class SharedLayoutUnicodePriority3Tests
{
    private static LayoutResult Layout(RichTextDocument document, double width = 200, double height = 300)
    {
        using var images = new ImageStore();
        return new DocumentLayout(new LayoutSettings(), images).Layout(
            document,
            new DocumentPageBox(width, height, 10, 10, 10, 10));
    }

    #region 1. Non-Breaking Spaces in Pagination

    [Fact]
    public void NonBreakingSpace_DoesNotBreakAcrossLines()
    {
        // Set column width so that "One Two" fits easily on two lines if wrapped at space,
        // but "One\u00A0Two" stays together as an unbroken unit.
        // Content width = 100 - 20 = 80 points.
        var docBreakable = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("FirstLongerWord One Two")
        ]);
        var resultBreakable = Layout(docBreakable, width: 120, height: 200);
        var linesBreakable = resultBreakable.Pages[0].Lines;

        var docNbsp = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("FirstLongerWord One\u00A0Two")
        ]);
        var resultNbsp = Layout(docNbsp, width: 120, height: 200);
        var linesNbsp = resultNbsp.Pages[0].Lines;

        // In the breakable version, "One" and "Two" can separate across lines
        // In the non-breaking version, "One\u00A0Two" is a single unit
        string textNbspLine = string.Join("", linesNbsp[^1].Pieces.Select(p => p.Text));
        Assert.Contains("One\u00A0Two", textNbspLine);
    }

    [Fact]
    public void NarrowNonBreakingSpace_DoesNotBreakAcrossLines()
    {
        var docNnbsp = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("FirstLongerWord Item\u202F100")
        ]);
        var result = Layout(docNnbsp, width: 120, height: 200);
        var lines = result.Pages[0].Lines;

        string lastLineText = string.Join("", lines[^1].Pieces.Select(p => p.Text));
        Assert.Contains("Item\u202F100", lastLineText);
    }

    #endregion

    #region 2. Emergency Fitting Cluster Integrity (Surrogates & Combining Marks)

    [Fact]
    public void EmergencyFitting_SurrogatePairs_NeverSplitAcrossPieces()
    {
        // Create an overwide token with emoji surrogate pairs: 😀 (U+1F600), 🍎 (U+1F34E)
        // Set column width very narrow (50pt) so emergency fitting splits the word.
        string emojiWord = "LongWord🍎😀🍎😀🍎😀End";
        var doc = RichTextDocument.FromParagraphs([RichTextParagraph.Plain(emojiWord)]);

        var result = Layout(doc, width: 60, height: 300);
        var lines = result.Pages[0].Lines;

        Assert.True(lines.Count > 1, "Word should have been split across multiple lines");

        foreach (var line in lines)
        {
            foreach (var piece in line.Pieces)
            {
                // Verify no piece has dangling high or low surrogate half
                string pieceText = piece.Text;
                for (int i = 0; i < pieceText.Length; i++)
                {
                    if (char.IsHighSurrogate(pieceText[i]))
                    {
                        Assert.True(i + 1 < pieceText.Length, $"High surrogate at index {i} in piece '{pieceText}' missing low surrogate");
                        Assert.True(char.IsLowSurrogate(pieceText[i + 1]), $"Character following high surrogate is not low surrogate");
                        i++; // Skip low surrogate
                    }
                    else
                    {
                        Assert.False(char.IsLowSurrogate(pieceText[i]), $"Found isolated low surrogate at index {i} in piece '{pieceText}'");
                    }
                }
            }
        }
    }

    [Fact]
    public void EmergencyFitting_CombiningMarks_RemainAttachedToBaseCharacter()
    {
        // Word with combining acute accents: e\u0301
        string accentedWord = "A\u0301B\u0301C\u0301D\u0301E\u0301F\u0301G\u0301H\u0301";
        var doc = RichTextDocument.FromParagraphs([RichTextParagraph.Plain(accentedWord)]);

        var result = Layout(doc, width: 50, height: 300);
        var lines = result.Pages[0].Lines;

        Assert.True(lines.Count > 1, "Word should have been split across multiple lines");

        foreach (var line in lines)
        {
            foreach (var piece in line.Pieces)
            {
                // A piece must never begin with an unattached combining mark (\u0301)
                Assert.False(piece.Text.StartsWith('\u0301'), $"Piece started with unattached combining mark: '{piece.Text}'");
            }
        }
    }

    [Fact]
    public void EmergencyFitting_SingleIndivisibleCluster_TerminatesWithoutHanging()
    {
        // An emoji cluster on a column that is extremely narrow (e.g. 5pt)
        var doc = RichTextDocument.FromParagraphs([RichTextParagraph.Plain("😀")]);

        var result = Layout(doc, width: 15, height: 100);
        var page = Assert.Single(result.Pages);

        // Terminated cleanly and placed the cluster
        Assert.NotEmpty(page.Lines);
        string text = string.Join("", page.Lines[0].Pieces.Select(p => p.Text));
        Assert.Equal("😀", text);
    }

    #endregion

    #region 3. Cross-Style Words & Mandatory Breaks

    [Fact]
    public void CrossStyleWord_StaysInSingleWordToken()
    {
        // Paragraph with "super" (regular) + "cali" (bold) + "fragilistic" (italic)
        // With room on the line, it must NOT split between styles.
        var p1 = RichTextParagraph.Create("super", InlineStyle.Default);
        var p2 = RichTextParagraph.Create("cali", InlineStyle.Default with { Bold = true });
        var p3 = RichTextParagraph.Create("fragilistic", InlineStyle.Default with { Italic = true });
        var p = p1.Append(p2).Append(p3);

        var doc = RichTextDocument.FromParagraphs([p]);
        var result = Layout(doc, width: 300, height: 200);

        // Entire word fits on one line
        Assert.Single(result.Pages[0].Lines);
        Assert.Equal(3, result.Pages[0].Lines[0].Pieces.Count);
    }

    [Fact]
    public void MandatoryLineBreak_U2028_BreaksLineRegardlessOfRoom()
    {
        // Text with U+2028 forced line break
        var doc = RichTextDocument.FromParagraphs([
            RichTextParagraph.Plain("LineOne\u2028LineTwo")
        ]);

        var result = Layout(doc, width: 400, height: 200);
        var lines = result.Pages[0].Lines;

        Assert.Equal(2, lines.Count);
        Assert.Equal("LineOne", string.Join("", lines[0].Pieces.Select(p => p.Text)));
        Assert.Equal("LineTwo", string.Join("", lines[1].Pieces.Select(p => p.Text)));
    }

    #endregion
}
