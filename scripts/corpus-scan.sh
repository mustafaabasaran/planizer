#!/usr/bin/env bash
# Corpus scan: run the Release CLI over one or more directories of real migrations and print
# finding counts per rule x severity plus a few sample statements per rule. Used to triage
# false positives after a rule or parser change (see the false-positive rounds in CLAUDE.md).
#
# Usage:
#   scripts/corpus-scan.sh DIR [DIR...]
#
#   PLANIZER_CLI       path to Planizer.Cli.dll (default: build src/Planizer.Cli -c Release and use it)
#   PLANIZER_SCAN_OUT  directory for the per-dir JSON reports + summary.tsv (default: mktemp -d)
#   PLANIZER_ARGS      extra CLI arguments, word-split (e.g. "--target-version 2016 --edition enterprise")
#   PLANIZER_SAMPLES   sample statements printed per rule (default 3)
#
# zsh callers: a variable holding several directories is NOT word-split by default, so use an
# array ( dirs=(a b c); scripts/corpus-scan.sh "${dirs[@]}" ) or force splitting with ${=dirs}.
# Example over every migrations folder under a source tree:
#   scripts/corpus-scan.sh $(find ~/src -type d -path '*/Migration/MsSql' \
#       -not -path '*/bin/*' -not -path '*/obj/*')
#
# Exit code 1 from the CLI means "findings at or above --fail-on" and is expected; only exit
# code 2 (tool error) is reported as a failure.

set -euo pipefail

if [ "$#" -eq 0 ]; then
    sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

cli="${PLANIZER_CLI:-}"
if [ -z "$cli" ]; then
    echo "building src/Planizer.Cli (Release)..." >&2
    dotnet build "$repo_root/src/Planizer.Cli" -c Release --nologo -v quiet >&2
    cli="$repo_root/src/Planizer.Cli/bin/Release/net8.0/Planizer.Cli.dll"
fi
if [ ! -f "$cli" ]; then
    echo "corpus-scan: CLI not found at $cli" >&2
    exit 2
fi

out="${PLANIZER_SCAN_OUT:-}"
if [ -z "$out" ]; then
    out="$(mktemp -d "${TMPDIR:-/tmp}/planizer-scan.XXXXXX")"
fi
mkdir -p "$out"

# shellcheck disable=SC2206  # PLANIZER_ARGS is deliberately word-split
extra_args=(${PLANIZER_ARGS:-})

echo "cli:    $cli" >&2
echo "output: $out" >&2

failures=0
for dir in "$@"; do
    if [ ! -d "$dir" ]; then
        echo "corpus-scan: skipping $dir (not a directory)" >&2
        failures=$((failures + 1))
        continue
    fi

    abs="$(cd "$dir" && pwd)"
    # /home/me/src/Shop/Migration/MsSql -> home_me_src_Shop_Migration_MsSql (unique, filesystem-safe)
    name="$(printf '%s' "${abs#/}" | tr '/' '_' | sed 's/__*/_/g')"
    json="$out/$name.json"

    files="$(find "$abs" -type f -iname '*.sql' | wc -l | tr -d ' ')"
    printf '%-60s %5s files ... ' "$dir" "$files" >&2

    set +e
    # ${arr[@]+"${arr[@]}"}: an empty array is "unbound" under set -u on bash 3.2 (macOS)
    dotnet "$cli" analyze "$abs" --output json ${extra_args[@]+"${extra_args[@]}"} > "$json" 2> "$out/$name.stderr"
    code=$?
    set -e

    if [ "$code" -ge 2 ]; then
        echo "FAILED (exit $code): $(head -c 300 "$out/$name.stderr")" >&2
        failures=$((failures + 1))
        rm -f "$json"
        continue
    fi

    findings="$(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))["findings"]))' "$json")"
    echo "$findings findings (exit $code)" >&2
done

python3 - "$out" "${PLANIZER_SAMPLES:-3}" <<'PY'
import glob
import json
import os
import sys
from collections import Counter, defaultdict

out, samples_per_rule = sys.argv[1], int(sys.argv[2])
severity_order = {"Info": 0, "Warning": 1, "Critical": 2, "Blocker": 3}

files_total = 0
summary_totals = Counter()
counts = Counter()            # (rule, severity) -> n
suppressed = Counter()        # rule -> n
inconclusive = Counter()      # rule -> n
samples = defaultdict(list)   # rule -> [(file, line, summary, message)]
sample_files = defaultdict(set)
per_dir = []

for path in sorted(glob.glob(os.path.join(out, "*.json"))):
    with open(path, encoding="utf-8") as handle:
        report = json.load(handle)

    files_total += len(report.get("files", []))
    for key, value in (report.get("summary") or {}).items():
        if isinstance(value, (int, float)) and not isinstance(value, bool):
            summary_totals[key] += value

    findings = report.get("findings", [])
    per_dir.append((os.path.basename(path)[:-5], len(report.get("files", [])), len(findings)))

    for finding in findings:
        rule = finding["ruleId"]
        if finding.get("suppressed"):
            suppressed[rule] += 1
            continue
        counts[(rule, finding["severity"])] += 1
        if finding.get("inconclusive"):
            inconclusive[rule] += 1
        location = finding.get("location") or {}
        file = location.get("file", "?")
        # prefer samples from distinct files so one seed script does not fill all slots
        if len(samples[rule]) < samples_per_rule and file not in sample_files[rule]:
            sample_files[rule].add(file)
            samples[rule].append((file, location.get("line"), finding.get("statementSummary") or "",
                                  finding.get("message") or ""))

print()
print(f"files: {files_total}   findings: {sum(counts.values())}   suppressed: {sum(suppressed.values())}")
if summary_totals:
    print("summary: " + ", ".join(f"{k}={v}" for k, v in sorted(summary_totals.items())))
print()
print("per directory:")
for name, files, findings in per_dir:
    print(f"  {name:<70} {files:>6} files {findings:>8} findings")

print()
print(f"{'rule':<18} {'severity':<9} {'count':>8}  notes")
rule_totals = Counter()
for (rule, severity), n in counts.items():
    rule_totals[rule] += n
for (rule, severity), n in sorted(counts.items(), key=lambda kv: (kv[0][0], severity_order.get(kv[0][1], 9))):
    notes = []
    if inconclusive.get(rule):
        notes.append(f"inconclusive={inconclusive[rule]}")
    if suppressed.get(rule):
        notes.append(f"suppressed={suppressed[rule]}")
    print(f"{rule:<18} {severity:<9} {n:>8}  {' '.join(notes)}")
print(f"{'TOTAL':<18} {'':<9} {sum(counts.values()):>8}")

print()
print("samples:")
for rule in sorted(samples):
    print(f"  {rule} ({rule_totals[rule]})")
    for file, line, summary, message in samples[rule]:
        print(f"    {file}:{line}")
        print(f"      {summary[:160]}")
        print(f"      -> {message[:200]}")

with open(os.path.join(out, "summary.tsv"), "w", encoding="utf-8") as tsv:
    for (rule, severity), n in sorted(counts.items()):
        tsv.write(f"{rule}\t{severity}\t{n}\n")
print()
print(f"summary.tsv written to {out}")
PY

if [ "$failures" -gt 0 ]; then
    echo "corpus-scan: $failures director$( [ "$failures" -eq 1 ] && echo y || echo ies) failed" >&2
    exit 2
fi
