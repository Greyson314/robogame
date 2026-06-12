using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Animates the garage decor built by <see cref="GarageDecor"/>: slow
    /// starfield rotation, the spinning holo build-pad ring, the tumbling
    /// asteroid field outside the bubble, and the staggered blink of the
    /// platform rim beacons. Purely cosmetic — nothing here may affect
    /// gameplay state.
    /// </summary>
    /// <remarks>
    /// All fields are wired by <see cref="GarageDecor.Apply"/> right after
    /// AddComponent; nothing is scene-serialized. The per-frame path is
    /// allocation-free (INV-6): one cached MaterialPropertyBlock, static
    /// property ids, plain index loops.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GarageAmbience : MonoBehaviour
    {
        private static readonly ProfilerMarker s_marker =
            new ProfilerMarker("Robogame.GarageAmbience.Update");
        private static readonly int RotationId = Shader.PropertyToID("_Rotation");
        private static readonly int RimIntensityId = Shader.PropertyToID("_RimIntensity");

        public Material SkyboxMaterial;
        // Stars stay still — even slow skybox drift reads as the whole
        // world rotating and made building physically uncomfortable
        // (motion sickness). Motion in the garage sky comes from the
        // asteroid field orbit + cluster tumble instead, which parallax
        // reads as "objects moving" rather than "camera moving".
        public float SkyDegPerSec = 0f;

        public Transform HoloRing;
        public float HoloDegPerSec = 9f;

        public Transform AsteroidPivot;
        public float AsteroidOrbitDegPerSec = 0.4f;
        public Transform[] AsteroidClusters;
        public Vector3[] ClusterTumbleAxes;
        public float[] ClusterTumbleDegPerSec;

        // Index-aligned: BeaconLights[i] is null for beacons without a Light.
        public Renderer[] BeaconTips;
        public Light[] BeaconLights;
        public float[] BeaconPhases;

        /// <summary>
        /// Runtime-created assets (materials, textures) the decor owns.
        /// Destroyed with this component so repeated garage visits don't
        /// leak instances.
        /// </summary>
        public readonly List<Object> Owned = new List<Object>();

        private MaterialPropertyBlock _mpb;
        private bool _skyHasRotation;

        private void Start()
        {
            _mpb = new MaterialPropertyBlock();
            // Built-in Skybox/Cubemap exposes _Rotation; guard in case the
            // material is ever swapped for a shader that doesn't.
            _skyHasRotation = SkyboxMaterial != null && SkyboxMaterial.HasProperty(RotationId);
        }

        private void Update()
        {
            using var _ = s_marker.Auto();

            float t = Time.time;
            float dt = Time.deltaTime;

            if (_skyHasRotation)
                SkyboxMaterial.SetFloat(RotationId, (t * SkyDegPerSec) % 360f);

            if (HoloRing != null)
                HoloRing.Rotate(0f, HoloDegPerSec * dt, 0f, Space.World);

            if (AsteroidPivot != null)
                AsteroidPivot.Rotate(0f, AsteroidOrbitDegPerSec * dt, 0f, Space.World);

            if (AsteroidClusters != null)
            {
                for (int i = 0; i < AsteroidClusters.Length; i++)
                {
                    Transform c = AsteroidClusters[i];
                    if (c != null) c.Rotate(ClusterTumbleAxes[i], ClusterTumbleDegPerSec[i] * dt, Space.Self);
                }
            }

            if (BeaconTips != null && _mpb != null)
            {
                for (int i = 0; i < BeaconTips.Length; i++)
                {
                    Renderer tip = BeaconTips[i];
                    if (tip == null) continue;
                    // Staggered slow blink — the CPU-beacon motif
                    // (art-direction § Silhouette rule 6) at garage pace.
                    float w = 0.5f * (1f + Mathf.Sin(t * 1.7f + BeaconPhases[i]));
                    _mpb.SetFloat(RimIntensityId, Mathf.Lerp(0.8f, 2.6f, w));
                    tip.SetPropertyBlock(_mpb);
                    Light l = BeaconLights[i];
                    if (l != null) l.intensity = Mathf.Lerp(0.35f, 1.5f, w);
                }
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < Owned.Count; i++)
                if (Owned[i] != null) Destroy(Owned[i]);
            Owned.Clear();
        }
    }
}
