# 113 — Scrap deposit: warmup indicator + silent-loss fix

> User report: "after you pick up scrap you should deposit it at your base to
> raise the score, but the score is permanently stuck at 0-0." Diagnosed from
> the Unity `Editor.log` — not a scoring bug, a **match-state trap**.

## Root cause

The arena `MatchConfig` defaults to `RequireManualStart = true`, so the round
sits in `WarmingUp` until the player presses `` ` `` (Backquote) to begin
combat. Across the last 6 logged arena sessions the match was started **0
times** (`"Match started"` never logged), while a deposit *did* fire:

```
Scrap depots spawned ... Target = 20 team scrap.   (state = WarmingUp)
Robot 'GroundBot_7d60c8' destroyed: Lost 78 % of mass
ScrapDepot (Player) instant-banked 3 → pending pool 3.
```

The full chain (pickup → `ScrapHeld` → depot trigger → `side==_team` →
`TryInstantTransfer` → `_bankedScrap` → `TickScoreDrain` →
`MatchController.DepositScrap` → `ObjectiveHud.ScoreForSide`) is correct. But
`DepositScrap` rejects every deposit while `State != InProgress`, and
`TickScoreDrain` decremented `_bankedScrap` **before** that rejected call — so
scrap banked during warmup was silently destroyed and the score never moved.
The player, reading the top-centre scoreboard (0-0), never connected the
bottom-centre "Press [`] to begin combat" prompt to "scoring is paused."

## What shipped

Working-as-designed (scoring is intentionally gated to the live round); the fix
is to make warmup legible and stop the silent scrap loss.

### `ObjectiveHud` — warmup is now obvious on the scoreboard

During `WarmingUp` the centre timer pill reads **"WARMUP"** (accent) instead of
the manual-start-pinned `0:00`, and both score numbers dim to `TextMuted`. The
indicator lives where the player actually looks (top-centre, at the 0-0), not
just the bottom prompt. Restores to live colours / countdown on `InProgress`.
Pure OnGUI colour+text swap — no new allocations, no layout change.

### `ScrapDepot` — depot is inert during warmup (no silent loss)

- `TryInstantTransfer` now early-returns unless `_match.State == InProgress`, so
  warmup driving over your own pad leaves your carried scrap on the robot
  (warmup stays neutral; the deposit lands once the round starts and you touch
  the pad again).
- `TickScoreDrain` gained the same `InProgress` guard (belt-and-braces) so any
  pre-banked pool holds and scores at round start instead of being drained
  against the rejecting controller.

No invariant/ADR impact: depot reads the server-authoritative `MatchController`
state; offline `IsServer==true` so SP is byte-identical. Zero new physics
objects.

## Verification

- EditMode suite via `run-tests.sh` (batch compile across Combat / Gameplay /
  Network / Tests). Changes are MonoBehaviour OnGUI + trigger-gating, not unit-
  covered; the suite's value here is the clean batch compile. **EditMode
  288/289 (0 failed, 1 pre-existing inconclusive), PlayMode 113/114 (0
  failed)** — clean compile across all asmdefs with both edits applied.
- Unity MCP bridge was down this session (known "revoked" state), so no live
  in-editor console capture; diagnosis came from `Editor.log` / `Editor-prev.log`.

## Note for later

The deeper match-flow choice (auto-start vs. keep manual `` ` `` start) was
offered and the user opted to keep manual start + just signal warmup. If
new-player confusion persists, revisit `RequireManualStart` default or make the
start prompt modal.
