# Office issue #249: RTF text (list labels, code pages, fields)

The rtf-text cluster of issue 249: 64 RTF samples whose semantic text differs from
LibreOffice 24.2.7.2. I ran each through the office runner (`--font-set
office-fonts-2`, the pinned fonts) and compared `broilerdoc dump --as text` with
`soffice --convert-to 'txt:Text (encoded):UTF8'`. Where the cause was not plain from
the file, I ran cut-down RTFs through the same `soffice` (text and PDF export) to see
what LibreOffice does; every behaviour below is from such a measurement or from the
writerfilter source it names.

Most of the cluster came from one change: the RTF list import added in 75550c4
replaced the old "print `\listtext`" approach with real list definitions, but it read
only a narrow subset (decimal, roman, letters and bullets, one `\leveltext` shape, no
`\ls0`), and dropped the whole list on anything else. Every such list lost its labels.

| Sample | Cause | Outcome |
| --- | --- | --- |
| `cjklist12/13/16/20/21/24/25/30/31/34/38.rtf` | `\levelnfc` 12, 13, 16, 20, 21, 24, 25, 30, 31, 34, 38 were unknown, so the list was rejected (label "."). LibreOffice maps Word's NFC codes (aiueo, iroha, Japanese legal, Hangul syllables and jamo, Tian Gan, Di Zi, the two financial forms). | **Fixed.** `RtfLists.NumberFormat` maps every NFC code 0-48 as measured in a 1-60 item list per code (`levelnfc` 8, 9 and 49+ draw no number but keep the label's text, as LibreOffice does). New `ListNumberFormat` members (`AiueoHalfWidth`, `AiueoFullWidth`, `IrohaHalfWidth`, `IrohaFullWidth`, `JapaneseLegal`, `ChineseLegalSimplified`, `HangulSyllable`, `HangulJamo`, `EnclosedCircle`) count exactly as LibreOffice's 1-60 output. |
| `cp950listleveltext1/2/3.rtf`, `chtoutline.rtf` | Big5 `\leveltext` (`\'ab` + `e` is one character) was read byte by byte. LibreOffice decodes the level text in the document's code page first, then takes its first character as the label's length and `\levelnumbers` as character positions. | **Text fixed** (all four). `chtoutline` still fails pixel: its CJK text and Latin headings resolve to other faces than LibreOffice's (the East Asian font gap of issue 242's `tdf49073`). |
| `arabic-zero-numbering.rtf`, `tdf141964_numId0.rtf` | `\listid0` / `\ls0` were treated as "no list"; `\levelnfc22` unknown. `tdf141964`'s last item also ends with `\pard` before its `\par`. | **Fixed.** Zero is a list id. A `\pard` between a paragraph's text and its mark no longer takes the numbering away (LibreOffice sends the numbering with the paragraph's first text). |
| `invalidParagraphStyle.rtf` | Level without `\levelnfc` was dropped. | **Fixed:** no `\levelnfc` is decimal. |
| `tdf108416.rtf` | `\leveltemplateid` inside `\leveltext` invalidated it. | **Fixed:** control words in the level text are not part of it. |
| `listWithLgl.rtf` | `\levelnfc22` unknown. LibreOffice 24.2 ignores `\levellegal` ("Sect I.01"). | **Fixed.** |
| `tdf122455.rtf`, `tdf116265.rtf` | A numbered level with no or an empty `\leveltext` drew "1."; LibreOffice draws nothing. A bullet level with none draws LibreOffice's default bullet. | **Fixed.** |
| `tdf106953.rtf`, `tdf78506.rtf` | `\levelnumbers \'01{\uc1 \u59 ?}`: a `;` written as `\u59` leaves LibreOffice 24.2 with no label at all (measured: `\'3b`, `{;}` or a plain `;` keep "1.", `\u59` alone or grouped drops it). The positions use LibreOffice's rule: the n-th of k positions at level l is `%(n+l+1-k)`. | **Fixed.** |
| `tdf104016.rtf` | `\'b7 ;` bullet text with a trailing space was rejected; the bullet is in the Symbol font. | **Fixed:** the label is the first `length` characters; a Symbol/Wingdings bullet byte is mapped through U+F000 as the DOCX reader maps it. |
| `fdo47107.rtf`, `tdf104085.rtf`, `tdf116841.rtf` | Word 95 `{\*\pn ...}` paragraph numbering was not read. | **Fixed:** `\pnlvlblt`/`\pnlvlbody` with `\pndec`/letters/roman/ordinals, `\pnstart`, `\pntxtb`/`\pntxta`, `\pnf` read as a one-level list (a bullet is the first character of `\pntxtb`); paragraphs numbered alike share one counter. Outline levels (`\pnlvlN`) are not read. |
| `fdo48446.rtf` | `\cpg1251` in a font entry was ignored. | **Fixed.** |
| `fdo85889-pc/pca/mac.rtf` | `\pc`, `\pca`, `\mac` header character sets ignored, and a font with no `\fcharset` decoded as 1252 instead of the document's code page. | **Fixed:** 437, 850, 10000; a font that states no charset or code page uses the document's. |
| `tdf95706.rtf` | LibreOffice reads the code page from a font name's suffix (" CE", " Cyr", " Greek", " Tur", " (Hebrew)", " (Arabic)", " Baltic", " (Vietnamese)") ahead of `\cpg`/`\fcharset` (measured on the sample's 14 lines). | **Fixed** (`RtfFontTable.GetCodePage`). |
| `tdf161864.rtf` | `SYMBOL 108 \f "Liberation Serif"` with an empty result. LibreOffice inserts the character in the named font (DomainMapper_Impl, FIELD_SYMBOL). | **Fixed.** |
| `165333.rtf` | `SYMBOL 253 \f "Wingdings"`: the text export has "ý", but LibreOffice draws nothing visible (no Wingdings installed). Inserting "ý" made broilerdoc draw a glyph on LibreOffice's blank page (pixel 15 → 900 severity). | **Declined:** SYMBOL fields in Windows symbol fonts are left empty, as before; the sample stays a text failure. |
| `fdo36089.rtf` | `EQ \s\do5(1)` with no result; LibreOffice writes the text lowered (FIELD_EQ). | **Fixed.** |
| `fdo48193.rtf` | `PAGE` field with no `\fldrslt`. | **Fixed:** an empty PAGE/NUMPAGES result becomes a page field, as `\chpgn`. |
| `fdo84685.rtf` | `\xe` and `\tc` entry text was body text. | **Fixed:** skipped (LibreOffice makes index and TOC marks). |
| `tdf108947.rtf` | A `\fldrslt` with no `\field` in a header sent its "?" to the body. | **Fixed:** it stays in its destination. |
| `tdf130817.rtf` | `{\super ${\*\footnote\ftnalt ...}}`: raised text right before a note is its custom mark (RTFDocumentImpl's super buffer); LibreOffice's text and PDF show "$$". | **Text fixed** (note `CustomMark`). Pixel still fails: 1 page against LibreOffice's 2 (the endnote page). |
| `tdf169298.rtf` | `DOCVARIABLE` result `foo\par bar`. LibreOffice gathers a text field's result into one string inserted at the field's end, after the paragraph marks (measured for DOCVARIABLE, DOCPROPERTY, AUTHOR, REF; QUOTE keeps its paragraphs). | **Text fixed.** Pixel 834 → 781 severity, still failing. |
| `n825305.rtf`, `tdf104936.rtf`, `tdf88811.rtf`, `fdo47036.rtf`, `fdo49892.rtf` | Body is one empty paragraph; the text dump exported the first *text* frame. LibreOffice exports only `SpzFrameFormats()[0]`, and here that is a line, rectangle or picture with no text. | **Fixed** in `DocumentCommands.LabelledText`: when the first drawing has no text the export is empty. Shapes sharing a DOCX group's stacking order count as one drawing (LibreOffice exports a group's text boxes: `dml-groupshape-*.docx`, `wpg-nested.docx`, measured). This also fixed 7 DOCX samples outside the cluster. |
| `FWDP90_min.rtf` | `{\*\revtbl Unknown;}`: LibreOffice 24.2 puts the ungrouped revision-table text in the body, tdf#167710, fixed in 26.2 (b45ba94b). | **Reference defect:** added to the existing `tdf167710-rtf-revision-table-text` row. |
| `pgndec`, `pgnlcltr`, `pgnlcrm`, `pgnucrm`, `tdf65642`, `fdo42109.rtf` | Body PAGE fields: the text export writes "0". Measured: a PAGE field on page 1 exports "1" (in any format), the same field after a `\page` exports "0", and `pgndec`'s first-page fields export "0" too. The export expands from whatever layout exists at export time. | **Declined:** not a property of the document; the same "0" shows in about 30 DOCX/ODT samples. |
| `fdo48037.rtf` | `DATE` field: LibreOffice exports today's date. | **Declined** (time-dependent). |
| `tdf136445-1-min.rtf` | LibreOffice skips `\listtext`; broilerdoc's reader restarts the counter from it ("E119" for "E1"), and existing tests pin that behaviour. | **Declined** (existing test contract). |
| `tdf108949_footnote.rtf` | `\listoverride` names a list that does not exist; LibreOffice numbers the paragraph "1." with a default numbering (measured also with no list table at all). An existing test pins "no list" for that case. | **Declined** (existing test contract). |
| `fdo66040.rtf`, `textframe-table.rtf` | A table inside shape text; the reader reads `\cell` there as a tab. | **Declined:** tables in RTF shape text are a separate feature (pixel fails too). |
| `fdo72031.rtf` | Text in an `\fcharset2` (Symbol) font: LibreOffice's text is U+F0C5. Decoding symbol text to the private-use area needs symbol-font glyph mapping in the painter first, or every Symbol-font character would draw as a missing glyph. | **Declined.** |
| `fdo79384.rtf` | Raw Shift-JIS whose trail bytes are `{`, `}` or `\`; LibreOffice's tokenizer reads them as trail bytes in code page 932 (fdo#79384). The tokenizer here is code-page blind. | **Declined** (tokenizer change). |
| `tdf108505_fieldCharFormat.rtf` | `FORMDROPDOWN`: LibreOffice draws the first entry ("MZ") but its text export leaves the drop-down out. The model has no form-field run. | **Declined.** |
| `tdf156030.rtf` | `IF` fields with no result: issue 232 draws their evaluated text (pixel), LibreOffice's text export leaves the conditional text out. | **Declined** (would undo the pixel fix). |
| `tdf161878.rtf`, `fieldmark_QUOTE_nest.rtf` | Nested fields in instructions with paragraph marks inside instructions and results. | **Not fixed.** |

Two existing tests encoded behaviour LibreOffice does not have: three
`RtfListTableTests` cases built levels with no `\leveltext` and expected "1.". Their
subject is counters (start overrides, instances, cached ordinals), so their input now
states `{\leveltext \'02\'00.;}{\levelnumbers\'01;}` and they assert the same numbers;
the missing-text case is pinned by the new tests instead.

## Verification

Office runner on the 64 samples at the base commit (8a20ca0): 0 passing. After:

- **42 pass**, 1 is a known reference defect (`FWDP90_min.rtf`), 21 still fail. None
  has a higher severity than before; `chtoutline` 725.6 → 338.1, `tdf130817`
  762.1 → 703.4, `tdf169298` 834.2 → 781.4.
- **Text dumps of the whole corpus** (4,063 docx/odt/fodt/rtf/doc files), base build
  against this branch: 56 change, all in the run below.
- **Layout sweep** against the baseline: 46 samples change, all in the run below.
- **Runner on the union** (78 samples: the cluster, every changed dump, every changed
  layout): 49 failing → passing (the 42 above and `fdo66688`, `fdo78432`,
  `tdf137850_compat14ZOrder`, `tdf137850_compat15ZOrder` from ooxmlexport,
  `tdf117028` from layout, `tdf135412` and `tdf99004` from uiwriter). Nothing that
  passed fails; `tdf159158_zOrder_zIndexWins.docx` keeps failing on pixel only, as
  before. The three `forcepoint` crash inputs pass their robustness check.
- A first version of the first-frame rule ignored DOCX groups and broke 8 passing
  group-shape samples (`dkvert`, `relorientation`, `wpg-nested`,
  `dml-groupshape-runfonts`...); the stacking-order grouping restored all of them.

## Tests

`RtfListTableTests` (level labels as LibreOffice reads them, the position rule,
Big5 level text, list 0, Word 95 numbering, `\pard` before the mark),
`RtfIssue249Tests` (header character sets, font code pages and name suffixes, SYMBOL,
lowered EQ, empty PAGE, `\xe`/`\tc`, stray `\fldrslt`, custom note marks, text-field
results), `ListNumbererTests.East_Asian_Formats_Count_As_LibreOffice_Does` (LibreOffice's
1-60 output for nine formats), `DumpTableTextTests` (a textless first drawing, a group's
text box).
