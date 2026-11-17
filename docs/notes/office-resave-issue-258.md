# Office re-save run 3: issue #258

Investigation of [issue #258](https://github.com/Broiler-Platform/Broiler.Documents/issues/258),
[run 44157852752](https://github.com/Broiler-Platform/Broiler.Documents/actions/runs/44157852752),
at commit `b8eb5b6c8f1896628b7685976da734effb580c1c`.
Corpus: LibreOffice `54dd3de1c918cfdde2aa261dd4fb9745520bd40b`, `sw/qa/extras`.
Evidence: the complete `office-resave` artifact, ID `14361992562`, especially
`office-results.json`; the issue body is truncated and omits later failures.

## Finding

The hosted Ubuntu job set up .NET and resolved the corpus successfully. Its
failure is the re-save suite's verdict, not a missing Office container or
LibreOffice installation. All 3,918 samples failed, but those failures have
different causes:

| Result | Samples |
| --- | ---: |
| Conversion exited 0, output differed byte for byte | 3,897 |
| Conversion exited 3 (read rejected) | 17 |
| Conversion exited 1 (PDF writing unsupported) | 4 |
| Passed / skipped | 0 / 0 |

There are no reported exit-70 conversions or missing-output failures. The
issue-publication step also reports failure because it explicitly calls
`core.setFailed` when the preceding job status is not success; the issue was
created and artifact upload succeeded.

## The byte check measures a contract the codecs do not promise

`ReSaveConformance.Evaluate` compares source bytes directly with a newly
serialized file. `convert` reads into the normalized document model and writes
that model through the selected codec. It does not preserve the original
package. The [roadmap](../roadmap.md) and CLI round-trip contract explicitly
exclude source-preserving round trips.

Local reproductions against the same commit and corpus reproduced these sizes:

| Sample | Input bytes | Re-saved bytes | CLI model comparison |
| --- | ---: | ---: | --- |
| `htmlimport/data/green-highlight.html` | 92 | 118 | Equal, text and format codes equal |
| `autocorrect/data/de-DE.fodt` | 709 | 1,850 | Equal, text and format codes equal |
| `docbookexport/data/simple.docx` | 3,759 | 2,246 | Equal, text and format codes equal |

For `simple.docx`, the ZIP entry count changes from nine to five: settings,
font table, and document properties are not copied into the output package.
For `de-DE.fodt`, the source begins with XML, while the output begins with the
ZIP signature `PK`. The suite maps `.fodt` to `odt` and selects `--to odt`, so
the workflow also compares different serializations of the format family.
The workflow currently retains the `.fodt` output suffix despite writing a ZIP.

Three further local round trips, `accessibility/testdocuments/tdf155705.fodt`,
`cjk/data/UnderlineTrailingSpace.docx`, and
`globalfilter/data/fieldmark_QUOTE_nest.rtf`, reported zero structural differences.
`tdf155705.fodt` did report unequal format codes: paragraph spacing changes from
`7.001575` to `7.002`. The model comparison tolerates this rounding. A replacement
check must define its numeric tolerances rather than assuming exact formatting
code equality is equivalent to the existing structural comparison.

These reproductions establish false positives in the byte criterion. They do
not establish that all 3,897 conversions preserve content, or that the initial
import captured every upstream feature. A model round trip cannot detect data
that was already lost during the initial import.

## Four DOCX read failures are caused by forced input routing

These four `.docx` files are ODT ZIP packages. Each contains an ODT `mimetype`
entry and no Word document part or OOXML root relationships:

- `odfimport/data/tdf76322_columnBreakInHeader.docx`
- `ooxmlexport/data/tdf171025_pageAfter.docx`
- `ooxmlexport/data/tdf171038_pageAfter.docx`
- `uiwriter/data/tdf132596.docx`

The runner derives `sample.Format` from the extension and unconditionally
passes `--from docx`. That bypasses normal content detection and produces the
"did not contain a main word/document.xml part" rejection. Locally,
`info --json` identifies all four as ODT; conversion without `--from`, using
`--to odt`, succeeds for all four.

## Other rejections need explicit policy

Of the remaining 13 read rejections, eight are encrypted/password-protected
inputs, and five report XML parsing errors:

- Three encrypted ODT packages: `odfexport/data/sha1_correct.odt`,
  `odfexport/data/sha1_wrong.odt`, `ooxmlexport/data/tdf133771.odt`.
- Four encrypted DOCX packages:
  `ooxmlexport/data/Encrypted_LO_Standard_abc.docx`,
  `ooxmlexport/data/Encrypted_MSO2007_abc.docx`,
  `ooxmlexport/data/Encrypted_MSO2010_abc.docx`,
  `ooxmlexport/data/Encrypted_MSO2013_abc.docx`.
- One password-protected PDF:
  `pdf/data/Hybrid_EmbeddedFileOnlyPDF20UAPasswordpop.pdf`.
- Five XML read failures: `layout/data/ofz64109-1.fodt`,
  `odfimport/data/forcepoint108.fodt`,
  `ooxmlimport/data/math-malformed_xml.docx`,
  `ooxmlimport/data/tdf165348_broken_package.docx`,
  `uiwriter/data/threadedException.fodt`.

The four exit-1 failures are readable PDFs for which `convert --to pdf`
reports "This tool does not write PDF":
`pdf/data/Hybrid_AdditionalStreamsAndPDFAttachedFile.pdf`,
`pdf/data/Hybrid_AdditionalStreamsOnly.pdf`, `pdf/data/PDFOnly.pdf`, and
`uiwriter/data/hello-world.pdf`.

The selected corpus includes 23 members of the existing crash-regression
families; two of them are among the read rejections. Unlike the semantic/pixel
suite, re-save mode does not apply their existing handling policy. Their
expected behavior needs to be reported separately from document fidelity.
Do not blanket-accept every exit 3: that would conceal new reader failures.

## Recommended correction

1. Make normalized same-format round-trip fidelity the default verdict, using
   `roundtrip <input> --via <detected-format> --json`, or an equivalent
   convert/read-back comparison. Retain byte identity as an optional diagnostic
   metric. Define structural, text, formatting, and numeric tolerance policy
   explicitly and keep per-sample diagnostics and reproduction commands.
2. Detect the input codec from content. Use its writable canonical format for
   the output and a matching output extension; only force input routing when
   justified for a specific sample.
3. Report unsupported write targets and known encrypted inputs explicitly.
   Apply the established crash-regression policy, while retaining failures for
   unexpected rejections, exit 70, crashes, timeouts, and unreadable output.
4. Summarize failure categories and representative samples in the issue, with
   full details in artifacts. Alphabetical truncation currently hides the
   non-byte failures behind thousands of serialization differences.
5. Regenerate the separate re-save failure list after adopting the new verdict;
   carrying forward all 3,918 byte failures makes incremental runs effectively
   full runs.

This investigation changes no pass/fail behavior and does not rerun the entire
corpus. The artifact supplies the complete-run counts; local reproduction
checks the representative conversions and the four misrouted inputs.

## Follow-up implementation and full recheck

The recommended default verdict is now implemented as
`normalized-roundtrip-v1`; the [guide](../guides/office-conformance.md#normalized-re-save-conformance)
defines the comparison and rejection policies. Rechecking the same pinned corpus
on Windows produced:

| Category | Samples |
| --- | ---: |
| Normalized model equal | 3,367 |
| Normalized model different | 513 |
| Crash-regression robustness handled | 23 |
| Known encrypted input skipped (exact hash and exit 3) | 8 |
| Read-only PDF write target skipped | 4 |
| Unexpected read rejection | 3 |

Totals: **3,390 passed, 516 failed, 12 skipped**, across all 3,918 samples.
There were no invalid-report, round-trip process, output read-back, crash, or
timeout failures in this run. The 513 model differences are findings to triage,
not an assertion that every difference is an undocumented codec defect.
The separate `resave-failed-documents.txt` was regenerated and checked against
all failed IDs in the JSON report: exactly 516 entries.

The remaining read rejections are `ooxmlimport/data/math-malformed_xml.docx`,
`ooxmlimport/data/tdf165348_broken_package.docx`, and
`uiwriter/data/threadedException.fodt`. They are not blanket-exempted just for
returning exit 3. A focused 13-sample recheck covered the four mislabeled ODTs,
serialization false positives, rounding, known encryption, a fuzz rejection,
a read-only PDF, and an unclassified malformed input: nine passed, three
skipped, and only the unclassified malformed input failed. All 130 office unit
and integration tests pass; workflow YAML/Bash and rejection-policy schema
validation also passed. GitHub Actions itself was not dispatched.
