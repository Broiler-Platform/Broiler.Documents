# HTML text and attribute decoding fixes — 2026-10-20

The two selected defects shared a redundant decoding step:

1. **Text content was decoded twice.** The DOM parser already resolves HTML
   character references. The codec decoded its text nodes again, so literal
   entity spellings changed content. For example, `&amp;lt;tag&amp;gt;` became
   `<tag>` instead of `&lt;tag&gt;`. Removing the second pass preserves normal
   and preformatted text and restores writer/reader round trips.
2. **Attribute values were decoded twice.** Links, legacy font attributes and
   CSS declarations received a second entity-decoding pass after DOM parsing.
   This changed URL query values and font-family names. They now consume the
   already decoded attribute values. The CSS parser also leaves raw stylesheet
   text alone, where an HTML entity spelling is ordinary CSS string content.

Eight new regressions cover named/numeric entity text, preformatted text, a
model → HTML → model round trip, link targets, legacy font attributes, inline
CSS and type-rule CSS. All eight failed before the changes and pass after them.
Existing single-encoded entity tests continue to pass.

All 160 HTML tests and 235 CLI tests pass using the isolated cached .NET runtime.
`git diff --check` reports no whitespace errors. These codec corrections do not
reclassify the remaining office render rows.
