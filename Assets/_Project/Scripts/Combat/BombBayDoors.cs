using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Swings the bomb bay's two trapdoor nodes on a drop: snap open with
    /// overshoot, hold while the bomb clears, slam shut with a rebound.
    /// Procedural (no Animator) — the doors are rigid nodes whose FBX
    /// origins sit on the hinge lines. Visual only.
    /// </summary>
    /// <remarks>
    /// Session 139 animation pass. Keyframes mirror the Blender study in
    /// artgen/inv_anims.py build_bombbay(). Sleeps between drops.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BombBayDoors : MonoBehaviour
    {
        // (seconds, swing degrees) — piecewise, smoothstepped per segment.
        private static readonly float[] KeyT = { 0f, 0.125f, 0.25f, 0.375f, 1.05f, 1.25f, 1.375f, 1.55f };
        private static readonly float[] KeyA = { 0f, 100f, 88f, 93f, 93f, 0f, 16f, 0f };

        private Transform _doorA, _doorB;
        private Quaternion _restA, _restB;
        private float _t;

        public static BombBayDoors Attach(GameObject host, Transform modelRoot)
        {
            Transform a = FindSuffix(modelRoot, "Door1");
            Transform b = FindSuffix(modelRoot, "Door-1");
            if (host == null || a == null || b == null) return null;
            if (!host.TryGetComponent(out BombBayDoors doors))
                doors = host.AddComponent<BombBayDoors>();
            doors._doorA = a;
            doors._doorB = b;
            doors._restA = a.localRotation;
            doors._restB = b.localRotation;
            doors.enabled = false;
            return doors;
        }

        public void Drop()
        {
            // Fresh drop starts the clip; a drop mid-close re-opens from
            // the hold (same pose the hold ends on, so no visible pop when
            // the drop interval is shorter than the clip).
            _t = enabled && _t > KeyT[4] ? KeyT[3] : 0f;
            enabled = true;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            bool done = _t >= KeyT[KeyT.Length - 1];
            float a = done ? 0f : Evaluate(_t);
            if (_doorA != null) _doorA.localRotation = _restA * Quaternion.AngleAxis(-a, Vector3.right);
            if (_doorB != null) _doorB.localRotation = _restB * Quaternion.AngleAxis(a, Vector3.right);
            if (done) enabled = false;
        }

        private static float Evaluate(float t)
        {
            for (int i = KeyT.Length - 2; i >= 0; i--)
            {
                if (t < KeyT[i]) continue;
                float u = Mathf.InverseLerp(KeyT[i], KeyT[i + 1], t);
                u = u * u * (3f - 2f * u);
                return Mathf.Lerp(KeyA[i], KeyA[i + 1], u);
            }
            return 0f;
        }

        private static Transform FindSuffix(Transform root, string suffix)
        {
            if (root == null) return null;
            if (root.name.EndsWith(suffix)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindSuffix(root.GetChild(i), suffix);
                if (found != null) return found;
            }
            return null;
        }
    }
}
