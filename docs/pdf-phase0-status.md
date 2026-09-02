# PDF Phase 0 Status

**Status date:** 2026-08-25  
**Phase state:** Repository-controlled groundwork complete and the base
implementation slice landed and every construct, filter, and codec register row
is now decided; Phase 0 exit remains blocked on the remaining provenance and
wording rows and on the external legal,
standards-access, jurisdiction, history-audit, and approval items below.

This record separates work the repository can prove from approvals that
engineering cannot self-grant.

## Repository-controlled work

- [x] Define component scope, dependency direction, delivery stages, and exclusions.
- [x] Define request/result/transaction direction and resolve conflicts in old ADRs.
- [x] Define default-deny resources, encryption rejection, active-content, metadata,
  privacy, and non-redaction policy.
- [x] Define units, pagination ownership, scripts/fonts, and platform gates.
- [x] Establish a feature/claim matrix.
- [x] Establish a versioned IP/licensing and standards register.
- [x] Establish approved-source and similarity-review controls.
- [x] Establish an empty, rights-aware corpus manifest and schema; no old fixture is
  presumed reusable.
- [x] Remove all obsolete external-process PDF CLI code and tests.
- [x] Correct documents that still describe the obsolete architecture.
- [x] Add automated guards against reintroducing legacy or misplaced PDF code.
- [x] Run and record the Documents and affected CLI test baselines.

## Base implementation slice

`Broiler.Documents.Pdf` now exists and implements the syntax, structure, filter,
text-import, and writer subset described in
[roadmap §2.5](pdf-support-roadmap.md#25-current-implementation-state). It was
built deliberately inside the Phase 0 constraints rather than after them:

- it carries no third-party runtime dependency and bundles no font, glyph list,
  metric file, ICC profile, or codec asset, so it adds no new licence obligation;
- every technology with a pending register row is detected and skipped with its
  own stable diagnostic instead of being implemented (see
  [PDF extension points](pdf-extension-points.md));
- no fixture is committed — every test PDF is generated in code — so the corpus
  manifest remains empty and no artifact needs a rights decision; and
- the package is `IsPackable=false`, and its registration is confined to the
  composition roots explicitly opened to it — none at Phase 0; since the §10.1
  read-preview candidate, the Windows and Linux Writer heads, for opening only —
  enforced by `PdfDeliveryGuardTests`, so nothing is published or advertised.

Implementation is therefore not a claim. Nothing below becomes checked because
code exists, and no feature-matrix entry may reach `Supported` until its register
row clears.

## Validation record

| Date | Command / check | Result |
|---|---|---|
| 2026-08-22 | `dotnet test Broiler.Documents/Broiler.Documents.slnx -c Release --no-restore` | Passed: 363 tests across seven projects; 0 failed, 0 skipped |
| 2026-08-22 | `dotnet build src/Broiler.Cli.Tests/Broiler.Cli.Tests.csproj -c Release --no-restore` | Passed with 0 errors; existing repository warnings remain |
| 2026-08-22 | Parse both corpus JSON documents and run `git diff --check` | Passed; only Git line-ending notices reported |
| 2026-08-22 | Search active CLI/current architecture documentation for retired process tokens | No active occurrences |
| 2026-08-25 | `dotnet test Broiler.Documents/Broiler.Documents.slnx -c Release` | Passed: 504 tests across nine projects; 0 failed, 0 skipped |
| 2026-08-25 | `dotnet build Broiler.Documents/Broiler.Documents.Pdf/Broiler.Documents.Pdf.csproj -c Release` | Passed with 0 errors and 0 warnings under `TreatWarningsAsErrors` |
| 2026-08-25 | Delivery guards: package is unpacked, references only Documents projects, is absent from every `src/` composition root, and no `.pdf` is committed | Passed |
| 2026-08-25 | `.github/workflows/documents-pdf.yml` added; its exact build, test and console-runner commands rehearsed locally | Passed: 508 tests executed across 8 result files, plus 99, 10 and 18 from the Graphics and Media runners |

## External decisions required for Phase 0 exit

- [ ] Name the qualified legal reviewer and target implementation/distribution
  jurisdictions. *Half done 2026-09-02: the **project reviewer** is named —
  Maik Ratzmer (MaiRat), senior software engineer, researcher and architect —
  and every engineering and source-provenance decision in the register is now
  attributed to him rather than to "the project maintainer". He is not a
  lawyer and the register records that. The **qualified legal seat stays
  open**, and with it the jurisdictions, every expiry/review date, SRC-017,
  and IP-012's re-opening.*
- [ ] Approve the first implementation slice's exact ISO 32000 scope and lawful
  standards access.
- [ ] Decide the scope and obligations of the Adobe ISO 32000-1 public patent
  license; investigate relevant third-party declarations/claims.
- [ ] Clear each included filter/codec tuple, including exact JPEG processes,
  entropy modes, component/precision combinations, APP14/`ColorTransform`, and
  any LZW or fax work. *Partly done 2026-09-01, extended 2026-09-02: IP-005
  (baseline JPEG, widened to progressive DCT on 2026-09-02), IP-006
  (APP14/`ColorTransform`), IP-009 (CCITT fax, retired) and IP-010 (LZW, retired)
  are approved, IP-007 clears JPEG 2000 Part 1 though no decoder for it is
  written, and IP-008 clears JBIG2 though only its MMR generic regions decode.
  **Every filter and codec row is decided**; what remains on them is engineering.
  SRC-017 still carries the one transcribed normative table the fax work needed.*
- [ ] Clear selected font formats/tables, Unicode data, URI standards, and any
  generated normative tables. *Partly done 2026-09-01: IP-012 is approved for
  font-program inspection, and IP-004 for the XMP read subset. Unicode data
  (IP-013), the URI standards (IP-014), and font embedding remain open.*
- [ ] Approve source-use, contributor-provenance, conformance wording, trademark,
  and non-endorsement policies.
- [ ] Complete the repository-history redistribution audit; document authority or
  apply the project's approved removal/rewrite policy to restricted material.
- [ ] Approve every corpus artifact's origin, license, redistribution, and privacy
  status before it is committed or used in distributed tests.

No unchecked item above is implied to be approved by this document. Phase 1 may
begin with architecture-only work only if the project explicitly accepts that
implementation and public capability claims remain blocked by the applicable
register rows.
