#!/usr/bin/env bash
#
# Build a Windows Standalone player from the CLI on the dedicated test-rig
# worktree (LAUNCH-READINESS B1/B2). Resets the rig to the given sha, strips
# the MCP-for-Unity package exactly as run-tests.sh's strip_mcp_package does
# (CHG-015/F-031: an unstripped rig kills the factory's MCP server on quit),
# then runs PlayerBuild.BuildWindows in batch mode and prints the
# [PLAYER-BUILD] row.
#
# Usage:
#   .claude/scripts/build-player.sh [sha]
#
# A build holds the rig for minutes — never run this while a -runTests
# Unity.exe or the live Editor has -projectPath pointing at the rig; this
# script waits for the rig to be free before touching it.
#
# Exits non-zero if Unity itself failed to run (a missing method, a crash).
# A build that completes but reports result=Failed still exits 0 from
# Unity's side (PlayerBuild.BuildWindows calls EditorApplication.Exit(1) in
# that case) — check the printed row, not just this script's exit code, is
# also fine: PlayerBuild's own Exit(1) makes the two agree.

set -e
set -u
set -o pipefail

MAIN_PATH="$(git rev-parse --show-toplevel)"
RIG_PATH="$MAIN_PATH/.claude/worktrees/test-rig"
UNITY_EXE="C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe"
SHA="${1:-HEAD}"

if [ ! -d "$RIG_PATH" ]; then
    echo "Error: test-rig worktree not set up at $RIG_PATH" >&2
    exit 2
fi

RESOLVED_SHA="$(git -C "$MAIN_PATH" rev-parse "$SHA")"
LABEL="$(date -u +%Y%m%dT%H%M%SZ)"

# -----------------------------------------------------------------------------
# Step 0 — wait for the rig to be free (a batch test run or the live Editor's
# AssetImportWorker helpers may hold it; a build takes minutes, so wait
# rather than race it).
# -----------------------------------------------------------------------------
echo "[0/3] waiting for a free rig…"
WAIT_START=$(date +%s)
while : ; do
    BUSY_COUNT=$(powershell -NoProfile -Command \
        "(Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | % CommandLine | ? { \$_ -match 'test-rig' } | Measure-Object).Count" \
        | tr -d '\r')
    [ "$BUSY_COUNT" = "0" ] && break
    ELAPSED=$(( $(date +%s) - WAIT_START ))
    if [ "$ELAPSED" -gt 1800 ]; then
        echo "Error: rig still busy after 30 min" >&2
        exit 3
    fi
    sleep 15
done
echo "       rig free after $(( $(date +%s) - WAIT_START ))s."

# -----------------------------------------------------------------------------
# Step 1 — reset the rig to the sha and strip the MCP package (CHG-015/F-031)
# -----------------------------------------------------------------------------
echo "[1/3] resetting rig to $RESOLVED_SHA…"
git -C "$RIG_PATH" reset --hard "$RESOLVED_SHA" --quiet
git -C "$RIG_PATH" clean -fd --quiet

python - "$RIG_PATH/Packages/manifest.json" "$RIG_PATH/Packages/packages-lock.json" <<'PY'
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

# -----------------------------------------------------------------------------
# Step 2 — run the build in batch mode
# -----------------------------------------------------------------------------
LOG="$RIG_PATH/BuildRun-$LABEL.log"
echo "[2/3] building (Unity batch, may take several minutes)… log: $LOG"
set +e
"$UNITY_EXE" \
    -batchmode \
    -nographics \
    -projectPath "$RIG_PATH" \
    -executeMethod Robogame.Tools.Editor.PlayerBuild.BuildWindows \
    -quit \
    -logFile "$LOG"
UNITY_EXIT=$?
set -e

# -----------------------------------------------------------------------------
# Step 3 — report
# -----------------------------------------------------------------------------
echo "[3/3] Unity exit=$UNITY_EXIT log=$LOG"
ROW="$(grep -o '\[PLAYER-BUILD\].*' "$LOG" | tail -1 || true)"

if [ -n "$ROW" ]; then
    echo "$ROW"
else
    echo "no [PLAYER-BUILD] row found in $LOG; last 40 lines:"
    tail -40 "$LOG"
fi

exit "$UNITY_EXIT"
