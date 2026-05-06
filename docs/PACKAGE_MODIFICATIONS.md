# Package modifications

> **Audience.** Anyone (human or AI) updating a third-party Unity
> Package Manager package after a session, or trying to understand
> why one of our packages doesn't match upstream.
>
> **Purpose.** Inventory of the spots where this project has modified
> a vendored package's source. Every entry must include: which package,
> which file, what we changed, *why*, and how to re-apply if the
> package is ever upgraded.
>
> **Rule.** Don't add silent modifications to packages. Either land
> the change here on the same PR, or don't ship the change.

---

## com.occasoftware.fluff (v2.1.0) — Stochastic-tile UV warp

### What

Added a `_TileWarpStrength` material property + uniform that warps
the `_GroundTex` / `_MainTex` UV samples by the existing shape /
detail noise. Hides the regular tile grid that the player otherwise
reads as obvious texture repeats on the 220×220 m hills mesh.

### Why

Fluff samples the ground texture with `uv = positionWS.xz / _WorldScale`
([`Grass.hlsl:533`](../Packages/com.occasoftware.fluff/Runtime/Shaders/Grass.hlsl)),
ignoring the material's Tiling/Offset (`_GroundTex_ST`) entirely. The
only knob for ground texture tiling is `_WorldScale`, which controls
*tile size in metres* — but at gameplay framing (~5–10 m camera
height) any tile-based texture with visible features (brush strokes
in our case, from the imported Handpainted Grass pack) reads as
repeats unless the regularity of the grid is broken up.

Standard fix: domain-warp the UV by a low-frequency noise sample so
the tile boundaries become wobbly curves the eye can't pick out as
repeats. The Fluff fragment shader already pays for two noise
texture samples (`_ShapeNoiseTexture` + `_DetailNoiseTexture`); we
reuse those values for the warp at zero extra texture-sample cost.

The warp pattern itself doesn't repeat within view because the
underlying noise textures are sampled at world-scale ~175 m (shape)
and ~65 m (detail), much larger than a screen.

### Files changed

1. **[`Packages/com.occasoftware.fluff/Runtime/Shaders/Grass.shader`](../Packages/com.occasoftware.fluff/Runtime/Shaders/Grass.shader)**
   — added the `_TileWarpStrength("Tile Warp Strength", Range(0, 1)) = 0.5`
   property after `_GrassDirectionStrength`. Marked with a
   `// [robogame mod]` comment block.

2. **[`Packages/com.occasoftware.fluff/Runtime/Shaders/Grass.hlsl`](../Packages/com.occasoftware.fluff/Runtime/Shaders/Grass.hlsl)**
   — three edits:

   - In the cbuffer (after `_GrassDirectionStrength`): added
     `float _TileWarpStrength;`.
   - In the fragment, right after the noise samples (line ~588):
     captured raw, unbiased shape/detail noise into a `tileWarp`
     `float2` BEFORE the strength-lerp tames them. The lerp pushes
     both values toward 1, which would skew the warp asymmetric
     toward +x +y — using the raw samples gives a symmetric
     warp around zero.
   - In the fragment albedo block (line ~688): replaced the two
     direct `uv.xy` ground/grass texture samples with sampling at
     `warpedUV = uv.xy + tileWarp`. Both `_MainTex` (grass color
     atop shells) and `_GroundTex` (ground colour visible between
     blades) get warped, so the grass tile look matches the ground
     tile look as the eye sweeps across.

   All three edits are bracketed with `// [robogame mod]` comments
   referencing this file.

### How to re-apply after a Fluff package upgrade

If the user ever drops a fresh copy of Fluff over the existing
embedded package and wants to keep this fix:

1. In `Grass.shader`, after `_GrassDirectionStrength`, paste:
   ```
   _TileWarpStrength("Tile Warp Strength", Range(0, 1)) = 0.5
   ```

2. In `Grass.hlsl`, in the cbuffer near `_GrassDirectionStrength`,
   add `float _TileWarpStrength;`.

3. In `Grass.hlsl`, find the noise-sampling block in the Fragment
   function. Right after the two raw noise samples and *before* the
   `lerp(1.0, …, _ShapeNoiseStrength)` lines, insert:
   ```hlsl
   float2 tileWarp = (float2(shapeNoise, detailNoise) - 0.5) * _TileWarpStrength;
   ```

4. In `Grass.hlsl`, find the `// Albedo` block. Change the two
   texture samples from `uv.xy` to `uv.xy + tileWarp` (or compute
   `float2 warpedUV = uv.xy + tileWarp;` once and use that).

5. Confirm that `Mat_ArenaFluff.mat` still has `_TileWarpStrength: 0.5`
   in `m_Floats`. If a new shader version drops the property, Unity
   will re-add it on next material save — no manual fix needed.

If the upstream Fluff project ever ships a built-in stochastic
tiling feature, prefer that over this mod and remove the entries
above.

### Cost

- **One extra `mad` instruction** per fragment (`uv + tileWarp`).
- **Zero extra texture samples** — reuses noise samples the shader
  was already paying for.
- **No additional vertex/geometry shader cost.**
- **No URP variant changes.**

### Tuning

- `_TileWarpStrength: 0` → no warp (off).
- `_TileWarpStrength: 0.5` → moderate wobble (current default).
- `_TileWarpStrength: 1.0` → maximum wobble; can read as visibly
  warped on closeup-flat grass terrain. Past ~1.5 the grass blades
  look visibly stretched.

The warp magnitude in metres ≈ `_TileWarpStrength × 0.5 × _WorldScale`.
At `_TileWarpStrength = 0.5` and `_WorldScale = 32` that's ~8 m —
big enough to pull a tile's edge into the middle of the next, fully
breaking the regular grid.

---

*Add new package modifications to the top of this file (newest
first), with a heading of the form `## <package> (<version>) — <one-line summary>`.*
