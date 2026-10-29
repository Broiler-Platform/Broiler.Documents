namespace Broiler.Documents.Model;

/// <summary>What a <see cref="PageField"/> counts.</summary>
public enum PageFieldKind
{
    /// <summary>The number of the page the field is drawn on.</summary>
    PageNumber,

    /// <summary>How many pages the document has.</summary>
    PageCount,
}

/// <summary>
/// A field whose text is worked out from the page it is drawn on: ODF's
/// <c>text:page-number</c> and <c>text:page-count</c>.
/// </summary>
/// <remarks>
/// <para>
/// The run holding the field keeps the text the document last computed for it,
/// which is what every reader that cannot lay pages out shows. A header or
/// footer is laid out again on every page, and there the layout draws the
/// field's own value instead (<see cref="Text"/>). A word processor saves the
/// value from wherever it last drew the header, so the saved text is the right
/// number on at most one page.
/// </para>
/// <para>
/// The number is the page's number, not its position: a section that restarts
/// the count (<see cref="DocumentSection.PageNumberStart"/>) starts it again.
/// </para>
/// </remarks>
/// <param name="Kind">What the field counts.</param>
/// <param name="Format">How the number is written; <see cref="ListNumberFormat.None"/> writes nothing.</param>
/// <param name="Adjust">
/// Added to the page number before it is written: ODF's <c>text:page-adjust</c>,
/// and -1 or +1 for a field that shows the previous or the next page's number.
/// </param>
public sealed record PageField(PageFieldKind Kind, ListNumberFormat Format = ListNumberFormat.Decimal, int Adjust = 0)
{
    /// <summary>
    /// The field's text on a page numbered <paramref name="pageNumber"/> in a
    /// document of <paramref name="pageCount"/> pages, or null for a page count
    /// not known yet. A number adjusted to before the first page shows nothing,
    /// as LibreOffice shows nothing for a page that is not there.
    /// </summary>
    public string? Text(int pageNumber, int? pageCount)
    {
        int? value = Kind == PageFieldKind.PageNumber ? pageNumber + Adjust : pageCount;
        if (value is not int number)
            return null;

        return number < 1 || Format == ListNumberFormat.None ? "" : ListNumberer.Format(number, Format);
    }
}
