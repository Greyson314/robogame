using System.Collections;
using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// A trigger-volume "magic healing pad" placed in an arena. While the
    /// player chassis overlaps it, the bot is gradually rebuilt back
    /// toward its frozen blueprint state — surviving blocks heal to full
    /// HP, and destroyed blocks pop back into their original positions
    /// in the chassis grid. Player-only — bots crossing the pad are
    /// ignored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why gradual?</b> Per-session direction (session 34): the player
    /// asked for a 10-second ramp instead of an instant rebuild. The work
    /// is split into N items (one per missing block to place + one per
    /// damaged-but-alive block to heal), and each item ticks at
    /// <c>maxDuration / robot.InitialBlockCount</c>. So a fully-destroyed
    /// chassis takes ~10 s to come back, while a chip-damaged one only
    /// takes a second or two — both feel proportional.
    /// </para>
    /// <para>
    /// <b>Cancellation.</b> If the player drives off the pad mid-rebuild
    /// the coroutine stops cleanly. Re-entering starts fresh (work list
    /// is recomputed; any blocks already restored stay restored). This
    /// is the "leave and re-enter" gate the user asked for instead of a
    /// pad-side cooldown.
    /// </para>
    /// <para>
    /// <b>MP shape.</b> The trigger callback raises
    /// <see cref="RepairRequested"/> as a seam — when netcode lands, this
    /// becomes a server RPC and the client only renders the visuals the
    /// server reports. Block placement goes through
    /// <see cref="BlockGrid.PlaceBlock"/>, which fires
    /// <c>BlockPlaced</c>; existing per-binder subscribers
    /// (<c>RobotWeaponBinder</c>, <c>RobotRopeBinder</c>,
    /// <c>RobotRotorBinder</c>, <c>RobotTipBlockBinder</c>) re-attach
    /// behaviours automatically without any RepairPad-side wiring.
    /// </para>
    /// <para>
    /// <b>Block-index ordering.</b> Repair iterates
    /// <c>blueprint.Entries</c> in the same order
    /// <c>ChassisFactory.Build</c> used at original spawn (same
    /// blueprint reference). Block indices in the rebuilt grid match the
    /// at-spawn ordering — the netcode contract on
    /// <c>BlockHitEvent.blockIndex</c> stability is preserved without
    /// any explicit sort here. (CLAUDE.md flags a pre-existing concern
    /// that the serializer doesn't enforce a Vector3Int sort; that's
    /// upstream of this feature and is left alone — see TODO below.)
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class RepairPad : MonoBehaviour
    {
        [Tooltip("Maximum total repair duration, in seconds, when the chassis is fully destroyed " +
                 "(every block needs replacing). Lightly damaged chassis finish faster — the pad ticks " +
                 "one block per (Duration / InitialBlockCount) seconds.")]
        [SerializeField, Min(0.5f)] private float _maxRepairDuration = 10f;

        [Tooltip("Hard floor on the per-step interval so a tiny chassis can't repair in a single frame. " +
                 "Visual cadence floor.")]
        [SerializeField, Min(0.01f)] private float _minStepInterval = 0.1f;

        [Tooltip("Cadence (seconds) for the ambient repair-glow column re-emission while the pad is active. " +
                 "Each pulse lasts ~2 s; we re-fire on this period so the column never fades visibly.")]
        [SerializeField, Min(0.1f)] private float _glowReemitInterval = 0.6f;

        [Tooltip("If true, the pad heals damaged-but-alive blocks back to full HP as part of the work list. " +
                 "If false, only re-places destroyed blocks. Keep on for v0 — the visual cadence reads better.")]
        [SerializeField] private bool _healDamagedBlocks = true;

        // Trigger overlap is per-collider; a chassis with N block colliders
        // fires N OnTriggerEnter / OnTriggerExit events as it crosses the
        // boundary. Counting overlaps lets us treat the chassis as "in
        // the volume" while any of its colliders is touching, and "out"
        // only when every one has left.
        private readonly Dictionary<Robot, int> _overlapCounts = new(2);

        private Robot _activeRobot;
        private Coroutine _repairCo;
        private Coroutine _glowCo;

        // Idle glow plays continuously from Awake, regardless of overlap
        // state, so the pad is visually findable from anywhere in the
        // arena. Repair-active glow uses a tighter cadence; we stop the
        // idle loop and start the active loop on entry, then swap back
        // on exit/complete.
        private Coroutine _idleGlowCo;

        /// <summary>
        /// Raised the moment a chassis becomes the active repair target — the
        /// seam a future server-authoritative implementation hooks into.
        /// </summary>
        public event System.Action<Robot> RepairRequested;

        /// <summary>Raised when a repair finishes successfully (every block restored).</summary>
        public event System.Action<Robot> RepairCompleted;

        /// <summary>Raised when an in-progress repair was cancelled (player left the pad).</summary>
        public event System.Action<Robot> RepairCancelled;

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Awake()
        {
            // The Collider is required by attribute, but we lean on the user
            // to set isTrigger=true in the editor. Force-set it here so a
            // misconfigured prefab still works correctly.
            Collider col = GetComponent<Collider>();
            if (col != null && !col.isTrigger) col.isTrigger = true;
        }

        private void OnEnable()
        {
            // Always-on idle glow so the pad is findable from anywhere.
            _idleGlowCo = StartCoroutine(IdleGlowLoop());
        }

        private IEnumerator IdleGlowLoop()
        {
            // Slower cadence than the active-repair pulse: enough that the
            // column reads as a steady marker, not so fast that it
            // upstages the repair feedback.
            var wait = new WaitForSeconds(_glowReemitInterval * 2f);
            while (true)
            {
                VfxSpawner.Spawn(VfxKind.RepairGlow, transform.position, Quaternion.identity, 1f);
                yield return wait;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Robot robot = ResolveRobot(other);
            if (robot == null) return;

            if (!_overlapCounts.TryGetValue(robot, out int count))
            {
                _overlapCounts[robot] = 1;
                BeginRepair(robot);
            }
            else
            {
                _overlapCounts[robot] = count + 1;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            Robot robot = ResolveRobot(other);
            if (robot == null) return;

            if (!_overlapCounts.TryGetValue(robot, out int count)) return;
            count--;
            if (count <= 0)
            {
                _overlapCounts.Remove(robot);
                EndRepair(robot, completed: false);
            }
            else
            {
                _overlapCounts[robot] = count;
            }
        }

        private void OnDisable()
        {
            // Pad got disabled mid-rebuild (scene unload, dev hot-reload):
            // stop coroutines cleanly so we don't yield-once-more on a
            // dead GameObject and noisy-warn into the console.
            if (_repairCo != null)    { StopCoroutine(_repairCo);    _repairCo = null; }
            if (_glowCo != null)      { StopCoroutine(_glowCo);      _glowCo = null; }
            if (_idleGlowCo != null)  { StopCoroutine(_idleGlowCo);  _idleGlowCo = null; }
            _overlapCounts.Clear();
            _activeRobot = null;
        }

        // -----------------------------------------------------------------
        // Robot resolution + filter
        // -----------------------------------------------------------------

        // The pad is player-only for v0 — bots crossing the volume are a
        // patrol-path artefact, not an intent to heal. Filter on the
        // PlayerInputHandler that ChassisFactory.Build adds when
        // addPlayerInputs:true. This is the same component that gates
        // any other "is this the player" runtime check in the project.
        private static Robot ResolveRobot(Collider col)
        {
            if (col == null) return null;
            Robot r = col.GetComponentInParent<Robot>();
            if (r == null || r.IsDestroyed) return null;
            // PlayerInputHandler is in Robogame.Input; Gameplay refs
            // Input so this is a clean lookup. Bots use
            // GroundBotInputSource / AirBotInputSource (also in Input)
            // so the PlayerInputHandler check is what cleanly distinguishes
            // "this is the human player" from "this is a bot".
            if (r.GetComponent<PlayerInputHandler>() == null) return null;
            return r;
        }

        // -----------------------------------------------------------------
        // Repair sequence
        // -----------------------------------------------------------------

        private void BeginRepair(Robot robot)
        {
            // Concurrent-pad guard: if a different robot was already in
            // the volume (impossible in singleplayer but cheap to be safe),
            // cancel its repair before starting the new one.
            if (_activeRobot != null && _activeRobot != robot)
            {
                EndRepair(_activeRobot, completed: false);
            }

            _activeRobot = robot;

            if (robot.Blueprint == null || robot.Library == null)
            {
                Debug.LogWarning(
                    "[Robogame] RepairPad: chassis has no Blueprint/Library reference (built outside ChassisFactory?). " +
                    "Skipping rebuild.",
                    this);
                return;
            }

            RepairRequested?.Invoke(robot);
            AudioRouter.PlayOneShot(AudioCue.RepairPadEnter, transform.position);

            _glowCo = StartCoroutine(EmitGlowLoop());
            _repairCo = StartCoroutine(DoRepair(robot));
        }

        private void EndRepair(Robot robot, bool completed)
        {
            if (_repairCo != null) { StopCoroutine(_repairCo); _repairCo = null; }
            if (_glowCo != null)   { StopCoroutine(_glowCo);   _glowCo   = null; }

            if (completed)
            {
                AudioRouter.PlayOneShot(AudioCue.RepairComplete, transform.position);
                RepairCompleted?.Invoke(robot);
            }
            else if (robot != null)
            {
                AudioRouter.PlayOneShot(AudioCue.RepairCancel, transform.position);
                RepairCancelled?.Invoke(robot);
            }

            if (_activeRobot == robot) _activeRobot = null;
        }

        private IEnumerator EmitGlowLoop()
        {
            var wait = new WaitForSeconds(_glowReemitInterval);
            while (true)
            {
                VfxSpawner.Spawn(VfxKind.RepairGlow, transform.position, Quaternion.identity, 1f);
                yield return wait;
            }
        }

        // Pre-allocated work-list buffers so the per-tick path is
        // allocation-free past the warmup. The List capacity grows once
        // on first repair and stays at that size for the GameObject's
        // lifetime — repairs fire infrequently enough that this is fine.
        private readonly List<WorkItem> _work = new(64);

        private IEnumerator DoRepair(Robot robot)
        {
            BuildWorkList(robot, _work);

            int total = _work.Count;
            if (total == 0)
            {
                // Chassis was already at full HP and full block count.
                EndRepair(robot, completed: true);
                yield break;
            }

            // Step interval scales with the chassis's at-spawn block count
            // so the user-visible "10 seconds for a fully-destroyed bot"
            // translates to "fast top-up for a lightly-damaged one." The
            // floor prevents zero-time loops on a one-block chassis.
            int initialCount = Mathf.Max(1, robot.InitialBlockCount);
            float stepInterval = Mathf.Max(_minStepInterval, _maxRepairDuration / initialCount);
            var stepWait = new WaitForSeconds(stepInterval);

            for (int i = 0; i < _work.Count; i++)
            {
                yield return stepWait;
                if (robot == null || robot.IsDestroyed)
                {
                    // Player took enough damage during repair to cross the
                    // mass-loss threshold and die mid-rebuild. Clear our
                    // overlap state so the pad doesn't leak the dead-Robot
                    // reference (the destroyed chassis's colliders never
                    // fire OnTriggerExit because they're disabled before
                    // they leave the volume).
                    _overlapCounts.Remove(robot);
                    EndRepair(robot, completed: false);
                    yield break;
                }
                ApplyStep(robot, _work[i]);
            }

            // Re-baseline so the mass-loss destroy threshold doesn't fire
            // on the first chip of damage taken after a successful repair.
            robot.ResetInitialAggregates();
            EndRepair(robot, completed: true);
        }

        private void BuildWorkList(Robot robot, List<WorkItem> work)
        {
            work.Clear();
            BlockGrid grid = robot.Grid;
            ChassisBlueprint blueprint = robot.Blueprint;
            if (grid == null || blueprint == null) return;

            ChassisBlueprint.Entry[] entries = blueprint.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                ChassisBlueprint.Entry entry = entries[i];
                if (grid.TryGetBlock(entry.Position, out BlockBehaviour existing) && existing != null)
                {
                    if (_healDamagedBlocks && existing.HealthFraction < 1f)
                    {
                        work.Add(WorkItem.HealAt(entry.Position));
                    }
                    // else: block is already healthy — no work needed.
                }
                else
                {
                    work.Add(WorkItem.PlaceFromBlueprint(i));
                }
            }
        }

        private void ApplyStep(Robot robot, WorkItem item)
        {
            using var _scope = PerfMarkers.RepairPadStep.Auto();
            BlockGrid grid = robot.Grid;
            ChassisBlueprint blueprint = robot.Blueprint;
            BlockDefinitionLibrary lib = robot.Library;
            if (grid == null || blueprint == null || lib == null) return;

            switch (item.Kind)
            {
                case WorkKind.Heal:
                {
                    if (grid.TryGetBlock(item.Position, out BlockBehaviour b) && b != null && b.Definition != null)
                    {
                        // Heal to full in one step — gradual cadence comes
                        // from the inter-step delay, not from per-block
                        // ramp. Visual: a small bright pop at the block
                        // and the colour flips back to fully healthy.
                        b.Heal(b.Definition.MaxHealth);
                        VfxSpawner.Spawn(VfxKind.BlockRespawn, b.transform.position, Quaternion.identity, 0.6f);
                        AudioRouter.PlayOneShot(AudioCue.RepairBlockRespawn, b.transform.position);
                    }
                    break;
                }
                case WorkKind.Place:
                {
                    ChassisBlueprint.Entry[] entries = blueprint.Entries;
                    if (item.EntryIndex < 0 || item.EntryIndex >= entries.Length) return;
                    ChassisBlueprint.Entry entry = entries[item.EntryIndex];

                    // Skip if a block somehow already came back at this
                    // position between work-list build and now (e.g. dev
                    // re-spawn). PlaceBlock returns null + warns on
                    // occupied cells; the early-return is just cleaner.
                    if (grid.TryGetBlock(entry.Position, out _)) return;

                    BlockDefinition def = lib.Get(entry.BlockId);
                    if (def == null) return;

                    BlockBehaviour placed = grid.PlaceBlock(def, entry.Position, entry.EffectiveUp, entry.Dims, entry.Pitch);
                    if (placed != null)
                    {
                        VfxSpawner.Spawn(VfxKind.BlockRespawn, placed.transform.position, Quaternion.identity, 1f);
                        AudioRouter.PlayOneShot(AudioCue.RepairBlockRespawn, placed.transform.position);
                    }
                    // Robot.HandleBlockPlaced calls RecalculateAggregates
                    // for us — mass / COM / inertia stay correct as the
                    // chassis grows back.
                    break;
                }
            }
        }

        // -----------------------------------------------------------------
        // Work-item plumbing
        // -----------------------------------------------------------------

        private enum WorkKind { Heal, Place }

        private readonly struct WorkItem
        {
            public readonly WorkKind Kind;
            public readonly Vector3Int Position;
            public readonly int EntryIndex;

            private WorkItem(WorkKind kind, Vector3Int pos, int idx)
            {
                Kind = kind; Position = pos; EntryIndex = idx;
            }

            public static WorkItem HealAt(Vector3Int pos) => new WorkItem(WorkKind.Heal, pos, -1);
            public static WorkItem PlaceFromBlueprint(int entryIndex) => new WorkItem(WorkKind.Place, Vector3Int.zero, entryIndex);
        }

        // -----------------------------------------------------------------
        // Procedural pad spawner — used by arena controllers so scenes
        // don't need a hand-authored prefab. Authors can override by
        // dropping a custom prefab into the arena and disabling the
        // procedural spawn on the controller.
        // -----------------------------------------------------------------

        /// <summary>
        /// Build a default RepairPad GameObject at <paramref name="worldPosition"/>,
        /// parented under <paramref name="sceneParent"/> (or scene root if null).
        /// Adds a flat trigger collider, a tinted cube mesh as the visual,
        /// and the <see cref="RepairPad"/> component.
        /// </summary>
        public static RepairPad CreateProcedural(Vector3 worldPosition, Transform sceneParent = null, string padName = "RepairPad")
        {
            var root = new GameObject(padName);
            if (sceneParent != null) root.transform.SetParent(sceneParent, worldPositionStays: false);
            root.transform.position = worldPosition;

            // Disc visual: a flat cube on the ground, palette-tinted cyan
            // with emission so it reads brightly even in shaded arenas.
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cube);
            disc.name = "Disc";
            disc.transform.SetParent(root.transform, worldPositionStays: false);
            disc.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            disc.transform.localScale = new Vector3(6f, 0.3f, 6f);
            Collider discCol = disc.GetComponent<Collider>();
            if (discCol != null) Object.Destroy(discCol);
            ApplyEmissiveCyan(disc);

            // Beacon visual: a tall thin pillar above the disc so the pad
            // is findable from across the arena. The disc + RepairGlow
            // particle column alone are flat — a vertical landmark fixes
            // the "I can't find it" failure mode.
            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beacon.name = "Beacon";
            beacon.transform.SetParent(root.transform, worldPositionStays: false);
            beacon.transform.localPosition = new Vector3(0f, 4f, 0f);
            beacon.transform.localScale = new Vector3(0.6f, 8f, 0.6f);
            Collider beaconCol = beacon.GetComponent<Collider>();
            if (beaconCol != null) Object.Destroy(beaconCol);
            ApplyEmissiveCyan(beacon);

            // Trigger volume: 6 m square, 4 m tall, sized to match the
            // disc plus a bit of headroom so a plane skimming low triggers
            // the repair without having to land. Centre raised so a
            // ground vehicle driving on top is fully inside.
            BoxCollider trig = root.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = new Vector3(6f, 4f, 6f);
            trig.center = new Vector3(0f, 2f, 0f);

            RepairPad pad = root.AddComponent<RepairPad>();
            Debug.Log(
                $"[Robogame] RepairPad spawned at {worldPosition}. Drive onto the cyan pillar to repair.",
                root);
            return pad;
        }

        // Build a fresh URP/Lit (or Standard) material with both base and
        // emission set to the Cyan palette token. Emission is what makes
        // the pad read as a glowing landmark in shaded arenas.
        private static void ApplyEmissiveCyan(GameObject target)
        {
            Renderer rend = target.GetComponent<Renderer>();
            if (rend == null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader) { name = "RepairPadMat" };
            Color baseCol = RuntimePalette.Cyan;
            Color emit = RuntimePalette.Cyan * 2.5f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emit);
            mat.EnableKeyword("_EMISSION");
            rend.sharedMaterial = mat;
        }
    }
}
