using Robogame.Voxel;
using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Static set-dressing for the combat arena (session 119 — "Sunken
    /// Crossing"): a non-diggable backdrop mountain range BEYOND the wall
    /// ring, rock columns crowning the diagonal ridges, and a light tree
    /// scatter on the mid-slopes. All props sample the shared
    /// <see cref="HeightmapField"/> so they sit ON the sculpted ground,
    /// not floating over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Diggable-by-default carve-out.</b> The playfield ground stays
    /// fully diggable (it's the voxel <c>DigZone</c>). These props are the
    /// stated exception: trees and ridge rocks are static decor placed on
    /// the terrain, and the backdrop range lives outside the ±170 m walls
    /// where the player can't reach it — pure horizon silhouette.
    /// </para>
    /// <para>
    /// <b>Palette compliance.</b> Reuses the existing
    /// <see cref="WorldPalette.ArenaWall"/> (Slate) for rock/backdrop and
    /// <see cref="WorldPalette.ArenaGround"/> (Grass) for foliage — no new
    /// materials, so the "if it isn't a token it's wrong" rule holds.
    /// </para>
    /// <para>
    /// Everything here is deterministic (hash/golden-angle, no
    /// <see cref="Random"/>) so re-scaffolding lands identical decor.
    /// </para>
    /// </remarks>
    internal static class ArenaProps
    {
        private const float GoldenAngle = 2.39996323f; // π(3−√5)

        public static void Build(Transform envRoot)
        {
            GameObject root = new GameObject("Props");
            root.transform.SetParent(envRoot, worldPositionStays: false);

            HeightmapParams hp = HillsGround.LoadHeightmapParams();

            BuildBackdropRange(root.transform);
            BuildRidgeRocks(root.transform, hp);
            BuildPebbleScatter(root.transform, hp);
            BuildTreeScatter(root.transform, hp);
            BuildAtmosphere(root.transform);
        }

        // -----------------------------------------------------------------
        // Pebble scatter — small Stylized Nature Pack pebbles as ground detail
        // across the mid-field (decor, no colliders).
        // -----------------------------------------------------------------

        private static readonly string[] PebblePrefabs =
        {
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Pebble_1.prefab",
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Pebble_2.prefab",
        };

        private static void BuildPebbleScatter(Transform parent, HeightmapParams hp)
        {
            GameObject pebbles = new GameObject("Pebbles");
            pebbles.transform.SetParent(parent, worldPositionStays: false);

            const int count = 40;
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                float r = Mathf.Lerp(58f, 160f, Mathf.Sqrt(t));
                float ang = i * GoldenAngle;
                float x = Mathf.Cos(ang) * r, z = Mathf.Sin(ang) * r;
                // Keep clear of the team bowls.
                if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, 92f)) < 26f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, -92f)) < 26f) continue;
                float y = HeightmapField.Sample(hp, x, z);

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PebblePrefabs[i % PebblePrefabs.Length]);
                if (prefab == null) continue;
                GameObject pb = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pebbles.transform);
                pb.name = $"Pebble_{i:D2}";
                float s = 4f * Mathf.Lerp(0.6f, 1.6f, Hash(i * 7));
                pb.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, Hash(i * 3) * 360f, 0f));
                pb.transform.localScale = Vector3.one * s;
                SetStaticRecursive(pb);
            }
        }

        // -----------------------------------------------------------------
        // Atmosphere — Stylized Nature Pack particle systems (URP-converted):
        // drifting ground-fog wisps + falling leaves + ambient dust, for the
        // misty meadow mood. Sized up from the demo's tiny-terrain values.
        // -----------------------------------------------------------------

        private const string FogPrefab    = "Assets/Stylized Nature Pack/Particles/_Prefabs/Ground Fog.prefab";
        private const string LeavesPrefab = "Assets/Stylized Nature Pack/Particles/_Prefabs/Falling Leaves.prefab";
        private const string DustPrefab   = "Assets/Stylized Nature Pack/Particles/_Prefabs/Dust.prefab";

        private static void BuildAtmosphere(Transform parent)
        {
            GameObject atmo = new GameObject("Atmosphere");
            atmo.transform.SetParent(parent, worldPositionStays: false);

            // Ground-fog wisps ringed across the playfield, hugging the ground.
            for (int i = 0; i < 7; i++)
            {
                float ang = (i / 7f) * Mathf.PI * 2f + 0.3f;
                float r = 55f + Hash(i * 13) * 80f;
                PlaceParticle(atmo.transform, FogPrefab,
                    new Vector3(Mathf.Cos(ang) * r, 2f, Mathf.Sin(ang) * r),
                    16f + Hash(i * 7) * 8f, $"GroundFog_{i}");
            }
            // Falling leaves drifting down over the whole field (high + wide).
            PlaceParticle(atmo.transform, LeavesPrefab, new Vector3(0f, 75f, 0f), 14f, "FallingLeaves");
            // Ambient floating dust mid-field.
            PlaceParticle(atmo.transform, DustPrefab, new Vector3(0f, 14f, 0f), 12f, "Dust");
        }

        private static void PlaceParticle(Transform parent, string path, Vector3 pos, float scale, string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return; // pack absent → silently skip
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
        }

        // -----------------------------------------------------------------
        // Backdrop range — two staggered rings of craggy peaks beyond the
        // ±170 m walls. Tall + unreachable: a horizon silhouette only.
        // -----------------------------------------------------------------

        private static void BuildBackdropRange(Transform parent)
        {
            GameObject range = new GameObject("BackdropRange");
            range.transform.SetParent(parent, worldPositionStays: false);

            // Near ring: closer, a touch shorter, reads as foothills.
            BuildPeakRing(range.transform, count: 11, ringRadius: 218f, radialJitter: 18f,
                          minHeight: 38f, maxHeight: 58f, seed: 17);
            // Far ring: taller, set between the near gaps for depth layering.
            BuildPeakRing(range.transform, count: 10, ringRadius: 300f, radialJitter: 26f,
                          minHeight: 56f, maxHeight: 82f, seed: 53, angleBias: 0.32f);
        }

        private static void BuildPeakRing(Transform parent, int count, float ringRadius,
                                          float radialJitter, float minHeight, float maxHeight,
                                          int seed, float angleBias = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + angleBias;
                float r = ringRadius + (Hash(seed + i) - 0.5f) * 2f * radialJitter;
                Vector3 basePos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float h = Mathf.Lerp(minHeight, maxHeight, Hash(seed * 7 + i));
                float w = h * Mathf.Lerp(0.85f, 1.15f, Hash(seed * 13 + i));
                BuildPeak(parent, basePos, h, w, seed * 31 + i);
            }
        }

        /// <summary>
        /// One craggy peak: 5 stacked boxes narrowing toward the apex, each
        /// jittered laterally so the silhouette reads as rock rather than a
        /// clean pyramid. Colliders stripped — it lives beyond the walls and
        /// is never touched, so a cooked collider would be pure waste.
        /// </summary>
        private static void BuildPeak(Transform parent, Vector3 basePos, float height, float baseWidth, int seed)
        {
            const int tiers = 5;
            float tierH = height / tiers;
            float y = 0f;
            float prevW = baseWidth;
            for (int t = 0; t < tiers; t++)
            {
                float frac = 1f - t / (float)tiers;          // 1 → 0.2
                float w = baseWidth * Mathf.Lerp(0.28f, 1f, frac);
                float jx = (Hash(seed + t * 3) - 0.5f) * prevW * 0.30f;
                float jz = (Hash(seed + t * 3 + 1) - 0.5f) * prevW * 0.30f;
                Vector3 pos = basePos + new Vector3(jx, y + tierH * 0.5f, jz);
                MakeBox(parent, pos, new Vector3(w, tierH * 1.04f, w), $"Backdrop_{seed}_{t}",
                        WorldPalette.ArenaWall, stripCollider: true);
                y += tierH;
                prevW = w;
            }
        }

        // -----------------------------------------------------------------
        // Ridge rocks — rock columns crowning the two diagonal ridges
        // (lines z = ±x). Silhouette + micro-cover on the high ground.
        // -----------------------------------------------------------------

        private static void BuildRidgeRocks(Transform parent, HeightmapParams hp)
        {
            GameObject rocks = new GameObject("RidgeRocks");
            rocks.transform.SetParent(parent, worldPositionStays: false);

            // Four ridge arms × three radii along each crown. `a` is the
            // coordinate along a 45° line so (±a, ±a) lands on the crown.
            float[] arms = { 1f, -1f };           // sign pattern selectors below
            float[] radii = { 58f, 80f, 104f };   // a-values (world r = a√2)
            int idx = 0;
            foreach (float sx in arms)
            foreach (float sz in arms)
            foreach (float a in radii)
            {
                float x = a * sx;
                float z = a * sz;
                float y = HeightmapField.Sample(hp, x, z);
                BuildRockCluster(rocks.transform, new Vector3(x, y, z), idx++);
            }
        }

        // Real stones from the Stylized Nature Pack (URP-converted), placed on
        // the ridge crowns in place of the old procedural cubes.
        private static readonly string[] StonePrefabs =
        {
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Stone_1.prefab",
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Stone_2.prefab",
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Stone_3.prefab",
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Stone_4.prefab",
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Stone_5.prefab",
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Stone_6.prefab",
            "Assets/Stylized Nature Pack/Stones/_Prefabs/Stone_7.prefab",
        };
        private const float StoneBaseScale = 3.0f;

        private static void BuildRockCluster(Transform parent, Vector3 crown, int idx)
        {
            int count = 1 + (int)(Hash(idx * 5) * 1.99f); // 1–2 stones per crown
            for (int k = 0; k < count; k++)
            {
                float ox = (Hash(idx * 9 + k) - 0.5f) * 9f;
                float oz = (Hash(idx * 9 + k + 1) - 0.5f) * 9f;
                BuildStone(parent, crown + new Vector3(ox, 0f, oz), idx * 7 + k);
            }
        }

        private static void BuildStone(Transform parent, Vector3 ground, int seed)
        {
            string path = StonePrefabs[(int)(Hash(seed * 31) * StonePrefabs.Length) % StonePrefabs.Length];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                // Fallback to a slate cube if the pack is missing.
                float fh = Mathf.Lerp(3f, 6f, Hash(seed * 11));
                MakeBox(parent, ground + new Vector3(0f, fh * 0.5f, 0f),
                        new Vector3(fh * 0.8f, fh, fh * 0.8f), $"RidgeRock_{seed}",
                        WorldPalette.ArenaWall, stripCollider: false);
                return;
            }
            GameObject stone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            stone.name = $"Stone_{seed:D2}";
            float s = StoneBaseScale * Mathf.Lerp(0.7f, 1.5f, Hash(seed * 7));
            stone.transform.SetPositionAndRotation(
                ground,
                Quaternion.Euler((Hash(seed * 5) - 0.5f) * 24f, Hash(seed * 3) * 360f, (Hash(seed * 9) - 0.5f) * 24f));
            stone.transform.localScale = Vector3.one * s;
            SetStaticRecursive(stone);
            // Solid cover: mesh-collide the stone so robots can hide behind it.
            foreach (var mr in stone.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        // -----------------------------------------------------------------
        // Tree scatter — light, deterministic golden-angle spiral over the
        // mid-slope annulus. Rejected on ridge crowns (rocks live there),
        // in the flat combat box, inside the base bowls, and outside a
        // sensible height band so trees only sprout on real slopes.
        // -----------------------------------------------------------------

        private static void BuildTreeScatter(Transform parent, HeightmapParams hp)
        {
            GameObject trees = new GameObject("Trees");
            trees.transform.SetParent(parent, worldPositionStays: false);

            const int candidates = 24; // few, gigantic landmark trees
            const float rMin = 60f, rMax = 150f;
            int placed = 0;
            for (int i = 0; i < candidates; i++)
            {
                // Area-uniform radius (sqrt) so trees don't clump at centre.
                float t = (i + 0.5f) / candidates;
                float r = Mathf.Lerp(rMin, rMax, Mathf.Sqrt(t));
                float ang = i * GoldenAngle;
                float x = Mathf.Cos(ang) * r;
                float z = Mathf.Sin(ang) * r;

                // Keep clear of the diagonal ridge crowns (rocks + cover land).
                float distToDiag = Mathf.Min(Mathf.Abs(z - x), Mathf.Abs(z + x)) * 0.70710678f;
                if (distToDiag < 16f) continue;

                // Keep clear of the team base bowls (depots).
                if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, 92f)) < 32f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(0f, -92f)) < 32f) continue;

                float y = HeightmapField.Sample(hp, x, z);
                if (y < 1.6f || y > 8.5f) continue; // slopes only, not valley floor or peaks

                BuildTree(trees.transform, new Vector3(x, y, z), i);
                placed++;
            }
        }

        // Single leafy tree — the fruitless green variant (no apples/pears).
        // Only one non-pine tree model ships with the project; a genuinely
        // different silhouette would need a new asset pack.
        private static readonly string[] TreePrefabs =
        {
            "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Fruit_Tree_01_green.prefab",
        };

        // GIGANTIC landmark trees — tower over the field, block sightlines,
        // and force navigation around their trunks (which get colliders,
        // below). Native ~6.9 m × 17 ≈ 117 m (dialed back 15% from 20).
        private const float TreeBaseScale = 17f;

        /// <summary>
        /// Instantiate a stylized Polytope tree prefab (mixed fruit/pine),
        /// scaled up with deterministic per-tree variation + yaw. Colliders
        /// stripped (decor — robots drive through the scatter). Falls back to
        /// a blocky primitive tree if the asset pack isn't present.
        /// </summary>
        private static void BuildTree(Transform parent, Vector3 ground, int seed)
        {
            string path = TreePrefabs[(int)(Hash(seed * 23) * TreePrefabs.Length) % TreePrefabs.Length];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { BuildBlockyTreeFallback(parent, ground, seed); return; }

            GameObject tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            tree.name = $"Tree_{seed:D3}";
            tree.transform.position = ground;
            float yaw = Hash(seed * 3) * 360f;
            float s = TreeBaseScale * Mathf.Lerp(0.82f, 1.25f, Hash(seed * 7));
            tree.transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f));
            tree.transform.localScale = Vector3.one * s;
            SetStaticRecursive(tree);
            // Gigantic trees are real obstacles: give the woody trunk/branch
            // mesh a MeshCollider so robots navigate around it; leave the leaf
            // canopy (the "Foliage" renderer) pass-through so shots/leaves
            // don't block. Strip any stray prefab colliders first.
            foreach (var mr in tree.GetComponentsInChildren<MeshRenderer>())
            {
                var stray = mr.GetComponent<Collider>();
                if (stray != null) Object.DestroyImmediate(stray);

                string shaderName = mr.sharedMaterial != null && mr.sharedMaterial.shader != null
                    ? mr.sharedMaterial.shader.name : string.Empty;
                bool isFoliage = shaderName.Contains("Foliage");
                var mf = mr.GetComponent<MeshFilter>();
                if (!isFoliage && mf != null && mf.sharedMesh != null)
                    mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        private static void SetStaticRecursive(GameObject go)
        {
            go.isStatic = true;
            foreach (Transform t in go.transform) SetStaticRecursive(t.gameObject);
        }

        /// <summary>
        /// Fallback blocky tree (slim trunk + two tapered canopy boxes) used
        /// only if the Polytope tree pack is missing on a checkout. Trunk
        /// reuses Slate; canopy reuses Grass.
        /// </summary>
        private static void BuildBlockyTreeFallback(Transform parent, Vector3 ground, int seed)
        {
            GameObject tree = new GameObject($"Tree_{seed:D3}");
            tree.transform.SetParent(parent, worldPositionStays: false);
            tree.transform.position = ground;
            float yaw = Hash(seed * 3) * 360f;
            float s = TreeBaseScale * Mathf.Lerp(0.8f, 1.35f, Hash(seed * 7));
            tree.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            float trunkH = 2.4f * s, trunkW = 0.55f * s;
            MakeChild(tree.transform, new Vector3(0f, trunkH * 0.5f, 0f),
                      new Vector3(trunkW, trunkH, trunkW), "Trunk",
                      WorldPalette.ArenaWall, stripCollider: true);

            float c0H = 2.6f * s, c0W = 3.0f * s;
            MakeChild(tree.transform, new Vector3(0f, trunkH + c0H * 0.4f, 0f),
                      new Vector3(c0W, c0H, c0W), "Canopy0",
                      WorldPalette.ArenaGround, stripCollider: true);

            float c1H = 1.9f * s, c1W = 1.9f * s;
            MakeChild(tree.transform, new Vector3(0f, trunkH + c0H * 0.75f + c1H * 0.4f, 0f),
                      new Vector3(c1W, c1H, c1W), "Canopy1",
                      WorldPalette.ArenaGround, stripCollider: true);
        }

        // -----------------------------------------------------------------
        // Box helpers
        // -----------------------------------------------------------------

        private static GameObject MakeBox(Transform parent, Vector3 pos, Vector3 size, string name,
                                          Material mat, bool stripCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.isStatic = true;
            if (stripCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            WorldPalette.Apply(go, mat);
            return go;
        }

        // Like MakeBox but positions relative to the parent (localPosition),
        // so a tree's parts move/rotate as one with the tree root.
        private static void MakeChild(Transform parent, Vector3 localPos, Vector3 size, string name,
                                      Material mat, bool stripCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.isStatic = true;
            if (stripCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            WorldPalette.Apply(go, mat);
        }

        // Deterministic [0,1) hash of an integer (classic frac-sin).
        private static float Hash(int n)
        {
            float v = Mathf.Sin(n * 12.9898f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }
    }
}
