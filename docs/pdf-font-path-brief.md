# §11.3 Font Path Brief: How a Writer Gets a Font

**Status:** Draft for the **project reviewer** — Maik Ratzmer — whose seat this
falls in: it is a product and scope decision about what this project ships. Not a
recommendation, and not legal advice.
**One part is not his to settle.** Path A obliges the project to a specific
font's licence, and reading that licence is the qualified legal seat's work, which
is unassigned. Path B avoids it entirely, which is a difference worth weighing
rather than a tiebreaker.
**Prepared:** 2026-09-02
**Decides:** the operational font path required by
[roadmap §11.3](pdf-support-roadmap.md#113-font-and-embedding-license-policy)
before `CanWrite` is enabled in an official host.

Unlike [SRC-017](pdf-src-017-review-brief.md), this is not a question about
somebody else's rights. It is a product decision with a licensing consequence,
and either answer is defensible. What is not defensible is leaving it unmade
while the writer's capabilities grow around it.

## 1. The decision, exactly

§11.3 requires **one** operational path, implemented identically by the CLI and
by every Writer head:

- **A — bundle.** Ship a specifically approved, package-tested fallback font,
  with all required notices and generated-document obligations.
- **B — require configuration.** Require an explicitly configured caller font
  set, and present a preflight or UX failure when it is absent.

The roadmap's own warning is the reason it insists: *"a roadmap promise without
provisioned fonts is not a working save feature."*

## 2. Why the question is dormant today, and what wakes it

The PDF writer emits **the fourteen standard font names with no embedded
program**. It provisions nothing, because it names fonts and leaves the reader to
supply them. A character outside the WinAnsi repertoire is replaced with `?` and
reported as `pdf.write.character-unsupported`.

That is why there is no font problem today, and it is also the limitation: the
standard fourteen cannot carry Greek, Cyrillic, or any complex script. Every one
of those needs an embedded program, and an embedded program needs a font that
came from somewhere with terms attached.

So this decision is **coupled to the embedding decision**, not independent of it.
IP-012 must be re-opened before embedding is implemented at all; §11.3 must be
answered before the result can be enabled in a host. Answering §11.3 first costs
nothing if IP-012 is later refused — the machinery simply stays unused — and
answering it late blocks a feature that is otherwise finished.

## 3. What is already built, and what is not

**Built** (and inside IP-012's inspection clearance, clearing nothing):

- A font's `OS/2` `fsType` declaration is readable.
- The conversion context records a caller's per-font licence disposition, bound
  to the program's digest so a substituted font fails the check.
- `DocumentFontEmbedding` is a fail-closed preflight requiring the caller's
  decision *and* the font's declaration, refusing on restricted, silent,
  no-subsetting and bitmap-only.
- `DocumentFontEmbedding.MayReExport` encodes §11.3's rule that a font found in
  an input document is not export authority.

**Not built, and blocked on this decision:**

- Any provisioned font at all — no asset is bundled and no configuration surface
  exists.
- The preflight *failure experience* path B requires: what the CLI prints, what
  each Writer head shows, and what a save does when no font is configured.
- Embedding and subsetting themselves, which are additionally blocked on IP-012.

## 4. A hazard worth naming before either path is chosen

`Broiler.Graphics` performs **ambient installed-font discovery** —
`BSystemFonts`, `InstalledFontScan`, and the fallback face resolution the
renderer uses. That is correct and necessary for *display*: a control draws with
whatever the machine has.

§11.3 forbids it for *export*: "never select fonts through ambient installed-font
discovery", and "never substitute an ambient OS font" when a requested one cannot
be embedded.

Both statements are true at once, and the boundary between them is not currently
enforced by anything — it is simply that no export path selects a font yet. Under
either path, that boundary needs a test rather than an intention, because the
failure is silent: a document exported on a machine with a font would differ from
the same document exported on a machine without it, and nothing would say so.

## 5. Path A — bundle an approved fallback

**What it obligates.** A font whose licence covers, in writing: embedding,
subsetting or other modification, redistribution inside a generated document,
commercial use by the caller, every target platform, and whatever must travel
with each generated document. Reserved Font Name and modified-naming obligations
apply if the chosen font carries them. The licence text and attribution ship in
the package; the per-document obligation, if any, is the writer's to fulfil or
refuse.

**What it costs.** A package asset in every head, including Android and
WebAssembly where size is not free. A licence review of the specific font, not of
a family or a foundry. Package tests proving the asset is actually present in a
clean build, which the roadmap asks for by name.

**What it buys.** Save works out of the box, with no host configuration and no
failure path for the common case. A user who opens a Cyrillic document and saves
it gets a correct file rather than an error.

## 6. Path B — require a configured font set

**What it obligates.** No bundled asset and no notices of the project's own,
which removes the licence review entirely — the caller supplies fonts and the
caller holds the terms, which is what the resource policy already records.

**What it costs.** Every host must implement configuration and a preflight
failure: the CLI needs options and documentation, and every Writer head needs a
way to be told and something to show when it has not been. Until a user does
that, saving anything outside WinAnsi fails. §11.3 requires the CLI and every
head to implement the same decision, so this is four surfaces, not one.

**What it buys.** The project ships no font and holds no font licence, which is
the smallest possible obligation surface, and it matches the position the codec
already takes everywhere else: caller-composed, explicitly permitted, nothing
ambient.

## 7. What must not be assumed

- **That a freely downloadable font is redistributable.** §11.3 says so
  explicitly, and it is the most likely wrong turn on path A.
- **That `fsType` decides.** It is an enforcement input; the licence governs. The
  preflight already requires both and neither substitutes.
- **That the display fallback can serve as the export fallback.** It is selected
  ambiently, which §11.3 forbids for export, and it is whatever the machine
  happened to have.
- **That a hybrid is available.** §11.3 says *choose one operational path*. A
  bundled font that is silently used when configuration is absent is path A with
  path B's user experience, and it obliges the project to the licence anyway.

## 8. What a decision should record

The chosen path; for path A the exact font, version, licence, and where its text
and attribution ship; the generated-document obligation if any and how the writer
fulfils or refuses it; for path B the configuration surface in the CLI and each
head and what each shows on failure; the platforms covered; and the determinism
evidence §11.3 requires — that one fixed font set produces identical font
resources, shaping, and page-scene geometry on Windows and Linux, with separate
WebAssembly evidence before that head is enabled.
