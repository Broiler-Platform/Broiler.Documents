# Office conformance workflow: improvements found during issue #170

These came up while resolving the first difference of
[issue #170](https://github.com/Broiler-Platform/Broiler.Documents/issues/170)
(`ooxmlexport/data/tdf163894_hidden.docx`, see the
[issue note](office-issue-170.md)). The issue itself needed only the
reference-defect list (item 2). The others were left to be decided on
deliberately. They are ordered by how much of the ranking they distort.

**Status, 2026-10-29.** Done: item 1's font fix (it takes effect once
`office-references-v3` is generated), item 3's flag and item 4's links to
earlier notes. Still open: a newer reference LibreOffice or flagging samples
newer than it (item 2), and item 4's other three observations. Each section
says what was done.

## 1. The reference fonts have no Liberation faces

**What happens.** `office-reference-data.yml` installs
`fonts-liberation2 fonts-crosextra-carlito fonts-crosextra-caladea fonts-dejavu-core`
and copies into the reference font directory only the `.ttf` files those four
packages list (`dpkg-query -L`). On Ubuntu 24.04, `fonts-liberation2` is
a transitional dummy package. Since Debian `1:2.1.5-2` the fonts ship in
`fonts-liberation`, and `fonts-liberation2` only depends on it (Debian's
`fonts-liberation` `1:2.1.5-3` control file: "Package: fonts-liberation2 …
Description: transitional dummy package"). `FONTCONFIG_FILE` then resets
fontconfig to that directory alone. So the reference LibreOffice never sees
Liberation Serif, Sans or Mono.

**Evidence.**

- The `office-references-v2` font directory (`fonts/0`) holds Caladea, Carlito,
  DejaVu Sans and DejaVu Serif, and nothing else.
- The reference PDF for `tdf163894_hidden.docx`, whose body text asks for
  Liberation Serif and whose header asks for Liberation Sans, embeds
  `DejaVuSans-Bold`, `DejaVuSerif-Bold` and `DejaVuSerif` (`pdffonts`).

**Why it matters.** Every DOCX that asks for Times New Roman, Arial or Courier
New, and every ODT that asks for Liberation, is drawn in the reference with
DejaVu. DejaVu is much wider, so lines break earlier and documents run to more
pages. `broilerdoc` gets the same font directory. Its pinning maps Times New
Roman to Liberation Serif, Arial to Liberation Sans and Courier New to
Liberation Mono (`FontPinning.cs`), but those files aren't there, so it falls
back on its own terms. In the render checked here it drew Liberation Serif text
in a sans face where LibreOffice chose DejaVu Serif. Both sides draw text the
document didn't ask for, and they substitute differently. A large share of the
pixel axis's page-count and line-break differences may come from this rather
than from either renderer.

**Proposed fix.** Add `fonts-liberation` to both the `apt-get install` line
and the copy loop, and make the step fail when a package contributes no
`.ttf` file, so a future transitional package can't empty the set silently:

```bash
for package in fonts-liberation fonts-crosextra-carlito fonts-crosextra-caladea fonts-dejavu-core; do
  count=0
  while IFS= read -r font; do
    case "$font" in
      /usr/share/fonts/truetype/*.ttf)
        relative="${font#/usr/share/fonts/truetype/}"
        mkdir -p "$FONT_ROOT/$(dirname "$relative")"
        cp "$font" "$FONT_ROOT/$relative"
        count=$((count + 1))
        ;;
    esac
  done < <(dpkg-query -L "$package")
  test "$count" -gt 0 || { echo "$package installed no TrueType font" >&2; exit 1; }
done
```

That changes `FontsSha256`, so it needs a new font-set name (`office-fonts-2`),
a new `releaseTag` in `tests/office/reference-source.json`
(`office-references-v3`) and one dispatch of the reference workflow. Cached
conformance runs already take their fonts from the archive (`fonts/0`), but
they pass the font-set name from the office image's `BROILER_OFFICE_FONT_SET`,
and the reference loader compares it, so the image or the workflow has to name
the new set too. Expect the first ranking after it to look very different. That will be
the fonts, not a regression.

**Done 2026-10-29.** The reference workflow installs and copies
`fonts-liberation`, fails a package that contributes no TrueType file, and
names the set `office-fonts-2`. `reference-source.json` names
`office-references-v3`. Cached conformance runs now read the font-set name
from the archive's manifest instead of the office image, so the image didn't
need to change. `eng/run-office-conformance.sh` installs `fonts-liberation`
too. Its local install was never broken, since `fonts-liberation2` depends on
the real package. The release exists once someone dispatches **Office
reference data**. Until then, the nightly run fails with the missing-release
message.

## 2. The references are two years older than the corpus

**What happens.** The corpus is LibreOffice master at `corpusRevision`
(`54dd3de`, 2026). The references come from Ubuntu 24.04's LibreOffice
24.2.7.2, released in 2024. Upstream adds a sample with almost every layout
fix, so the samples added since 24.2 test fixes that 24.2 doesn't have. The
pinned reference then shows the defect the sample was written to catch, and a
`broilerdoc` that draws it as the fixed LibreOffice and Word do ranks at the top.

**Evidence.** The first two differences in issue #170 were exactly this:
tdf#163894, fixed on master in February 2026 and first released in 26.8. The
same samples led reports #140, #143, #145 and #147. Each time the triage
concluded "reference-version dependent" without a mechanism to record it, so
each report spent its first slots, and a reviewer's time, on them again.

**Done here.** `tests/office/extras-reference-defects.json` lists samples
whose reference is a traced LibreOffice defect, with the bug, the fixing
commits, the upstream tests and the first fixed release. While the reference
LibreOffice is older than `fixedIn`, the runner doesn't compare the named
checks. `broilerdoc` still has to read and render each sample, and the report
lists them under "Known Reference Defects". A row stops applying by itself once
the reference is new enough, and a guard fails if the pinned LibreOffice
already has the fix.

**Proposed next steps.**

- **Pin a newer LibreOffice for references.** Install a specific release from
  The Document Foundation's `.deb` archives (version and SHA-256 pinned in
  `reference-source.json`) instead of the distribution's. A release close to
  `corpusRevision`, today 26.8, makes most of these rows unnecessary. The
  tool register entry for LibreOffice would need re-approval for the new
  version.
