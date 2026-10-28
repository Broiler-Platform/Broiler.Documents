using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Broiler.Documents.Model;
using Broiler.Documents.Resources;
using Xunit;

namespace Broiler.Documents.Tests;

/// <summary>
/// Locks down the exported public surface of <c>Broiler.Documents</c> and <c>Broiler.Documents.Model</c>
/// and enforces that every public type and member is fully documented in XML.
/// </summary>
public sealed class PublicApiFreezeTests
{
    private static readonly string[] FrozenDocumentsExportedTypes =
    [
        "Broiler.Documents.DocumentCodec",
        "Broiler.Documents.DocumentCodecCatalog",
        "Broiler.Documents.DocumentCodecMatch",
        "Broiler.Documents.DocumentCodecSelection",
        "Broiler.Documents.DocumentDate",
        "Broiler.Documents.DocumentDestinationState",
        "Broiler.Documents.DocumentDiagnostic",
        "Broiler.Documents.DocumentDiagnosticCodes",
        "Broiler.Documents.DocumentDiagnosticLocation",
        "Broiler.Documents.DocumentDiagnosticSeverity",
        "Broiler.Documents.DocumentException",
        "Broiler.Documents.DocumentFormatDescriptor",
        "Broiler.Documents.DocumentInput",
        "Broiler.Documents.DocumentLimitExceededException",
        "Broiler.Documents.DocumentLimits",
        "Broiler.Documents.DocumentLinkTarget",
        "Broiler.Documents.DocumentMetadata",
        "Broiler.Documents.DocumentMetadataReport",
        "Broiler.Documents.DocumentProbeConfidence",
        "Broiler.Documents.DocumentProbeRequest",
        "Broiler.Documents.DocumentProbeResult",
        "Broiler.Documents.DocumentReadOptions",
        "Broiler.Documents.DocumentReadRequest",
        "Broiler.Documents.DocumentReadResult",
        "Broiler.Documents.DocumentResourceDecision",
        "Broiler.Documents.DocumentResourcePolicy",
        "Broiler.Documents.DocumentResourceRequest",
        "Broiler.Documents.DocumentResultStatus",
        "Broiler.Documents.DocumentSourceHints",
        "Broiler.Documents.DocumentSpoolPolicy",
        "Broiler.Documents.DocumentTimestamp",
        "Broiler.Documents.DocumentWriteOptions",
        "Broiler.Documents.DocumentWriteRequest",
        "Broiler.Documents.DocumentWriteResult",
        "Broiler.Documents.Resources.DocumentConversionContext",
        "Broiler.Documents.Resources.DocumentConversionContextBuilder",
        "Broiler.Documents.Resources.DocumentFontEmbedding",
        "Broiler.Documents.Resources.DocumentFontResource",
        "Broiler.Documents.Resources.DocumentFontSet",
        "Broiler.Documents.Resources.DocumentResourceBinding",
        "Broiler.Documents.Resources.DocumentResourceDisposition",
        "Broiler.Documents.Resources.DocumentResourceEntry",
        "Broiler.Documents.Resources.DocumentResourceGate",
        "Broiler.Documents.Resources.DocumentResourceKind",
        "Broiler.Documents.Resources.DocumentResourceOperations",
        "Broiler.Documents.Resources.DocumentResourceProvenance",
        "Broiler.Documents.XmpDate",
        "Broiler.Documents.XmpMetadata",
        "Broiler.Documents.XmpReader",
        "Broiler.Documents.XmpReadOutcome",
        "Broiler.Documents.XmpReadResult",
    ];

    [Fact]
    public void Exported_Types_In_Broiler_Documents_Are_Frozen()
    {
        var actualTypes = typeof(DocumentCodec).Assembly.GetExportedTypes()
            .Select(t => t.FullName!)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(FrozenDocumentsExportedTypes.OrderBy(n => n), actualTypes);
    }

