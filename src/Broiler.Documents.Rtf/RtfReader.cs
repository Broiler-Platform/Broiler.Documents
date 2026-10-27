using System.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics.Color;

namespace Broiler.Documents.Rtf;

/// <summary>
/// Reads an RTF byte stream into a <see cref="RichTextDocument"/> by interpreting
/// the token stream from <see cref="RtfTokenizer"/>. Implements the first-release
/// subset of ADR 0005 (inline + paragraph formatting, <c>\fonttbl</c>/<c>\colortbl</c>,
/// <c>\'hh</c>/<c>\uN</c>/<c>\ucN</c>, hyperlink fields); unsupported destinations are
/// skipped and lossy skips are reported as diagnostics. The reader is total:
/// malformed input yields a best-effort document rather than an exception.
/// </summary>
public static class RtfReader
{
    public static DocumentReadResult Read(ReadOnlyMemory<byte> content, DocumentReadOptions? options = null)
    {
        DocumentReadOptions effective = options ?? DocumentReadOptions.Default;
        RtfTokenizeResult tokenized = RtfTokenizer.Tokenize(content, effective.Limits);
        var diagnostics = tokenized.Diagnostics.ToList();
        var tokens = RtfShapeGroups.Expand(tokenized.Tokens, effective.Limits.MaxGroupDepth, diagnostics);
        return new Worker(effective, diagnostics).Run(tokens);
    }

    private struct State
    {
        public InlineStyle Char;
        public ParagraphStyle Para;
        public RtfDestination Dest;
        public int ShapeOwnerDepth;
        public RtfLegacyDrawing? LegacyDrawing;
        public ShapeScope? Shape;

        /// <summary>
        /// Where a field's result belongs: the destination the field was sitting
        /// in when it opened. It is part of the state RTF pushes and pops with its
        /// braces, so a field inside a footer keeps the footer while the group
        /// that holds it is open and gives it back when the group closes.
        /// </summary>
        public RtfDestination FieldHost;
        public FieldFrame? Field;
        public int UnicodeSkip;
        public int CodePage;
        public int DocumentCodePage;
        public int LineSpacing;
        public bool MultipleLineSpacing;

        /// <summary>
        /// <c>\intbl</c>: the paragraph is in a table. A paragraph property, so
        /// <c>\pard</c> clears it and a group gives it back when it closes.
        /// </summary>
        public bool InTable;

        /// <summary>
        /// The frame the paragraph's position words put it in, if they make one.
        /// A paragraph property, so <c>\pard</c> clears it.
        /// </summary>
        public RtfFrame Frame;

        /// <summary>
        /// Inside <c>{\*\background ...}</c>: the shape there is the page's
        /// background, not a shape on it.
        /// </summary>
        public bool InBackground;
    }

    private sealed class ShapeScope
    {
        public bool HasInstructions;
    }

    private sealed class FieldFrame
    {
        public StringBuilder Instruction { get; } = new();
        public string? Link;
        public string? RubyBase;
        public int Characters;
    }

    private sealed class Worker
    {
        private readonly List<DocumentDiagnostic> _diagnostics = [];
        private readonly RtfColorTable _colors = new();
        private readonly RtfFontTable _fonts = new();
        private int? _defaultFont;

        // Whether runs of spaces are laid out wider. LibreOffice assumes so of
        // an RTF document unless it says otherwise, in one of the three ways
        // HandleBodyControlWord and WidensSpaceRuns look for.
        private bool _widensSpaceRuns = true;
        private bool _sawGenerator;
        private readonly StringBuilder _generator = new();
        private readonly Stack<State> _stack = new();
        private readonly Accumulator _builder;
        private readonly Dictionary<RtfDestination, Accumulator> _running = [];
        // An omitted page is still a page: LibreOffice imports it as Letter
        // with one-inch margins. Leaving these at zero discarded margin-only
        // documents and made the renderer substitute its own paper size.
        private int _paperWidth = 12240;
        private int _paperHeight = 15840;
        private int _marginLeft = 1440;
        private int _marginRight = 1440;
        private int _marginTop = 1440;
        private int _marginBottom = 1440;
        private int _headerDistance = 720;
        private int _footerDistance = 720;

        // The page the first section states. A section's \pgwsxn and the rest
        // are its own page and override the document's \paperw and the rest,
        // which RTF 1.9.1 gives as their defaults; null is a value the section
        // did not state.
        private int? _sectionWidth;
        private int? _sectionHeight;
        private int? _sectionLeft;
        private int? _sectionRight;
        private int? _sectionTop;
        private int? _sectionBottom;
        private bool _pastFirstSection;
        private bool _bodySectionPending;
        private bool _bodySectionHasProperties;
        private BodySectionBreak _bodySectionBreak = BodySectionBreak.Page;
        private enum BodySectionBreak { Continuous, Page, Column }
        private readonly RtfTableBuilder _table;
        private readonly List<DocumentTable> _tables = [];
        private int _columns = 1;
        private int _columnGap = 720;
        private readonly StringBuilder _shapePropertyName = new();
        private readonly StringBuilder _shapePropertyValue = new();
        private readonly List<DocumentShape> _shapes = [];
        private Accumulator? _shapeText;

        // The note being read: its text, whether \ftnalt made it an endnote,
        // and the character formatting where it was cited, which its citation
        // takes.
        private Accumulator? _noteText;
        private bool _noteIsEndnote;
        private InlineStyle _noteCitationStyle;

        // The paragraphs of the frame being read, and the frame they share.
        // Finished into a shape when a paragraph outside it arrives.
        private Accumulator? _frameText;
        private RtfFrame _frameKey;
        private int _shapeLeft;
        private int _shapeTop;
        private int _shapeRight;
        private int _shapeBottom;
        private int _shapeZ;
        private bool _shapeZStated;
        private int _shapeAlignX, _shapeAlignY;
        private readonly Dictionary<string, double> _shapeWrapDistances = new(StringComparer.Ordinal);

        // What a shape's \shptop is measured from: 0 the top margin, 1 the page,
        // 2 the paragraph - posrelv's numbering, which \shpbymargin, \shpbypage
        // and \shpbypara state too.
        private int _shapeVerticalFrame = 2;

        // What a shape's \shpleft is measured from: \shpbxpage, or else the
        // margin or column, which the model measures from already.
        private bool _shapeLeftFromPage;
        private bool _shapeFitsText;
        private BColor _shapeFillStart = BColor.Empty;
        private BColor _shapeFillEnd = BColor.Empty;
        private BColor _shapeLineColor = BColor.Empty;
        private double _shapeFillAngle;
        private bool _shapeHasFill;
        private bool _shapeGradient;
        private bool _shapeHasLine = true;

        // The shape's shapeType: 1 a rectangle, which Word takes when none is
        // given, 20 a line.
        private long _shapeType = 1;
        private double _shapeLineWidth = 0.75;
        private bool _shapeFlipVertical;
        private bool _shapeInBackground;
        private double _shapeFillFocus;
        private bool? _shapeFillEnabled;
        private bool _shapeFlipHorizontal;
        private bool _shapeBehindText;
        private double _shapeFillOpacity = 1, _shapeBackOpacity = 1, _shapeLineOpacity = 1;
        private TableCellPadding _shapePadding;
        private CellVerticalAlignment _shapeAlignment;
        private ShapeWrap _shapeWrap;
        private WrapSide _shapeWrapSide = WrapSide.Largest;
        private double _shapeWrapDistance;
        private ShapeFill? _pageBackground;
        private bool _viewBackground;
        private readonly int _maxParagraphs;

        // Buffered same-style body text (flushed when the style changes or the paragraph ends).
        private readonly StringBuilder _pending = new();
        private InlineStyle _pendingStyle;
        private bool _hasPending;

        // Table/field parsing scratch.
        private readonly StringBuilder _fontName = new();
        private int _fontIndex;
        private int _fontCharset;
        private int _r, _g, _b;
        private bool _colorSeen;

        private int _pendingUnicodeSkip;
        private bool _sawStar;
        private bool _reportedCodePage;
        private bool _reportedFrameAlignment;
        private bool _reportedSlantedLine;
        private bool _reportedEmbedded;
        private bool _reportedListTable;
        private bool _titlePage;
        private bool _reportedEmptyPage;
        private readonly bool _embeddedDecodingRequested;

        private State _state;

        public Worker(DocumentReadOptions options, IReadOnlyList<DocumentDiagnostic> tokenizerDiagnostics)
        {
            _diagnostics.AddRange(tokenizerDiagnostics);
            // Reading a member announced for removal (ADR 0014), suppressed here
            // rather than project-wide so the next deprecation still warns. This
            // call is what escalates the embedded-object note to
            // document.capability.not-composed when a caller asked for decoding
            // and the document really carries an object; it goes when the member
            // does, and the note stays at its unescalated severity.
#pragma warning disable CS0618 // Type or member is obsolete
            _embeddedDecodingRequested = options.DecodeEmbeddedObjects;
#pragma warning restore CS0618
            _builder = new Accumulator(options.Limits.MaxParagraphCount);
            _table = new RtfTableBuilder(_colors);
            _maxParagraphs = options.Limits.MaxParagraphCount;
            _state = new State
            {
                Char = InlineStyle.Default,
                Para = ParagraphStyle.Default,
                Dest = RtfDestination.Normal,
                FieldHost = RtfDestination.Normal,
                UnicodeSkip = 1,
                CodePage = options.DefaultCodePage,
                DocumentCodePage = options.DefaultCodePage,
            };
        }

        public DocumentReadResult Run(IReadOnlyList<RtfToken> tokens)
        {
            int next = 0;
            while (next < tokens.Count)
            {
                RtfToken token = tokens[next++];
                switch (token.Type)
                {
                    case RtfTokenType.GroupStart:
                        _sawStar = false;
                        // A scope delimiter ends Unicode fallback data before
                        // the delimiter; the next group's text is real text.
                        _pendingUnicodeSkip = 0;
                        // Pending text belongs to the destination that produced it.
                        // Flushing lazily was fine while everything landed in one
                        // accumulator; now a header's text would be attributed to
                        // whatever destination happened to be current at flush time.
                        FlushPending();
                        _stack.Push(_state);
                        break;
                    case RtfTokenType.GroupEnd:
                        // The document is the {\rtf1 ...} group and ends with it.
                        // Whatever follows its closing brace is not part of it, and
                        // Word and LibreOffice both stop there. Reading on turned
                        // the bytes after the end into body text: a file whose last
                        // section had been repeated past the brace came out with
                        // that section's footer, and the picture data beside it,
                        // once for every copy.
                        if (HandleGroupEnd())
                        {
                            ReportTrailingContent(tokens, next);
                            next = tokens.Count;
                        }

                        break;
                    case RtfTokenType.ControlWord:
                        HandleControlWord(token);
                        break;
                    case RtfTokenType.ControlSymbol:
                        HandleControlSymbol(token.Symbol);
                        break;
                    case RtfTokenType.HexByte:
                        HandleByte((byte)token.Parameter);
                        break;
                    case RtfTokenType.Text:
                        HandleText(token.Text);
                        break;
                }
            }

            FlushPending();
            FinishFrame();
            FinishTable();
            if (_bodySectionPending)
            {
                if (_bodySectionHasProperties)
                    ApplyBodySectionBreak();
                else
                    _bodySectionPending = false;
                _builder.EndParagraph(_state.Para);
            }
            // A final frame can belong to an otherwise empty body paragraph.
            // Keep that anchor even when preceding body paragraphs already exist.
            if (_shapes.Any(shape => !shape.IsPageAnchored && shape.ParagraphIndex == _builder.ParagraphCount))
                _builder.EndParagraph(_state.Para);
            RichTextDocument document = _builder.Build(_state.Para)
                .WithStyleDefaults(DocumentStyleDefaults.Default with
                {
                    FontFamily = _defaultFont is int font ? _fonts.GetName(font) : null,
                    WidensSpaceRuns = WidensSpaceRuns(),
                })
                .WithRunningContent(BuildRunningContent())
                .WithPageGeometry(BuildPageGeometry())
                .WithPageBackground(_viewBackground ? _pageBackground : null);
            if (_shapes.Count > 0)
                document = document.WithShapes(_shapes);
            if (_tables.Count > 0)
                document = document.WithTables(_tables);
            if (_builder.LimitHit)
                _diagnostics.Add(DocumentDiagnostic.Warning("rtf.paragraphs", "Document exceeded MaxParagraphCount; extra paragraphs were dropped."));
            if (_builder.PageBreakPending)
                ReportEmptyPage();

            return new DocumentReadResult(document, _diagnostics, DocumentReadResult.StatusFrom(_diagnostics));
        }

