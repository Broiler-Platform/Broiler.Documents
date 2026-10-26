## Office Conformance Major Differences (`sw/qa/extras`)

Automated comparison of sample documents from [https://github.com/LibreOffice/core/tree/master/sw/qa/extras](https://github.com/LibreOffice/core/tree/master/sw/qa/extras) rendered with **LibreOffice headless** and **`broiler.cli` (`broilerdoc`)**.

### Run Summary

| Property | Value |
| :--- | :--- |
| **Corpus Source** | `https://github.com/LibreOffice/core/tree/master/sw/qa/extras` |
| **Supported Samples Evaluated** | 3901 |
| **Documents Compared With LibreOffice** | 3878 |
| **Crash-Regression Inputs Set Aside** | 23 |
| **Documents With Differences** | 3468 |
| **Refused By Both** | 11 |
| **Top Major Differences Reported** | 30 (requested top 30) |
| **LibreOffice** | `LibreOffice 24.2.7.2 420(Build:2)` |
| **Rasteriser** | `pdftoppm version 24.02.0` |
| **Platform / DPI** | `linux` @ `144` dpi |

### Format Breakdown

| Format | Documents | With Differences | Set Aside |
| :--- | ---: | ---: | ---: |
| `docx` | 1802 | 1590 | 2 |
| `html` | 37 | 36 | 7 |
| `odt` | 1488 | 1320 | 3 |
| `pdf` | 5 | 4 | 0 |
| `rtf` | 546 | 518 | 11 |

### Crash-Regression Inputs

23 sample(s) are fuzzer output that LibreOffice keeps as crash regressions (`forcepoint` 18, `fuzzer-testcase` 2, `ofz` 3; families from `tests/office/extras-crash-regressions.json`). They are not compared with LibreOffice, whose reading of a fuzzed file is not a reference, and they are not ranked. `broilerdoc` still has to handle each one with a documented exit code.

All 23 were handled.

### Refused By Both

11 document(s) produced neither text nor pages in LibreOffice and were refused by `broilerdoc` with a documented exit code - an encrypted package with no password given, or a part that is not well-formed. They are not ranked.

- [`odfexport/data/sha1_correct.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/odfexport/data/sha1_correct.odt): error: The ODT codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/odfexport/data/sha1_correct.odt": The ODT package is encrypted; this codec does not decrypt packages.
- [`odfexport/data/sha1_wrong.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/odfexport/data/sha1_wrong.odt): error: The ODT codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/odfexport/data/sha1_wrong.odt": The ODT package is encrypted; this codec does not decrypt packages.
- [`ooxmlexport/data/Encrypted_LO_Standard_abc.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/Encrypted_LO_Standard_abc.docx): error: The DOCX codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/ooxmlexport/data/Encrypted_LO_Standard_abc.docx": DOCX ZIP package could not be opened: InvalidDataException.
- [`ooxmlexport/data/Encrypted_MSO2007_abc.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/Encrypted_MSO2007_abc.docx): error: The DOCX codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/ooxmlexport/data/Encrypted_MSO2007_abc.docx": DOCX ZIP package could not be opened: InvalidDataException.
- [`ooxmlexport/data/Encrypted_MSO2010_abc.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/Encrypted_MSO2010_abc.docx): error: The DOCX codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/ooxmlexport/data/Encrypted_MSO2010_abc.docx": DOCX ZIP package could not be opened: InvalidDataException.
- [`ooxmlexport/data/Encrypted_MSO2013_abc.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/Encrypted_MSO2013_abc.docx): error: The DOCX codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/ooxmlexport/data/Encrypted_MSO2013_abc.docx": DOCX ZIP package could not be opened: InvalidDataException.
- [`ooxmlexport/data/tdf133771.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf133771.odt): error: The ODT codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/ooxmlexport/data/tdf133771.odt": The ODT package is encrypted; this codec does not decrypt packages.
- [`ooxmlimport/data/math-malformed_xml.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlimport/data/math-malformed_xml.docx): error: The DOCX codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/ooxmlimport/data/math-malformed_xml.docx": A DOCX XML part could not be parsed: XmlException.
- [`ooxmlimport/data/tdf165348_broken_package.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlimport/data/tdf165348_broken_package.docx): error: The DOCX codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/ooxmlimport/data/tdf165348_broken_package.docx": A DOCX XML part could not be parsed: XmlException.
- [`pdf/data/Hybrid_EmbeddedFileOnlyPDF20UAPasswordpop.pdf`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/pdf/data/Hybrid_EmbeddedFileOnlyPDF20UAPasswordpop.pdf): error: The PDF codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/pdf/data/Hybrid_EmbeddedFileOnlyPDF20UAPasswordpop.pdf": The document is protected by a user password, and the read supplied none. Nothing was decrypted.
- [`uiwriter/data/threadedException.fodt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/uiwriter/data/threadedException.fodt): error: The ODT codec rejected "/tmp/broilerdoc-office/79d2540e2f5d46709a4beda9d69886d5/extras-src/git-sparse/sw/qa/extras/uiwriter/data/threadedException.fodt": The flat ODT document could not be parsed: XmlException.

### Top 30 Major Differences

#### 1. [`layout/data/tdf123163-1.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/layout/data/tdf123163-1.docx) (`.docx`, severity `1641.71`)

- **Summary**: page count mismatch (13 vs 16); geometry off, ink box wild, profile poor, coverage close, blurred severe (blur diff 93.9%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **13** page(s) vs LibreOffice **16** page(s) (worst page: `2`)
- **Visual Bands**: `geometry off, ink box wild, profile poor, coverage close, blurred severe`
- **Visual Metrics**: blur diff `93.87%`, ink profile similarity `0.752`, coverage delta `0.38%`, ink box delta `blank-vs-inked`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: xxx xx xxxxxx xxxxxx xx
- broilerdoc: 2
- broilerdoc: xxxx xxx xxxxxxxxxx xx xxxx xx xxxx xxxxxxx xxxxxxx xxxxxx?
+ libreoffice: xxx xx xxxxxx xxxxxx xx 2 xxxx xxx xxxxxxxxxx xx xxxx xx xxxx xxxxxxx xxxxxxx xxxxxx?
- broilerdoc: 52
- broilerdoc: xxxxxxxxxx xxxxxxxx, xxxxxxx 2019
+ libreoffice: 52 xxxxxxxxxx xxxxxxxx, xxxxxxx 2019
- broilerdoc: xx xxxxxxx xxxxxxxx xxx xxxxxxxxxxxxx xxxxxxxxxxx, xxxxxxx xxxxxxx xxxxxxxx xxxx xx xxxx xxx xxxx xxxx xx xxxxxxx
- broilerdoc: xxxxxxxx
- broilerdoc: xxxxxxxxx. xxxxx xx xxxxx xxxxxxxx xxxxxxxx xx xxxx xx xxxxxx xxxxxxx xxxx xxx xxxxxxxxxx xxxxx xxxx. xxxx xx xxxx xxxxx ...
... and 11 more line(s)
```
- **Diagnostics**: `docx.columns.unequal`, `docx.image.missing`, `docx.image.shape`, `docx.section.columns`, `docx.table.rowheight`

#### 2. [`ooxmlexport/data/tdf163894_hidden.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf163894_hidden.docx) (`.docx`, severity `1474.11`)

- **Summary**: page count mismatch (4 vs 17); geometry match, ink box wild, profile poor, coverage poor, blurred severe (blur diff 34.3%)
- **Pagination**: `broilerdoc` rendered **4** page(s) vs LibreOffice **17** page(s) (worst page: `4`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `34.29%`, ink profile similarity `0.394`, coverage delta `13.37%`, ink box delta `1177 px`
- **Diagnostics**: `docx.header.even`, `docx.header.first`

#### 3. [`tiledrendering/data/page-background-link.fodt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/tiledrendering/data/page-background-link.fodt) (`.fodt`, severity `1311.56`)

- **Summary**: geometry match, ink box wild, profile poor, coverage poor, blurred severe (blur diff 99.8%)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **1** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `99.82%`, ink profile similarity `0.188`, coverage delta `99.81%`, ink box delta `1513 px`

#### 4. [`layout/data/floattable-center-shift-down.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/layout/data/floattable-center-shift-down.docx) (`.docx`, severity `1308.20`)

- **Summary**: page count mismatch (4 vs 3); geometry match, ink box wild, profile poor, coverage poor, blurred heavy (blur diff 15.2%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **4** page(s) vs LibreOffice **3** page(s) (worst page: `3`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage poor, blurred heavy`
- **Visual Metrics**: blur diff `15.22%`, ink profile similarity `0.000`, coverage delta `5.03%`, ink box delta `blank-vs-inked`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
- broilerdoc: x
... and 11 more line(s)
```

#### 5. [`ooxmlexport/data/ooo96040-2.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/ooo96040-2.odt) (`.odt`, severity `1249.52`)

- **Summary**: page count mismatch (14 vs 12); geometry match, ink box loose, profile poor, coverage fair, blurred heavy (blur diff 17.3%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **14** page(s) vs LibreOffice **12** page(s) (worst page: `4`)
- **Visual Bands**: `geometry match, ink box loose, profile poor, coverage fair, blurred heavy`
- **Visual Metrics**: blur diff `17.33%`, ink profile similarity `0.255`, coverage delta `3.56%`, ink box delta `116 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: 10. Nov 2008
+ libreoffice: Nov 10, 2008
- broilerdoc: ● Adding UI-Elements to your specification
- broilerdoc: ● Delete whole UI-Elemt descriptions
- broilerdoc: ● Hide or show field shadings
- broilerdoc: ● Switch Document Help ON or OFF
+ libreoffice: • Adding UI-Elements to your specification
+ libreoffice: • Delete whole UI-Elemt descriptions
+ libreoffice: • Hide or show field shadings
+ libreoffice: • Switch Document Help ON or OFF
... and 11 more line(s)
```
- **Diagnostics**: `odt.block.unsupported`, `odt.image.anchored`, `odt.image.format`, `odt.link`, `odt.revision.tracked`

#### 6. [`ooxmlexport/data/tdf127814.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf127814.docx) (`.docx`, severity `1249.13`)

- **Summary**: page count mismatch (1 vs 2); geometry match, ink box wild, profile poor, coverage fair, blurred heavy (blur diff 9.9%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **2** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage fair, blurred heavy`
- **Visual Metrics**: blur diff `9.94%`, ink profile similarity `0.199`, coverage delta `2.79%`, ink box delta `726 px`
- **Semantic Text Differences** (21 line(s)):

```diff
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
+ libreoffice: ‍
... and 11 more line(s)
```

#### 7. [`ooxmlexport/data/tdf163894.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf163894.docx) (`.docx`, severity `1235.25`)

- **Summary**: page count mismatch (4 vs 11); geometry match, ink box wild, profile poor, coverage poor, blurred severe (blur diff 32.6%)
- **Pagination**: `broilerdoc` rendered **4** page(s) vs LibreOffice **11** page(s) (worst page: `4`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `32.56%`, ink profile similarity `0.358`, coverage delta `12.79%`, ink box delta `1249 px`
- **Diagnostics**: `docx.header.even`, `docx.header.first`

#### 8. [`ooxmlexport/data/n830205.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/n830205.docx) (`.docx`, severity `1229.17`)

- **Summary**: geometry match, ink box wild, profile poor, coverage close, blurred light (blur diff 1.7%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **5** page(s) vs LibreOffice **5** page(s) (worst page: `4`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage close, blurred light`
- **Visual Metrics**: blur diff `1.68%`, ink profile similarity `0.007`, coverage delta `0.40%`, ink box delta `1279 px`
- **Semantic Text Differences** (21 line(s)):

```diff
+ libreoffice: XXX
- broilerdoc: BodyTextBodySpacePreserveBodyTextBodySpacePreserveBodyText
- broilerdoc: BodyTextBodySpacePreserveBodySpacePreserveBodyText
- broilerdoc: BodySpacePreserveBodySpacePreserveBodyText
+ libreoffice: BodyTextBodySpacePreserveBodyTextYYYSPBodySpacePreserveBodyTextYYYSPYYYSPXXXXXX
+ libreoffice: BodyTextBodySpacePreserveBodySpacePreserveYYYSPBodyTextYYYSPYYYSPXXXYYYSPYYYSPXXX
+ libreoffice: BodySpacePreserveBodySpacePreserveBodyTextYYYSPYYYSPYYYSPYYYSPXXX
- broilerdoc: 2.4.1 BodyTextBodyTextBodySpacePreserveBodySpacePreserveBodySpacePreserveBodySpacePreserveBodyTextBodySpacePreserveBodyS ...
+ libreoffice: 2.4.1 BodyTextBodyTextBodySpacePreserveBodySpacePreserveBodySpacePreserveBodySpacePreserveBodyTextBodySpacePreserveBodyS ...
- broilerdoc: 2.5 BodyTextBodySpacePreserveBodySpacePreserveBodySpacePreserveBodySpacePreserveBodySpacePreserveBodySpacePreserveBodyTe ...
... and 11 more line(s)
```
- **Diagnostics**: `docx.revision.delete`, `docx.section.columns`

#### 9. [`uiwriter/data/tdf130685.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/uiwriter/data/tdf130685.odt) (`.odt`, severity `1220.06`)

- **Summary**: geometry match, ink box wild, profile poor, coverage exact, blurred clean (blur diff 0.0%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **2** page(s) vs LibreOffice **2** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage exact, blurred clean`
- **Visual Metrics**: blur diff `0.02%`, ink profile similarity `0.000`, coverage delta `0.00%`, ink box delta `1333 px`
- **Semantic Text Differences** (21 line(s)):

```diff
+ libreoffice: • Past president of the Robotic Society of Southern California.
+ libreoffice: • Created autonomous robots for contests: using sensors, motors, and micro-controllers.
+ libreoffice: •
+ libreoffice: •
+ libreoffice: •
+ libreoffice: •
+ libreoffice: •
+ libreoffice: • Developed Machine Vision projects using RoboRealm software.
+ libreoffice: • Engineered integration and testing of subsystems.
+ libreoffice: • Developed mechanical hands with multiple grasp shapes and multiple sensors.
... and 11 more line(s)
```
- **Diagnostics**: `odt.revision.tracked`

#### 10. [`ooxmlexport/data/tdf162746.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf162746.docx) (`.docx`, severity `1204.31`)

- **Summary**: page count mismatch (2 vs 1); geometry match, ink box loose, profile poor, coverage poor, blurred heavy (blur diff 16.5%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **2** page(s) vs LibreOffice **1** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box loose, profile poor, coverage poor, blurred heavy`
- **Visual Metrics**: blur diff `16.50%`, ink profile similarity `0.218`, coverage delta `5.98%`, ink box delta `47 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: ◦ Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
- broilerdoc: ◦ Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem
+ libreoffice: • Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
+ libreoffice: • Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem
- broilerdoc: ◦ Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
- broilerdoc: ◦ Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
- broilerdoc: ◦ Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
- broilerdoc: ◦ Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
- broilerdoc: ◦ Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
+ libreoffice: • Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lorem Lore ...
... and 11 more line(s)
```
- **Diagnostics**: `docx.header.even`, `docx.header.first`, `docx.styles.unknown`, `docx.table.rowheight`, `docx.table.style`

#### 11. [`odfimport/data/tdf116195.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/odfimport/data/tdf116195.odt) (`.odt`, severity `1200.25`)

- **Summary**: geometry match, ink box wild, profile poor, coverage poor, blurred severe (blur diff 69.9%)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **1** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `69.85%`, ink profile similarity `0.000`, coverage delta `60.46%`, ink box delta `blank-vs-inked`

#### 12. [`ooxmlexport/data/wDateValueFormat.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/wDateValueFormat.docx) (`.docx`, severity `1196.58`)

- **Summary**: page count mismatch (16 vs 18); geometry match, ink box tight, profile poor, coverage exact, blurred severe (blur diff 26.1%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **16** page(s) vs LibreOffice **18** page(s) (worst page: `16`)
- **Visual Bands**: `geometry match, ink box tight, profile poor, coverage exact, blurred severe`
- **Visual Metrics**: blur diff `26.06%`, ink profile similarity `0.413`, coverage delta `0.03%`, ink box delta `5 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: 1.3 Status Filter-Strömungstest
+ libreoffice: 1.3
+ libreoffice: 2 Status Filter-Strömungstest
- broilerdoc: 2 Beschreibung der Räume
+ libreoffice: 3 Beschreibung der Räume
- broilerdoc: 3 Messdaten und Prüfergebnisse
- broilerdoc: 3.1 Raum: OP
- broilerdoc: 3.1.1 Reinraumklassenbestimmung
+ libreoffice: 4 Messdaten und Prüfergebnisse
+ libreoffice: 4.1 Raum: OP
... and 11 more line(s)
```
- **Diagnostics**: `docx.image.format`

#### 13. [`uiwriter/data/floattable-split.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/uiwriter/data/floattable-split.docx) (`.docx`, severity `1179.88`)

- **Summary**: page count mismatch (2 vs 3); geometry match, ink box wild, profile poor, coverage fair, blurred severe (blur diff 32.9%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **2** page(s) vs LibreOffice **3** page(s) (worst page: `2`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage fair, blurred severe`
- **Visual Metrics**: blur diff `32.93%`, ink profile similarity `0.632`, coverage delta `2.21%`, ink box delta `221 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: 1
- broilerdoc: One
- broilerdoc: Eins
- broilerdoc: 2
- broilerdoc: Two
- broilerdoc: Zwei
- broilerdoc: 3
- broilerdoc: Three
- broilerdoc: Drei
- broilerdoc: 4
... and 11 more line(s)
```
- **Diagnostics**: `docx.table.style`

#### 14. [`layout/data/tdf170477.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/layout/data/tdf170477.docx) (`.docx`, severity `1179.72`)

- **Summary**: page count mismatch (3 vs 5); geometry match, ink box loose, profile poor, coverage poor, blurred severe (blur diff 22.3%); 15 text difference(s)
- **Pagination**: `broilerdoc` rendered **3** page(s) vs LibreOffice **5** page(s) (worst page: `3`)
- **Visual Bands**: `geometry match, ink box loose, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `22.34%`, ink profile similarity `0.242`, coverage delta `14.85%`, ink box delta `119 px`
- **Semantic Text Differences** (15 line(s)):

```diff
- broilerdoc: Table3:A1 p1
- broilerdoc: Table5:A1 p1
- broilerdoc: Table5:A1 p2
- broilerdoc: Table5:A2 p1
- broilerdoc: Table5:A3 p1
- broilerdoc: Table5:A4 p1
- broilerdoc: Table5:A5 p1
- broilerdoc: Table5:A6 p1
- broilerdoc: Table9:A1 p1
- broilerdoc: Table9:A1 p2
... and 5 more line(s)
```
- **Diagnostics**: `docx.image.anchored`, `docx.table.floating`

#### 15. [`layout/data/section-nested-with-pagebreaks.fodt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/layout/data/section-nested-with-pagebreaks.fodt) (`.fodt`, severity `1171.24`)

- **Summary**: geometry match, ink box wild, profile poor, coverage poor, blurred severe (blur diff 70.5%)
- **Pagination**: `broilerdoc` rendered **4** page(s) vs LibreOffice **4** page(s) (worst page: `3`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `70.46%`, ink profile similarity `0.216`, coverage delta `68.67%`, ink box delta `1380 px`

#### 16. [`ooxmlimport/data/tdf136952_pgBreak3.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlimport/data/tdf136952_pgBreak3.docx) (`.docx`, severity `1161.88`)

- **Summary**: page count mismatch (6 vs 8); geometry off, ink box wild, profile poor, coverage fair, blurred heavy (blur diff 12.1%)
- **Pagination**: `broilerdoc` rendered **6** page(s) vs LibreOffice **8** page(s) (worst page: `6`)
- **Visual Bands**: `geometry off, ink box wild, profile poor, coverage fair, blurred heavy`
- **Visual Metrics**: blur diff `12.08%`, ink profile similarity `0.000`, coverage delta `1.64%`, ink box delta `441 px`
- **Diagnostics**: `docx.section.parity`

#### 17. [`ooxmlexport/data/mixednumberings.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/mixednumberings.docx) (`.docx`, severity `1161.73`)

- **Summary**: page count mismatch (6 vs 5); geometry match, ink box wild, profile poor, coverage exact, blurred light (blur diff 1.9%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **6** page(s) vs LibreOffice **5** page(s) (worst page: `5`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage exact, blurred light`
- **Visual Metrics**: blur diff `1.87%`, ink profile similarity `0.469`, coverage delta `0.15%`, ink box delta `448 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: 2.4 x
- broilerdoc: 2.5 x
- broilerdoc: 2.6 x
- broilerdoc: 3 x
+ libreoffice: 1.1 x
+ libreoffice: 1.2 x
+ libreoffice: 1.3 x
+ libreoffice: 2 x
+ libreoffice: 3 x
+ libreoffice: 3.1 xxxxxxx
... and 11 more line(s)
```
- **Diagnostics**: `docx.numbering.format`

#### 18. [`rtfimport/data/fdo45183.rtf`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/rtfimport/data/fdo45183.rtf) (`.rtf`, severity `1152.91`)

- **Summary**: geometry off, ink box wild, profile poor, coverage poor, blurred severe (blur diff 42.1%)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **1** page(s) (worst page: `1`)
- **Visual Bands**: `geometry off, ink box wild, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `42.09%`, ink profile similarity `0.358`, coverage delta `28.79%`, ink box delta `1205 px`
- **Diagnostics**: `rtf.embedded`

#### 19. [`uiwriter/data/tdf104492.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/uiwriter/data/tdf104492.docx) (`.docx`, severity `1152.00`)

- **Summary**: geometry match, ink box wild, profile poor, coverage close, blurred severe (blur diff 20.2%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **3** page(s) vs LibreOffice **3** page(s) (worst page: `3`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage close, blurred severe`
- **Visual Metrics**: blur diff `20.17%`, ink profile similarity `0.440`, coverage delta `0.79%`, ink box delta `292 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: o dghsdfghs
- broilerdoc: o Plouiklvs
+ libreoffice: • dghsdfghs
+ libreoffice: • Plouiklvs
- broilerdoc: o Atelier « Faire gfjgfjgfj sont rencontrées
- broilerdoc: o 15/gfjgfjgfjgfj du Lab
+ libreoffice: • Atelier « Faire gfjgfjgfj sont rencontrées
+ libreoffice: • 15/gfjgfjgfjgfj du Lab
- broilerdoc: o Fait : gfjque
- broilerdoc: o Fait : ghjgfj gfj h,
... and 11 more line(s)
```
- **Diagnostics**: `docx.image.shape`

#### 20. [`docbookexport/data/tdf91095.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/docbookexport/data/tdf91095.docx) (`.docx`, severity `1151.62`)

- **Summary**: geometry match, ink box loose, profile poor, coverage close, blurred heavy (blur diff 7.6%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **8** page(s) vs LibreOffice **8** page(s) (worst page: `6`)
- **Visual Bands**: `geometry match, ink box loose, profile poor, coverage close, blurred heavy`
- **Visual Metrics**: blur diff `7.62%`, ink profile similarity `0.051`, coverage delta `1.00%`, ink box delta `87 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: - L
- broilerdoc: - L
- broilerdoc: - L
- broilerdoc: - L
+ libreoffice: • L
+ libreoffice: • L
+ libreoffice: • L
+ libreoffice: • L
- broilerdoc: - L
- broilerdoc: - L
... and 11 more line(s)
```
- **Diagnostics**: `docx.table.style`

#### 21. [`uiwriter/data/tdf130680.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/uiwriter/data/tdf130680.odt) (`.odt`, severity `1144.05`)

- **Summary**: page count mismatch (1 vs 2); geometry match, ink box wild, profile poor, coverage fair, blurred severe (blur diff 21.3%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **2** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage fair, blurred severe`
- **Visual Metrics**: blur diff `21.25%`, ink profile similarity `0.703`, coverage delta `1.14%`, ink box delta `271 px`
- **Semantic Text Differences** (21 line(s)):

```diff
+ libreoffice: Exponential Functions
+ libreoffice: These all operate component-wise. The description is per component.
+ libreoffice: Syntax
+ libreoffice: Description
+ libreoffice: genType pow (genType x, genType y)
+ libreoffice: Returns x raised to the y power, i.e.,
+ libreoffice: Results are undefined if x < 0.
+ libreoffice: Results are undefined if x = 0 and y <= 0.
+ libreoffice: genType exp (genType x)
+ libreoffice: Returns the natural exponentiation of x, i.e., ex.
... and 11 more line(s)
```
- **Diagnostics**: `odt.image.format`, `odt.revision.tracked`

#### 22. [`ooxmlexport/data/tdf123705.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf123705.docx) (`.docx`, severity `1143.31`)

- **Summary**: geometry match, ink box wild, profile poor, coverage close, blurred light (blur diff 1.1%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **4** page(s) vs LibreOffice **4** page(s) (worst page: `4`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage close, blurred light`
- **Visual Metrics**: blur diff `1.15%`, ink profile similarity `0.102`, coverage delta `0.50%`, ink box delta `173 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: AM X PM
+ libreoffice:  AM X PM
- broilerdoc: Male Female Year of Birth
- broilerdoc: Adult (18 or older) Minor
+ libreoffice:  Male  Female Year of Birth
+ libreoffice:  Adult (18 or older)  Minor
- broilerdoc: Type of Incident Illness X Incident Injury Death
+ libreoffice: Type of Incident  Illness X Incident  Injury  Death
- broilerdoc: XNo Yes
+ libreoffice:  XNo  Yes
... and 11 more line(s)
```
- **Diagnostics**: `docx.link`, `docx.table.style`

#### 23. [`ooxmlexport/data/tdf95033.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf95033.docx) (`.docx`, severity `1141.35`)

- **Summary**: page count mismatch (2 vs 3); geometry off, ink box wild, profile poor, coverage poor, blurred heavy (blur diff 12.7%)
- **Pagination**: `broilerdoc` rendered **2** page(s) vs LibreOffice **3** page(s) (worst page: `2`)
- **Visual Bands**: `geometry off, ink box wild, profile poor, coverage poor, blurred heavy`
- **Visual Metrics**: blur diff `12.70%`, ink profile similarity `0.000`, coverage delta `5.50%`, ink box delta `blank-vs-inked`
- **Diagnostics**: `docx.table.style`

#### 24. [`ww8export/data/fdo66304-1.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ww8export/data/fdo66304-1.odt) (`.odt`, severity `1139.74`)

- **Summary**: page count mismatch (1 vs 2); geometry match, ink box wild, profile poor, coverage fair, blurred heavy (blur diff 16.0%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **2** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage fair, blurred heavy`
- **Visual Metrics**: blur diff `16.00%`, ink profile similarity `0.838`, coverage delta `1.11%`, ink box delta `488 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: Structures
+ libreoffice: Introduction
+ libreoffice: This document specifies only version 4.40 of the OpenGL Shading Language. It requires __VERSION__ to substitute 440, and ...
+ libreoffice: Previous versions of the OpenGL Shading Language, as well as the OpenGL ES Shading Language, are not strict subsets of t ...
+ libreoffice: All OpenGL Graphics System Specification references in this specification are to version 4.4.
+ libreoffice: 1.1 Acknowledgments
+ libreoffice: This specification is based on the work of those who contributed to past versions of the OpenGL Language Specification,  ...
+ libreoffice: Pat Brown, NVIDIA
+ libreoffice: Jeff Bolz, NVIDIA
+ libreoffice: Frank Chen
... and 11 more line(s)
```
- **Diagnostics**: `odt.revision.tracked`

#### 25. [`ooxmlexport/data/tdf132149_pgBreak.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf132149_pgBreak.odt) (`.odt`, severity `1135.72`)

- **Summary**: page count mismatch (6 vs 5); geometry off, ink box wild, profile poor, coverage close, blurred severe (blur diff 27.5%)
- **Pagination**: `broilerdoc` rendered **6** page(s) vs LibreOffice **5** page(s) (worst page: `2`)
- **Visual Bands**: `geometry off, ink box wild, profile poor, coverage close, blurred severe`
- **Visual Metrics**: blur diff `27.47%`, ink profile similarity `0.104`, coverage delta `0.33%`, ink box delta `418 px`

#### 26. [`ooxmlexport/data/tdf132149_pgBreakB.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf132149_pgBreakB.odt) (`.odt`, severity `1135.72`)

- **Summary**: page count mismatch (6 vs 5); geometry off, ink box wild, profile poor, coverage close, blurred severe (blur diff 27.5%)
- **Pagination**: `broilerdoc` rendered **6** page(s) vs LibreOffice **5** page(s) (worst page: `2`)
- **Visual Bands**: `geometry off, ink box wild, profile poor, coverage close, blurred severe`
- **Visual Metrics**: blur diff `27.47%`, ink profile similarity `0.104`, coverage delta `0.33%`, ink box delta `418 px`

#### 27. [`ooxmlexport/data/tdf97090.docx`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/ooxmlexport/data/tdf97090.docx) (`.docx`, severity `1131.03`)

- **Summary**: geometry match, ink box tight, profile poor, coverage poor, blurred severe (blur diff 30.2%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **1** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box tight, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `30.19%`, ink profile similarity `0.469`, coverage delta `6.09%`, ink box delta `1 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: год
- broilerdoc: альбом
- broilerdoc: лейбл
- broilerdoc: 1971
- broilerdoc: Meddle
- broilerdoc: Harvest
- broilerdoc: 1972
- broilerdoc: Live at Pompeii
- broilerdoc: Universal Home Video
- broilerdoc: 1992
... and 11 more line(s)
```
- **Diagnostics**: `docx.image.anchored`, `docx.table.style`

#### 28. [`layout/data/abi11870-2.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/layout/data/abi11870-2.odt) (`.odt`, severity `1129.70`)

- **Summary**: page count mismatch (32 vs 33); geometry match, ink box loose, profile poor, coverage close, blurred severe (blur diff 27.8%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **32** page(s) vs LibreOffice **33** page(s) (worst page: `21`)
- **Visual Bands**: `geometry match, ink box loose, profile poor, coverage close, blurred severe`
- **Visual Metrics**: blur diff `27.84%`, ink profile similarity `0.701`, coverage delta `0.98%`, ink box delta `100 px`
- **Semantic Text Differences** (21 line(s)):

```diff
+ libreoffice: Inhaltsverzeichnis
- broilerdoc: 12,1
+ libreoffice: 12.1
- broilerdoc: 12,2
+ libreoffice: 12.2
- broilerdoc: 12,3
+ libreoffice: 12.3
- broilerdoc: 12,4
+ libreoffice: 12.4
- broilerdoc: 12,5
... and 11 more line(s)
```
- **Diagnostics**: `odt.block.unsupported`, `odt.image.anchored`

#### 29. [`cjk/data/tdf115079.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/cjk/data/tdf115079.odt) (`.odt`, severity `1128.46`)

- **Summary**: page count mismatch (6 vs 8); geometry match, ink box wild, profile poor, coverage close, blurred severe (blur diff 24.4%); 21 text difference(s)
- **Pagination**: `broilerdoc` rendered **6** page(s) vs LibreOffice **8** page(s) (worst page: `6`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage close, blurred severe`
- **Visual Metrics**: blur diff `24.37%`, ink profile similarity `0.838`, coverage delta `0.44%`, ink box delta `136 px`
- **Semantic Text Differences** (21 line(s)):

```diff
- broilerdoc: ◆ ログローテート設定
- broilerdoc: ► /etc/logrotate.d/syslog
+ libreoffice: • ログローテート設定
+ libreoffice: • /etc/logrotate.d/syslog
- broilerdoc: ► /etc/logrotate.d/httpd
+ libreoffice: • /etc/logrotate.d/httpd
- broilerdoc: ► /etc/logrotate.d/vsftpd
+ libreoffice: • /etc/logrotate.d/vsftpd
- broilerdoc: ► /etc/logrotate.d/abc_trashmail
+ libreoffice: • /etc/logrotate.d/abc_trashmail
... and 11 more line(s)
```

#### 30. [`layout/data/tdf138039.odt`](https://github.com/LibreOffice/core/blob/master/sw/qa/extras/layout/data/tdf138039.odt) (`.odt`, severity `1123.74`)

- **Summary**: page count mismatch (1 vs 3); geometry match, ink box wild, profile poor, coverage poor, blurred severe (blur diff 31.6%)
- **Pagination**: `broilerdoc` rendered **1** page(s) vs LibreOffice **3** page(s) (worst page: `1`)
- **Visual Bands**: `geometry match, ink box wild, profile poor, coverage poor, blurred severe`
- **Visual Metrics**: blur diff `31.55%`, ink profile similarity `0.000`, coverage delta `29.39%`, ink box delta `blank-vs-inked`
- **Diagnostics**: `odt.image.anchored`