    [Fact]
    public void DocumentReadOptions_Surface_Is_Frozen()
    {
        Type type = typeof(DocumentReadOptions);

        Assert.False(type.IsSealed, "DocumentReadOptions is open for codec-specific derivation.");

        // Obsolete option must remain removed
        Assert.Null(type.GetProperty("DecodeEmbeddedObjects"));

        // Frozen properties
        PropertyInfo limitsProp = AssertProperty(type, nameof(DocumentReadOptions.Limits), typeof(DocumentLimits));
        Assert.True(limitsProp.CanRead && !limitsProp.CanWrite);

        PropertyInfo codePageProp = AssertProperty(type, nameof(DocumentReadOptions.DefaultCodePage), typeof(int));
        Assert.True(codePageProp.CanRead && !codePageProp.CanWrite);

        PropertyInfo resourcePolicyProp = AssertProperty(type, nameof(DocumentReadOptions.ResourcePolicy), typeof(DocumentResourcePolicy));
        Assert.True(resourcePolicyProp.CanRead && !resourcePolicyProp.CanWrite);

        PropertyInfo defaultProp = AssertProperty(type, nameof(DocumentReadOptions.Default), typeof(DocumentReadOptions));
        Assert.True(defaultProp.GetMethod!.IsStatic);

        // Constant
        FieldInfo constField = type.GetField(nameof(DocumentReadOptions.Windows1252CodePage), BindingFlags.Public | BindingFlags.Static)!;
        Assert.NotNull(constField);
        Assert.Equal(1252, (int)constField.GetValue(null)!);

        // Constructor
        ConstructorInfo ctor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = ctor.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal("limits", parameters[0].Name);
        Assert.Equal(typeof(DocumentLimits), parameters[0].ParameterType);
        Assert.Equal("defaultCodePage", parameters[1].Name);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal("resourcePolicy", parameters[2].Name);
        Assert.Equal(typeof(DocumentResourcePolicy), parameters[2].ParameterType);
    }

    [Fact]
    public void DocumentWriteOptions_Surface_Is_Frozen()
    {
        Type type = typeof(DocumentWriteOptions);

        Assert.False(type.IsSealed, "DocumentWriteOptions is open for codec-specific derivation.");

        PropertyInfo asciiOnlyProp = AssertProperty(type, nameof(DocumentWriteOptions.AsciiOnly), typeof(bool));
        Assert.True(asciiOnlyProp.CanRead && !asciiOnlyProp.CanWrite);

        PropertyInfo resourcesProp = AssertProperty(type, nameof(DocumentWriteOptions.Resources), typeof(DocumentConversionContext));
        Assert.True(resourcesProp.CanRead && !resourcesProp.CanWrite);

        PropertyInfo fontsProp = AssertProperty(type, nameof(DocumentWriteOptions.Fonts), typeof(DocumentFontSet));
        Assert.True(fontsProp.CanRead && !fontsProp.CanWrite);

        PropertyInfo metadataProp = AssertProperty(type, nameof(DocumentWriteOptions.Metadata), typeof(DocumentMetadata));
        Assert.True(metadataProp.CanRead && !metadataProp.CanWrite);

        PropertyInfo defaultProp = AssertProperty(type, nameof(DocumentWriteOptions.Default), typeof(DocumentWriteOptions));
        Assert.True(defaultProp.GetMethod!.IsStatic);

        // Constructor
        ConstructorInfo ctor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = ctor.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal("asciiOnly", parameters[0].Name);
        Assert.Equal(typeof(bool), parameters[0].ParameterType);
        Assert.Equal("resources", parameters[1].Name);
        Assert.Equal(typeof(DocumentConversionContext), parameters[1].ParameterType);
        Assert.Equal("fonts", parameters[2].Name);
        Assert.Equal(typeof(DocumentFontSet), parameters[2].ParameterType);
        Assert.Equal("metadata", parameters[3].Name);
        Assert.Equal(typeof(DocumentMetadata), parameters[3].ParameterType);
    }

    [Fact]
    public void DocumentLimits_Surface_Is_Frozen()
    {
        Type type = typeof(DocumentLimits);
        Assert.True(type.IsSealed);

        Assert.Equal(4096, DocumentLimits.DefaultMaxProbeBytes);
        Assert.Equal(64L * 1024 * 1024, DocumentLimits.DefaultMaxDocumentBytes);
        Assert.Equal(256, DocumentLimits.DefaultMaxGroupDepth);
        Assert.Equal(1 << 20, DocumentLimits.DefaultMaxRunLength);
        Assert.Equal(1 << 20, DocumentLimits.DefaultMaxParagraphCount);
        Assert.Equal(16L * 1024 * 1024, DocumentLimits.DefaultMaxBinBytes);

        var limits = DocumentLimits.Default;
        Assert.Equal(DocumentLimits.DefaultMaxProbeBytes, limits.MaxProbeBytes);
        Assert.Equal(DocumentLimits.DefaultMaxDocumentBytes, limits.MaxDocumentBytes);
        Assert.Equal(DocumentLimits.DefaultMaxGroupDepth, limits.MaxGroupDepth);
        Assert.Equal(DocumentLimits.DefaultMaxRunLength, limits.MaxRunLength);
        Assert.Equal(DocumentLimits.DefaultMaxParagraphCount, limits.MaxParagraphCount);
        Assert.Equal(DocumentLimits.DefaultMaxBinBytes, limits.MaxBinBytes);
    }

