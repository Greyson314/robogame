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
| T1 | Suite green on main, 0 failures, every `[Ignore]` justified | open | after CHG-003 (2026-09-17): EditMode 530/530, 0 inconclusive; PlayMode 152/153 (1 skip, documented: `MatchFlowTests.SpawnBot`, BACKLOG 2); 0 failed. Still open: F-021, a Burst benchmark with a hard 1.0 ms gate that failed once at 1.005 ms (flaky = red); F-022, two shipped presets validated by no test (CHG-008) | factory |
| T2 | Zero console errors on load of every shipped scene | unknown | console sweep | factory |
| T3 | Crash-free soak: N minutes of bot-vs-bot play, no exceptions, GC/frame 0 B | unknown | instrument first (BACKLOG 6) | factory |
| T4 | Save / blueprint format versioned; an old save loads or fails loudly | unknown | atomic writes open (BACKLOG 4); tweakables JSON gotcha (BACKLOG 3) | factory |

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
| B1 | A Windows player build succeeds from the CLI or `manage_build`, reproducibly | unknown | never recorded in docs/changes | factory |
| B2 | Build size and load time recorded with a band | unknown | | factory |
| B3 | First run: bootstrap → garage → arena without a dev HUD or a console | unknown | DevHud straggler (BACKLOG 1) | factory |
| B4 | Options: resolution, fullscreen, volume, key rebinding, controller | unknown | Input System is in; rebinding UI unknown | factory |
| B5 | Pause, quit, return-to-garage flows | unknown | | factory |
| B6 | Onboarding for a pilot in the first five minutes (pillars: pickup-and-play) | unknown | a PLAY question once a candidate exists | factory / Grey |

## Content within the pillars

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| C1 | Shipped arenas (flat / spherical / water) each pass the visual audit against art-direction.md | unknown | visual sweep | factory |
| C2 | Every block, weapon and chassis preset has VFX + audio (INV-8) | unknown | missing-cue logger | factory |
| C3 | Enough presets that a pilot can play without building first | unknown | count them; a DECIDE if the number is a taste call | factory / Grey |

## Provenance (I6)

| # | Item | Status | Evidence / note | Owner |
|---|---|---|---|---|
| L1 | Every imported asset has a recorded license (art-direction.md § Imported Assets, PACKAGE_MODIFICATIONS.md, the soundfont, Universal Sound FX, Kenney packs) | in progress | provenance sweep 2026-09-16: 10 of 15 locations pass; the two unused unlicensed packs are gone (CHG-005, docs/changes/176-delete-unused-packs.md); the three wired Asset Store packs' rows + the artgen manifest + the unity-mcp row land with CHG-001 | factory |
| L2 | Every third-party package edit is documented and re-appliable | open | Fluff's two shader edits are documented; `com.coplaydev.unity-mcp` (editor-only) has no origin row (F-007, CHG-001) | factory |

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
