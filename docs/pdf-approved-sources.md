# PDF Approved Sources And Similarity Record

**Version:** 0.2  
**Updated:** 2026-08-25

Only sources with an `Approved` decision may inform implementation. A source may
be approved for background but not for copying code, prose, tables, fixtures, or
generated data. Contributors add a row before using a new implementation source.

## Source register

| ID | Source | Permitted use | Decision | Reviewer / evidence |
|---|---|---|---|---|
| SRC-001 | Lawfully obtained ISO 32000 editions | Requirements research limited to licensed access terms | Pending | Qualified source/copyright review required |
| SRC-002 | Adobe ISO 32000-1 Public Patent License | Identify license text and conditions; no implementation content | Approved for issue spotting only | Public primary document; legal interpretation pending in IP-001 |
| SRC-003 | ISO patent-policy page and declaration database | Locate declarations; never infer clearance | Approved for issue spotting only | Public primary source; IP-002 |
| SRC-004 | ITU-T T.81 recommendation/declaration pages | Locate exact JPEG recommendation and declarations | Approved for issue spotting only | Public primary source; IP-005 |
| SRC-005 | Adobe XMP Toolkit SDK repository | Identify published SDK license and separate spec-license link | Approved for issue spotting only | Public primary repository; no code may be copied without a new decision |
| SRC-006 | Microsoft OpenType specification page | Identify selected version and normative source | Pending implementation use | Copyright/license and exact-table scope review required |
| SRC-007 | Existing Broiler shared-component source | Reuse through normal repository contribution history | Approved | Project-owned source; PDF-specific historical artifacts remain excluded |
| SRC-008 | Old Broiler PDF source, decompiled binaries, tests, fixtures, outputs | None | Rejected | ADR 0011 and IP-019 |
| SRC-009 | Third-party PDF libraries and their tests | None unless individually registered | Rejected by default | Prevent accidental code/test-vector similarity |
| SRC-010 | Broiler-authored PDF character and metric data (IP-021, IP-022) | Implementation use | Approved | Authored in this repository from character identities and letterform proportions; no third-party glyph list, encoding table, or metric file was transcribed |
| SRC-011 | The .NET runtime's compression, cryptography, and globalization APIs | Implementation use | Approved | Platform APIs consumed as APIs; no algorithm, table, or test vector is copied into this repository |

## Contributor provenance declaration

Each PDF-related change records:

- source IDs consulted and the permitted use for each;
- files, tables, fixtures, or generated data added;
- whether any code, prose, constants, or test vectors were adapted;
- the license and notice treatment for adapted material;
- a similarity review against unapproved old Broiler PDF and third-party code;
- reviewer name, decision, and date.

Suggested pull-request statement:

> I used only the approved sources listed as: [IDs]. I did not copy or adapt
> unapproved legacy or third-party PDF code, tables, tests, fixtures, or output
> files. Added generated material is reproducible from the sources recorded in
> this change.

## Similarity review log

| Change | Source IDs | Reviewer | Date | Result |
|---|---|---|---|---|
| Phase 0 governance documents and legacy CLI removal | SRC-002–SRC-005, SRC-007 | Engineering review completed | 2026-08-22 | No PDF implementation code or fixtures introduced; legacy process/test surface removed |
| Base PDF codec implementation (`Broiler.Documents.Pdf`) | SRC-007, SRC-010, SRC-011 | Engineering review completed | 2026-08-25 | No code, table, fixture, or test vector adapted from the retired PdfSharp/PdfPig lineages or from any third-party PDF implementation. Syntax and structure written from the clause structure of ISO 32000-1 without reproducing its text. Encoding and metric data authored under SRC-010. All test fixtures generated in code; no `.pdf` committed. |
