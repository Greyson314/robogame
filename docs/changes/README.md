# Robogame — dev log

This directory is the catch-up brief for any future contributor (human
or AI) landing on the project mid-stream.

- Read [architecture.md](architecture.md) first — that's what's true
  *right now*.
- Then skim sessions in **reverse** chronological order (highest number
  first) for the *why* behind recent shape changes.

Style: dev log, not changelog. Each session entry covers user intent,
what shipped, what we learned. File links use repo-relative paths.

## Sessions (newest first)

| # | Title |
|---|---|
| 16 | [Rotor / aerofoil decoupling (WIP — three regressions outstanding)](16-rotor-foil-decoupling.md) |
| 15 | [Rotor follow-ups: tip collider, plane rotor, stress tower, physics plan](15-rotor-followups.md) |
| 14 | [Rotor block + spinning-rope ring + perf-discipline note](14-rotor-block.md) |
| 13 | [Rope block + GUI tweaks polish + momentum impact damage](13-rope-and-momentum-damage.md) |
| 12 | [Bomber preset + Bomb Bay block + health check / docs split](12-bomber-bombbay-and-audit.md) |
| 11 | [Polish: foam wake on chassis + connectivity flood-fill at placement](11-foam-wake-connectivity.md) |
| 10 | [Water visuals: Bitgem shader + Gerstner mesh + DevHud waves slider](10-water-bitgem.md) |
| 09 | [Build mode: in-garage block editor (Pass B Phase 3a)](09-build-mode-editor.md) |
| 08 | [Save/load foundations + "+ New Robot" button (Pass B kickoff)](08-save-load-blueprints.md) |
| 07 | [Phase 1 art pass: cel-shading, post-FX, ambient, skybox](07-art-direction-phase1.md) |
| 06 | [Settings panel + Tweakables registry](06-settings-tweakables.md) |
| 05 | [Plane "feel" pass](05-plane-feel.md) |
| 04 | [HitscanGun MissingReferenceException on Stop](04-hitscan-gun-fix.md) |
| 03 | [Chassis dropdown (Tank / Plane / Buggy)](03-chassis-dropdown.md) |
| 02 | [Launch button, three rounds of debugging](02-launch-button-debug.md) |
| 01 | [Pass A + garage/arena visual identity](01-pass-a-visual-identity.md) |
| 00 | [Background — initial refactor pass (pre-log)](00-background-pre-log.md) |

## Architecture

- [architecture.md](architecture.md) — current modules, runtime flow,
  patterns and gotchas.
