# SRC-017 Review Brief: Reproducing Normative Code Tables

**Status:** Open question, written up for decision. Nothing here is a decision,
and nothing here is legal advice.
**Who may decide it.** Under the register's evidence-based standard this is the
project reviewer's to settle — but it is the row where that standard is most
strained, and the reason it is still open. Every other row was answerable from
evidence a reader can check. This one asks what a rights-holder permits, the
published evidence does not say, and reading the Recommendation does not answer
it. **This is the row on which taking counsel would be worth its cost**, and the
brief exists so that either a lawyer or a well-informed decision has something to
work from.
**Prepared:** 2026-09-02. **Updated:** 2026-09-03, when a second transcribed
table entered the repository and, with it, a second rights-holder — see §4.
**Decides:** [SRC-017](pdf-approved-sources.md), and by reference SRC-018 and
SRC-019. **SRC-016 is adjacent rather than covered**, for the reason §4 sets
out: it asks the same shape of question about a different publisher's document,
and one decision does not answer both.

This brief exists because SRC-017 is the last genuinely open provenance question
in the PDF work, and because it is the only one an engineer cannot narrow further
by inspection. Everything below is fact-gathering and option-setting for whoever
holds the decision.

## 1. The question, exactly

May this repository reproduce, as source-code constants, the Huffman code
assignments defined normatively in ITU-T Recommendations T.4 and T.6?

It is a **copyright question about a specification's text**, not a patent
question about a technology. The patent position is separate, settled, and
recorded: IP-009 is approved and retired on expiry-by-age, and it says nothing
about this.

## 2. What is actually reproduced

All of it is in one file,
[`CcittFaxTables.cs`](../src/Broiler.Documents.Pdf.Images/CcittFaxTables.cs),
180 lines, and it is the **only** such file in the repository.

| Table | Entries | What each entry is |
|---|---:|---|
| White run lengths | 91 | code, bit length, run length |
| Black run lengths | 91 | code, bit length, run length |
| Shared makeup codes | 13 | code, bit length, run length |
| Two-dimensional mode codes | 10 | code, bit length, mode |
| **Total** | **205** | |

Nothing else from either Recommendation is present: no prose, no figures, no
worked examples, no test images, no state diagrams. The decoder itself — the
bit reader, the changing-element representation, the reference-line handling,
the PDF parameter semantics — is this repository's own work, written from the
clause structure, and is recorded as such in the similarity review log.

## 3. Why it could not be authored instead

This is the fact that distinguishes SRC-017 from every other data row in the
register, and the reviewer should test it rather than take it on trust.

Every other table in this codec was **authored from an underlying fact**. The
`WinAnsiEncoding` table is derived from the character each slot denotes and is
independently documented outside ISO 32000-1 as Windows-1252; the font metrics
are a proportion model built from letterform proportions; the Symbol encoding was
written slot by slot from character identity. IP-021 records the inspection that
confirmed this, and a guard test now fails the build if a data file appears
beside the codec.

**A Huffman code assignment has no underlying fact to derive it from.** Which
bit pattern means "a white run of 64" is a choice the standard's authors made.
An implementation either reproduces that choice or does not decode files that
used it. There is no third option, no independent derivation, and no other
publication of these particular assignments that is not itself derived from T.4.

## 4. What turns on the answer

More than fax. Two rows defer to this one outright:

- **SRC-018** (JPEG 2000): "should an entropy decoder ever be written, its
  MQ-coder state table and context assignments are normative constants with no
  authored alternative, and fall under the same open question as SRC-017."
- **SRC-019** (JBIG2): the arithmetic decoder's state and context tables, on the
  same reasoning.

Both are ITU-T Recommendations, so the evidence in §5a reaches them and one
decision can properly govern all three. A decision here therefore governs whether
the two unfinished codecs can be finished at all. IP-007 and IP-008 are both
approved; the engineering is scoped; and this question sits in front of both
regardless of how much of that engineering gets done first.

It also gates publication of the fax path that already exists and works.

### 4a. What changed on 2026-09-03, and why it does not simply add a row

