using System;
using System.Collections.Generic;

namespace Broiler.Documents.Tests;

/// <summary>
/// The format-neutral metadata envelope: the distinctions it exists to keep, and
/// the transfer rule that keeps a read from authorizing a write.
/// </summary>
public sealed class DocumentMetadataTests
{
    [Fact]
    public void Missing_And_Explicitly_Empty_Stay_Distinct()
    {
        // The difference a writer needs to choose between omitting a key and
        // emitting an empty one. Collapsing them loses a statement an author made
        // on purpose, and no later code can recover which it was.
        var saidNothing = new DocumentMetadata();
        var saidEmpty = new DocumentMetadata(title: string.Empty);

        Assert.Null(saidNothing.Title);
        Assert.Equal(string.Empty, saidEmpty.Title);
        Assert.NotEqual(saidNothing.Title, saidEmpty.Title);
    }

    [Fact]
    public void An_Envelope_That_States_Nothing_Is_Empty()
    {
        Assert.True(DocumentMetadata.Empty.IsEmpty);
        Assert.True(new DocumentMetadata().IsEmpty);

        // An explicitly empty title is a statement, so it is not nothing.
        Assert.False(new DocumentMetadata(title: string.Empty).IsEmpty);
    }

    [Fact]
    public void Authors_And_Keywords_Keep_Source_Order()
    {
        var metadata = new DocumentMetadata(
            authors: ["Ada", "Grace", "Barbara"],
            keywords: ["z", "a", "m"]);

        Assert.Equal(["Ada", "Grace", "Barbara"], metadata.Authors);
        Assert.Equal(["z", "a", "m"], metadata.Keywords);
    }

    [Fact]
    public void With_Replaces_Only_What_It_Is_Given()
    {
        // The caller's override step: take what a read produced, correct the
        // fields that should not survive, leave the rest.
        var read = new DocumentMetadata(
            title: "Draft",
            authors: ["Ada"],
            producer: "SomeoneElse 4.2");

        DocumentMetadata write = read.With(producer: "Broiler");

        Assert.Equal("Broiler", write.Producer);
        Assert.Equal("Draft", write.Title);
        Assert.Equal(["Ada"], write.Authors);
    }

    [Fact]
    public void A_Read_Result_Does_Not_Supply_A_Write()
    {
        // The transfer policy is the absence of an automatic path: nothing in the
        // component copies one to the other. Having read what a document says
        // about itself is not authority to republish it under someone else's name.
        var read = new DocumentReadResult(
            Model.RichTextDocument.FromPlainText("body"),
            metadata: new DocumentMetadata(title: "Someone's report", authors: ["Ada"]));

        Assert.Equal("Someone's report", read.Metadata.Title);
        Assert.True(DocumentWriteOptions.Default.Metadata.IsEmpty);

        // A caller who wants the transfer performs it, which is this line.
        var options = new DocumentWriteOptions(metadata: read.Metadata);
        Assert.Equal("Someone's report", options.Metadata.Title);
    }

    [Fact]
    public void A_Result_And_Options_Given_No_Envelope_Carry_The_Empty_One()
    {
        var read = new DocumentReadResult(Model.RichTextDocument.FromPlainText("body"));

        Assert.Same(DocumentMetadata.Empty, read.Metadata);
        Assert.Same(DocumentMetadata.Empty, DocumentWriteOptions.Default.Metadata);
    }
}

/// <summary>
/// Reading and writing W3C-DTF timestamps without inventing a zone the source
/// never stated.
/// </summary>
public sealed class DocumentTimestampTests
{
    [Theory]
    [InlineData("2026-09-04T08:30:00Z")]
    [InlineData("2026-09-04T08:30:00+02:00")]
    [InlineData("2026-09-04T08:30:00-05:00")]
    public void A_Stated_Zone_Is_Recorded_As_One(string value)
    {
        Assert.True(DocumentTimestamp.TryParse(value, out DocumentDate date));
        Assert.True(date.HasUtcOffset);
    }

    [Theory]
    [InlineData("2026-09-04T08:30:00")]
    [InlineData("2026-09-04")]
    public void A_Zone_Less_Value_Stays_Zone_Less(string value)
    {
        // The rule the type exists for. Parsing this into the converting
        // machine's zone would attribute that machine's location to the document.
        Assert.True(DocumentTimestamp.TryParse(value, out DocumentDate date));
        Assert.False(date.HasUtcOffset);
    }

