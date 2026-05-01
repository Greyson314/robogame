using UnityEngine;

namespace Robogame.Block
{
    /// <summary>
    /// Purely-visual beacon attached to <see cref="BlockCategory.Cpu"/>
    /// blocks so the player can spot the CPU at a glance — it's the
    /// instakill cell, so leaving it visually identical to a structure
    /// cube was a footgun. Adds a cyan antenna + tip sphere with
    /// emissive material and a pulsing point light.
    /// </summary>
    /// <remarks>
    /// Wired up by <see cref="BlockGrid.PlaceBlock"/> at placement time.
    /// The beacon is non-collider, non-block: it has no
    /// <see cref="BlockBehaviour"/>, doesn't enter the grid, and won't
    /// catch raycasts. Damage hitting the host block destroys the whole
    /// GameObject hierarchy including this marker.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CpuBlockMarker : MonoBehaviour
    {
        // Pulled out of a serialized field on purpose: every CPU pulses
        // at the same rate, and we don't want a hundred inspector knobs
        // for one cosmetic effect.
        private const float PulseHz = 1.4f;
        private const float MinIntensity = 1.5f;
        private const float MaxIntensity = 4.0f;

        private static readonly Color s_beaconColor = new Color(0.20f, 0.85f, 0.95f);
        private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_albedoColorId = Shader.PropertyToID("_AlbedoColor"); // MK Toon
        private static readonly int s_emissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int s_legacyColorId = Shader.PropertyToID("_Color");

        private Light _light;
        private MaterialPropertyBlock _mpb;
        private Renderer[] _renderers;

        private void Awake()
        {
            BuildVisuals();
        }

        private void Update()
        {
            if (_light == null) return;
            // Cosine pulse keeps it visible at minimum (never fully dark).
            float t = 0.5f + 0.5f * Mathf.Cos(Time.time * PulseHz * Mathf.PI * 2f);
            _light.intensity = Mathf.Lerp(MinIntensity, MaxIntensity, t);
        }

        private void BuildVisuals()
        {
            // Antenna mast — thin tall cylinder rising from the top of the cube.
            // The host cube has localScale = cellSize (1m default), so local
            // y=0.5 is the top face. We stack the antenna on top of that.
            Transform mast = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, "CpuBeaconMast", PrimitiveType.Cylinder);
            mast.localScale = new Vector3(0.12f, 0.45f, 0.12f);
            mast.localPosition = new Vector3(0f, 0.5f + 0.45f, 0f);

            // Tip sphere — sits on top of the antenna.
            Transform tip = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, "CpuBeaconTip", PrimitiveType.Sphere);
            tip.localScale = new Vector3(0.32f, 0.32f, 0.32f);
            tip.localPosition = new Vector3(0f, 0.5f + 0.45f * 2f + 0.16f, 0f);

            // Apply emissive cyan to both.
            _renderers = new[]
            {
                mast.GetComponent<Renderer>(),
                tip.GetComponent<Renderer>(),
            };
            _mpb = new MaterialPropertyBlock();
            ApplyEmissive(_renderers, _mpb);

            // Point light at the tip — this is the bit that screams "kill me"
            // from across the arena.
            GameObject lightGo = new GameObject("CpuBeaconLight");
            lightGo.transform.SetParent(tip, worldPositionStays: false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = s_beaconColor;
            _light.range = 8f;
            _light.intensity = MaxIntensity;
            _light.shadows = LightShadows.None;
        }

        private static void ApplyEmissive(Renderer[] renderers, MaterialPropertyBlock mpb)
        {
            // HDR-ish boost: emission colour scaled past 1 so URP/Lit
            // actually glows in bloom rather than just looking flat-cyan.
            Color emission = s_beaconColor * 4f;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(s_baseColorId, s_beaconColor);
                mpb.SetColor(s_albedoColorId, s_beaconColor); // MK Toon
                mpb.SetColor(s_legacyColorId, s_beaconColor);
                mpb.SetColor(s_emissionColorId, emission);
                r.SetPropertyBlock(mpb);
                // Toggle the global emission keyword on the per-instance
                // material so URP actually samples _EmissionColor.
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(s_emissionColorId))
                {
                    r.sharedMaterial.EnableKeyword("_EMISSION");
                }
            }
        }
    }
}