A second transcribed table entered the repository: the **391 CFF standard
strings**, in `CffStandardStrings`, under **SRC-016**. It is the same shape of
problem — an ordered normative list with no authored alternative, where an
implementation either reproduces it or resolves nothing — and the register
initially recorded it as pending "on the same question as SRC-017."

**That wording was too quick, and this brief corrects it.** The question's shape
is shared; its governing terms are not. SRC-017 is about ITU-T Recommendations,
and every fact in §5a is an ITU fact: ITU's reproduction notice, ITU's exception
list, ITU's permissions address. The CFF standard strings come from Adobe's CFF
specification and ISO/IEC 14496-22 — **a different publisher, different terms,
and a different route to permission**. None of the §5a evidence has been checked
against them, and this brief does not assume it transfers.

So the reviewer faces a choice about scope before facing the question itself:

- **Decide SRC-017 alone**, on the ITU evidence gathered, and leave SRC-016 open
  for its own evidence-gathering and its own decision. This brief supports that
  today.
- **Decide a general policy** on reproducing normative tables from any
  specification, which would cover both — but would be decided on evidence
  gathered for one publisher and applied to another, which is the weaker basis
  and this brief flags it as such.

The first is the more defensible and the slower. Nothing forces the choice now:
`CffStandardStrings` is unpublished on the same footing as the fax tables, and
the feature matrix already refuses any CFF-derived support claim while the row is
open.

**What is genuinely shared** is the precedent. Whatever is decided here will be
the reasoning anyone reaches for next time a table cannot be authored from an
underlying fact — and on current evidence that is now happening about once a
month.

## 5. Facts the reviewer may want, stated without conclusions

These are offered as starting points. Each is a fact; none of them is an answer,
and the brief deliberately stops short of joining them together.

- **ITU-T Recommendations T.4 and T.6 are published by the ITU and are
  obtainable.** Obtainability is not a reproduction right, and this brief does
  not assume it is one. The applicable terms are the ITU's own, and identifying
  which terms attach to these editions is part of the decision.
- **The tables are the operative content of the Recommendation, not incidental
  to it.** A reviewer weighing extent will find that 205 assignments are a small
  part of the document and a large part of its normative substance.
- **Other implementations contain these tables** — libtiff, ghostscript,
  leptonica among them. SRC-015 already records the project's position on this
  shape of argument, for JPEG: the existence of open-source implementations is
  evidence that a practice is ordinary, **not permission to copy anyone**. The
  same caution applies here, and the reviewer should treat prevalence as context
  rather than as a defence.
- **No code, table, constant or test vector was taken from any of those
  implementations.** The transcription is from the Recommendation itself. This
  matters because it separates the question from any third party's licence: for
  SRC-017 the only rights-holder in view is the ITU. (For SRC-016 it is not —
  see §4a.)
- **Nothing has been published.** The package is `IsPackable=false`, the fax
  filter is not composed by default, and no capability claim exists. A decision
  taken now is taken before distribution rather than after it.

## 5a. Evidence gathered, 2026-09-02

The brief previously said identifying the applicable terms was part of the
decision. That legwork is now done, and it points somewhere. **Nothing below is a
conclusion**; it is what the rights-holder publishes about its own material.

### What ITU's terms say

