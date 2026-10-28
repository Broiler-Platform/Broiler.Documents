namespace Broiler.Documents.Rtf;

/// <summary>The kind of content the reader is currently inside (a group's destination).</summary>
internal enum RtfDestination
{
    /// <summary>Ordinary document body text.</summary>
    Normal,

    /// <summary>An unknown or intentionally ignored destination — its text is dropped.</summary>
    Skip,

    /// <summary>An unsupported list table; its entire subtree stays opaque.</summary>
    ListTable,

    /// <summary>The <c>\stylesheet</c> destination: style definitions.</summary>
    StyleSheet,

    /// <summary>The <c>\revtbl</c> destination: the authors tracked changes name by index.</summary>
    RevisionTable,

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

    /// <summary>A <c>\shp</c> drawing: its geometry and its properties.</summary>
    Shape,

    /// <summary>A legacy drawing-object destination.</summary>
    LegacyDrawing,

    /// <summary>One <c>\sp</c> property pair inside a shape.</summary>
    ShapeProperty,

    /// <summary>A property's name, from <c>\sn</c>.</summary>
    ShapePropertyName,

    /// <summary>A property's value, from <c>\sv</c>.</summary>
    ShapePropertyValue,

    /// <summary>A shape's own text, from <c>\shptxt</c>.</summary>
    ShapeText,

    /// <summary>The <c>\*\generator</c> destination: the name of what wrote the document.</summary>
    Generator,

    /// <summary>A <c>\footnote</c> group cited from the body: the note's own text.</summary>
    Note,
}
