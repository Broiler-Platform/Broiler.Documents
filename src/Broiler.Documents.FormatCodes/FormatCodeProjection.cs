using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Broiler.Documents.Model;

namespace Broiler.Documents.FormatCodes;

/// <summary>An immutable canonical projection and its source mappings.</summary>
public sealed class FormatCodeProjection
{
    private readonly RichTextDocument _document;
    private readonly ReadOnlyCollection<FormatCodeToken> _tokens;
    private readonly ReadOnlyCollection<FormatCodeToken> _pendingTokens;
    private readonly ReadOnlyCollection<FormatCodeDiagnostic> _diagnostics;

    internal FormatCodeProjection(
        RichTextDocument document,
        string text,
        IReadOnlyList<FormatCodeToken> tokens,
        IReadOnlyList<FormatCodeToken> pendingTokens,
        IReadOnlyList<FormatCodeDiagnostic> diagnostics)
    {
        _document = document;
        Text = text;
        _tokens = new List<FormatCodeToken>(tokens).AsReadOnly();
        _pendingTokens = new List<FormatCodeToken>(pendingTokens).AsReadOnly();
        _diagnostics = new List<FormatCodeDiagnostic>(diagnostics).AsReadOnly();
    }

    public int GrammarVersion => 1;

    public string Text { get; }

    /// <summary>Canonical tokens whose display text concatenates to <see cref="Text"/>.</summary>
    public IReadOnlyList<FormatCodeToken> Tokens => _tokens;

    /// <summary>Transient caret-formatting tokens excluded from canonical text.</summary>
    public IReadOnlyList<FormatCodeToken> PendingTokens => _pendingTokens;

    public IReadOnlyList<FormatCodeDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Maps a document position to the earliest or latest projected caret at that boundary.</summary>
    public FormatCodeCaret MapDocumentPosition(
        RichTextPosition position,
        FormatCodeBoundaryAffinity affinity = FormatCodeBoundaryAffinity.After)
    {
        if (!_document.IsValid(position))
            throw new ArgumentOutOfRangeException(nameof(position), "Position is not valid for the projected document.");

        if (_tokens.Count == 0)
            return new FormatCodeCaret(-1, 0, affinity);

        FormatCodeCaret? selected = null;
        int selectedOffset = affinity == FormatCodeBoundaryAffinity.Before ? int.MaxValue : int.MinValue;

        for (int i = 0; i < _tokens.Count; i++)
        {
            FormatCodeToken token = _tokens[i];
            AddCandidateIfEqual(token.SourceBefore, new FormatCodeCaret(i, 0, FormatCodeBoundaryAffinity.Before));
            AddCandidateIfEqual(
                token.SourceAfter,
                new FormatCodeCaret(i, token.ProjectedLength, FormatCodeBoundaryAffinity.After));

            if (token.MappingMode == FormatCodeMappingMode.Linear &&
                position.ParagraphIndex == token.SourceBefore.ParagraphIndex &&
                position.Offset > token.SourceBefore.Offset &&
                position.Offset < token.SourceAfter.Offset)
            {
                int relative = position.Offset - token.SourceBefore.Offset;
                AddCandidate(new FormatCodeCaret(i, relative, affinity));
            }
        }

        if (selected is not null)
            return selected.Value;

        throw new InvalidOperationException("The projection does not contain a mapping for a valid document position.");

        void AddCandidateIfEqual(RichTextPosition source, FormatCodeCaret candidate)
        {
            if (source == position)
                AddCandidate(candidate);
        }

        void AddCandidate(FormatCodeCaret candidate)
        {
            int projectedOffset = GetProjectedOffset(candidate);
            bool replace = affinity == FormatCodeBoundaryAffinity.Before
                ? projectedOffset < selectedOffset ||
                  (projectedOffset == selectedOffset && candidate.TokenIndex < selected?.TokenIndex)
                : projectedOffset > selectedOffset ||
                  (projectedOffset == selectedOffset && candidate.TokenIndex > selected?.TokenIndex);
            if (selected is null || replace)
            {
                selected = candidate;
                selectedOffset = projectedOffset;
            }
        }
    }

    /// <summary>Maps every canonical projected boundary to a predictable document position.</summary>
    public FormatCodeMappedPosition MapProjectedOffset(int projectedOffset)
    {
        if (projectedOffset < 0 || projectedOffset > Text.Length)
            throw new ArgumentOutOfRangeException(nameof(projectedOffset));

        if (_tokens.Count == 0)
        {
            return new FormatCodeMappedPosition(
                new FormatCodeCaret(-1, 0, FormatCodeBoundaryAffinity.After),
                _document.Start,
                RichTextRange.Caret(_document.Start));
        }

        int tokenIndex = FindToken(projectedOffset);
        FormatCodeToken token = _tokens[tokenIndex];
        int relative = projectedOffset - token.ProjectedStart;
        RichTextPosition position;
        FormatCodeBoundaryAffinity affinity;

        if (token.MappingMode == FormatCodeMappingMode.Linear)
        {
            int sourceOffset = token.SourceBefore.Offset + relative;
            string sourceText = _document.Paragraphs[token.SourceBefore.ParagraphIndex].Text;
            bool splitsSurrogatePair = sourceOffset > 0 && sourceOffset < sourceText.Length &&
                char.IsLowSurrogate(sourceText[sourceOffset]) &&
                char.IsHighSurrogate(sourceText[sourceOffset - 1]);
            if (splitsSurrogatePair)
                sourceOffset--;
            position = new RichTextPosition(
                token.SourceBefore.ParagraphIndex,
                sourceOffset);
            affinity = relative == 0 || splitsSurrogatePair
                ? FormatCodeBoundaryAffinity.Before
                : FormatCodeBoundaryAffinity.After;
        }
        else if (token.MappingMode == FormatCodeMappingMode.Boundary ||
                 relative * 2 <= token.ProjectedLength)
        {
            position = token.SourceBefore;
            affinity = FormatCodeBoundaryAffinity.Before;
        }
        else
        {
            position = token.SourceAfter;
            affinity = FormatCodeBoundaryAffinity.After;
        }

        return new FormatCodeMappedPosition(
            new FormatCodeCaret(tokenIndex, relative, affinity),
            position,
            token.AffectedRange);
    }

    /// <summary>Returns the canonical UTF-16 offset represented by a projected caret.</summary>
    public int GetProjectedOffset(FormatCodeCaret caret)
    {
        if (caret.TokenIndex == -1 && _tokens.Count == 0)
            return 0;
        if (caret.TokenIndex < 0 || caret.TokenIndex >= _tokens.Count)
            throw new ArgumentOutOfRangeException(nameof(caret));

        FormatCodeToken token = _tokens[caret.TokenIndex];
        if (caret.OffsetWithinToken < 0 || caret.OffsetWithinToken > token.ProjectedLength)
            throw new ArgumentOutOfRangeException(nameof(caret));
        return token.ProjectedStart + caret.OffsetWithinToken;
    }

    private int FindToken(int projectedOffset)
    {
        if (projectedOffset == Text.Length)
            return _tokens.Count - 1;

        int low = 0;
        int high = _tokens.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            FormatCodeToken token = _tokens[middle];
            if (projectedOffset < token.ProjectedStart)
            {
                high = middle - 1;
            }
            else if (projectedOffset >= token.ProjectedStart + token.ProjectedLength)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }

        throw new InvalidOperationException("Canonical tokens do not cover the projected text.");
    }
}
