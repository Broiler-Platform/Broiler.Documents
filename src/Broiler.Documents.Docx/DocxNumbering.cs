using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Broiler.Documents.Model;
using static Broiler.Documents.Docx.DocxValueReader;

namespace Broiler.Documents.Docx;

internal sealed class DocxNumbering
{
    private readonly Dictionary<int, ListKind> _numKinds;

    private DocxNumbering(Dictionary<int, ListKind> numKinds) => _numKinds = numKinds;

    public static DocxNumbering Empty { get; } = new(new Dictionary<int, ListKind>());

    public static DocxNumbering Load(
        ZipArchive archive,
        DocxRelationships documentRelationships,
        string documentBaseDirectory,
        DocumentLimits limits,
        List<DocumentDiagnostic> diagnostics)
    {
        string numberingPath = DocxPackage.ResolvePartPath(
            documentRelationships,
            DocxNamespaces.NumberingRelationship,
            documentBaseDirectory,
            "numbering.xml");

        ZipArchiveEntry? entry = DocxPackage.FindEntry(archive, numberingPath);
        if (entry is null)
            return Empty;

        XDocument? xml = DocxPackage.LoadEntryXml(entry, limits, diagnostics, "docx.numbering");
        if (xml?.Root is null)
            return Empty;

        var abstractKinds = new Dictionary<int, ListKind>();
        foreach (XElement abstractNum in xml.Root.Elements(DocxNamespaces.Wordprocessing + "abstractNum"))
        {
            if (!TryReadInt(abstractNum.Attribute(DocxNamespaces.Wordprocessing + "abstractNumId"), out int abstractId))
                continue;

            XElement? level = abstractNum.Elements(DocxNamespaces.Wordprocessing + "lvl").FirstOrDefault();
            string? format = WordValue(level?.Element(DocxNamespaces.Wordprocessing + "numFmt"));
            abstractKinds[abstractId] = format is "decimal" or "decimalZero" or "upperRoman" or "lowerRoman" or "upperLetter" or "lowerLetter"
                ? ListKind.Numbered
                : ListKind.Bullet;
        }

        var numKinds = new Dictionary<int, ListKind>();
        foreach (XElement num in xml.Root.Elements(DocxNamespaces.Wordprocessing + "num"))
        {
            if (!TryReadInt(num.Attribute(DocxNamespaces.Wordprocessing + "numId"), out int numId))
                continue;
            if (!TryReadInt(num.Element(DocxNamespaces.Wordprocessing + "abstractNumId")?.Attribute(DocxNamespaces.Wordprocessing + "val"), out int abstractId))
                continue;
            if (abstractKinds.TryGetValue(abstractId, out ListKind kind))
                numKinds[numId] = kind;
        }

        return new DocxNumbering(numKinds);
    }

    public ListKind KindFor(int numId) =>
        _numKinds.TryGetValue(numId, out ListKind kind) ? kind : ListKind.Bullet;
}
