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

        [Tooltip("Brightness at 0 HP, relative to the block's authored colour.")]
        [SerializeField, Range(0f, 1f)] private float _minDamageBrightness = 0.2f;

        /// <summary>Fired when this block reaches 0 HP, immediately before it is removed from the grid.</summary>
        public event Action<BlockBehaviour> Destroyed;

        public BlockDefinition Definition => _definition;
        public Vector3Int GridPosition => _gridPosition;
        public float CurrentHealth => _currentHealth;
        public bool IsAlive => _currentHealth > 0f;

        public float HealthFraction =>
            (_definition != null && _definition.MaxHealth > 0f)
                ? Mathf.Clamp01(_currentHealth / _definition.MaxHealth)
                : 1f;

        // Visual-damage cache.
        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private MaterialPropertyBlock _mpb;

        /// <summary>Internal initializer called by <see cref="BlockGrid"/> at placement time.</summary>
        internal void Initialize(BlockDefinition definition, Vector3Int gridPosition)
        {
            _definition = definition;
            _gridPosition = gridPosition;
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
                    if (mat.HasProperty(s_baseColorId)) c = mat.GetColor(s_baseColorId);
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
                _mpb.SetColor(s_baseColorId, tinted);
                _mpb.SetColor(s_legacyColorId, tinted);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
