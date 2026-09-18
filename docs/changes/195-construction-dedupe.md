# 195 — Three construction de-duplications: MakeHighlightShell, BuildRowShell, StyleButton (LOG-195)

Landed by the factory 2026-09-18T06:25:54Z. Change CHG-027, branch `chg/027-construction-dedupe` @ bf72f8aa6a. Gate: suite PASS, perf N/A, red team PASS — Builder's gate at bf72f8aa on the rig (direct run, MCP stripped): EditMode 546/546 (543 + 3 new helper tests, RED first: the helpers did not exist), PlayMode 152/153 (1 documented skip); red team sonnet/medium PASS (.utmp/factory/gate/CHG-027-redteam.md, 75k tokens): every old inline block reproduced field-for-field, per-site differences carried as parameters, none normalised; its own rig run 546/546 + 152/153. Perf N/A: construction-time code only. OWED before land: the spec's live UGUI hierarchy/colour dumps before and after (Garage build-mode highlight incl. the Collider destroy the EditMode test cannot assert; Settings panel rows; Lab buttons), once CHG-024's builder frees the Editor and the tree..

## Intent

CHARTER D8 S1 (Grey, 2026-09-17): de-duplication and simplification are a standing slot in every shift. The second census sweep (2026-09-18) found three verbatim-duplicated construction blocks in three of the four largest runtime classes: the highlight cube BlockEditor builds twice (F-050), the ~19-line row shell SettingsHud builds four times (F-051), and the Button colour styling LabController repeats at four sites (F-054). Three helpers, one per file, each replacing copies with a call and nothing else; bundled because they are one kind of edit with one kind of proof and each gate costs a red team.

## What shipped

- `BlockEditor.MakeHighlightShell(Material)` (private static): CreatePrimitive(Cube), the auto Collider destroyed, `sharedMaterial`, shadows off both ways; the two call sites (`HighlightInstance`, `DriveHoverHighlight`) keep their own shell name and `FitShellToBlock` call, which were never part of the duplicate. 1252 → 1253 lines.
- `SettingsHud.BuildRowShell(Transform parent, string name, string label, float labelWidth)` (private static): the row GameObject, `LayoutElement.preferredHeight = 44`, the ink-tinted background Image (alpha 0.04), the left-anchored label child with its font, size, colour, alignment and overflow, returned as `(row, label)`; used by AddActionRow, AddBisectToggleRow, AddSliderRow and AddBoolRow, each passing its own name, label and label width. 1209 → 1167 lines.
- `LabController.StyleButton(Button, Color normal, Color hover, Color pressed)` (private static): reads the ColorBlock, sets exactly the three colours the old sites set, writes it back; four call sites (row select, New row, Close, Save). 1207 → 1209 lines.
- Three new EditMode tests (`MakeHighlightShellTests`, `BuildRowShellTests`, `StyleButtonTests`, under Tests/EditMode/Gameplay) assert each helper's product against the values copied from the old call sites; written first, RED on main because the helpers did not exist (the only RED a pure extraction can produce).

Net: three copies became one in each file; the line count fell only in SettingsHud, as CHG-022 taught to expect.

## Verification

- Suite at the frozen sha bf72f8aa on the rig (direct run, MCP stripped): EditMode 546/546 (543 + 3), PlayMode 152/153 (the one documented skip), 0 failures; the red team's own run the same.
- Red team sonnet/medium PASS (`.utmp/factory/gate/CHG-027-redteam.md`, 75k tokens): every old inline block reproduced field-for-field, every per-site difference carried as a parameter, none normalised across sites; no invariant touched (INV-6: construction-time only; INV-1: no Tweakable newly read). Its one flag: the EditMode test cannot assert the Collider destroy (Play-Mode semantics), so the live dump below is what proves that half.
- Live UGUI dumps over the bridge, before and after: NOT TAKEN. The bridge died at 05:25Z (F-057, LESSONS 15) before the tree was free, and stayed dead. The landing rests on the red team's field-for-field reading of every replaced block (component order, colours with alpha, sizes, anchors, fonts, alignment, overflow, names, parents, Destroy vs DestroyImmediate) and the three EditMode tests; the one half no test asserts is MakeHighlightShell's Collider destroy in play mode, and there both the old sites and the helper call `Destroy(col)` on the same object (the red team checked the call). A PlayMode assertion of that Collider is owed as a one-test follow-up on the next shift with a bridge or on the rig.
- Perf N/A: nothing on a per-frame path.

## Aftermath

- F-049 (BlockEditor's highlight subsystem, ~160 lines), F-052 (SettingsHud's keybinds section) and F-053 (LabController's ~600-line canvas builder) are the extractions these helpers make easier; F-053 needs a pixel-identical proof (screenshot diff) because the Lab canvas is on the player's path.
