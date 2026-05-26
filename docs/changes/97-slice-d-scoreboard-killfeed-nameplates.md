# 97 — Slice D: Phase 7 QoL (scoreboard + kill feed + nameplates)

> Status: **Slice D shipped (SP layer).** Tab-held scoreboard, persistent
> kill feed, world-space chassis nameplates — all three auto-attached
> in `ArenaController.ConfigureCamera`. NGO replication sibling
> deliberately deferred — see "What's deferred".

## Scope

Third slice of the polish + netcode QoL phase. The plan called for
building these three Phase 7 quality-of-life HUDs "once, networked-
aware": ship the SP layer now reading directly from
`MatchController` / `Robot`, scaffold the NGO siblings so MP fills
them in later. This session lands the SP layer; the NGO scaffold
slips to a future netcode-specific session because the user is a
netcode beginner and adding placeholder `NetworkBehaviour` siblings
without an exercising flow risks dead code that will need to be
rewritten when MP actually lands.

## What landed

**`KillFeedHud`** ([Assets/_Project/Scripts/Gameplay/KillFeedHud.cs](Assets/_Project/Scripts/Gameplay/KillFeedHud.cs)).
Persistent kill-feed strip on the top-right of the screen. Counterpart
to `KillAnnouncer`'s splashy centre-screen banner — the announcer is
juice, this is readable history. Subscribes to
`MatchController.KillRegistered` (both player AND enemy kills,
unlike the announcer which is player-only). Ring-style buffer
capped at `_maxEntries` (default 6); each entry holds at full
opacity for `_holdSeconds` (5 s), then fades over `_fadeSeconds`
(0.8 s) and drops from the ring on the next render pass. Tints
green for player kills, red for enemy kills, muted for environment.

**`NameplateOverlay`** ([Assets/_Project/Scripts/Gameplay/NameplateOverlay.cs](Assets/_Project/Scripts/Gameplay/NameplateOverlay.cs)).
World-space chassis nameplates — name label + HP bar floating above
every non-local Robot in the arena. One camera-scoped overlay (one
OnGUI walks the cached Robot list and projects each anchor through
the camera) instead of a per-chassis MonoBehaviour. Periodic
`FindObjectsByType<Robot>` refresh at `_refreshInterval` (1 s) catches
spawn / respawn / destroy without per-event plumbing. Culls past
`_maxDistance` (120 m) and skips the local chassis (no plate on the
player's own crosshair). HP-bar tint follows the same Healthy /
Warning / Danger palette as `ObjectiveHud`'s HP rail.

**`ScoreboardOverlay`** ([Assets/_Project/Scripts/Gameplay/ScoreboardOverlay.cs](Assets/_Project/Scripts/Gameplay/ScoreboardOverlay.cs)).
Tab-held centred overlay listing per-side match state — scrap totals,
frag counts, player lives, time remaining. Renders nothing until the
player holds the configured key (`Key.Tab` default), reads
`MatchController` directly on every OnGUI event. Two-column layout
(YOU left, ENEMY right) with a centred timer row at the bottom.
Input binding is direct (`Keyboard.current[_holdKey].isPressed`)
to avoid pulling another `InputActionAsset` dependency through
the gameplay asmdef — same pattern `StartMatchHud` uses.

**`ArenaController.ConfigureCamera` wiring**
([Assets/_Project/Scripts/Gameplay/ArenaController.cs](Assets/_Project/Scripts/Gameplay/ArenaController.cs)).
All three new components auto-attach to the camera alongside the
existing `ObjectiveHud` / `MatchEndOverlay` / `KillAnnouncer` /
`ScrapCarriedIndicator` setup. Each is bound to the live
`MatchController` immediately so respawn / round-reset paths
flow through the same `BindMatch` call the older HUDs use.

## What's deferred

**NGO replication sibling.** The plan called for `NetworkKillFeed`
and `NetworkScoreboard` `NetworkBehaviour` siblings under
`Assets/_Project/Scripts/Network/`. Skipped this session because:

1. The user is a netcode beginner; landing dead placeholder
   `NetworkBehaviour`s without an exercising flow creates surface
   area that will need to be rewritten when the actual MP integration
   lands.
2. All three new HUDs already design through the right abstraction
   — `MatchController` event + per-`Robot` state. The Phase 4
   destruction-replication work already makes `Robot.BlockCount`
   server-authoritative, so the `NameplateOverlay`'s HP bars are
   correct in MP today. The kill feed will need an
   `INetworkMatchSnapshot`-style read path when MP-side kill events
   start coming from the server rather than the local
   `MatchController`, but the HUD render is unchanged.

The natural moment to scaffold the NGO siblings is alongside the
Phase 7 MP push (voice + scoreboard + nameplates as one networked
slice). Plan file's "Slice D" section already calls this out —
nothing here invalidates the original architecture.

## Tests

`EditMode: 252/253 passed, 0 failed, 1 inconclusive.`
`PlayMode: 92/93 passed, 0 failed, 0 inconclusive.`

No new tests for this slice — pure UI / render polish, exercised
through playtest. The `MatchControllerTests` + `MatchFlowTests` PlayMode
coverage on `KillRegistered` / `ScoreForSide` / `KillsForSide` /
`PlayerLivesRemaining` guards against regression on the read paths
the three new HUDs depend on.

## Files

New:
- `Assets/_Project/Scripts/Gameplay/KillFeedHud.cs`
- `Assets/_Project/Scripts/Gameplay/NameplateOverlay.cs`
- `Assets/_Project/Scripts/Gameplay/ScoreboardOverlay.cs`

Edited:
- `Assets/_Project/Scripts/Gameplay/ArenaController.cs` (auto-attach + bind the three new components)
- `docs/changes/README.md` (session index)
