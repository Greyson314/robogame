using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// A free-body, jointed rope that dangles below its host block. Used
    /// for hanging cosmetic rigging — e.g. a banner trailing under a
    /// plane, an anchor chain hanging off a boat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spawned by <see cref="RobotRopeBinder"/> on any block whose id is
    /// <see cref="BlockIds.Rope"/>. The host cube's mesh is hidden; the
    /// visible thing is a chain of N capsule segments connected by
    /// <see cref="ConfigurableJoint"/>s, with segment 0 anchored to the
    /// chassis <see cref="Rigidbody"/> at the bottom face of the host
    /// cell.
    /// </para>
    /// <para>
    /// <b>Why segments live at scene root, not under the chassis.</b>
    /// Unity does not support <see cref="Rigidbody"/> children of a
    /// moving Rigidbody parent — the parent's transform writes would
    /// kinematically yank the children every frame and fight the
    /// solver. Per <c>docs/BEST_PRACTICES.md §3.1</c> the chassis is
    /// one rigidbody plus child colliders; rope segments are extra
    /// rigidbodies and therefore have to sit under their own
    /// scene-root container. The joint to the chassis carries them
    /// along while the physics solver figures out the rest.
    /// </para>
    /// <para>
    /// <b>Cost.</b> One container GameObject + N segment Rigidbodies
    /// per rope block. Default N = 8. A plane with one rope adds 8
    /// rigidbodies to the active count — well under the
    /// <c>BEST_PRACTICES.md §16</c> alarm of 64. Colliders are OFF by
    /// default to avoid self-jamming with the chassis (the rope is
    /// cosmetic, not a tow line — yet).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class RopeBlock : MonoBehaviour
    {
        [Header("Chain geometry")]
        [Tooltip("Number of capsule segments hanging below the host cell. " +
                 "Each segment is one visible \"link\" of the rope; the host " +
                 "cell itself is hidden.")]
        [SerializeField, Range(2, 32)] private int _segmentCount = 5;

        [Tooltip("Length of one segment in metres. 0.5 means a 5-segment rope hangs ~2.5m — about 2.5 chassis cells, enough to read as a rope at gameplay distance.")]
        [SerializeField, Min(0.05f)] private float _segmentLength = 0.5f;

        [Tooltip("Radius of one capsule segment in metres.")]
        [SerializeField, Min(0.01f)] private float _segmentRadius = 0.08f;

        [Tooltip("Mass per segment in kilograms. Total rope mass = N × this. " +
                 "Keep tiny so the rope doesn't perturb the chassis flight model.")]
        [SerializeField, Min(0.001f)] private float _segmentMass = 0.04f;

        [Header("Joint behaviour")]
        [Tooltip("Maximum bend per joint in degrees, applied symmetrically around all three angular axes.")]
        [SerializeField, Range(0f, 90f)] private float _angularLimit = 30f;

        [Tooltip("Per-segment linear damping. Bleeds off whip oscillation.")]
        [SerializeField, Min(0f)] private float _segmentLinearDamping = 0.10f;

        [Tooltip("Per-segment angular damping. Higher = stiffer rope.")]
        [SerializeField, Min(0f)] private float _segmentAngularDamping = 0.50f;

        [Tooltip("If true, segments get capsule colliders and can interact with " +
                 "the world (drag on the ground, snag on terrain). Off by " +
                 "default — colliders on a long chain risk self-collision " +
                 "with the chassis and other ropes, which causes jitter for " +
                 "what is currently a purely cosmetic feature.")]
        [SerializeField] private bool _segmentColliders = false;

        [Header("Visual")]
        [Tooltip("Tint applied to the segment cylinders. Defaults to a dark slate; " +
                 "override per-instance for team colours / banners later.")]
        [SerializeField] private Color _segmentColor = new Color(0.18f, 0.20f, 0.22f);

        // Spawned objects -------------------------------------------------
        private readonly List<Rigidbody> _segments = new();
        private GameObject _segmentContainer;
        private Rigidbody _anchorRb;
        // Adopted tip block (Hook or Mace placed adjacent in the chassis
        // grid). Stored so a Rebuild can put it back where we found it
        // before destroying the segments.
        private TipBlock _adoptedTip;
        private Transform _tipOriginalParent;
        private Vector3 _tipOriginalLocalPos;
        private Quaternion _tipOriginalLocalRot;

        // Property cache for tinting without instantiating a new material.
        private static readonly int s_albedoColorId = Shader.PropertyToID("_AlbedoColor");
        private static readonly int s_baseColorId   = Shader.PropertyToID("_BaseColor");
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            // The host cube is just a placement / damage hull. Hide its
            // mesh so the rope segments are what the player actually sees.
            BlockVisuals.HideHostMesh(gameObject);
        }

        private void OnEnable()
        {
            Tweakables.Changed += OnTweakablesChanged;
            // Build is idempotent (early-returns if segments already
            // exist) so this is a no-op when re-enabled after the
            // CaptureTemplate cascade. First-time enable builds fresh.
            Build();
        }
        private void OnDisable()
        {
            Tweakables.Changed -= OnTweakablesChanged;
            // Intentionally do NOT destroy segments here. Robot.CaptureTemplate
            // briefly SetActive(false) → Instantiate → SetActive(true) on the
            // chassis to clone it as a cold-storage template; that cascade
            // fires OnDisable on every block. DestroySegments would call
            // ReleaseAdoptedTip, which tries to SetParent the tip back to
            // its original parent (a chassis-hierarchy transform that's
            // currently mid-deactivation) and Unity throws. Skip the teardown
            // here — the segments live at scene root and aren't affected by
            // the chassis cascade. Real teardown happens in OnDestroy and
            // explicit Rebuild() calls (Tweakables change, parent swap).
        }
        private void OnDestroy() => DestroySegments();

        // Tweakables.Changed fires for any key — cheap to just rebuild,
        // since rope rebuilds are O(N segments) and N is tiny (≤32).
        // Drag-tuning a slider in the settings menu therefore updates
        // every active rope live without a scene reload.
        private void OnTweakablesChanged() => Rebuild();

        // Live geometry — Tweakables overrides the inspector default at
        // runtime so the settings menu is the single source of truth.
        private int   LiveSegmentCount   => Mathf.Clamp(Mathf.RoundToInt(Tweakables.Get(Tweakables.RopeSegmentCount)), 2, 32);
        private float LiveSegmentLength  => Mathf.Max(0.05f, Tweakables.Get(Tweakables.RopeSegmentLength));
        private float LiveSegmentRadius  => Mathf.Max(0.01f, Tweakables.Get(Tweakables.RopeSegmentRadius));
        private float LiveSegmentMass    => Mathf.Max(0.001f, Tweakables.Get(Tweakables.RopeSegmentMass));
        private float LiveAngularLimit   => Mathf.Clamp(Tweakables.Get(Tweakables.RopeAngularLimit), 0f, 90f);
        private float LiveLinearDamping  => Mathf.Max(0f, Tweakables.Get(Tweakables.RopeLinearDamping));
        private float LiveAngularDamping => Mathf.Max(0f, Tweakables.Get(Tweakables.RopeAngularDamping));

        // The host block can be reparented at runtime — Robot.DetachAsDebris
        // hands orphaned blocks their own Rigidbody and reparents to scene
        // root. When that happens our anchor changes; rebuild against it
        // so the rope stays hooked up to whichever rigidbody now owns us.
        private void OnTransformParentChanged() => Rebuild();

        private void Update()
        {
            // Cheap safety net for ancestor-rigidbody swaps the parent
            // change callback didn't catch (e.g. the chassis Rigidbody
            // being destroyed mid-flight in some edge case).
            Rigidbody current = GetComponentInParent<Rigidbody>();
            if (current != _anchorRb) Rebuild();
        }

        // -----------------------------------------------------------------
        // Build / teardown
        // -----------------------------------------------------------------

        private void Rebuild()
        {
            DestroySegments();
            Build();
        }

        private void Build()
        {
            // Idempotent: if segments are already up, the OnEnable
            // post-CaptureTemplate cascade is a no-op. Explicit Rebuild
            // callers tear down first via DestroySegments before calling
            // Build, so the early-out doesn't block them.
            if (_segmentContainer != null) return;

            _anchorRb = GetComponentInParent<Rigidbody>();
            if (_anchorRb == null) return; // no chassis to hang from yet

            // Container at scene root so kinematic transform writes from
            // the chassis don't drag our segment Rigidbodies around.
            _segmentContainer = new GameObject($"Rope_{name}_Segments");

            // Hang along the chassis-down axis at spawn — gravity will
            // straighten things out within a frame or two regardless.
            Vector3 down = -transform.up;
            // Anchor at the TOP face of the host cell. Rope cells are
            // typically placed directly under another chassis block (e.g.
            // under the plane's tail thruster), so starting the chain at
            // the host cell's top means it visually flush-attaches to
            // that block's underside instead of leaving a one-cell gap
            // through the (hidden) host cell volume.
            Vector3 anchorWorld = transform.position + transform.up * 0.5f;

            int   count    = LiveSegmentCount;
            float segLen   = LiveSegmentLength;
            float segRad   = LiveSegmentRadius;
            float segMass  = LiveSegmentMass;
            float linDamp  = LiveLinearDamping;
            float angDamp  = LiveAngularDamping;

            Rigidbody prev = _anchorRb;
            Vector3 prevAttachWorld = anchorWorld;

            for (int i = 0; i < count; i++)
            {
                Vector3 segCentre = anchorWorld + down * (segLen * (i + 0.5f));
                Quaternion segRot = Quaternion.LookRotation(down, transform.forward);

                GameObject seg = new GameObject($"Segment_{i}");
                seg.transform.SetParent(_segmentContainer.transform, worldPositionStays: false);
                seg.transform.SetPositionAndRotation(segCentre, segRot);

                BuildSegmentVisual(seg.transform, segLen, segRad);

                Rigidbody rb = seg.AddComponent<Rigidbody>();
                rb.mass = segMass;
                rb.linearDamping  = linDamp;
                rb.angularDamping = angDamp;
                rb.interpolation  = RigidbodyInterpolation.Interpolate;
                // Discrete is fine: short segments + capped angular limits
                // keep tunnelling out of reach. Continuous would be a
                // contact-solver tax we don't need on cosmetic chain.
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                if (_segmentColliders)
                {
                    CapsuleCollider cc = seg.AddComponent<CapsuleCollider>();
                    cc.radius = segRad;
                    cc.height = segLen;
                    cc.direction = 2; // capsule axis = local Z (segment forward)
                }

                BuildJoint(seg, rb, prev, prevAttachWorld, segLen);

                _segments.Add(rb);
                prev = rb;
                prevAttachWorld = segCentre + down * (segLen * 0.5f); // bottom of this segment

                // Tip collider on the LAST segment only. See RopeTip for
                // why per-segment colliders are deliberately avoided
                // (cost + joint-vs-contact solver fight). The tip stops
                // the rope from phasing through the world entirely
                // without paying for full-chain world collision.
                if (i == count - 1)
                {
                    // First, look for an adjacent Hook / Mace block to adopt
                    // as a custom tip. If found, the default sphere collider
                    // is skipped and the tip block's geometry / damage
                    // logic takes over.
                    if (TryAdoptTipBlock(rb))
                    {
                        // Adopted tip handles its own collider + IgnoreChassis;
                        // mass is summed into rb inside TryAdoptTipBlock.
                    }
                    else
                    {
                        RopeTip tip = seg.AddComponent<RopeTip>();
                        tip.Initialize(segRad * 1.6f);
                        tip.IgnoreChassisCollisions(_anchorRb.transform);
                    }
                }
            }
        }

        // Look at the rope's grid neighbours for a Hook / Mace block.
        // If found, reparent it under the last segment, sum its mass into
        // the segment's rigidbody, and wire a TipCollisionForwarder on
        // the segment so contact callbacks reach the tip block.
        private bool TryAdoptTipBlock(Rigidbody lastSegmentRb)
        {
            BlockBehaviour ropeHost = GetComponent<BlockBehaviour>();
            if (ropeHost == null) return false;
            BlockGrid grid = GetComponentInParent<BlockGrid>();
            if (grid == null) return false;

            // Six axial neighbours; first hit wins. The blueprint convention
            // is to place tips directly below the rope (-Y), but we accept
            // any face neighbour so a creative builder can route a hook
            // off the side of a rope-bearing arm.
            Vector3Int[] all =
            {
                new Vector3Int( 0,-1, 0), new Vector3Int( 0, 1, 0),
                new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
            };
            TipBlock tip = null;
            foreach (Vector3Int off in all)
            {
                if (!grid.TryGetBlock(ropeHost.GridPosition + off, out BlockBehaviour neighbor)) continue;
                if (neighbor == null) continue;
                tip = neighbor.GetComponent<TipBlock>();
                if (tip != null) break;
            }
            if (tip == null) return false;

            _adoptedTip            = tip;
            _tipOriginalParent     = tip.transform.parent;
            _tipOriginalLocalPos   = tip.transform.localPosition;
            _tipOriginalLocalRot   = tip.transform.localRotation;

            // Reparent under the last segment, sit at the segment center
            // (the segment's collider area is what we want the tip to
            // visually occupy), local rotation aligned to the segment
            // forward.
            tip.transform.SetParent(lastSegmentRb.transform, worldPositionStays: false);
            tip.transform.localPosition = Vector3.zero;
            tip.transform.localRotation = Quaternion.identity;

            // Relax the last segment's joint angular limits to Free so
            // the segment + adopted tip can pivot freely about the joint
            // anchor. Without this, the 30° default limit constrains the
            // hook's orientation to the chain's local frame and the
            // J-silhouette never points "down" via gravity — it stays
            // locked into whatever rotation the chain dictates. The
            // relaxation is rebuilt fresh on every rope Rebuild (since
            // the joint is part of the segment GameObject which we
            // destroy + recreate), so we don't need a "restore" path.
            ConfigurableJoint lastJoint = lastSegmentRb.GetComponent<ConfigurableJoint>();
            if (lastJoint != null)
            {
                lastJoint.angularXMotion = ConfigurableJointMotion.Free;
                lastJoint.angularYMotion = ConfigurableJointMotion.Free;
                lastJoint.angularZMotion = ConfigurableJointMotion.Free;
            }

            // Mass: the tip block's BlockDefinition mass is summed into
            // the segment so the chain's pendulum dynamics reflect the
            // weight at the end. Without this, a "heavy mace" wouldn't
            // actually swing harder than a "light hook".
            lastSegmentRb.mass += tip.Mass;

            // Forward collision callbacks to the tip block.
            TipCollisionForwarder fwd = lastSegmentRb.gameObject.AddComponent<TipCollisionForwarder>();
            fwd.Tip = tip;
            tip.AttachToHost(lastSegmentRb, _anchorRb);
            return true;
        }

        private void ReleaseAdoptedTip()
        {
            if (_adoptedTip == null) return;
            // Notify the tip its host is going away.
            _adoptedTip.DetachFromHost();
            // Reparent back to where we found it. If the original parent
            // is gone (chassis destroyed), unparent to scene root so the
            // tip GameObject doesn't leak under a destroyed segment.
            if (_tipOriginalParent != null)
            {
                _adoptedTip.transform.SetParent(_tipOriginalParent, worldPositionStays: false);
                _adoptedTip.transform.localPosition = _tipOriginalLocalPos;
                _adoptedTip.transform.localRotation = _tipOriginalLocalRot;
            }
            else
            {
                _adoptedTip.transform.SetParent(null, worldPositionStays: true);
            }
            _adoptedTip = null;
            _tipOriginalParent = null;
        }

        private void BuildSegmentVisual(Transform segRoot, float segLen, float segRad)
        {
            // Unity's primitive cylinder is height 2 along Y. Rotating to
            // align with segment-local Z (forward) and scaling Y by L/2
            // gives a capsule of correct length. Strip its collider — the
            // capsule we add ourselves above is the only physical body.
            Transform vis = BlockVisuals.GetOrCreatePrimitiveChild(
                segRoot, "Vis", PrimitiveType.Cylinder, stripCollider: true);
            vis.localRotation = Quaternion.Euler(90f, 0f, 0f);
            vis.localScale = new Vector3(segRad * 2f,
                                         segLen * 0.5f,
                                         segRad * 2f);

            MeshRenderer mr = vis.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                mr.GetPropertyBlock(mpb);
                mpb.SetColor(s_albedoColorId, _segmentColor);
                mpb.SetColor(s_baseColorId,   _segmentColor);
                mpb.SetColor(s_legacyColorId, _segmentColor);
                mr.SetPropertyBlock(mpb);
            }
        }

        private void BuildJoint(GameObject seg, Rigidbody rb, Rigidbody prev, Vector3 prevAttachWorld, float segLen)
        {
            float angLimit = LiveAngularLimit;
            ConfigurableJoint joint = seg.AddComponent<ConfigurableJoint>();
            joint.connectedBody = prev;
            joint.autoConfigureConnectedAnchor = false;

            // Anchor on this segment: top of capsule (= -L/2 along its forward Z).
            joint.anchor = new Vector3(0f, 0f, -segLen * 0.5f);

            // Connected anchor on prev: bottom of prev's last cell, in prev's local space.
            joint.connectedAnchor = prev.transform.InverseTransformPoint(prevAttachWorld);

            // Lock translation entirely — segments only swing.
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            // Limited angular freedom on all three axes gives a believable
            // rope-bend without the floppy degeneracy of full Free.
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit  = new SoftJointLimit { limit = -angLimit };
            joint.highAngularXLimit = new SoftJointLimit { limit =  angLimit };
            joint.angularYLimit     = new SoftJointLimit { limit =  angLimit };
            joint.angularZLimit     = new SoftJointLimit { limit =  angLimit };

            // We never want the rope to push the chassis or other segments
            // around through collision response — friction is what damping
            // is for. enableCollision=false also disables collision
            // between the joint pair, which is what we want for the
            // chassis↔segment-0 link in particular.
            joint.enableCollision    = false;
            joint.enablePreprocessing = false;
        }

        private void DestroySegments()
        {
            // Detach any adopted tip first, before the segment it's parented
            // under gets destroyed — otherwise Unity destroys the tip too.
            ReleaseAdoptedTip();
            _segments.Clear();
            _anchorRb = null;
            if (_segmentContainer == null) return;
            if (Application.isPlaying) Destroy(_segmentContainer);
            else                       DestroyImmediate(_segmentContainer);
            _segmentContainer = null;
        }
    }
}
