namespace Broiler.Documents.Model;

/// <summary>
/// Specifies how a document section starts relative to the preceding content.
/// </summary>
public enum SectionBreakType
{
    /// <summary>The section continues immediately on the current page without starting a new page.</summary>
    Continuous = 0,

    /// <summary>The section starts on the next page.</summary>
    NextPage = 1,

    /// <summary>The section starts on the next text column.</summary>
    NextColumn = 2,

    /// <summary>The section starts on the next odd-numbered page, inserting a blank page if necessary.</summary>
    OddPage = 3,

    /// <summary>The section starts on the next even-numbered page, inserting a blank page if necessary.</summary>
    EvenPage = 4,
}
