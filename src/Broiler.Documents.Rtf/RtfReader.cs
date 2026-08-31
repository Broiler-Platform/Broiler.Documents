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
            _embeddedDecodingRequested = options.DecodeEmbeddedObjects;
            _builder = new Accumulator(options.Limits.MaxParagraphCount);
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
            RichTextDocument document = _builder.Build(_state.Para);
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
                case "info":
                case "stylesheet":
                case "header":
                case "footer":
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
            if (star)
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
                case RtfDestination.FieldResult: HandleBodyText(text); break;
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

        private void EndParagraph()
        {
            FlushPending();
            _builder.EndParagraph(_state.Para);
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
                _builder.Append(_pending.ToString(), _pendingStyle);

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
