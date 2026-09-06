#!/usr/bin/env bash
# run-office-conformance.sh - Provision LibreOffice and poppler, materialise the
# pinned font set, and run the office conformance suite against them.
#
# LibreOffice writes the documents, broilerdoc reads them, and the disagreements
# are held against tests/office/office-baseline.json. See
# docs/office-conformance.md for what that does and does not prove.
#
# Usage:
#     ./eng/run-office-conformance.sh [OPTIONS]
#
# Options:
#     --output-dir <dir>       Where the log and reports go (default: ./test-results)
#     --axis <kind>            semantic, pixel, or all (default: all)
#     --only <substring>       Run only documents whose seed/format key contains this
#     --jobs <n>               Concurrent documents
#     --dpi <n>                Resolution for both rasterisers (default: 144)
#     --font-dir <dir>         Pin a font directory. Repeatable
#     --skip-install           Trust whatever soffice and pdftoppm are on PATH
#     --require-pinned-toolchain
#                              Stop with exit 69 when the installed LibreOffice is
#                              not the expected version. CI passes this
#     --allow-pending-tools    Drive tools whose register row is not yet approved
#     --update-baseline        Regenerate the baseline from this run
#     --restamp                Permit --update-baseline to change the toolchain stamp
#     --font-set <name>        The name the baseline stamp records for the pinned fonts
#     --keep                   Keep the workspace and print where it is
#     --verbose                Name every skipped check under its grouped reason
#     -h, --help               Show this help and exit 0
#
# Environment:
#     BROILER_SOFFICE                          Path to the LibreOffice binary
#     BROILER_PDFTOPPM                         Path to the poppler rasteriser
#     BROILER_OFFICE_FONT_DIR                  Default pinned font directory
#     BROILER_OFFICE_EXPECTED_VERSION          The LibreOffice version the baseline was made on
#     BROILER_OFFICE_INSTALL_RETRIES           Default 3
#     BROILER_OFFICE_INSTALL_RETRY_DELAY_SECONDS
#                                              Default 15
#
# Prerequisites:
#     - .NET 10 SDK
#     - On Linux, apt-get for the provisioning path. On Windows and macOS the
#       script does not install anything and expects the tools to be present.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

OUTPUT_DIR="$REPO_ROOT/test-results"
AXIS="all"
ONLY=""
JOBS=""
DPI="144"
FONT_DIRS=()
SKIP_INSTALL=false
REQUIRE_PINNED=false
ALLOW_PENDING=false
UPDATE_BASELINE=false
RESTAMP=false
FONT_SET="${BROILER_OFFICE_FONT_SET:-}"
KEEP=false
VERBOSE=false

INSTALL_RETRIES="${BROILER_OFFICE_INSTALL_RETRIES:-3}"
INSTALL_RETRY_DELAY="${BROILER_OFFICE_INSTALL_RETRY_DELAY_SECONDS:-15}"
EXPECTED_VERSION="${BROILER_OFFICE_EXPECTED_VERSION:-}"

# sysexits.h EX_UNAVAILABLE. Deliberately distinct from the runner's own exit
# code, which is its failure count: a run that never started a check must not be
# mistaken for a run that found sixty-nine failures.
EXIT_TOOLCHAIN_UNAVAILABLE=69

REASON_LO_INSTALL_FAILED="LibreOfficeInstallFailed"
REASON_LO_VERSION_MISMATCH="LibreOfficeVersionMismatch"
REASON_RASTERIZER_MISSING="RasterizerInstallFailed"

