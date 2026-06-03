using System.Collections.Generic;
using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Chassis-root controller for the bot's module abilities (up to
    /// <see cref="ModuleBudget.MaxModules"/>). Owns one independent,
    /// server-authoritative cooldown per slot; polls the chassis
    /// <see cref="IInputSource"/> for each slot's ability key (1/2/3/4) and
    /// dispatches to <see cref="ModuleEffects"/> when fired. Dormant (zero
    /// per-frame work) until a <see cref="ModuleBlock"/> registers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Slots.</b> Each placed module block registers a slot, in canonical
    /// blueprint order (the assembler places blocks in <c>SetEntries</c> sort
    /// order, so OnEnable fires in that order). Slot index → ability key is
    /// therefore stable for the whole match (frozen at spawn, invariant #2). A
    /// destroyed carrier leaves its slot in place but empty, so the remaining
    /// keybinds don't shift mid-fight.
    /// </para>
    /// <para>
    /// <b>Server authority.</b> Cooldown ticks, effect execution, and the
    /// smoke/invisibility lifetime are gated on <see cref="NetworkContext"/>'s
    /// <c>IsServer</c>. In singleplayer the offline stub is always the server.
    /// When NGO lands, the local press becomes a ServerRpc and this stays the
    /// server-side gate; cloak/smoke visuals would broadcast via ClientRpc.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ModuleSystem : MonoBehaviour
    {
        /// <summary>One ability slot: a live carrier + its own cooldown clock.</summary>
        public sealed class Slot
        {
            public ModuleBlock Block;
            public float CooldownRemaining;
            public float CooldownDuration;
            public bool WasOnCooldown;

            public bool HasBlock => Block != null && Block.IsOperational;
            public ModuleKind Kind => Block != null ? Block.Kind : ModuleKind.EmpBurst;

            /// <summary>Cooldown progress in [0,1]; 1 = recharged.</summary>
            public float ReadyFraction =>
                CooldownDuration <= 0f ? 1f : Mathf.Clamp01(1f - CooldownRemaining / CooldownDuration);

            /// <summary>True when off cooldown AND contextually usable (spring needs ground).</summary>
            public bool IsAvailable =>
                HasBlock && CooldownRemaining <= 0f && Block.ContextAvailable;
        }

        // Number row 1..4 for slots 0..3 — R is taken (reload / hook release)
        // and the digit keys are free in arena (build-only otherwise).
        private static readonly string[] s_keyLabels = { "1", "2", "3", "4" };

        private IInputSource _input;
        private Rigidbody _rb;
        private Robot _robot;
        private readonly List<Slot> _slots = new(ModuleBudget.MaxModules);

        // Smoke / invisibility lifetime (server-tracked). Healthbar hides while
        // either is active.
        private float _smokeActiveUntil;
        private bool _invisActive;
        private float _invisActiveUntil;
        private float _invisDamageBudget;
        private float _invisDamageTaken;
        private StealthVisual _stealth;

        /// <summary>The live ability slots, in canonical (keybind) order.</summary>
        public IReadOnlyList<Slot> Slots => _slots;

        /// <summary>True while at least one module-carrier slot exists.</summary>
        public bool HasAnyModule
        {
            get
            {
                for (int i = 0; i < _slots.Count; i++) if (_slots[i].HasBlock) return true;
                return false;
            }
        }

        /// <summary>
        /// True while smoke or invisibility is active — the local healthbar
        /// surrogate is suppressed (and, with networked enemy healthbars, would
        /// hide from opponents too). Server-authoritative state.
        /// </summary>
        public bool HealthbarHidden => _invisActive || Time.time < _smokeActiveUntil;

        /// <summary>The 1/2/3/4 key label for <paramref name="slotIndex"/>.</summary>
        public static string KeyLabel(int slotIndex) =>
            slotIndex >= 0 && slotIndex < s_keyLabels.Length ? s_keyLabels[slotIndex] : "";

        public void Register(ModuleBlock module)
        {
            if (module == null) return;
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Block == module) return; // already registered
            // Reuse an emptied slot before growing, so a rebuild doesn't bloat
            // the list past the cap.
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Block == null) { _slots[i].Block = module; return; }
            }
            if (_slots.Count >= ModuleBudget.MaxModules) return; // cap (spawn trim should prevent this)
            _slots.Add(new Slot { Block = module });
        }

        public void Unregister(ModuleBlock module)
        {
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Block == module) { _slots[i].Block = null; return; }
        }

        private void Awake()
        {
            _input = GetComponent<IInputSource>();
            _rb = GetComponent<Rigidbody>();
            _robot = GetComponent<Robot>();
        }

        private void OnDisable()
        {
            // Defensive: drop any active invis subscription if the chassis is torn down.
            if (_invisActive) EndInvisibility(silent: true);
        }

        private void Update()
        {
            if (_slots.Count == 0) return;

            // Server owns the cooldown clock + effects + stealth lifetime.
            // SP: always server. TODO(netcode): remote client press → ServerRpc.
            if (!NetworkContext.Instance.IsServer) return;

            float dt = Time.deltaTime;

            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot.CooldownRemaining > 0f)
                {
                    slot.CooldownRemaining = Mathf.Max(0f, slot.CooldownRemaining - dt);
                    if (slot.CooldownRemaining <= 0f && slot.WasOnCooldown)
                    {
                        slot.WasOnCooldown = false;
                        AudioRouter.PlayUI(AudioCue.ModuleReady);
                    }
                }

                if (_input != null && _input.GetModulePressed(i) && slot.IsAvailable)
                    Activate(slot);
            }

            TickStealthLifetime();
        }

        private void Activate(Slot slot)
        {
            ModuleBlock block = slot.Block;
            ModuleKind kind = block.Kind;
            ModuleTuning.Resolved t = block.Tuning;
            Vector3 chassisPos = _rb != null ? _rb.worldCenterOfMass : transform.position;
            Vector3 modulePos = block.transform.position;

            switch (kind)
            {
                case ModuleKind.Spring:
                    if (_rb != null)
                    {
                        ModuleEffects.SpringLaunch(block.transform, _rb, t.Magnitude);
                        block.PlaySpringSquash();
                        VfxSpawner.Spawn(VfxKind.SpringBurst, modulePos, -block.transform.up);
                        AudioRouter.PlayOneShot(AudioCue.SpringLaunch, modulePos);
                    }
                    break;

                case ModuleKind.EmpBurst:
                    ModuleEffects.EmpBurst(modulePos, t.Magnitude, t.Duration, _robot);
                    VfxSpawner.Spawn(VfxKind.EmpBurst, chassisPos, Quaternion.identity, t.Magnitude / 8f);
                    AudioRouter.PlayOneShot(AudioCue.ModuleActivate, chassisPos);
                    break;

                case ModuleKind.Blink:
                    if (_rb != null)
                    {
                        Vector3 arrival = ModuleEffects.Blink(_rb, transform.forward, t.Magnitude);
                        VfxSpawner.Spawn(VfxKind.BlinkArrive, arrival, Quaternion.identity);
                        AudioRouter.PlayOneShot(AudioCue.ModuleActivate, chassisPos);
                    }
                    break;

                case ModuleKind.DiscShield:
                    ModuleEffects.DiscShield(_robot, t.Magnitude, t.Duration);
                    VfxSpawner.Spawn(VfxKind.ShieldActivate, chassisPos, Quaternion.identity, t.Magnitude / 4f);
                    AudioRouter.PlayOneShot(AudioCue.ModuleActivate, chassisPos);
                    break;

                case ModuleKind.Smoke:
                    // Visual-only obscurant: a lingering cloud + a healthbar
                    // blackout. No world mutation. Cloud scale tracks radius
                    // (default magnitude 6 → ~1.5× the already-big recipe).
                    VfxSpawner.Spawn(VfxKind.SmokeCloud, chassisPos, Quaternion.identity, t.Magnitude / 4f);
                    _smokeActiveUntil = Time.time + t.Duration;
                    AudioRouter.PlayOneShot(AudioCue.SmokeDeploy, chassisPos);
                    break;

                case ModuleKind.Invisibility:
                    BeginInvisibility(t.Duration, chassisPos);
                    break;

                case ModuleKind.Mines:
                    // Drop a proximity mine on the ground below the chassis.
                    // Magnitude = centre damage, Duration = mine lifetime.
                    // The detonation's own explosion VFX/audio fire when it
                    // goes off; this is just the deploy thunk.
                    ModuleEffects.DeployMine(_robot, t.Magnitude, t.Duration);
                    VfxSpawner.Spawn(VfxKind.HitSpark, modulePos, Vector3.up, 0.9f);
                    AudioRouter.PlayOneShot(AudioCue.ModuleActivate, modulePos);
                    break;
            }

            slot.CooldownDuration = t.Cooldown;
            slot.CooldownRemaining = t.Cooldown;
            slot.WasOnCooldown = true;
        }

        // -----------------------------------------------------------------
        // Invisibility lifetime + damage-break
        // -----------------------------------------------------------------

        private void BeginInvisibility(float duration, Vector3 atPos)
        {
            if (_robot == null) return;
            if (_invisActive) EndInvisibility(silent: true); // refresh

            _invisActive = true;
            _invisActiveUntil = Time.time + duration;
            _invisDamageTaken = 0f;
            _invisDamageBudget = 0.05f * TotalHealth(); // ends early at 5% HP lost

            _stealth = StealthVisual.Activate(_robot);
            BlockBehaviour.DamageDealt += OnBlockDamaged;

            VfxSpawner.Spawn(VfxKind.CloakShimmer, atPos, Quaternion.identity);
            AudioRouter.PlayOneShot(AudioCue.Cloak, atPos);
        }

        private void TickStealthLifetime()
        {
            if (!_invisActive) return;
            if (Time.time >= _invisActiveUntil || _invisDamageTaken >= _invisDamageBudget)
                EndInvisibility(silent: false);
        }

        private void EndInvisibility(bool silent)
        {
            BlockBehaviour.DamageDealt -= OnBlockDamaged;
            if (_stealth != null) { _stealth.Deactivate(); _stealth = null; }
            _invisActive = false;
            if (!silent && _robot != null)
            {
                Vector3 pos = _rb != null ? _rb.worldCenterOfMass : transform.position;
                VfxSpawner.Spawn(VfxKind.CloakShimmer, pos, Quaternion.identity);
            }
        }

        private void OnBlockDamaged(BlockBehaviour block, float dealt)
        {
            if (block == null || _robot == null) return;
            if (block.GetComponentInParent<Robot>() != _robot) return; // only our own hull
            _invisDamageTaken += dealt;
        }

        private float TotalHealth()
        {
            if (_robot == null || _robot.Grid == null) return 0f;
            float sum = 0f;
            foreach (KeyValuePair<Vector3Int, BlockBehaviour> kv in _robot.Grid.Blocks)
                if (kv.Value != null) sum += kv.Value.CurrentHealth;
            return sum;
        }
    }
}
