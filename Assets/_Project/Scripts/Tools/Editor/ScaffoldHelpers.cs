using Robogame.Block;
using Robogame.Combat;
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
    /// Small editor utilities shared by <see cref="SceneScaffolder"/> and
    /// <see cref="RobotLayouts"/>: component ensure / wiring / tuning
    /// assignment.
    /// </summary>
    internal static class ScaffoldHelpers
    {
        public static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        /// <summary>
        /// Assign a SerializedObject reference field. Used to wire tuning
        /// SO assets onto components without re-defining every field.
        /// </summary>
        public static void AssignTuning(Object component, string serializedField, Object asset)
        {
            if (component == null || asset == null) return;
            SerializedObject so = new SerializedObject(component);
            SerializedProperty prop = so.FindProperty(serializedField);
            if (prop == null) return;
            prop.objectReferenceValue = asset;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void WirePlayerInput(GameObject go)
        {
            var input = EnsureComponent<PlayerInputHandler>(go);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ScaffoldUtils.InputActionsAsset);
            if (actions == null) return;
            SerializedObject so = new SerializedObject(input);
            so.FindProperty("_actions").objectReferenceValue = actions;
            // Backfill descend action if absent (older scaffolds left it blank).
            SerializedProperty descend = so.FindProperty("_descendAction");
            if (descend != null && string.IsNullOrEmpty(descend.stringValue)) descend.stringValue = "Sprint";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureWeaponMountAndBinder(GameObject robotGO)
        {
            // Mount lives as a child so its rotation is independent of the chassis.
            Transform mountT = robotGO.transform.Find("WeaponMount");
            GameObject mountGO = mountT != null ? mountT.gameObject : new GameObject("WeaponMount");
            mountGO.transform.SetParent(robotGO.transform, worldPositionStays: false);
            mountGO.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            mountGO.transform.localRotation = Quaternion.identity;
            if (mountGO.GetComponent<WeaponMount>() == null) mountGO.AddComponent<WeaponMount>();

            RobotWeaponBinder binder = robotGO.GetComponent<RobotWeaponBinder>();
            if (binder == null) binder = robotGO.AddComponent<RobotWeaponBinder>();
            SerializedObject so = new SerializedObject(binder);
            so.FindProperty("_mount").objectReferenceValue = mountGO.GetComponent<WeaponMount>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureWheelBinder(GameObject robotGO)
        {
            if (robotGO.GetComponent<RobotWheelBinder>() == null)
                robotGO.AddComponent<RobotWheelBinder>();
        }

        public static void EnsureAeroBinder(GameObject robotGO)
        {
            if (robotGO.GetComponent<RobotAeroBinder>() == null)
                robotGO.AddComponent<RobotAeroBinder>();
        }

        public static void RemoveLegacyRootGun(GameObject robotGO)
        {
            // Guns belong on the WeaponBlock turret, never on the chassis
            // root. Strip whatever's at the root (current ProjectileGun
            // OR any leftover from earlier versions) so re-running
            // scaffolds on an old asset doesn't double-fire.
            ProjectileGun rootGun = robotGO.GetComponent<ProjectileGun>();
            if (rootGun != null) Object.DestroyImmediate(rootGun);

            Transform muzzle = robotGO.transform.Find("Muzzle");
            if (muzzle != null) Object.DestroyImmediate(muzzle.gameObject);
        }

        public static void BindFollowCameraTo(Transform target, bool followRotation = false, Vector3? offset = null)
        {
            // followRotation / offset are kept in the signature for source
            // compatibility with older scaffold callers, but the orbit
            // FollowCamera ignores them — every chassis uses the same
            // mouse-driven rig now.
            _ = followRotation;
            _ = offset;

            GameObject cam = GameObject.Find("Main Camera");
            if (cam == null) return;

            FollowCamera follow = cam.GetComponent<FollowCamera>();
            if (follow == null) follow = cam.AddComponent<FollowCamera>();
            SerializedObject so = new SerializedObject(follow);
            so.FindProperty("_target").objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Reticle lives on the camera so it appears whenever the camera
            // does. Idempotent — added once per camera.
            if (cam.GetComponent<AimReticle>() == null) cam.AddComponent<AimReticle>();
        }

        /// <summary>
        /// Destroy every player-controlled chassis EXCEPT the one named
        /// <paramref name="keepName"/>. Keeps the "one player at a time"
        /// invariant true across consecutive scaffold runs.
        /// </summary>
        public static void ClearPlayerChassis(string keepName)
        {
            PlayerController[] all = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
            foreach (PlayerController pc in all)
            {
                if (pc == null) continue;
                if (pc.gameObject.name == keepName) continue;
                Object.DestroyImmediate(pc.gameObject);
            }
            // Legacy cube from BuildSimpleGarage (no PlayerController, just RobotDrive).
            GameObject legacyPlayer = GameObject.Find("Player");
            if (legacyPlayer != null && legacyPlayer.name != keepName)
            {
                Object.DestroyImmediate(legacyPlayer);
            }
        }

        public static void EnsureDevHud()
        {
            GameObject hudGO = ScaffoldUtils.GetOrCreate("DevHud");
            UI.DevHud hud = hudGO.GetComponent<UI.DevHud>();
            if (hud == null) hudGO.AddComponent<UI.DevHud>();

            // FPS counter rides on the same GameObject so it's in lock-step
            // with the dev HUD lifecycle but draws independently — DevHud
            // is opt-in behind F1, the FPS readout is always visible
            // top-left so a designer flipping the panel off doesn't lose
            // the perf number.
            UI.FpsCounter fps = hudGO.GetComponent<UI.FpsCounter>();
            if (fps == null) hudGO.AddComponent<UI.FpsCounter>();
        }
    }
}