        /// <summary>
        /// Closes the current group, and says whether the group it closed was the
        /// root one. A closing brace with no group open closes nothing.
        /// </summary>
        private bool HandleGroupEnd()
        {
            _sawStar = false;
            _pendingUnicodeSkip = 0;
            State closing = _state;

            if (closing.Dest == RtfDestination.FieldInstruction && closing.Field is FieldFrame instruction)
            {
                instruction.Link = ExtractHyperlink(instruction.Instruction.ToString());
                instruction.RubyBase = ExtractRubyBase(instruction.Instruction.ToString());
            }
            else if (closing.Dest == RtfDestination.FontTable && _fontName.Length > 0)
                CommitFont();
            else if (closing.Dest == RtfDestination.Field && closing.Field is { Characters: 0, RubyBase: string ruby } &&
                (_stack.Count == 0 || _stack.Peek().Field != closing.Field))
            {
                _state.Dest = closing.FieldHost;
                AppendBody(ruby);
                FlushPending();
                _state.Dest = closing.Dest;
                if (!_diagnostics.Any(diagnostic => diagnostic.Code == "rtf.field.ruby"))
                    _diagnostics.Add(DocumentDiagnostic.Warning("rtf.field.ruby", "An empty-result ruby equation field was read as its base text; phonetic annotation layout is not represented."));
            }

            FlushPending();
            // The root group can close without a final \par. Commit its body
            // text before popping the paragraph properties back to defaults.
            if (_stack.Count == 1 && closing.Dest == RtfDestination.Normal)
                _builder.EndPendingParagraph(closing.Para);
            if (closing.Dest == RtfDestination.ShapeProperty)
                ApplyShapeProperty();
            else if (closing.Dest == RtfDestination.Shape && closing.ShapeOwnerDepth == _stack.Count)
                FinishShape();

            if (closing.Dest == RtfDestination.LegacyDrawing && closing.LegacyDrawing is { } drawing && drawing.OwnerDepth == _stack.Count)
            {
                DocumentShape? shape = drawing.Build(_builder.ParagraphCount,
                    (_sectionLeft ?? _marginLeft) / 20d, (_sectionTop ?? _marginTop) / 20d, _diagnostics);
                if (shape is not null) _shapes.Add(shape);
            }

            if (_stack.Count == 0)
                return false;

            _state = _stack.Pop();
            if (closing.Dest == RtfDestination.Field && closing.Field is not null && _state.Field is not null && closing.Field != _state.Field)
                _state.Field.Characters += closing.Field.Characters;
            if (closing.Dest == RtfDestination.Note && _state.Dest != RtfDestination.Note)
                FinishNote(closing.Para);

            return _stack.Count == 0;
        }

        private bool _noteLabelSeen;

        /// <summary>
        /// Closes the <c>\footnote</c> group just ended: its paragraphs become a
        /// note, and the note's citation one character where the group stood.
        /// </summary>
        /// <remarks>
        /// The citation goes where the group is, not where <c>\chftn</c> is:
        /// Word writes the label as <c>{\super\chftn}</c> just before the group,
        /// and a reader that has the note draws the label itself. A note's text
        /// opens with its label again and the space after it, and both are left
        /// out, as the DOCX reader leaves out <c>w:footnoteRef</c>.
        /// </remarks>
        private void FinishNote(ParagraphStyle finalStyle)
        {
            Accumulator? text = _noteText;
            _noteText = null;
            if (text is null)
                return;

            IReadOnlyList<RichTextParagraph> paragraphs = text.Build(finalStyle).Paragraphs;
            if (_noteLabelSeen && paragraphs.Count > 0)
            {
                RichTextParagraph first = paragraphs[0];
                int count = 0;
                while (count < first.Length && first.Text[count] is ' ' or '\t')
                    count++;
                if (count > 0)
                    paragraphs = [first.RemoveRange(0, count), .. paragraphs.Skip(1)];
            }

            _noteLabelSeen = false;
            var note = new DocumentNote(_noteIsEndnote ? NoteKind.Endnote : NoteKind.Footnote, paragraphs);
            FlushPending();
            Active.Append(DocumentNote.PlaceholderText, _noteCitationStyle with { Note = note, Image = null, LinkHref = null });
        }

        /// <summary>
        /// Says once that the stream went on past the root group's closing brace.
        /// Blank text and further closing braces are not worth a word: a file that
        /// ends in a newline, a NUL or one brace too many has nothing after its
        /// document that anybody wrote.
        /// </summary>
        private void ReportTrailingContent(IReadOnlyList<RtfToken> tokens, int from)
        {
            for (int i = from; i < tokens.Count; i++)
            {
                RtfToken token = tokens[i];
                if (token.Type == RtfTokenType.GroupEnd ||
                    (token.Type == RtfTokenType.Text && IsBlank(token.Text)))
                {
                    continue;
                }

                _diagnostics.Add(DocumentDiagnostic.Info(
                    "rtf.trailing",
                    "Content after the document's closing brace is not part of the document and was ignored."));
                return;
            }
        }

        private static bool IsBlank(string text)
        {
            foreach (char c in text)
            {
                if (c is not ('\0' or ' ' or '\t' or '\f'))
                    return false;
            }

            return true;
        }

        private void HandleControlWord(RtfToken token)
        {
            bool star = _sawStar;
            _sawStar = false;
            string kw = token.Keyword;

            // A control word (including its argument) counts as one fallback
            // character and must not apply formatting or emit content.
            if (_pendingUnicodeSkip > 0)
            {
                _pendingUnicodeSkip--;
                return;
            }

            // List definitions are opaque, including nested destinations. A
            // field, header or font table inside one must not escape into the
            // document or alter the tables used to interpret its body.
            if (_state.Dest == RtfDestination.ListTable)
                return;

            // So is every other destination this reader skips. Skipping a group
            // means skipping the groups inside it too, and this used to stop one
            // level down: a footnote's hyperlink set its own destination and its
            // result landed in the body beside the footnote mark, and a header
            // inside a stylesheet left the stylesheet to become the page's header.
            // A picture is still reported, because the report is about what the
            // document lost, and a picture inside a skipped \*\shppict is a
            // picture this reader did not import.
            if (_state.Dest == RtfDestination.Skip)
            {
                if (kw is "pict" or "object")
                    ReportEmbedded();
                return;
            }

            if (_state.Dest == RtfDestination.LegacyDrawing)
            {
                // Unknown ignorable subgroups remain opaque inside a drawing too.
                if (star || kw is "pict" or "object")
                    _state.Dest = RtfDestination.Skip;
                else
                    _state.LegacyDrawing?.Read(kw, token.Parameter, token.HasParameter);
                return;
            }

            // A note's text is the note's: a header, a font or colour table or a
            // shape stated inside it would escape into the document, so each is
            // skipped there with everything inside it, as a skipped destination
            // skips them.
            if (_state.Dest == RtfDestination.Note &&
                kw is "header" or "headerf" or "headerl" or "headerr" or
                    "footer" or "footerf" or "footerl" or "footerr" or
                    "fonttbl" or "colortbl" or "shp" or "do" or "listtable" or "listoverridetable")
            {
                _state.Dest = RtfDestination.Skip;
                return;
            }

            // Destination-setting keywords take effect in every destination the
            // reader walks rather than skips.
            switch (kw)
            {
                case "do":
                    if (_state.Dest != RtfDestination.Normal)
                    {
                        _state.Dest = RtfDestination.Skip;
                        return;
                    }
                    _state.Dest = RtfDestination.LegacyDrawing;
                    _state.LegacyDrawing = new RtfLegacyDrawing(_stack.Count);
                    return;
                case "shprslt" when _state.Shape?.HasInstructions == true:
                    _state.Dest = RtfDestination.Skip;
                    return;
                case "fonttbl":
                    _state.Dest = RtfDestination.FontTable;
                    _fontName.Clear();
                    return;
                case "colortbl":
                    _state.Dest = RtfDestination.ColorTable;
                    ResetColor();
                    return;
                case "field":
                    // Remembered before the destination is overwritten. A PAGE
                    // field in a footer used to hand its result to the body: the
                    // result destination replaced the footer rather than sitting
                    // inside it, so a letterhead's cached page number arrived at
                    // the head of the letter with the word beside it correctly
                    // skipped.
                    _state.FieldHost = _state.Dest == RtfDestination.FieldResult ? _state.FieldHost : _state.Dest;
                    _state.Field = new FieldFrame();
                    _state.Dest = RtfDestination.Field;
                    return;
                case "fldinst":
                    _state.Dest = RtfDestination.FieldInstruction;
                    _state.Field?.Instruction.Clear();
                    return;
                case "fldrslt":
                    _state.Dest = RtfDestination.FieldResult;
                    if (_state.Field?.Link is string link)
                        _state.Char = _state.Char with { LinkHref = link };
                    return;
                case "pict":
                case "object":
                    _state.Dest = RtfDestination.Skip;
                    ReportEmbedded();
                    return;
                // \titlepg is what makes \headerf and \footerf mean anything,
                // and what makes a band the first page does not state empty there
                // rather than the default one - the same fact w:titlePg carries in
                // DOCX and a first master page carries in ODF.
                case "titlepg": _titlePage = true; return;
                case "header": _state.Dest = RtfDestination.Header; return;
                case "headerf": _state.Dest = RtfDestination.HeaderFirst; return;
                case "headerl": _state.Dest = RtfDestination.HeaderEven; return;
                // \headerr is the right-hand, odd page - which is the one every
                // page gets in a document that does not distinguish them.
                case "headerr": _state.Dest = RtfDestination.Header; return;
                case "footer": _state.Dest = RtfDestination.Footer; return;
                case "footerf": _state.Dest = RtfDestination.FooterFirst; return;
                case "footerl": _state.Dest = RtfDestination.FooterEven; return;
                case "footerr": _state.Dest = RtfDestination.Footer; return;

                case "listtable":
                case "listoverridetable":
                    _state.Dest = RtfDestination.ListTable;
                    if (!_reportedListTable)
                    {
                        _diagnostics.Add(DocumentDiagnostic.Warning(
                            "rtf.list",
                            "List tables were detected and skipped; list numbering and marker semantics are not imported."));
                        _reportedListTable = true;
                    }
                    return;

                case "generator":
                    _state.Dest = RtfDestination.Generator;
                    _sawGenerator = true;
                    return;
                case "footnote":
                    // A note is read where the body, a table cell or a frame
                    // cites it. One in a header, a shape or another note has
                    // nowhere to be cited from, and is skipped as before.
                    if (_state.Dest == RtfDestination.Normal && _noteText is null)
                    {
                        _state.Dest = RtfDestination.Note;
                        _noteText = new Accumulator(_maxParagraphs);
                        _noteIsEndnote = false;
                        _noteCitationStyle = _stack.Count > 0 ? _stack.Peek().Char : _state.Char;
                        return;
                    }

                    _state.Dest = RtfDestination.Skip;
                    return;
                case "ftnalt" when _state.Dest == RtfDestination.Note:
                    _noteIsEndnote = true;
                    return;
                case "chftn" when _state.Dest == RtfDestination.Note:
                    // The note's text repeats its label here. It is drawn from
                    // the citation, so the text starts after it.
                    FlushPending();
                    _noteLabelSeen = true;
                    return;
                case "revtbl":
                    _state.Dest = RtfDestination.RevisionTable;
                    return;
                case "info":
                case "stylesheet":
                case "annotation":
                case "colorschememapping":
                case "latentstyles":
                case "datastore":
                case "themedata":
                case "pntext":
                    _state.Dest = RtfDestination.Skip;
                    return;
            }

            // An unknown ignorable destination (\*\something) is skipped safely.
            // \* says to ignore what the reader does not understand, so a
            // destination it does understand has to be let through - a shape
            // arrives as {\*\shpinst ...} and would otherwise be dropped here,
            // several hundred lines before the case that handles it.
            if (star && kw is not ("shpinst" or "background"))
            {
                _state.Dest = RtfDestination.Skip;
                return;
            }

            switch (_state.Dest)
            {
                case RtfDestination.Skip:
                case RtfDestination.Field:
                case RtfDestination.Generator:
                case RtfDestination.RevisionTable:
                    return;
                case RtfDestination.FieldInstruction:
                    if (kw == "uc") _state.UnicodeSkip = token.HasParameter ? Math.Max(0, token.Parameter) : 1;
                    else if (kw == "u") HandleUnicode(token.Parameter);
                    return;
                case RtfDestination.FontTable:
                    HandleFontTableControlWord(kw, token.Parameter);
                    return;
                case RtfDestination.ColorTable:
                    HandleColorTableControlWord(kw, token.Parameter);
                    return;
                default:
                    HandleBodyControlWord(kw, token.Parameter, token.HasParameter);
                    return;
            }
        }

