# LAUNCH-READINESS — what "ready for a Steam launch" means, item by item (END GOAL)

Grey owns the definition: which items exist and what counts as done. The factory owns the statuses and works the items down. Statuses: `unknown` (not yet measured) · `open` (measured, not met) · `in progress` (a CHG is in flight) · `done` (evidence pointer) · `grey` (blocked on a Grey decision or action; a DECIDE or BUY entry exists) · `n/a` (Grey struck it). Reviewed by the readiness sweep weekly; every row cites its evidence or says why it cannot.

Seeded 2026-09-16 from README.md, docs/changes/README.md's known unknowns, the pillars and docs/best-practices.md § 16. Items marked "if v1 includes MP" wait on NEEDS-GREY D-002.

## Scope

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| S1 | v1 scope decided: singleplayer-only or with multiplayer | done | D-002, Grey 2026-09-17: "Singleplayer-only v1" (NEEDS-GREY § ANSWERED) | Grey |
| S2 | Theme decided (pillars open question) or explicitly deferred past v1 | grey | pillars § Open questions | Grey |
| S3 | Win conditions for the shipped modes decided | grey | pillars § Open questions | Grey |

## Stability

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| T1 | Suite green on main, 0 failures, every `[Ignore]` justified | done | 2026-09-18 shift 8, main @ e2d2cf6b on the rig: EditMode 543/543, PlayMode 152/153, 0 failures, 0 inconclusive; the one skip (`MatchFlowTests.SpawnBot`, BACKLOG 2 → CHG-007) is documented in its `[Ignore]` message, so every skip is justified; the Hover Tank quarantine emptied with CHG-013 (docs/changes/183) and its matcher is self-tested (CHG-016, 188); the benchmark coin fixed at its cause (CHG-012, 181). Residual, named not hidden: F-038 (the first post-recompile SurfaceNets window can read ~0.94 ms and passes only by best-of-three; SPIKES L7 owed). Re-checked by the rung-0 suite every shift; flips back to `open` on any red or unjustified skip. History: after CHG-008 (2026-09-17, docs/changes/178-preset-coverage.md): 14 presets validated, both lists one source; EditMode green, PlayMode 152/153 (1 documented skip). After CHG-012 (docs/changes/181): the benchmark coin's cold-cache cause is fixed at the cause (reproduced 3/3, fixed 5/5); a post-recompile residual remains (F-038: first run after a recompile read 0.942 ms best, passing by best-of-three). Still open: F-038; F-026, the Hover Tank quarantine → CHG-013 built and green, waiting on Grey (APPROVE: the fix is visible). Suite after CHG-015/012: EditMode 537/537, PlayMode 152/153, 82–100 s | factory |
| T2 | Zero console errors on load of every shipped scene | done | console sweep 2026-09-17 over the live bridge (F-030): 0 errors, 0 warnings on the load of all six shipped scenes in the factory Editor on main @ 8751b11e; re-checked by the console sweep every shift. Scope: in-Editor load; a player build's first-run log is B3 | factory |
| T3 | Crash-free soak: N minutes of bot-vs-bot play, no exceptions, GC/frame 0 B | unknown | instrument first (BACKLOG 6) | factory |
| T4 | Save / blueprint format versioned; an old save loads or fails loudly | open | atomic writes DONE 2026-09-19 (CHG-006, docs/changes/203: blueprints, concoctions and tweakables go through .tmp + File.Replace with a .bak); still open: format versioning / an old save fails loudly (unmeasured), the tweakables-defaults gotcha (BACKLOG 3) | factory |

## Performance (docs/best-practices.md § 16, on the target class: GTX 1660, 6-core CPU)

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| P1 | Arena populated: frame time ≤ 16.6 ms target, never past the 33 ms cliff | unknown | idle baseline 5.13 ms (2026-08-17); populated unmeasured | factory |
| P2 | GC 0 B/frame in steady-state play | unknown | harness rows show 0 B idle | factory |
| P3 | Draw calls < 1,500, SetPass < 200, PhysX < 2 ms per FixedUpdate | unknown | render probe rows exist for 592 blocks | factory |
| P4 | Numbers measured on target-class hardware, not only Grey's desktop | grey | needs a second machine or a downclock plan; DECIDE when it matters | Grey |

