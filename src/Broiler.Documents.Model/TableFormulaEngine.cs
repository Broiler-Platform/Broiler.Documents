using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Broiler.Documents.Model;

/// <summary>
/// Value types that can be stored in or produced by a spreadsheet or table cell formula.
/// </summary>
public enum CellValueType
{
    Empty,
    Number,
    Text,
    Boolean,
    Error
}

/// <summary>
/// Standard spreadsheet error codes representing calculation or evaluation failures.
/// </summary>
public enum FormulaError
{
    None = 0,
    Div0,      // #DIV/0!
    Value,     // #VALUE!
    Ref,       // #REF!
    Name,      // #NAME?
    Num,       // #NUM!
    NA,        // #N/A
    Cycle,     // #CYCLE!
    Syntax     // #SYNTAX!
}

/// <summary>
/// Helper extensions for parsing and formatting <see cref="FormulaError"/> values.
/// </summary>
public static class FormulaErrorExtensions
{
    /// <summary>Returns the standard string representation of a formula error (e.g. "#DIV/0!").</summary>
    public static string ToErrorString(this FormulaError error) => error switch
    {
        FormulaError.Div0 => "#DIV/0!",
        FormulaError.Value => "#VALUE!",
        FormulaError.Ref => "#REF!",
        FormulaError.Name => "#NAME?",
        FormulaError.Num => "#NUM!",
        FormulaError.NA => "#N/A",
        FormulaError.Cycle => "#CYCLE!",
        FormulaError.Syntax => "#SYNTAX!",
        _ => "#ERROR!"
    };

