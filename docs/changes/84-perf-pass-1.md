# 84 — Performance pass 1 (harness, GC fixes, GPU reductions, chassis batching)

> Status: **pass complete, user-stopped at a good point.** Grew well
> past "baseline + OnGUI": added a Perf-Bisect HUD, disabled the
> outline pass, landed documented GPU reductions (shadows + grass,
> user-confirmed ≈ +30–40 passive fps), and replaced ~150
> per-chassis block renderers with one combined mesh per material.
> See the "Continued" section below; numbers/limitations in
> `docs/PERFORMANCE_BASELINES.md`. User will push.
>
> User intent: execute `PERFORMANCE_PASS_PLAN.md`. User explicitly
> relaxed the plan's "measure-before-fix" gate ("make all changes
> first, analyze after") and approved Phase 4 + Phase 5, then
> narrowed scope to **Arena only** (drop Water/Planet).

## What shipped

**Harness (Phase 0/1, editor-playmode variant).**
[`PerfBaselineHarness`](../../Assets/_Project/Tests/PlayMode/Perf/PerfBaselineHarness.cs)
— a `Perf`-category PlayMode test. Loads Arena and Garage, settles
(240 frames + 2 s), samples 600 uncapped frames, logs frame-time
percentiles + `GC.GetAllocatedBytesForCurrentThread()` delta as a
greppable `[PERF-BASELINE]` line, appends to
`docs/perf-captures/harness-log.txt`. Hard-asserts idle GC < 2048
B/frame so a future per-frame-alloc regression fails loudly. Editor
absolute numbers are 5–10× off (PERFORMANCE.md §3.2) but valid for
the *delta*, which is the falsifiable number the pass needs.