## Build and first run

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| B1 | A Windows player build succeeds from the CLI or `manage_build`, reproducibly | done | 2026-09-18 (CHG-029 the instrument, docs/changes/197; CHG-030 the fix, 198): `build-player.sh <sha>` twice on one sha → `[PLAYER-BUILD] result=Succeeded totalSize=282090782 errors=0 warnings=29 scenes=6` both times, `Builds/Windows` 282,380,832 B both times (identical). The first build ever attempted (CHG-029) failed with 197 errors because the PlayMode test assembly was compiled into the player (F-058, fixed by one asmdef define constraint). Caveat named: the batch Editor crashes in its `-quit` teardown AFTER the report (exit code 21, F-059, SPIKES L8); the row, not the exit code, is the verdict until that is understood. Re-run on every release-shaped change | factory |
| B2 | Build size and load time recorded with a band | open | first numbers 2026-09-18 (two builds on one sha, the rig, Windows64, BuildOptions.None, Library warm): size 282,090,782 B (282.1 MB) both runs (band 0); build time 312.6 s cold / 11.0 s warm incremental (two points, not yet a band); 29 compiler warnings (third-party MidiPlayer/Water, F-027 NOTE). Third build 2026-09-19 on main @ 85202018 (after CHG-006/034): 282,093,848 B (+3,034 B), 43.0 s with a warm Library after a rig reset. Load time: still unmeasured (the first-run log carries no timestamps; add `-timestamps` to first-run.sh) | factory |
| B3 | First run: bootstrap → garage → arena without a dev HUD or a console | open | an executable exists since CHG-030 (the rig's `Builds/Windows/Robogame.exe`, rebuilt on demand; wiped by every rig reset). First-run log NOT captured on 2026-09-18: launching a player on Grey's desktop while he is at it would take the screen and, without an Editor mute, the speakers (PT-001's complaint); plan: run it `-batchmode -nographics -logFile <path>` for 30 s and read Player.log, after checking whether the player's audio init honours batch mode (a 30 s music blast is the risk); CHECKED shift 9 (2026-09-18): FMOD has no batch guard (F-062), so the headless run WOULD play music; B3 waits on a mute flag the player honours; the DevHud straggler note (BACKLOG 1) is stale, DevHud is dev-only UI behind its own toggle; FIRST CAPTURE 2026-09-19 (shift 10): CHG-034's `-factory-mute` landed (docs/changes/204) and `.claude/scripts/first-run.sh` ran the built player `-batchmode -nographics -factory-mute` for 30 s: Bootstrap → MainMenu, 0 errors, 0 exceptions, the `[CommandLineMute]` line present, one FMOD listener notice (F-065); log `.utmp/factory/first-run/20260919T052700Z/Player.log`. STILL OPEN: garage → arena needs a scripted route (nobody presses Play in a headless run): the next instrument | factory |
| B4 | Options: resolution, fullscreen, volume, key rebinding, controller | unknown | Input System is in; rebinding UI unknown | factory |
| B5 | Pause, quit, return-to-garage flows | unknown | | factory |
| B6 | Onboarding for a pilot in the first five minutes (pillars: pickup-and-play) | unknown | a PLAY question once a candidate exists | factory / Grey |

## Content within the pillars

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| C1 | Shipped arenas (flat / spherical / water) each pass the visual audit against art-direction.md | grey | first visual sweep 2026-09-17 (Garage + Arena captured; Water, Planet, MainMenu not: the bridge died mid-sweep): 0 findings, because art-direction.md's own status banner suspends the palette lock and the style Forbidden List (style exploration mode since July 4, 2026); the sweep has no rulebook until Grey answers NEEDS-GREY D-006 | Grey / factory |
| C2 | Every block, weapon and chassis preset has VFX + audio (INV-8) | unknown | missing-cue logger | factory |
| C3 | Enough presets that a pilot can play without building first | unknown | counted 2026-09-18: 14 shipped presets, all validated by the suite (CHG-008/013, `PresetBlueprintTests.PresetPaths`; the ASCII snapshot docs/blueprint-snapshots/presets.md shows each); whether 14 is "enough" is Grey's taste call and becomes a DECIDE when the B rows (a build a pilot can launch) exist, not before | factory / Grey |

## Provenance (I6)

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| L1 | Every imported asset has a recorded license (art-direction.md § Imported Assets, PACKAGE_MODIFICATIONS.md, the soundfont, Universal Sound FX, Kenney packs) | done | 2026-09-17: the provenance sweep's 15 locations all carry a license or origin record: 10 passed on 2026-09-16, three Asset Store packs got rows (CHG-001, docs/changes/177-provenance-records.md, per Grey's D-004), two unlicensed packs were deleted (CHG-005), the 33 generated FBX have a machine-checked manifest (artgen/README.md + ArtgenManifestTests), the editor-only MCP package has an origin row. Re-checked by the provenance sweep on every import | factory |
| L2 | Every third-party package edit is documented and re-appliable | done | Fluff's two shader edits documented (PACKAGE_MODIFICATIONS.md); the only other non-registry package, `com.coplaydev.unity-mcp`, has an origin row (MIT) there since CHG-001 (docs/changes/177-provenance-records.md) and carries no edits | factory |

## Steam

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| ST1 | Steamworks App ID provisioned | grey | docs/changes/README.md: gated on Grey | Grey |
| ST2 | Bindings chosen (Facepunch.Steamworks vs Steamworks.NET) | grey | netcode.md § 14 | Grey |
| ST3 | Store assets (capsule art, screenshots, trailer) | grey | Grey's kept domain (art) | Grey |
| ST4 | Steam overlay and quit-to-desktop behave in a build | unknown | after ST1 | factory |

## Multiplayer — not in v1 (D-002, 2026-09-17: singleplayer-only); rows kept for a later version

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| M1 | Phase 2 Relay + Lobby | n/a | v1 is singleplayer (D-002); README roadmap: planned for later | factory |
| M2 | Phase 5 Steam lobby + transport | n/a | v1 is singleplayer (D-002); needs ST1/ST2 later | Grey / factory |
| M3 | Phase 6 dedicated-server deployment decision | n/a | v1 is singleplayer (D-002); Multiplay / Hathora is Grey's billing decision later | Grey |
| M4 | 16-player bandwidth and tickrate budgets (§ 16) measured under the latency matrix | n/a | v1 is singleplayer (D-002) | factory |

## Docs

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| D1 | README.md current | done | CHG-002, 2026-09-17 (docs/changes/175-doc-drift-sweep.md): date current, Changelog points at docs/changes/README.md; the doc-drift sweep re-checks weekly | factory |
| D2 | docs/changes/architecture.md matches the code | done | CHG-002, 2026-09-17 (docs/changes/175-doc-drift-sweep.md): `PlanetGravity` → `GravityField`; the 2026-09-16 sweep found no other contradiction; re-checked weekly | factory |
