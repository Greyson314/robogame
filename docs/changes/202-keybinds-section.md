# 202 — KeybindsSectionBuilder: the keybinds reference table leaves SettingsHud (LOG-202)

Landed by the factory 2026-09-18T22:54:16Z. Change CHG-033, branch `chg/033-keybinds-section` @ f2f8e98e63. Gate: suite PASS, perf N/A, red team PASS — Suite at f2f8e98e via run-tests.sh --at, run twice (builder, red team): EditMode 562/562, PlayMode 160/161 (1 documented skip). Red team sonnet/medium PASS 47k (.utmp/factory/gate/CHG-033-redteam.md): moved verbatim, before/after 19-row dumps byte-identical incl. the search filter; INV-1, INV-6 hold. Perf N/A: one-time panel construction..

## What changed

`SettingsHud.BuildKeybindsSection` and `AddKeybindRow` (the static keybinds reference table) moved to a new `KeybindsSectionBuilder` in the same folder and assembly. SettingsHud passes in the content parent, the font, the two colours, `Tweakables.DevSurfacesVisible`, its group-header helpers and `_allRows`. `RowEntry` and `GroupSection` widened from private to internal (same assembly). SettingsHud.cs: 1,167 → 1,098 lines. Source: F-052 (D8 S1).

## Evidence

- Tests first: commit e0b4c510 carries only the pinning PlayMode test, green on the old code: 19 rows pinned by GameObject name, action text, key text, order, default-collapsed state and the search filter. It logs a hierarchy dump.
- Commit f2f8e98e is the extraction: same test green, the before and after dumps are byte-identical (`TestRun-chg033-1-PlayMode.log` against `TestRun-chg033-final-PlayMode.log`).
- Suite at f2f8e98e, run twice: EditMode 562/562, PlayMode 160/161 (1 documented skip).
- Red team (sonnet/medium, 47k) PASS: the values passed as parameters are read at the same point as before; `_allRows` is passed by reference, so search still sees the rows; INV-1 and INV-6 hold.

## Revert

`git revert`.
