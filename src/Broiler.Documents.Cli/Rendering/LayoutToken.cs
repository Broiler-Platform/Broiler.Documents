using System.Collections.Generic;

namespace Broiler.Documents.Cli.Rendering;

/// <summary>
/// An unbreakable run of pieces: one word, one whitespace gap, one tab, one
/// image - or the forced break, which is the one token that carries no pieces
/// at all and says only where the line ends.
/// </summary>
internal sealed class LayoutToken
{
    private LayoutToken(bool isWhitespace, bool isForcedBreak = false)
    {
        IsWhitespace = isWhitespace;
        IsForcedBreak = isForcedBreak;
    }

    public bool IsWhitespace { get; }

    /// <summary>True for the token U+2028 makes: end this line here.</summary>
    public bool IsForcedBreak { get; }

    public List<LayoutPiece> Pieces { get; } = new();

    public double Width { get; private set; }

    /// <summary>True for the single-piece token a tab makes.</summary>
    public bool IsTab => Pieces.Count == 1 && Pieces[0].IsTab;

    public static LayoutToken Empty(bool isWhitespace = false) => new(isWhitespace);

    /// <summary>
    /// The break itself. Not whitespace: the wrapping loop drops leading
    /// whitespace, and a break that arrived as whitespace would be dropped
    /// at the very place it is meant to act.
    /// </summary>
    public static LayoutToken ForcedBreak() => new(isWhitespace: false, isForcedBreak: true);

    public static LayoutToken Single(LayoutPiece piece, bool isWhitespace = false)
    {
        var token = new LayoutToken(isWhitespace);
        token.Add(piece);
        return token;
    }

    public void Add(LayoutPiece piece)
    {
        Pieces.Add(piece);
        Width += piece.Width;
    }

    /// <summary>
    /// Sets a tab's width once wrapping knows where on its line it starts.
    /// The piece is the same object the line will place, so both agree.
    /// </summary>
    public void ResolveTabWidth(double width)
    {
        Pieces[0].Width = width;
        Width = width;
    }
}
