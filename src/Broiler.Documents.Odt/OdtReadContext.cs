namespace Broiler.Documents.Odt;

/// <summary>Everything a read needs to turn one element into document content.</summary>
/// <remarks>
/// One of these belongs to one flow of text. The body builds its own, and so
/// does every header, every footer, and every shape that keeps its text to
/// itself, which is what stops a page break stated at the end of a header
/// coming out at the top of the body. A text box read as body content is
/// body content and shares the body's, which is the same answer read the
/// other way round.
/// </remarks>
internal sealed record OdtReadContext(
    OdtStyles Styles,
    OdtImageLoader Images,
    OdtDocumentBuilder Builder)
{
    private bool _pendingPageBreak;

    /// <summary>
    /// Records that the paragraph just read asked for a page break after
    /// itself, so the next paragraph in this flow can carry it.
    /// </summary>
    /// <remarks>
    /// A break stated after paragraph five and a break stated before
    /// paragraph six are the same break - that identity is the reason the
    /// model holds one end of it and not both. Moving the far spelling onto
    /// the near paragraph is therefore not a guess about what the author
    /// meant; it is the only way the model has of writing down what the
    /// document already said. The alternative, reporting it and dropping it,
    /// would be honest about the loss and would still lose a page break a
    /// word processor draws.
    /// </remarks>
    public void HoldPageBreak(bool wanted) => _pendingPageBreak = wanted;

    /// <summary>Hands any held break to the paragraph now starting.</summary>
    public bool TakePageBreak()
    {
        bool held = _pendingPageBreak;
        _pendingPageBreak = false;
        return held;
    }

    /// <summary>
    /// Gives up on a held break, because nothing follows it that the model
    /// could hang one on: the flow ended, or the next block is a table, whose
    /// page break would belong to the table rather than to a paragraph inside
    /// somebody's cell. It goes out as a diagnostic rather than quietly,
    /// since a break the reader knew about and could not place is exactly the
    /// thing a caller comparing page counts needs told.
    /// </summary>
    public void DropPageBreak()
    {
        if (!_pendingPageBreak)
            return;

        _pendingPageBreak = false;
        Builder.AddDiagnosticOnce(
            "odt.break.after",
            "A fo:break-after page break had no following paragraph to carry it and was dropped.");
    }
}
