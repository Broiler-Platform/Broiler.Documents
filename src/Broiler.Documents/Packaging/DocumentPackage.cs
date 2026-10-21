using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Broiler.Documents.Packaging;

/// <summary>Bounded ZIP entry and XML loading shared by packaged document codecs.</summary>
internal static class DocumentPackage
{
    public static byte[]? ReadEntryBytes(ZipArchiveEntry entry, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            // Read at most one byte beyond the remaining budget, even when the
            // ZIP header understates the decompressed size.
            long remaining = maxBytes - buffer.Length;
            int count = remaining >= chunk.Length ? chunk.Length : (int)remaining + 1;
            int read = stream.Read(chunk, 0, count);
            if (read == 0)
                return buffer.ToArray();
            if (read > remaining)
                return null;
            buffer.Write(chunk, 0, read);
        }
    }

    public static XDocument? LoadEntryXml(
        ZipArchiveEntry entry,
        long maxBytes,
        ICollection<DocumentDiagnostic> diagnostics,
        string diagnosticCode,
        string partDescription,
        LoadOptions loadOptions = LoadOptions.None,
        XmlImpliedPrefixes? impliedPrefixes = null)
    {
        byte[]? bytes;
        try
        {
            bytes = ReadEntryBytes(entry, maxBytes);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(DocumentDiagnostic.Error(diagnosticCode,
                partDescription + " could not be parsed: " + ex.GetType().Name + "."));
            return null;
        }

        if (bytes is null)
        {
            diagnostics.Add(DocumentDiagnostic.Error(diagnosticCode + ".limit",
                partDescription + " exceeded MaxBinBytes and was skipped."));
            return null;
        }

        return LoadXml(bytes, diagnostics, diagnosticCode, partDescription, loadOptions, impliedPrefixes);
    }

    /// <summary>
    /// Parses a whole XML document held in memory with the same settings a
    /// package part gets: no DTD, no resolver, comments and processing
    /// instructions dropped. A flat single-file document comes through here
    /// rather than through a ZIP entry.
    /// </summary>
    /// <remarks>
    /// A document that uses a prefix it never declares is not well-formed XML,
    /// and the first parse refuses it. With <paramref name="impliedPrefixes"/>
    /// the parse is tried once more with those prefixes bound in an outer
    /// scope, so a declaration the document does make still wins, and the
    /// retry is reported. Anything else wrong with the XML fails both times.
    /// </remarks>
    public static XDocument? LoadXml(
        byte[] bytes,
        ICollection<DocumentDiagnostic> diagnostics,
        string diagnosticCode,
        string partDescription,
        LoadOptions loadOptions = LoadOptions.None,
        XmlImpliedPrefixes? impliedPrefixes = null)
    {
        try
        {
            return Parse(bytes, loadOptions, context: null);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            if (ex is XmlException && impliedPrefixes is not null)
            {
                try
                {
                    XDocument document = Parse(bytes, loadOptions, impliedPrefixes.CreateContext());
                    diagnostics.Add(DocumentDiagnostic.Warning(impliedPrefixes.DiagnosticCode,
                        partDescription + " " + impliedPrefixes.Message));
                    return document;
                }
                catch (Exception retry) when (retry is XmlException or InvalidDataException)
                {
                }
            }

            diagnostics.Add(DocumentDiagnostic.Error(diagnosticCode,
                partDescription + " could not be parsed: " + ex.GetType().Name + "."));
            return null;
        }
    }

    private static XDocument Parse(byte[] bytes, LoadOptions loadOptions, XmlParserContext? context)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = (loadOptions & LoadOptions.PreserveWhitespace) == 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        }, context);
        return XDocument.Load(reader, loadOptions);
    }
}

/// <summary>
/// Prefixes a format's readers take as bound when a document uses them without
/// declaring them, and what to report when a document needed that.
/// </summary>
/// <param name="Prefixes">Each prefix and the namespace it stands for.</param>
/// <param name="DiagnosticCode">The warning reported when the parse relied on them.</param>
/// <param name="Message">The warning's text, after the part's description.</param>
internal sealed record XmlImpliedPrefixes(
    IReadOnlyDictionary<string, string> Prefixes,
    string DiagnosticCode,
    string Message)
{
    public XmlParserContext CreateContext()
    {
        var names = new NameTable();
        var scope = new XmlNamespaceManager(names);
        foreach ((string prefix, string uri) in Prefixes)
            scope.AddNamespace(prefix, uri);

        return new XmlParserContext(names, scope, xmlLang: null, XmlSpace.None);
    }
}
