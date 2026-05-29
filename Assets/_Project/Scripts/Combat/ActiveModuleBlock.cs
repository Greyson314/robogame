using Robogame.Block;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Per-block carrier for the chassis's active-module ability. Holds no
    /// cooldown state itself — that lives on the chassis-root
    /// <see cref="ActiveModuleSystem"/> (the server-authoritative location).
    /// This component's job is: expose the ability's kind + tuning, and be
    /// the <i>destructible</i> thing whose death disables the ability
    /// (functional disable, invariant-friendly — mirrors how a destroyed
    /// weapon block stops firing).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BlockBehaviour))]
    public sealed class ActiveModuleBlock : MonoBehaviour
    {
        private BlockBehaviour _bb;
        private ModuleDefinition _def;
        private ActiveModuleSystem _system;
        private ModuleDefinition.Tuning _tuning;

        /// <summary>The ability this chassis runs, chosen in the garage
        /// (<see cref="ChassisBlueprint.ActiveModuleKind"/>).</summary>
        public ModuleKind Kind { get; private set; }

        public float Cooldown => _tuning.Cooldown;
        public float EffectDuration => _tuning.EffectDuration;
        public float EffectRadius => _tuning.EffectRadius;

        /// <summary>False once the carrier block is destroyed/disabled — the
        /// system refuses to fire when this goes false.</summary>
        public bool IsOperational => isActiveAndEnabled;

        private void Awake()
        {
            _bb = GetComponent<BlockBehaviour>();
            _def = _bb != null && _bb.Definition != null
                ? _bb.Definition.GetComponentData<ModuleDefinition>()
                : null;
            ResolveKind();
        }

        private void OnEnable()
        {
            _system = GetComponentInParent<ActiveModuleSystem>();
            if (_system != null) _system.Register(this);
            if (_bb != null) _bb.Destroyed += HandleBlockDestroyed;
        }

        private void OnDisable()
        {
            if (_bb != null) _bb.Destroyed -= HandleBlockDestroyed;
            if (_system != null) _system.Unregister(this);
        }

        private void HandleBlockDestroyed(BlockBehaviour _)
        {
            // Go dark the instant the carrier dies, before the GameObject is
            // actually torn down a frame or two later by connectivity.
            if (_system != null) _system.Unregister(this);
        }

        private void ResolveKind()
        {
            Robot robot = GetComponentInParent<Robot>();
            Kind = robot != null && robot.Blueprint != null
                ? robot.Blueprint.ActiveModuleKind
                : ModuleKind.EmpBurst;
            // Per-kind tuning from the definition, or sane fallbacks when the
            // block carries no ModuleDefinition (e.g. EditMode tests).
            _tuning = _def != null
                ? _def.For(Kind)
                : Kind switch
                {
                    ModuleKind.Blink => new ModuleDefinition.Tuning(10f, 0f, 12f),
                    ModuleKind.DiscShield => new ModuleDefinition.Tuning(20f, 4f, 2.5f),
                    _ => new ModuleDefinition.Tuning(15f, 3f, 8f),
                };
        }
    }
}
