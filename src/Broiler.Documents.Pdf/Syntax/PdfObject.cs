using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Documents.Pdf.Syntax;

/// <summary>
/// The base of the eight PDF object types (ISO 32000-1 clause 7.3) plus the
/// indirect reference that stands in for a not-yet-resolved object.
/// </summary>
/// <remarks>
/// Deliberately <c>internal</c>: the public surface rule in the PDF roadmap keeps
/// objects, xref entries, page dictionaries, and parser internals out of the
/// package's API until a second real consumer justifies them.
/// </remarks>
internal abstract class PdfObject
{
    /// <summary>The singleton null object; PDF treats a missing key and null alike.</summary>
    public static PdfNull Null => PdfNull.Instance;
}

internal sealed class PdfNull : PdfObject
{
    public static PdfNull Instance { get; } = new();

    private PdfNull()
    {
    }

    public override string ToString() => "null";
}

internal sealed class PdfBoolean : PdfObject
{
    public static PdfBoolean True { get; } = new(true);

    public static PdfBoolean False { get; } = new(false);

    private PdfBoolean(bool value) => Value = value;

    public bool Value { get; }

    public static PdfBoolean Of(bool value) => value ? True : False;

    public override string ToString() => Value ? "true" : "false";
}

/// <summary>
/// A PDF numeric object. PDF has integers and reals; both are kept in one type
/// with an <see cref="IsInteger"/> flag so a writer can round-trip the
/// distinction and a reader can apply the integer-only rules (object numbers,
/// generations, array lengths) without a second type test.
/// </summary>
internal sealed class PdfNumber : PdfObject
{
    public PdfNumber(long value)
    {
        Value = value;
        IsInteger = true;
    }

    public PdfNumber(double value)
    {
        Value = value;
        IsInteger = false;
    }

    public double Value { get; }

    public bool IsInteger { get; }

    /// <summary>The value as an <see cref="int"/>, saturating rather than wrapping.</summary>
    public int ToInt32() => Value switch
    {
        > int.MaxValue => int.MaxValue,
        < int.MinValue => int.MinValue,
        _ => (int)Value,
    };

    public long ToInt64() => Value switch
    {
        > long.MaxValue => long.MaxValue,
        < long.MinValue => long.MinValue,
        _ => (long)Value,
    };

    public override string ToString() =>
        Value.ToString(IsInteger ? "F0" : "G", CultureInfo.InvariantCulture);
}

/// <summary>
/// A PDF name object. The stored <see cref="Value"/> is the decoded form, with
/// <c>#xx</c> escapes already resolved, and never includes the leading solidus.
/// </summary>
internal sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    public PdfName(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Value { get; }

    public bool Equals(PdfName? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PdfName);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => "/" + Value;
}

/// <summary>
/// A PDF string object: a byte string, not text. Text semantics (PDFDocEncoding
/// or UTF-16BE) are applied by the caller that knows the context, because the
/// same bytes mean different things in a content stream and in an Info value.
/// </summary>
internal sealed class PdfString : PdfObject
{
    public PdfString(byte[] bytes, bool hexadecimal = false)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        IsHexadecimal = hexadecimal;
    }

    public byte[] Bytes { get; }

    /// <summary>True when the source form was <c>&lt;hex&gt;</c> rather than <c>(literal)</c>.</summary>
    public bool IsHexadecimal { get; }

    public override string ToString() => $"<string:{Bytes.Length} bytes>";
}

internal sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly List<PdfObject> _items;

    public PdfArray() => _items = [];

    public PdfArray(IEnumerable<PdfObject> items) => _items = [.. items];

    public PdfObject this[int index] => _items[index];

    public int Count => _items.Count;

    public void Add(PdfObject item) => _items.Add(item ?? PdfObject.Null);

    public IEnumerator<PdfObject> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"[{_items.Count} items]";
}

internal sealed class PdfDictionary : PdfObject, IEnumerable<KeyValuePair<string, PdfObject>>
{
    private readonly Dictionary<string, PdfObject> _entries;

    public PdfDictionary() => _entries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);

    public PdfDictionary(IEnumerable<KeyValuePair<string, PdfObject>> entries)
        : this()
    {
        foreach (KeyValuePair<string, PdfObject> entry in entries)
            _entries[entry.Key] = entry.Value;
    }

    public int Count => _entries.Count;

    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>The raw entry, which may still be a <see cref="PdfReference"/>.</summary>
    public PdfObject? this[string key]
    {
        get => _entries.TryGetValue(key, out PdfObject? value) ? value : null;
        set => _entries[key] = value ?? PdfObject.Null;
    }

    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public bool TryGetValue(string key, out PdfObject value) => _entries.TryGetValue(key, out value!);

    public void Remove(string key) => _entries.Remove(key);

    public IEnumerator<KeyValuePair<string, PdfObject>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"<<{_entries.Count} entries>>";
}

/// <summary>
/// A stream object: its dictionary plus the <em>raw</em> (still encoded) bytes.
/// Decoding runs through <see cref="Filters.PdfFilterPipeline"/> so every decode
/// is charged against the document's byte and work budgets.
/// </summary>
internal sealed class PdfStream : PdfObject
{
    public PdfStream(PdfDictionary dictionary, byte[] rawData)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        RawData = rawData ?? throw new ArgumentNullException(nameof(rawData));
    }

    public PdfDictionary Dictionary { get; }

    /// <summary>The bytes between <c>stream</c> and <c>endstream</c>, before filters.</summary>
    public byte[] RawData { get; }

    public override string ToString() => $"<<stream:{RawData.Length} raw bytes>>";
}

/// <summary>An indirect reference (<c>n g R</c>), resolved through the object store.</summary>
internal sealed class PdfReference : PdfObject, IEquatable<PdfReference>
{
    public PdfReference(int objectNumber, int generation)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    public int ObjectNumber { get; }

    public int Generation { get; }

    public bool Equals(PdfReference? other) =>
        other is not null && other.ObjectNumber == ObjectNumber && other.Generation == Generation;

    public override bool Equals(object? obj) => Equals(obj as PdfReference);

    public override int GetHashCode() => HashCode.Combine(ObjectNumber, Generation);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{ObjectNumber} {Generation} R");
}