    /// <summary>Attempts to parse a standard formula error string.</summary>
    public static bool TryParse(string text, out FormulaError error)
    {
        error = FormulaError.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string trimmed = text.Trim();
        if (string.Equals(trimmed, "#DIV/0!", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.Div0; return true; }
        if (string.Equals(trimmed, "#VALUE!", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.Value; return true; }
        if (string.Equals(trimmed, "#REF!", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.Ref; return true; }
        if (string.Equals(trimmed, "#NAME?", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.Name; return true; }
        if (string.Equals(trimmed, "#NUM!", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.Num; return true; }
        if (string.Equals(trimmed, "#N/A!", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "#N/A", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.NA; return true; }
        if (string.Equals(trimmed, "#CYCLE!", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.Cycle; return true; }
        if (string.Equals(trimmed, "#SYNTAX!", StringComparison.OrdinalIgnoreCase)) { error = FormulaError.Syntax; return true; }
        return false;
    }
}

/// <summary>
/// An immutable, strongly-typed cell value representing a number, text, boolean, error, or empty value.
/// </summary>
public readonly record struct CellValue : IEquatable<CellValue>
{
    public CellValueType Type { get; }
    public double Number { get; }
    public string? Text { get; }
    public bool Boolean { get; }
    public FormulaError Error { get; }

    private CellValue(CellValueType type, double number = 0, string? text = null, bool boolean = false, FormulaError error = FormulaError.None)
    {
        Type = type;
        Number = number;
        Text = text;
        Boolean = boolean;
        Error = error;
    }

    public static CellValue Empty { get; } = new(CellValueType.Empty);
    public static CellValue True { get; } = new(CellValueType.Boolean, boolean: true);
    public static CellValue False { get; } = new(CellValueType.Boolean, boolean: false);

    public static CellValue FromNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return new CellValue(CellValueType.Error, error: FormulaError.Num);
        return new CellValue(CellValueType.Number, number: value);
    }

    public static CellValue FromText(string text) =>
        new(CellValueType.Text, text: text ?? string.Empty);

    public static CellValue FromBoolean(bool value) =>
        value ? True : False;

    public static CellValue FromError(FormulaError error) =>
        new(CellValueType.Error, error: error);

    public bool IsEmpty => Type == CellValueType.Empty;
    public bool IsNumber => Type == CellValueType.Number;
    public bool IsText => Type == CellValueType.Text;
    public bool IsBoolean => Type == CellValueType.Boolean;
    public bool IsError => Type == CellValueType.Error;

    /// <summary>Coerces the value to a double if possible.</summary>
    public bool AsDouble(out double result)
    {
        switch (Type)
        {
            case CellValueType.Number:
                result = Number;
                return true;
            case CellValueType.Boolean:
                result = Boolean ? 1.0 : 0.0;
                return true;
            case CellValueType.Empty:
                result = 0.0;
                return true;
            case CellValueType.Text:
                if (double.TryParse(Text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result))
                    return true;
                if (double.TryParse(Text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result))
                    return true;
                result = 0;
                return false;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>Coerces the value to a boolean if possible.</summary>
    public bool AsBoolean(out bool result)
    {
        switch (Type)
        {
            case CellValueType.Boolean:
                result = Boolean;
                return true;
            case CellValueType.Number:
                result = Number != 0;
                return true;
            case CellValueType.Empty:
                result = false;
                return true;
            case CellValueType.Text:
                if (string.Equals(Text, "TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    return true;
                }
                if (string.Equals(Text, "FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    result = false;
                    return true;
                }
                result = false;
                return false;
            default:
                result = false;
                return false;
        }
    }

    /// <summary>
    /// Parses a raw string into a strongly-typed <see cref="CellValue"/>.
    /// </summary>
    public static CellValue Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Empty;

        string trimmed = raw.Trim();
        if (trimmed.StartsWith('#') && FormulaErrorExtensions.TryParse(trimmed, out FormulaError err))
            return FromError(err);

        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase))
            return True;
        if (string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase))
            return False;

        if (trimmed.EndsWith('%') && double.TryParse(trimmed[..^1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double percentVal))
            return FromNumber(percentVal / 100.0);

        if (trimmed.StartsWith('$') && double.TryParse(trimmed[1..].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double currencyVal))
            return FromNumber(currencyVal);

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double num))
            return FromNumber(num);

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out double numLocal))
            return FromNumber(numLocal);

        return FromText(raw);
    }

    /// <summary>Formats the value as a clean display string.</summary>
    public string Format() => Type switch
    {
        CellValueType.Empty => string.Empty,
        CellValueType.Number => FormatNumber(Number),
        CellValueType.Text => Text ?? string.Empty,
        CellValueType.Boolean => Boolean ? "TRUE" : "FALSE",
        CellValueType.Error => Error.ToErrorString(),
        _ => string.Empty
    };

    private static string FormatNumber(double val)
    {
        if (val == Math.Floor(val) && !double.IsInfinity(val) && Math.Abs(val) < 1e15)
            return ((long)val).ToString(CultureInfo.InvariantCulture);
        return val.ToString("G15", CultureInfo.InvariantCulture);
    }

    public override string ToString() => Format();
}

/// <summary>
/// A 0-indexed coordinate pair (Row, Column) within a table grid.
/// </summary>
public readonly record struct CellAddress(int Row, int Column) : IComparable<CellAddress>
{
    public int CompareTo(CellAddress other)
    {
        int rowCompare = Row.CompareTo(other.Row);
        return rowCompare != 0 ? rowCompare : Column.CompareTo(other.Column);
    }

    /// <summary>Returns standard A1 notation for this cell (e.g. "A1", "B3", "AA12").</summary>
    public string Format() => $"{ColumnToLetters(Column)}{Row + 1}";

    public override string ToString() => Format();

    /// <summary>Converts a 0-indexed column index into uppercase letters (0 -> "A", 25 -> "Z", 26 -> "AA").</summary>
    public static string ColumnToLetters(int columnIndex)
    {
        if (columnIndex < 0) return string.Empty;
        Span<char> buffer = stackalloc char[8];
        int pos = buffer.Length;
        int c = columnIndex;
        while (c >= 0)
        {
            buffer[--pos] = (char)('A' + (c % 26));
            c = (c / 26) - 1;
        }
        return new string(buffer[pos..]);
    }

    /// <summary>Converts uppercase column letters into a 0-indexed column index ("A" -> 0, "Z" -> 25, "AA" -> 26).</summary>
    public static bool TryLettersToColumn(ReadOnlySpan<char> span, out int column)
    {
        column = 0;
        if (span.IsEmpty) return false;
        int acc = 0;
        for (int i = 0; i < span.Length; i++)
        {
            char ch = span[i];
            if (ch >= 'a' && ch <= 'z') ch = (char)(ch - 'a' + 'A');
            if (ch < 'A' || ch > 'Z') return false;
            acc = checked(acc * 26 + (ch - 'A' + 1));
        }
        column = acc - 1;
        return true;
    }

    /// <summary>Attempts to parse standard A1 cell notation (e.g. "A1", "$B$2", "AA12").</summary>
    public static bool TryParse(ReadOnlySpan<char> span, out CellAddress address)
    {
        address = default;
        ReadOnlySpan<char> s = span.Trim();
        if (s.IsEmpty) return false;

        if (s[0] == '$') s = s[1..];
        if (s.IsEmpty) return false;

        int letterCount = 0;
        while (letterCount < s.Length && char.IsLetter(s[letterCount]))
            letterCount++;

        if (letterCount == 0) return false;
        if (!TryLettersToColumn(s[..letterCount], out int col)) return false;

        s = s[letterCount..];
        if (s.IsEmpty) return false;
        if (s[0] == '$') s = s[1..];
        if (s.IsEmpty) return false;

        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int rowNum) || rowNum < 1)
            return false;

        address = new CellAddress(rowNum - 1, col);
        return true;
    }
}

/// <summary>
/// A rectangular range of cell addresses (e.g. "A1:B3", "A:C", "1:5").
/// </summary>
public readonly record struct CellRangeAddress
{
    public CellAddress Start { get; }
    public CellAddress End { get; }
    public bool IsWholeColumn { get; }
    public bool IsWholeRow { get; }
    public int StartRow { get; }
    public int EndRow { get; }
    public int StartCol { get; }
    public int EndCol { get; }

    public CellRangeAddress(CellAddress start, CellAddress end)
    {
        int minRow = Math.Min(start.Row, end.Row);
        int maxRow = Math.Max(start.Row, end.Row);
        int minCol = Math.Min(start.Column, end.Column);
        int maxCol = Math.Max(start.Column, end.Column);
        Start = new CellAddress(minRow, minCol);
        End = new CellAddress(maxRow, maxCol);
        StartRow = minRow;
        EndRow = maxRow;
        StartCol = minCol;
        EndCol = maxCol;
        IsWholeColumn = false;
        IsWholeRow = false;
    }

    private CellRangeAddress(int startRow, int endRow, int startCol, int endCol, bool isWholeCol, bool isWholeRow)
    {
        StartRow = Math.Min(startRow, endRow);
        EndRow = Math.Max(startRow, endRow);
        StartCol = Math.Min(startCol, endCol);
        EndCol = Math.Max(startCol, endCol);
        Start = new CellAddress(StartRow, StartCol);
        End = new CellAddress(EndRow, EndCol);
        IsWholeColumn = isWholeCol;
        IsWholeRow = isWholeRow;
    }

    public static CellRangeAddress ForColumns(int startCol, int endCol) =>
        new(0, int.MaxValue, startCol, endCol, isWholeCol: true, isWholeRow: false);

    public static CellRangeAddress ForRows(int startRow, int endRow) =>
        new(startRow, endRow, 0, int.MaxValue, isWholeCol: false, isWholeRow: true);

    /// <summary>Enumerates all bounded cell coordinates within this range for a table of given dimensions.</summary>
    public IEnumerable<CellAddress> EnumerateCells(int totalRows, int totalColumns)
    {
        if (totalRows <= 0 || totalColumns <= 0) yield break;

        int minR = Math.Clamp(StartRow, 0, totalRows - 1);
        int maxR = Math.Clamp(EndRow, 0, totalRows - 1);
        int minC = Math.Clamp(StartCol, 0, totalColumns - 1);
        int maxC = Math.Clamp(EndCol, 0, totalColumns - 1);

        for (int r = minR; r <= maxR; r++)
        {
            for (int c = minC; c <= maxC; c++)
            {
                yield return new CellAddress(r, c);
            }
        }
    }

    /// <summary>Attempts to parse a range address string (e.g. "A1:B3", "A:C", "1:5").</summary>
    public static bool TryParse(ReadOnlySpan<char> span, out CellRangeAddress range)
    {
        range = default;
        ReadOnlySpan<char> s = span.Trim();
        int colonIdx = s.IndexOf(':');
        if (colonIdx < 0) return false;

        var left = s[..colonIdx].Trim();
        var right = s[(colonIdx + 1)..].Trim();

        if (CellAddress.TryParse(left, out var start) && CellAddress.TryParse(right, out var end))
        {
            range = new CellRangeAddress(start, end);
            return true;
        }

        string leftClean = left.ToString().Replace("$", "");
        string rightClean = right.ToString().Replace("$", "");
        if (CellAddress.TryLettersToColumn(leftClean, out int c1) && CellAddress.TryLettersToColumn(rightClean, out int c2))
        {
            range = ForColumns(c1, c2);
            return true;
        }

        if (int.TryParse(leftClean, out int r1) && int.TryParse(rightClean, out int r2) && r1 >= 1 && r2 >= 1)
        {
            range = ForRows(r1 - 1, r2 - 1);
            return true;
        }

        return false;
    }
}

/// <summary>
/// Directional range keywords used in word-processor tables.
/// </summary>
public enum DirectionalRangeKind
{
    Above,
    Below,
    Left,
    Right
}

/// <summary>
/// Abstract syntax tree node representing a parsed formula expression.
/// </summary>
public abstract record FormulaNode;

public sealed record LiteralNode(CellValue Value) : FormulaNode;

public sealed record CellRefNode(CellAddress Address) : FormulaNode;

public sealed record CellRangeNode(CellRangeAddress Range) : FormulaNode;

public sealed record DirectionalRangeNode(DirectionalRangeKind Kind) : FormulaNode;

public enum UnaryOp { Plus, Negate, Percent }

public sealed record UnaryOpNode(UnaryOp Op, FormulaNode Operand) : FormulaNode;

public enum BinaryOp
{
    Add, Sub, Mul, Div, Pow, Concat,
    Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual
}

public sealed record BinaryOpNode(BinaryOp Op, FormulaNode Left, FormulaNode Right) : FormulaNode;

public sealed record FunctionCallNode(string Name, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

/// <summary>
/// Fast tokenizer and parser for Excel and word-processor table formulas.
/// </summary>
public static class FormulaParser
{
    private enum TokenType
    {
        Number,
        String,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        Caret,
        Percent,
        Ampersand,
        Equal,
        NotEqual,
        Less,
        LessEqual,
        Greater,
        GreaterEqual,
        LParen,
        RParen,
        Comma,
        Colon,
        EndOfInput
    }

    private readonly record struct Token(TokenType Type, string StringValue, double NumberValue = 0);

    public static bool TryParse(string formula, out FormulaNode? ast, out FormulaError error)
    {
        try
        {
            ast = Parse(formula);
            error = FormulaError.None;
            return true;
        }
        catch
        {
            ast = null;
            error = FormulaError.Syntax;
            return false;
        }
    }

    public static FormulaNode Parse(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return new LiteralNode(CellValue.Empty);

        string text = formula.Trim();
        if (text.StartsWith('='))
            text = text[1..].Trim();

        var tokens = Tokenize(text);
        var parser = new ParserState(tokens);
        var ast = parser.ParseExpression();
        parser.Expect(TokenType.EndOfInput);
        return ast;
    }

    private static List<Token> Tokenize(string formula)
    {
        var tokens = new List<Token>();
        int i = 0;
        int n = formula.Length;

        while (i < n)
        {
            char ch = formula[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            // String literal
            if (ch == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < n)
                {
                    if (formula[i] == '"')
                    {
                        if (i + 1 < n && formula[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++; // closing quote
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(formula[i]);
                        i++;
                    }
                }
                tokens.Add(new Token(TokenType.String, sb.ToString()));
                continue;
            }

            // Number literal
            if (char.IsDigit(ch) || (ch == '.' && i + 1 < n && char.IsDigit(formula[i + 1])))
            {
                int start = i;
                bool hasDot = false;
                while (i < n && (char.IsDigit(formula[i]) || (!hasDot && formula[i] == '.')))
                {
                    if (formula[i] == '.') hasDot = true;
                    i++;
                }
                if (i < n && (formula[i] == 'e' || formula[i] == 'E'))
                {
                    i++;
                    if (i < n && (formula[i] == '+' || formula[i] == '-')) i++;
                    while (i < n && char.IsDigit(formula[i])) i++;
                }

                string numStr = formula[start..i];
                double val = double.Parse(numStr, CultureInfo.InvariantCulture);
                tokens.Add(new Token(TokenType.Number, numStr, val));
                continue;
            }

            // Error literal or identifier
            if (ch == '#' || ch == '$' || char.IsLetter(ch) || ch == '_')
            {
                int start = i;
                if (ch == '#')
                {
                    i++;
                    while (i < n && (char.IsLetterOrDigit(formula[i]) || formula[i] == '/' || formula[i] == '!' || formula[i] == '?'))
                        i++;
                }
                else
                {
                    while (i < n && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_' || formula[i] == '$'))
                        i++;
                }
                string id = formula[start..i];
                tokens.Add(new Token(TokenType.Identifier, id));
                continue;
            }

            // Operators
            switch (ch)
            {
                case '+': tokens.Add(new Token(TokenType.Plus, "+")); i++; break;
                case '-': tokens.Add(new Token(TokenType.Minus, "-")); i++; break;
                case '*': tokens.Add(new Token(TokenType.Star, "*")); i++; break;
                case '/': tokens.Add(new Token(TokenType.Slash, "/")); i++; break;
                case '^': tokens.Add(new Token(TokenType.Caret, "^")); i++; break;
                case '%': tokens.Add(new Token(TokenType.Percent, "%")); i++; break;
                case '&': tokens.Add(new Token(TokenType.Ampersand, "&")); i++; break;
                case '(': tokens.Add(new Token(TokenType.LParen, "(")); i++; break;
                case ')': tokens.Add(new Token(TokenType.RParen, ")")); i++; break;
                case ',': tokens.Add(new Token(TokenType.Comma, ",")); i++; break;
                case ':': tokens.Add(new Token(TokenType.Colon, ":")); i++; break;
                case '=': tokens.Add(new Token(TokenType.Equal, "=")); i++; break;
                case '<':
                    if (i + 1 < n && formula[i + 1] == '>')
                    {
                        tokens.Add(new Token(TokenType.NotEqual, "<>"));
                        i += 2;
                    }
                    else if (i + 1 < n && formula[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenType.LessEqual, "<="));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Less, "<"));
                        i++;
                    }
                    break;
                case '>':
                    if (i + 1 < n && formula[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenType.GreaterEqual, ">="));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(new Token(TokenType.Greater, ">"));
                        i++;
                    }
                    break;
                case '!':
                    if (i + 1 < n && formula[i + 1] == '=')
                    {
                        tokens.Add(new Token(TokenType.NotEqual, "!="));
                        i += 2;
                    }
                    else
                    {
                        throw new FormatException($"Unexpected character '!' at position {i}");
                    }
                    break;
                default:
                    throw new FormatException($"Unexpected character '{ch}' at position {i}");
            }
        }

        tokens.Add(new Token(TokenType.EndOfInput, string.Empty));
        return tokens;
    }

    private sealed class ParserState(List<Token> tokens)
    {
        private int _pos = 0;

        private Token Peek() => _pos < tokens.Count ? tokens[_pos] : new Token(TokenType.EndOfInput, string.Empty);

        private Token Next() => _pos < tokens.Count ? tokens[_pos++] : new Token(TokenType.EndOfInput, string.Empty);

        public void Expect(TokenType type)
        {
            var tok = Next();
            if (tok.Type != type)
                throw new FormatException($"Expected token {type} but found {tok.Type} ('{tok.StringValue}')");
        }

        public FormulaNode ParseExpression(int minPrecedence = 1)
        {
            var left = ParseUnary();

            while (true)
            {
                var token = Peek();
                int prec = GetBinaryPrecedence(token.Type);
                if (prec < minPrecedence)
                    break;

                Next();
                var op = ToBinaryOp(token.Type);
                int nextMinPrec = (op == BinaryOp.Pow) ? prec : prec + 1;
                var right = ParseExpression(nextMinPrec);

                left = new BinaryOpNode(op, left, right);
            }

            return left;
        }

        private FormulaNode ParseUnary()
        {
            var token = Peek();
            if (token.Type == TokenType.Plus)
            {
                Next();
                return new UnaryOpNode(UnaryOp.Plus, ParseUnary());
            }
            if (token.Type == TokenType.Minus)
            {
                Next();
                return new UnaryOpNode(UnaryOp.Negate, ParseUnary());
            }

            var node = ParsePrimary();

            while (Peek().Type == TokenType.Percent)
            {
                Next();
                node = new UnaryOpNode(UnaryOp.Percent, node);
            }

            return node;
        }

        private FormulaNode ParsePrimary()
        {
            var token = Peek();
            if (token.Type == TokenType.Number)
            {
                Next();
                // Check if row range like 1:5
                if (Peek().Type == TokenType.Colon)
                {
                    int saved = _pos;
                    Next(); // consume colon
                    if (Peek().Type == TokenType.Number)
                    {
                        var nextNum = Next();
                        int r1 = (int)token.NumberValue - 1;
                        int r2 = (int)nextNum.NumberValue - 1;
                        if (r1 >= 0 && r2 >= 0)
                            return new CellRangeNode(CellRangeAddress.ForRows(r1, r2));
                    }
                    _pos = saved; // backtrack if not row range
                }
                return new LiteralNode(CellValue.FromNumber(token.NumberValue));
            }

            if (token.Type == TokenType.String)
            {
                Next();
                return new LiteralNode(CellValue.FromText(token.StringValue));
            }

            if (token.Type == TokenType.LParen)
            {
                Next();
                var expr = ParseExpression();
                Expect(TokenType.RParen);
                return expr;
            }

            if (token.Type == TokenType.Identifier)
            {
                Next();
                string text = token.StringValue;

                // Function call
                if (Peek().Type == TokenType.LParen)
                {
                    Next(); // consume '('
                    var args = new List<FormulaNode>();
                    if (Peek().Type != TokenType.RParen)
                    {
                        while (true)
                        {
                            args.Add(ParseExpression());
                            if (Peek().Type == TokenType.Comma)
                            {
                                Next();
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    Expect(TokenType.RParen);
                    return new FunctionCallNode(text.ToUpperInvariant(), args);
                }

                // Boolean literals
                if (string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase))
                    return new LiteralNode(CellValue.True);
                if (string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase))
                    return new LiteralNode(CellValue.False);

                // Directional keywords
                if (string.Equals(text, "ABOVE", StringComparison.OrdinalIgnoreCase))
                    return new DirectionalRangeNode(DirectionalRangeKind.Above);
                if (string.Equals(text, "BELOW", StringComparison.OrdinalIgnoreCase))
                    return new DirectionalRangeNode(DirectionalRangeKind.Below);
                if (string.Equals(text, "LEFT", StringComparison.OrdinalIgnoreCase))
                    return new DirectionalRangeNode(DirectionalRangeKind.Left);
                if (string.Equals(text, "RIGHT", StringComparison.OrdinalIgnoreCase))
                    return new DirectionalRangeNode(DirectionalRangeKind.Right);

                // Cell reference or cell range
                if (CellAddress.TryParse(text, out var cellAddr))
                {
                    if (Peek().Type == TokenType.Colon)
                    {
                        Next(); // consume ':'
                        var nextTok = Next();
                        if (nextTok.Type == TokenType.Identifier && CellAddress.TryParse(nextTok.StringValue, out var endAddr))
                        {
                            return new CellRangeNode(new CellRangeAddress(cellAddr, endAddr));
                        }
                        throw new FormatException($"Invalid range endpoint after ':' at {nextTok.StringValue}");
                    }
                    return new CellRefNode(cellAddr);
                }

                // Column range like A:B
                string cleanText = text.Replace("$", "");
                if (CellAddress.TryLettersToColumn(cleanText, out int col1) && Peek().Type == TokenType.Colon)
                {
                    int saved = _pos;
                    Next(); // consume ':'
                    var nextTok = Peek();
                    if (nextTok.Type == TokenType.Identifier && CellAddress.TryLettersToColumn(nextTok.StringValue.Replace("$", ""), out int col2))
                    {
                        Next();
                        return new CellRangeNode(CellRangeAddress.ForColumns(col1, col2));
                    }
                    _pos = saved; // backtrack
                }

                // Error literals
                if (FormulaErrorExtensions.TryParse(text, out var err))
                    return new LiteralNode(CellValue.FromError(err));

                throw new FormatException($"Unknown identifier or reference: '{text}'");
            }

            throw new FormatException($"Unexpected token: {token.Type} ('{token.StringValue}')");
        }

        private static int GetBinaryPrecedence(TokenType type) => type switch
        {
            TokenType.Equal or TokenType.NotEqual or TokenType.Less or TokenType.LessEqual or
            TokenType.Greater or TokenType.GreaterEqual => 1,
            TokenType.Ampersand => 2,
            TokenType.Plus or TokenType.Minus => 3,
            TokenType.Star or TokenType.Slash => 4,
            TokenType.Caret => 5,
            _ => 0
        };

        private static BinaryOp ToBinaryOp(TokenType type) => type switch
        {
            TokenType.Equal => BinaryOp.Equal,
            TokenType.NotEqual => BinaryOp.NotEqual,
            TokenType.Less => BinaryOp.Less,
            TokenType.LessEqual => BinaryOp.LessEqual,
            TokenType.Greater => BinaryOp.Greater,
            TokenType.GreaterEqual => BinaryOp.GreaterEqual,
            TokenType.Ampersand => BinaryOp.Concat,
            TokenType.Plus => BinaryOp.Add,
            TokenType.Minus => BinaryOp.Sub,
            TokenType.Star => BinaryOp.Mul,
            TokenType.Slash => BinaryOp.Div,
            TokenType.Caret => BinaryOp.Pow,
            _ => throw new InvalidOperationException($"Not a binary operator: {type}")
        };
    }
}

/// <summary>
/// Evaluation result of all formulas in a <see cref="DocumentTable"/>.
/// </summary>
public sealed class TableEvaluationResult
{
    private readonly Dictionary<CellAddress, CellValue> _values;
    private readonly Dictionary<CellAddress, FormulaError> _errors;
    private readonly HashSet<CellAddress> _cyclicCells;
    private readonly IReadOnlyDictionary<CellAddress, IReadOnlySet<CellAddress>> _dependencies;

    internal TableEvaluationResult(
        DocumentTable table,
        Dictionary<CellAddress, CellValue> values,
        Dictionary<CellAddress, FormulaError> errors,
        HashSet<CellAddress> cyclicCells,
        Dictionary<CellAddress, HashSet<CellAddress>>? dependencies = null)
    {
        Table = table;
        _values = values;
        _errors = errors;
        _cyclicCells = cyclicCells;
        _dependencies = dependencies != null
            ? dependencies.ToDictionary(k => k.Key, v => (IReadOnlySet<CellAddress>)v.Value)
            : new Dictionary<CellAddress, IReadOnlySet<CellAddress>>();
    }

    /// <summary>The evaluated table.</summary>
    public DocumentTable Table { get; }

    /// <summary>Map of all cell values (both static and formula-computed).</summary>
    public IReadOnlyDictionary<CellAddress, CellValue> Values => _values;

    /// <summary>Map of cells that resulted in calculation errors.</summary>
    public IReadOnlyDictionary<CellAddress, FormulaError> Errors => _errors;

    /// <summary>Set of cells that participated in or transitively depended on a circular reference.</summary>
    public IReadOnlySet<CellAddress> CyclicCells => _cyclicCells;

    /// <summary>Map of formula cells to their set of dependencies.</summary>
    public IReadOnlyDictionary<CellAddress, IReadOnlySet<CellAddress>> Dependencies => _dependencies;

    /// <summary>Gets the evaluated cell value at the specified coordinate.</summary>
    public CellValue GetValue(int row, int column) => GetValue(new CellAddress(row, column));

    /// <summary>Gets the evaluated cell value at the specified address.</summary>
    public CellValue GetValue(CellAddress address) =>
        _values.TryGetValue(address, out var val) ? val : CellValue.Empty;

    public CellValue this[int row, int column] => GetValue(row, column);
    public CellValue this[CellAddress address] => GetValue(address);

    /// <summary>Returns true if the cell produced a calculation error.</summary>
    public bool TryGetError(int row, int column, out FormulaError error) =>
        TryGetError(new CellAddress(row, column), out error);

    /// <summary>Returns true if the cell produced a calculation error.</summary>
    public bool TryGetError(CellAddress address, out FormulaError error) =>
        _errors.TryGetValue(address, out error);

    public bool HasErrors => _errors.Count > 0;
    public bool HasCycles => _cyclicCells.Count > 0;

    /// <summary>
    /// Gets the formatted visual presentation for the cell at the specified coordinate,
    /// taking into account the cell's <see cref="TableCell.NumberFormat"/> and locale.
    /// </summary>
    public FormattedCellResult GetFormattedValue(int row, int column, CultureInfo? culture = null) =>
        GetFormattedValue(new CellAddress(row, column), culture);

    /// <summary>
    /// Gets the formatted visual presentation for the cell at the specified address,
    /// taking into account the cell's <see cref="TableCell.NumberFormat"/> and locale.
    /// </summary>
    public FormattedCellResult GetFormattedValue(CellAddress address, CultureInfo? culture = null)
    {
        var val = GetValue(address);
        string? format = null;
        var grid = TableGrid.FromTable(Table);
        if (address.Row >= 0 && address.Row < grid.RowCount && address.Column >= 0 && address.Column < grid.ColumnCount)
        {
            var slot = grid.GetSlot(address.Row, address.Column);
            format = slot.RootCell?.NumberFormat;
        }

        return CellNumberFormatter.Format(val, format, culture);
    }
}

/// <summary>
/// Recalculation and dependency DAG engine for spreadsheet and table formulas.
/// </summary>
public static class TableFormulaEngine
{
    /// <summary>
    /// Evaluates all formulas in a <see cref="DocumentTable"/> using document paragraphs or cell properties.
    /// </summary>
    public static TableEvaluationResult Evaluate(
        DocumentTable table,
        RichTextDocument? document = null,
        IReadOnlyDictionary<CellAddress, CellValue>? initialValues = null)
    {
        var grid = TableGrid.FromTable(table);
        int rowCount = grid.RowCount;
        int colCount = grid.ColumnCount;

        var staticValues = new Dictionary<CellAddress, CellValue>();
        var formulaTexts = new Dictionary<CellAddress, string>();
        var formulaAsts = new Dictionary<CellAddress, FormulaNode>();
        var errors = new Dictionary<CellAddress, FormulaError>();
        var evaluatedValues = new Dictionary<CellAddress, CellValue>();

        // 1. Gather all cells and detect formulas vs static values
        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < colCount; c++)
            {
                var slot = grid.GetSlot(r, c);
                if (!slot.IsRoot || slot.RootCell is null)
                    continue;

                var addr = new CellAddress(r, c);
                var cell = slot.RootCell;

                // Check explicit formula
                if (!string.IsNullOrWhiteSpace(cell.Formula))
                {
                    formulaTexts[addr] = cell.Formula;
                    continue;
                }

                // Explicit initial value override for cells without formula
                if (initialValues != null && initialValues.TryGetValue(addr, out var initVal))
                {
                    staticValues[addr] = initVal;
                    continue;
                }

                // Check cell explicit value
                if (cell.Value.HasValue)
                {
                    staticValues[addr] = cell.Value.Value;
                    continue;
                }

                // Check document text
                string cellText = string.Empty;
                if (document != null && cell.ParagraphCount > 0 && cell.ParagraphIndex < document.Paragraphs.Count)
                {
                    if (cell.ParagraphCount == 1)
                    {
                        cellText = document.Paragraphs[cell.ParagraphIndex].Text;
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        for (int pi = 0; pi < cell.ParagraphCount; pi++)
                        {
                            int pIdx = cell.ParagraphIndex + pi;
                            if (pIdx < document.Paragraphs.Count)
                            {
                                if (sb.Length > 0) sb.Append('\n');
                                sb.Append(document.Paragraphs[pIdx].Text);
                            }
                        }
                        cellText = sb.ToString();
                    }
                }

                string trimmed = cellText.Trim();
                if (trimmed.StartsWith('='))
                {
                    formulaTexts[addr] = trimmed;
                }
                else
                {
                    staticValues[addr] = CellValue.Parse(cellText);
                }
            }
        }

        // 2. Parse formulas into ASTs
        var dependencies = new Dictionary<CellAddress, HashSet<CellAddress>>();
        foreach (var (addr, text) in formulaTexts)
        {
            if (FormulaParser.TryParse(text, out var ast, out var err))
            {
                formulaAsts[addr] = ast!;
                var deps = new HashSet<CellAddress>();
                ExtractDependencies(ast!, addr, rowCount, colCount, grid, deps);
                dependencies[addr] = deps;
            }
            else
            {
                errors[addr] = FormulaError.Syntax;
                evaluatedValues[addr] = CellValue.FromError(FormulaError.Syntax);
            }
        }

        // 3. Cycle Detection (3-Color DFS) and Topological Sort
        var color = new Dictionary<CellAddress, int>(); // 0: White, 1: Gray, 2: Black
        var inCycle = new HashSet<CellAddress>();
        var topoOrder = new List<CellAddress>();

        foreach (var addr in formulaAsts.Keys)
            color[addr] = 0;

        void Visit(CellAddress u, List<CellAddress> path)
        {
            color[u] = 1;
            path.Add(u);

            if (dependencies.TryGetValue(u, out var deps))
            {
                foreach (var v in deps)
                {
                    if (formulaAsts.ContainsKey(v))
                    {
                        int vColor = color.TryGetValue(v, out int vc) ? vc : 0;
                        if (vColor == 1)
                        {
                            // Cycle detected!
                            int idx = path.IndexOf(v);
                            if (idx >= 0)
                            {
                                for (int i = idx; i < path.Count; i++)
                                    inCycle.Add(path[i]);
                            }
                        }
                        else if (vColor == 0)
                        {
                            Visit(v, path);
                        }

                        if (inCycle.Contains(v))
                            inCycle.Add(u);
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            color[u] = 2;
            if (!inCycle.Contains(u))
                topoOrder.Add(u);
        }

        foreach (var addr in formulaAsts.Keys)
        {
            if (color[addr] == 0)
                Visit(addr, []);
        }

        // Mark all cyclic cells
        foreach (var cyclic in inCycle)
        {
            errors[cyclic] = FormulaError.Cycle;
            evaluatedValues[cyclic] = CellValue.FromError(FormulaError.Cycle);
        }

        // 4. Evaluate acyclic formulas in topological order
        var evalContext = new FormulaContext(rowCount, colCount, grid, staticValues, evaluatedValues);

        foreach (var addr in topoOrder)
        {
            if (formulaAsts.TryGetValue(addr, out var ast))
            {
                evalContext.CurrentCell = addr;
                var res = EvaluateNode(ast, evalContext);
                evaluatedValues[addr] = res;
                if (res.IsError)
                    errors[addr] = res.Error;
            }
        }

        // 5. Combine static values and evaluated values into final result dictionary
        var finalValues = new Dictionary<CellAddress, CellValue>(staticValues);
        foreach (var (k, v) in evaluatedValues)
            finalValues[k] = v;

        return new TableEvaluationResult(table, finalValues, errors, inCycle, dependencies);
    }

    /// <summary>
    /// Parses a formula expression and extracts all cell dependencies.
    /// </summary>
    public static IReadOnlySet<CellAddress> ExtractDependencies(
        string formula,
        CellAddress currentCell,
        int totalRows,
        int totalCols,
        TableGrid? grid = null)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return new HashSet<CellAddress>();

        if (!FormulaParser.TryParse(formula, out var ast, out _))
            return new HashSet<CellAddress>();

        var deps = new HashSet<CellAddress>();
        grid ??= TableGrid.FromTable(new DocumentTable(0, 0, []));
        ExtractDependencies(ast!, currentCell, totalRows, totalCols, grid, deps);
        return deps;
    }

    public static void ExtractDependencies(
        FormulaNode node,
        CellAddress currentCell,
        int totalRows,
        int totalCols,
        TableGrid grid,
        HashSet<CellAddress> deps)
    {
        switch (node)
        {
            case CellRefNode cellRef:
                AddCellDependency(cellRef.Address, totalRows, totalCols, grid, deps);
                break;

            case CellRangeNode rangeNode:
                foreach (var addr in rangeNode.Range.EnumerateCells(totalRows, totalCols))
                    AddCellDependency(addr, totalRows, totalCols, grid, deps);
                break;

            case DirectionalRangeNode dirNode:
                foreach (var addr in EnumerateDirectionalCells(dirNode.Kind, currentCell, totalRows, totalCols))
                    AddCellDependency(addr, totalRows, totalCols, grid, deps);
                break;

            case UnaryOpNode unary:
                ExtractDependencies(unary.Operand, currentCell, totalRows, totalCols, grid, deps);
                break;

            case BinaryOpNode binary:
                ExtractDependencies(binary.Left, currentCell, totalRows, totalCols, grid, deps);
                ExtractDependencies(binary.Right, currentCell, totalRows, totalCols, grid, deps);
                break;

            case FunctionCallNode func:
                foreach (var arg in func.Arguments)
                    ExtractDependencies(arg, currentCell, totalRows, totalCols, grid, deps);
                break;
        }
    }

    private static void AddCellDependency(
        CellAddress addr,
        int totalRows,
        int totalCols,
        TableGrid grid,
        HashSet<CellAddress> deps)
    {
        if (addr.Row >= 0 && addr.Row < totalRows && addr.Column >= 0 && addr.Column < totalCols)
        {
            var slot = grid.GetSlot(addr.Row, addr.Column);
            if (slot.IsOccupied)
            {
                deps.Add(new CellAddress(slot.RootRow, slot.RootColumn));
            }
            else
            {
                deps.Add(addr);
            }
        }
    }

    private static IEnumerable<CellAddress> EnumerateDirectionalCells(
        DirectionalRangeKind kind,
        CellAddress currentCell,
        int totalRows,
        int totalCols)
    {
        switch (kind)
        {
            case DirectionalRangeKind.Above:
                for (int r = 0; r < currentCell.Row; r++)
                    yield return new CellAddress(r, currentCell.Column);
                break;
            case DirectionalRangeKind.Below:
                for (int r = currentCell.Row + 1; r < totalRows; r++)
                    yield return new CellAddress(r, currentCell.Column);
                break;
            case DirectionalRangeKind.Left:
                for (int c = 0; c < currentCell.Column; c++)
                    yield return new CellAddress(currentCell.Row, c);
                break;
            case DirectionalRangeKind.Right:
                for (int c = currentCell.Column + 1; c < totalCols; c++)
                    yield return new CellAddress(currentCell.Row, c);
                break;
        }
    }

    private sealed class FormulaContext(
        int totalRows,
        int totalCols,
        TableGrid grid,
        Dictionary<CellAddress, CellValue> staticValues,
        Dictionary<CellAddress, CellValue> evaluatedValues)
    {
        public int TotalRows => totalRows;
        public int TotalCols => totalCols;
        public TableGrid Grid => grid;
        public CellAddress CurrentCell { get; set; }

        public CellValue GetCellValue(CellAddress addr)
        {
            if (addr.Row < 0 || addr.Row >= totalRows || addr.Column < 0 || addr.Column >= totalCols)
                return CellValue.FromError(FormulaError.Ref);

            var slot = grid.GetSlot(addr.Row, addr.Column);
            if (!slot.IsOccupied)
                return CellValue.Empty;

            if (slot.IsContinuation)
                return CellValue.Empty;

            var rootAddr = new CellAddress(slot.RootRow, slot.RootColumn);
            if (evaluatedValues.TryGetValue(rootAddr, out var evalVal))
                return evalVal;
            if (staticValues.TryGetValue(rootAddr, out var statVal))
                return statVal;

            return CellValue.Empty;
        }
    }

    private static CellValue EvaluateNode(FormulaNode node, FormulaContext ctx)
    {
        switch (node)
        {
            case LiteralNode lit:
                return lit.Value;

            case CellRefNode cellRef:
                return ctx.GetCellValue(cellRef.Address);

            case CellRangeNode range:
                // Scalar evaluation takes the top-left cell
                var first = range.Range.EnumerateCells(ctx.TotalRows, ctx.TotalCols).FirstOrDefault();
                return ctx.GetCellValue(first);

            case DirectionalRangeNode dir:
                var dirFirst = EnumerateDirectionalCells(dir.Kind, ctx.CurrentCell, ctx.TotalRows, ctx.TotalCols).FirstOrDefault();
                return ctx.GetCellValue(dirFirst);

            case UnaryOpNode unary:
                return EvaluateUnary(unary, ctx);

            case BinaryOpNode binary:
                return EvaluateBinary(binary, ctx);

            case FunctionCallNode func:
                return EvaluateFunction(func, ctx);

            default:
                return CellValue.FromError(FormulaError.Value);
        }
    }

    private static CellValue EvaluateUnary(UnaryOpNode unary, FormulaContext ctx)
    {
        var val = EvaluateNode(unary.Operand, ctx);
        if (val.IsError) return val;

        switch (unary.Op)
        {
            case UnaryOp.Plus:
                if (!val.AsDouble(out double p)) return CellValue.FromError(FormulaError.Value);
                return CellValue.FromNumber(p);

            case UnaryOp.Negate:
                if (!val.AsDouble(out double n)) return CellValue.FromError(FormulaError.Value);
                return CellValue.FromNumber(-n);

            case UnaryOp.Percent:
                if (!val.AsDouble(out double pct)) return CellValue.FromError(FormulaError.Value);
                return CellValue.FromNumber(pct / 100.0);

            default:
                return CellValue.FromError(FormulaError.Value);
        }
    }

    private static CellValue EvaluateBinary(BinaryOpNode binary, FormulaContext ctx)
    {
        var left = EvaluateNode(binary.Left, ctx);
        if (left.IsError) return left;
        var right = EvaluateNode(binary.Right, ctx);
        if (right.IsError) return right;

        switch (binary.Op)
        {
            case BinaryOp.Add:
            case BinaryOp.Sub:
            case BinaryOp.Mul:
            case BinaryOp.Div:
            case BinaryOp.Pow:
                if (!left.AsDouble(out double l) || !right.AsDouble(out double r))
                    return CellValue.FromError(FormulaError.Value);

                switch (binary.Op)
                {
                    case BinaryOp.Add: return CellValue.FromNumber(l + r);
                    case BinaryOp.Sub: return CellValue.FromNumber(l - r);
                    case BinaryOp.Mul: return CellValue.FromNumber(l * r);
                    case BinaryOp.Div:
                        if (r == 0) return CellValue.FromError(FormulaError.Div0);
                        return CellValue.FromNumber(l / r);
                    case BinaryOp.Pow:
                        double pow = Math.Pow(l, r);
                        if (double.IsNaN(pow) || double.IsInfinity(pow))
                            return CellValue.FromError(FormulaError.Num);
                        return CellValue.FromNumber(pow);
                }
                break;

            case BinaryOp.Concat:
                return CellValue.FromText(left.Format() + right.Format());

            case BinaryOp.Equal:
            case BinaryOp.NotEqual:
            case BinaryOp.Less:
            case BinaryOp.LessEqual:
            case BinaryOp.Greater:
            case BinaryOp.GreaterEqual:
                int cmp = CompareValues(left, right);
                bool res = binary.Op switch
                {
                    BinaryOp.Equal => cmp == 0,
                    BinaryOp.NotEqual => cmp != 0,
                    BinaryOp.Less => cmp < 0,
                    BinaryOp.LessEqual => cmp <= 0,
                    BinaryOp.Greater => cmp > 0,
                    BinaryOp.GreaterEqual => cmp >= 0,
                    _ => false
                };
                return CellValue.FromBoolean(res);
        }

        return CellValue.FromError(FormulaError.Value);
    }

    private static int CompareValues(CellValue a, CellValue b)
    {
        if (a.IsNumber && b.IsNumber)
            return a.Number.CompareTo(b.Number);

        if (a.IsBoolean && b.IsBoolean)
            return a.Boolean.CompareTo(b.Boolean);

        if (a.IsNumber && b.AsDouble(out double bNum))
            return a.Number.CompareTo(bNum);

        if (b.IsNumber && a.AsDouble(out double aNum))
            return aNum.CompareTo(b.Number);

        return string.Compare(a.Format(), b.Format(), StringComparison.OrdinalIgnoreCase);
    }

    private static CellValue EvaluateFunction(FunctionCallNode func, FormulaContext ctx)
    {
        string name = func.Name.ToUpperInvariant();

        // 1. Short-circuiting functions
        if (name == "IF")
        {
            if (func.Arguments.Count < 2 || func.Arguments.Count > 3)
                return CellValue.FromError(FormulaError.Value);

            var cond = EvaluateNode(func.Arguments[0], ctx);
            if (cond.IsError) return cond;
            if (!cond.AsBoolean(out bool condBool))
                return CellValue.FromError(FormulaError.Value);

            if (condBool)
                return EvaluateNode(func.Arguments[1], ctx);
            if (func.Arguments.Count > 2)
                return EvaluateNode(func.Arguments[2], ctx);
            return CellValue.False;
        }

        if (name == "IFERROR")
        {
            if (func.Arguments.Count != 2)
                return CellValue.FromError(FormulaError.Value);

            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError)
                return EvaluateNode(func.Arguments[1], ctx);
            return val;
        }

        // 2. Statistical & Aggregates
        if (name is "SUM" or "AVERAGE" or "AVG" or "COUNT" or "COUNTA" or "MIN" or "MAX" or "PRODUCT" or "MEDIAN")
        {
            var items = FlattenArguments(func.Arguments, ctx);
            switch (name)
            {
                case "SUM":
                {
                    double sum = 0;
                    foreach (var (val, fromCell) in items)
                    {
                        if (val.IsError) return val;
                        if (val.IsNumber) sum += val.Number;
                        else if (!fromCell && val.AsDouble(out double d)) sum += d;
                    }
                    return CellValue.FromNumber(sum);
                }
                case "AVERAGE" or "AVG":
                {
                    double sum = 0;
                    int count = 0;
                    foreach (var (val, fromCell) in items)
                    {
                        if (val.IsError) return val;
                        if (val.IsNumber) { sum += val.Number; count++; }
                        else if (!fromCell && val.AsDouble(out double d)) { sum += d; count++; }
                    }
                    if (count == 0) return CellValue.FromError(FormulaError.Div0);
                    return CellValue.FromNumber(sum / count);
                }
                case "COUNT":
                {
                    int count = 0;
                    foreach (var (val, fromCell) in items)
                    {
                        if (val.IsNumber) count++;
                        else if (!fromCell && val.AsDouble(out _)) count++;
                    }
                    return CellValue.FromNumber(count);
                }
                case "COUNTA":
                {
                    int count = 0;
                    foreach (var (val, _) in items)
                    {
                        if (!val.IsEmpty) count++;
                    }
                    return CellValue.FromNumber(count);
                }
                case "MIN":
                {
                    double min = double.PositiveInfinity;
                    int count = 0;
                    foreach (var (val, fromCell) in items)
                    {
                        if (val.IsError) return val;
                        if (val.IsNumber) { if (val.Number < min) min = val.Number; count++; }
                        else if (!fromCell && val.AsDouble(out double d)) { if (d < min) min = d; count++; }
                    }
                    return CellValue.FromNumber(count > 0 ? min : 0);
                }
                case "MAX":
                {
                    double max = double.NegativeInfinity;
                    int count = 0;
                    foreach (var (val, fromCell) in items)
                    {
                        if (val.IsError) return val;
                        if (val.IsNumber) { if (val.Number > max) max = val.Number; count++; }
                        else if (!fromCell && val.AsDouble(out double d)) { if (d > max) max = d; count++; }
                    }
                    return CellValue.FromNumber(count > 0 ? max : 0);
                }
                case "PRODUCT":
                {
                    double prod = 1;
                    int count = 0;
                    foreach (var (val, fromCell) in items)
                    {
                        if (val.IsError) return val;
                        if (val.IsNumber) { prod *= val.Number; count++; }
                        else if (!fromCell && val.AsDouble(out double d)) { prod *= d; count++; }
                    }
                    return CellValue.FromNumber(count > 0 ? prod : 0);
                }
                case "MEDIAN":
                {
                    var nums = new List<double>();
                    foreach (var (val, fromCell) in items)
                    {
                        if (val.IsError) return val;
                        if (val.IsNumber) nums.Add(val.Number);
                        else if (!fromCell && val.AsDouble(out double d)) nums.Add(d);
                    }
                    if (nums.Count == 0) return CellValue.FromError(FormulaError.Num);
                    nums.Sort();
                    int count = nums.Count;
                    double med = (count % 2 == 1)
                        ? nums[count / 2]
                        : (nums[count / 2 - 1] + nums[count / 2]) / 2.0;
                    return CellValue.FromNumber(med);
                }
            }
        }

        // 3. Math Functions
        if (name == "ABS")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            if (!val.AsDouble(out double d)) return CellValue.FromError(FormulaError.Value);
            return CellValue.FromNumber(Math.Abs(d));
        }

        if (name == "ROUND")
        {
            if (func.Arguments.Count < 1 || func.Arguments.Count > 2) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            if (!val.AsDouble(out double d)) return CellValue.FromError(FormulaError.Value);
            int digits = 0;
            if (func.Arguments.Count > 1)
            {
                var dVal = EvaluateNode(func.Arguments[1], ctx);
                if (dVal.IsError) return dVal;
                if (!dVal.AsDouble(out double dDigits)) return CellValue.FromError(FormulaError.Value);
                digits = Math.Clamp((int)dDigits, -15, 15);
            }
            if (digits >= 0)
                return CellValue.FromNumber(Math.Round(d, digits, MidpointRounding.AwayFromZero));
            double factor = Math.Pow(10, -digits);
            return CellValue.FromNumber(Math.Round(d / factor, 0, MidpointRounding.AwayFromZero) * factor);
        }

        if (name is "INT" or "TRUNC")
        {
            if (func.Arguments.Count < 1 || func.Arguments.Count > 2) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            if (!val.AsDouble(out double d)) return CellValue.FromError(FormulaError.Value);

            if (name == "INT")
                return CellValue.FromNumber(Math.Floor(d));

            int digits = 0;
            if (func.Arguments.Count > 1)
            {
                var dVal = EvaluateNode(func.Arguments[1], ctx);
                if (dVal.IsError) return dVal;
                if (!dVal.AsDouble(out double dDigits)) return CellValue.FromError(FormulaError.Value);
                digits = (int)dDigits;
            }
            double factor = Math.Pow(10, digits);
            return CellValue.FromNumber(Math.Truncate(d * factor) / factor);
        }

        if (name == "MOD")
        {
            if (func.Arguments.Count != 2) return CellValue.FromError(FormulaError.Value);
            var nVal = EvaluateNode(func.Arguments[0], ctx);
            if (nVal.IsError) return nVal;
            var dVal = EvaluateNode(func.Arguments[1], ctx);
            if (dVal.IsError) return dVal;
            if (!nVal.AsDouble(out double n) || !dVal.AsDouble(out double d))
                return CellValue.FromError(FormulaError.Value);
            if (d == 0) return CellValue.FromError(FormulaError.Div0);
            return CellValue.FromNumber(n - d * Math.Floor(n / d));
        }

        if (name == "SQRT")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            if (!val.AsDouble(out double d)) return CellValue.FromError(FormulaError.Value);
            if (d < 0) return CellValue.FromError(FormulaError.Num);
            return CellValue.FromNumber(Math.Sqrt(d));
        }