        private void HandleBodyControlWord(string kw, int p, bool has)
        {
            if (_table.HandleDefinitionWord(kw, p, has))
                return;

            switch (kw)
            {
                case "deff": if (has && p >= 0) _defaultFont = p; break;
                // Two of the ways an RTF document says it was not written by
                // Word, as LibreOffice takes them: a double-byte style font other
                // than the one Word's compatibility settings fix (tdf#123703), and
                // \dntblnsbdb (tdf#128428). The third is the generator's name.
                case "stshfdbch": if (p != 31505) _widensSpaceRuns = false; break;
                case "dntblnsbdb": _widensSpaceRuns = false; break;
                case "b": _state.Char = _state.Char with { Bold = !(has && p == 0) }; break;
                case "i": _state.Char = _state.Char with { Italic = !(has && p == 0) }; break;
                case "strike": _state.Char = _state.Char with { Strikethrough = !(has && p == 0) }; break;
                case "v": _state.Char = _state.Char with { Hidden = !(has && p == 0) }; break;
                case "ul": _state.Char = _state.Char with { Underline = !(has && p == 0) }; break;
                case "uldb": case "uld": case "uldash": case "uldashd": case "uldashdd":
                case "ulth": case "ulwave": case "ulw":
                    _state.Char = _state.Char with { Underline = !(has && p == 0) };
                    if (!(has && p == 0)) ReportApproximation("rtf.underline.pattern", "An RTF underline pattern was retained as a single continuous underline.");
                    break;
                case "striked":
                    _state.Char = _state.Char with { Strikethrough = !(has && p == 0) };
                    if (!(has && p == 0)) ReportApproximation("rtf.strike.double", "An RTF double strikethrough was retained as a single rule.");
                    break;
                case "ulnone": _state.Char = _state.Char with { Underline = false }; break;

                // Tracked changes: \revised marks inserted text and \deleted
                // deleted text, each naming its author by index into \revtbl and
                // its time as a packed DTTM. A deletion is kept, as LibreOffice
                // keeps it, and wins over an insertion the same text is in.
                case "revised":
                    if (_state.Char.Revision?.IsDeletion != true)
                        _state.Char = _state.Char with { Revision = has && p == 0 ? null : new TextRevision(RevisionKind.Insertion) };
                    break;
                case "deleted":
                    _state.Char = _state.Char with { Revision = has && p == 0 ? null : new TextRevision(RevisionKind.Deletion) };
                    break;
                case "revauth" when _state.Char.Revision is { Kind: RevisionKind.Insertion } inserted:
                    _state.Char = _state.Char with { Revision = inserted with { Author = RevisionAuthor(p) } };
                    break;
                case "revauthdel" when _state.Char.Revision is { Kind: RevisionKind.Deletion } deleted:
                    _state.Char = _state.Char with { Revision = deleted with { Author = RevisionAuthor(p) } };
                    break;
                case "revdttm" when _state.Char.Revision is { Kind: RevisionKind.Insertion } inserted:
                    _state.Char = _state.Char with { Revision = inserted with { Date = RtfDateTime.FromDttm(p) } };
                    break;
                case "revdttmdel" when _state.Char.Revision is { Kind: RevisionKind.Deletion } deleted:
                    _state.Char = _state.Char with { Revision = deleted with { Date = RtfDateTime.FromDttm(p) } };
                    break;
                case "caps": _state.Char = ApplyCapitalization(_state.Char, TextCapitalization.AllCaps, !(has && p == 0)); break;
                case "scaps": _state.Char = ApplyCapitalization(_state.Char, TextCapitalization.SmallCaps, !(has && p == 0)); break;
                case "plain":
                    _state.Char = InlineStyle.Default;
                    int defaultCharset = _defaultFont is int defaultIndex ? _fonts.GetCharset(defaultIndex) : -1;
                    _state.CodePage = defaultCharset >= 0
                        ? RtfCodePage.CharsetToCodePage(defaultCharset)
                        : _state.DocumentCodePage;
                    break;
                case "fs": _state.Char = _state.Char with { FontSize = has ? p / 2f : null }; break;
                case "f":
                    int charset = _fonts.GetCharset(p);
                    if (charset >= 0)
                        _state.CodePage = RtfCodePage.CharsetToCodePage(charset);
                    _state.Char = _state.Char with { FontFamily = _fonts.GetName(p) };
                    break;
                case "cf": _state.Char = _state.Char with { Foreground = _colors.Get(p) }; break;
                case "cb":
                case "chcbpat":
                case "highlight": _state.Char = _state.Char with { Background = _colors.Get(p) }; break;

                // The paragraph's shading: the colour behind its pattern, which
                // with no \shading is all that shows. A paragraph property, so
                // \pard clears it.
                case "cbpat": _state.Para = _state.Para with { Background = has ? _colors.Get(p) : BColor.Empty }; break;
                case "ansicpg":
                    if (has)
                        _state.DocumentCodePage = _state.CodePage = p;
                    break;
                case "uc": _state.UnicodeSkip = has ? Math.Max(0, p) : 1; break;
                case "u": HandleUnicode(p); break;

                // Section geometry, in twips. Held aside and turned into a page
                // at the end, because a page needs all of it and RTF states it
                // one control word at a time.
                case "paperw": if (has) _paperWidth = p; return;
                case "paperh": if (has) _paperHeight = p; return;
                case "margl": if (has) _marginLeft = p; return;
                case "margr": if (has) _marginRight = p; return;
                case "margt": if (has) _marginTop = p; return;
                case "margb": if (has) _marginBottom = p; return;
                case "headery": if (has) _headerDistance = p; return;
                case "footery": if (has) _footerDistance = p; return;

                // The section's own page, which is the one its text is laid out
                // on whatever the document says. The document's page is the one
                // it opens on, so it is the first section's; a later section's
                // page belongs to that section, which this reader does not read,
                // and letting it overwrite the first would turn the whole
                // document landscape for one landscape section at its end.
                case "pgwsxn": if (has && !_pastFirstSection) _sectionWidth = p; return;
                case "pghsxn": if (has && !_pastFirstSection) _sectionHeight = p; return;
                case "marglsxn": if (has && !_pastFirstSection) _sectionLeft = p; return;
                case "margrsxn": if (has && !_pastFirstSection) _sectionRight = p; return;
                case "margtsxn": if (has && !_pastFirstSection) _sectionTop = p; return;
                case "margbsxn": if (has && !_pastFirstSection) _sectionBottom = p; return;
                // \sectd puts the section back to its defaults, which for the
                // page are the document's.
                case "sectd":
                    if (_bodySectionPending)
                    {
                        _bodySectionBreak = BodySectionBreak.Page;
                        _bodySectionHasProperties = true;
                    }
                    if (!_pastFirstSection)
                        _sectionWidth = _sectionHeight = _sectionLeft = _sectionRight = _sectionTop = _sectionBottom = null;
                    return;
                case "sect":
                    if (_state.Dest == RtfDestination.Normal && !_state.Frame.IsFrame && !_state.InTable)
                    {
                        FlushPending();
                        if (_bodySectionPending)
                        {
                            ApplyBodySectionBreak();
                            _builder.EndParagraph(_state.Para);
                        }
                        else if (_builder.ParagraphCount == 0)
                            _builder.EndParagraph(_state.Para);
                        else
                            _builder.EndPendingParagraph(_state.Para);
                        FinishTable();
                        _bodySectionPending = true;
                        _bodySectionHasProperties = false;
                        _bodySectionBreak = BodySectionBreak.Page;
                    }
                    _pastFirstSection = true;
                    return;
                case "sbknone": SetBodySectionBreak(BodySectionBreak.Continuous); return;
                case "sbkpage": SetBodySectionBreak(BodySectionBreak.Page); return;
                case "sbkcol": SetBodySectionBreak(BodySectionBreak.Column); return;
                case "sbkodd":
                case "sbkeven":
                    if (_bodySectionPending)
                    {
                        _bodySectionBreak = BodySectionBreak.Page;
                        _bodySectionHasProperties = true;
                        if (!_diagnostics.Any(diagnostic => diagnostic.Code == "rtf.section.parity"))
                            _diagnostics.Add(DocumentDiagnostic.Warning("rtf.section.parity",
                                "An odd/even section starts a new page, but insertion of a blank parity page is not represented."));
                    }
                    return;
                // Orientation is a flag beside the size, not an instruction to
                // swap it: the width and height a landscape section states are
                // already the landscape page's. LibreOffice draws
                // \lndscpsxn\pgwsxn11907\pghsxn16840 as a portrait page.
                case "lndscpsxn":
                case "landscape":
                    return;

                // \cols is a count, not a length; \colsx is the gap, which the
                // specification puts at 720 twips when the control word is absent.
                case "cols": if (has) _columns = p; return;
                case "colsx": if (has) _columnGap = p; return;

                // A drawing. Its geometry rides on the control words here and
                // its paint arrives as {\sp{\sn name}{\sv value}} pairs, so the
                // shape is assembled as the group is walked and finished when
                // the group closes.
                // \shp opens the shape but does not own the destination: \shpinst
                // does, and only one group may close as the shape or it would be
                // finished twice - once for the inner group and once for the outer.
                case "shp": BeginShape(); _state.Shape = new ShapeScope(); return;
                case "background": _state.InBackground = true; return;
                // Whether the background is shown at all, which LibreOffice
                // takes as Word's w:displayBackgroundShape.
                case "viewbksp": _viewBackground = !has || p != 0; return;
                case "shpinst": if (_state.Shape is not null) _state.Shape.HasInstructions = true; _state.Dest = RtfDestination.Shape; _state.ShapeOwnerDepth = _stack.Count; return;
                case "shpleft": if (has) _shapeLeft = p; return;
                case "shptop": if (has) _shapeTop = p; return;
                case "shpright": if (has) _shapeRight = p; return;
                case "shpbottom": if (has) _shapeBottom = p; return;
                // \shpz orders one shape against another, and the RTF a
                // letterhead makes is where it matters: the stripe and the box
                // over it are both in the body here, and this is the only thing
                // saying which the reader sees.
                case "shpz": if (has) { _shapeZ = p; _shapeZStated = true; } return;
                case "shpfblwtxt": _shapeBehindText = !has || p != 0; return;
                case "shpwr":
                    _shapeWrap = p switch { 1 => ShapeWrap.TopAndBottom, 2 or 4 => ShapeWrap.Square, _ => ShapeWrap.None };
                    if (p is 4 or 5)
                        ReportShapeApproximation("wrap", "Contour wrapping was approximated by the shape rectangle or unrestricted flow.");
                    return;
                case "shpwrk": _shapeWrapSide = p switch { 1 => WrapSide.Left, 2 => WrapSide.Right, _ => WrapSide.Largest }; return;
                case "shpbymargin": _shapeVerticalFrame = 0; return;
                case "shpbypage": _shapeVerticalFrame = 1; return;
                case "shpbypara": _shapeVerticalFrame = 2; return;
                case "shpbxpage": _shapeLeftFromPage = true; return;
                case "shpbxmargin":
                case "shpbxcolumn": _shapeLeftFromPage = false; return;
                case "sp": _state.Dest = RtfDestination.ShapeProperty; BeginShapeProperty(); return;
                case "sn": _state.Dest = RtfDestination.ShapePropertyName; return;
                case "sv": _state.Dest = RtfDestination.ShapePropertyValue; return;
                case "shptxt": _state.Dest = RtfDestination.ShapeText; return;

                case "pard":
                    _state.Para = ParagraphStyle.Default;
                    _state.LineSpacing = 0;
                    _state.MultipleLineSpacing = false;
                    _state.InTable = false;
                    _state.Frame = default;
                    break;

                // A frame's position words, as LibreOffice maps them onto Word's
                // w:framePr. A position cancels an alignment and an alignment a
                // position, whichever comes last.
                case "absw": if (has) _state.Frame = _state.Frame with { Width = p }; break;
                case "absh": if (has) _state.Frame = _state.Frame with { Height = p }; break;
                case "posx": if (has) _state.Frame = _state.Frame with { X = p, AlignX = RtfFrameAlignX.None }; break;
                case "posy": if (has) _state.Frame = _state.Frame with { Y = p, AlignY = RtfFrameAlignY.None }; break;
                case "posxl": _state.Frame = _state.Frame with { AlignX = RtfFrameAlignX.Left }; break;
                case "posxc": _state.Frame = _state.Frame with { AlignX = RtfFrameAlignX.Center }; break;
                case "posxr": _state.Frame = _state.Frame with { AlignX = RtfFrameAlignX.Right }; break;
                case "posxi": _state.Frame = _state.Frame with { AlignX = RtfFrameAlignX.Inside }; break;
                case "posxo": _state.Frame = _state.Frame with { AlignX = RtfFrameAlignX.Outside }; break;
                case "posyil": _state.Frame = _state.Frame with { AlignY = RtfFrameAlignY.Inline }; break;
                case "posyt": _state.Frame = _state.Frame with { AlignY = RtfFrameAlignY.Top }; break;
                case "posyc": _state.Frame = _state.Frame with { AlignY = RtfFrameAlignY.Center }; break;
                case "posyb": _state.Frame = _state.Frame with { AlignY = RtfFrameAlignY.Bottom }; break;
                case "posyin": _state.Frame = _state.Frame with { AlignY = RtfFrameAlignY.Inside }; break;
                case "posyout": _state.Frame = _state.Frame with { AlignY = RtfFrameAlignY.Outside }; break;
                case "phcol": _state.Frame = _state.Frame with { Horizontal = RtfFrameHorizontal.Column }; break;
                case "phmrg": _state.Frame = _state.Frame with { Horizontal = RtfFrameHorizontal.Margin }; break;
                case "phpg": _state.Frame = _state.Frame with { Horizontal = RtfFrameHorizontal.Page }; break;
                case "pvmrg": _state.Frame = _state.Frame with { Vertical = RtfFrameVertical.Margin }; break;
                case "pvpg": _state.Frame = _state.Frame with { Vertical = RtfFrameVertical.Page }; break;
                case "pvpara": _state.Frame = _state.Frame with { Vertical = RtfFrameVertical.Paragraph }; break;
                case "wraparound": _state.Frame = _state.Frame with { Wrap = RtfFrameWrap.Around }; break;
                case "nowrap": _state.Frame = _state.Frame with { Wrap = RtfFrameWrap.NotBeside }; break;
                case "overlay": _state.Frame = _state.Frame with { Wrap = RtfFrameWrap.None }; break;
                case "wrapthrough": _state.Frame = _state.Frame with { Wrap = RtfFrameWrap.Through }; break;
                case "dxfrtext":
                    if (has)
                        _state.Frame = _state.Frame with { HorizontalSpace = p, VerticalSpace = p };
                    break;
                case "dfrmtxtx": if (has) _state.Frame = _state.Frame with { HorizontalSpace = p }; break;
                case "dfrmtxty": if (has) _state.Frame = _state.Frame with { VerticalSpace = p }; break;
                case "intbl": _state.InTable = !(has && p == 0); break;
                case "sl":
                    if (has)
                    {
                        _state.LineSpacing = p;
                        ApplyLineSpacing();
                    }
                    break;
                case "slmult":
                    if (has)
                    {
                        _state.MultipleLineSpacing = p == 1;
                        ApplyLineSpacing();
                    }
                    break;
                case "ql": _state.Para = _state.Para with { Alignment = TextAlignment.Left }; break;
                case "qc": _state.Para = _state.Para with { Alignment = TextAlignment.Center }; break;
                case "qr": _state.Para = _state.Para with { Alignment = TextAlignment.Right }; break;
                case "qj": _state.Para = _state.Para with { Alignment = TextAlignment.Justify }; break;
                case "li":
                    int indent = TwipsToLevel(p);
                    float exactLeft = Math.Clamp(p / 20f, -20000, 20000);
                    _state.Para = _state.Para with { IndentLevel = indent,
                        LeftIndent = Math.Abs(exactLeft - indent * 18f) < 0.001f ? null : exactLeft };
                    break;
                case "sb": _state.Para = _state.Para with { SpacingBefore = has ? p / 20f : 0f }; break;
                case "sa": _state.Para = _state.Para with { SpacingAfter = has ? p / 20f : 0f }; break;

                // The property spelling of a page break, and the one LibreOffice
                // writes: it says this paragraph starts a page. It is a paragraph
                // property like any other here, so \pard clears it along with the
                // rest, and the 0 form turns it off the way every other toggle's
                // does.
                case "pagebb": _state.Para = _state.Para with { PageBreakBefore = !(has && p == 0) }; break;

                case "par": EndParagraph(); break;
                // A row end in the body closes a row of the table. Anywhere else
                // - a header, a shape's text - there is no table to close, and it
                // ends the paragraph as it always did.
                case "row":
                    if (InBody && _table.IsOpen)
                    {
                        FlushPending();
                        _table.EndRow();
                    }
                    else
                    {
                        EndParagraph();
                    }

                    break;
                case "page": PageBreak(); break;
                case "column": ColumnBreak(); break;
                case "line": AppendChar(0x2028); break; // soft line break
                case "tab": AppendBody("\t"); break;
                case "cell":
                    if (InBody)
                        EndCell();
                    else
                        AppendBody("\t");
                    break;
                // A cell of a table nested in a cell. Nested tables are not
                // read, so their text joins the outer cell's, and a tab keeps
                // one inner cell's text off the next.
                case "nestcell": AppendBody("\t"); break;
                case "lquote": AppendChar(0x2018); break;
                case "rquote": AppendChar(0x2019); break;
                case "ldblquote": AppendChar(0x201C); break;
                case "rdblquote": AppendChar(0x201D); break;
                case "bullet": AppendChar(0x2022); break;
                case "endash": AppendChar(0x2013); break;
                case "emdash": AppendChar(0x2014); break;
                case "enspace": AppendChar(0x2002); break;
                case "emspace": AppendChar(0x2003); break;
                default: break; // Unknown formatting control word: ignore (predictable degradation).
            }
        }