    [Fact]
    public void DocumentSpoolPolicy_Surface_Is_Frozen()
    {
        Type type = typeof(DocumentSpoolPolicy);
        Assert.True(type.IsSealed);

        // Constants
        FieldInfo maxSpoolField = type.GetField(nameof(DocumentSpoolPolicy.DefaultMaxSpoolBytes), BindingFlags.Public | BindingFlags.Static)!;
        Assert.NotNull(maxSpoolField);
        Assert.Equal(64L * 1024 * 1024, (long)maxSpoolField.GetValue(null)!);

        FieldInfo memThreshField = type.GetField(nameof(DocumentSpoolPolicy.DefaultMemoryThresholdBytes), BindingFlags.Public | BindingFlags.Static)!;
        Assert.NotNull(memThreshField);
        Assert.Equal(64 * 1024, (int)memThreshField.GetValue(null)!);

        FieldInfo prefixField = type.GetField(nameof(DocumentSpoolPolicy.DefaultFilePrefix), BindingFlags.Public | BindingFlags.Static)!;
        Assert.NotNull(prefixField);
        Assert.Equal("broiler-spool-", (string)prefixField.GetValue(null)!);

        // Properties
        PropertyInfo dirProp = AssertProperty(type, nameof(DocumentSpoolPolicy.DirectoryPath), typeof(string));
        Assert.True(dirProp.CanRead && !dirProp.CanWrite);

        PropertyInfo quotaProp = AssertProperty(type, nameof(DocumentSpoolPolicy.MaxSpoolBytes), typeof(long));
        Assert.True(quotaProp.CanRead && !quotaProp.CanWrite);

        PropertyInfo threshProp = AssertProperty(type, nameof(DocumentSpoolPolicy.MemoryThresholdBytes), typeof(int));
        Assert.True(threshProp.CanRead && !threshProp.CanWrite);

        PropertyInfo pfxProp = AssertProperty(type, nameof(DocumentSpoolPolicy.FilePrefix), typeof(string));
        Assert.True(pfxProp.CanRead && !pfxProp.CanWrite);

        PropertyInfo delProp = AssertProperty(type, nameof(DocumentSpoolPolicy.DeleteOnDispose), typeof(bool));
        Assert.True(delProp.CanRead && !delProp.CanWrite);

        // Constructor
        ConstructorInfo ctor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = ctor.GetParameters();
        Assert.Equal(5, parameters.Length);
        Assert.Equal("directoryPath", parameters[0].Name);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal("maxSpoolBytes", parameters[1].Name);
        Assert.Equal(typeof(long), parameters[1].ParameterType);
        Assert.Equal("memoryThresholdBytes", parameters[2].Name);
        Assert.Equal(typeof(int), parameters[2].ParameterType);
        Assert.Equal("filePrefix", parameters[3].Name);
        Assert.Equal(typeof(string), parameters[3].ParameterType);
        Assert.Equal("deleteOnDispose", parameters[4].Name);
        Assert.Equal(typeof(bool), parameters[4].ParameterType);

        // CleanOrphanedFiles method
        MethodInfo cleanMethod = type.GetMethod(nameof(DocumentSpoolPolicy.CleanOrphanedFiles), BindingFlags.Public | BindingFlags.Instance)!;
        Assert.NotNull(cleanMethod);
        Assert.Equal(typeof(int), cleanMethod.ReturnType);
        ParameterInfo[] cleanParams = cleanMethod.GetParameters();
        Assert.Single(cleanParams);
        Assert.Equal("olderThan", cleanParams[0].Name);
        Assert.Equal(typeof(TimeSpan?), cleanParams[0].ParameterType);
    }

