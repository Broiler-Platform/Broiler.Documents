using System;
using System.Collections.Generic;

namespace Broiler.Documents.Pdf.Syntax;

/// <summary>
/// Builds PDF objects from a <see cref="PdfLexer"/> token stream.
/// </summary>
/// <remarks>
/// Containers are assembled on an explicit stack rather than by recursion, so a
/// file nested a million arrays deep costs a depth check instead of the call
/// stack (PDF roadmap §7.1). Recovery is deliberately narrow: a token that
/// cannot start an object is consumed and reported, never used as the cue to
/// scan the file for something that looks like an object.
/// </remarks>
internal sealed class PdfObjectParser
{
    private readonly PdfLexer _lexer;
    private readonly PdfLimits _limits;
    private readonly PdfWorkBudget _budget;
    private readonly Queue<PdfToken> _lookahead = new();

    public PdfObjectParser(PdfLexer lexer, PdfWorkBudget budget)
    {
        _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        _limits = budget.Limits;
    }

    public PdfLexer Lexer => _lexer;

    /// <summary>True when the last <see cref="ParseObject"/> stopped on a structural error.</summary>
    public bool LastObjectWasMalformed { get; private set; }

    private PdfToken Next() => _lookahead.Count > 0 ? _lookahead.Dequeue() : _lexer.ReadToken();

    private PdfToken Peek(int distance)
    {
        while (_lookahead.Count <= distance)
            _lookahead.Enqueue(_lexer.ReadToken());

        int index = 0;
        foreach (PdfToken token in _lookahead)
        {
            if (index++ == distance)
                return token;
        }

        return new PdfToken(PdfTokenType.EndOfData, _lexer.Position, 0);
    }

    private void PushBack(PdfToken token)
    {
        var restored = new Queue<PdfToken>();
        restored.Enqueue(token);
        while (_lookahead.Count > 0)
            restored.Enqueue(_lookahead.Dequeue());
        while (restored.Count > 0)
            _lookahead.Enqueue(restored.Dequeue());
    }

    /// <summary>
    /// Returns the lexer to the first token this parser has buffered but not
    /// consumed, and empties the buffer.
    /// </summary>
    /// <remarks>
    /// Recognizing <c>n g R</c> needs two tokens of lookahead, so a parse that
    /// ends on a plain number leaves two tokens buffered. A caller that goes back
    /// to reading the lexer directly — the content interpreter between operators,
    /// the object store looking for the <c>stream</c> keyword — must rewind first,
    /// or those two tokens vanish.
    /// </remarks>
    public void Rewind()
    {
        if (_lookahead.Count == 0)
            return;

        PdfToken first = _lookahead.Peek();
        _lookahead.Clear();
        _lexer.Position = first.Start;
    }

