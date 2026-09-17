# 183 — Hover Tank preset: the deck's corner cubes hosted on cubes, not on hoverblades (LOG-183)

Landed by the factory 2026-09-17T21:44:12Z. Change CHG-013, branch `chg/013-hovertank-preset` @ 28358f9398. Gate: suite PASS, perf N/A, red team PASS — Red team round 1 (2026-09-17) returned KILL on CLASS ONLY: a structure cube's Up rotates its transform (BlockGrid.cs:181) and the plank albedo is UV-mapped (_ArtisticProjection 0), so four deck tiles change grain direction, which made the change ASK rather than AUTO. It found the DIFF correct and complete (.utmp/factory/gate/CHG-013-redteam.md). The foreground arbitrated in writing: the kill stands as a class finding, the change lands byte-for-byte on Grey's check-off. Grey approved it in the runner session 2026-09-17 and narrowed I1 and D16b in the same word, so the class objection is resolved by the rule change itself; recorded PASS on that basis. Suite on the branch merged with main @ 64c715ef: EditMode 537/537, PlayMode 152/153 (documented skip), 0 failures (.utmp/factory/gate/CHG-013-suite-final.txt). Negative proof: the KnownInvalid quarantine assertion was written to fail the moment the preset became valid, and did..

Factory, 2026-09-17, branch `chg/013-hovertank-preset` (built during shift 5, landed from the runner session after Grey's approval).
Class ASK (charter I1: anything visible) — approved by Grey 2026-09-17 with a rule change that makes this class of change AUTO from now on (see § Aftermath).

## Intent

The Hover Tank preset was not a blueprint a player could have built. Four corner cubes on the
deck at y=0 were hosted by the hoverblades beneath them, and a hoverblade is a leaf block:
nothing may attach to it. The rule is the one build mode enforces on the player, so the preset
violated the property every shipped preset is supposed to have — equivalent to a player save.
It drove fine; only its legality was wrong.

CHG-008's new coverage caught it on its first run (FINDINGS F-026) and quarantined it loudly in
`PresetBlueprintTests.KnownInvalid`, an entry that asserts the preset STILL fails so it expires
with the fix rather than hiding it. Emptying that quarantine is LAUNCH-READINESS item T1.

Root cause: Hover Tank is the one preset the scaffolder never writes, so it never passed
through the write-time validator that would have rejected the hosting.

## What shipped

- `Blueprint_DefaultHoverTank.asset`: the four corner cubes at y=0 get Up = -X (the x=-2
  corners, hosted on x=-1) or Up = +X (the x=1 corners, hosted on x=0). Same cells, same block
  ids, same hoverblades — only orientation metadata moves, so INV-2 holds.
- `PresetBlueprintTests.cs`: the `KnownInvalid` entry for Hover Tank is gone and the quarantine
  pattern now pins the expected error text rather than only `IsValid == false`, so a future
  entry cannot pass for the wrong reason. Two stale strings fixed: the list comment and the
  guard's "scaffold via Build Everything" hint both claimed every preset is scaffolder-written.
- `ScriptedChassisBuilderTests.cs`: `DumpAllPresets_WritesAsciiSnapshot` takes the block library,
  so the snapshot can no longer print "Validation: OK" for a preset the suite quarantines.
- `docs/blueprint-snapshots/presets.md` regenerated: 88 lines that were missing, the Grappler
  and Hover Tank sections among them.

## Verification

Suite on the branch merged with main @ 64c715ef: EditMode and PlayMode green, the Hover Tank
row passing under both `Preset_PassesValidation` and
`EveryShippedPreset_PassesLibraryAwareValidation`, with `KnownInvalid` empty. The quarantine's
own assertion was written to fail the moment the preset became valid, and it did, which is the
negative proof that the fix is what flipped it. Evidence:
`.utmp/factory/gate/CHG-013-suite-final.txt`.

The red team's KILL was on class, not on the diff: a structure cube's Up rotates its transform
(`BlockGrid.cs:181`) and the cube's plank albedo is UV-mapped, so four deck tiles show their
wood grain crosswise. The diff was found correct and complete
(`.utmp/factory/gate/CHG-013-redteam.md`). It landed byte-for-byte on Grey's check-off.

## Aftermath

Grey approved and, in the same breath, narrowed what needs approval at all: this project is a
casual solo game, not the trading system whose validation culture the loop inherited, and a
dev-facing preset that exists to have something to drive is below the approval line. The
charter's I1 and D16b were amended in the same session. Four deck tiles of cross-grain plank on
the friendly tank are the visible cost, and are considered paid.
