# IP-012 Re-opening Brief: Embedding A Caller's Font Into Output

**Status:** Open question, written up for decision. Nothing here is a decision,
and nothing here is legal advice.
**Who may decide it.** The project reviewer, under the register's evidence-based
standard — but see §3, which argues this row is a different *kind* of question
from every other one in the register and may want a different standard applied
to it.
**Prepared:** 2026-09-03
**Re-opens:** [IP-012](pdf-ip-licensing-register.md), which is approved for
inspection and says in terms that embedding, subsetting, and redistribution are
outside it and that **the row must be re-opened before that work starts**. This
is that re-opening, requested rather than performed.

## 1. The question, exactly

May this project build and ship a mechanism that embeds a **caller-supplied**
font program into a PDF it writes, and subsets it?

Note what the question is not. It is not "may this project embed font X", because
the project will never see font X: the fonts belong to callers, arrive through
`DocumentFontSet`, and differ per document. §11.3's path B settled that on
2026-09-02 — this project bundles no font and holds no font licence.

## 2. Why it cannot be avoided

The writer emits WinAnsi text and nothing else. Every character outside that
repertoire — all Cyrillic, all Greek, all CJK, most punctuation a European
document uses — is replaced and reported. The only mechanism PDF offers for the
rest is a Type 0 font with an embedded program and a `ToUnicode` map
([roadmap §12.1](pdf-support-roadmap.md)).

So this row is what stands between the writer and every non-Latin script. It is
not one feature among several: it is the feature, and `CanWrite` cannot honestly
become true without it.

## 3. Why this row is not like the others

Every other row in the register clears a **technology or a document**, and shares
a shape: there is one rights-holder, their terms are published, an engineer reads
them, and the risk is stated in plain words. ISO 32000-1 has Adobe's patent
licence. T.4 has the ITU's notice. The CFF standard strings have Adobe's.

**This row has no rights-holder to read.** The rights in an embedded font belong
to whoever made that font — a different party for every document, unknown to this
project at the moment of clearing, and quite possibly unknown to the caller. There
is no term sheet to inspect and no evidence to gather about it, because the object
of the clearance does not exist yet and never will in the singular.

That changes what a decision can even be about. It cannot be *"embedding is
permitted"*, which is not this project's to say. What it can be about is:

> **May this project ship a mechanism that redistributes a third party's property
> on a caller's instruction, and what must that mechanism do for shipping it to be
> defensible?**

The register's standard — "whether a question is answerable from evidence a reader
can check" — points somewhere specific here. The evidence about *any given font*
is unavailable in principle. The evidence about *the mechanism* is entirely
available: it is in this repository, it is tested, and §4 sets it out.

## 4. What already exists, and what it refuses

The row's own note records why: the machinery was built first, deliberately, so
that re-opening would be a decision about permission rather than one taken under
time pressure with code still to write. It is worth the reviewer knowing exactly
how much is already standing.

`DocumentFontEmbedding.MayEmbed` is a fail-closed preflight requiring **two
independent permissions**, and refusing if either is absent:

| Gate | Refuses when |
|---|---|
| The caller's own disposition, recorded per font in the conversion context | The caller has not granted `EmbedOrSubset` — and `Transform` as well, when subsetting |
| `fsType` — `Unknown` | The font declares no embedding permission. *"An unreadable declaration is not a permissive one"* |
| `fsType` — `Restricted` | The font declares restricted-licence embedding |
| No-subsetting bit | The font permits embedding but forbids subsetting, and subsetting was asked for |
| Bitmap-only bit | This writer emits no bitmap font program, so the condition can never be satisfied |

Two properties of that design matter to the decision.

**It never treats `fsType` as the licence.** §11.3 states the rule and the code
implements it: `fsType` is a technical signal and an enforcement input, not a
substitute for the font's EULA. A permissive `fsType` alone gets a font nowhere;
the caller must also have said yes.

**It fails closed on silence.** An unreadable or absent declaration refuses. That
is the opposite of the common industry default, and it is deliberate.

What none of it does is embed anything. There is no code path in this repository
that writes a font program into a PDF, and there will not be one until this row
moves.

## 5. What a decision would still have to weigh

Offered as questions, not answers, and the brief stops short of joining them up.

- **Whose act is the embedding?** The project ships a tool; the caller supplies a
  font and instructs it. A word processor is not usually thought to infringe when
  a user embeds their own font. But this project also *distributes the tool*, and
  the tool's purpose includes doing that — which is a different posture from a
  general-purpose editor.
