using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Drives the Wing block's baked flap animation: playing (looped) in
    /// arena scenes, pinned to the rest/bind pose in the garage. Attached
    /// by <see cref="RobotAeroBinder"/> next to the Wing's
    /// <see cref="AeroSurfaceBlock"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flap is VISUAL-ONLY: the skinned mesh deforms, but the block's
    /// collider, lift math and blueprint state never move — placement
    /// reserves the swept airspace via <c>BlockOccupancy</c>'s Wing entry
    /// instead. Building happens only in the garage (invariant #2), where
    /// the wing holds its rest pose, so the build-mode view always matches
    /// the frozen blueprint.
    /// </para>
    /// <para>
    /// Costs: everything is decided ONCE in <see cref="Start"/> — scene
    /// kind, animation on/off, audio loop, tip trail. No Update method,
    /// no per-frame allocations. In the garage the legacy
    /// <see cref="Animation"/> component is fully disabled (not paused),
    /// so a parked wing costs zero.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WingFlapAnimator : MonoBehaviour
    {
        private Animation _anim;
        private AudioLoopHandle _loop;
        private Material _trailMat;

        private void Start()
        {
            // The rigged model instance lands under AeroSurfaceBlock's
            // Wing rig during Awake/OnEnable (EnsureWingModel); Start runs
            // after every OnEnable in the chassis build, so the search is
            // deterministic. The FBX importer wires the Animation
            // component with playAutomatically=false (see
            // WingModelImportSettings) — nothing plays until we say so.
            _anim = GetComponentInChildren<Animation>(includeInactive: true);
            if (_anim == null) return; // no rigged model wired (test rigs)

            if (!SceneKind.IsArena())
            {
                // Garage: rest pose, component disabled = zero cost.
                _anim.Stop();
                _anim.enabled = false;
                return;
            }

            _anim.enabled = true;
            _anim.wrapMode = WrapMode.Loop;
            _anim.Play();

            // Invariant 8 dressing: a soft wing-beat loop (no-op until the
            // cue is authored in the AudioCue library) and a faint scull
            // trail off the outer flap bone.
            _loop = AudioRouter.PlayLoop(AudioCue.WingFlapLoop, transform);
            AttachTipTrail();
        }

        private void OnDisable()
        {
            _loop?.Stop();
            _loop = null;
        }

        private void OnDestroy()
        {
            if (_trailMat != null) Destroy(_trailMat);
        }

        // Faint translucent ribbon on the outermost flap bone so the
        // scull reads as moving air, not just geometry. TrailRenderer is
        // allocation-free per frame; one small material per wing, owned
        // and destroyed here.
        private void AttachTipTrail()
        {
            Transform tipBone = FindDeep(transform, "Flap2");
            if (tipBone == null) return;

            var go = new GameObject("WingTipTrail");
            go.transform.SetParent(tipBone, worldPositionStays: false);
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.28f;
            trail.startWidth = 0.05f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.04f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _trailMat = new Material(sh) { name = "Mat_WingTipTrail" };
            if (_trailMat.HasProperty("_Surface")) _trailMat.SetFloat("_Surface", 1f);
            if (_trailMat.HasProperty("_ZWrite")) _trailMat.SetFloat("_ZWrite", 0f);
            _trailMat.renderQueue = 3000;
            _trailMat.SetOverrideTag("RenderType", "Transparent");
            _trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            Color c = new Color(0.92f, 0.90f, 0.82f, 0.18f); // linen haze
            if (_trailMat.HasProperty("_BaseColor")) _trailMat.SetColor("_BaseColor", c);
            if (_trailMat.HasProperty("_Color")) _trailMat.SetColor("_Color", c);
            trail.sharedMaterial = _trailMat;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
