# ADR 0014: API Deprecation And Removal

**Status:** Accepted
**Date:** 2026-09-04

## Context

The roadmap has carried an item for some time saying that removing
`DocumentReadOptions.DecodeEmbeddedObjects` "waits on the repository's
deprecation policy". No such policy exists. It is not in the ADRs, the README, or
`docs/`, and nothing in this component has ever been marked `[Obsolete]`.

So the item was blocked on a document nobody had written, which is a worse state
than being blocked on a decision somebody had declined to take: there was nothing
to disagree with and nothing to point at. This ADR writes the policy so the
blockage becomes a decision again.

The component publishes eight packages. A public member removed without notice
breaks a consumer's build on an upgrade with no warning and no migration text,
and this project has no way of knowing who those consumers are. That is the
constraint the policy has to respect; everything else follows from it.

## Decision

**A public member is retired in two releases, never one.**

1. **Announce.** The member is marked `[Obsolete]` with a message naming its
   replacement and, where the replacement is not a like-for-like swap, what the
   caller has to decide differently. The member keeps working exactly as it did.
   A caller upgrading gets a warning at the call site, in their build, at the
   moment the information is useful.
2. **Remove.** In a later release, and only after the announcement has shipped in
   at least one. Removal is a breaking change and is recorded as one.

**The message carries the migration, not a pointer to it.** `[Obsolete("Use X
instead")]` is the minimum; where the replacement asks a different question, the
message says what that question is. A caller reading a build warning should not
have to find a document to know what to do.

**`error: true` is never used at the announcement step.** A deprecation that
fails the consumer's build is a removal wearing a warning's clothes.

**Internal use of an announced member is suppressed at the call site, never
project-wide.** This component builds with `TreatWarningsAsErrors`, so a single
remaining internal use would otherwise force either a global suppression — which
would hide the warning for every future deprecation too — or an immediate
rewrite of working code. Each suppression carries a comment saying why the call
still exists and what removes it.

**A member with no replacement is not announced.** Deprecating something a caller
cannot migrate away from tells them to stop using it and offers nothing; it is
noise in their build until removal, which is the only part that helps them. Such
a member is either documented as narrow — which is a different problem — or
removed on its own merits under the two-step rule.

**Nothing here applies to the PDF codec's surface while its package is
unpublished.** `Broiler.Documents.Pdf` is `IsPackable=false` and has no consumers
to break, so its API moves freely until the delivery gates in the PDF roadmap
§4.1 open. The moment it is published, it is under this policy like everything
else.

## Consequences

- `DecodeEmbeddedObjects` is announced now and removable in a later release. Its
  replacement, `DocumentReadOptions.ResourcePolicy`, is not a like-for-like swap
  — a boolean asks "may images happen", a policy answers per resource and per
  operation — so the message says so rather than pointing at a type name alone.
- `DocumentWriteOptions.AsciiOnly` is **not** announced, under the
  no-replacement rule. It is narrow rather than superseded: the RTF writer
  implements it and the other codecs do not consult it. Telling callers to stop
  using it while offering nothing to use instead would not help them. Whether it
  should exist at all is a separate question this ADR does not answer.
- The two-step rule costs a release of carrying a member that is already
  redundant. That is the price of not breaking a build somebody else owns, and
  it is cheap next to the alternative.