- **Or pin the corpus to the reference's release.** Taking `sw/qa/extras`
  from the `libreoffice-24.2.7.2` tag instead of master makes sample and
  reference agree by construction. The cost is losing every sample added
  since, and with them the fixes they test.
- **Flag samples newer than the reference.** The reference workflow already
  clones `sw/qa/extras` with `--filter=blob:none`. Without `--depth=1`,
  `git log --diff-filter=A --format=%cs -- <sample>` gives the date each sample
  was added for little extra cost. If the report printed "added after the
  reference LibreOffice was released" beside a ranked sample, the next
  tdf163894 would show up in the first triage instead of the sixth.

## 3. Local reproduction can't use the CI's references

`OfficeReferences.Load` refuses a reference archive whose platform, font
hash, font-set name or timeout differs from the consumer's ("Reference
toolchain mismatch … Regenerate references."). That's right for CI. But a
developer on Windows has the archive and can't score against it, so local
triage runs this host's LibreOffice instead: 26.2.6.3 here, which draws
`tdf163894_hidden.docx` on 67 pages, not 22. The notes for issues #135 to
#154 record 17, 22, 40, 4 and 5 reference pages for the same file, measured
with four LibreOffice versions; 40 was the comparison's page cap.

**Proposal.** A `--accept-reference-mismatch` flag (never passed in CI, refused
together with `--create-github-issue`) that loads the archive anyway. It would
print the mismatched fields in the run header and the issue. The input hashes,
the crash classification and the per-sample status would stay checked.
Together with item 1's font directory, a developer could then reproduce a
nightly ranking exactly on any host.

**Done 2026-10-29.** `--accept-reference-mismatch` (runner and
`eng/run-office-conformance.sh`) loads the archive anyway. It requires
`--references-dir` and refuses `--create-github-issue`. It lists every
differing field (platform, font set, font hash, timeout) on the console, in
the issue's run summary as "Reference Toolchain" and in the JSON report as
`referenceMismatches`. The schema version, producer provenance, input hashes,
crash classification and output hashes are still checked, and a mismatch
without the flag still fails. The error now names the differing fields and
the flag.

## 4. Smaller observations

- **Headers and footers are only on the pixel axis.** LibreOffice's text
  export leaves running content out (`wrtasc.cxx`), so a header that is wrong
  costs nothing on the text axis. tdf163894's whole difference was in its
  headers. A text projection of the running content on both sides would make
  such differences visible without pixels. `broilerdoc` would need a dump mode
  for it, and on LibreOffice's side the reference PDF's text in the header and
  footer bands could serve (`pdftotext -layout` with crop boxes), since its
  text export has no such option.
- **Page-count gaps dominate severity.** A defect that changes only how many
  lines fit per page (here, justified-line shrinking) moves the score as much
  as a missing table. The per-page bands already exist. Scoring the pages both
  sides have in common separately from the page-count gap would make the
  ranking say which kind of difference it is.
- **Prior triage isn't linked.** Earlier notes in `docs/notes/` mention most
  of the long-running top samples. The report could list, per ranked sample,
  the notes that name it (a grep over `docs/notes/*.md`), so whoever picks up
  the next issue starts from what was already found. *Done 2026-10-29:* each
  of the top differences has an "Earlier Notes" line (`PriorNotes.cs`). It
  matches the sample's file name on its own and links each note at the run's
  commit in GitHub Actions.
- **A word processor's compatibility settings are layout.** Word 2013's
  justification (DOCX `compatibilityMode` 15, LibreOffice's
  `JustifyLinesWithShrinking`) changed which words fit on a line in every
  justified paragraph of most modern DOCX files, and nothing in the report
  pointed at it. A per-sample note of the compatibility settings a document
  states (`compatibilityMode`, ODF `config:config-item` layout flags) would
  have pointed at it from the first ranking.
