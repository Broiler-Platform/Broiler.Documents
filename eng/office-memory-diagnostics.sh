#!/usr/bin/env bash
# Best-effort Linux diagnostics. Never print process arguments or environment
# values: either may contain credentials. RSS values are KiB, cgroup values bytes.
set -u

snapshot() {
    echo "=== Office memory snapshot: $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
    echo 'Host memory and swap (KiB; not necessarily the container allowance):'
    grep -E '^(MemTotal|MemAvailable|SwapTotal|SwapFree|CommitLimit|Committed_AS):' /proc/meminfo || true
    echo 'Memory pressure:'
    cat /proc/pressure/memory 2>/dev/null || true
    echo 'Process cgroup membership and mounts:'
    cat /proc/self/cgroup 2>/dev/null || true
    findmnt -rn -t cgroup,cgroup2 -o TARGET,FSROOT,OPTIONS 2>/dev/null || true
    # Walk every visible ancestor. Limits above a cgroup namespace root cannot
    # be inspected here; the host-side Docker inspection complements this.
    local membership root directory name
    membership=$(awk -F: '$1 == "0" { print $3 }' /proc/self/cgroup)
    root=/sys/fs/cgroup
    directory="$root${membership:-/}"
    [[ -d "$directory" ]] || directory="$root"
    while [[ "$directory" == "$root"* ]]; do
        echo "Cgroup: $directory"
        for name in memory.max memory.high memory.current memory.peak memory.swap.max memory.swap.current memory.events memory.events.local memory.stat pids.current pids.max pids.events cpu.max cpu.stat; do
            if [[ -r "$directory/$name" ]]; then
                echo "  $name:"
                cat "$directory/$name" || true
            fi
        done
        [[ "$directory" == "$root" || "$directory" == "$root/" ]] && break
        directory=$(dirname "$directory")
    done
    # Legacy cgroup v1 runners.
    for root in /sys/fs/cgroup/memory /sys/fs/cgroup/pids; do
        [[ -d "$root" ]] || continue
        membership=$(awk -F: -v controller="${root##*/}" '("," $2 ",") ~ ("," controller ",") { print $3 }' /proc/self/cgroup)
        directory="$root${membership:-/}"
        [[ -d "$directory" ]] || directory="$root"
        while [[ "$directory" == "$root"* ]]; do
            echo "Legacy cgroup: $directory"
            for name in memory.limit_in_bytes memory.usage_in_bytes memory.max_usage_in_bytes memory.failcnt memory.oom_control memory.memsw.limit_in_bytes pids.current pids.max pids.events; do
                [[ ! -r "$directory/$name" ]] || { echo "  $name:"; cat "$directory/$name"; }
            done
            [[ "$directory" == "$root" || "$directory" == "$root/" ]] && break
            directory=$(dirname "$directory")
        done
    done
    echo 'Resource limits:'
    cat /proc/self/limits 2>/dev/null || true
    echo 'Largest processes (RSS KiB; command names only):'
    ps -eo pid,ppid,nlwp,rss,vsz,comm --sort=-rss | head -n 21 || true
    echo 'Visible process and thread totals:'
    ps -e --no-headers | wc -l
    ps -eLf --no-headers | wc -l
}

if [[ "${1:-}" != -- ]]; then
    snapshot
    exit 0
fi
shift
if (( $# == 0 )); then
    echo 'Usage: office-memory-diagnostics.sh [-- command arguments...]' >&2
    exit 2
fi

mkdir -p "${GITHUB_WORKSPACE:-.}/test-results"
log="${GITHUB_WORKSPACE:-.}/test-results/office-memory.log"
snapshot 2>&1 | tee "$log"
monitor_pid=''
cleanup() {
    if [[ -n "$monitor_pid" ]]; then
        kill "$monitor_pid" 2>/dev/null || true
        wait "$monitor_pid" 2>/dev/null || true
    fi
    snapshot 2>&1 | tee -a "$log"
}
trap cleanup EXIT
# Waiting on a child sleep allows TERM to stop the monitor immediately.
(
    sleeper=''
    trap '[[ -z "$sleeper" ]] || kill "$sleeper" 2>/dev/null; exit 0' TERM INT
    while true; do
        sleep 60 & sleeper=$!
        wait "$sleeper" || break
        snapshot
    done
) >> "$log" 2>&1 &
monitor_pid=$!
# Mirror periodic snapshots to the Actions log as well as the report artifact.
tail -n 0 -f "$log" &
tail_pid=$!
trap 'kill "$tail_pid" 2>/dev/null || true; cleanup' EXIT
"$@"
status=$?
exit "$status"
