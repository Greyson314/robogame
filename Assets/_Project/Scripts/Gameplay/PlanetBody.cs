using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// A spherical body that pulls rigidbodies toward its center with a
    /// constant-magnitude gravity, modelled after small Outer Wilds-style
    /// planets. Lives in the scene as a sibling of the visual sphere mesh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constant-magnitude (not <c>1/r²</c>) is a deliberate gameplay choice
    /// — see [docs/SPHERICAL_ARENAS.md](../../../../../docs/SPHERICAL_ARENAS.md)
    /// §3. Predictable jump heights, bullet drop, and fall damage win
    /// over realism at the radii we author for.
    /// </para>
    /// <para>
    /// The default radius (2400 m) sits inside the comfort window in §9
    /// of the same doc: peak vehicle speed produces ~0.3°/s camera-up
    /// rotation (well under the comfort threshold), horizon dip stays
    /// ~2.3° (still clearly curved), and a full lap takes ~19 minutes.
    /// </para>
    /// <para>
    /// <b>File layout:</b> Unity requires every <see cref="MonoBehaviour"/>
    /// to live in a file matching its class name for serialization to
    /// resolve a script GUID. <c>PlanetBody</c> used to share
    /// <c>PlanetGravity.cs</c> with <see cref="GravityField"/>, but Unity
    /// silently dropped the component on save because it couldn't find
    /// <c>PlanetBody.cs</c>. The interface and registry stay in
    /// <c>PlanetGravity.cs</c> (no script asset needed for a static
    /// class); this MonoBehaviour and <see cref="PlanetGravityBody"/>
    /// each get their own file.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlanetBody : MonoBehaviour, IGravitySource
    {
        [Tooltip("Surface radius in metres. 1500–3000 m is the motion-sickness sweet spot " +
                 "(see SPHERICAL_ARENAS.md §9). Smaller planets feel more curved but spin the " +
                 "camera-up vector fast enough to fatigue most players.")]
        [SerializeField, Min(1f)] private float _radius = 2400f;

        [Tooltip("Magnitude of gravitational acceleration at and inside the SOI (m/s²). " +
                 "Constant by design — see SPHERICAL_ARENAS.md §3.")]
        [SerializeField, Min(0.1f)] private float _surfaceGravity = 9.81f;

        [Tooltip("Extra altitude above the surface where gravity still applies (metres). " +
                 "Robots that briefly launch off the surface should still be pulled back, " +
                 "so 50–200 m of slack is sensible. Outside SOI, GravityField falls back " +
                 "to Physics.gravity (flat) so the chassis doesn't drift in zero-G.")]
        [SerializeField, Min(1f)] private float _soiPadding = 200f;

        public float Radius => _radius;
        public float SurfaceGravity => _surfaceGravity;
        public Vector3 Center => transform.position;

        public Vector3 GetGravityAt(Vector3 worldPosition)
        {
            if (!ContainsPoint(worldPosition)) return Vector3.zero;
            Vector3 toCenter = transform.position - worldPosition;
            float sq = toCenter.sqrMagnitude;
            // A rigidbody at the exact center sees zero gravity (avoids
            // NaN from normalising a zero vector). Players can't reach
            // there on any authored planet; this is just defensive.
            if (sq < 0.0001f) return Vector3.zero;
            return toCenter / Mathf.Sqrt(sq) * _surfaceGravity;
        }

        public bool ContainsPoint(Vector3 worldPosition)
        {
            float r = _radius + _soiPadding;
            return (worldPosition - transform.position).sqrMagnitude < r * r;
        }

        private void OnEnable()  => GravityField.Register(this);
        private void OnDisable() => GravityField.Unregister(this);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, _radius);
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, _radius + _soiPadding);
        }
    }
}