    [Fact]
    public void DocumentWriteRequest_Surface_Is_Frozen()
    {
        Type type = typeof(DocumentWriteRequest);
        Assert.True(type.IsSealed);

        PropertyInfo docProp = AssertProperty(type, nameof(DocumentWriteRequest.Document), typeof(RichTextDocument));
        Assert.True(docProp.CanRead && !docProp.CanWrite);

        PropertyInfo destProp = AssertProperty(type, nameof(DocumentWriteRequest.Destination), typeof(Stream));
        Assert.True(destProp.CanRead && !destProp.CanWrite);

        PropertyInfo optProp = AssertProperty(type, nameof(DocumentWriteRequest.Options), typeof(DocumentWriteOptions));
        Assert.True(optProp.CanRead && !optProp.CanWrite);

        PropertyInfo ctProp = AssertProperty(type, nameof(DocumentWriteRequest.CancellationToken), typeof(CancellationToken));
        Assert.True(ctProp.CanRead && !ctProp.CanWrite);

        PropertyInfo atomicProp = AssertProperty(type, nameof(DocumentWriteRequest.RequireAtomic), typeof(bool));
        Assert.True(atomicProp.CanRead && !atomicProp.CanWrite);

        PropertyInfo stagedProp = AssertProperty(type, nameof(DocumentWriteRequest.IsStaged), typeof(bool));
        Assert.True(stagedProp.CanRead && !stagedProp.CanWrite);

        // Constructor
        ConstructorInfo ctor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = ctor.GetParameters();
        Assert.Equal(6, parameters.Length);
        Assert.Equal("document", parameters[0].Name);
        Assert.Equal(typeof(RichTextDocument), parameters[0].ParameterType);
        Assert.Equal("destination", parameters[1].Name);
        Assert.Equal(typeof(Stream), parameters[1].ParameterType);
        Assert.Equal("options", parameters[2].Name);
        Assert.Equal(typeof(DocumentWriteOptions), parameters[2].ParameterType);
        Assert.Equal("cancellationToken", parameters[3].Name);
        Assert.Equal(typeof(CancellationToken), parameters[3].ParameterType);
        Assert.Equal("requireAtomic", parameters[4].Name);
        Assert.Equal(typeof(bool), parameters[4].ParameterType);
        Assert.Equal("isStaged", parameters[5].Name);
        Assert.Equal(typeof(bool), parameters[5].ParameterType);

        // Diagnostic code
        Assert.Equal("document.commit.atomic-required", DocumentDiagnosticCodes.CommitAtomicRequired);
    }

    [Fact]
    public void All_Exported_Types_And_Members_In_Broiler_Documents_Have_Xml_Documentation()
    {
        string baseDir = AppContext.BaseDirectory;
        string docXmlPath = Path.Combine(baseDir, "Broiler.Documents.xml");
        Assert.True(File.Exists(docXmlPath), $"XML doc file not found at {docXmlPath}");

        var docXml = XDocument.Load(docXmlPath);
        var memberDocNames = docXml.Descendants("member")
            .Select(m => m.Attribute("name")?.Value)
            .Where(n => n is not null)
            .ToHashSet();

        var missing = new System.Collections.Generic.List<string>();

        foreach (var type in typeof(DocumentCodec).Assembly.GetExportedTypes())
        {
            string typeDocName = "T:" + type.FullName;
            if (!memberDocNames.Contains(typeDocName))
            {
                missing.Add(typeDocName);
            }

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (member is MethodInfo method && method.IsSpecialName && (method.Name.StartsWith("get_") || method.Name.StartsWith("set_")))
                    continue;

                if (member is FieldInfo f && f.IsSpecialName)
                    continue;

                if (member is PropertyInfo prop)
                {
                    string propDocName = $"P:{type.FullName}.{prop.Name}";
                    if (!memberDocNames.Contains(propDocName))
                        missing.Add(propDocName);
                }
                else if (member is ConstructorInfo)
                {
                    bool hasCtorDoc = memberDocNames.Any(n => n!.StartsWith($"M:{type.FullName}.#ctor"));
                    if (!hasCtorDoc)
                        missing.Add($"M:{type.FullName}.#ctor");
                }
                else if (member is FieldInfo field)
                {
                    string fieldDocName = $"F:{type.FullName}.{field.Name}";
                    if (!memberDocNames.Contains(fieldDocName))
                        missing.Add(fieldDocName);
                }
                else if (member is MethodInfo meth && !meth.IsSpecialName)
                {
                    bool hasMethodDoc = memberDocNames.Any(n => n!.StartsWith($"M:{type.FullName}.{meth.Name}"));
                    if (!hasMethodDoc)
                        missing.Add($"M:{type.FullName}.{meth.Name}");
                }
            }
        }

        Assert.Empty(missing);
    }

    private static PropertyInfo AssertProperty(Type type, string propertyName, Type propertyType)
    {
        PropertyInfo? prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.NotNull(prop);
        Assert.Equal(propertyType, prop.PropertyType);
        return prop;
    }
}
