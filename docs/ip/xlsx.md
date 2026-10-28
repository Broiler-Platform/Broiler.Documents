# XLSX IP, Licensing, And Standards Register

**Register version:** 1.0
**Updated:** 2026-10-28 (evidence assembled; no row decided)
**Owner:** Broiler.Documents maintainers
**Approval authority:** Maik Ratzmer (GitHub [MaiRat](https://github.com/MaiRat)), project reviewer
**Governance:** [ADR 0013](../adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)

---

## ⚠ NO LAWYER HAS REVIEWED ANY OF THIS

The standard of review is an engineer reading published evidence. **No
freedom-to-operate determination has been made** and patent-freedom is not
claimed.

**No row is decided.** Every row below is `Pending` until the project reviewer
takes it. Under ADR 0013 the codec may ship while its rows are pending; nothing
may be claimed about it beyond what the note states and the proposed labels
allow.

## The shape of this one

XLSX is the Open XML SpreadsheetML format specified in ISO/IEC 29500 and ECMA-376.
The codec reads and writes workbook sheets into and from the rich-text table model
and the `SpreadsheetWorkbook` model.

## Decision fields

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| XLSX-IP-001 | Open XML SpreadsheetML package and worksheet structure (ISO/IEC 29500 / ECMA-376) | ISO/IEC 29500-1; ECMA-376; Microsoft Open Specification Promise | Covered under vendor covenant for Open XML implementations. Risk assessed **low**. | **Pending.** |
| XLSX-IP-002 | Implementation provenance | Inspection of `src/Broiler.Documents.Xlsx` | The codec directory holds **nothing but `.cs` files and its `.csproj`**, takes **no third-party package reference**, and references only `Broiler.Documents` and `Broiler.Documents.Model`. Written against published specification. | **Pending.** |
| XLSX-IP-003 | Naming and claims | The proposed label set below | "XLSX" is descriptive of the Open XML spreadsheet format. Nothing describes this codec as certified or conforming. | **Pending** for the label set below. |

## Approved labels

**Proposed, not approved.** These are the labels the codec uses and the only ones
anything may use until the reviewer decides.

| Context | Approved label |
|---|---|
| Format list | **XLSX** |
| Save As | **Excel Workbook (*.xlsx)** |
| Import / Export | **XLSX** |
| Tooltip / Help | **Open XML SpreadsheetML Workbook** |