        if (name == "POWER")
        {
            if (func.Arguments.Count != 2) return CellValue.FromError(FormulaError.Value);
            var bVal = EvaluateNode(func.Arguments[0], ctx);
            if (bVal.IsError) return bVal;
            var eVal = EvaluateNode(func.Arguments[1], ctx);
            if (eVal.IsError) return eVal;
            if (!bVal.AsDouble(out double b) || !eVal.AsDouble(out double e))
                return CellValue.FromError(FormulaError.Value);
            double pow = Math.Pow(b, e);
            if (double.IsNaN(pow) || double.IsInfinity(pow)) return CellValue.FromError(FormulaError.Num);
            return CellValue.FromNumber(pow);
        }

        if (name == "SIGN")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            if (!val.AsDouble(out double d)) return CellValue.FromError(FormulaError.Value);
            return CellValue.FromNumber(Math.Sign(d));
        }

        // 4. Logical Functions
        if (name == "AND")
        {
            var items = FlattenArguments(func.Arguments, ctx);
            bool hasItem = false;
            foreach (var (val, _) in items)
            {
                if (val.IsError) return val;
                if (!val.AsBoolean(out bool b)) return CellValue.FromError(FormulaError.Value);
                if (!b) return CellValue.False;
                hasItem = true;
            }
            return CellValue.FromBoolean(hasItem);
        }

        if (name == "OR")
        {
            var items = FlattenArguments(func.Arguments, ctx);
            foreach (var (val, _) in items)
            {
                if (val.IsError) return val;
                if (!val.AsBoolean(out bool b)) return CellValue.FromError(FormulaError.Value);
                if (b) return CellValue.True;
            }
            return CellValue.False;
        }

        if (name == "NOT")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            if (!val.AsBoolean(out bool b)) return CellValue.FromError(FormulaError.Value);
            return CellValue.FromBoolean(!b);
        }

        if (name == "ISNUMBER")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            return CellValue.FromBoolean(val.IsNumber);
        }

        if (name == "ISTEXT")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            return CellValue.FromBoolean(val.IsText);
        }

        if (name == "ISBLANK")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            return CellValue.FromBoolean(val.IsEmpty);
        }

        if (name == "ISERROR")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            return CellValue.FromBoolean(val.IsError);
        }

        // 5. Text Functions
        if (name is "CONCAT" or "CONCATENATE")
        {
            var items = FlattenArguments(func.Arguments, ctx);
            var sb = new StringBuilder();
            foreach (var (val, _) in items)
            {
                if (val.IsError) return val;
                sb.Append(val.Format());
            }
            return CellValue.FromText(sb.ToString());
        }

        if (name == "LEN")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            return CellValue.FromNumber(val.Format().Length);
        }

        if (name == "UPPER")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            return CellValue.FromText(val.Format().ToUpperInvariant());
        }

        if (name == "LOWER")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            return CellValue.FromText(val.Format().ToLowerInvariant());
        }

        if (name == "TRIM")
        {
            if (func.Arguments.Count != 1) return CellValue.FromError(FormulaError.Value);
            var val = EvaluateNode(func.Arguments[0], ctx);
            if (val.IsError) return val;
            return CellValue.FromText(val.Format().Trim());
        }

        return CellValue.FromError(FormulaError.Name);
    }

    private static List<(CellValue Value, bool FromCell)> FlattenArguments(
        IReadOnlyList<FormulaNode> args,
        FormulaContext ctx)
    {
        var list = new List<(CellValue Value, bool FromCell)>();

        foreach (var arg in args)
        {
            if (arg is CellRangeNode rangeNode)
            {
                foreach (var addr in rangeNode.Range.EnumerateCells(ctx.TotalRows, ctx.TotalCols))
                    list.Add((ctx.GetCellValue(addr), true));
            }
            else if (arg is DirectionalRangeNode dirNode)
            {
                foreach (var addr in EnumerateDirectionalCells(dirNode.Kind, ctx.CurrentCell, ctx.TotalRows, ctx.TotalCols))
                    list.Add((ctx.GetCellValue(addr), true));
            }
            else if (arg is CellRefNode cellRef)
            {
                list.Add((ctx.GetCellValue(cellRef.Address), true));
            }
            else
            {
                list.Add((EvaluateNode(arg, ctx), false));
            }
        }

        return list;
    }
}
