# 📚 Robocraft Reference

> Game design reference for Robogame, drawn from the now-defunct **Robocraft** by Freejam Games (2014–2025).
> This is design research, **not** a copy-paste spec — Robogame should respect the spirit while making its own decisions.

---

## 📋 Table of Contents

- [Game Summary](#game-summary)
- [Timeline & Fate](#timeline--fate)
- [Core Pillars](#core-pillars)
- [Robot Construction](#robot-construction)
- [Movement Parts](#movement-parts)
- [Weapons](#weapons)
- [Modules (Active Abilities)](#modules-active-abilities)
- [Damage & Destruction](#damage--destruction)
- [Energy & Cooldowns](#energy--cooldowns)
- [Game Modes](#game-modes)
- [Progression & Economy](#progression--economy)
- [Notable Era Changes](#notable-era-changes)
- [What Robogame Should Borrow](#what-robogame-should-borrow)
- [What Robogame Should Avoid](#what-robogame-should-avoid)
- [Open Design Questions](#open-design-questions)
- [Sources](#sources)

---

## Game Summary

**Robocraft** was a free-to-play vehicular combat / third-person shooter built in Unity by UK studio **Freejam Games**. Tagline: **"Build, Drive, Fight."** Players assembled robots from block-based parts (cubes, wheels, hovers, jets, mech legs, helicopter blades, weapons, modules) inside a garage, then took them into team-based PvP arenas.

- **Engine:** Unity
- **Platforms:** Windows, macOS, Linux, Xbox One
- **Release:** Alpha 2013 → 1.0 on August 23, 2017
- **Closure:** Servers shut down January 2025 alongside Freejam's studio closure

---

## Timeline & Fate

| Year | Milestone |
|------|-----------|
| 2013 | First alpha |
| 2014 | 300k+ players; tank tracks + Tesla Blade introduced |
| 2015 | "Dawn of the Megabots", "Respawned and Overclocked" (Protonium reactors, Fusion Shields), "Full Spectrum Combat" (Unity 5 upgrade, paint, armor cube collapse) |
| 2016 | "Maximum Loadout" (multi-weapon-type bots), "Epic Loot" (currency switch to Robits, crates), "Battle for Earth" (new TDM mode) |
| 2017 | 1.0 full release |
| 2018 | Tech Tree replaces crates |
| 2019 | F = MA physics-based handling, weapon upgrade system, dev focus shifts to Robocraft 2 |
| 2023 | Robocraft 2 enters early access (poor reception) |
| 2024 | Robocraft 2 delisted, "rebuild from scratch" announced |
| 2025 | Freejam closes; both games delisted and servers shut down |

---

## Core Pillars

1. **Build** — voxel-style robot construction in a garage scene, snapping cubes and components onto a structural lattice.
2. **Drive** — physics-driven movement with wildly different feels (wheels vs. hovers vs. jets vs. mech legs vs. helicopter blades).
3. **Fight** — team-based arena PvP where the robot you built **is** your loadout. Damage tears chunks off your build in real-time.

---

## Robot Construction

### The Garage
- Free-form 3D building space.
- Robots are built around a **CPU block** (the "brain"). Destruction of CPU = loss of control.
- Builds were limited by a global **CPU budget** (later: **pFLOP / power**). Default cap: **2000 CPU**. Anything larger = "Megabot" (Custom Games / vs AI only).
- Each part type costs a different amount of CPU (e.g. heavy weapons cost more, structural cubes cost little).

### Block Types (rough taxonomy)
- **Structure / Armor cubes** — load-bearing, damage-soaking, shaped (cubes, slopes, rods, struts).
- **CPU** — the heart. Required. One per bot.
- **Movement parts** — wheels, hovers, jets, rotors, mech legs, tank tracks.
- **Weapons** — see [Weapons](#weapons).
- **Modules** — active abilities (shields, blink, EMP, ghost, etc.).
- **Cosmetics** — painted cubes, decals, holo-flags.

### Connectivity & "the structural graph"
- Cubes had to be connected to the CPU through a chain of other cubes.
- Players exploited this with **"tri-forcing" / "rod-forcing"** — using thin rods or struts to control how damage propagated through the block graph (since adjacent blocks took splash damage).
- Disconnect a chunk from the CPU → it falls off.

---

## Movement Parts

| Part | Behavior |
|------|----------|
| **Wheels** | Ground vehicles. Drive forward/back, turn via differential or steering. Different sizes / grip / hover. |
| **Tank Tracks** | Heavy ground; slower, tougher, no individual wheels to lose. |
| **Hovers** | Hover above ground at fixed altitude; strafing movement, no terrain hugging needed. |
| **Aerofoils / Wings + Thrusters** | Plane-style flight; lift depends on speed/orientation. |
| **Rotors / Helicopter Blades** | Vertical takeoff and copter handling; spin-up time. |
| **Mech Legs** | Bipedal/quadrupedal walking; can jump; harder to balance. |
| **Sprinter / Insect Legs** | Faster but lighter walking. |
| **Skis** | Frictionless slide on ground; combine with thrusters. |
| **Jets / Thrusters** | Pure thrust forces, no stable hover; for boats, planes, jump jets. |

> Different movement parts couldn't always be mixed freely — there were soft incompatibilities (e.g. wheels and helicopter blades didn't mesh).

> **2019 F=MA update:** mass/inertia became real. Big bots turn slowly, small bots turn fast. Center-of-mass was visible in the editor.

---

## Weapons

All weapons hooked into a shared **energy** pool with per-shot energy cost. Most had rarity tiers with stat variants.

| Weapon | Style | Notes |
|--------|-------|-------|
| **Laser (SMG)** | Hitscan auto | Front- or top-mounted variants |
| **Plasma Launcher** | Lobbed AOE grenade | Arcing, splash damage |
| **Rail Cannon** | Sniper | Single high-damage shot, very low ROF, perfect accuracy |
| **Nano Disruptor** | Healing beam | Cannot damage; heals teammates at short range |
| **Tesla Blade** | Melee blade | Heavy contact damage; defining "vehicle melee" weapon |
| **Aeroflak** | Anti-air | Projectiles only detonate near flying targets; multi-hit damage stacking |
| **Proto-Seeker** | Short-range homing | Many small lock-on projectiles, high ROF |
| **Lock-on Missile Launcher** | Guided missiles | 2–3s lock time, missiles persist after lock loss |
| **Ion Distorter** | Shotgun | Massive damage, very short range |
| **Chain Shredder / Splitter** | Minigun | Spin-up to high ROF |
| **Mortar / Gyro Mortar** | Indirect artillery | Locked aim arcs, area denial |

### Loadout Rules
- Pre-2019: up to **5 weapons / mixed types** (post-"Maximum Loadout" 2016).
- Post-2019: max loadout reduced to **3 weapons**.
- All weapons of one fire group fired together (linked triggers).

---

## Modules (Active Abilities)

Single-use / cooldown items that consume the same energy pool as weapons:

- **Disc Shield Module (DSM)** — drops a temporary stationary shield; allies can fire through.
- **Blink Module (BLM)** — short-range warp; high energy cost, short cooldown.
- **EMP Module** — area stun; disables movement & weapons of enemies in radius.
- **Ghost Module** — temporary invisibility, drains energy.
- **Window Maker Module** — wall-hack reveal of nearby enemies for ~7.5 seconds.
- **Weapon Energy Module** — passive: faster energy regen.

---

## Damage & Destruction

This is the system that made Robocraft **Robocraft**.

- **Part-based damage** — every block has its own HP. No global health bar.
- **Destruction threshold** — a robot is "destroyed" when **75% of its CPU** has been removed.
- **Splash propagation** — damaging one block also chip-damages adjacent connected blocks. This is what makes block layout strategic.
- **Functional disable** — destroyed weapon = can't fire it. Destroyed leg/wheel = lopsided handling. Players could **disable parts surgically** without killing the bot.
- **Auto-repair** — after 10 seconds out of combat, blocks regenerate. Damage resets the timer.
- **Respawn shield** — bubble of invulnerability for a few seconds after spawning to prevent spawn camping.

> ⚠️ For Robogame, this means the **block graph** is the central data structure. It's not just a visual scene graph — connectivity, neighbor lookups, and propagation queries are hot paths.

---

## Energy & Cooldowns

- Single shared **weapon/module energy pool** per bot.
- Shooting drains it; not shooting refills it.
- Combat rhythm is dictated by energy economy: poke → reposition → poke. Constant fire = empty pool = vulnerable.

---

## Game Modes

| Mode | Format | Win condition |
|------|--------|---------------|
| **Test** | Solo sandbox | None — just drive around |
| **AI Bots Deathmatch / Play vs AI** | 5v5 with bots filling teams | TDM rules vs AI |
| **Team Deathmatch** | 5v5 PvP, 5s respawn | First to 15 frags |
| **Elimination** | 10v10, no respawns | Wipe enemy team OR cap their base |
| **Battle Arena / League Arena** | Capture-and-feed | Charge your **Protonium Reactor** by holding 3 control points; trigger the **Annihilator** to destroy enemy base. **Equalizer** crystal spawns for the losing team to catch up. |
| **Brawl** | Rotating ruleset | Wacky mode of the week (slow time, weapon restrictions, large teams, etc.) |
| **Custom Game** | Configurable | Only mode that allowed Megabots |

---

## Progression & Economy

- **Robits** — soft currency, earned per match, used to buy parts and garage slots.
- **Tech Points** — spent in the **Tech Tree** (added 2018, replacing crates) to unlock new parts.
- **Garage Bays** — each holds one robot build; bays could be upgraded to higher CPU caps (up to Megabot 10,000 CPU).
- **Weapon Power / Upgrades** (2019) — weapons earned XP from use; up to 5 upgrade tiers per weapon.
- **Premium / Season Pass** — paid track for cosmetic / faster progression rewards.

> Robocraft cycled through monetization models: tech tree → crates → tech tree again. The community hated crates; the tech tree stuck.

---

## Notable Era Changes

- **2015 "Full Spectrum Combat"** — collapsed the old armor *tier* system (where higher-tier cubes had more HP) into a single armor type with multiple shapes. Removed the iconic Pilot Seat (controversial).
- **2016 "Epic Loot"** — eliminated the tech tree, switched to crates and Robits. Heavily criticized.
- **2018 Tech Tree return** — crates removed; deterministic unlocks restored.
- **2019 F=MA** — physics handling overhaul; mass and inertia matter.
- **2023 Robocraft 2** — released, immediately panned for being slow and content-light.
- **2024** — RC2 rebuild attempt.
- **2025** — Freejam closure; both games offline.

---

## What Robogame Should Borrow

✅ **Block-graph as source of truth.** Connectivity tracking + splash propagation + functional disable is the mechanical core. Without it, it's just another shooter with a build phase.

✅ **CPU/power budget.** A single number that says "your build is too big" is more elegant than weight, slot count, or category limits combined.

✅ **Modular movement profiles.** Wheels, hovers, jets, legs as drop-in components with the same interface — exactly the `IMovementProvider` pattern in our README.

✅ **Shared energy pool for weapons + modules.** One resource, many decisions. Clean and readable in combat.

✅ **Auto-repair + respawn shield.** Cheap ways to soften early-game frustration without dumbing down combat.

✅ **Center-of-mass visualization in the garage.** F=MA-style. Helps players understand why their build flips.

✅ **Garage / Arena scene split.** Already in our scene plan (`Garage.unity`, `Arena.unity`).

---

## What Robogame Should Avoid

❌ **Crate/loot box economies.** Robocraft burned trust badly with this. Tech-tree-style deterministic unlocks are non-negotiable.

❌ **Premium-locked cosmetics that used to be free.** Painted cubes drama. Don't take things away.

❌ **Removing iconic features mid-cycle.** The Pilot Seat removal alienated long-term players. Deprecate carefully and visibly.

❌ **Pretending physics is deterministic.** PhysX isn't, especially across machines. The README already calls this out.

❌ **Megabot / Custom-only content gating.** Splitting "real" play from "fun toy" play discouraged build experimentation. If we cap CPU, custom modes should still feel first-class.

❌ **Crate-driven weapon balance churn.** Robocraft constantly rebalanced rarity tiers; players lost track. Prefer a flat, transparent stat system.

---

## Open Design Questions

These are decisions Robogame still needs to make. Robocraft made *one* answer; we may make a different one.

1. **Voxel grid resolution.** Robocraft used relatively large cubes. Smaller voxels = more detail but exponentially harder physics + netcode.
2. **Smooth or discrete shapes?** Robocraft eventually shipped slopes, rods, and curved tiles. Where does Robogame draw the line between voxel and "kit-bash"?
3. **Single energy pool vs per-weapon ammo.** Energy pool is elegant but flattens weapon identity. Per-weapon ammo is more typical-shooter but adds UI clutter.
4. **Auto-repair on/off.** It enables longer fights but also rewards passive play (running away to heal).
5. **CPU/power budget value.** A single number is elegant but creates strict meta optimization. Multiple sub-budgets (movement, combat, structure) might encourage more diverse builds.
6. **How authoritative is the host on building?** Does the server validate every block placement, or trust the client and validate on load?
7. **Sequel-style "modern rebuild" risks.** Robocraft 2 launched with prettier visuals but less depth and tanked. Visual polish is not a substitute for the build/destroy core loop.

---

## Sources

- [Robocraft — Wikipedia](https://en.wikipedia.org/wiki/Robocraft)
- [Robocraft Steam Page](https://store.steampowered.com/app/301520/Robocraft/) (delisted but archived)
- [Freejam Studio Closure announcement](https://store.steampowered.com/news/app/301520/view/569242775933944076) — Jan 20, 2025

> Wikipedia content is licensed under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). Summarized and reorganized here for design reference.

---

*Last updated: April 29, 2026*
