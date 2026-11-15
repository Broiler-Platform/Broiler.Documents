using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf.Tests;

/// <summary>
/// Unit tests for RTF style sheet interpretation (<c>\stylesheet</c>, <c>\sN</c>, <c>\csN</c>, <c>\sbasedonN</c>).
/// </summary>
public sealed class RtfStyleSheetTests
{
    private static DocumentReadResult ReadResult(string rtf) =>
        RtfReader.Read(Encoding.Latin1.GetBytes(rtf));

    private static RichTextDocument Read(string rtf) => ReadResult(rtf).Document;

    [Fact]
    public void Paragraph_Style_Formatting_Is_Applied_To_Referencing_Paragraph()
    {
        string rtf = @"{\rtf1\ansi
{\fonttbl{\f0 Arial;}{\f1 Georgia;}}
{\stylesheet
{\s0\ql\f0\fs24 Normal;}
{\s1\qc\sb240\sa120\b\fs36\f1 Heading 1;}
}
\pard\plain\s1 Centered Heading\par
\pard\plain Normal body text\par}";

        RichTextDocument document = Read(rtf);

        Assert.Equal(2, document.ParagraphCount);

        // writerfilter reads a style's own formatting as what the paragraph
        // repeats, and gives the paragraph the RTF default for what it does
        // not (RtfStyleOwnFormatting). soffice 24.2 sets this heading left
        // aligned and unspaced in 12 point regular Times New Roman: the
        // paragraph names the style but repeats none of it.
        RichTextParagraph p1 = document.Paragraphs[0];
        Assert.Equal("Centered Heading", p1.Text);
        Assert.Equal(TextAlignment.Left, p1.Style.Alignment);
        Assert.Equal(0f, p1.Style.SpacingBefore);
        Assert.Equal(0f, p1.Style.SpacingAfter);
        Assert.False(p1.StyleAt(0).Bold);
        Assert.Equal(12f, p1.StyleAt(0).FontSize ?? document.StyleDefaults.FontSizePoints);
        Assert.Equal("Times New Roman", document.StyleDefaults.FontFamilyOf(p1.StyleAt(0)));

