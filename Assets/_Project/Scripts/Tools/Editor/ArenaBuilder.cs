using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Decorates the Arena scene with real props from the Kenney kits — a
    /// terrain ring of grass/stone blocks, a horizon of distant industrial
    /// buildings, and deterministic scatter cover (crates, barrels, rocks).
    /// Replaces the four primitive walls and bare plane with something
    /// that reads as "a place" instead of "a cube floor and four cubes."
    /// </summary>
    /// <remarks>
    /// <para><strong>Determinism:</strong> a fixed seed drives every random
    /// pick so re-running Build All Pass A produces the same arena. Bump
    /// <see cref="Seed"/> to roll a new layout.
    /// </para>
    /// <para><strong>Layered staging:</strong>
    /// <list type="number">
    /// <item>Combat plane (centre, flat, ~220m): kept from the legacy
    /// builder — collision is reliable and AI navigation already works
    /// on it.</item>
    /// <item>Terrain ring (medium-distance, raised): grass-block chunks
    /// from the platformer kit form a low cliff around the play area.
    /// Reads as a natural perimeter, blocks line-of-sight on the
    /// horizon edge.</item>
    /// <item>Horizon city (far, decorative): industrial buildings ring
    /// the terrain at 4× scale, far enough away that they're silhouettes
    /// against the skybox. No colliders — pure mood.</item>
    /// <item>In-arena cover: crates / barrels / rocks scattered in
    /// concentric bands, leaving the spawn pad and the central kill zone
    /// clear.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static class ArenaBuilder
    {
        // -----------------------------------------------------------------
        // Layout knobs — change these and re-run Build All Pass A.
        // -----------------------------------------------------------------

        private const int    Seed             = 8675309;

        // Combat plane size (radius in world units, square arena).
        private const float  PlayHalfExtent   = 90f;

        // Terrain ring: radius from centre, plus how many blocks per side.
        private const float  TerrainRingRadius = 100f;
        private const int    TerrainRingPerSide = 22;
        private const float  TerrainBlockScale  = 5f;

        // Horizon city: distant industrial silhouettes.
        private const float  HorizonRadius     = 240f;
        private const int    HorizonBuildings  = 36;
        // Horizon buildings: stock industrial FBXs are ~1.5 m tall in
        // metres (verified from OBJ siblings). At radius 240 they need
        // a hefty multiplier to read as proper factory silhouettes.
        // 8–12× → 12–18 m tall buildings, comfortably visible.
        private const float  HorizonScaleMin   = 8f;
        private const float  HorizonScaleMax   = 12f;

        // Cover scatter inside the play area.
        private const int    CoverPropCount    = 28;
        private const float  CoverInnerExclusion = 12f;  // keep spawn clear
        private const float  CoverOuterRadius    = 80f;

        // -----------------------------------------------------------------
        // Asset name lists — picked from the kit inventories.
        // The more variants per slot, the more visual variety per scatter.
        // -----------------------------------------------------------------

        // Platformer kit: chunky grass/stone terrain blocks for the ring.
        // Mixing low + tall + corner shapes keeps the silhouette unsquare.
        private static readonly string[] TerrainBlocks =
        {
            "block-grass-large",
            "block-grass-large-tall",
            "block-grass-low-large",
            "block-stone-large",
            "block-stone-large-tall",
            "block-stone-low-large",
        };

        // Cover props — small enough to crouch behind, varied enough that
        // 28 of them don't read as "same crate everywhere".
        private static readonly string[] CoverProps =
        {
            "crate",
            "crate-strong",
            "barrel",
            "rocks",
            "stones",
            "tree-pine-small",
            "tree-pine",
        };

        // Industrial kit: buildings + chimneys for the horizon. Chimneys
        // are tall + thin so they punctuate the building skyline.
        private static readonly string[] HorizonProps =
        {
            "building-a", "building-b", "building-c", "building-d",
            "building-e", "building-f", "building-g", "building-h",
            "building-l", "building-n", "building-q", "building-s",
            "chimney-large", "chimney-medium", "detail-tank",
        };

        // -----------------------------------------------------------------
        // Public entry — called from BuildArenaPassA.
        // -----------------------------------------------------------------

        public static void DecorateArena()
        {
            // Make sure every Kenney FBX is at the right scale before we
            // try to spawn it. Idempotent.
            KenneyKit.EnsureImportSettings();

            var rng = new System.Random(Seed);

            GameObject root = ScaffoldUtils.GetOrCreate("ArenaDecor");
            // Wipe previous decor so re-runs don't stack hundreds of
            // duplicate barrels.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            BuildHorizonCity(root.transform, rng);
            BuildTerrainRing(root.transform, rng);
            BuildCoverScatter(root.transform, rng);

            Debug.Log("[Robogame] ArenaBuilder: decor pass complete.");
        }

        // -----------------------------------------------------------------
        // Horizon: ring of industrial buildings + chimneys far from spawn.
        // -----------------------------------------------------------------

        private static void BuildHorizonCity(Transform parent, System.Random rng)
        {
            var horizonParent = new GameObject("Horizon").transform;
            horizonParent.SetParent(parent, worldPositionStays: false);

            float angleStep = 360f / HorizonBuildings;

            for (int i = 0; i < HorizonBuildings; i++)
            {
                // Jitter angle a bit so the ring doesn't read as perfectly
                // regular spokes.
                float angleDeg = i * angleStep + (float)(rng.NextDouble() - 0.5) * angleStep * 0.6f;
                float radius   = HorizonRadius + (float)(rng.NextDouble() - 0.5) * 30f;

                Vector3 pos = new Vector3(
                    Mathf.Cos(angleDeg * Mathf.Deg2Rad) * radius,
                    0f,
                    Mathf.Sin(angleDeg * Mathf.Deg2Rad) * radius);

                // Face the building roughly toward arena centre so the
                // tallest face is visible (skyline read).
                Quaternion rot = Quaternion.LookRotation(-pos.normalized, Vector3.up);
                rot *= Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 60f, 0f);

                float scale = Mathf.Lerp(HorizonScaleMin, HorizonScaleMax, (float)rng.NextDouble());

                string propName = HorizonProps[rng.Next(HorizonProps.Length)];
                var prefab = KenneyKit.Industrial(propName);
                var go = KenneyKit.Spawn(prefab, horizonParent, pos, rot, scale);
                if (go == null) continue;

                // No collisions on the horizon — they're just silhouettes.
                StripColliders(go);
            }
        }

        // -----------------------------------------------------------------
        // Terrain ring: square wall of grass/stone blocks around the
        // playable area. Each side is a row of blocks at TerrainRingRadius.
        // -----------------------------------------------------------------

        private static void BuildTerrainRing(Transform parent, System.Random rng)
        {
            var ringParent = new GameObject("TerrainRing").transform;
            ringParent.SetParent(parent, worldPositionStays: false);

            float spacing = (TerrainRingRadius * 2f) / TerrainRingPerSide;
            float startCoord = -TerrainRingRadius + spacing * 0.5f;

            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < TerrainRingPerSide; i++)
                {
                    Vector3 pos = side switch
                    {
                        0 => new Vector3(startCoord + i * spacing, 0f,  TerrainRingRadius),  // North
                        1 => new Vector3(startCoord + i * spacing, 0f, -TerrainRingRadius),  // South
                        2 => new Vector3( TerrainRingRadius, 0f, startCoord + i * spacing),  // East
                        _ => new Vector3(-TerrainRingRadius, 0f, startCoord + i * spacing),  // West
                    };

                    string propName = TerrainBlocks[rng.Next(TerrainBlocks.Length)];
                    var prefab = KenneyKit.Platformer(propName);

                    // Rotate around Y in 90° steps so adjacent blocks
                    // don't all share the same orientation seam.
                    Quaternion rot = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
                    var go = KenneyKit.Spawn(prefab, ringParent, pos, rot, TerrainBlockScale);
                    if (go == null) continue;
                }
            }
        }

        // -----------------------------------------------------------------
        // Cover scatter — concentric polar bands so cover density falls
        // off near the centre (so spawn isn't claustrophobic).
        // -----------------------------------------------------------------

        private static void BuildCoverScatter(Transform parent, System.Random rng)
        {
            var coverParent = new GameObject("Cover").transform;
            coverParent.SetParent(parent, worldPositionStays: false);

            int placed = 0;
            int tries  = 0;
            var taken  = new List<Vector3>(CoverPropCount);
            const float minSpacing = 6f;

            while (placed < CoverPropCount && tries < CoverPropCount * 8)
            {
                tries++;

                // Polar sample with sqrt for uniform area distribution.
                float t      = (float)rng.NextDouble();
                float radius = Mathf.Lerp(CoverInnerExclusion, CoverOuterRadius, Mathf.Sqrt(t));
                float angle  = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 pos  = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                // Spacing check — avoid stacking props on top of each other.
                bool tooClose = false;
                foreach (var t2 in taken)
                {
                    if ((t2 - pos).sqrMagnitude < minSpacing * minSpacing) { tooClose = true; break; }
                }
                if (tooClose) continue;

                string propName = CoverProps[rng.Next(CoverProps.Length)];
                var prefab = KenneyKit.Platformer(propName);
                Quaternion rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                // Crates are bigger than rocks, but the kit is already
                // metre-scaled — variation comes from props.
                var go = KenneyKit.Spawn(prefab, coverParent, pos, rot, 1.0f);
                if (go == null) continue;

                taken.Add(pos);
                placed++;
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static void StripColliders(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(col);
        }
    }
}
