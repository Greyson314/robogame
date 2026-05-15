# Autonomous test runner

`run-tests.sh` runs Robogame's Unity EditMode / PlayMode tests from the
command line without the user closing their Editor. Used by Claude Code
during autonomous sessions to verify changes before commit.

## Why this exists

Unity locks `Library/` while the Editor is open, so a CLI `Unity.exe
-batchmode -runTests …` invocation against the main project fails if
the user is editing in parallel. The workaround: a dedicated git
worktree at `.claude/worktrees/test-rig` with its own `Library/`. The
test-rig is never opened by the user — it exists purely as a CLI test
target. The script syncs the main checkout's current working-tree
state into the worktree before each run.

## One-time setup

```bash
# Create the worktree on a dedicated branch.
git worktree add .claude/worktrees/test-rig -b test-rig main

# Warm up Library/ — Unity needs to import all assets once.
# Either:
#   (a) open the worktree in Unity via the Hub once, wait for the
#       import to finish, then close. ~5 min.
#   (b) let the first `run-tests.sh` invocation do the import. Same
#       ~5 min, hidden in the batch-mode run.
```

The worktree is on a `test-rig` branch that the script keeps reset to
main's HEAD. Never commit on the `test-rig` branch directly.

## Usage

```bash
.claude/scripts/run-tests.sh              # both EditMode + PlayMode
.claude/scripts/run-tests.sh EditMode     # EditMode only
.claude/scripts/run-tests.sh PlayMode     # PlayMode only
```

Output format:

```
[1/3] Syncing main → test-rig…
       copied 3 untracked file(s).
[2/3] Running EditMode tests (Unity batch, may take 30–90 s)…
[3/3] EditMode: 13/13 passed, 0 failed, 0 inconclusive.
[2/3] Running PlayMode tests …
[3/3] PlayMode: 12/12 passed, 0 failed, 0 inconclusive.
```

On failure, the failing test full names + first-line assertion message
print under the summary. Exit code is non-zero.

Raw NUnit XML lives at
`.claude/worktrees/test-rig/TestResults-{Platform}.xml` after each run;
the full Unity log is at `TestRun-{Platform}.log` in the same dir.

## What the sync step does

1. `git -C $WORKTREE reset --hard main_HEAD --quiet` then `clean -fd` —
   discard whatever state the worktree was in.
2. `git -C $MAIN diff HEAD --binary | git -C $WORKTREE apply` — apply
   main's tracked-but-uncommitted modifications (modified, staged,
   deleted) including binary diffs.
3. Copy `git ls-files --others --exclude-standard` (untracked files)
   from main to the worktree. New `.cs` files Claude hasn't `git add`-ed
   yet land here.

Net effect: the worktree's working tree matches main's working tree
exactly (modulo `Library/`, which the worktree owns separately).

## Constraints + gotchas

- **Don't open the test-rig worktree in Unity yourself.** It exists for
  CLI tests; the script clobbers its working tree on every run. If you
  open it casually, your edits will be lost.
- **Cold first run is slow.** Unity will import all assets the first
  time the worktree is loaded (~5 min). Subsequent runs are
  ~30–90 s each.
- **Unity exe path is hard-coded** at the top of the script
  (`C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Unity.exe`).
  Edit if your install is elsewhere or you upgrade Unity.
- **PlayMode tests run in a headless Editor.** Anything that needs
  graphics-card-backed rendering won't work in `-nographics` batch
  mode. None of the current Robogame tests depend on this (mesh
  geometry assertions don't render).
- **Disk usage**: the worktree's `Library/` is a few GB. Acceptable
  cost for the autonomy win, but worth knowing.

## Cleanup

If you ever need to nuke the test-rig (e.g., upgrade Unity and want a
fresh import):

```bash
git worktree remove --force .claude/worktrees/test-rig
git branch -D test-rig
git worktree add .claude/worktrees/test-rig -b test-rig main
```