    /// <summary>Parses exactly one object starting at the current position.</summary>
    public PdfObject ParseObject()
    {
        LastObjectWasMalformed = false;
        var stack = new List<Frame>();
        PdfObject? completed = null;

        while (true)
        {
            _budget.ChargeWork(4);
            PdfToken token = Next();

            if (token.Type == PdfTokenType.EndOfData)
            {
                LastObjectWasMalformed = stack.Count > 0;
                return Unwind(stack, completed);
            }

            switch (token.Type)
            {
                case PdfTokenType.ArrayStart:
                    if (stack.Count >= _limits.MaxNestingDepth)
                        throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxNestingDepth), _limits.MaxNestingDepth);
                    stack.Add(Frame.ForArray());
                    continue;

                case PdfTokenType.DictionaryStart:
                    if (stack.Count >= _limits.MaxNestingDepth)
                        throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxNestingDepth), _limits.MaxNestingDepth);
                    stack.Add(Frame.ForDictionary());
                    continue;

                case PdfTokenType.ArrayEnd:
                {
                    if (!TryCloseFrame(stack, wantArray: true, out PdfObject closed))
                    {
                        LastObjectWasMalformed = true;
                        return completed ?? PdfObject.Null;
                    }

                    if (!TryDeliver(stack, closed, ref completed))
                        return completed!;
                    continue;
                }

                case PdfTokenType.DictionaryEnd:
                {
                    if (!TryCloseFrame(stack, wantArray: false, out PdfObject closed))
                    {
                        LastObjectWasMalformed = true;
                        return completed ?? PdfObject.Null;
                    }

                    if (!TryDeliver(stack, closed, ref completed))
                        return completed!;
                    continue;
                }
            }

            PdfObject? value = ScalarFrom(token);
            if (value is null)
            {
                // A keyword where a value belongs. Inside a container it is skipped
                // (some producers emit stray operators); at top level it ends the
                // object and is pushed back for the caller to interpret.
                if (stack.Count == 0)
                {
                    PushBack(token);
                    LastObjectWasMalformed = completed is null;
                    return completed ?? PdfObject.Null;
                }

                LastObjectWasMalformed = true;
                continue;
            }

            if (!TryDeliver(stack, value, ref completed))
                return completed!;
        }
    }

    // Turns a scalar token into an object, folding "n g R" into a reference.
    private PdfObject? ScalarFrom(PdfToken token)
    {
        switch (token.Type)
        {
            case PdfTokenType.Integer:
                if (TryReadReference(token, out PdfReference? reference))
                    return reference;
                return new PdfNumber((long)token.Number);

            case PdfTokenType.Real:
                return new PdfNumber(token.Number);

            case PdfTokenType.Name:
                return new PdfName(token.Text);

            case PdfTokenType.LiteralString:
                return new PdfString(token.Bytes ?? [], hexadecimal: false);

            case PdfTokenType.HexString:
                return new PdfString(token.Bytes ?? [], hexadecimal: true);

            case PdfTokenType.Keyword:
                return token.Text switch
                {
                    "true" => PdfBoolean.True,
                    "false" => PdfBoolean.False,
                    "null" => PdfObject.Null,
                    _ => null,
                };

            default:
                return null;
        }
    }

    private bool TryReadReference(PdfToken first, out PdfReference? reference)
    {
        reference = null;
        if (first.Number < 0 || first.Number > int.MaxValue)
            return false;

        PdfToken second = Peek(0);
        if (second.Type != PdfTokenType.Integer || second.Number < 0 || second.Number > ushort.MaxValue)
            return false;

        PdfToken third = Peek(1);
        if (!third.IsKeyword("R"))
            return false;

        Next();
        Next();
        reference = new PdfReference((int)first.Number, (int)second.Number);
        return true;
    }

    // Places a finished value into the innermost container, or completes the parse.
    private bool TryDeliver(List<Frame> stack, PdfObject value, ref PdfObject? completed)
    {
        if (stack.Count == 0)
        {
            completed = value;
            return false;
        }

        Frame frame = stack[^1];
        if (frame.Array is { } array)
        {
            if (array.Count >= _limits.MaxContainerEntries)
                throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxContainerEntries), _limits.MaxContainerEntries);
            array.Add(value);
            return true;
        }

        PdfDictionary dictionary = frame.Dictionary!;

        if (frame.DiscardNextValue)
        {
            // Resynchronizing after a malformed key: drop the value that belonged
            // to it so the following pairs land on their own keys rather than
            // shifting by one for the rest of the dictionary.
            stack[^1] = frame.WithoutDiscard();
            return true;
        }

        if (frame.PendingKey is null)
        {
            if (value is PdfName name)
            {
                stack[^1] = frame.WithPendingKey(name.Value);
            }
            else
            {
                LastObjectWasMalformed = true;
                stack[^1] = frame.WithDiscard();
            }

            return true;
        }

        if (dictionary.Count >= _limits.MaxContainerEntries)
            throw PdfWorkBudget.Exceeded(nameof(PdfLimits.MaxContainerEntries), _limits.MaxContainerEntries);

        dictionary[frame.PendingKey] = value;
        stack[^1] = frame.WithPendingKey(null);
        return true;
    }

    private bool TryCloseFrame(List<Frame> stack, bool wantArray, out PdfObject closed)
    {
        closed = PdfObject.Null;
        if (stack.Count == 0)
            return false;

        Frame frame = stack[^1];
        bool isArray = frame.Array is not null;
        if (isArray != wantArray)
        {
            // Mismatched terminator: close the frame anyway so the parse can finish,
            // and report the object as malformed.
            LastObjectWasMalformed = true;
        }

        stack.RemoveAt(stack.Count - 1);
        closed = (PdfObject?)frame.Array ?? frame.Dictionary!;
        return true;
    }

    private static PdfObject Unwind(List<Frame> stack, PdfObject? completed)
    {
        if (completed is not null)
            return completed;
        if (stack.Count == 0)
            return PdfObject.Null;

        // Truncated input: return the outermost partially built container so the
        // caller sees the keys that were present rather than nothing at all.
        Frame outermost = stack[0];
        return (PdfObject?)outermost.Array ?? outermost.Dictionary!;
    }

    private readonly struct Frame
    {
        private Frame(PdfArray? array, PdfDictionary? dictionary, string? pendingKey, bool discardNextValue)
        {
            Array = array;
            Dictionary = dictionary;
            PendingKey = pendingKey;
            DiscardNextValue = discardNextValue;
        }

        public PdfArray? Array { get; }

        public PdfDictionary? Dictionary { get; }

        public string? PendingKey { get; }

        /// <summary>True while resynchronizing past a malformed dictionary key.</summary>
        public bool DiscardNextValue { get; }

        public static Frame ForArray() => new(new PdfArray(), null, null, false);

        public static Frame ForDictionary() => new(null, new PdfDictionary(), null, false);

        public Frame WithPendingKey(string? key) => new(Array, Dictionary, key, false);

        public Frame WithDiscard() => new(Array, Dictionary, null, true);

        public Frame WithoutDiscard() => new(Array, Dictionary, null, false);
    }
}
