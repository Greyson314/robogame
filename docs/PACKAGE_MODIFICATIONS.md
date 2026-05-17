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

## com.occasoftware.fluff (v2.1.0) — Top-down dig-mask grass clip

### What

Added a global (not per-material) dig mask the modified grass fragment
samples by world-XZ to `discard` grass over columns the player has dug
below the original heightmap. This keeps the decoupled Fluff grass
layer consistent with the carved voxel terrain (the whole arena ground
is now diggable — see `docs/changes/83-*` and `docs/TERRAFORMING_PLAN.md`).

### Why

The full-footprint arena dig zone seeds its voxel SDF surface to the
same Perlin heightmap the Fluff grass mesh is baked from, sunk a small
amount so the opaque grass occludes the voxels while undug. When the
player drills, the voxel surface drops metres below the grass mesh, but
the grass mesh itself doesn't move — so without a clip the grass would
visibly float over the hole. `Robogame.Voxel.DigZone` maintains an
`RFloat` texture (one texel per surface column = "metres dug below the
original surface") and pushes it + a world-XZ→UV mapping as **global**
shader uniforms. The grass fragment discards where the column is dug
deeper than a threshold. Globals (not material properties) mean no
per-scene material wiring, and the defaults are inert so any scene
without a heightmap dig zone renders Fluff exactly as before.

### Files changed

1. **[`Packages/com.occasoftware.fluff/Runtime/Shaders/Grass.hlsl`](../Packages/com.occasoftware.fluff/Runtime/Shaders/Grass.hlsl)**
   — two edits, both bracketed with `// [robogame mod]`:

   - Right **after** `CBUFFER_END` (file scope, OUTSIDE the
     `UnityPerMaterial` cbuffer because these are globals): declared
     `TEXTURE2D(_DigMask)` + `SAMPLER(sampler_DigMask)`,
     `float2 _DigMaskWorldMin`, `float2 _DigMaskWorldInvSize`,
     `float _DigMaskEnabled`, `float _DigMaskClipDepth`.
   - In `Fragment(...)`, right after `IN.normalWS = normalize(IN.normalWS);`:
     an early `if (_DigMaskEnabled > 0.5)` block that maps
     `IN.positionWS.xz` into mask UV, samples the dug depth, and
     `discard`s when it exceeds `_DigMaskClipDepth`. Placed early so
     clipped fragments skip all shell/wind/lighting work.

2. **`Grass.shader`** — *not* modified. Unlike the `_TileWarpStrength`
   mod, these are global uniforms, so no `Properties` entry is needed.

### How to re-apply after a Fluff package upgrade

1. In `Grass.hlsl`, immediately after the `CBUFFER_END` that closes
   `UnityPerMaterial`, paste:
   ```hlsl
   TEXTURE2D(_DigMask);
   SAMPLER(sampler_DigMask);
   float2 _DigMaskWorldMin;
   float2 _DigMaskWorldInvSize;
   float _DigMaskEnabled;
   float _DigMaskClipDepth;
   ```
2. In `Grass.hlsl`'s `Fragment` function, right after
   `IN.normalWS = normalize(IN.normalWS);`, insert:
   ```hlsl
   if (_DigMaskEnabled > 0.5)
   {
       float2 dmUV = (IN.positionWS.xz - _DigMaskWorldMin) * _DigMaskWorldInvSize;
       if (dmUV.x >= 0.0 && dmUV.x <= 1.0 && dmUV.y >= 0.0 && dmUV.y <= 1.0)
       {
           float dugDepth = SAMPLE_TEXTURE2D(_DigMask, sampler_DigMask, dmUV).r;
           if (dugDepth > _DigMaskClipDepth) discard;
       }
   }
   ```
3. No material asset changes — the uniforms are set at runtime by
   `DigZone.UpdateDigMask()` via `Shader.SetGlobal*`.

### Cost

- **One texture sample + a branch** per grass fragment, *only* in
  scenes with a heightmap dig zone (`_DigMaskEnabled` gate). Clipped
  fragments early-out before the shell/wind/lighting work, a net win
  over a dug area.
- **Zero** cost in every other scene (branch not taken).
- **No vertex/geometry shader change. No URP variant change.**

### Tuning

- `_DigMaskClipDepth` (global, set from `DigZone._grassClipDepth`,
  default 2.0 m) — the dug depth is measured from the *seeded* surface,
  so it must comfortably exceed the voxel cell size (1 m in the arena):
  quantisation alone can put an undug column's top ~1 cell low, and
  clipping there would punch grass holes in ground nobody dug. A real
  drill / bomb removes several metres, well past 2 m. Raise it for a
  softer "grass hangs over the lip" look; lower it (but keep > cell
  size) for a crisper dug edge.

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
