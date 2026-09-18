# 201 — BlockEditHighlights: the build-mode highlight subsystem leaves BlockEditor (LOG-201)

Landed by the factory 2026-09-18T22:43:48Z. Change CHG-032, branch `chg/032-block-edit-highlights` @ 9afa1e467c. Gate: suite PASS, perf N/A, red team PASS — Suite at 9afa1e46 via run-tests.sh --at: EditMode 562/562, PlayMode 158/159 (1 documented skip). Red team sonnet/medium PASS 58k (.utmp/factory/gate/CHG-032-redteam.md): every call site like-for-like, INV-6 holds on the pulse path. Perf N/A: the per-frame path only writes a material colour, unchanged. Arbitration: the static material caches rebuild under a == null guard, which survives a domain reload; no finding..

## What changed

The build-mode highlight subsystem (the bound-instance highlight and the tune-mode hover highlight: shell construction, fitting to a block's bounds, the hover pulse, hide and clear) moved from `BlockEditor` into a plain C# class, `BlockEditHighlights`, in the same folder and assembly. BlockEditor keeps target resolution and makes thin calls. BlockEditor.cs: 1,253 → 1,124 lines. Source: F-049 (D8 S1).

No value, colour, timing, parent or lifetime changed. `HandleEditingInstanceChanged`'s inlined highlight destroy became `ClearInstanceHighlight()`. The helper is public because the test assembly has no InternalsVisibleTo.

## Evidence

- Tests first: commit 7f8ba72b carries only the tests and fails to compile for the right reason (CS0246 `BlockEditHighlights`); commit 9afa1e46 adds the class: EditMode 562/562, PlayMode 158/159 (1 documented skip), via `run-tests.sh --at`.
- Four new PlayMode tests drive the public surface against a real block: one shell parented to the block, no Collider, fitted bounds, hover shell reuse, hide and clear.
- Red team (sonnet/medium, 58k) PASS: every call site diffed like for like; INV-6 holds on the per-frame pulse; INV-4/5 untouched.

## Revert

`git revert`.
