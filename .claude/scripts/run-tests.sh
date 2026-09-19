#!/usr/bin/env bash
#
# Run Robogame Unity tests autonomously against the dedicated test-rig
# worktree.
#
# Default mode syncs the main checkout's current working-tree state
# (tracked diffs + untracked files) into the worktree. `--at <sha>` instead
# resets the worktree straight to that commit (`reset --hard && clean -fd`),
# for measuring an exact commit (a builder's branch, a red-team gate) without
# touching the working tree at all (LESSONS 10). Either way: the MCP for
# Unity package is stripped from the rig (F-031 / CHG-015), Unity runs in
# batch mode, TestResults.xml is parsed, and the counts + any failures are
# printed.
#
# Usage:
#   .claude/scripts/run-tests.sh [--at <sha>] [--label <name>] [EditMode|PlayMode|All]
#
# --at <sha>       reset the rig to this commit instead of syncing the
#                   working tree. Required to test a branch that was never
#                   checked out (a plumbing-built branch, LESSONS 10).
# --label <name>   suffix for the result/log file names
#                   (TestResults-<name>-<platform>.xml); defaults to no
#                   suffix (TestResults-<platform>.xml, the historical name).
#
# Exits non-zero if any test failed (or the runner itself errored).
#
# Prereqs (one-time):
#   git worktree add .claude/worktrees/test-rig -b test-rig main
#   # Then open the worktree in Unity once to warm Library/ (or run this
#   # script and accept the 5+ min cold-import on the first invocation).

set -e
set -u
set -o pipefail

# -----------------------------------------------------------------------------
# Config / args
# -----------------------------------------------------------------------------
MAIN_PATH="$(git rev-parse --show-toplevel)"
WORKTREE_PATH="$MAIN_PATH/.claude/worktrees/test-rig"
UNITY_EXE="C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe"

AT_SHA=""
LABEL=""
PLATFORM=""

usage() {
    echo "Usage: $0 [--at <sha>] [--label <name>] [EditMode|PlayMode|All]" >&2
    exit 2
}

while [ $# -gt 0 ]; do
    case "$1" in
        --at)
            [ $# -ge 2 ] || usage
            AT_SHA="$2"
            shift 2
            ;;
        --label)
            [ $# -ge 2 ] || usage
            LABEL="$2"
            shift 2
            ;;
        EditMode|PlayMode|All)
            [ -z "$PLATFORM" ] || usage
            PLATFORM="$1"
            shift
            ;;
        *)
            usage
            ;;
    esac
done
PLATFORM="${PLATFORM:-All}"

if [ ! -d "$WORKTREE_PATH" ]; then
    echo "Error: test-rig worktree not set up at $WORKTREE_PATH" >&2
    echo "Run: git worktree add .claude/worktrees/test-rig -b test-rig main" >&2
    exit 2
fi

if [ ! -x "$UNITY_EXE" ] && [ ! -f "$UNITY_EXE" ]; then
    echo "Error: Unity executable not found at $UNITY_EXE" >&2
    echo "Edit \$UNITY_EXE in this script if your Unity install is elsewhere." >&2
    exit 2
fi

if [ -n "$AT_SHA" ]; then
    RESOLVED_SHA="$(git -C "$MAIN_PATH" rev-parse "$AT_SHA" 2>/dev/null)" || {
        echo "Error: '$AT_SHA' does not resolve to a commit in $MAIN_PATH" >&2
        exit 2
    }
fi

# -----------------------------------------------------------------------------
# THE RIG RULE — one batch job at a time: a Unity.exe with -runTests on its
# command line (a test run, here or in the live Editor's harness) OR with the
# test-rig worktree as its project (build-player.sh: F-064, a test run reset
# the rig under a player build and the build failed with CS2001). Never kill a
# Unity process; wait for it to finish, with a timeout, so no brief has to
# re-implement this (LESSONS 12 / METHOD 15).
# -----------------------------------------------------------------------------
RIG_WAIT_TIMEOUT_SECS="${RIG_WAIT_TIMEOUT_SECS:-1800}"