- **Subsetting is modification, not just copying.** A subset is a derivative of
  the font program. Some licences address it explicitly, some forbid it, some are
  silent. The no-subsetting bit is honoured, but the bit is a signal and not the
  licence, and a silent licence is not a permissive one.
- **Obligations that attach to the generated document.** Some font licences —
  open ones included — attach conditions to documents that contain the font:
  attribution, licence inclusion, naming restrictions. §11.3 already requires the
  writer to fulfil such an obligation or reject the resource. Whether that is
  sufficient, and what the writer must emit to satisfy it, is part of this
  decision rather than a detail after it.
- **What the caller's "yes" is worth.** The conversion context records a caller
  disposition. A decision should say what that disposition is understood to
  assert — that the caller holds the right, or merely that they claim to — and
  where the responsibility sits when it is wrong.
- **Jurisdictions.** The register's standing unrecorded item, and it bites harder
  here than on a row about a specification, because font licensing practice and
  the treatment of subsets vary.

## 6. What must not be assumed

- **That a permissive `fsType` is permission.** It is a flag in a table the font
  vendor set; the licence governs. Both §11.3 and the preflight already refuse
  this inference, and a decision must not quietly reintroduce it.
- **That the caller having a font means the caller may embed it.** Possession is
  not a licence — the same rule IP-020 states for fixtures.
- **That a font read out of an input document may be re-embedded on export.**
  §11.3 forbids exactly this: an import-to-export conversion resolves a new,
  caller-supplied font resource. The inspection this row already approves must
  not become an export authority by the back door.
- **That refusing is free.** See §7, disposition D. Refusal has a product cost
  and it should be named, not discovered later.

## 7. Dispositions available, and what each costs

Set out neutrally. The reviewer may reach a different one.

**A. Widen the row to caller-directed embedding, on the existing preflight.**
IP-012 becomes inspection plus embedding-and-subsetting of caller-supplied fonts,
conditioned on both permissions the preflight already requires, with the
generated-document obligation named and its handling specified. Unblocks the
writer's non-Latin path. Costs: the row stays live rather than retiring, and the
obligations travel with every release.

**B. Widen it for embedding but not subsetting.**
Whole font programs only. Removes the derivative-work question entirely and keeps
the simpler act. Costs: output carries a full font per family, which for CJK is
megabytes per document, and §12.1's subset requirement would need rewriting.

**C. Widen it only for fonts whose `fsType` is `Installable`.**
The narrowest technical reading — embed only what the vendor flagged as
unrestricted. Costs: refuses many commercial fonts a caller has legitimately
licensed, and it elevates `fsType` to something close to the licence, which §11.3
says it is not. Cheap to implement because the preflight already computes it.

**D. Decline, and record it as decided rather than pending.**
The writer stays WinAnsi-only. PDF export never supports Cyrillic, Greek, or CJK,
and the feature matrix says so permanently rather than provisionally. This is a
**product** decision as much as a legal one, and it should be taken as one: it is
not a holding position, and calling it one would leave the write side blocked
indefinitely by a question nobody had actually answered.

## 8. What a decision should record

Per the register's own decision fields, and the two this row has left unrecorded
since it first cleared:

- the exact scope — embedding, subsetting, or both; which font formats; whether
  CFF and CFF2 output are included or deferred;
- what the caller's recorded disposition is taken to assert, and where
  responsibility sits when it is mistaken;
- how a generated-document obligation is discharged, and what the writer must do
  when it cannot be;
- whether `fsType` remains an enforcement input alongside the licence, or is
  elevated in any disposition that relies on it;
- **implementation jurisdictions**, which this row has never recorded and which
  matter more here than on a specification row;
- the reviewer, the decision date, and an expiry/review date — this row should
  not retire, because unlike IP-009 and IP-010 it rests on no expiry and on no
  single grant;
- and whether the decision changes `CanWrite`, which it does not on its own:
  [roadmap §12.1](pdf-support-roadmap.md)'s writer-core gate and the Phase 7 exit
  criteria are engineering and stand regardless.

## 9. What this brief does not claim

It does not claim the mechanism is lawful, that the preflight is sufficient, or
that building the machinery before the decision creates any entitlement to a
particular answer. The machinery was built to make the decision unhurried, and
disposition D remains fully available: nothing in this repository embeds a font
today, and deleting the path that would is a smaller change than the one that
built the preflight.
