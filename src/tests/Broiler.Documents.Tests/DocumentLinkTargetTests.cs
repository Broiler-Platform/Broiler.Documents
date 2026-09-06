namespace Broiler.Documents.Tests;

/// <summary>
/// The one link allow-list, which used to be five and disagreed.
/// </summary>
public sealed class DocumentLinkTargetTests
{
    [Theory(Timeout = 600000)]
    [InlineData("https://example.test/")]
    [InlineData("http://example.test/path?q=1&r=2#frag")]
    [InlineData("mailto:someone@example.test")]
    [InlineData("HTTPS://EXAMPLE.TEST/")]
    [InlineData("MailTo:someone@example.test")]
    public void Absolute_Http_Https_And_Mailto_Are_Admitted(string target) =>
        Assert.True(DocumentLinkTarget.IsAllowed(target));

    [Theory(Timeout = 600000)]
    // Another scheme: a link is inert metadata here, and writing one would hand
    // an active target to whatever opens the document.
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox")]
    [InlineData("data:text/html,<script>x</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.test/x")]
    public void Another_Scheme_Is_Refused(string target) =>
        Assert.False(DocumentLinkTarget.IsAllowed(target));

    [Theory(Timeout = 600000)]
    // Nothing to resolve against: a document is not a page, it has no base, and
    // nothing here fetches.
    [InlineData("/docs/page")]
    [InlineData("page.html")]
    [InlineData("../up/one")]
    [InlineData("//example.test/protocol-relative")]
    public void A_Relative_Target_Is_Refused(string target) =>
        Assert.False(DocumentLinkTarget.IsAllowed(target));

    [Theory(Timeout = 600000)]
    // A same-document reference is admitted everywhere, which is what DOCX and
    // ODT always did and what the other three now do too. What it points at is
    // not carried - no codec here reads or writes a bookmark and the model has
    // nowhere to put one - so this preserves a reference the source made rather
    // than a jump that works. Dropping it would discard something the document
    // said.
    [InlineData("#chapter")]
    [InlineData("#a")]
    public void A_Non_Empty_Fragment_Is_Admitted(string target) =>
        Assert.True(DocumentLinkTarget.IsAllowed(target));

    [Fact(Timeout = 600000)]
    public void A_Bare_Hash_Is_Refused()
    {
        // It names no destination, so there is nothing for a round trip to
        // preserve. Every writer already refused it; one reader predicate said
        // yes, which is the kind of disagreement one copy cannot have.
        Assert.False(DocumentLinkTarget.IsAllowed("#"));
    }

    [Theory(Timeout = 600000)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri at all")]
    public void Nothing_And_Nonsense_Are_Refused(string? target) =>
        Assert.False(DocumentLinkTarget.IsAllowed(target));

    [Theory(Timeout = 600000)]
    // Correctness rather than policy: the package formats put the target in an
    // XML attribute, XML 1.0 cannot represent a control character, and handing
    // one over threw - so the tool reported an internal error where a diagnostic
    // belonged.
    [InlineData("https://example.test/a\u0007b")]
    [InlineData("https://example.test/a\nb")]
    [InlineData("https://example.test/a\tb")]
    [InlineData("https://example.test/a\u001Fb")]
    public void A_Control_Character_In_The_Target_Is_Refused(string target) =>
        Assert.False(DocumentLinkTarget.IsAllowed(target));

    [Fact(Timeout = 600000)]
    public void Length_Is_Not_Capped()
    {
        // PdfUriPolicy caps its own, because a PDF annotation is a different
        // risk surface. Here no defect argues for a cap and one would silently
        // refuse links that work today, so the absence is deliberate and this
        // records it.
        Assert.True(DocumentLinkTarget.IsAllowed("https://example.test/" + new string('a', 8000)));
    }

    [Theory(Timeout = 600000)]
    // Admitting a fragment without constraining it put the silent loss back.
    // RTF spells one as a quoted field argument and has no escape for a quote
    // inside one, so a quote in the name truncated the target there and still
    // reported Success. Whitespace is HTML's own rule for the id a fragment
    // resolves against; a second '#' is not a fragment character.
    [InlineData("#a\"b")]
    [InlineData("#a b")]
    [InlineData("#a\u00A0b")]
    [InlineData("#a#b")]
    [InlineData("# ")]
    public void A_Fragment_That_No_Format_Can_Quote_Is_Refused(string target) =>
        Assert.False(DocumentLinkTarget.IsAllowed(target));
}