        /// <summary>
        /// Turns one capitalization kind on or off. <c>\caps0</c> and
        /// <c>\scaps0</c> each clear only the kind they name, so a run can drop
        /// small caps without disturbing an all-caps state and vice versa.
        /// </summary>
        private static InlineStyle ApplyCapitalization(InlineStyle style, TextCapitalization kind, bool on)
        {
            if (on)
                return style with { Capitalization = kind };

            return style.Capitalization == kind
                ? style with { Capitalization = TextCapitalization.None }
                : style;
        }

        /// <summary>
        /// <c>\slN</c> and <c>\slmult</c>, in whichever order they came.
        /// </summary>
        /// <remarks>
        /// Under <c>\slmult1</c> the amount is 240ths of a line; otherwise it is
        /// twips, the least a line is where positive and exactly its height where
        /// negative, which is how LibreOffice reads it (RTFKeyword::SL) and how
        /// Word writes a form's fixed lines. Nought is single spacing. LibreOffice
        /// takes a negative multiple by its size, and so does this.
        /// </remarks>
        private void ApplyLineSpacing()
        {
            long amount = _state.LineSpacing;
            _state.Para = amount == 0
                ? _state.Para with { LineSpacing = 1f, LineHeightRule = LineHeightRule.Proportional, LineHeight = 0f }
                : _state.MultipleLineSpacing
                    ? _state.Para with
                    {
                        LineSpacing = (float)Math.Min(Math.Abs(amount) / 240d, 100d), LineHeightRule = LineHeightRule.Proportional, LineHeight = 0f,
                    }
                    : _state.Para with
                    {
                        LineSpacing = 1f,
                        LineHeightRule = amount < 0 ? LineHeightRule.Exact : LineHeightRule.AtLeast,
                        LineHeight = Math.Min(ParagraphStyle.MaxLineHeight, Math.Abs(amount) / 20f),
                    };
        }