**Fix 1 — ObjectiveHud OnGUI GC.** Was allocating 6× `new GUIStyle`
+ 2× `"/ " + int` concat **per OnGUI call**; OnGUI runs 2–6× per
displayed frame and the Arena scene always has a live match, so this
was steady-state garbage. Variants now pre-built in `EnsureStyles`
(the file's own existing pattern); timer colour set in-place on the
cached style (no alloc); `"/ target"` moved to the dirty-string
pattern already used for the timer/frags lines.

**Fix 2 — ScrapCarriedIndicator OnGUI GC + GetComponent.** `OnGUI`
called `GetComponent<Camera>()` every IMGUI event (PERFORMANCE.md
§2.5 violation) and built `$"⛁ {scrap}"` per visible robot per
event. Camera cached in `Awake`; scrap labels interned in a small
`Dictionary<int,string>` that saturates within a second.

**Fix 3 — PerformanceHud resample cadence.** `_resampleInterval`
1 s → 2.5 s (Phase 4.1). The 3× `FindObjectsByType` scan is already
gated to visible-only, so this only matters when F3 is left on —
it stops the HUD's own scan from contaminating a measurement.

## What was deliberately NOT done (and why)

- **DigZone `_pendingBakes` gate (Phase 4.4)** — `PollBakeAndSwap`
  is one bool check ×36 ≈ µs. The plan §5.1 predicted this exact
  shape. Adding the gate would be dead code (Rule 2/3). Documented
  as confirmed-cheap.
- **DigZone `Camera.main` cache** — initially landed, then reverted
  when scope narrowed to Arena: the LOD branch is off in Arena
  (`_enableLod:0`) and no other in-scope scene has a DigZone. Latent
  footgun (C# default `=true`) flagged in BASELINES, not patched.
- **Phase 5 outline bake / water analytic normals** — their triggers
  are a Frame-Debugger SetPass count and a water-path profile the
  idle harness cannot produce, and both are visible changes
  unverifiable without a human. Per the plan's own "no big rock
  without measurement" gate, deferred despite Phase-5 approval.
  Surfaced rather than blind-landed (Rule 7/12).

Full suspect-by-suspect triage table: `docs/PERFORMANCE_BASELINES.md`.

## Net finding

Matches the plan's "honest picture": per-frame hygiene is good, no
single bottleneck. Two real steady-state GC sources in always-on
Arena overlays — fixed. The deliverable is the trend harness + two
targeted fixes, not a 2× win.

## User follow-ups

1. **Capture the numbers.** Run `PerfBaselineHarness` (`Perf`
   category) via Test Runner, or CLI batchmode with the editor
   closed (command + git-stash before/after procedure in
   `docs/PERFORMANCE_BASELINES.md`). Do **not** use `-nographics` —
   it skips the OnGUI path these fixes target.
2. **Active/combat rows** still need a manual build run (human at
   the controls); append to BASELINES per PERFORMANCE.md §9.
3. The harness asserts idle GC < 2048 B/frame — if it fails on the
   "before" stash that *confirms* the regression these fixes remove.

## Continued — the rest of the pass

After the OnGUI fixes the pass continued, driven by in-game bisect
(the user is the GPU measurement loop; the headless harness proved
**GPU-blind** — CPU render submission for 227 chassis blocks = 6 µs,
so it only guards CPU/GC, not the GPU costs that actually bind).

- **Perf-Bisect HUD** (`Robogame.Core.PerfBisect` + a "Perf Bisect"
  section in `SettingsHud`): non-destructive Esc-menu switches to
  disable AI bots / Fluff grass / dig chunk renderers live for
  empirical A/B against fps. This is what localised the costs.
- **Bisect results:** dig renderers ≈ ruled out; AI/physics modest;
  the big deltas are **grass** and **chassis block rendering** —
  both GPU.
- **Outline pass disabled** (`PC_Renderer.asset` m_Active 0,
  reversible). Long-term intent is a player-only layer-mask
  (§5.4), not deletion — deferred as a user-decision item.
- **GPU reductions (documented, reversible), user-confirmed ≈ +30–40
  passive fps:** shadow cascades 4→2 (§6); hills resolution 121→81
  + mesh rebake (grass input tris 28 800→12 800, ×~22 in the geom
  shader — §5.3's biggest grass lever); Fluff `_ShellCount` 7→6;
  `_FinsEnabled` 1→0 (§5.3 #5, the one visible knob — revert by
  setting it back to 1). Note: Unity re-serialises hand-edited
  `.mat` values when the editor reopens — the rebaked mesh +
  `PC_RPAsset` cascades are the durable carriers.
- **Chassis block batching (`ChassisInstancedRenderer`).** GPU
  instancing was tried first and failed — MK Toon has no instancing
  variant, so `RenderMeshInstanced` drew invisible cubes (the
  flagged shader risk, realised). Pivoted to `Mesh.CombineMeshes`:
  one combined mesh per material under the chassis root, same shader
  path as the visible originals (guaranteed identical), moves with
  the chassis for free (zero per-frame cost). Only full-health
  single-mesh `Structure` blocks; a damaged/destroyed block is
  evicted back to its own renderer + the combined mesh rebuilt
  (debounced). Collapses ~150 draws + their per-cascade shadow
  draws/chassis to one per material. Biggest payoff is the
  16-chassis MP target, not single-machine fps.

### Deliberately NOT blind-landed (Rule 1/7/12)

The SRP/MPB chassis fix (commit 44cf5a1) is kept as correct hygiene
but did **not** move this machine's fps (CPU submission was already
free — the GPU-blind finding explains why). Outline-bake, water
analytic normals, the player-only outline mask, and CPU-beacon
light reduction are surfaced for user/architect sign-off, not
unilaterally landed — they are visible/architectural and
unverifiable headless.

### Methodology note for the next pass

Headless CLI (`PerfBaselineHarness`/`PerfRenderProbe`) measures
CPU/GC/render-submission only. **GPU cost (grass shells, fragment
/overdraw, shadows) is only measurable in the editor Game-view or a
windowed build** — i.e. a human at the controls. Do not trust
headless fps for GPU work; bisect in-game with the Perf-Bisect HUD.
