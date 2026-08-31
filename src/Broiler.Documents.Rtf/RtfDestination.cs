namespace Broiler.Documents.Rtf;

/// <summary>The kind of content the reader is currently inside (a group's destination).</summary>
internal enum RtfDestination
{
    /// <summary>Ordinary document body text.</summary>
    Normal,

    /// <summary>An unknown or intentionally ignored destination — its text is dropped.</summary>
    Skip,

    /// <summary>The <c>\fonttbl</c> destination.</summary>
    FontTable,

    /// <summary>The <c>\colortbl</c> destination.</summary>
    ColorTable,

    /// <summary>A <c>\field</c> container.</summary>
    Field,

    /// <summary>A field's <c>\fldinst</c> (instruction) destination.</summary>
    FieldInstruction,

    /// <summary>A field's <c>\fldrslt</c> (result/display) destination.</summary>
    FieldResult,

    /// <summary>The <c>\header</c> destination: the header for every page.</summary>
    Header,

    /// <summary>The <c>\headerf</c> destination: the first page's header.</summary>
    HeaderFirst,

    /// <summary>The <c>\headerl</c> destination: the header for left, i.e. even, pages.</summary>
    HeaderEven,

    /// <summary>The <c>\footer</c> destination.</summary>
    Footer,

    /// <summary>The <c>\footerf</c> destination.</summary>
    FooterFirst,

    /// <summary>The <c>\footerl</c> destination.</summary>
    FooterEven,
}
