namespace Broiler.Documents.Model;

/// <summary>Which pages a running header or footer is drawn on.</summary>
public enum PageSelection
{
    /// <summary>Every page the more specific selections do not claim.</summary>
    Default = 0,

    /// <summary>The first page only, when the document asks for a distinct one.</summary>
    First,

    /// <summary>Even-numbered pages, when the document asks for distinct ones.</summary>
    Even,
}
