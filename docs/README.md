# Broiler.Documents Documentation

The documentation is split by **kind**, not by format: one roadmap for
everything still open, one set of notes describing the code as it is, and one
place for rights and provenance.

| Where | Kind | Contents |
|---|---|---|
| [roadmap.md](roadmap.md) | Open work | Everything not done yet, across all formats, including the PDF delivery gates and the [shared layout foundations plan](roadmap.md#shared-layout-foundations) with Broiler.Layout and Graphics |
| [notes/](notes/) | The code as it is | [architecture.md](notes/architecture.md) for shared contracts and guards; one note per codec: [RTF](notes/rtf.md), [DOCX](notes/docx.md), [ODT](notes/odt.md), [HTML](notes/html.md), [Markdown](notes/markdown.md), [CSV](notes/csv.md), [XLSX](notes/xlsx.md), [ODS](notes/ods.md); and [notes/pdf/](notes/pdf/README.md) with the PDF overview, [feature matrix](notes/pdf/feature-matrix.md), [construct inventory](notes/pdf/construct-inventory.md) and [extension points](notes/pdf/extension-points.md) |
| [ip/](ip/README.md) | Rights and provenance | The standard of review, one register per format, PDF approved sources, the PDF corpus manifest, and review briefs |
| [guides/](guides/) | Using and testing | [CLI](guides/cli.md), [packaging and releases](guides/packaging.md), [corpus suite](guides/corpus-suite.md), [office conformance suite](guides/office-conformance.md) |
| [adr/](adr/README.md) | Decisions | Why the component is shaped the way it is |
| [GRAMMAR.md](../src/Broiler.Documents.FormatCodes/GRAMMAR.md) | Specification | Formatting Codes grammar, version 1 |

## Keeping it that way

- **Open work goes in the roadmap, and only there.** When an item is done, delete
  it and describe the result in the matching note.
- **A behaviour change updates its note in the same commit.** For PDF that
  includes the construct inventory and the feature matrix. Guard tests fail when
  a diagnostic code is not documented.
- **Rights decisions go in the format's register**, never restated in a note.
  A note links to its register instead.
- **A decision with real alternatives gets an ADR.**
- **Notes describe the present.** "Used to", "was fixed on" and "found while"
  belong in commit messages and pull requests, which keep that history already.

## Older section references

Code comments, ADRs and register review records cite sections of the former
`pdf-support-roadmap.md`, such as "roadmap §6.1" or "§14.1". That document was
split into open work and notes. Its sections now live here:

| Old section | Now |
|---|---|
| §1 End state, §1.1 V1 scope, §1.2 Semantic and safety boundaries | [notes/pdf § Scope](notes/pdf/README.md#scope) |
| §2 Historical reset | Done. The ban on reusing the retired PDF lineages is IP-019 and SRC-008 in [ip/](ip/README.md) |
| §2.5 Current implementation state | [notes/pdf § What is implemented](notes/pdf/README.md#what-is-implemented) |
| §3 Component ownership | [notes/architecture § Ownership](notes/architecture.md#ownership) |
| §4 Phase summary, §4.1 Delivery milestones | [roadmap § PDF phase status](roadmap.md#pdf-phase-status), [§ PDF delivery gates](roadmap.md#pdf-delivery-gates) |
| §5 Phase 0: scope, standards, IP | Rules in [ip/README.md](ip/README.md#rules-every-format-follows); open approvals in [roadmap § Rights and provenance](roadmap.md#rights-and-provenance) |
| §6.1 Requests, options, input, results | [notes/architecture § Codec contract](notes/architecture.md#codec-contract) |
| §6.2 Resources and metadata | [notes/architecture § Resources and metadata](notes/architecture.md#resources-and-metadata) |
| §6.3 Limits and URI policy | [notes/architecture § Limits and link policy](notes/architecture.md#limits-and-link-policy) |
| §6.4 Model review | [notes/architecture § Document model rules](notes/architecture.md#document-model-rules) |
| §6.5 Graphics and Media prerequisites | [notes/architecture § Graphics and Media prerequisites](notes/architecture.md#graphics-and-media-prerequisites) |
| §6.6 Test, corpus, licence and CI foundation | [roadmap § Test, corpus and oracle foundation](roadmap.md#test-corpus-and-oracle-foundation-phase-1-remainder) |
| §7 Syntax (Phase 2), §8 Structure, filters, security (Phase 3) | [notes/pdf § What is implemented](notes/pdf/README.md#what-is-implemented) and [§ Design rules](notes/pdf/README.md#design-rules-the-code-follows) |
| §9 Logical import (Phase 4), §9.3 Images and links | [notes/pdf § Images and links](notes/pdf/README.md#images-and-links); gaps in [roadmap § Import gaps](roadmap.md#import-gaps-phase-4-remainder) |
| §10 Read preview (Phase 5), §10.1, §10.2 | [roadmap § Read preview](roadmap.md#read-preview-phase-5) |
| §11 Export foundation (Phase 6), §11.3 Font policy | [roadmap § Shared export foundation](roadmap.md#shared-export-foundation-phase-6); the font-path decision in [ip/briefs/font-path.md](ip/briefs/font-path.md) |
| §12 Writer (Phase 7), §12.1 | [roadmap § Writer and output integration](roadmap.md#writer-and-output-integration-phase-7) |
| §13 Hardening and release (Phase 8) | [roadmap § Hardening and stable release](roadmap.md#hardening-and-stable-release-phase-8) |
| §14 Post-V1 tracks: §14.1 encrypted output, §14.2 tagged PDF | [roadmap § Later PDF tracks](roadmap.md#later-pdf-tracks), tracks 1 and 2 |

The former `pdf-phase0-status.md` and `component-review.md` were records of
finished work. Their open items are in the roadmap and their lasting facts in
the notes.