usage() {
    # To the last line of the leading comment block, found rather than counted:
    # a hard-coded range goes stale the first time an option is added, and it
    # goes stale by truncating the help rather than by failing.
    sed -n '2,/^$/p' "${BASH_SOURCE[0]}" | sed 's/^#\{1,\} \{0,1\}//'
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output-dir) OUTPUT_DIR="$2"; shift 2 ;;
        --axis) AXIS="$2"; shift 2 ;;
        --only) ONLY="$2"; shift 2 ;;
        --jobs) JOBS="$2"; shift 2 ;;
        --dpi) DPI="$2"; shift 2 ;;
        --font-dir) FONT_DIRS+=("$2"); shift 2 ;;
        --skip-install) SKIP_INSTALL=true; shift ;;
        --require-pinned-toolchain) REQUIRE_PINNED=true; shift ;;
        --allow-pending-tools) ALLOW_PENDING=true; shift ;;
        --update-baseline) UPDATE_BASELINE=true; shift ;;
        --restamp) RESTAMP=true; shift ;;
        --font-set) FONT_SET="$2"; shift 2 ;;
        --keep) KEEP=true; shift ;;
        --verbose) VERBOSE=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 1 ;;
    esac
done

if ! [[ "$INSTALL_RETRIES" =~ ^[1-9][0-9]*$ ]]; then
    echo "  ✗ BROILER_OFFICE_INSTALL_RETRIES must be a positive integer, got '$INSTALL_RETRIES'" >&2
    exit 1
fi

mkdir -p "$OUTPUT_DIR"
LOG_FILE="$OUTPUT_DIR/office-results.log"
JSON_FILE="$OUTPUT_DIR/office-results.json"
JUNIT_FILE="$OUTPUT_DIR/office-results.junit.xml"
SUMMARY_FILE="$OUTPUT_DIR/office-summary.txt"
FAILURE_MARKER="$OUTPUT_DIR/office-toolchain-failure.json"

# Removed at startup so a marker left by an earlier run can never be attributed
# to this one. A stale marker is worse than no marker: it names a cause that did
# not happen.
rm -f "$FAILURE_MARKER"

echo "=== Broiler.Documents Office Conformance Runner ==="
echo "Repository root : $REPO_ROOT"
echo "Output directory: $OUTPUT_DIR"
echo "Log file        : $LOG_FILE"
echo ""

# ---------------------------------------------------------------------------
# Provisioning helpers, ported from the WPT runner's browser install library.
# ---------------------------------------------------------------------------

# Writes the marker that tells a merge step why a run never measured anything.
write_failure_marker() {
    local reason="$1"
    local detail="$2"
    local escaped
    escaped="$(printf '%s' "$detail" | sed 's/\\/\\\\/g; s/"/\\"/g' | tr '\n' ' ')"
    printf '{"reason":"%s","detail":"%s"}\n' "$reason" "$escaped" > "$FAILURE_MARKER"
}

# True when the log names a cause that retrying will not fix.
install_is_blocked() {
    local log_file="$1"
    [[ -f "$log_file" ]] || return 1
    grep -qiE '403 +Forbidden|Unable to locate package|Hash Sum mismatch|Could not resolve|Temporary failure resolving' "$log_file"
}

# Retries a command, tailing the log of the last attempt on failure.
#
# The status capture is deliberate and was a real bug in the runner this is
# ported from: reading $? after an `if cmd; then` compound reports the status of
# the compound, not of the command, so every failed attempt looked like a
# success. Capture it in the same statement that runs the command.
run_with_retries() {
    local description="$1"; shift
    local log_file
    log_file="$(mktemp)"
    local attempt=1
    local delay="$INSTALL_RETRY_DELAY"

    while (( attempt <= INSTALL_RETRIES )); do
        local status=0
        "$@" >"$log_file" 2>&1 || status=$?

        if (( status == 0 )); then
            rm -f "$log_file"
            return 0
        fi

        if install_is_blocked "$log_file"; then
            echo "  ✗ $description is blocked rather than flaky; not retrying." >&2
            sed 's/^/      /' "$log_file" >&2 | tail -20
            cp "$log_file" "$OUTPUT_DIR/office-install.log" 2>/dev/null || true
            rm -f "$log_file"
            return 1
        fi

        echo "  ⚠ $description failed (attempt $attempt of $INSTALL_RETRIES, exit $status)" >&2
        if (( attempt < INSTALL_RETRIES )); then
            sleep "$delay"
            delay=$(( delay * 2 ))
        fi
        attempt=$(( attempt + 1 ))
    done

    tail -20 "$log_file" | sed 's/^/      /' >&2
    cp "$log_file" "$OUTPUT_DIR/office-install.log" 2>/dev/null || true
    rm -f "$log_file"
    return 1
}

