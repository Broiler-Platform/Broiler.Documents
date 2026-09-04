using System;
using System.Linq;
using System.Reflection;

namespace Broiler.Documents.Tests;

/// <summary>
/// ADR 0014's rules about announcing a public member for removal, checked
/// against the members that carry the announcement.
/// </summary>
/// <remarks>
/// A policy nothing enforces is a document. These are the parts of ADR 0014 that
/// can be checked mechanically: that an announcement names a replacement, and
/// that it warns rather than breaks.
/// </remarks>
public sealed class ApiDeprecationTests
{
    private static ObsoleteAttribute[] Announcements =>
        typeof(DocumentReadOptions).Assembly
            .GetExportedTypes()
            .SelectMany(static type => type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(static member => member.GetCustomAttribute<ObsoleteAttribute>())
            .Where(static attribute => attribute is not null)
            .Select(static attribute => attribute!)
            .ToArray();

    [Fact]
    public void An_Announcement_Never_Breaks_The_Callers_Build()
    {
        // ADR 0014: error: true at the announcement step is a removal wearing a
        // warning's clothes. The two-step rule exists so an upgrade warns before
        // it breaks.
        foreach (ObsoleteAttribute announcement in Announcements)
            Assert.False(announcement.IsError, "An announced member must warn, not fail the build.");
    }

    [Fact]
    public void An_Announcement_Says_What_To_Use_Instead()
    {
        // The message carries the migration rather than pointing at it: a caller
        // reading a build warning should not have to find a document.
        foreach (ObsoleteAttribute announcement in Announcements)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(announcement.Message),
                "An announced member states its replacement in the attribute.");
            Assert.True(
                announcement.Message!.Length > 40,
                "An announcement long enough to be useful: " + announcement.Message);
        }
    }

    [Fact]
    public void DecodeEmbeddedObjects_Is_Announced_And_Names_Its_Replacement()
    {
        ObsoleteAttribute? announcement = typeof(DocumentReadOptions)
            .GetProperty(nameof(DocumentReadOptions.DecodeEmbeddedObjects))!
            .GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(announcement);
        Assert.Contains("ResourcePolicy", announcement!.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void AsciiOnly_Is_Not_Announced()
    {
        // ADR 0014's no-replacement rule. AsciiOnly is narrow rather than
        // superseded — the RTF writer implements it and nothing else consults it
        // — so announcing it would tell callers to stop using it while offering
        // nothing to use instead. Whether it should exist is a separate question.
        Assert.Null(
            typeof(DocumentWriteOptions)
                .GetProperty(nameof(DocumentWriteOptions.AsciiOnly))!
                .GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void An_Announced_Member_Still_Works()
    {
        // The whole point of the two-step rule. A caller who has not migrated
        // yet gets a warning and a working build, not a broken one.
#pragma warning disable CS0618
        Assert.True(new DocumentReadOptions(decodeEmbeddedObjects: true).DecodeEmbeddedObjects);
        Assert.False(new DocumentReadOptions(decodeEmbeddedObjects: false).DecodeEmbeddedObjects);
#pragma warning restore CS0618
    }
}
