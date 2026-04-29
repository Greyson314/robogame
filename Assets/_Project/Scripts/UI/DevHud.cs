using Robogame.Block;
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.UI
{
    /// <summary>
    /// Runtime IMGUI overlay with 1-click test actions: rebuild robots, damage,
    /// destroy CPU. Intentionally minimal — toggle with F1, no scene/asset setup.
    /// </summary>
    /// <remarks>
    /// Lives in the UI module so it doesn't pollute gameplay code. Wired up by
    /// the scene scaffolder onto a dedicated DevHud GameObject.
    /// </remarks>
    public sealed class DevHud : MonoBehaviour
    {
        [Tooltip("Key that toggles the dev HUD on/off.")]
        [SerializeField] private Key _toggleKey = Key.F1;

        [Tooltip("Show the HUD when the scene starts.")]
        [SerializeField] private bool _visibleAtStart = true;

        [Tooltip("Per-ring damage applied by the 'Damage Random Block' button. Index 0 = direct hit.")]
        [SerializeField] private float[] _splashRings = { 200f, 100f, 40f };

        private bool _visible;

        private void Awake()
        {
            _visible = _visibleAtStart;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            // Guard against stale serialized values from the old KeyCode field
            // (KeyCode.F1 == 282, which is out of range for the Key enum).
            if (!System.Enum.IsDefined(typeof(Key), _toggleKey) || _toggleKey == Key.None) return;
            try
            {
                if (kb[_toggleKey].wasPressedThisFrame) _visible = !_visible;
            }
            catch (System.ArgumentOutOfRangeException)
            {
                _toggleKey = Key.F1;
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            const float pad = 8f;
            const float w = 220f;
            const float h = 220f;
            GUILayout.BeginArea(new Rect(pad, pad, w, h), GUI.skin.box);
            GUILayout.Label("<b>Robogame Dev</b>", RichLabel());
            GUILayout.Label($"FPS: {Mathf.RoundToInt(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f))}");

            if (GUILayout.Button("Rebuild Player Robot")) RebuildByName("Robot");
            if (GUILayout.Button("Rebuild Combat Dummy")) RebuildByName("CombatDummy");
            if (GUILayout.Button("Damage Random Block")) DamageRandomBlock();
            if (GUILayout.Button("Destroy CPU")) DestroyCpu();

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Toggle: {_toggleKey}");
            GUILayout.EndArea();
        }

        private static GUIStyle RichLabel()
        {
            var s = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 13 };
            return s;
        }

        private static void RebuildByName(string n)
        {
            Robot.RebuildByName(n);
        }

        // -----------------------------------------------------------------
        // Test actions (operate on whatever robot we can find — dummy preferred)
        // -----------------------------------------------------------------

        private void DamageRandomBlock()
        {
            Robot target = PickTargetRobot();
            if (target == null || target.Grid == null || target.Grid.Count == 0) return;

            var live = new System.Collections.Generic.List<Vector3Int>();
            foreach (var kvp in target.Grid.Blocks)
            {
                if (kvp.Value != null && kvp.Value.IsAlive) live.Add(kvp.Key);
            }
            if (live.Count == 0) return;

            Vector3Int pos = live[Random.Range(0, live.Count)];
            target.Grid.ApplySplashDamage(pos, _splashRings);
        }

        private void DestroyCpu()
        {
            Robot target = PickTargetRobot();
            if (target != null && target.CpuBlock != null)
            {
                target.CpuBlock.TakeDamage(float.MaxValue);
            }
        }

        private static Robot PickTargetRobot()
        {
            // Prefer the dummy if it's around — that's almost always what you want
            // to poke during dev. Fall back to any robot.
            GameObject dummy = GameObject.Find("CombatDummy");
            if (dummy != null)
            {
                Robot r = dummy.GetComponent<Robot>();
                if (r != null) return r;
            }
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<Robot>();
#else
            return Object.FindObjectOfType<Robot>();
#endif
        }
    }
}