        RichTextParagraph p2 = document.Paragraphs[1];
        Assert.Equal("Normal body text", p2.Text);
        Assert.Equal(TextAlignment.Left, p2.Style.Alignment);
        Assert.False(p2.StyleAt(0).Bold);
        Assert.Equal(12f, p2.StyleAt(0).FontSize ?? document.StyleDefaults.FontSizePoints);
        Assert.Equal("Times New Roman", document.StyleDefaults.FontFamilyOf(p2.StyleAt(0)));
    }

    [Fact]
    public void Paragraph_Style_Formatting_The_Paragraph_Repeats_Is_Applied()
    {
        // As Word writes RTF: the paragraph repeats its style's formatting.
        string rtf = @"{\rtf1\ansi
{\fonttbl{\f0 Arial;}{\f1 Georgia;}}
{\stylesheet
{\s0\ql\f0\fs24 Normal;}
{\s1\qc\sb240\sa120\b\fs36\f1 Heading 1;}
}
\pard\plain\s1\qc\sb240\sa120\b\fs36\f1 Centered Heading\par}";

        RichTextParagraph p1 = Read(rtf).Paragraphs[0];
        Assert.Equal(TextAlignment.Center, p1.Style.Alignment);
        Assert.Equal(12f, p1.Style.SpacingBefore);
        Assert.Equal(6f, p1.Style.SpacingAfter);
        Assert.True(p1.StyleAt(0).Bold);
        Assert.Equal(18f, p1.StyleAt(0).FontSize);
        Assert.Equal("Georgia", p1.StyleAt(0).FontFamily);
    }

    [Fact]
    public void Style_0_Normal_Applies_Defaults_To_Document_Paragraphs()
    {
        string rtf = @"{\rtf1\ansi
{\fonttbl{\f0 Helvetica;}}
{\stylesheet
{\s0\qr\sa180\f0\fs28 Normal;}
}
Default styled paragraph\par}";

        RichTextDocument document = Read(rtf);

        // Text that names no style is in style 0, and like any style's own
        // formatting, Normal's is put back to the RTF defaults where the
        // paragraph does not repeat it (tdf118047.rtf; soffice 24.2 sets this
        // paragraph left aligned and unspaced in 12 point Times New Roman).
        RichTextParagraph p = document.Paragraphs[0];
        Assert.Equal("Default styled paragraph", p.Text);
        Assert.Equal(TextAlignment.Left, p.Style.Alignment);
        Assert.Equal(0f, p.Style.SpacingAfter);
        Assert.Equal("Times New Roman", document.StyleDefaults.FontFamilyOf(p.StyleAt(0)));
        Assert.Equal(12f, p.StyleAt(0).FontSize ?? document.StyleDefaults.FontSizePoints);
    }

    [Fact]
    public void Character_Style_Is_Applied_To_Run_And_Additive()
    {
        string rtf = @"{\rtf1\ansi
{\fonttbl{\f0 Calibri;}}
{\colortbl;\red255\green0\blue0;}
{\stylesheet
{\s0\f0\fs24 Normal;}
{\*\cs10\additive\b\i\cf1 Strong Emphasis;}
}
\pard\plain Regular {\cs10\b\i\cf1 emphasized} text\par}";

        // The run repeats its style's formatting, as Word writes it; one that
        // does not is set without it (RtfIssue249Tests).
        RichTextDocument document = Read(rtf);
        RichTextParagraph p = document.Paragraphs[0];

        Assert.Equal(3, p.Runs.Count);
        Assert.Equal("Regular ", p.Text.Substring(0, p.Runs[0].Length));
        Assert.False(p.Runs[0].Style.Bold);
        Assert.False(p.Runs[0].Style.Italic);

        Assert.Equal("emphasized", p.Text.Substring(p.Runs[0].Length, p.Runs[1].Length));
        Assert.True(p.Runs[1].Style.Bold);
        Assert.True(p.Runs[1].Style.Italic);
        Assert.Equal(BColor.FromArgb(255, 0, 0), p.Runs[1].Style.Foreground);

        Assert.Equal(" text", p.Text.Substring(p.Runs[0].Length + p.Runs[1].Length, p.Runs[2].Length));
        Assert.False(p.Runs[2].Style.Bold);
        Assert.False(p.Runs[2].Style.Italic);
    }

    [Fact]
    public void Paragraph_Style_Inheritance_Via_Sbasedon()
    {
        string rtf = @"{\rtf1\ansi
{\fonttbl{\f0 Arial;}}
{\stylesheet
{\s0\ql\li720\sa120\f0\fs24 Normal;}
{\s1\sbasedon0\qc\b\fs36 Heading 1;}
}
\pard\plain\s1\qc\b\fs36 Heading text\par}";

        RichTextDocument document = Read(rtf);
        RichTextParagraph p = document.Paragraphs[0];

        // Inherited from s0, and not put back to defaults: only what s1
        // states itself is (soffice 24.2: a style stating nothing of its own
        // shows its parent's formatting).
        Assert.Equal("Arial", p.StyleAt(0).FontFamily);
        Assert.Equal(6f, p.Style.SpacingAfter);
        Assert.Equal(36f, p.Style.LeftIndentIn());

        // Overridden by s1, and repeated by the paragraph:
        Assert.Equal(TextAlignment.Center, p.Style.Alignment);
        Assert.True(p.StyleAt(0).Bold);
        Assert.Equal(18f, p.StyleAt(0).FontSize);
    }

    [Fact]
    public void Multi_Level_Style_Inheritance()
    {
        string rtf = @"{\rtf1\ansi
{\fonttbl{\f0 Arial;}{\f1 Times New Roman;}}
{\stylesheet
{\s0\ql\f0\fs24\sa100 Normal;}
{\s1\sbasedon0\f1\fs32 SubStyle;}
{\s2\sbasedon1\qc\b Heading;}
}
\pard\plain\s2\qc\b Deeply Inherited\par}";

        RichTextDocument document = Read(rtf);
        RichTextParagraph p = document.Paragraphs[0];

        // Inherited from s0 via s1:
        Assert.Equal(5f, p.Style.SpacingAfter);
        // Inherited from s1:
        Assert.Equal("Times New Roman", p.StyleAt(0).FontFamily);
        Assert.Equal(16f, p.StyleAt(0).FontSize);
        // Set on s2:
        Assert.Equal(TextAlignment.Center, p.Style.Alignment);
        Assert.True(p.StyleAt(0).Bold);
    }

    [Fact]
    public void Circular_Style_Inheritance_Does_Not_Crash()
    {
        string rtf = @"{\rtf1\ansi
{\stylesheet
{\s1\sbasedon2\b A;}
{\s2\sbasedon1\i B;}
}
\pard\plain\s1 Safe\par}";

        RichTextDocument document = Read(rtf);
        RichTextParagraph p = document.Paragraphs[0];

        // A's own \b is not repeated, so it is not applied; B's \i, which A
        // inherits around the cycle, is.
        Assert.Equal("Safe", p.Text);
        Assert.False(p.StyleAt(0).Bold);
        Assert.True(p.StyleAt(0).Italic);
    }

    [Fact]
    public void Character_Style_Inheritance_Via_Sbasedon()
    {
        string rtf = @"{\rtf1\ansi
{\fonttbl{\f0 Arial;}{\f1 Courier;}}
{\stylesheet
{\s0\f0 Normal;}
{\cs10\b\f1 BaseChar;}
{\cs11\sbasedon10\i DerivedChar;}
}
\pard\plain Text {\cs11\i styled} end\par}";

        RichTextDocument document = Read(rtf);
        RichTextParagraph p = document.Paragraphs[0];

        // Bold comes from the base style; the derived style's own italic is
        // what the run repeats.
        InlineStyle styled = p.Runs[1].Style;
        Assert.Equal("Courier", styled.FontFamily);
        Assert.True(styled.Bold);
        Assert.True(styled.Italic);
    }

    [Fact]
    public void Pard_Resets_Paragraph_Style_To_Normal_Defaults()
    {
        string rtf = @"{\rtf1\ansi
{\stylesheet
{\s0\ql Normal;}
{\s1\qr\sa200 Heading;}
}
\pard\plain\s1\qr\sa200 Right\par
\pard\plain Left\par}";

        RichTextDocument document = Read(rtf);

        Assert.Equal(TextAlignment.Right, document.Paragraphs[0].Style.Alignment);
        Assert.Equal(10f, document.Paragraphs[0].Style.SpacingAfter);

        Assert.Equal(TextAlignment.Left, document.Paragraphs[1].Style.Alignment);
        Assert.Equal(0f, document.Paragraphs[1].Style.SpacingAfter);
    }

    [Fact]
    public void Plain_Resets_Character_Formatting_To_Paragraph_Style_Defaults()
    {
        string rtf = @"{\rtf1\ansi
{\stylesheet
{\s0 Normal;}
{\s1\b\fs32 Heading;}
}
\pard\s1\plain\b\fs32 BoldText \b0 PlainText \plain BoldAgain\par}";

        RichTextDocument document = Read(rtf);
        RichTextParagraph p = document.Paragraphs[0];

        Assert.True(p.StyleAt(0).Bold);
        Assert.Equal(16f, p.StyleAt(0).FontSize);

        Assert.False(p.StyleAt(10).Bold);

        // \plain puts the run back to its paragraph style's formatting, which
        // for formatting the style states itself is the RTF default: soffice
        // 24.2 sets all three words in 12 point regular text.
        Assert.False(p.StyleAt(22).Bold);
        Assert.Equal(12f, p.StyleAt(22).FontSize ?? document.StyleDefaults.FontSizePoints);
    }

    [Fact]
    public void Direct_Formatting_Overrides_Style_Formatting()
    {
        string rtf = @"{\rtf1\ansi
{\stylesheet
{\s0 Normal;}
{\s1\qc\b\fs24 Styled;}
}
\pard\plain\s1\qr\b0\fs40 Custom\par}";

        RichTextDocument document = Read(rtf);
        RichTextParagraph p = document.Paragraphs[0];

        Assert.Equal(TextAlignment.Right, p.Style.Alignment);
        Assert.False(p.StyleAt(0).Bold);
        Assert.Equal(20f, p.StyleAt(0).FontSize);
    }

    [Fact]
    public void Malformed_Or_Empty_Stylesheet_Is_Handled_Safely()
    {
        string rtf = @"{\rtf1\ansi
{\stylesheet}
Body text\par}";

        RichTextDocument document = Read(rtf);
        Assert.Equal("Body text", document.PlainText);
    }

    [Fact]
    public void Undefined_Style_Reference_Does_Not_Throw()
    {
        string rtf = @"{\rtf1\ansi
{\stylesheet{\s0 Normal;}}
\pard\plain\s99 Body {\cs88 text}\par}";

        RichTextDocument document = Read(rtf);
        Assert.Equal("Body text", document.PlainText);
    }

    [Fact]
    public void Style_Names_With_Aliases_And_Unicode_Are_Extracted()
    {
        string rtf = @"{\rtf1\ansi
{\stylesheet
{\s1\qc Heading 1,h1;}
{\s2\qr \'c9tiquette,tag;}
}
\pard\plain\s2 Styled text\par}";

        RichTextDocument document = Read(rtf);
        Assert.Equal(TextAlignment.Left, document.Paragraphs[0].Style.Alignment);
        Assert.Equal(TextAlignment.Right, Read(rtf.Replace(@"\s2 Styled", @"\s2\qr Styled", StringComparison.Ordinal))
            .Paragraphs[0].Style.Alignment);
    }
}
