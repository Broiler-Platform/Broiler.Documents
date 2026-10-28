# ODS IP, Licensing, And Standards Register

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

ODS is the OASIS OpenDocument Spreadsheet format (ISO/IEC 26300).
The codec reads and writes workbook sheets into and from the rich-text table model
and the `SpreadsheetWorkbook` model.

## Decision fields

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| ODS-IP-001 | OASIS OpenDocument spreadsheet specification (ODF 1.0 - 1.3 / ISO/IEC 26300) | OASIS ODF specification; Sun / Oracle ODF patent covenant | Covered under standard committee licensing and public covenants. Risk assessed **low**. | **Pending.** |
| ODS-IP-002 | Implementation provenance | Inspection of `src/Broiler.Documents.Ods` | The codec directory holds **nothing but `.cs` files and its `.csproj`**, takes **no third-party package reference**, and references only `Broiler.Documents` and `Broiler.Documents.Model`. Written against published specification. | **Pending.** |
| ODS-IP-003 | Naming and claims | The proposed label set below | "ODS" is descriptive of the OpenDocument spreadsheet format. Nothing describes this codec as certified or conforming. | **Pending** for the label set below. |

## Approved labels

**Proposed, not approved.** These are the labels the codec uses and the only ones
anything may use until the reviewer decides.

| Context | Approved label |
|---|---|
| Format list | **ODS** |
| Save As | **OpenDocument Spreadsheet (*.ods)** |
| Import / Export | **ODS** |
| Tooltip / Help | **OASIS OpenDocument Spreadsheet** |
