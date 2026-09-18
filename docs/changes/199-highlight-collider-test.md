# 199 — A PlayMode test proves the build-mode highlight shell drops its Collider (LOG-199)

Landed by the factory 2026-09-18T22:26:59Z. Change CHG-031, branch `chg/031-highlight-collider-test` @ d02c231066. Gate: suite PASS, perf N/A, red team N/A — D16b: a test only, no runtime code. Suite: main EditMode 562/562 (suite-shift9-start.log, code tree unchanged), PlayMode with the test 154/155 0 failed (suite-chg031-green.log); red proof: Destroy(col) disabled -> the test fails with its message (suite-chg031-red.log), restored. Perf N/A: nothing hot..

## What changed

One PlayMode test, `MakeHighlightShellColliderTests.MakeHighlightShell_HasNoColliderAfterOneFrame`: it invokes `BlockEditor.MakeHighlightShell` in play mode, yields one frame (Object.Destroy is deferred) and asserts the shell has no Collider. CHG-027 (docs/changes/195) owed this: its EditMode test cannot assert it, and the live dump it planned was lost with the bridge (F-057).

## Why it matters

The highlight shells are parented to blocks on the chassis. A shell that kept the cube primitive's BoxCollider would sit between the build-mode raycast and the block under the cursor, and would add a collider under the chassis Rigidbody.

## Evidence

- Green: PlayMode 154/155, 0 failed (`.utmp/factory/suite-chg031-green.log`); EditMode 562/562 on the same code (`suite-shift9-start.log`).
- Red first, by mutation: with `if (col != null) Destroy(col);` commented out the test fails with its own message (`suite-chg031-red.log`); the line was restored with `git checkout`.
- Red team N/A (D16b: a test). Perf N/A.

## Revert

`git revert`; nothing at runtime changes either way.