- **The Recommendations carry a reproduction notice.** The standard notice
  printed in ITU-T Recommendations reads: *"No part of this publication may be
  reproduced, by any means whatsoever, without the prior written permission of
  ITU."* It appears in current Recommendations across series — for example
  [ITU-T F.751.9 (09/2023)](https://www.itu.int/rec/dologin_pub.asp?lang=f&id=T-REC-F.751.9-202309-I%21%21PDF-E&type=items)
  and
  [ITU-T X.1771 (04/2024)](https://www.itu.int/rec/dologin_pub.asp?lang=e&id=T-REC-X.1771-202404-I%21%21PDF-E&type=items).
- **ITU's own copyright page says the same and names a route.** Permission to
  reproduce ITU material is requested from `jur@itu.int`
  ([ITU copyright](https://www.itu.int/en/Pages/copyright.aspx)).
- **The only exception ITU states is newsroom material** — press releases, media
  advisories, statistical reports, photos and *ITU News* — usable without
  authorization if ITU is acknowledged. Recommendations are not that.
- **No implementation exception is stated anywhere found.** ITU's copyright page
  carries no carve-out for implementing a standard, for excerpts, or for
  personal use.
- **T.4 (07/03) is in force**, and its page carries "Copyright © ITU, All Rights
  Reserved" ([T-REC-T.4](https://www.itu.int/rec/T-REC-T.4/en)).

### What this evidence does not settle

It is the rights-holder's statement of terms, which is one input and not the
answer. Arguments a qualified reviewer would weigh against it, and which this
brief is **not** competent to weigh:

- Whether transcribing a normative table into executable constants is
  *reproducing the publication* at all, or is implementing a standard — a
  distinction the notice does not address and which the two activities do not
  obviously share.
- Whether a Huffman code assignment is protected expression or an unprotectable
  fact or method, and whether merger applies when there is exactly one way to
  express the thing and still interoperate.
- How any of that varies by jurisdiction, which is the register's standing
  unrecorded item.

### What it does change about the dispositions

**It makes C — seek permission — materially more attractive than §6 framed it.**
The brief called C "slowest, most certain" and worth weighing only if A or B
looked doubtful. On this evidence they look harder to sustain than they did:
the terms are broad, the exception list is short and does not include this, and
the rights-holder publishes an address for exactly this request. The cost of
asking is an email; the cost of being wrong is disposition D, which the code is
already written for.

It also sharpens **D — refuse and remove**. If permission is not sought or not
given, removal is not a failure state but the answer, and the engineering is
recoverable: `CcittFaxDecoder` returns to reporting, and JPEG 2000 and JBIG2
entropy decoding stay out of scope.

**Who gathered this.** Assembled from public ITU sources on 2026-09-02 as an
evidence record, in the same way the register's other rows were prepared. It is
fact-gathering by an engineer, not advice, and the decision remains open.

## 6. Dispositions available, and what each costs

Set out neutrally. The reviewer may reach a different one.

**A. Approve reproduction under the Recommendations' terms.**
The fax path becomes publishable and SRC-018/SRC-019 unblock on the same footing.
Requires identifying the applicable terms and recording any attribution or notice
obligation they carry, which would then attach to the release artifact in the way
IP-013's Unicode notice does.

**B. Approve narrowly, for fax only.**
Publishes the existing decoder and leaves the MQ-coder question open for a
separate decision when JPEG 2000 or JBIG2 work actually starts. Costs a second
review later; avoids deciding about tables nobody has written yet.

**C. Seek permission.** *(Reweighted by §5a.)*
ITU publishes an address for this request and states no exception that covers
reproduction in an implementation. Asking costs an email and removes the question
rather than managing it. Against A and B, which now have to argue past the plain
terms, this is the cheap path to certainty — and the tables are already written,
so nothing waits on the answer except publication.

**D. Refuse, and remove.**
`CcittFaxDecoder` loses the ability to decode and the composed filter reports
rather than decodes — the state it was in before 2026-09-01. JPEG 2000 and JBIG2
entropy decoding become permanently out of scope for this project. This is a
real option and the brief does not treat it as a formality: it is what happens if
reproduction is not permitted, and the engineering is recoverable.

## 7. What must not be assumed

- That the patent clearance in IP-009 reaches this. It does not, and the row says
  so.
- That prevalence among other implementations is permission. SRC-015 exists to
  refuse exactly that inference.
- That obtaining a document permits reproducing its normative content.
- That this brief's framing is complete. It is an engineer's account of what was
  copied and why; the terms, the jurisdictions, and the weighing are the
  reviewer's.

## 8. What a decision should record

To close the row, per the register's own decision fields: the exact editions
consulted, the applicable terms and where they were read, the disposition, any
attribution or notice obligation and where it must appear, whether SRC-018 and
SRC-019 are covered or left open, the reviewer, the date, and the review date.

Since 2026-09-03 it should also record **whether SRC-016 is covered, and on what
basis** — because the honest answer may be "not covered, decide separately," and
a decision that is silent on it will be read as covering it. §4a sets out why
that reading would be wrong.
