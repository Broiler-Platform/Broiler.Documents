# SRC-017 Review Brief: Reproducing Normative Code Tables

**Status:** Draft for the **qualified legal reviewer**, a seat that is currently
unassigned. Nothing here is a decision, and nothing here is legal advice.
**Not decidable by the project reviewer.** Maik Ratzmer holds the engineering
seat and is not a lawyer; this question is about the terms of somebody else's
publication, which is the other seat's work.
**Prepared:** 2026-09-02
**Decides:** [SRC-017](pdf-approved-sources.md), and by reference SRC-018 and
SRC-019.

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

More than fax. Two other rows defer to this one:

- **SRC-018** (JPEG 2000): "should an entropy decoder ever be written, its
  MQ-coder state table and context assignments are normative constants with no
  authored alternative, and fall under the same open question as SRC-017."
- **SRC-019** (JBIG2): the arithmetic decoder's state and context tables, on the
  same reasoning.

So a decision here governs whether the two unfinished codecs can be finished at
all. IP-007 and IP-008 are both approved; the engineering is scoped; and this
question sits in front of both regardless of how much of that engineering gets
done first. That makes SRC-017 the highest-leverage open item in the PDF work,
not merely the last one.

It also gates publication of the fax path that already exists and works.

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
  matters because it separates the question from any third party's licence: the
  only rights-holder in view is the ITU.
- **Nothing has been published.** The package is `IsPackable=false`, the fax
  filter is not composed by default, and no capability claim exists. A decision
  taken now is taken before distribution rather than after it.

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

**C. Seek permission.**
Slowest, most certain. Worth weighing only if A or B look genuinely doubtful,
since the tables are already written and the alternative to permission is
removal.

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
