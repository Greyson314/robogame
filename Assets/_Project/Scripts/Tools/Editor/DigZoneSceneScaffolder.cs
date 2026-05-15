using Robogame.Core;
using Robogame.Voxel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor menu entries that build and exercise the Phase 1b DigZone
    /// test scene. Scaffolded programmatically — the scene is regenerable
    /// from code on a fresh checkout (TERRAFORMING_PLAN.md §12 autonomy
    /// contract).
    /// </summary>
    public static class DigZoneSceneScaffolder
    {
        public const string DigZoneTestScenePath = ScaffoldUtils.ScenesFolder + "/DigZone_Test.unity";

        // -----------------------------------------------------------------
        // Build Test Scene
        // -----------------------------------------------------------------

        [MenuItem("Robogame/Dig Zone/Build Test Scene", priority = 200)]
        public static void BuildTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new System.OperationCanceledException("User cancelled scene save.");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Save the new scene to disk first so subsequent ops have a
            // path to write back to.
            EditorSceneManager.SaveScene(scene, DigZoneTestScenePath);

            // Ground plane below the chunk. The chunk's half-space init
            // makes its bottom half solid, so the chunk visually rests on
            // the ground plane along its lower face.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, 0f, 0f);
            ground.transform.localScale = new Vector3(4f, 1f, 4f);   // 40 m × 40 m

            // Directional light. Stylised palette is fine — Phase 1b is
            // about geometry verification, art pass deferred.
            GameObject lightObj = new GameObject("Sun");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Camera positioned to frame the chunk diagonally from above.
            GameObject cameraObj = new GameObject("MainCamera");
            cameraObj.tag = "MainCamera";
            Camera cam = cameraObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.13f, 0.18f);
            // Chunk centres around (~8, ~8, ~8) at default settings (32×0.5 m).
            cameraObj.transform.position = new Vector3(20f, 18f, -10f);
            cameraObj.transform.LookAt(new Vector3(8f, 8f, 8f));

            // The DigZone itself. Located at world origin so its chunk
            // occupies [0..16, 0..16, 0..16] m.
            GameObject digZoneObj = new GameObject("DigZone");
            digZoneObj.transform.position = Vector3.zero;
            digZoneObj.AddComponent<MeshFilter>();
            MeshRenderer renderer = digZoneObj.AddComponent<MeshRenderer>();
            // Phase 4 introduces Mat_DigZoneEarth per TERRAFORMING_PLAN §7.
            // For now reuse the arena-ground palette material so the geometry
            // reads against the toon palette instead of magenta-default.
            renderer.sharedMaterial = WorldPalette.ArenaGround;
            digZoneObj.AddComponent<MeshCollider>();
            digZoneObj.AddComponent<DigZone>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Robogame] Built {DigZoneTestScenePath}. Use 'Robogame > Dig Zone > Test Sphere Subtract' to dig.");
        }

        // -----------------------------------------------------------------
        // Test Sphere Subtract (menu-driven brush trigger per the autonomy
        // contract — invokable from tests + CLI batch mode without a
        // Scene-View click handler).
        // -----------------------------------------------------------------

        [MenuItem("Robogame/Dig Zone/Test Sphere Subtract", priority = 210)]
        public static void TestSphereSubtract()
        {
            DigZone zone = Object.FindFirstObjectByType<DigZone>();
            if (zone == null)
            {
                Debug.LogWarning("[Robogame] No DigZone in the current scene. Run 'Build Test Scene' first.");
                return;
            }

            ApplyCentredSphereSubtract(zone, radiusMeters: 2.0f);
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
        }

        /// <summary>
        /// Apply a <see cref="BrushKind.SphereSubtract"/> at the chunk's
        /// centre with the given radius. Exposed so PlayMode tests can
        /// invoke the same brush trigger as the menu.
        /// </summary>
        public static int ApplyCentredSphereSubtract(DigZone zone, float radiusMeters)
        {
            Bounds b = zone.WorldBounds;
            BrushOp op = new BrushOp
            {
                kind = BrushKind.SphereSubtract,
                serverTick = 0,
                p0 = Vector3Fixed.FromVector3(b.center),
                p1 = Vector3Fixed.FromVector3(b.center),
                radiusFixed = (ushort)Mathf.Clamp(
                    Mathf.RoundToInt(radiusMeters * Vector3Fixed.UnitsPerMeter),
                    0, ushort.MaxValue),
            };
            return zone.ApplyBrush(op);
        }
    }
}
