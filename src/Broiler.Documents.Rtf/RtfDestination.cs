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
}