    [Theory]
    [InlineData("2026-09-04T08:30:00Z")]
    [InlineData("2026-09-04T08:30:00+02:00")]
    [InlineData("2026-09-04T08:30:00")]
    public void A_Timestamp_Round_Trips_In_The_Form_It_Arrived_In(string value)
    {
        Assert.True(DocumentTimestamp.TryParse(value, out DocumentDate date));

        Assert.Equal(value, DocumentTimestamp.ToW3cdtf(date));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("last Tuesday")]
    [InlineData("2026-13-45T99:99:99")]
    public void What_Cannot_Be_Read_Is_Refused_Rather_Than_Guessed_At(string? value)
    {
        Assert.False(DocumentTimestamp.TryParse(value, out DocumentDate date));
        Assert.Equal(default, date);
    }
}

/// <summary>
/// What a writer says it did with the metadata it was given.
/// </summary>
public sealed class DocumentMetadataReportTests
{
    private static List<DocumentDiagnostic> Describe(
        DocumentMetadata metadata,
        params string[] unsupported)
    {
        var diagnostics = new List<DocumentDiagnostic>();
        DocumentMetadataReport.Describe(metadata, unsupported, diagnostics);
        return diagnostics;
    }

    [Fact]
    public void An_Empty_Envelope_Reports_Nothing()
    {
        Assert.Empty(Describe(DocumentMetadata.Empty));
    }

    [Fact]
    public void What_Reached_The_Output_Is_Named()
    {
        List<DocumentDiagnostic> diagnostics =
            Describe(new DocumentMetadata(title: "T", authors: ["Ada"]));

        DocumentDiagnostic emitted = Assert.Single(diagnostics);
        Assert.Equal(DocumentDiagnosticCodes.MetadataEmitted, emitted.Code);
        Assert.Equal(DocumentDiagnosticSeverity.Info, emitted.Severity);
        Assert.Contains("Title", emitted.Message, StringComparison.Ordinal);
        Assert.Contains("Authors", emitted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void What_The_Format_Could_Not_State_Is_Named_Separately()
    {
        List<DocumentDiagnostic> diagnostics = Describe(
            new DocumentMetadata(title: "T", creatorApplication: "Some Editor"),
            "CreatorApplication");

        Assert.Equal(2, diagnostics.Count);
        DocumentDiagnostic dropped = Assert.Single(
            diagnostics,
            d => d.Code == DocumentDiagnosticCodes.MetadataDropped);
        Assert.Equal(DocumentDiagnosticSeverity.Warning, dropped.Severity);
        Assert.Contains("CreatorApplication", dropped.Message, StringComparison.Ordinal);

        // And the field that did reach the output is not also reported as lost.
        DocumentDiagnostic emitted = Assert.Single(
            diagnostics,
            d => d.Code == DocumentDiagnosticCodes.MetadataEmitted);
        Assert.DoesNotContain("CreatorApplication", emitted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Field_Nobody_Set_Is_Not_Reported_As_Stripped()
    {
        // A format that cannot express a field the caller never set has stripped
        // nothing, and saying otherwise would train a reader to ignore the code.
        List<DocumentDiagnostic> diagnostics =
            Describe(new DocumentMetadata(title: "T"), "CreatorApplication");

        Assert.Single(diagnostics);
        Assert.Equal(DocumentDiagnosticCodes.MetadataEmitted, diagnostics[0].Code);
    }

    [Fact]
    public void A_Value_Never_Appears_In_A_Diagnostic()
    {
        // Titles and authors carry names and case numbers. Diagnostics travel to
        // logs and manifests, so they name fields and never quote them.
        List<DocumentDiagnostic> diagnostics = Describe(
            new DocumentMetadata(title: "Patient A, biopsy result", authors: ["Dr Grace Hopper"]),
            "CreatorApplication");

        foreach (DocumentDiagnostic diagnostic in diagnostics)
        {
            Assert.DoesNotContain("Patient", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Hopper", diagnostic.Message, StringComparison.Ordinal);
        }
    }
}