find_soffice() {
    if [[ -n "${BROILER_SOFFICE:-}" && -x "${BROILER_SOFFICE}" ]]; then
        echo "$BROILER_SOFFICE"; return 0
    fi
    # soffice.com and never soffice.exe on Windows: the .exe is a GUI-subsystem
    # program that detaches, never terminates and prints nothing.
    local candidate
    for candidate in soffice.com soffice; do
        if command -v "$candidate" >/dev/null 2>&1; then
            command -v "$candidate"; return 0
        fi
    done
    for candidate in \
        "/c/Program Files/LibreOffice/program/soffice.com" \
        "/c/Program Files (x86)/LibreOffice/program/soffice.com" \
        "/usr/lib/libreoffice/program/soffice" \
        "/Applications/LibreOffice.app/Contents/MacOS/soffice"; do
        if [[ -x "$candidate" ]]; then
            echo "$candidate"; return 0
        fi
    done
    return 1
}

# ---------------------------------------------------------------------------

echo "--- Step 1: Provisioning LibreOffice and poppler ---"

if [[ "$SKIP_INSTALL" == "true" ]]; then
    echo "  ⏭ --skip-install given; using whatever is on PATH."
elif [[ "$(uname -s)" == Linux* ]]; then
    if command -v soffice >/dev/null 2>&1 && command -v pdftoppm >/dev/null 2>&1; then
        echo "  ✓ LibreOffice and poppler are already present."
    elif command -v apt-get >/dev/null 2>&1; then
        # libreoffice-writer and not libreoffice: the full meta-package is about
        # three times the size and Calc, Impress and Draw are never used here.
        # Not libreoffice-core either - the Writer import and export filters live
        # in the Writer package, and core alone cannot convert a .docx at all.
        SUDO=""
        [[ "$(id -u)" != "0" ]] && SUDO="sudo"
        if ! run_with_retries "apt-get update" $SUDO apt-get update; then
            write_failure_marker "$REASON_LO_INSTALL_FAILED" "apt-get update failed"
            echo "  ✗ Could not refresh the package lists." >&2
            exit "$EXIT_TOOLCHAIN_UNAVAILABLE"
        fi
        if ! run_with_retries "apt-get install" $SUDO apt-get install -y --no-install-recommends \
            libreoffice-writer poppler-utils \
            fonts-liberation2 fonts-crosextra-carlito fonts-crosextra-caladea fonts-dejavu-core; then
            write_failure_marker "$REASON_LO_INSTALL_FAILED" "apt-get install failed"
            echo "  ✗ Could not install LibreOffice and poppler." >&2
            exit "$EXIT_TOOLCHAIN_UNAVAILABLE"
        fi
        echo "  ✓ Installed LibreOffice, poppler and the pinned font packages."
    else
        echo "  ⚠ No apt-get on this host; nothing was installed."
    fi
else
    echo "  ⏭ Not Linux; this script does not install anything here."
fi

SOFFICE="$(find_soffice || true)"
if [[ -z "$SOFFICE" ]]; then
    write_failure_marker "$REASON_LO_INSTALL_FAILED" "no soffice binary found after provisioning"
    echo "  ✗ LibreOffice is not available." >&2
    exit "$EXIT_TOOLCHAIN_UNAVAILABLE"
fi
echo "  ✓ LibreOffice: $SOFFICE"

