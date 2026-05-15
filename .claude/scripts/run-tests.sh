#!/usr/bin/env bash
#
# Run Robogame Unity tests autonomously against the dedicated test-rig
# worktree. Syncs the main checkout's current working-tree state (tracked
# diffs + untracked files) into the worktree, runs Unity in batch mode,
# parses TestResults.xml, and prints a one-line summary plus any failure
# details.
#
# Usage:
#   .claude/scripts/run-tests.sh [EditMode|PlayMode|All]
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
# Config
# -----------------------------------------------------------------------------
MAIN_PATH="$(git rev-parse --show-toplevel)"
WORKTREE_PATH="$MAIN_PATH/.claude/worktrees/test-rig"
UNITY_EXE="C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe"
PLATFORM="${1:-All}"

case "$PLATFORM" in
    EditMode|PlayMode|All) ;;
    *)
        echo "Usage: $0 [EditMode|PlayMode|All]" >&2
        exit 2
        ;;
esac

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

# -----------------------------------------------------------------------------
# Step 1 — sync main's working-tree state into the worktree
# -----------------------------------------------------------------------------
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

# -----------------------------------------------------------------------------
# Step 2 — run Unity in batch mode
# -----------------------------------------------------------------------------
run_platform() {
    local p="$1"
    local results="$WORKTREE_PATH/TestResults-$p.xml"
    local log="$WORKTREE_PATH/TestRun-$p.log"
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
    local total passed failed inconclusive
    total=$(grep -oP 'total="\K[0-9]+' "$results" | head -1)
    passed=$(grep -oP 'passed="\K[0-9]+' "$results" | head -1)
    failed=$(grep -oP 'failed="\K[0-9]+' "$results" | head -1)
    inconclusive=$(grep -oP 'inconclusive="\K[0-9]+' "$results" | head -1)
    : "${total:=0}" "${passed:=0}" "${failed:=0}" "${inconclusive:=0}"

    echo "[3/3] $p: $passed/$total passed, $failed failed, $inconclusive inconclusive."

    if [ "$failed" -gt 0 ]; then
        # Extract failure messages — keep it terse, just the test name and
        # the assertion message (without the full stack trace).
        echo ""
        echo "Failed tests:"
        # Use Python for proper XML parsing — grep regex on multi-line XML is fragile.
        python3 - "$results" <<'PY'
import sys
import xml.etree.ElementTree as ET
tree = ET.parse(sys.argv[1])
for tc in tree.iter("test-case"):
    if tc.attrib.get("result") == "Failed":
        name = tc.attrib.get("fullname", tc.attrib.get("name", "?"))
        failure = tc.find("failure")
        if failure is not None:
            msg = (failure.findtext("message") or "").strip().split("\n")[0]
        else:
            msg = "(no failure message)"
        print(f"  - {name}")
        print(f"    {msg}")
PY
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
