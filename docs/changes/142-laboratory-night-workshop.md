# 142 — Laboratory "night workshop" reskin

**Intent.** Implement the Claude Design "Laboratory" handoff — the
evil-scientist / night-workshop treatment for the concoction screen.
Presentation only: all 141 behaviour (save / two-click delete /
auto-name / ConcoctionColor / CPU surcharge / LabSave cue) is kept.
Spec copied to
[research/ui-design-handoff-laboratory.md](../research/ui-design-handoff-laboratory.md).

## What shipped

- **`LabKit` (new, Core):** screen-scoped night-workshop tokens (bone,
  galvanic accent #7FDCC8, indigo wash, wood/brass) + runtime-baked
  sprites (soot ground, wood, raised plate, glow, circle/ring, 1px
  9-slice border, brass bar/knob/cork, tick strip, tube fill/outline,
  mini-vial, vertical fade). Same bake-once discipline as `InkKit`;
  colour literals live here so the controller stays token-only.
- **`LabController` presentation rebuild** to the handoff layout:
  1060×560 wood panel with brass corner screws over a dark radial
  ground (soot blotches, chalk 28px grid, corner L-ticks). Left:
  recessed Concoctions well — "New Concoction" row + jar rows with
  tinted mini-vial swatches and "no. N" batch annotations; selected row
  gets the indigo wash, a glowing swatch that wears the live mix, and a
  label that chases the name field. Centre: raised switchboard plate —
  five brass-track sliders (ticks every 10%, 9px pip, galvanic accent +
  glow while dragging), sunken name field, bone brushstroke **Save**,
  text-only **Delete** whose armed state is a vermilion strike-through.
  Right: the specimen vial — cork, rolled lip, masked liquid at
  `Concoction.MixedColor`, fill level 30–70% from Size + Spread,
  surface ellipse, three rising bubbles, flicker, liquid-coloured outer
  glow, wax seal (the screen's one vermilion), "fig. 1" caption.

## Deviations from the handoff (deliberate)

- Fonts: Averia Libre + Space Mono (locked user pick, ui-direction.md)
  instead of Yuji Syuku / Xanh Mono.
- Vial hue: `MixedColor`, not the prototype's damage/speed hue formula —
  the pigment identity already dyes shots and names recipes.
- CPU/multiplier formula line kept (handoff computes it and invites
  surfacing); goes galvanic when the surcharge passes +100%.
- 141's journal search filter removed (not in the design; list is small).
- Skipped: Save hover "depress" translate, vial rotateX tilt, accent
  colour options prop (fixed cyan default), glow under the 4px fill bar.

## Verification

- Live play-mode screenshots through four iterations (Garage →
  `lab.Open()`); caught and fixed: children-over-parent render order on
  panel/plate/save shadows, Slider re-stretching the pip (zero-height
  slide area fix), ColorTint × `Color.clear` erasing row highlights,
  Delete's only graphic being raycast-off, seal offset, formula wrap.
- Save → arm → delete round trip through the real methods: 3→4→3
  records, armed flag observed. Console clean (errors + warnings).
- Full suite via run-tests.sh (see result below). qa-verifier subagent
  skipped — its checks (build/tests/console/screenshot) were all run
  inline this session; perf-checker skipped (zero physics objects,
  UI-only; per-frame Lab work stays alloc-free and gated on `IsOpen`).

## Follow-up (same session, user-directed)

- Panel scaled ×1.22 (fills ~⅔ of the screen), deeper elevation:
  two-layer panel drop shadow biased downward, top-edge catchlight,
  stronger sheen/pool, darker well + deeper inset, heavier plate shadow.
- **Background de-blueprinted** per user request (their day job is
  blueprint aesthetics): chalk drafting grid + corner registration
  ticks removed from the Lab ground. Replaced with three procedural
  fog banks (`LabKit.FogA/FogB`, wispy flattened blobs) rolling on
  slow sine swells + vertical bob — a looping 2.5D haze with no
  binary assets. Sines, not a wrap-conveyor, so ultrawide never sees
  a bank teleport. Note: registration ticks remain part of the paper
  screens' language; only the Lab drops them.

## Known limits / next steps

- Screenshot MCP inline previews lift linear→sRGB (washed); the PNGs on
  disk are correct — sample the file, not the preview.
- Garage aim reticle (IMGUI) still draws over the Lab overlay
  (pre-existing, visible in captures).
- Batch "no. N" is an id-hash flavour annotation, not a real counter.
- Accent stays default cyan; wire the green/violet options if wanted.
