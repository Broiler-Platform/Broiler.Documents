# RTF inherited-size export verification — 2026-10-20

The selected defect is loss of a nondefault `DocumentStyleDefaults.FontSizePoints`
on RTF export. Runs with no explicit size emitted no `\fs` control and therefore
reopened at RTF's 12-point reset size. An 18-point document could change size
through export while reporting no loss.

The writer now materializes a non-12-point inherited size on each affected run
in body paragraphs, headers/footers and shape text. Explicit sizes retain
precedence. Import preserves the effective size as an explicit run property;
it does not reconstruct the original inheritance arrangement. This follows the
normalized model's semantic round-trip goal. Sizes remain rounded to RTF's
existing half-point precision.

Three new regression cases exercise body, header and shape text with an
inherited 18-point size and an explicit 9-point override. All three were observed
failing before the correction and pass after it. All 270 RTF tests pass.

The CLI suite could not start afterward: the x64 .NET 10 runtime that ran the
RTF suite was no longer installed, leaving only x64 .NET 8. The installed
10.0.100-rc.1 SDK then failed to launch. No machine configuration was changed.
`git diff --check` reports no whitespace errors.

Follow-up: the [literal font-name bytes verification](rtf-literal-font-bytes-verification-2026-10-20.md)
records a successful 235-test CLI run using an isolated cached runtime, completing
that blocked validation.

This fixes an export fidelity defect and does not change office-baseline
classifications. The existing office fixture defaults are 12 points, so this
change does not claim to resolve their remaining render differences.
