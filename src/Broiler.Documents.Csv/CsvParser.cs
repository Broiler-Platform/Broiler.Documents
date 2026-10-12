using System.Collections.Generic;
using System.Text;

namespace Broiler.Documents.Csv;

/// <summary>
/// Splits CSV text into records and fields, one record at a time.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is RFC 4180's: fields separated by the delimiter, records by a
/// line break, and a field that holds a delimiter, a quote or a line break
/// enclosed in double quotes, with a quote inside written twice. Spaces belong
/// to the field. The last record may or may not end with a line break.
/// </para>
/// <para>
/// It reads more than the RFC writes, because files that break the grammar are
/// common and losing their text would be worse than reading them generously. A
/// line break is CRLF, LF or a lone CR. A quote in the middle of an unquoted
/// field is kept as a character, and text after a closing quote is appended to
/// the field, which is what spreadsheets do with both; each is noted in
/// <see cref="SawStrayQuote"/>. A quoted field still open at the end of the
/// text runs to the end, noted in <see cref="SawUnterminatedQuote"/>.
/// </para>
/// </remarks>
internal sealed class CsvParser(string text, char delimiter)
{
    private readonly StringBuilder _field = new();
    private int _position;

    /// <summary>True once a quote has been read where the grammar does not allow one.</summary>
    public bool SawStrayQuote { get; private set; }

    /// <summary>True once a quoted field has run to the end of the text.</summary>
    public bool SawUnterminatedQuote { get; private set; }

    /// <summary>The next record's fields, or null when the text is exhausted.</summary>
    public List<string>? ReadRecord()
    {
        if (_position >= text.Length)
            return null;

        var fields = new List<string>();
        while (true)
        {
            _field.Clear();
            bool endOfRecord = ReadField();
            fields.Add(_field.ToString());
            if (endOfRecord)
                return fields;
        }
    }

    /// <summary>
    /// Reads one field into <see cref="_field"/>. Returns true when the field
    /// ended its record - at a line break or the end of the text - and false
    /// when a delimiter followed it.
    /// </summary>
    private bool ReadField()
    {
        if (_position < text.Length && text[_position] == '"')
        {
            _position++;
            if (!ReadQuoted())
            {
                SawUnterminatedQuote = true;
                return true;
            }

            if (_position < text.Length && text[_position] != delimiter && !IsLineBreak(text[_position]))
                SawStrayQuote = true;
        }

        return ReadUnquoted();
    }

    /// <summary>Reads a quoted field's content up to its closing quote; false when the text ended first.</summary>
    private bool ReadQuoted()
    {
        while (_position < text.Length)
        {
            char c = text[_position++];
            if (c != '"')
            {
                _field.Append(c);
                continue;
            }

            if (_position < text.Length && text[_position] == '"')
            {
                _field.Append('"');
                _position++;
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>Reads unquoted text up to the delimiter, a line break, or the end.</summary>
    private bool ReadUnquoted()
    {
        int start = _position;
        while (_position < text.Length)
        {
            char c = text[_position];
            if (c == delimiter)
            {
                _field.Append(text, start, _position - start);
                _position++;
                return false;
            }

            if (IsLineBreak(c))
            {
                _field.Append(text, start, _position - start);
                ConsumeLineBreak();
                return true;
            }

            if (c == '"')
                SawStrayQuote = true;

            _position++;
        }

        _field.Append(text, start, _position - start);
        return true;
    }

    private void ConsumeLineBreak()
    {
        if (text[_position] == '\r' && _position + 1 < text.Length && text[_position + 1] == '\n')
            _position += 2;
        else
            _position++;
    }

    private static bool IsLineBreak(char c) => c is '\r' or '\n';
}
