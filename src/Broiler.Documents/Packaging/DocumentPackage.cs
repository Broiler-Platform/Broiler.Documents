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
        LoadOptions loadOptions = LoadOptions.None)
    {
        try
        {
            byte[]? bytes = ReadEntryBytes(entry, maxBytes);
            if (bytes is null)
            {
                diagnostics.Add(DocumentDiagnostic.Error(diagnosticCode + ".limit",
                    partDescription + " exceeded MaxBinBytes and was skipped."));
                return null;
            }

            using var stream = new MemoryStream(bytes, writable: false);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = (loadOptions & LoadOptions.PreserveWhitespace) == 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            return XDocument.Load(reader, loadOptions);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            diagnostics.Add(DocumentDiagnostic.Error(diagnosticCode,
                partDescription + " could not be parsed: " + ex.GetType().Name + "."));
            return null;
        }
    }
}
