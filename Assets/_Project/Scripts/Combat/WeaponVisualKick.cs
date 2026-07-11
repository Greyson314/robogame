using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Visual-only recoil: punches the yoke's mesh children backward along
    /// the barrel on each shot and eases them home. The muzzle chain is
    /// deliberately excluded from the punched set so the projectile origin
    /// never moves — the kick cannot leak into gameplay.
    /// </summary>
    /// <remarks>
    /// Session 139 animation pass. Sleeps (disabled) between shots; zero
    /// per-frame cost while idle and zero allocations after Attach.
    /// TRACE[INV-3]: fire origin (ShootPoint) stays fixed under recoil.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeaponVisualKick : MonoBehaviour
    {
        private const float RecoverPerSecond = 9f;
        private const float SnapEpsilon = 0.0015f;

        private Transform _muzzle;
        private Transform[] _parts;
        private Vector3[] _rest;
        private float _amplitude;
        private float _offset;
        private Vector3 _dirLocal = Vector3.back;

        /// <summary>
        /// Add (or re-init, idempotently) the kick on <paramref name="yoke"/>.
        /// <paramref name="amplitude"/> is in yoke-local (authored model)
        /// units; the instance scale shrinks it to world size.
        /// </summary>
        public static WeaponVisualKick Attach(Transform yoke, Transform muzzle, float amplitude)
        {
            if (yoke == null) return null;
            if (!yoke.TryGetComponent(out WeaponVisualKick kick))
                kick = yoke.gameObject.AddComponent<WeaponVisualKick>();
            kick._muzzle = muzzle;
            kick._amplitude = amplitude;
            kick.CollectParts();
            kick.enabled = false;
            return kick;
        }

        public void Kick()
        {
            if (_parts == null || _parts.Length == 0) return;
            _offset = Mathf.Min(_offset + _amplitude, _amplitude * 1.6f);
            _dirLocal = _muzzle != null
                ? -(Quaternion.Inverse(transform.rotation) * _muzzle.forward)
                : Vector3.back;
            enabled = true;
        }

        private void CollectParts()
        {
            int n = 0;
            for (int i = 0; i < transform.childCount; i++)
                if (!IsMuzzleChain(transform.GetChild(i))) n++;
            _parts = new Transform[n];
            _rest = new Vector3[n];
            int k = 0;
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform c = transform.GetChild(i);
                if (IsMuzzleChain(c)) continue;
                _parts[k] = c;
                _rest[k] = c.localPosition;
                k++;
            }
        }

        private bool IsMuzzleChain(Transform c)
            => _muzzle != null && (c == _muzzle || _muzzle.IsChildOf(c));

        private void LateUpdate()
        {
            _offset *= Mathf.Exp(-RecoverPerSecond * Time.deltaTime);
            bool done = _offset < SnapEpsilon;
            if (done) _offset = 0f;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i] == null) continue;
                _parts[i].localPosition = done ? _rest[i] : _rest[i] + _dirLocal * _offset;
            }
            if (done) enabled = false;
        }
    }
}
