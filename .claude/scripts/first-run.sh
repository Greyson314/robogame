#!/usr/bin/env bash
#
# Headless first run of the Windows player (LAUNCH-READINESS B3): copy the
# rig's last build out of the rig (every rig reset wipes Builds/), start it
# `-batchmode -nographics -factory-mute` for N seconds, stop it, and print
# one [FIRST-RUN] row with the log's error / exception / warning counts.
#
# `-factory-mute` (CHG-034) is what keeps Grey's speakers quiet: FMOD has no
# batch guard of its own (F-062). The script refuses to start a player whose
# build predates the flag unless --allow-unmuted is given.
#
# Usage:
#   .claude/scripts/first-run.sh [--seconds N] [--allow-unmuted]
#
# Build first: .claude/scripts/build-player.sh <sha>

set -e
set -u
set -o pipefail

MAIN_PATH="$(git rev-parse --show-toplevel)"
RIG_BUILD="$MAIN_PATH/.claude/worktrees/test-rig/Builds/Windows"
OUT_ROOT="$MAIN_PATH/.utmp/factory/first-run"
SECONDS_TO_RUN=30
ALLOW_UNMUTED=0

while [ $# -gt 0 ]; do
    case "$1" in
        --seconds) SECONDS_TO_RUN="$2"; shift 2 ;;
        --allow-unmuted) ALLOW_UNMUTED=1; shift ;;
        *) echo "Usage: $0 [--seconds N] [--allow-unmuted]" >&2; exit 2 ;;
    esac
done

if [ ! -f "$RIG_BUILD/Robogame.exe" ]; then
    echo "Error: no build at $RIG_BUILD/Robogame.exe (run build-player.sh first)" >&2
    exit 2
fi

LABEL="$(date -u +%Y%m%dT%H%M%SZ)"
RUN_DIR="$OUT_ROOT/$LABEL"
mkdir -p "$RUN_DIR"
cp -r "$RIG_BUILD" "$RUN_DIR/player"

# The flag's name is a string literal in CommandLineMute (UTF-16 in a Mono
# assembly, UTF-8 in IL2CPP metadata); a build without it would play the music.
if [ "$ALLOW_UNMUTED" -eq 0 ] && ! grep -rqa -e "-factory-mute" -e "f.a.c.t.o.r.y.-.m.u.t.e" "$RUN_DIR/player/Robogame_Data" 2>/dev/null; then
    rm -rf "$RUN_DIR/player"
    echo "Error: this build does not carry -factory-mute (CHG-034); refusing a run that would play audio" >&2
    exit 2
fi

LOG="$RUN_DIR/Player.log"
WIN_EXE="$(cygpath -w "$RUN_DIR/player/Robogame.exe")"
WIN_LOG="$(cygpath -w "$LOG")"

echo "[1/2] running $WIN_EXE for ${SECONDS_TO_RUN}s (batchmode, nographics, factory-mute)…"
EXIT_NOTE="$(powershell -NoProfile -Command "
    \$p = Start-Process -FilePath '$WIN_EXE' -ArgumentList '-batchmode','-nographics','-factory-mute','-logFile','$WIN_LOG' -PassThru
    if (\$p.WaitForExit($SECONDS_TO_RUN * 1000)) { 'exited-early code=' + \$p.ExitCode }
    else { Stop-Process -Id \$p.Id -Force; 'stopped-after-${SECONDS_TO_RUN}s' }
" | tr -d '\r')"

count() { grep -c -- "$1" "$LOG" 2>/dev/null || true; }
ERRORS=$(grep -ci "error" "$LOG" 2>/dev/null || true)
EXCEPTIONS=$(count "Exception")
WARNINGS=$(grep -ci "warning" "$LOG" 2>/dev/null || true)
SCENES=$(grep -o "\[SceneFlow\][^\r]*\|Loaded scene [^\r]*" "$LOG" 2>/dev/null | head -5 | tr '\n' ';' || true)

echo "[2/2] [FIRST-RUN] label=$LABEL seconds=$SECONDS_TO_RUN end=$EXIT_NOTE errors=$ERRORS exceptions=$EXCEPTIONS warnings=$WARNINGS log=${LOG#$MAIN_PATH/}"
[ -n "$SCENES" ] && echo "       scenes: $SCENES"
rm -rf "$RUN_DIR/player"
