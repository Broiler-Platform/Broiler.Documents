using System.Globalization;
using System;
using System.Collections.Generic;
using System.Text;
using Broiler.Documents.Model;
using Broiler.Graphics;

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
        return new Worker(effective, tokenized.Diagnostics).Run(tokenized.Tokens);
    }

    private struct State
    {
        public InlineStyle Char;
        public ParagraphStyle Para;
        public RtfDestination Dest;
        public int UnicodeSkip;
        public int CodePage;
    }

    private sealed class Worker
    {
        private readonly List<DocumentDiagnostic> _diagnostics = [];
        private readonly RtfColorTable _colors = new();
        private readonly RtfFontTable _fonts = new();
        private readonly Stack<State> _stack = new();
        private readonly Accumulator _builder;
        private readonly Dictionary<RtfDestination, Accumulator> _running = new();
        private int _paperWidth;
        private int _paperHeight;
        private int _marginLeft;
        private int _marginRight;
        private int _marginTop;
        private int _marginBottom;
        private int _headerDistance;
        private int _footerDistance;
        private readonly StringBuilder _shapePropertyName = new();
        private readonly StringBuilder _shapePropertyValue = new();
        private readonly List<DocumentShape> _shapes = [];
        private Accumulator? _shapeText;
        private int _shapeLeft;
        private int _shapeTop;
        private int _shapeRight;
        private int _shapeBottom;
        private BColor _shapeFillStart = BColor.Empty;
        private BColor _shapeFillEnd = BColor.Empty;
        private BColor _shapeLineColor = BColor.Empty;
        private double _shapeFillAngle;
        private bool _shapeHasFill;
        private bool _shapeGradient;
        private bool _shapeHasLine = true;
        private int _maxParagraphs;

        // Buffered same-style body text (flushed when the style changes or the paragraph ends).
        private readonly StringBuilder _pending = new();
        private InlineStyle _pendingStyle;
        private bool _hasPending;

        // Table/field parsing scratch.
        private readonly StringBuilder _fontName = new();
        private readonly StringBuilder _fieldInstruction = new();
        private int _fontIndex;
        private int _fontCharset;
        private int _r, _g, _b;
        private bool _colorSeen;
        private string? _fieldLink;

        private int _pendingUnicodeSkip;
        private bool _sawStar;
        private bool _reportedCodePage;
        private bool _reportedEmbedded;
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
            _maxParagraphs = options.Limits.MaxParagraphCount;
            _state = new State
            {
                Char = InlineStyle.Default,
                Para = ParagraphStyle.Default,
                Dest = RtfDestination.Normal,
                UnicodeSkip = 1,
                CodePage = options.DefaultCodePage,
            };
        }

        public DocumentReadResult Run(IReadOnlyList<RtfToken> tokens)
        {
            foreach (RtfToken token in tokens)
            {
                switch (token.Type)
                {
                    case RtfTokenType.GroupStart:
                        _sawStar = false;
                        // Pending text belongs to the destination that produced it.
                        // Flushing lazily was fine while everything landed in one
                        // accumulator; now a header's text would be attributed to
                        // whatever destination happened to be current at flush time.
                        FlushPending();
                        _stack.Push(_state);
                        break;
                    case RtfTokenType.GroupEnd:
                        HandleGroupEnd();
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
            RichTextDocument document = _builder.Build(_state.Para)
                .WithRunningContent(BuildRunningContent())
                .WithPageGeometry(BuildPageGeometry());
            if (_shapes.Count > 0)
                document = document.WithShapes(_shapes);
            if (_builder.LimitHit)
                _diagnostics.Add(DocumentDiagnostic.Warning("rtf.paragraphs", "Document exceeded MaxParagraphCount; extra paragraphs were dropped."));

            return new DocumentReadResult(document, _diagnostics, DocumentReadResult.StatusFrom(_diagnostics));
        }

        private void HandleGroupEnd()
        {
            _sawStar = false;
            State closing = _state;

            if (closing.Dest == RtfDestination.FieldInstruction)
                _fieldLink = ExtractHyperlink(_fieldInstruction.ToString());
            else if (closing.Dest == RtfDestination.FontTable && _fontName.Length > 0)
                CommitFont();
            else if (closing.Dest == RtfDestination.Field)
                _fieldLink = null;

            FlushPending();
            if (closing.Dest == RtfDestination.ShapeProperty)
                ApplyShapeProperty();
            else if (closing.Dest == RtfDestination.Shape)
                FinishShape();

            if (_stack.Count > 0)
                _state = _stack.Pop();
        }

        private void HandleControlWord(RtfToken token)
        {
            bool star = _sawStar;
            _sawStar = false;
            string kw = token.Keyword;

            // Destination-setting keywords take effect regardless of the current destination.
            switch (kw)
            {
                case "fonttbl":
                    _state.Dest = RtfDestination.FontTable;
                    _fontName.Clear();
                    return;
                case "colortbl":
                    _state.Dest = RtfDestination.ColorTable;
                    ResetColor();
                    return;
                case "field":
                    _state.Dest = RtfDestination.Field;
                    _fieldLink = null;
                    return;
                case "fldinst":
                    _state.Dest = RtfDestination.FieldInstruction;
                    _fieldInstruction.Clear();
                    return;
                case "fldrslt":
                    _state.Dest = RtfDestination.FieldResult;
                    if (_fieldLink is not null)
                        _state.Char = _state.Char with { LinkHref = _fieldLink };
                    return;
                case "pict":
                case "object":
                    _state.Dest = RtfDestination.Skip;
                    if (!_reportedEmbedded)
                    {
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

                    return;
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

                case "info":
                case "stylesheet":
                case "footnote":
                case "annotation":
                case "colorschememapping":
                case "latentstyles":
                case "datastore":
                case "themedata":
                case "generator":
                case "listtable":
                case "listoverridetable":
                case "revtbl":
                case "pntext":
                    _state.Dest = RtfDestination.Skip;
                    return;
            }

            // An unknown ignorable destination (\*\something) is skipped safely.
            // \* says to ignore what the reader does not understand, so a
            // destination it does understand has to be let through - a shape
            // arrives as {\*\shpinst ...} and would otherwise be dropped here,
            // several hundred lines before the case that handles it.
            if (star && kw is not "shpinst")
            {
                _state.Dest = RtfDestination.Skip;
                return;
            }

            switch (_state.Dest)
            {
                case RtfDestination.Skip:
                case RtfDestination.Field:
                case RtfDestination.FieldInstruction:
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
            switch (kw)
            {
                case "b": _state.Char = _state.Char with { Bold = !(has && p == 0) }; break;
                case "i": _state.Char = _state.Char with { Italic = !(has && p == 0) }; break;
                case "strike": _state.Char = _state.Char with { Strikethrough = !(has && p == 0) }; break;
                case "ul": _state.Char = _state.Char with { Underline = !(has && p == 0) }; break;
                case "ulnone": _state.Char = _state.Char with { Underline = false }; break;
                case "caps": _state.Char = ApplyCapitalization(_state.Char, TextCapitalization.AllCaps, !(has && p == 0)); break;
                case "scaps": _state.Char = ApplyCapitalization(_state.Char, TextCapitalization.SmallCaps, !(has && p == 0)); break;
                case "plain": _state.Char = InlineStyle.Default; break;
                case "fs": _state.Char = _state.Char with { FontSize = has ? p / 2f : null }; break;
                case "f":
                    int charset = _fonts.GetCharset(p);
                    if (charset >= 0)
                        _state.CodePage = RtfCodePage.CharsetToCodePage(charset);
                    _state.Char = _state.Char with { FontFamily = _fonts.GetName(p) };
                    break;
                case "cf": _state.Char = _state.Char with { Foreground = _colors.Get(p) }; break;
                case "cb":
                case "highlight": _state.Char = _state.Char with { Background = _colors.Get(p) }; break;
                case "ansicpg": if (has) _state.CodePage = p; break;
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

                // A drawing. Its geometry rides on the control words here and
                // its paint arrives as {\sp{\sn name}{\sv value}} pairs, so the
                // shape is assembled as the group is walked and finished when
                // the group closes.
                // \shp opens the shape but does not own the destination: \shpinst
                // does, and only one group may close as the shape or it would be
                // finished twice - once for the inner group and once for the outer.
                case "shp": BeginShape(); return;
                case "shpinst": _state.Dest = RtfDestination.Shape; return;
                case "shpleft": if (has) _shapeLeft = p; return;
                case "shptop": if (has) _shapeTop = p; return;
                case "shpright": if (has) _shapeRight = p; return;
                case "shpbottom": if (has) _shapeBottom = p; return;
                case "sp": _state.Dest = RtfDestination.ShapeProperty; BeginShapeProperty(); return;
                case "sn": _state.Dest = RtfDestination.ShapePropertyName; return;
                case "sv": _state.Dest = RtfDestination.ShapePropertyValue; return;
                case "shptxt": _state.Dest = RtfDestination.ShapeText; return;

                case "pard": _state.Para = ParagraphStyle.Default; break;
                case "ql": _state.Para = _state.Para with { Alignment = TextAlignment.Left }; break;
                case "qc": _state.Para = _state.Para with { Alignment = TextAlignment.Center }; break;
                case "qr": _state.Para = _state.Para with { Alignment = TextAlignment.Right }; break;
                case "qj": _state.Para = _state.Para with { Alignment = TextAlignment.Justify }; break;
                case "li": _state.Para = _state.Para with { IndentLevel = TwipsToLevel(p) }; break;
                case "sb": _state.Para = _state.Para with { SpacingBefore = has ? p / 20f : 0f }; break;
                case "sa": _state.Para = _state.Para with { SpacingAfter = has ? p / 20f : 0f }; break;

                case "par":
                case "row":
                    EndParagraph();
                    break;
                case "line": AppendChar(0x2028); break; // soft line break
                case "tab":
                case "cell": AppendBody("\t"); break;
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

        private void HandleControlSymbol(char symbol)
        {
            if (symbol == '*')
            {
                _sawStar = true;
                return;
            }

            _sawStar = false;

            if (_state.Dest is not (RtfDestination.Normal or RtfDestination.FieldResult))
                return;

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
            if (_state.Dest is not (RtfDestination.Normal or RtfDestination.FieldResult))
                return;

            if (_pendingUnicodeSkip > 0)
            {
                _pendingUnicodeSkip--;
                return;
            }

            ReportCodePageIfNeeded(value);
            AppendChar(RtfCodePage.DecodeByte(value, _state.CodePage));
        }

        private void HandleText(string text)
        {
            _sawStar = false;
            switch (_state.Dest)
            {
                case RtfDestination.FontTable: HandleFontTableText(text); break;
                case RtfDestination.ColorTable: HandleColorTableText(text); break;
                case RtfDestination.FieldInstruction: _fieldInstruction.Append(text); break;
                case RtfDestination.Normal:
                case RtfDestination.FieldResult:
                case RtfDestination.Header:
                case RtfDestination.HeaderFirst:
                case RtfDestination.HeaderEven:
                case RtfDestination.Footer:
                case RtfDestination.FooterFirst:
                case RtfDestination.FooterEven:
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
            if (_state.Dest is not (RtfDestination.Normal or RtfDestination.FieldResult))
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
                default:
                    break;
            }
        }

        private void HandleFontTableText(string text)
        {
            int semicolon = text.IndexOf(';');
            if (semicolon < 0)
            {
                _fontName.Append(text);
                return;
            }

            _fontName.Append(text, 0, semicolon);
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
                if (_state.Dest is RtfDestination.Normal or RtfDestination.FieldResult)
                    return _builder;

                if (_state.Dest == RtfDestination.ShapeText)
                    return _shapeText ??= new Accumulator(_maxParagraphs);

                if (!IsRunning(_state.Dest))
                    return _builder;

                if (!_running.TryGetValue(_state.Dest, out Accumulator? accumulator))
                {
                    accumulator = new Accumulator(_maxParagraphs);
                    _running[_state.Dest] = accumulator;
                }

                return accumulator;
            }
        }

        private static bool IsRunning(RtfDestination destination) => destination is
            RtfDestination.Header or RtfDestination.HeaderFirst or RtfDestination.HeaderEven or
            RtfDestination.Footer or RtfDestination.FooterFirst or RtfDestination.FooterEven;

        /// <summary>
        /// The page the section control words describe, or null when the document
        /// gave no paper size. Twips throughout, 20 to the point.
        /// </summary>
        private PageGeometry? BuildPageGeometry()
        {
            if (_paperWidth <= 0 || _paperHeight <= 0)
                return null;

            var geometry = new PageGeometry(
                _paperWidth / 20d,
                _paperHeight / 20d,
                _marginLeft / 20d,
                _marginRight / 20d,
                _marginTop / 20d,
                _marginBottom / 20d,
                _headerDistance / 20d,
                _footerDistance / 20d);

            if (geometry.IsUsable)
                return geometry;

            _diagnostics.Add(DocumentDiagnostic.Warning(
                "rtf.page.geometry",
                "RTF section properties gave a page with no room to write on; the page was not read."));
            return null;
        }

        private void BeginShape()
        {
            _shapeLeft = 0;
            _shapeTop = 0;
            _shapeRight = 0;
            _shapeBottom = 0;
            _shapeFillStart = BColor.Empty;
            _shapeFillEnd = BColor.Empty;
            _shapeLineColor = BColor.Empty;
            _shapeFillAngle = 0;
            _shapeHasFill = false;
            _shapeGradient = false;
            _shapeHasLine = true;
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
                case "fillAngle":
                    // Word states it in 65536ths of a degree.
                    _shapeFillAngle = number / 65536d;
                    break;
                case "fFilled":
                    _shapeHasFill = number != 0;
                    break;
                case "lineColor":
                    _shapeLineColor = ShapeColor(number);
                    break;
                case "fLine":
                    _shapeHasLine = number != 0;
                    break;
                default:
                    break;
            }
        }

        private static BColor ShapeColor(long value) =>
            new((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

        /// <summary>Turns the walked group into a shape, when it described one.</summary>
        private void FinishShape()
        {
            double width = (_shapeRight - _shapeLeft) / 20d;
            double height = (_shapeBottom - _shapeTop) / 20d;
            IReadOnlyList<RichTextParagraph> paragraphs = _shapeText is null
                ? []
                : _shapeText.Build(ParagraphStyle.Default).Paragraphs;
            if (paragraphs.Count == 1 && paragraphs[0].Length == 0)
                paragraphs = [];

            ShapeFill? fill = null;
            if (_shapeHasFill && !_shapeFillStart.IsEmpty)
            {
                fill = _shapeGradient && !_shapeFillEnd.IsEmpty
                    ? new ShapeFill(_shapeFillStart, _shapeFillEnd, _shapeFillAngle)
                    : ShapeFill.Solid(_shapeFillStart);
            }

            _shapeText = null;
            if (width <= 0 || height <= 0 || (fill is null && paragraphs.Count == 0))
                return;

            _shapes.Add(new DocumentShape(
                _builder.ParagraphCount,
                _shapeLeft / 20d,
                _shapeTop / 20d,
                width,
                height,
                fill,
                _shapeHasLine ? _shapeLineColor : BColor.Empty,
                paragraphs));
        }

        /// <summary>The headers and footers the document's running destinations collected.</summary>
        private RunningContent BuildRunningContent()
        {
            RunningContent content = RunningContent.Empty;
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

        private void EndParagraph()
        {
            FlushPending();
            Active.EndParagraph(_state.Para);
        }

        private void AppendChar(int code) => AppendBody(((char)code).ToString());

        private void AppendBody(string text)
        {
            if (text.Length == 0)
                return;

            if (_hasPending && !_pendingStyle.Equals(_state.Char))
                FlushPending();

            if (!_hasPending)
            {
                _pendingStyle = _state.Char;
                _hasPending = true;
            }

            _pending.Append(text);
        }

        private void FlushPending()
        {
            if (_hasPending && _pending.Length > 0)
                Active.Append(_pending.ToString(), _pendingStyle);

            _pending.Clear();
            _hasPending = false;
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
            if (IsAllowedUrl(url))
                return url;

            _diagnostics.Add(DocumentDiagnostic.Warning(
                "rtf.link", "A hyperlink with an unsupported URL scheme was dropped."));
            return null;
        }

        private static bool IsAllowedUrl(string url) =>
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

        private static int Clamp(int value) => Math.Clamp(value, 0, 255);

        private static int TwipsToLevel(int twips) =>
            twips <= 0 ? 0 : Math.Clamp((int)Math.Round(twips / 360.0), 0, 32);
    }

    private sealed class Accumulator
    {
        private readonly List<RichTextParagraph> _paragraphs = [];

        /// <summary>The paragraph a shape met right now would be anchored to.</summary>
        public int ParagraphCount => _paragraphs.Count;
        private readonly int _maxParagraphs;
        private RichTextParagraph _current = RichTextParagraph.Create(string.Empty, InlineStyle.Default, ParagraphStyle.Default);

        public Accumulator(int maxParagraphs) => _maxParagraphs = maxParagraphs;

        public bool LimitHit { get; private set; }

        public void Append(string text, InlineStyle style)
        {
            // Once the paragraph cap is hit, drop further text so memory stays bounded.
            if (LimitHit || text.Length == 0)
                return;
            _current = _current.InsertText(_current.Length, text, style);
        }

        public void EndParagraph(ParagraphStyle style)
        {
            if (LimitHit)
                return;
            if (_paragraphs.Count >= _maxParagraphs)
            {
                LimitHit = true;
                return;
            }

            _paragraphs.Add(_current.WithParagraphStyle(style));
            _current = RichTextParagraph.Create(string.Empty, InlineStyle.Default, style);
        }

        public RichTextDocument Build(ParagraphStyle finalStyle)
        {
            if (!LimitHit && (_current.Length > 0 || _paragraphs.Count == 0))
                _paragraphs.Add(_current.WithParagraphStyle(finalStyle));

            return RichTextDocument.FromParagraphs(_paragraphs);
        }
    }
}