rig_busy_count() {
    powershell -NoProfile -Command \
        "(Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | ForEach-Object CommandLine | Where-Object { \$_ -match '-runTests|test-rig' } | Measure-Object).Count" \
        2>/dev/null | tr -d '\r'
}

wait_for_free_rig() {
    local count
    count="$(rig_busy_count)"
    count="${count:-0}"
    if [ "$count" = "0" ]; then
        return 0
    fi
    echo "[wait] a test run or a rig build is already running; waiting for the rig to free up (timeout ${RIG_WAIT_TIMEOUT_SECS}s)…"
    local waited=0
    while [ "$count" != "0" ]; do
        if [ "$waited" -ge "$RIG_WAIT_TIMEOUT_SECS" ]; then
            echo "Error: rig still busy after ${RIG_WAIT_TIMEOUT_SECS}s; refusing to start another batch run." >&2
            exit 3
        fi
        sleep 15
        waited=$((waited + 15))
        count="$(rig_busy_count)"
        count="${count:-0}"
    done
    echo "[wait] rig free after ${waited}s."
}

wait_for_free_rig

# -----------------------------------------------------------------------------
# Step 1 — put the worktree in the state to test
# -----------------------------------------------------------------------------
if [ -n "$AT_SHA" ]; then
    echo "[1/3] Resetting test-rig to $RESOLVED_SHA…"
    git -C "$WORKTREE_PATH" reset --hard "$RESOLVED_SHA" --quiet
    git -C "$WORKTREE_PATH" clean -fd --quiet
else
    echo "[1/3] Syncing main → test-rig…"

    MAIN_HEAD="$(git -C "$MAIN_PATH" rev-parse HEAD)"

    # Reset worktree to main's HEAD (drops any stale state from a previous run).
    # --quiet to keep our own logging readable.
    git -C "$WORKTREE_PATH" reset --hard "$MAIN_HEAD" --quiet
    git -C "$WORKTREE_PATH" clean -fd --quiet

    # Apply main's tracked-but-uncommitted diffs (modified, staged, deleted).
    # --binary handles .meta / .unity / other binary-ish files. --3way is
    # robust against the rare case where the worktree somehow drifted.
    if ! git -C "$MAIN_PATH" diff --quiet HEAD; then
        git -C "$MAIN_PATH" diff HEAD --binary \
            | git -C "$WORKTREE_PATH" apply --whitespace=nowarn --3way
    fi

    # Copy untracked-but-not-gitignored files. New .cs files I haven't `git
    # add`-ed yet land here.
    UNTRACKED_COUNT=0
    while IFS= read -r f; do
        [ -z "$f" ] && continue
        src="$MAIN_PATH/$f"
        dst="$WORKTREE_PATH/$f"
        mkdir -p "$(dirname "$dst")"
        cp "$src" "$dst"
        UNTRACKED_COUNT=$((UNTRACKED_COUNT + 1))
    done < <(git -C "$MAIN_PATH" ls-files --others --exclude-standard)

    if [ "$UNTRACKED_COUNT" -gt 0 ]; then
        echo "       copied $UNTRACKED_COUNT untracked file(s)."
    fi
fi

# -----------------------------------------------------------------------------
# Step 1b — strip the MCP for Unity package from the RIG (FINDINGS F-031)
# -----------------------------------------------------------------------------
# The package keeps its "server I launched" handshake in EditorPrefs, which are
# per user, not per project, so the rig's batch Editor, on quit, terminates the
# factory Editor's MCP server on 8080 and takes the bridge down for the rest of
# the session. No test references the package (grep 2026-09-17), and the sync
# (or reset) above clobbers this edit on every run, so it never reaches the
# clone or main.
strip_mcp_package() {
    local manifest="$WORKTREE_PATH/Packages/manifest.json"
    local lock="$WORKTREE_PATH/Packages/packages-lock.json"
    if python - "$manifest" "$lock" <<'PY'
import json, sys
for path in sys.argv[1:]:
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        continue
    if data.get("dependencies", {}).pop("com.coplaydev.unity-mcp", None) is None:
        continue
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    print("       stripped com.coplaydev.unity-mcp from " + path)
PY
    then
        return 0
    fi
    # Fallback without python: drop the manifest line (it is not the last entry,
    # so no trailing-comma repair is needed); UPM rewrites the lock itself.
    echo "       python unavailable; stripping the manifest line with sed"
    sed -i '/"com.coplaydev.unity-mcp"/d' "$manifest"
}
strip_mcp_package

