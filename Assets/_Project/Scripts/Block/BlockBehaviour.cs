using System;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Runtime instance of a block on a robot. Holds per-instance state
    /// (current HP, grid position) and references its shared
    /// <see cref="BlockDefinition"/> for stats.
    /// </summary>
    /// <remarks>
    /// Always lives as a child of a <see cref="BlockGrid"/>. Do not place
    /// directly into a scene — use <c>BlockGrid.PlaceBlock</c>.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BlockBehaviour : MonoBehaviour, IDamageable
    {
        [SerializeField] private BlockDefinition _definition;
        [SerializeField] private Vector3Int _gridPosition;
        [SerializeField] private float _currentHealth;
        [SerializeField] private Vector3 _dims;

        [Tooltip("Brightness at 0 HP, relative to the block's authored colour.")]
        [SerializeField, Range(0f, 1f)] private float _minDamageBrightness = 0.2f;

        /// <summary>Fired when this block reaches 0 HP, immediately before it is removed from the grid.</summary>
        public event Action<BlockBehaviour> Destroyed;

        /// <summary>
        /// Fired after every successful <see cref="TakeDamage"/> call, with the block + damage actually
        /// applied. Static so HUD overlays (floating damage numbers, hit markers) can subscribe without
        /// per-block wiring. PHYSICS_PLAN-aligned: server-authoritative damage events flow through this
        /// channel when MP lands; today every chassis fires it locally.
        /// </summary>
        public static event Action<BlockBehaviour, float> DamageDealt;

        public BlockDefinition Definition => _definition;
        public Vector3Int GridPosition => _gridPosition;
        public float CurrentHealth => _currentHealth;
        public bool IsAlive => _currentHealth > 0f;

        /// <summary>
        /// Per-instance "variable part" dimensions, copied from the
        /// <see cref="ChassisBlueprint.Entry"/> that placed this block.
        /// Vector3.zero means the consumer (AeroSurfaceBlock, RopeBlock, …)
        /// should fall back to its block-default values. See the Entry's
        /// Dims tooltip for the per-kind interpretation.
        /// </summary>
        public Vector3 Dims => _dims;

        /// <summary>
        /// Replace this block's dims at runtime. Used by tests + (eventually)
        /// the build-mode "select existing block and modify" flow. Consumers
        /// that need to react live (visual rebuild) can subscribe to
        /// <see cref="DimsChanged"/>.
        /// </summary>
        public void SetDims(Vector3 dims)
        {
            if (_dims == dims) return;
            _dims = dims;
            DimsChanged?.Invoke(this);
        }

        /// <summary>Fired after <see cref="SetDims"/> mutates <see cref="Dims"/>.</summary>
        public event Action<BlockBehaviour> DimsChanged;

        public float HealthFraction =>
            (_definition != null && _definition.MaxHealth > 0f)
                ? Mathf.Clamp01(_currentHealth / _definition.MaxHealth)
                : 1f;

        // Visual-damage cache.
        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_albedoColorId = Shader.PropertyToID("_AlbedoColor"); // MK Toon
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private MaterialPropertyBlock _mpb;

        /// <summary>Internal initializer called by <see cref="BlockGrid"/> at placement time.</summary>
        internal void Initialize(BlockDefinition definition, Vector3Int gridPosition, Vector3 dims = default)
        {
            _definition = definition;
            _gridPosition = gridPosition;
            _dims = dims;
            _currentHealth = definition != null ? definition.MaxHealth : 1f;
            CacheRenderers();
            UpdateDamageVisual();
        }

        private void Awake()
        {
            if (_renderers == null) CacheRenderers();
            UpdateDamageVisual();
        }

        /// <summary>Apply damage. Returns the actual damage dealt (clamped to remaining HP).</summary>
        public float TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return 0f;
            float dealt = Mathf.Min(amount, _currentHealth);
            _currentHealth -= dealt;
            UpdateDamageVisual();
            DamageDealt?.Invoke(this, dealt);
            if (_currentHealth <= 0f)
            {
                _currentHealth = 0f;
                Destroyed?.Invoke(this);
            }
            return dealt;
        }

        // -----------------------------------------------------------------
        // Visuals
        // -----------------------------------------------------------------

        private void CacheRenderers()
        {
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            _baseColors = new Color[_renderers.Length];
            _mpb = new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                Material mat = r != null ? r.sharedMaterial : null;
                Color c = Color.white;
                if (mat != null)
                {
                    // MK Toon writes to _AlbedoColor; URP/Lit to _BaseColor;
                    // Standard to _Color. Probe in priority order so the
                    // damage darkening always reads the actual authored hue.
                    if (mat.HasProperty(s_albedoColorId)) c = mat.GetColor(s_albedoColorId);
                    else if (mat.HasProperty(s_baseColorId)) c = mat.GetColor(s_baseColorId);
                    else if (mat.HasProperty(s_legacyColorId)) c = mat.GetColor(s_legacyColorId);
                }
                _baseColors[i] = c;
            }
        }

        private void UpdateDamageVisual()
        {
            if (_renderers == null || _renderers.Length == 0) return;
            float darken = Mathf.Lerp(_minDamageBrightness, 1f, HealthFraction);

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                Color baseCol = _baseColors[i];
                Color tinted = new Color(baseCol.r * darken, baseCol.g * darken, baseCol.b * darken, baseCol.a);
                _mpb.SetColor(s_albedoColorId, tinted);
                _mpb.SetColor(s_baseColorId, tinted);
                _mpb.SetColor(s_legacyColorId, tinted);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
