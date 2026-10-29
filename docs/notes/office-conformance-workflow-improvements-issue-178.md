# Office conformance workflow: improvements found during issue #178

These came up while resolving the first difference of
[issue #178](https://github.com/Broiler-Platform/Broiler.Documents/issues/178)
(`tiledrendering/data/page-background-link.fodt`, see the
[issue note](office-issue-178.md)). They continue the lists from
[issue #170](office-conformance-workflow-improvements.md),
[issue #173](office-conformance-workflow-improvements-issue-173.md) and
[issue #175](office-conformance-workflow-improvements-issue-175.md), and are
ordered by how much time they would have saved. All are proposals; none is
implemented in this change.

## 1. Five investigations repeated one wrong cause

**What happened.** The notes for issues #138, #143, #145, #147 and #164 all
looked at this sample and wrote some form of "the external image is not
fetched", as though the reference showed the picture. The address in the link
is `192.0.2.1`, TEST-NET-1, which is never routed, and the references are made
in a container. LibreOffice painted its fallback for an unloadable picture, a
solid `#729FCF`. Nobody looked at what the reference drew. The issue's
"Earlier Notes" links led each new reader back to the same sentence. Issue #175's
workflow note had the same problem with a different sample.

**Proposals.**

- **Print what the reference draws.** For a failed pixel check, add one line
  per side for the worst page: its dominant non-white colours and their share
  of the page, from the rasters the check already has. Here it would have read
  "LibreOffice: 99.7% `#729FCF`; broilerdoc: 0.3% `#000000`", which gives the
  cause straight away. A wild coverage delta with matching geometry is nearly
  always a fill, and the colour usually names it: `#729FCF` is LibreOffice's
  default fill.
- **Mark a sample that keeps coming back.** When "Earlier Notes" finds three or
  more notes for a sample and it still ranks, say so in the entry ("ranked in
  5 earlier issues, still open"). Those are the samples where the recorded
  cause should be checked first, not repeated (compare #175's item 3).

## 2. The reference PDF is the fastest evidence and goes unused

**What happened.** The answer came from the reference PDF's content stream,
not from a raster: the `rg` operator and the rectangle before the first `BT`.
The PDFs are in the reference archive, but the report only uses them through
`pdftoppm`.

**Proposal.** Keep a small vector summary per reference page next to the
manifest: the fill colours and rectangle bounds drawn before the first text
object, the image draws (`Do`) with their placement matrices, and the text
start positions. It is cheap to extract with the PDF reader `broilerdoc`
already has. The report could then say "reference: full-page fill `#729FCF`"
or "reference: picture at 0,0 595x842", and a background, frame or picture
placement difference becomes a comparison of numbers. The same summary from
`broilerdoc render --format pdf` would make the comparison direct.

## 3. One worst page hides changes on the others

**What happened.** On `odfexport/data/pagestyle_background_lo64.odt` (six
pages, six page styles) this change put the stretched and single pictures of
pages 3 and 4 inside the margins, as the reference has them. The report's
numbers got worse: blur difference 0.06% -> 12.8%. The worst page moved from 4
(fixed) to 6, a hatch that was always 12.8% off. The check flipped from failed
to passed, but a reader comparing the two `renders[]` entries would have seen
a regression. `report.json` keeps only the worst page's metrics.

**Proposal.** Write every page's metrics into `renders[].pages[]` (the runner
computes them anyway) and let `--previous-triage` compare page by page,
reporting "pages 3, 4 improved; page 6 unchanged" alongside the worst page.

## 4. A fix's siblings had to be found by hand

**What happened.** The rule behind the ranked sample covers four more corpus
samples with linked backgrounds (`background-image-link.fodt` twice,
`paragraph-style-background-link.fodt`, `two-paragraph-background-links.fodt`),
one with a linked shape fill (`shape-fill-link.fodt`), and the page-bounds half
covers ten producer-dependent samples. Finding them took a script over the
unpacked corpus.

**Proposals.**

- **Group by diagnostics.** The read check already knows each sample's
  diagnostic codes. The triage could list, under each ranked sample, the other
  failing samples that share its rarer codes (here `odt.image.external` and
  `odt.fill`), so that the blast radius of a fix is known before work starts.
- **Keep a feature index.** A one-off scan of the inputs (which ODF elements and
  attributes, DOCX parts and RTF control words each sample uses) stored next to
  the manifest would make "which samples use `style:background-image` with an
  `http:` link" one lookup. Earlier issues built such scans ad hoc
  (`D:/scratch/tools` on the maintainer's host); a committed generator would
  keep them comparable.

## 5. LibreOffice's behaviour depends on the producer, and the report does not show it

**What happened.** Half of this fix depends on `meta:generator`: LibreOffice
keeps an old document's page fill inside the margins and gives a current one
the whole page. Issue #140 had generalised from old documents to all of them.
The same kind of rule already exists for OpenOffice.org 2.x's inverted
`text:display`, and LibreOffice has more: `isGeneratorVersionOlderThan` is called throughout
`xmloff` and the filters.

**Proposal.** Record each sample's producer (ODF `meta:generator`, DOCX
`docProps/app.xml` `Application`/`AppVersion`, RTF `\generator`) in the
manifest, and show it in the ranked entry. The triage could then also break
failures down by producer family, which shows producer-dependent rules
before anyone has to guess at them.

## 6. Authored probes against the host's LibreOffice settled the rules

**What happened.** The rules in the issue note were settled by about 20 small
authored documents converted with the host's LibreOffice 26.2 and read back
from the PDF (fill colour and bounds). That took minutes and needed no
reference rebuild. The probe documents and the fill extractor were throwaway
scripts outside the repository, so the next investigation will write them
again.

**Proposal.** Add a small probe harness under `eng/`: it takes a template with
placeholders and a table of variants, writes the documents into a scratch
directory, converts them with a given `soffice`, and prints each PDF's page
fills, image placements and text origins (item 2's summary). Nothing would be
committed but the generator, in line with the rule against vendored documents.
A probe that runs on both the pinned 24.2 image and the host's newer release
would also give issue #175's item 6 its fixed-version evidence.

## 7. Small things met on the way

- **The offline replay is fast and could be the documented default for a
  fix.** `--references-dir` with `--sample-list` scored the 372 samples this
  change could touch in about three minutes per build, and all 1,492 ODT
  samples in under eight minutes. The guide documents both options, but not the
  before/after recipe of building `main` and the branch into two directories
  and replaying each. A short section, or a `--tool-before`/`--tool-after` pair
  that writes the comparison, would make every fix come with its corpus
  numbers.
- **`dotnet test` without the solution name.** The repository has
  `Broiler.Documents.slnx`; `dotnet test Broiler.Documents.sln` ends with a
  test summary of 17 failures and none passed, which looks like broken tests
  rather than a wrong file name.
  The guide could name the `.slnx` explicitly.
- **Heredocs and backslashes on the Windows host.** Inline Python passed
  through a Git Bash heredoc lost `'\\'` string literals twice; `chr(92)` or a
  script file avoids it.