MANIFEST_HITS="$(grep -c 'com.coplaydev.unity-mcp' "$WORKTREE_PATH/Packages/manifest.json" || true)"
MANIFEST_HITS="${MANIFEST_HITS:-0}"
echo "       manifest grep com.coplaydev.unity-mcp: $MANIFEST_HITS"

# -----------------------------------------------------------------------------
# Step 2 — run Unity in batch mode
# -----------------------------------------------------------------------------
run_platform() {
    local p="$1"
    local suffix="$p"
    [ -n "$LABEL" ] && suffix="$LABEL-$p"
    local results="$WORKTREE_PATH/TestResults-$suffix.xml"
    local log="$WORKTREE_PATH/TestRun-$suffix.log"
    rm -f "$results"

    echo "[2/3] Running $p tests (Unity batch, may take 30–90 s)…"
    # NOTE: do NOT pass -quit alongside -runTests. -runTests already
    # implies quit-on-completion; -quit makes Unity exit before any
    # tests execute.
    set +e
    "$UNITY_EXE" \
        -batchmode \
        -nographics \
        -projectPath "$WORKTREE_PATH" \
        -runTests \
        -testPlatform "$p" \
        -testResults "$results" \
        -logFile "$log"
    local unity_exit=$?
    set -e

    if [ ! -f "$results" ]; then
        echo "       FAIL: no TestResults file produced. See $log"
        return 1
    fi

    # NUnit3 result XML format: top-level <test-run> with passed/failed/total attrs.
    local total passed failed inconclusive skipped
    total=$(grep -oP 'total="\K[0-9]+' "$results" | head -1)
    passed=$(grep -oP 'passed="\K[0-9]+' "$results" | head -1)
    failed=$(grep -oP 'failed="\K[0-9]+' "$results" | head -1)
    inconclusive=$(grep -oP 'inconclusive="\K[0-9]+' "$results" | head -1)
    skipped=$(grep -oP 'skipped="\K[0-9]+' "$results" | head -1)
    : "${total:=0}" "${passed:=0}" "${failed:=0}" "${inconclusive:=0}" "${skipped:=0}"

    echo "[3/3] $p: $passed/$total passed, $failed failed, $inconclusive inconclusive, $skipped skipped."

    if [ "$failed" -gt 0 ]; then
        # Extract failure messages — keep it terse, just the test name and
        # the assertion message (without the full stack trace).
        echo ""
        echo "Failed tests:"
        # grep/awk only — this machine has no Python (the Windows App
        # Execution Alias shim silently ate the old python3 parse). NUnit3
        # writes each <test-case ...> opening tag on one line, so a
        # line-oriented pull of fullname= is reliable; the first non-empty
        # CDATA line after that case's <message> is the assertion text.
        grep -o '<test-case[^>]*result="Failed"[^>]*>' "$results" \
            | grep -oP 'fullname="\K[^"]+' \
            | while IFS= read -r name; do
                echo "  - $name"
                msg=$(awk -v n="fullname=\"$name\"" '
                    index($0, n) { intc = 1 }
                    intc && /<message>/ { inmsg = 1 }
                    inmsg {
                        gsub(/.*<!\[CDATA\[|\]\]>.*|<message>|<\/message>/, "")
                        sub(/^[ \t]+/, "")
                        if (length($0)) { print; exit }
                    }' "$results")
                echo "    ${msg:-(no failure message)}"
              done
        return 1
    fi

    return 0
}

# -----------------------------------------------------------------------------
# Step 3 — dispatch
# -----------------------------------------------------------------------------
overall_status=0
case "$PLATFORM" in
    EditMode) run_platform EditMode || overall_status=$? ;;
    PlayMode) run_platform PlayMode || overall_status=$? ;;
    All)
        run_platform EditMode || overall_status=$?
        run_platform PlayMode || overall_status=$?
        ;;
esac

exit $overall_status