if ! command -v pdftoppm >/dev/null 2>&1 && [[ -z "${BROILER_PDFTOPPM:-}" ]]; then
    if [[ "$AXIS" == "pixel" ]]; then
        write_failure_marker "$REASON_RASTERIZER_MISSING" "pdftoppm not found and --axis pixel was asked for"
        echo "  ✗ --axis pixel needs pdftoppm and it is not installed." >&2
        exit "$EXIT_TOOLCHAIN_UNAVAILABLE"
    fi
    # Not fatal for the default axis. The semantic half is the one that carries
    # most of the value, and it needs no rasteriser at all; the pixel checks
    # report themselves as skipped with the reason.
    echo "  ⚠ pdftoppm not found; the pixel axis will report itself skipped."
else
    echo "  ✓ Rasteriser: ${BROILER_PDFTOPPM:-$(command -v pdftoppm)}"
fi

echo ""
echo "--- Step 2: Checking the LibreOffice version against the baseline ---"

# `--version` on soffice.com exits 0 and prints one line. It is checked here
# rather than inside the suite because a version mismatch is a fact about
# provisioning, and the suite's own response to one is to skip every comparison
# rather than to fail - which is right for a developer and wrong for CI.
LO_VERSION="$("$SOFFICE" --version 2>/dev/null | head -1 | tr -d '\r' || true)"
echo "  installed: ${LO_VERSION:-unknown}"
if [[ -n "$EXPECTED_VERSION" ]]; then
    if [[ "$LO_VERSION" == *"$EXPECTED_VERSION"* ]]; then
        echo "  ✓ matches the expected $EXPECTED_VERSION"
    elif [[ "$REQUIRE_PINNED" == "true" ]]; then
        write_failure_marker "$REASON_LO_VERSION_MISMATCH" \
            "expected $EXPECTED_VERSION, found ${LO_VERSION:-unknown}"
        echo "  ✗ expected $EXPECTED_VERSION. A baseline generated on one LibreOffice is not" >&2
        echo "    evidence about another: layout moves between releases." >&2
        exit "$EXIT_TOOLCHAIN_UNAVAILABLE"
    else
        echo "  ⚠ expected $EXPECTED_VERSION; the suite will skip its comparisons and say so."
    fi
else
    echo "  ⏭ no expected version set."
fi

echo ""
echo "--- Step 3: Materialising the pinned font set ---"

