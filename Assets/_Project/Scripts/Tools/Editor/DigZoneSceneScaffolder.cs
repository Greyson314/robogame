using Robogame.Core;
using Robogame.Voxel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Editor menu entries that build and exercise the DigZone test scene.
    /// Scaffolded programmatically per TERRAFORMING_PLAN.md §12 autonomy
    /// contract — the scene is regenerable on a fresh checkout.
    /// </summary>
    public static class DigZoneSceneScaffolder
    {
        public const string DigZoneTestScenePath = ScaffoldUtils.ScenesFolder + "/DigZone_Test.unity";

        [MenuItem("Robogame/Dig Zone/Build Test Scene", priority = 200)]
        public static void BuildTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new System.OperationCanceledException("User cancelled scene save.");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, DigZoneTestScenePath);

            // Ground plane. At 2×2×2 chunks of 16 m each the zone covers
            // 32×32×32 m, so widen the ground to keep some visible margin.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, 0f, 0f);
            ground.transform.localScale = new Vector3(8f, 1f, 8f);   // 80 m × 80 m

            GameObject lightObj = new GameObject("Sun");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject cameraObj = new GameObject("MainCamera");
            cameraObj.tag = "MainCamera";
            Camera cam = cameraObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.13f, 0.18f);
            // 2×2×2 zone centred at (16,16,16); pull the camera back to frame it.
            cameraObj.transform.position = new Vector3(38f, 32f, -18f);
            cameraObj.transform.LookAt(new Vector3(16f, 16f, 16f));

            // The DigZone container itself has no renderer/collider — chunks
            // carry those. Configure serialised fields before the component's
            // Awake fires so the chunk grid spins up at the right size with
            // the right material.
            GameObject digZoneObj = new GameObject("DigZone");
            digZoneObj.transform.position = Vector3.zero;
            digZoneObj.SetActive(false);
            DigZone zone = digZoneObj.AddComponent<DigZone>();

            SerializedObject so = new SerializedObject(zone);
            so.FindProperty("_cellSize").floatValue = 0.5f;
            so.FindProperty("_chunkSizeCells").intValue = 32;
            so.FindProperty("_chunkGridSize").vector3IntValue = new Vector3Int(2, 2, 2);
            so.FindProperty("_chunkMaterial").objectReferenceValue = WorldPalette.ArenaGround;
            so.ApplyModifiedPropertiesWithoutUndo();

            digZoneObj.SetActive(true);   // Awake fires, chunks spawn.

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[Robogame] Built {DigZoneTestScenePath} with " +
                      $"{zone.ChunkGridSize.x}×{zone.ChunkGridSize.y}×{zone.ChunkGridSize.z} chunks. " +
                      "Use 'Robogame > Dig Zone > Test Sphere Subtract' to dig.");
        }

        [MenuItem("Robogame/Dig Zone/Test Sphere Subtract", priority = 210)]
        public static void TestSphereSubtract()
        {
            DigZone zone = Object.FindAnyObjectByType<DigZone>();
            if (zone == null)
            {
                Debug.LogWarning("[Robogame] No DigZone in the current scene. Run 'Build Test Scene' first.");
                return;
            }

            int changed = ApplyCentredSphereSubtract(zone, radiusMeters: 4.0f);
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            Debug.Log($"[Robogame] Sphere brush mutated {changed} cells across {zone.ChunkCount} chunks.");
        }

        /// <summary>
        /// Apply a <see cref="BrushKind.SphereSubtract"/> at the zone's centre.
        /// Exposed for PlayMode tests so they can use the same brush trigger
        /// as the menu.
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