        private void HandleControlSymbol(char symbol)
        {
            if (symbol == '*')
            {
                _sawStar = true;
                return;
            }

            _sawStar = false;

            if (_state.Dest == RtfDestination.FontTable)
            {
                if (symbol is '\\' or '{' or '}')
                {
                    if (_pendingUnicodeSkip > 0)
                        _pendingUnicodeSkip--;
                    else
                        _fontName.Append(symbol);
                }

                return;
            }

            // A field instruction is text, and the three literal escapes belong
            // in it. They used to be dropped here, which is why the \l switch
            // Word spells a same-document link with - an escaped backslash, then
            // an l - arrived at ExtractHyperlink invisible, and its bookmark name
            // was reported as a hyperlink with an unsupported scheme.
            //
            // Only these three. The rest either produce characters an
            // instruction has no use for or, in the case of a line break, would
            // end a paragraph in the middle of one.
            if (_state.Dest is RtfDestination.FieldInstruction)
            {
                switch (symbol)
                {
                    case '\\': HandleText("\\"); break;
                    case '{': HandleText("{"); break;
                    case '}': HandleText("}"); break;
                }

                return;
            }

            if (!KeepsText(_state.Dest))
                return;

            // An escaped character is one Unicode fallback character, just
            // like a literal byte or a hex escape. Consume it here so the next
            // real text character is not skipped instead.
            if (_pendingUnicodeSkip > 0 && symbol is '\\' or '{' or '}' or '~' or '_' or '-')
            {
                _pendingUnicodeSkip--;
                return;
            }

            switch (symbol)
            {
                case '\\': AppendBody("\\"); break;
                case '{': AppendBody("{"); break;
                case '}': AppendBody("}"); break;
                case '~': AppendChar(0x00A0); break; // non-breaking space
                case '_': AppendChar(0x2011); break; // non-breaking hyphen
                case '-': break;                     // optional hyphen: drop
                case '\r':
                case '\n': EndParagraph(); break;
                default: break;
            }
        }

        private void HandleByte(byte value)
        {
            if (_state.Dest == RtfDestination.FieldInstruction)
            {
                if (_pendingUnicodeSkip > 0) _pendingUnicodeSkip--;
                else _state.Field?.Instruction.Append(RtfCodePage.DecodeByte(value, _state.CodePage));
                return;
            }
            if (_state.Dest == RtfDestination.FontTable)
            {
                if (_pendingUnicodeSkip > 0)
                    _pendingUnicodeSkip--;
                else
                {
                    ReportCodePageIfNeeded(value);
                    _fontName.Append((char)RtfCodePage.DecodeByte(value, _state.CodePage));
                }

                return;
            }

            if (!KeepsText(_state.Dest))
                return;

            if (_pendingUnicodeSkip > 0)
            {
                _pendingUnicodeSkip--;
                return;
            }

            ReportCodePageIfNeeded(value);
            AppendChar(RtfCodePage.DecodeByte(value, _state.CodePage));
        }

        /// <summary>
        /// The <c>\revtbl</c> entries, each ended by a semicolon. The first is
        /// conventionally "Unknown", which a change by nobody in particular names.
        /// </summary>
        private readonly List<string> _revisionAuthors = [];
        private readonly StringBuilder _revisionAuthor = new();

        private void HandleRevisionTableText(string text)
        {
            foreach (char character in text)
            {
                if (character != ';')
                {
                    _revisionAuthor.Append(character);
                    continue;
                }

                _revisionAuthors.Add(_revisionAuthor.ToString().Trim());
                _revisionAuthor.Clear();
            }
        }

        /// <summary>The author <c>\revtbl</c> lists at <paramref name="index"/>, or null for none.</summary>
        private string? RevisionAuthor(int index) =>
            index >= 0 && index < _revisionAuthors.Count && _revisionAuthors[index].Length > 0
                ? _revisionAuthors[index]
                : null;

        private void HandleText(string text)
        {
            _sawStar = false;
            switch (_state.Dest)
            {
                case RtfDestination.FontTable: HandleFontTableText(text); break;
                case RtfDestination.ColorTable: HandleColorTableText(text); break;
                case RtfDestination.FieldInstruction:
                    int skipped = Math.Min(_pendingUnicodeSkip, text.Length);
                    _pendingUnicodeSkip -= skipped;
                    foreach (char character in text.AsSpan(skipped))
                        _state.Field?.Instruction.Append(character <= 255 ? RtfCodePage.DecodeByte((byte)character, _state.CodePage) : character);
                    break;
                case RtfDestination.Generator: _generator.Append(text); break;
                case RtfDestination.RevisionTable: HandleRevisionTableText(text); break;
                case RtfDestination.Normal:
                case RtfDestination.FieldResult:
                case RtfDestination.Header:
                case RtfDestination.HeaderFirst:
                case RtfDestination.HeaderEven:
                case RtfDestination.Footer:
                case RtfDestination.FooterFirst:
                case RtfDestination.FooterEven:
                case RtfDestination.Note:
                case RtfDestination.ShapeText: HandleBodyText(text); break;
                case RtfDestination.ShapePropertyName: _shapePropertyName.Append(text); break;
                case RtfDestination.ShapePropertyValue: _shapePropertyValue.Append(text); break;
                default: break; // Skip / Field container: drop.
            }
        }

        private void HandleBodyText(string text)
        {
            int i = 0;
            while (_pendingUnicodeSkip > 0 && i < text.Length)
            {
                i++;
                _pendingUnicodeSkip--;
            }

            if (i >= text.Length)
                return;

            var decoded = new StringBuilder(text.Length - i);
            for (; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch <= 0xFF)
                {
                    ReportCodePageIfNeeded((byte)ch);
                    decoded.Append(RtfCodePage.DecodeByte((byte)ch, _state.CodePage));
                }
                else
                {
                    decoded.Append(ch);
                }
            }

            AppendBody(decoded.ToString());
        }

        private void HandleUnicode(int parameter)
        {
            if (_state.Dest == RtfDestination.FieldInstruction)
            {
                int value = parameter < 0 ? parameter + 65536 : parameter;
                _state.Field?.Instruction.Append((char)(value is >= 0 and <= 0xFFFF ? value : 0xFFFD));
                _pendingUnicodeSkip = _state.UnicodeSkip;
                return;
            }
            if (!KeepsText(_state.Dest))
                return;

            int code = parameter < 0 ? parameter + 65536 : parameter;
            if (code is < 0 or > 0xFFFF)
                code = 0xFFFD;

            AppendChar(code);
            _pendingUnicodeSkip = _state.UnicodeSkip;
        }

        private void HandleFontTableControlWord(string kw, int parameter)
        {
            switch (kw)
            {
                case "f":
                    _fontIndex = parameter;
                    _fontName.Clear();
                    _fontCharset = 0;
                    break;
                case "fcharset":
                    _fontCharset = parameter;
                    break;
                case "uc":
                    _state.UnicodeSkip = Math.Max(0, parameter);
                    break;
                case "u":
                    int code = parameter < 0 ? parameter + 65536 : parameter;
                    _fontName.Append((char)(code is >= 0 and <= 0xFFFF ? code : 0xFFFD));
                    _pendingUnicodeSkip = _state.UnicodeSkip;
                    break;
                default:
                    break;
            }
        }

        private void HandleFontTableText(string text)
        {
            int skip = Math.Min(_pendingUnicodeSkip, text.Length);
            _pendingUnicodeSkip -= skip;
            text = text[skip..];
            int semicolon = text.IndexOf(';');
            int length = semicolon < 0 ? text.Length : semicolon;
            for (int i = 0; i < length; i++)
            {
                char ch = text[i];
                if (ch <= 0xFF)
                {
                    ReportCodePageIfNeeded((byte)ch);
                    _fontName.Append(RtfCodePage.DecodeByte((byte)ch, _state.CodePage));
                }
                else
                {
                    _fontName.Append(ch);
                }
            }

            if (semicolon >= 0)
                CommitFont();
        }

        private void CommitFont()
        {
            _fonts.Set(_fontIndex, _fontName.ToString().Trim(), _fontCharset);
            _fontName.Clear();
            _fontCharset = 0;
        }

        private void HandleColorTableControlWord(string kw, int parameter)
        {
            switch (kw)
            {
                case "red": _r = parameter; _colorSeen = true; break;
                case "green": _g = parameter; _colorSeen = true; break;
                case "blue": _b = parameter; _colorSeen = true; break;
                default: break;
            }
        }

        private void HandleColorTableText(string text)
        {
            foreach (char ch in text)
            {
                if (ch == ';')
                    CommitColor();
            }
        }

        private void CommitColor()
        {
            _colors.Add(_colorSeen
                ? new BColor((byte)Clamp(_r), (byte)Clamp(_g), (byte)Clamp(_b))
                : BColor.Empty);
            ResetColor();
        }

        private void ResetColor()
        {
            _r = _g = _b = 0;
            _colorSeen = false;
        }

        /// <summary>
        /// The accumulator the current destination writes into. Chosen per call
        /// rather than swapped on group entry: a destination is part of the state
        /// RTF pushes and pops with its braces, so reading it each time gets the
        /// nesting right without tracking where a group began.
        /// </summary>
        private Accumulator Active
        {
            get
            {
                // A field result belongs where the field is, not where the reader
                // happens to be: the destination says "this is a field result" and
                // the host says which part of the document it is a field result in.
                RtfDestination destination = _state.Dest == RtfDestination.FieldResult
                    ? _state.FieldHost
                    : _state.Dest;

                if (destination == RtfDestination.ShapeText)
                    return _shapeText ??= new Accumulator(_maxParagraphs);

                if (destination == RtfDestination.Note)
                    return _noteText ??= new Accumulator(_maxParagraphs);

                if (!IsRunning(destination))
                    return BodyOrFrame(destination);

                if (!_running.TryGetValue(destination, out Accumulator? accumulator))
                {
                    accumulator = new Accumulator(_maxParagraphs);
                    _running[destination] = accumulator;
                }

                return accumulator;
            }
        }

