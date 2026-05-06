using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// One node in a Verlet rope chain — a position + previous position
    /// for implicit velocity. No Rigidbody, no Collider; the simulator
    /// integrates and constrains it directly.
    /// </summary>
    public struct VerletParticle
    {
        public Vector3 Position;
        public Vector3 PrevPosition;
    }

    /// <summary>
    /// Verlet-integrated rope chain. Anchored at both ends to real
    /// Rigidbodies (hub = chassis, tip = a per-rope tip-end body that
    /// hosts the adopted Hook / Mace block + the existing damage path).
    /// All interior nodes are particles only — N nodes per rope means
    /// 2 rigidbodies + N particles, vs the joint-chain's N rigidbodies +
    /// N joints. Per PHYSICS_PLAN § 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distance constraint solved iteratively (position-based dynamics):
    /// each pass projects every consecutive pair back onto the rest
    /// length, with the end nodes pinned to their rigidbody anchors.
    /// 8 iterations per FixedUpdate handles a 32-segment rope cleanly
    /// without visible stretching at typical chassis swing rates.
    /// </para>
    /// <para>
    /// Stored as a class so <see cref="VerletRopeSimulator"/> can hold a
    /// list reference without boxing. The arrays are pre-sized at build
    /// and reused (zero per-frame allocations).
    /// </para>
    /// </remarks>
    public sealed class VerletRopeChain
    {
        public VerletParticle[] Particles;
        public int Count;

        // Hub anchor: chassis Rigidbody + a local-space attach point.
        public Rigidbody HubRb;
        public Vector3 HubAnchorLocal;

        // Tip anchor: a per-rope Rigidbody at scene root that owns the
        // tip's collider + the adopted Hook/Mace block. Updated by the
        // simulator each step so its position tracks the last particle.
        public Rigidbody TipRb;

        public float SegmentLength;
        public float LinearDamping;
        public int Iterations;

        /// <summary>
        /// Bending stiffness in [0, 1]. Skip-one distance constraints pull
        /// particle i and i+2 toward 2 × segmentLength apart, scaled by
        /// this factor each iteration. Higher = straighter / stiffer
        /// rope; lower = floppier. With stiffness 0 the rope reverts to
        /// the chain-of-beads behaviour where adjacent segments can fold
        /// into Z-shapes; ~0.4 gives a fluid rope-like drape under
        /// gravity, ~0.8 reads as a stiffer cable.
        /// </summary>
        public float BendingStiffness;

        /// <summary>
        /// Number of integrate+solve passes per FixedUpdate. Sub-stepping
        /// stabilises a long chain whose endpoints move quickly: each
        /// pass has a smaller dt, integration error shrinks quadratically,
        /// and constraint corrections per pass are smaller — so the
        /// implicit velocity baked into PrevPosition by the solver doesn't
        /// kick the next step into oscillation. Cost is linear: 4
        /// sub-steps × 8 iterations = 32 distance-pair constraints per
        /// chain per FixedUpdate. Cheap.
        /// </summary>
        public int SubSteps;

        // Tip pin policy: in free flight the tip particle integrates with
        // gravity and the chain constraints pull it back toward the
        // chassis (so a moving plane drags the hook). When the tip
        // Rigidbody is constrained externally (HookBlock grapple joint
        // locks it to a target), the simulator should treat its position
        // as authoritative — pin the last particle to TipRb.position so
        // the chain conforms to the held tip.
        // The owning RopeBlock flips this flag when grapple state
        // changes (or the simulator can auto-detect by querying the
        // tip's joints; see IsTipExternallyConstrained).
        public bool PinTip;

        // Render hook: invoked after the constraint solver settles so the
        // owning RopeBlock can refresh its visual cylinder transforms.
        public System.Action OnPostSolve;
    }

    /// <summary>
    /// Scene-root singleton that integrates every registered Verlet rope
    /// chain in one batched <see cref="FixedUpdate"/>. Cache-friendly per
    /// PHYSICS_PLAN § 2 — the goal is "1 ms PhysX simulate at 5 rotors ×
    /// 4 ropes × 4 segs at 600 RPM" which the joint-chain solver fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Created lazily on first <see cref="Register"/>. Lives at scene root
    /// (DontDestroyOnLoad) so chains survive arena transitions; clears its
    /// list on scene unload via <see cref="OnDestroy"/>.
    /// </para>
    /// <para>
    /// One simulator per process. Multi-scene isolation isn't a goal here
    /// (rope physics doesn't care which scene owns the chassis); if it
    /// becomes one, swap the static for a per-scene hook.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VerletRopeSimulator : MonoBehaviour
    {
        private static VerletRopeSimulator s_instance;
        private readonly List<VerletRopeChain> _chains = new(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Statics survive domain reload; the GameObject doesn't. Drop
            // the reference so the next Register() spawns a fresh
            // singleton in the live scene.
            s_instance = null;
        }

        public static VerletRopeSimulator GetOrCreate()
        {
            if (s_instance != null) return s_instance;
            var go = new GameObject("[VerletRopeSimulator]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<VerletRopeSimulator>();
            return s_instance;
        }

        /// <summary>Read-only access to the live singleton, or null if no chain has registered yet.</summary>
        public static VerletRopeSimulator Instance => s_instance;

        /// <summary>Number of chains currently being integrated. Diagnostic only.</summary>
        public int ChainCount => _chains.Count;

        /// <summary>Sum of particle counts across every active chain. Diagnostic only.</summary>
        public int TotalParticleCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _chains.Count; i++)
                {
                    var c = _chains[i];
                    if (c != null) n += c.Count;
                }
                return n;
            }
        }

        public void Register(VerletRopeChain chain)
        {
            if (chain == null) return;
            if (!_chains.Contains(chain)) _chains.Add(chain);
        }

        public void Unregister(VerletRopeChain chain)
        {
            if (chain == null) return;
            _chains.Remove(chain);
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        // Cheap heuristic: a tip Rigidbody is "externally constrained"
        // when it's been flipped non-kinematic. RopeBlock.Build leaves
        // the tip kinematic in free flight (the simulator owns it via
        // MovePosition); HookBlock.Attach is the only path that flips
        // this off, so that PhysX integrates joint forces against the
        // chassis. Reading isKinematic is a free property check vs the
        // previous GetComponent<Joint>() per-chain-per-step walk —
        // measurable in the rotor stress tower (5 ropes × 50 Hz = 250
        // GetComponent calls/sec eliminated).
        private static bool IsTipExternallyConstrained(Rigidbody tipRb)
        {
            return tipRb != null && !tipRb.isKinematic;
        }

        // --- Integration step ----------------------------------------------

        private void FixedUpdate()
        {
            using var _scope = Robogame.Core.PerfMarkers.VerletRopeFixedUpdate.Auto();
            float dt = Time.fixedDeltaTime;
            Vector3 gravity = Physics.gravity;

            for (int ci = 0; ci < _chains.Count; ci++)
            {
                VerletRopeChain c = _chains[ci];
                if (c == null || c.Particles == null || c.Count < 2) continue;

                int N = c.Count;
                // Auto-detect external tip constraint (Hook grapple joint
                // on the tip Rigidbody) so a grappled rope conforms to
                // the held tip rather than fighting the joint.
                bool pinTip = c.PinTip || IsTipExternallyConstrained(c.TipRb);

                // Sub-step: run S × (integrate + solve) passes per
                // FixedUpdate, each with subDt = dt / S. Smaller dt =
                // smaller integration error per pass + smaller constraint
                // correction per pass = no implicit-velocity ringing.
                int subSteps = Mathf.Max(1, c.SubSteps);
                float subDt = dt / subSteps;
                float subDtSq = subDt * subDt;

                Vector3 hubWorld = c.HubRb != null
                    ? c.HubRb.transform.TransformPoint(c.HubAnchorLocal)
                    : c.Particles[0].Position;
                Vector3 tipWorld = (pinTip && c.TipRb != null)
                    ? c.TipRb.position
                    : c.Particles[N - 1].Position;

                // Per-particle damping ramp. Particles near the hub
                // barely damp so they retain inertia to track the
                // rapidly-moving chassis anchor cleanly; particles a
                // few segments out get the configured damping for the
                // "settled rope-tail" feel. Without this gradient, high
                // damping settings make the first ~3 particles jitter
                // visibly because the constraint solver has to do a
                // hard re-correct each frame against an inertia-killed
                // particle that can't follow the chassis on its own.
                // Ramps linearly to full damping over the first
                // _rampSegments particles (3 by default — short enough
                // that the bulk of the chain still gets the configured
                // damping, long enough that the chassis-end particles
                // don't fight the constraint solver).
                const float rampSegments = 3f;

                for (int sub = 0; sub < subSteps; sub++)
                {
                    // 1. Integrate. Hub always pinned (skip i=0); tip
                    //    pinned only in grappled mode (skip i=N-1 if so).
                    int integrateEnd = pinTip ? N - 1 : N;
                    for (int i = 1; i < integrateEnd; i++)
                    {
                        float distanceRamp = Mathf.Clamp01(i / rampSegments);
                        float effectiveDamping = c.LinearDamping * distanceRamp;
                        float dampMul = Mathf.Clamp01(1f - effectiveDamping * subDt);

                        Vector3 cur = c.Particles[i].Position;
                        Vector3 prev = c.Particles[i].PrevPosition;
                        Vector3 vel = (cur - prev) * dampMul;
                        Vector3 next = cur + vel + gravity * subDtSq;
                        c.Particles[i].PrevPosition = cur;
                        c.Particles[i].Position = next;
                    }

                    // 2. Iterative distance constraints. Pin hub each
                    //    iter; pin tip too if grappled. Adjacent-pair
                    //    constraints keep the rope from stretching;
                    //    skip-one constraints (bending stiffness) keep
                    //    consecutive segments from folding sharply, so
                    //    the rope drapes into smooth S-curves instead of
                    //    Z-folds. Interior pairs split corrections 50/50;
                    //    pinned ends absorb 0% (canonical PBD step).
                    float bendStiff = Mathf.Clamp01(c.BendingStiffness);
                    float bendTarget = 2f * c.SegmentLength;
                    for (int iter = 0; iter < c.Iterations; iter++)
                    {
                        c.Particles[0].Position = hubWorld;
                        if (pinTip) c.Particles[N - 1].Position = tipWorld;

                        // 2a. Adjacent-pair distance constraints.
                        for (int i = 0; i < N - 1; i++)
                        {
                            Vector3 a = c.Particles[i].Position;
                            Vector3 b = c.Particles[i + 1].Position;
                            Vector3 d = b - a;
                            float len = d.magnitude;
                            if (len < 1e-6f) continue;
                            float scale = (len - c.SegmentLength) / len;
                            bool aPin = (i == 0);
                            bool bPin = pinTip && (i + 1 == N - 1);
                            Vector3 corr = d * scale;
                            if (aPin && !bPin)
                            {
                                c.Particles[i + 1].Position -= corr;
                            }
                            else if (!aPin && bPin)
                            {
                                c.Particles[i].Position += corr;
                            }
                            else if (!aPin && !bPin)
                            {
                                c.Particles[i].Position += corr * 0.5f;
                                c.Particles[i + 1].Position -= corr * 0.5f;
                            }
                        }

                        // 2b. Skip-one bending stiffness constraints.
                        // Pull (i, i+2) toward 2×segmentLength apart at
                        // partial strength. The PBD-style rope bending
                        // term: at full stiffness the rope is rigid (a
                        // stick); at zero, beads-on-a-string. Mid-range
                        // gives natural rope feel.
                        if (bendStiff > 0f && N >= 3)
                        {
                            for (int i = 0; i < N - 2; i++)
                            {
                                Vector3 a = c.Particles[i].Position;
                                Vector3 b = c.Particles[i + 2].Position;
                                Vector3 d = b - a;
                                float len = d.magnitude;
                                if (len < 1e-6f) continue;
                                // Soft constraint: only the fraction
                                // bendStiff of the error is corrected
                                // each pass, so the bending term doesn't
                                // dominate gravity / chassis motion.
                                float scale = (len - bendTarget) / len * bendStiff;
                                bool aPin = (i == 0);
                                bool bPin = pinTip && (i + 2 == N - 1);
                                Vector3 corr = d * scale;
                                if (aPin && !bPin)
                                {
                                    c.Particles[i + 2].Position -= corr;
                                }
                                else if (!aPin && bPin)
                                {
                                    c.Particles[i].Position += corr;
                                }
                                else if (!aPin && !bPin)
                                {
                                    c.Particles[i].Position += corr * 0.5f;
                                    c.Particles[i + 2].Position -= corr * 0.5f;
                                }
                            }
                        }
                    }
                }

                // 4. Drive the tip Rigidbody when free. The body is
                //    kinematic in free flight (RopeBlock.Build sets it),
                //    so MovePosition / MoveRotation are clean transforms
                //    with no PhysX integrator overshoot — that's what
                //    eliminates the speed-correlated jitter we hit with
                //    a non-kinematic tip.
                //
                //    In grappled mode (pinTip), HookBlock.Attach has
                //    flipped the body to non-kinematic so the joint can
                //    pull the chassis through tension. The simulator
                //    leaves the body alone — PhysX integrates the joint
                //    + the chain pins its last particle to TipRb.position
                //    so the chain visually conforms.
                if (c.TipRb != null && N > 0 && !pinTip)
                {
                    c.TipRb.MovePosition(c.Particles[N - 1].Position);

                    if (N >= 2)
                    {
                        // Align the tip's local +Z with "down the chain"
                        // (direction from second-to-last to last particle)
                        // so the J-shaped hook (shaft along local +Z per
                        // HookBlock.BuildTipVisual) hangs from its
                        // attachment. Mace's sphere collider is rotation-
                        // agnostic so the same alignment is harmless.
                        //
                        // FromToRotation picks the shortest-arc rotation
                        // with no roll bias — the hook's facing around
                        // the chain axis is whatever the shortest arc
                        // produces. No chassis-coupled roll.
                        //
                        // MoveRotation (vs direct Rigidbody.rotation
                        // assignment) integrates cleanly through PhysX so
                        // Interpolate gets coherent prev/cur references
                        // and the hook visual flows smoothly across
                        // physics-step cadence.
                        Vector3 downChain = c.Particles[N - 1].Position - c.Particles[N - 2].Position;
                        float magSq = downChain.sqrMagnitude;
                        if (magSq > 1e-6f)
                        {
                            c.TipRb.MoveRotation(Quaternion.FromToRotation(Vector3.forward, downChain / Mathf.Sqrt(magSq)));
                        }
                    }
                }

                // 5. Render hook (cylinder transforms, line renderer, etc).
                c.OnPostSolve?.Invoke();
            }
        }
    }
}
