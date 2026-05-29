using Robogame.Block;
using UnityEngine;

namespace Robogame.Combat
{
    /// <summary>
    /// Designer-authored stats for the active-module block, carrying a
    /// separate tuning row for each <see cref="ModuleKind"/>. Rides a
    /// <see cref="BlockDefinition.ComponentData"/> slot, cast back via
    /// <c>GetComponentData&lt;ModuleDefinition&gt;()</c> — same pattern as
    /// <c>WeaponDefinition</c>. All values are config (Bucket A), never
    /// gameplay <c>Tweakable</c>s (invariant #1).
    /// </summary>
    /// <remarks>
    /// One asset backs all three abilities because the live ability is chosen
    /// per-chassis via <see cref="ChassisBlueprint.ActiveModuleKind"/>. The
    /// block resolves its stats by calling <see cref="For"/> with the chosen
    /// kind, so EMP / Blink / Shield can carry distinct cooldowns and radii
    /// from a single shared definition.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ModuleDef_New",
        menuName = "Robogame/Module Definition",
        order = 6)]
    public sealed class ModuleDefinition : ScriptableObject
    {
        /// <summary>Resolved tuning for one ability.</summary>
        public readonly struct Tuning
        {
            public readonly float Cooldown;
            public readonly float EffectDuration;
            public readonly float EffectRadius;

            public Tuning(float cooldown, float effectDuration, float effectRadius)
            {
                Cooldown = cooldown;
                EffectDuration = effectDuration;
                EffectRadius = effectRadius;
            }
        }

        [Header("EMP Burst")]
        [Tooltip("Seconds before EMP can fire again.")]
        [SerializeField, Min(0.5f)] private float _empCooldown = 15f;
        [Tooltip("How long enemy weapons stay disabled.")]
        [SerializeField, Min(0f)] private float _empDuration = 3f;
        [Tooltip("EMP lockout radius (m).")]
        [SerializeField, Min(0.5f)] private float _empRadius = 8f;

        [Header("Blink")]
        [Tooltip("Seconds before Blink can fire again.")]
        [SerializeField, Min(0.5f)] private float _blinkCooldown = 10f;
        [Tooltip("Teleport distance (m).")]
        [SerializeField, Min(0.5f)] private float _blinkRange = 12f;

        [Header("Disc Shield")]
        [Tooltip("Seconds before the shield can be raised again.")]
        [SerializeField, Min(0.5f)] private float _shieldCooldown = 20f;
        [Tooltip("How long the bubble persists.")]
        [SerializeField, Min(0f)] private float _shieldDuration = 4f;
        [Tooltip("Bubble radius (m).")]
        [SerializeField, Min(0.5f)] private float _shieldRadius = 2.5f;

        /// <summary>Resolve the tuning row for <paramref name="kind"/>.</summary>
        public Tuning For(ModuleKind kind) => kind switch
        {
            ModuleKind.Blink => new Tuning(_blinkCooldown, 0f, _blinkRange),
            ModuleKind.DiscShield => new Tuning(_shieldCooldown, _shieldDuration, _shieldRadius),
            _ => new Tuning(_empCooldown, _empDuration, _empRadius),
        };
    }
}