        /// <summary>
        /// Whether this document's runs of spaces are laid out wider: unless it
        /// said otherwise, or names a generator that is not Microsoft's - a
        /// document LibreOffice or this writer saved spaces its runs as their
        /// font does, and says so with a generator of its own.
        /// </summary>
        private bool WidensSpaceRuns() =>
            _widensSpaceRuns &&
            (!_sawGenerator || _generator.ToString().TrimStart().StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether text reaching this destination is kept at all.
        /// </summary>
        /// <remarks>
        /// The same set <see cref="HandleText"/> routes to the body writer, named
        /// once so the three gates that guard an escape, a hex byte and a \uN
        /// cannot drift from it. They used to list Normal and FieldResult and
        /// nothing else, so a footer reading "Seite 1 von 2" lost its umlaut while
        /// the plain letters beside it survived - the same routing defect the
        /// field result had, in the three places that spell a character rather
        /// than carry one.
        /// </remarks>
        private static bool KeepsText(RtfDestination destination) =>
            destination is RtfDestination.Normal or RtfDestination.FieldResult or RtfDestination.ShapeText or RtfDestination.Note ||
            IsRunning(destination);

        private static bool IsRunning(RtfDestination destination) => destination is
            RtfDestination.Header or RtfDestination.HeaderFirst or RtfDestination.HeaderEven or
            RtfDestination.Footer or RtfDestination.FooterFirst or RtfDestination.FooterEven;

        /// <summary>
        /// The page the section control words describe, or null when the document
        /// gave an unusable paper size. Omitted values use the import defaults.
        /// Twips throughout, 20 to the point.
        /// </summary>
        private PageGeometry? BuildPageGeometry()
        {
            int width = _sectionWidth ?? _paperWidth;
            int height = _sectionHeight ?? _paperHeight;
            if (width <= 0 || height <= 0)
                return null;

            var geometry = new PageGeometry(
                width / 20d,
                height / 20d,
                (_sectionLeft ?? _marginLeft) / 20d,
                (_sectionRight ?? _marginRight) / 20d,
                (_sectionTop ?? _marginTop) / 20d,
                (_sectionBottom ?? _marginBottom) / 20d,
                _headerDistance / 20d,
                _footerDistance / 20d);

            if (geometry.IsUsable)
                return BuildColumns(geometry);

            _diagnostics.Add(DocumentDiagnostic.Warning(
                "rtf.page.geometry",
                "RTF section properties gave a page with no room to write on; the page was not read."));
            return null;
        }

        /// <summary>The page divided into the columns <c>\cols</c> and <c>\colsx</c> state.</summary>
        private PageGeometry BuildColumns(PageGeometry geometry)
        {
            if (_columns <= 1)
                return geometry;

            PageGeometry divided = geometry.WithColumns(_columns, Math.Max(0, _columnGap) / 20d);
            if (!divided.HasColumns)
            {
                _diagnostics.Add(DocumentDiagnostic.Warning(
                    "rtf.columns.geometry",
                    "RTF section columns left no room to write in a column; the page was read as one column."));
            }

            return divided;
        }

        private void BeginShape()
        {
            _shapeFillEnabled = null;
            _shapeFlipHorizontal = false;
            _shapeBehindText = false;
            _shapeFillOpacity = _shapeBackOpacity = _shapeLineOpacity = 1;
            _shapePadding = default;
            _shapeAlignment = CellVerticalAlignment.Top;
            _shapeWrap = ShapeWrap.None;
            _shapeWrapSide = WrapSide.Largest;
            _shapeWrapDistance = 0;
            _shapeLeft = 0;
            _shapeTop = 0;
            _shapeRight = 0;
            _shapeBottom = 0;
            _shapeZ = 0;
            _shapeZStated = false;
            _shapeAlignX = _shapeAlignY = 0;
            _shapeWrapDistances.Clear();
            _shapeVerticalFrame = 2;
            _shapeLeftFromPage = false;
            _shapeFitsText = false;
            _shapeType = 1;
            _shapeLineWidth = 0.75;
            _shapeFlipVertical = false;
            _shapeFillStart = BColor.White;
            _shapeFillEnd = BColor.Empty;

            // Word draws a shape's line black unless it says otherwise, and so
            // does LibreOffice's import, against Writer's own blue default.
            _shapeLineColor = BColor.Black;
            _shapeFillAngle = 0;
            _shapeHasFill = false;
            _shapeGradient = false;
            _shapeHasLine = true;
            _shapeInBackground = _state.InBackground;
            _shapeFillFocus = 0;
            _shapeText = null;
        }

        private void BeginShapeProperty()
        {
            _shapePropertyName.Clear();
            _shapePropertyValue.Clear();
        }

        /// <summary>
        /// Applies one shape property pair.
        /// </summary>
        /// <remarks>
        /// RTF states a shape colour as one integer holding blue, green and red in
        /// that order, which is the reverse of how everything else here writes a
        /// colour and the easiest thing in the format to get backwards.
        /// </remarks>
        private void ApplyShapeProperty()
        {
            string name = _shapePropertyName.ToString().Trim();
            string value = _shapePropertyValue.ToString().Trim();
            if (name.Length == 0 || !long.TryParse(
                    value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            {
                return;
            }

            switch (name)
            {
                case "fillColor":
                    _shapeFillStart = ShapeColor(number);
                    _shapeHasFill = true;
                    break;
                case "fillBackColor":
                    _shapeFillEnd = ShapeColor(number);
                    break;
                case "fillType":
                    _shapeGradient = number != 0;
                    break;
                case "fillFocus":
                    _shapeFillFocus = number;
                    break;
                case "fillAngle":
                    // Word states it in 65536ths of a degree.
                    _shapeFillAngle = number / 65536d;
                    break;
                case "fFilled":
                    _shapeFillEnabled = number != 0;
                    break;
                case "lineColor":
                    _shapeLineColor = ShapeColor(number);
                    break;
                case "dhgt": if (!_shapeZStated) _shapeZ = (int)Math.Clamp(number, int.MinValue, int.MaxValue); break;
                case "posh": _shapeAlignX = number is >= 1 and <= 3 ? (int)number : 0; break;
                case "posv": _shapeAlignY = number is >= 1 and <= 3 ? (int)number : 0; break;
                case "lineWidth":
                    _shapeLineWidth = Math.Max(0, number / 12700d);
                    break;
                case "fFlipH":
                    _shapeFlipHorizontal = number != 0;
                    break;
                case "fillOpacity": _shapeFillOpacity = Math.Clamp(number / 65536d, 0, 1); break;
                case "fillBackOpacity": _shapeBackOpacity = Math.Clamp(number / 65536d, 0, 1); break;
                case "lineOpacity": _shapeLineOpacity = Math.Clamp(number / 65536d, 0, 1); break;
                case "fBehindDocument": _shapeBehindText = number != 0; break;
                case "posrelh": _shapeLeftFromPage = number == 1; break;
                case "dxTextLeft": _shapePadding = _shapePadding with { Left = ShapeDistance(number) }; break;
                case "dyTextTop": _shapePadding = _shapePadding with { Top = ShapeDistance(number) }; break;
                case "dxTextRight": _shapePadding = _shapePadding with { Right = ShapeDistance(number) }; break;
                case "dyTextBottom": _shapePadding = _shapePadding with { Bottom = ShapeDistance(number) }; break;
                case "anchorText":
                    _shapeAlignment = number switch { 1 or 4 => CellVerticalAlignment.Center, 2 or 5 => CellVerticalAlignment.Bottom, _ => CellVerticalAlignment.Top };
                    break;
                case "dxWrapDistLeft":
                case "dxWrapDistRight":
                case "dyWrapDistTop":
                case "dyWrapDistBottom":
                    _shapeWrapDistances[name] = ShapeDistance(number);
                    break;
                case "fFlipV":
                    _shapeFlipVertical = number != 0;
                    break;
                case "shapeType":
                    _shapeType = number;
                    break;
                case "fFitShapeToText":
                    _shapeFitsText = number != 0;
                    break;
                case "fLine":
                    _shapeHasLine = number != 0;
                    break;
                case "posrelv":
                    // 3 is the line, which the model holds as its paragraph.
                    _shapeVerticalFrame = number is 0 or 1 ? (int)number : 2;
                    break;
                default:
                    break;
            }
        }

        private static double ShapeDistance(long value) => Math.Clamp(value / 12700d, 0, 20000);
        private static BColor ShapeOpacity(BColor color, double opacity) => color.IsEmpty ? color :
            BColor.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
        private void ReportShapeApproximation(string code, string message) => ReportApproximation("rtf.shape." + code, message);
        private void ReportApproximation(string code, string message)
        {
            if (!_diagnostics.Any(d => d.Code == code))
                _diagnostics.Add(DocumentDiagnostic.Warning(code, message));
        }

        private static BColor ShapeColor(long value) =>
            new((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

        /// <summary>Turns the walked group into a shape, when it described one.</summary>
        private void FinishShape()
        {
            _shapeWrapDistance = _shapeWrapDistances.Values.DefaultIfEmpty(0).Max();
            double width = ((double)_shapeRight - _shapeLeft) / 20d;
            double height = ((double)_shapeBottom - _shapeTop) / 20d;
            IReadOnlyList<RichTextParagraph> paragraphs = _shapeText is null
                ? []
                : _shapeText.Build(ParagraphStyle.Default).Paragraphs;
            if (paragraphs.Count == 1 && paragraphs[0].Length == 0)
                paragraphs = [];

            ShapeFill? fill = null;
            if ((_shapeFillEnabled ?? (_shapeHasFill || _shapeType != 75)) && !_shapeFillStart.IsEmpty)
            {
                // A focus near 50 mirrors the gradient, as VML's does.
                fill = _shapeGradient && !_shapeFillEnd.IsEmpty
                    ? new ShapeFill(
                        ShapeOpacity(_shapeFillStart, _shapeFillOpacity),
                        ShapeOpacity(_shapeFillEnd, _shapeBackOpacity),
                        _shapeFillAngle,
                        Axial: Math.Abs(_shapeFillFocus) is >= 25 and <= 75)
                    : ShapeFill.Solid(ShapeOpacity(_shapeFillStart, _shapeFillOpacity));
            }

            // The page's background states no box; it is the page.
            if (_shapeInBackground)
            {
                _shapeText = null;
                _pageBackground = fill;
                return;
            }

            _shapeText = null;
            BColor outline = _shapeHasLine ? ShapeOpacity(_shapeLineColor, _shapeLineOpacity) : BColor.Empty;

            bool rule = outline.IsEmpty is false && ((width == 0) != (height == 0));
            // A box sized by its text may state no height at all.
            bool sizedByText = _shapeFitsText && paragraphs.Count > 0 && height == 0;
            if (!rule && (width == 0 || (height == 0 && !sizedByText)))
                return;

            // A picture's frame draws its picture, which is not read, rather than
            // a box; it is kept only for paint or text of its own.
            if (fill is null && paragraphs.Count == 0 && (outline.IsEmpty || _shapeType == 75))
                return;

            // A shape measured from the page or its top margin is placed from the
            // top of the page its paragraph is on, which the model holds as such.
            double top = _shapeTop / 20d;
            if (_shapeVerticalFrame == 0)
                top += (_sectionTop ?? _marginTop) / 20d;

            // A line drawn right to left or bottom to top states its far end
            // first; the rule is the same either way.
            double left = Math.Min(_shapeLeft, _shapeRight) / 20d;
            if (_shapeLeftFromPage)
                left -= (_sectionLeft ?? _marginLeft) / 20d;
            if (height < 0)
                top += height;

            if (_shapeAlignX != 0)
            {
                double pageWidth = (_sectionWidth ?? _paperWidth) / 20d;
                double marginLeft = (_sectionLeft ?? _marginLeft) / 20d;
                double marginRight = (_sectionRight ?? _marginRight) / 20d;
                double referenceWidth = _shapeLeftFromPage ? pageWidth : pageWidth - marginLeft - marginRight;
                left = (_shapeLeftFromPage ? -marginLeft : 0) +
                    (_shapeAlignX == 2 ? (referenceWidth - Math.Abs(width)) / 2 : _shapeAlignX == 3 ? referenceWidth - Math.Abs(width) : 0);
            }
            if (_shapeAlignY != 0 && _shapeVerticalFrame is 0 or 1 && height != 0 && !_shapeFitsText)
            {
                double pageHeight = (_sectionHeight ?? _paperHeight) / 20d;
                double marginTop = (_sectionTop ?? _marginTop) / 20d;
                double marginBottom = (_sectionBottom ?? _marginBottom) / 20d;
                double referenceHeight = _shapeVerticalFrame == 1 ? pageHeight : pageHeight - marginTop - marginBottom;
                top = (_shapeVerticalFrame == 1 ? 0 : marginTop) +
                    (_shapeAlignY == 2 ? (referenceHeight - Math.Abs(height)) / 2 : _shapeAlignY == 3 ? referenceHeight - Math.Abs(height) : 0);
            }
            else if (_shapeAlignY is 2 or 3)
                ReportShapeApproximation("position", "An aligned shape lacked a fixed page-relative height; its stated offset was retained.");

            _shapes.Add(new DocumentShape(
                _builder.ParagraphCount,
                left,
                top,
                Math.Abs(width),
                Math.Abs(height),
                fill,
                outline,
                paragraphs,
                behindText: _shapeBehindText,
                wrap: _shapeWrap,
                wrapSide: _shapeWrapSide,
                wrapDistance: _shapeWrapDistance,
                textPadding: _shapePadding,
                textVerticalAlignment: _shapeAlignment,
                zOrder: _shapeZ,
                offsetFromPage: _shapeVerticalFrame != 2,
                growsToText: _shapeFitsText,
                geometry: _shapeType == 3 ? ShapeGeometry.Ellipse : _shapeType == 20
                    ? ((width * height < 0) != (_shapeFlipVertical ^ _shapeFlipHorizontal) ? ShapeGeometry.LineAscending : ShapeGeometry.Line) : ShapeGeometry.Rectangle,
                outlineWidth: _shapeLineWidth));
        }

        /// <summary>The headers and footers the document's running destinations collected.</summary>
        private RunningContent BuildRunningContent()
        {
            RunningContent content = RunningContent.Empty.WithDifferentFirstPage(_titlePage);
            foreach ((RtfDestination destination, Accumulator accumulator) in _running)
            {
                IReadOnlyList<RichTextParagraph> paragraphs = accumulator.Build(ParagraphStyle.Default).Paragraphs;
                if (paragraphs.Count == 0)
                    continue;

                content = destination switch
                {
                    RtfDestination.Header => content.WithHeader(PageSelection.Default, paragraphs),
                    RtfDestination.HeaderFirst => content.WithHeader(PageSelection.First, paragraphs),
                    RtfDestination.HeaderEven => content.WithHeader(PageSelection.Even, paragraphs),
                    RtfDestination.Footer => content.WithFooter(PageSelection.Default, paragraphs),
                    RtfDestination.FooterFirst => content.WithFooter(PageSelection.First, paragraphs),
                    _ => content.WithFooter(PageSelection.Even, paragraphs),
                };
            }

            return content;
        }

        /// <summary>
        /// The body, or the frame the paragraph's position words put it in.
        /// </summary>
        /// <remarks>
        /// Paragraphs that state the same frame share it, and the first one that
        /// states another, or none, finishes it: RTF 1.9.1 measures a frame from
        /// "the next unframed paragraph", which is the one it is anchored to. A
        /// frame in a table is read as the table's text, as LibreOffice reads it.
        /// </remarks>
        private Accumulator BodyOrFrame(RtfDestination destination)
        {
            bool framed = destination == RtfDestination.Normal && _state.Frame.IsFrame && !_state.InTable;
            if (_frameText is not null && (!framed || _state.Frame != _frameKey))
                FinishFrame();

            if (!framed)
                return _builder;

            _frameKey = _state.Frame;
            return _frameText ??= new Accumulator(_maxParagraphs);
        }

        /// <summary>
        /// Turns the frame's paragraphs into a floating shape that holds them,
        /// placed where the frame's words say.
        /// </summary>
        private void FinishFrame()
        {
            Accumulator? text = _frameText;
            RtfFrame frame = _frameKey;
            _frameText = null;
            if (text is null)
                return;

            IReadOnlyList<RichTextParagraph> paragraphs = text.Build(ParagraphStyle.Default).Paragraphs;
            bool empty = true;
            foreach (RichTextParagraph paragraph in paragraphs)
                empty &= paragraph.Length == 0;
            if (empty && frame.Height == 0)
                return;

            double pageWidth = (_sectionWidth ?? _paperWidth) / 20d;
            double marginLeft = (_sectionLeft ?? _marginLeft) / 20d;
            double marginRight = (_sectionRight ?? _marginRight) / 20d;
            double marginTop = (_sectionTop ?? _marginTop) / 20d;
            double textWidth = Math.Max(0, pageWidth - marginLeft - marginRight);

            // A frame that gives no width is as wide as its column, which is
            // what the reference holds without measuring its lines.
            bool fromPage = frame.Horizontal == RtfFrameHorizontal.Page;
            double referenceWidth = fromPage ? pageWidth : textWidth;
            double width = frame.Width > 0 ? frame.Width / 20d : textWidth;
            double height = Math.Abs((double)frame.Height) / 20d;
            double x = frame.AlignX switch
            {
                RtfFrameAlignX.Left or RtfFrameAlignX.Inside => 0,
                RtfFrameAlignX.Center => (referenceWidth - width) / 2,
                RtfFrameAlignX.Right or RtfFrameAlignX.Outside => referenceWidth - width,
                _ => frame.X / 20d,
            };
            if (fromPage)
                x -= marginLeft;

            // Inline is the paragraph's own place, which is a frame measured from
            // its paragraph at nought.
            bool fromParagraph = frame.Vertical == RtfFrameVertical.Paragraph || frame.AlignY == RtfFrameAlignY.Inline;
            double y = frame.AlignY switch
            {
                RtfFrameAlignY.None => frame.Y / 20d,
                RtfFrameAlignY.Center or RtfFrameAlignY.Bottom when frame.Height < 0 && !fromParagraph =>
                    ((frame.Vertical == RtfFrameVertical.Page ? (_sectionHeight ?? _paperHeight) / 20d :
                        ((_sectionHeight ?? _paperHeight) - (double)(_sectionTop ?? _marginTop) - (_sectionBottom ?? _marginBottom)) / 20d) - height) /
                    (frame.AlignY == RtfFrameAlignY.Center ? 2 : 1),
                RtfFrameAlignY.Center or RtfFrameAlignY.Bottom => ReportFrameAlignment(frame.AlignY),
                _ => 0,
            };
            if (!fromParagraph && frame.Vertical == RtfFrameVertical.Margin)
                y += marginTop;

            (ShapeWrap wrap, double distance) = frame.Wrap switch
            {
                RtfFrameWrap.NotBeside => (ShapeWrap.TopAndBottom, frame.VerticalSpace / 20d),
                RtfFrameWrap.None or RtfFrameWrap.Through => (ShapeWrap.None, 0d),
                _ => (ShapeWrap.Square, frame.HorizontalSpace / 20d),
            };

            _shapes.Add(new DocumentShape(
                _builder.ParagraphCount,
                x,
                y,
                width,
                height,
                paragraphs: paragraphs,
                behindText: false,
                wrap: wrap,
                wrapDistance: distance,
                offsetFromPage: !fromParagraph,
                growsToText: frame.Height >= 0));
        }

        /// <summary>
        /// A frame centred on or set at the foot of its reference needs its
        /// height, which a frame sized by its text does not have until it is laid
        /// out; it is set at the top and said once.
        /// </summary>
        private double ReportFrameAlignment(RtfFrameAlignY alignment)
        {
            if (!_reportedFrameAlignment)
            {
                _diagnostics.Add(DocumentDiagnostic.Info(
                    "rtf.frame.position",
                    $"A frame aligned to the {(alignment == RtfFrameAlignY.Center ? "centre" : "foot")} of its reference was set at its top."));
                _reportedFrameAlignment = true;
            }

            return 0;
        }

        private void ReportSlantedLine()
        {
            if (_reportedSlantedLine)
                return;

            _diagnostics.Add(DocumentDiagnostic.Info(
                "rtf.shape.line",
                "A slanted line was not read; the model holds a line only as a horizontal or vertical rule."));
            _reportedSlantedLine = true;
        }

        private void EndParagraph()
        {
            FlushPending();

            // A body paragraph either belongs to the table being read or ends it:
            // the first paragraph out of a table is where the table stops.
            if (InBody)
            {
                if (_state.InTable)
                    _table.NoteParagraph(_builder.ParagraphCount);
                else
                {
                    FinishTable();
                    ApplyBodySectionBreak();
                }
            }

            CloseParagraph();
        }

        /// <summary>
        /// Closes the paragraph with the character formatting in effect, which is
        /// the paragraph mark's and what an empty paragraph is as tall as.
        /// </summary>
        private void CloseParagraph() =>
            Active.EndParagraph(_state.Para, _state.Char with { LinkHref = null, Image = null });

        /// <summary>
        /// <c>\cell</c>: ends the cell's last paragraph, and the cell with it.
        /// </summary>
        /// <remarks>
        /// It is a cell whether or not <c>\intbl</c> said so, because a cell end
        /// can mean nothing else. It used to be read as a tab and <c>\row</c> as
        /// a paragraph, so a table arrived as lines of tab-separated text: two
        /// rows of seven and eighteen cells drew as three lines with no grid.
        /// </remarks>
        private void EndCell()
        {
            FlushPending();
            _table.NoteParagraph(_builder.ParagraphCount);
            CloseParagraph();
            _table.EndCell(_builder.ParagraphCount);
        }

        private void FinishTable()
        {
            if (_table.Finish() is DocumentTable table)
                _tables.Add(table);
        }

        /// <summary>
        /// Whether text written now lands in the body flow, where a table can be:
        /// not a header, a footer or a shape's text, whose paragraphs the model
        /// holds as lists of their own.
        /// </summary>
        private bool InBody => ReferenceEquals(Active, _builder);

        /// <summary>
        /// Takes a <c>\page</c>, which RTF states in the character stream rather
        /// than on a paragraph.
        /// </summary>
        /// <remarks>
        /// The buffered text is flushed first, because the accumulator is about
        /// to be asked whether the paragraph it is building has anything in it,
        /// and text still sitting in the pending buffer would make a break that
        /// interrupted a paragraph look like one that arrived between two.
        /// </remarks>
        private void PageBreak()
        {
            FlushPending();
            // A positioned paragraph still belongs to the body's page. Its
            // frame has no paginated flow of its own, so keeping the break in
            // the frame would discard it when the shape is laid out. Frames
            // awaiting their next unframed paragraph travel with that paragraph.
            if (_state.Dest == RtfDestination.Normal && !_state.InTable)
            {
                FinishFrame();
                if (!_builder.HasPendingText &&
                    _shapes.Any(shape => !shape.IsPageAnchored && shape.ParagraphIndex == _builder.ParagraphCount))
                {
                    int anchor = _builder.ParagraphCount;
                    _builder.EndParagraph(_state.Para);
                    for (int i = 0; i < _shapes.Count; i++)
                    {
                        DocumentShape shape = _shapes[i];
                        if (shape.IsPageAnchored || shape.ParagraphIndex != anchor)
                            continue;
                        _shapes[i] = new DocumentShape(_builder.ParagraphCount, shape.OffsetX, shape.OffsetY,
                            shape.Width, shape.Height, shape.Fill, shape.Outline, shape.Paragraphs, shape.Image,
                            shape.BehindText, shape.Wrap, shape.WrapSide, shape.WrapDistance, shape.ZOrder,
                            shape.AnchorPage, shape.OffsetFromPage, shape.GrowsToText, shape.Tables, shape.TextPadding,
                            shape.TextVerticalAlignment, shape.TextOverflow, shape.GrowsWidthToText, shape.FrameName,
                            shape.NextFrameName, shape.Geometry, shape.OutlineWidth, shape.OffsetXFromCell, shape.CellHorizontalAlignment);
                    }
                }
                if (!_builder.BreakPage(_state.Para))
                    ReportEmptyPage();
                return;
            }
            if (!Active.BreakPage(_state.Para))
                ReportEmptyPage();
        }

        /// <summary>
        /// <c>\column</c>: the text after it starts the next column, or - in a
        /// section of one column, where there is no next column - the next page,
        /// as Word draws it.
        /// </summary>
        private void ColumnBreak()
        {
            if (_columns <= 1)
            {
                PageBreak();
                return;
            }

            FlushPending();
            Active.BreakColumn(_state.Para);
        }

        /// <summary>
        /// A break with no paragraph to land on - one at the end of the document,
        /// or a second with nothing between it and the first.
        /// </summary>
        /// <remarks>
        /// What such a break asks for is a blank page, and a page break here is a
        /// property of a paragraph, so there is no paragraph to hang it on and
        /// nothing to hold the emptiness. Inventing an empty paragraph to carry it
        /// would put a blank line into a document that has none, and would come
        /// back out of the writer as content. So it is dropped - but said once,
        /// because the difference is a page.
        /// </remarks>
        private void ReportEmptyPage()
        {
            if (_reportedEmptyPage)
                return;

            _reportedEmptyPage = true;
            _diagnostics.Add(DocumentDiagnostic.Info(
                "rtf.pagebreak.empty",
                "A page break with no paragraph after it asks for a blank page, which this model cannot hold; the break was dropped."));
        }

        private void ReportEmbedded()
        {
            if (_reportedEmbedded)
                return;

            // A caller that asked for image decoding gets a warning under
            // the shared capability code, not the bland note: it asked for
            // something this reader cannot do, and the document it receives
            // is missing content it expected.
            _diagnostics.Add(_embeddedDecodingRequested
                ? DocumentDiagnostic.Warning(
                    DocumentDiagnosticCodes.CapabilityNotComposed,
                    "Embedded image decoding was requested, but this reader composes no image service; pictures and objects were skipped.")
                : DocumentDiagnostic.Info(
                    "rtf.embedded",
                    "Embedded pictures/objects are not imported and were skipped."));
            _reportedEmbedded = true;
        }

        private void AppendChar(int code) => AppendBody(((char)code).ToString());

        private void AppendBody(string text)
        {
            if (text.Length == 0)
                return;
            if (_state.Field is FieldFrame field && !string.IsNullOrWhiteSpace(text)) field.Characters += text.Length;

            if (_hasPending && !_pendingStyle.Equals(_state.Char))
                FlushPending();

            if (!_hasPending)
            {
                _pendingStyle = _state.Char;
                _hasPending = true;
            }

            _pending.Append(text);
        }

        private static string? ExtractRubyBase(string instruction)
        {
            if (!instruction.TrimStart().StartsWith("EQ ", StringComparison.OrdinalIgnoreCase)) return null;
            int overlay = instruction.IndexOf("\\o", StringComparison.OrdinalIgnoreCase);
            if (overlay < 0) return null;
            int open = instruction.IndexOf('(', overlay), comma = -1, depth = 0;
            if (open < 0) return null;
            for (int i = open; i < instruction.Length; i++)
            {
                char ch = instruction[i];
                if (ch == '(') depth++;
                else if (ch == ')' && --depth == 0)
                {
                    if (comma < 0 || !instruction.AsSpan(open, comma - open).Contains("\\s\\up", StringComparison.OrdinalIgnoreCase)) return null;
                    string value = instruction[(comma + 1)..i].Trim();
                    return value.Length > 0 && !value.Contains('\\') ? value : null;
                }
                else if (ch == ',' && depth == 1) comma = i;
            }
            return null;
        }

        private void FlushPending()
        {
            if (_hasPending && _pending.Length > 0)
            {
                if (InBody && !_state.InTable)
                    ApplyBodySectionBreak();
                Active.Append(_pending.ToString(), _pendingStyle);
            }

            _pending.Clear();
            _hasPending = false;
        }

        private void ApplyBodySectionBreak()
        {
            if (!_bodySectionPending)
                return;
            _bodySectionPending = false;
            if (_bodySectionBreak == BodySectionBreak.Page ||
                (_bodySectionBreak == BodySectionBreak.Column && _columns <= 1))
            {
                if (!_builder.BreakPage(_state.Para))
                    ReportEmptyPage();
            }
            else if (_bodySectionBreak == BodySectionBreak.Column)
                _builder.BreakColumn(_state.Para);
        }

        private void SetBodySectionBreak(BodySectionBreak kind)
        {
            if (_bodySectionPending)
            {
                _bodySectionBreak = kind;
                _bodySectionHasProperties = true;
            }
        }

        private void ReportCodePageIfNeeded(byte value)
        {
            if (!_reportedCodePage && value >= 0x80 && !RtfCodePage.IsFullySupported(_state.CodePage))
            {
                _diagnostics.Add(DocumentDiagnostic.Info(
                    "rtf.codepage",
                    $"Code page {_state.CodePage} is not fully supported; high bytes used a Latin-1 fallback. Unicode (\\u) text is unaffected."));
                _reportedCodePage = true;
            }
        }

        /// <summary>The <c>\l</c> switch, which makes the field's target a bookmark name.</summary>
        private static bool HasLocalSwitch(ReadOnlySpan<char> switches)
        {
            for (int i = 0; i + 1 < switches.Length; i++)
            {
                if (switches[i] == '\\' && (switches[i + 1] == 'l' || switches[i + 1] == 'L'))
                    return true;
            }

            return false;
        }

        private string? ExtractHyperlink(string instruction)
        {
            int hyperlink = instruction.IndexOf("HYPERLINK", StringComparison.OrdinalIgnoreCase);
            if (hyperlink < 0)
                return null;

            int open = instruction.IndexOf('"', hyperlink);
            if (open < 0)
                return null;
            int close = instruction.IndexOf('"', open + 1);
            if (close < 0)
                return null;

            string url = instruction.Substring(open + 1, close - open - 1).Trim();

            // The \l switch makes the quoted value a bookmark name rather than
            // an address, which is how Word writes a same-document reference.
            // The instruction carries it as literal text, because the field
            // escapes the backslash. Without this the name reached the
            // allow-list on its own, failed it, and was reported as an
            // unsupported scheme - of which it had none.
            if (HasLocalSwitch(instruction.AsSpan(hyperlink, open - hyperlink)))
                url = "#" + url;

            if (DocumentLinkTarget.IsAllowed(url))
                return url;

            _diagnostics.Add(DocumentDiagnostic.Warning(
                "rtf.link", "A hyperlink with a disallowed or relative target was dropped."));
            return null;
        }


        private static int Clamp(int value) => Math.Clamp(value, 0, 255);

        private static int TwipsToLevel(int twips) =>
            twips <= 0 ? 0 : Math.Clamp((int)Math.Round(twips / 360.0), 0, 32);
    }

    private sealed class Accumulator(int maxParagraphs)
    {
        private readonly List<RichTextParagraph> _paragraphs = [];

        /// <summary>The paragraph a shape met right now would be anchored to.</summary>
        public int ParagraphCount => _paragraphs.Count;
        public bool HasPendingText => _current.Length > 0;
        private readonly int _maxParagraphs = maxParagraphs;
        private RichTextParagraph _current = RichTextParagraph.Create(string.Empty, InlineStyle.Default, ParagraphStyle.Default);

        public bool LimitHit { get; private set; }

        /// <summary>Whether a <c>\page</c> is still waiting for a paragraph.</summary>
        public bool PageBreakPending { get; private set; }

        /// <summary>Whether a <c>\column</c> is still waiting for a paragraph.</summary>
        public bool ColumnBreakPending { get; private set; }

        /// <summary>
        /// Takes a column break before whatever comes next, closing the paragraph
        /// it interrupted the way <see cref="BreakPage"/> does. A page break
        /// already waiting goes further and is the one kept.
        /// </summary>
        public void BreakColumn(ParagraphStyle style)
        {
            if (_current.Length > 0)
                EndParagraph(style);

            ColumnBreakPending = true;
        }

        /// <summary>
        /// Takes a break before whatever comes next, and says whether one was
        /// already waiting with nothing in between - a break that asked for a page
        /// this accumulator has no paragraph to put on it.
        /// </summary>
        /// <remarks>
        /// A break inside a paragraph ends it first. RTF lets <c>\page</c> sit
        /// anywhere in the character stream, so one document writes
        /// <c>text\page more\par</c> and another <c>text\par\page more\par</c> for
        /// the same break, and the two only reach the same model if the first
        /// spelling closes the paragraph the break interrupted. Holding the break
        /// against the paragraph being built instead would move it in front of the
        /// text it was written after, which is the one place it does not belong.
        /// </remarks>
        public bool BreakPage(ParagraphStyle style)
        {
            bool displaced = PageBreakPending && _current.Length == 0;
            if (_current.Length > 0)
                EndParagraph(style);

            PageBreakPending = true;
            return !displaced;
        }

        public void Append(string text, InlineStyle style)
        {
            // Once the paragraph cap is hit, drop further text so memory stays bounded.
            if (LimitHit || text.Length == 0)
                return;
            _current = _current.InsertText(_current.Length, text, style);
        }

        public void EndParagraph(ParagraphStyle style, InlineStyle mark = default)
        {
            if (LimitHit)
                return;
            if (_paragraphs.Count >= _maxParagraphs)
            {
                LimitHit = true;
                return;
            }

            _paragraphs.Add(_current.WithParagraphStyle(Spend(style)).WithMarkStyle(mark));
            _current = RichTextParagraph.Create(string.Empty, InlineStyle.Default, style);
        }

        public void EndPendingParagraph(ParagraphStyle style)
        {
            if (_current.Length > 0)
                EndParagraph(style);
        }

        public RichTextDocument Build(ParagraphStyle finalStyle)
        {
            if (!LimitHit && (_current.Length > 0 || _paragraphs.Count == 0))
                _paragraphs.Add(_current.WithParagraphStyle(Spend(finalStyle)));

            return RichTextDocument.FromParagraphs(_paragraphs);
        }

        /// <summary>
        /// The style a closing paragraph takes, with any waiting <c>\page</c>
        /// spent on it.
        /// </summary>
        /// <remarks>
        /// The first paragraph to close after a break is the one the break
        /// belongs to, and it takes the break away with it: a flag left set would
        /// start the paragraph after that on a new page as well, so one break in
        /// the source would become two in the model.
        /// </remarks>
        private ParagraphStyle Spend(ParagraphStyle style)
        {
            if (PageBreakPending)
            {
                PageBreakPending = false;
                ColumnBreakPending = false;
                return style with { PageBreakBefore = true };
            }

            if (ColumnBreakPending)
            {
                ColumnBreakPending = false;
                return style with { ColumnBreakBefore = true };
            }

            return style;
        }
    }
}
