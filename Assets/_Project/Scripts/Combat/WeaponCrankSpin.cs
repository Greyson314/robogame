using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Spins a named crank node (the SMG's crank-organ handle + drive gear)
    /// while its weapon is firing, with a short spin-up/down so the
    /// mechanism reads as driven rather than switched. Visual only.
    /// </summary>
    /// <remarks>
    /// Session 139 animation pass. The SMG FBX carries an "InvSMG_Crank"
    /// pivot node on the axle axis (artgen/inv_smg.py). Sleeps between
    /// bursts; no per-frame cost while idle, no allocations after Attach.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponCrankSpin : MonoBehaviour
    {
        private const float SpinDegPerSec = 620f;
        private const float ApproachRate = 10f;
        private const float GraceSeconds = 0.30f;

        private Transform _crank;
        private Quaternion _rest;
        private float _angle;
        private float _speed;
        private float _activeUntil;

        /// <summary>Attach to <paramref name="host"/> if the model under
        /// <paramref name="searchRoot"/> has a node named
        /// <paramref name="nodeName"/>; returns null otherwise (no-op for
        /// crankless models).</summary>
        public static WeaponCrankSpin Attach(GameObject host, Transform searchRoot, string nodeName)
        {
            Transform crank = FindDeep(searchRoot, nodeName);
            if (host == null || crank == null) return null;
            if (!host.TryGetComponent(out WeaponCrankSpin spin))
                spin = host.AddComponent<WeaponCrankSpin>();
            spin._crank = crank;
            spin._rest = crank.localRotation;
            spin.enabled = false;
            return spin;
        }

        public void NotifyFired()
        {
            _activeUntil = Time.time + GraceSeconds;
            enabled = true;
        }

        private void Update()
        {
            if (_crank == null) { enabled = false; return; }
            float target = Time.time <= _activeUntil ? SpinDegPerSec : 0f;
            _speed = Mathf.Lerp(_speed, target, 1f - Mathf.Exp(-ApproachRate * Time.deltaTime));
            _angle = Mathf.Repeat(_angle + _speed * Time.deltaTime, 360f);
            _crank.localRotation = _rest * Quaternion.AngleAxis(_angle, Vector3.right);
            if (target == 0f && _speed < 4f)
            {
                _speed = 0f;
                enabled = false;   // crank rests wherever it stopped
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
