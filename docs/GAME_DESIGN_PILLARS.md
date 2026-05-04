# Robogame — Game Design Pillars

> Committed design directions and open questions. Read this before proposing new features. The committed list constrains design space; the open list flags decisions still being made (don't accidentally lock them in).
>
> Format: each pillar is one sentence of *what*, one paragraph of *why*, and a *how to apply* line for future Claude sessions. Open questions get the same shape but with a *current best guess* instead of a commitment.

---

## Audience

### Pilot- and tinkerer-first

**What.** This game is built for pilots (people who want to drive and shoot) and tinkerers (people who want to play with builds in low-cost iterations). Engineers (people who want depth-first systems they can sink hours into) are a secondary audience at best.

**Why.** Robocraft 1 succeeded with pilots and tinkerers. Robocraft 2 tried to drag engineers in via complex per-part keymapping and lost everyone else in the process. The user-developer is themselves a tinkerer-pilot, not an engineer, and is building the game they want to play.

**How to apply.** When a feature would deepen the engineer experience at the cost of pilot or tinkerer ease, the pilot wins. Default flows must be pickup-and-play. Depth is opt-in, not gated.

### Recreational-but-aspires-Steam

**What.** This is a learning project that aspires to a Steam release. Commercial pressure is low. Goofy-but-fun beats commercial-safe.

**Why.** The user has the runway to swing at weird ideas. The competitive answer to Robocraft is to do something Robocraft never would have.

**How to apply.** Don't design for the median Steam buyer. Design for "what would be fun to ship." Differentiation matters more than polish-of-saturated-genre.

---

## Combat

### Tier 0 and Tier 1 physics weapons; no Tier 2

**What.** Players build weapons by composing existing physics primitives (chains, hinges, pistons, springs, rotors) with the existing block vocabulary. Damage is mass × velocity contact damage with a velocity threshold. Triggered actuation (piston-launched ramming spike, spring-released blade) is in scope. **No** sub-build mode for engineer-tier hand-crafted weapons. There is no in-game catapult editor.

**Why.** Tier 0 and Tier 1 serve all three player tiers (pilots, tinkerers, engineers via emergent complexity) with one small parts vocabulary. Tier 2 (Besiege-style hand-built weapons) is a different game, networks badly (custom physics in MP is a research project), and serves the smallest audience. The physics-weapon space we want is slapstick, not engineering simulation.

**How to apply.** When a player asks "can I build X weapon?", the answer is "yes, by combining existing physics blocks." If the answer would require a new in-game editor, the answer is no. Modular shipped weapons (guns, missiles) coexist with physics-built emergent weapons; both are first-class.

### Generic propulsion primitives, no special-case archetype blocks

**What.** Helicopters, propellers, fans, autogyros, ducted fans, tilt rotors are all built by composing the same primitives: a rotor (kinematic-spinning Rigidbody) plus N aerofoils (rigid children with the existing AeroSurfaceBlock lift formula). No "helicopter rotor" special-case block. Same principle for thrust-based propulsion.

**Why.** Robocraft and clones have a separate special block per propulsion type, each with hand-coded physics. Robogame's existing AeroSurfaceBlock math composes correctly via `Rigidbody.GetPointVelocity` for any spinning-blade case. One rotor + one aerofoil delivers expressivity that one-special-block-per-archetype can't match.

**How to apply.** Resist the temptation to add a new special block for a new propulsion archetype. First check if existing primitives compose to the desired behavior. If they don't, the new primitive should be the smallest possible addition (e.g., a "linear thruster" force-applier, not a "rocket booster" feature-bundle).

### Building is garage-only; blueprints frozen at match start

**What.** Block placement and editing happens in the garage scene. Once a match starts, the blueprint is immutable. Blocks can only be removed during a match (via destruction), never added.

**Why.** This is a hard netcode invariant. Replicating block-index stability requires the blueprint to be deterministic at match start. Allowing in-arena building would require dynamic block-index reassignment over the wire, which is replication-expensive and a class of bug we don't want.

**How to apply.** Build mode is gated to `GameState.Garage`. Any feature that would mutate block layout during a match (other than destruction) is rejected.

---

## Tone & art

### Cel-shaded stylized, locked palette

**What.** MK Toon shader on the 12-token WorldPalette. No realistic textures, no normal-mapped surface detail, no off-palette colors anywhere.

**Why.** See [ART_DIRECTION.md](ART_DIRECTION.md) for the full rationale. Tone-and-engineering compatibility: cel-shading is forgiving of solo-dev rough edges and reads at distance.

**How to apply.** Every new visual element gets reviewed against ART_DIRECTION § Palette and § Forbidden List before it ships.

### Slapstick over realism for combat feel

**What.** Physics weapons are inherently goofy. Lean into it. A chopper with a wrecking ball is Looney Tunes, not military sim.

**Why.** This was the reframing that unlocked Tier 0/1 as the right scope. Pilots and tinkerers respond to "this is fun" more than "this is realistic." It also aligns with the locked-palette cel-shaded direction — the tone is already cartoonish.

**How to apply.** When tuning physics weapons, prioritize "this is fun to use" and "this reads visually" over "this is physically accurate." Visual cues for "this thing is at murder velocity" matter more than realistic weight distribution.

---

## Architecture

### MP-readiness from day one in singleplayer code

**What.** All gameplay code is structured as if a server is the source of truth, even in singleplayer where the local client *is* the server.

**Why.** Retrofitting netcode onto singleplayer-shaped code is the most expensive way to ship a multiplayer game. See [NETCODE_PLAN.md](NETCODE_PLAN.md).

**How to apply.** Ask "would this work if the server were on a different machine?" before committing to a design. Tweakables-as-gameplay-knobs fail this test. Per-machine random seeds fail this test. Client-computed damage fails this test.

---

## Open questions (do NOT lock in without explicit user decision)

### Theme

**Status.** Open. Saturation in airships is real but not a blocker. Pirate ships have engineering investment behind them (water and buoyancy already built). Tonal direction (slapstick) opens space for weirder choices: bathtub navy, toy-box war, carnival war machines. No commitment yet.

**Current best guess.** Defer until the core combat loop is fun. Theme should follow the mechanic, not lead it.

### Splash propagation rule

**Status.** Open. Robocraft used a graph-distance splash that enabled "tri-forcing" exploits. Robogame has not picked a stance.

**Current best guess.** Splash falls off with chain-graph distance, not euclidean distance. Tri-forcing-style sacrificial-strut play emerges naturally as advanced expression. Revisit if it proves degenerate in playtest.

### CPU / power budget shape

**Status.** Open. Single number vs. category sub-budgets (movement, combat, structure). Currently no enforcement at all.

**Current best guess.** Single number with a visible HUD readout *now*, even before enforcement, so players playtest under the constraint. Robocraft's 2000-CPU cap is a reasonable reference. Sub-budgets are a Phase 2+ idea.

### Win conditions

**Status.** Open. Robocraft used 75%-CPU-loss for "destroyed" and a frag count for round win. Robogame's "loose fitness function" instinct (per the Wolfram quote in the user's notes) suggests multiple concurrent objectives.

**Current best guess.** Per-match win-condition variance (TFT-style augment cards that change the rules) is differentiated and serves the design philosophy. Defer until core loop is shippable.

### Per-match modifiers

**Status.** Open. Smite-style weather effects, TFT-style augments, gravity changes, weapon restrictions. Compatible with most other commitments.

**Current best guess.** Strong instinct toward yes, post-core-loop. Not a v1 blocker. The Brawl-mode pattern from Robocraft (rotating weekly ruleset) is a cheap way to ship variety once augments exist.

---

*Last updated: May 4, 2026.*