if [[ ${#FONT_DIRS[@]} -eq 0 && -n "${BROILER_OFFICE_FONT_DIR:-}" ]]; then
    FONT_DIRS+=("$BROILER_OFFICE_FONT_DIR")
fi

if [[ ${#FONT_DIRS[@]} -eq 0 ]]; then
    # Without a pinned set LibreOffice draws with the host's fonts and so does
    # broilerdoc, and the two disagree about a document neither of them got
    # wrong. The suite reports that rather than pretending otherwise.
    echo "  ⚠ No font directory pinned. The pixel axis will measure this machine's fonts."
else
    for directory in "${FONT_DIRS[@]}"; do
        if [[ -d "$directory" ]]; then
            echo "  ✓ $directory"
        else
            echo "  ⚠ $directory does not exist"
        fi
    done
fi

echo ""
echo "--- Step 4: Building the office conformance suite ---"

# `-c Release` and not a solution configuration: Broiler.Documents.slnx maps
# every solution configuration to the project configuration `Release`, so this
# is where a solution build already put the output.
if ! dotnet build "$REPO_ROOT/src/tests/Broiler.Documents.Office/Broiler.Documents.Office.csproj" \
        -c Release --nologo -v quiet; then
    echo "  ✗ The suite did not build." >&2
    exit 1
fi
echo "  ✓ Built."

echo ""
echo "--- Step 5: Running the office conformance suite ---"

RUNNER_ARGS=(
    --report "$JSON_FILE"
    --junit "$JUNIT_FILE"
    --axis "$AXIS"
    --dpi "$DPI"
    --soffice "$SOFFICE"
)

[[ -n "$ONLY" ]] && RUNNER_ARGS+=(--only "$ONLY")
[[ -n "$JOBS" ]] && RUNNER_ARGS+=(--jobs "$JOBS")
[[ -n "${BROILER_PDFTOPPM:-}" ]] && RUNNER_ARGS+=(--pdftoppm "$BROILER_PDFTOPPM")
[[ "$ALLOW_PENDING" == "true" ]] && RUNNER_ARGS+=(--allow-pending-tools)
[[ "$UPDATE_BASELINE" == "true" ]] && RUNNER_ARGS+=(--update-baseline)
[[ "$RESTAMP" == "true" ]] && RUNNER_ARGS+=(--restamp)
# Forwarded whenever it is set, and not only when regenerating: the baseline
# stamp records the font set, and a run that did not name one can never match a
# baseline that did. Leaving it out made the documented adoption sequence produce
# a baseline nothing could ever be compared against.
[[ -n "$FONT_SET" ]] && RUNNER_ARGS+=(--font-set "$FONT_SET")
[[ "$KEEP" == "true" ]] && RUNNER_ARGS+=(--keep)
[[ "$VERBOSE" == "true" ]] && RUNNER_ARGS+=(--verbose)

# "${FONT_DIRS[@]:-}" on an empty array expands to one empty string under set -u,
# which would reach the runner as a --font-dir with no value and be rejected as a
# usage error. Guard on the length instead.
if [[ ${#FONT_DIRS[@]} -gt 0 ]]; then
    for directory in "${FONT_DIRS[@]}"; do
        [[ -n "$directory" ]] && RUNNER_ARGS+=(--font-dir "$directory")
    done
fi

# No `--nologo`: `dotnet run` forwards an option it does not recognise to the
# application, and this runner rejects unknown options rather than ignoring them.
#
# `set +e` around the run, because the runner exits with its failure count and
# `set -e` would kill the script before the output is written - a failing run
# would then arrive with no failures in it. Judge after echoing, not instead.
set +e
dotnet run --project "$REPO_ROOT/src/tests/Broiler.Documents.Office" \
    -c Release --no-build -- "${RUNNER_ARGS[@]}" 2>&1 | tee "$LOG_FILE"
RUNNER_STATUS="${PIPESTATUS[0]}"
set -e

echo ""
echo "=== Office Conformance Run Complete ==="
echo ""
echo "--- Summary ---"

{
    grep -E '^(tool|libreoffice|rasterizer|fonts|corpus|axis|baseline) ' "$LOG_FILE" || true
    echo ""
    sed -n '/^by group$/,$p' "$LOG_FILE" || true
} | tee "$SUMMARY_FILE"

echo ""
if (( RUNNER_STATUS == 99 )); then
    echo "The suite could not run: exit 99 is a usage error or a failure of the harness,"
    echo "never a verdict about the component."
elif (( RUNNER_STATUS == 0 )); then
    echo "Clean."
else
    echo "$RUNNER_STATUS check(s) failed. See $LOG_FILE."
fi

echo ""
echo "Reports:"
echo "  $LOG_FILE"
echo "  $JSON_FILE"
echo "  $JUNIT_FILE"
echo "  $SUMMARY_FILE"
echo ""
echo "=== Done ==="

# The runner's exit code is its failure count, and this script's 69 means the
# toolchain could not be provisioned. Exactly 69 failures would otherwise be read
# by a caller - and by the workflow - as a run that never started, which is the
# one distinction the two codes exist to keep. 68 is a lie by one; 1 is a lie
# about the count. So the count is capped below the sentinel and the real number
# stays in the summary and the JSON report, where nothing has to encode it in a
# byte.
if (( RUNNER_STATUS == EXIT_TOOLCHAIN_UNAVAILABLE )); then
    echo "Note: the run reported exactly $EXIT_TOOLCHAIN_UNAVAILABLE failures, which is this"
    echo "script's code for an unprovisionable toolchain. Reporting 68 so the two stay apart;"
    echo "the real count is in the summary above."
    RUNNER_STATUS=68
fi

exit "$RUNNER_STATUS"
