# CSV IP, Licensing, And Standards Register

**Register version:** 1.0
**Updated:** 2026-10-12 (evidence assembled; no row decided)
**Owner:** Broiler.Documents maintainers
**Approval authority:** Maik Ratzmer (GitHub [MaiRat](https://github.com/MaiRat)), project reviewer
**Governance:** [ADR 0013](../adr/0013-standards-ip-provenance-and-claims-beyond-pdf.md)

---

## ⚠ NO LAWYER HAS REVIEWED ANY OF THIS

The standard of review is an engineer reading published evidence. **No
freedom-to-operate determination has been made** and patent-freedom is not
claimed. The evidence was gathered by Claude (Anthropic's coding agent,
engineering seat) on 2026-10-12 at the maintainer's direction; assembling a
record is not approving it, and only the approval authority above can.

**No row is decided.** Every row below is `Pending` until the project reviewer
takes it. Under ADR 0013 the codec may ship while its rows are pending; nothing
may be claimed about it beyond what the note states and the proposed labels
allow.

## The shape of this one

CSV is the closest thing to a format with no owner that this component reads.
Comma-separated data interchange is far older than any document describing it,
and the one document that does, RFC 4180, is an **Informational** RFC: it
records practice already in wide use and registers the `text/csv` media type. It
defines no standard and no one conforms to it by obligation. Much of what real
files do - semicolons, tabs, legacy code pages - is not in it at all.

So the rows here are less about an instrument than about two things this
repository did: the conventions it reads beyond the RFC, and the one table it
carries to do that.

## Decision fields

| ID | Technology / exact scope | Primary evidence | Current assessment | Status / required action |
|---|---|---|---|---|
| CSV-IP-001 | The record, field and quoting grammar of RFC 4180 §2, which the codec reads and writes, as [the note](../notes/csv.md) enumerates | [RFC 4180](https://www.rfc-editor.org/rfc/rfc4180) (Y. Shafranovich, October 2005; IETF stream, Informational; registers `text/csv`; updated by RFC 7111 on fragment identifiers, which this codec does not implement). Its IPR section defers to BCP 78 and BCP 79 and points to the IETF's online IPR repository. Datatracker lists the source draft as `draft-shafranovich-mime-csv` | The RFC presents itself as documenting a format already in common use, not inventing one, and the grammar - a separator, a line break, a quote doubled to escape itself - is the shape of decades of prior practice. **The IPR disclosure search is not recorded.** On 2026-10-12 the datatracker's IPR search page refused automated access, and its API returned zero disclosures for `rfc4180` but also zero for a control query that should have found some, so the result proves nothing either way. Risk assessed **very low** on the format's age and nature; that is an assessment, not a finding of freedom. | **Pending.** Required: search the IETF IPR database for `rfc4180` and `draft-shafranovich-mime-csv` by hand and record the result here; then decide. |
| CSV-IP-002 | Conventions read beyond RFC 4180: semicolon and tab delimiters and their detection; LF and lone-CR line breaks; UTF-8, UTF-16 and UTF-32 byte-order marks; the Windows-1252 fallback; lenient handling of stray quotes; and on write, the optional leading-apostrophe formula escape | The behaviour of spreadsheet programs that write such files; the Unicode standard's byte-order marks; [OWASP's CSV injection guidance](https://owasp.org/www-community/attacks/CSV_Injection) for the formula trigger characters | None of these has a specification or an owner to issue an instrument; they are what files in the wild do. Detection and lenient parsing are this repository's own logic, written for it. The formula escape follows published security guidance on which leading characters spreadsheets execute, and is off by default. Risk assessed **very low**, again on absence rather than evidence, which is weaker in kind than an instrument. | **Pending.** |
| CSV-IP-003 | Implementation provenance | Inspection of `src/Broiler.Documents.Csv` and `src/Broiler.Documents/Text/Windows1252.cs`; `FormatClaimGuardTests`, `CsvArchitectureTests`, `CsvClaimGuardTests` | The codec's directory holds **nothing but `.cs` files and its `.csproj`**, takes **no package reference**, and references only `Broiler.Documents` and `Broiler.Documents.Model`. No CSV library is present to account for. It registers no encoding provider. Test documents are built as strings in code. Each finding is enforced by a guard. | **Pending.** Evidence complete; the decision is the reviewer's. |
| CSV-IP-004 | RFC 4180 as a source | The RFC as published; inspection of the tree | Consulted for the grammar and the media-type registration, and **not reproduced**: no RFC text, ABNF or example is committed, and the note cites sections by number. Whether the IETF Trust's licence for code components reaches an RFC of this date was not looked into, because nothing here depends on it. | **Pending.** Importing RFC text or examples would reopen this row. |
| CSV-IP-005 | Naming, and any conformance claim | The proposed label set below | "CSV" and "comma-separated values" are descriptive terms for a data layout, not the name of anyone's product, and the labels name no spreadsheet program. **Nothing describes this codec as RFC 4180 conforming or compliant**: the reader deliberately accepts input the RFC does not allow, and the writer's output follows the RFC's grammar without any conformance testing behind it. | **Pending** for the label set below and no other wording. |
| CSV-IP-006 | Third-party or user-supplied CSV files used as fixtures | Per-artifact origin, author, licence, and approval | Possession is not permission to redistribute. A public data set is the easiest third-party document to drop into a test directory and the one whose licence is least often read. None is committed; `FormatClaimGuardTests` refuses any `.csv` file in the tree. | **Rejected by default**, per artifact. |
| CSV-SRC-001 | Sources consulted while writing the codec | RFC 4180 §2 and §3; the OWASP page in CSV-IP-002; the RTF codec's existing Windows-1252 table | Written for this repository against the RFC's description. Nothing was copied, and no third-party CSV implementation was consulted for content. | **Pending.** |
| CSV-SRC-002 | The Windows-1252 table: the 32 code points the code page assigns to bytes 0x80-0x9F | The code page's published mapping; `src/Broiler.Documents/Text/Windows1252.cs` | **The one transcribed table this codec rests on.** It is 32 facts about which character a byte stands for, the rest of the code page being identical to Latin-1, and there is no other way to state it. The RTF codec has carried the same table since its first release without a row of its own; it now lives in `Broiler.Documents` and both codecs use it. This row records it for CSV, and the RTF register should cross-reference it rather than restate it. | **Pending.** Decide whether a mapping of this kind needs a source row at all; if it does, add the cross-reference to [the RTF register](rtf.md). |

## Approved labels

**Proposed, not approved.** CSV-IP-005 is pending. These are the labels the codec
uses and the only ones anything may use until the reviewer decides.

| Context | Approved label |
|---|---|
| Format list | **CSV** |
| Save As | **CSV (*.csv)** |
| Import / Export | **CSV** |
| Tooltip / Help | **Comma-separated values** |

Deliberately absent: "RFC 4180" in any claim of conformance or compatibility,
because the reader accepts what the RFC forbids and nobody has tested the
writer against it; and any spreadsheet program's name, in the label or beside
it.

## What still blocks a claim

| Blocker | Kind | State |
|---|---|---|
| CSV-IP-001 | The IETF IPR disclosure search | **Pending**: the search has to be made by hand and recorded |
| CSV-IP-002 | Conventions with no owner | **Pending**: assessed on absence |
| CSV-IP-003, CSV-IP-004, CSV-SRC-001 | What this repository contains and consulted | **Pending**: evidence complete, guarded mechanically |
| CSV-SRC-002 | The Windows-1252 table | **Pending**, with the question of whether such a table needs a row |
| CSV-IP-005 label set | Positive wording | **Pending** |

## Review record

| Review | Reviewer | Date | Scope | Result |
|---|---|---|---|---|
| CSV evidence assembly and register creation | Claude (Anthropic coding agent, engineering seat), at the maintainer's direction — **not legal counsel, and not the approval authority** | 2026-10-12 | RFC 4180 as published and its datatracker entry; inspection of this repository's CSV source, tests and tracked files | **Evidence recorded; no row decided.** The one gap is CSV-IP-001's disclosure search, which the tooling could not complete and which is left open rather than reported as empty. Everything else is either an inspection the guards repeat or a convention with no owner to have said anything. The Windows-1252 table is called out as a row of its own because it is the only transcribed data the codec rests on, and because it turned out to have been shipping in RTF without one. |
