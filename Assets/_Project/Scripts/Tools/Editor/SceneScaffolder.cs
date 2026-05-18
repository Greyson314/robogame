using Robogame.Core;
using Robogame.Input;
using Robogame.Movement;
using Robogame.Player;
using Robogame.Robots;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor menu commands that build out our standard scenes from scratch.
    /// Block layouts come from <see cref="GameplayScaffolder"/>'s preset
    /// plans (built via <see cref="ScriptedChassisBuilder"/>); tuning
    /// data lives in ScriptableObject assets created via
    /// <see cref="TuningAssets"/>.
    /// </summary>
    public static class SceneScaffolder
    {
        // -----------------------------------------------------------------
        // Removed: legacy `Build Test Garage` / `Build Test Robot` /
        // `Build Test Plane` / `Build Combat Dummy` menu items. They
        // predated Pass A and overlapped with
        // `Scaffold/Gameplay/Build All Pass A` (also surfaced as the
        // top-level `Robogame/Build Everything` shortcut), which is now
        // the only sanctioned path for authoring the gameplay scenes.
        // PopulateTestTerrain remains below as a public helper because
        // EnvironmentBuilder.BuildArenaEnvironment still calls it.
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // Test terrain
        // -----------------------------------------------------------------

        public static void BuildTestTerrain()
        {
            ScaffoldUtils.OpenScene(ScaffoldUtils.GarageScene);
            EnsureGround();
            EnsureCamera();
            EnsureLight();
            PopulateTestTerrain();
            ScaffoldUtils.SaveActiveScene();
            Debug.Log("[Robogame] Test terrain built.");
        }

        /// <summary>
        /// Drop the arena's boundary walls + scenic mountain ring.
        /// Idempotent: nukes the previous "Terrain" parent and rebuilds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Session 59 strip-down: the central obstacle course (ramps,
        /// bumps, stairs, free-standing pillars) was removed in favour of
        /// an open playfield. The course read as test-bed scenery against
        /// the new larger arena scale and broke combat lines for the
        /// scrap loop (depots couldn't see each other across the field).
        /// In its place we get a wider arena (±170 m wall ring) with a
        /// scenic ring of mountains framing the playable area for visual
        /// landmark + sense of scale.
        /// </para>
        /// <para>
        /// Mountain placement uses deterministic golden-angle sampling so
        /// re-scaffolding lands them in the same spots every time. Each
        /// mountain is a cone (Unity ships Cylinder; we taper it via
        /// scale + a top vertex shrink isn't available — we use a
        /// scaled <c>Cube</c> with a wide base and stacked tier instead,
        /// which reads as a stylised mountain matching the Robogame
        /// blocky art direction).
        /// </para>
        /// </remarks>
        public static void PopulateTestTerrain()
        {
            GameObject root = GameObject.Find("Terrain");
            if (root != null) Object.DestroyImmediate(root);
            root = new GameObject("Terrain");

            // Boundary wall ring at the new arena half-extent. Walls are
            // taller than they were (8 m vs the legacy 4 m) so flyers can't
            // trivially clear them, and the ring is named "Wall_*" so the
            // existing palette tinter in EnvironmentBuilder.TintTerrain
            // catches them.
            const float arenaHalf = 170f;
            const float wallH = 8f, wallT = 1.2f;
            MakeBox(root.transform, new Vector3(0f, wallH * 0.5f,  arenaHalf), new Vector3(arenaHalf * 2f, wallH, wallT), "Wall_N");
            MakeBox(root.transform, new Vector3(0f, wallH * 0.5f, -arenaHalf), new Vector3(arenaHalf * 2f, wallH, wallT), "Wall_S");
            MakeBox(root.transform, new Vector3( arenaHalf, wallH * 0.5f, 0f), new Vector3(wallT, wallH, arenaHalf * 2f), "Wall_E");
            MakeBox(root.transform, new Vector3(-arenaHalf, wallH * 0.5f, 0f), new Vector3(wallT, wallH, arenaHalf * 2f), "Wall_W");

            BuildMountainRing(root.transform);
        }

        /// <summary>
        /// Build a deterministic ring of scenic mountains a bit outside
        /// the depot positions. Each mountain is a four-tier stacked
        /// pyramid of cubes — reads as a stylised peak that matches the
        /// project's blocky art direction. Tinted via <c>Pillar_</c> name
        /// prefix so <see cref="EnvironmentBuilder.TintTerrain"/> picks
        /// up <see cref="WorldPalette.ArenaPillar"/> automatically.
        /// </summary>
        private static void BuildMountainRing(Transform root)
        {
            // Mountains sit on a ring slightly inside the wall (160 m
            // radius vs 170 m wall) so the player sees them silhouetted
            // against the skybox without bumping into them mid-fight.
            const int mountainCount = 6;
            const float ringRadius = 152f;
            // Jitter each mountain's radial distance a bit so the ring
            // doesn't read as a perfect circle. Deterministic — sampled
            // from a fixed-seed hash of the index.
            for (int i = 0; i < mountainCount; i++)
            {
                float angle = (i / (float)mountainCount) * Mathf.PI * 2f
                              + 0.42f * (i % 2 == 0 ? 1f : -1f);
                float jitter = Mathf.Sin(i * 9.13f) * 12f;
                float r = ringRadius + jitter;
                Vector3 basePos = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                float scaleVariation = 1f + Mathf.Sin(i * 5.7f) * 0.25f;
                BuildMountain(root, basePos, scaleVariation, i);
            }
        }

        /// <summary>
        /// Build one tiered-pyramid mountain rooted at <paramref name="basePos"/>.
        /// Four stacked cubes shrinking toward the peak; total height
        /// scales with <paramref name="scale"/>. Naming convention
        /// "Mountain_NN_TT" routes through the palette tinter.
        /// </summary>
        private static void BuildMountain(Transform parent, Vector3 basePos, float scale, int index)
        {
            // Mountains tier in 4 stacked cubes; each tier is ~22 %
            // narrower than the last, total apex sits at ~36*scale m.
            const int tiers = 4;
            float baseWidth = 38f * scale;
            float tierHeight = 9f * scale;

            // Tiers attach directly to `parent` (the Terrain root) with
            // world-space positions because MakeBox writes
            // <c>transform.position</c> (world), not localPosition. A
            // dedicated container per mountain would require either
            // worldPositionStays:true on every tier or a custom helper —
            // not worth the extra indirection for what reads as a single
            // composite mesh to the player.
            for (int t = 0; t < tiers; t++)
            {
                float widthFrac = 1f - t * 0.22f; // each tier ~22 % narrower than the last
                float width = baseWidth * widthFrac;
                float y = tierHeight * (t + 0.5f);
                MakeBox(parent,
                    pos: basePos + new Vector3(0f, y, 0f),
                    size: new Vector3(width, tierHeight, width),
                    name: $"Mountain_{index:D2}_T{t}");
            }
        }

        // -----------------------------------------------------------------
        // Scene-element helpers
        // -----------------------------------------------------------------

        private static void EnsureGround()
        {
            // Unity's built-in Plane is 10m on a side at scale 1, so scale
            // 22 = 220m square. That comfortably contains the 200m arena
            // wall ring with a bit of slack on each side.
            GameObject ground = ScaffoldUtils.GetOrCreate(
                "Ground",
                () => GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(22f, 1f, 22f);
        }

        private static void EnsureCamera()
        {
            GameObject cam = GameObject.Find("Main Camera");
            if (cam == null)
            {
                cam = new GameObject("Main Camera");
                cam.AddComponent<Camera>();
                cam.AddComponent<AudioListener>();
                cam.tag = "MainCamera";
            }
            cam.transform.position = new Vector3(0f, 8f, -10f);
            cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
        }

        private static void EnsureLight()
        {
            GameObject light = ScaffoldUtils.GetOrCreate("Directional Light");
            var lightComp = light.GetComponent<Light>();
            if (lightComp == null) lightComp = light.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject MakeBox(Transform parent, Vector3 pos, Vector3 size, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = size;
            return go;
        }

        private static GameObject MakeRamp(Transform parent, Vector3 pos, float pitchDeg, Vector3 size, string name)
        {
            GameObject go = MakeBox(parent, pos, size, name);
            float halfH = size.y * 0.5f;
            float halfL = size.z * 0.5f;
            go.transform.rotation = Quaternion.Euler(-pitchDeg, 0f, 0f);
            float lift = Mathf.Sin(pitchDeg * Mathf.Deg2Rad) * halfL + Mathf.Cos(pitchDeg * Mathf.Deg2Rad) * halfH;
            go.transform.position = new Vector3(pos.x, lift, pos.z);
            return go;
        }
    }
}
