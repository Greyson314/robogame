using Robogame.Block;
using Robogame.Core;
using Robogame.Input;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Chassis-root controller for the one active-module ability. Polls the
    /// chassis <see cref="IInputSource"/> for the module key, owns the
    /// server-authoritative cooldown, and dispatches to
    /// <see cref="ModuleEffects"/> when fired. Dormant (zero per-frame work)
    /// until an <see cref="ActiveModuleBlock"/> registers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Server authority.</b> The cooldown tick and effect execution are
    /// gated on <see cref="NetworkContext"/>.<c>IsServer</c>. In singleplayer
    /// the offline stub is always the server, so this runs unchanged; when
    /// NGO lands, the local press becomes a ServerRpc and this stays the
    /// server-side gate — see the TODO in <see cref="Tick"/>. No client-side
    /// prediction: a one-shot ability doesn't need it.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ActiveModuleSystem : MonoBehaviour
    {
        private IInputSource _input;
        private Rigidbody _rb;
        private Robot _robot;
        private ActiveModuleBlock _module;

        private float _cooldownRemaining;
        private float _cooldownDuration;
        private bool _wasOnCooldown;

        /// <summary>True when a live module block is present and not destroyed.</summary>
        public bool HasModule => _module != null && _module.IsOperational;

        /// <summary>The chassis's active ability, or null if none.</summary>
        public ModuleKind? ModuleKindOrNull => HasModule ? _module.Kind : (ModuleKind?)null;

        /// <summary>Cooldown progress in [0,1]; 1 = ready to fire.</summary>
        public float ReadyFraction =>
            _cooldownDuration <= 0f ? 1f : Mathf.Clamp01(1f - _cooldownRemaining / _cooldownDuration);

        /// <summary>True when the ability can fire right now.</summary>
        public bool IsReady => HasModule && _cooldownRemaining <= 0f;

        public void Register(ActiveModuleBlock module)
        {
            // One module per chassis; last writer wins (a rebuild re-registers).
            _module = module;
        }

        public void Unregister(ActiveModuleBlock module)
        {
            if (_module == module) _module = null;
        }

        private void Awake()
        {
            _input = GetComponent<IInputSource>();
            _rb = GetComponent<Rigidbody>();
            _robot = GetComponent<Robot>();
        }

        private void Update()
        {
            if (_module == null) return; // dormant — no module on this chassis
            Tick(Time.deltaTime);
        }

        private void Tick(float dt)
        {
            // Server owns the cooldown clock + effect. SP: always server.
            // TODO(netcode): on a remote client, replace the direct
            // TryActivate below with a ServerRpc; the server validates the
            // cooldown here and ClientRpc-broadcasts the VFX/audio. Mirror
            // NetworkRobotCombat.FireCommandServerRpc.
            if (!NetworkContext.Instance.IsServer) return;

            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - dt);
                if (_cooldownRemaining <= 0f && _wasOnCooldown)
                {
                    _wasOnCooldown = false;
                    AudioRouter.PlayUI(AudioCue.ModuleReady);
                }
            }

            if (_input != null && _input.ModulePressed)
            {
                TryActivate();
            }
        }

        private void TryActivate()
        {
            if (!IsReady || !_module.IsOperational) return;

            ModuleKind kind = _module.Kind;
            Vector3 chassisPos = _rb != null ? _rb.worldCenterOfMass : transform.position;
            Vector3 modulePos = _module.transform.position;

            switch (kind)
            {
                case ModuleKind.EmpBurst:
                    ModuleEffects.EmpBurst(modulePos, _module.EffectRadius, _module.EffectDuration, _robot);
                    VfxSpawner.Spawn(VfxKind.EmpBurst, chassisPos, Quaternion.identity, _module.EffectRadius / 8f);
                    break;

                case ModuleKind.Blink:
                    if (_rb != null)
                    {
                        Vector3 dir = transform.forward;
                        Vector3 arrival = ModuleEffects.Blink(_rb, dir, _module.EffectRadius);
                        VfxSpawner.Spawn(VfxKind.BlinkArrive, arrival, Quaternion.identity);
                    }
                    break;

                case ModuleKind.DiscShield:
                    ModuleEffects.DiscShield(_robot, _module.EffectRadius, _module.EffectDuration);
                    VfxSpawner.Spawn(VfxKind.ShieldActivate, chassisPos, Quaternion.identity, _module.EffectRadius / 2.5f);
                    break;
            }

            AudioRouter.PlayOneShot(AudioCue.ModuleActivate, chassisPos);

            _cooldownDuration = _module.Cooldown;
            _cooldownRemaining = _cooldownDuration;
            _wasOnCooldown = true;
        }
    }
}
