using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Lightweight visual-only follower for a simulated projectile.
    /// Owns an optional <see cref="TrailRenderer"/> (SMG-style streak)
    /// and an optional sphere mesh child (bomb / cannonball body).
    /// <see cref="ProjectileWorld"/> drives <see cref="SyncTo"/> each
    /// physics step from the simulation state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split between this visual and the simulation
    /// <see cref="ProjectileSpec"/> is the same one Overwatch uses
    /// for predicted projectiles (Tim Ford, GDC 2017): the visual
    /// can lerp / extrapolate independently of the authoritative
    /// physics state. For v1 we render at the exact step position;
    /// per-frame lerp between fixed-tick states is a future polish.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ProjectileVisual : MonoBehaviour
    {
        private TrailRenderer _trail;
        private Transform _body;
        private MeshRenderer _bodyRenderer;

        public void Configure(bool showTrail, bool showMesh, Color tint, float meshDiameter,
                              Material trailMaterial, Material meshMaterial)
        {
            // Trail — built lazily, kept across pool checkouts.
            if (showTrail)
            {
                if (_trail == null)
                {
                    _trail = gameObject.AddComponent<TrailRenderer>();
                    _trail.time = 0.06f;
                    _trail.startWidth = 0.12f;
                    _trail.endWidth = 0f;
                    _trail.minVertexDistance = 0.05f;
                    _trail.numCapVertices = 2;
                    _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _trail.receiveShadows = false;
                    _trail.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    _trail.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    _trail.sharedMaterial = trailMaterial;
                }
                _trail.startColor = tint;
                _trail.endColor = new Color(tint.r, tint.g, tint.b, 0f);
                _trail.Clear();
                _trail.emitting = true;
                _trail.enabled = true;
            }
            else if (_trail != null)
            {
                _trail.emitting = false;
                _trail.enabled = false;
            }

            // Body mesh — built lazily on first request, reused.
            if (showMesh)
            {
                if (_body == null)
                {
                    GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    body.name = "Body";
                    Collider primCol = body.GetComponent<Collider>();
                    if (primCol != null) Object.Destroy(primCol);
                    body.transform.SetParent(transform, worldPositionStays: false);
                    _body = body.transform;
                    _bodyRenderer = body.GetComponent<MeshRenderer>();
                    _bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _bodyRenderer.receiveShadows = false;
                    _bodyRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    _bodyRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                _body.gameObject.SetActive(true);
                _body.localScale = new Vector3(meshDiameter, meshDiameter, meshDiameter);
                if (_bodyRenderer != null && meshMaterial != null) _bodyRenderer.sharedMaterial = meshMaterial;
            }
            else if (_body != null)
            {
                _body.gameObject.SetActive(false);
            }
        }

        public void SyncTo(Vector3 pos, Vector3 vel)
        {
            transform.position = pos;
            if (vel.sqrMagnitude > 1e-5f)
                transform.rotation = Quaternion.LookRotation(vel.normalized, Vector3.up);
        }

        public void Stop()
        {
            if (_trail != null) _trail.emitting = false;
        }
    }
}
