using System.Reflection;
using NUnit.Framework;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Tests.PlayMode
{
    /// <summary>
    /// TRACE[F-062]: proves <see cref="CommandLineMute"/> actually reaches
    /// the two live volume computations gated on
    /// <see cref="Tweakables.AudioMute"/> — MusicConductor's AudioSource
    /// fallback and AudioRouter's per-voice bus math. A flag that compiles
    /// but never lands on a real volume is worthless for LAUNCH-READINESS
    /// B3's quiet headless run.
    /// </summary>
    public sealed class CommandLineMuteAudioTests
    {
        // Test-only seam on CommandLineMute (internal field, set via
        // reflection — the project's established pattern for reaching
        // non-public members from another test assembly; see
        // RotorThrottleTests' SEAM NOTE).
        private static readonly FieldInfo ForcedField =
            typeof(CommandLineMute).GetField("s_forcedForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ComputeVolumeMethod =
            typeof(AudioRouter).GetMethod("ComputeVolume", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _conductorGo;
        private GameObject _routerGo;

        [SetUp]
        public void EnsureAudioMuteTweakableIsOff()
        {
            // The path under test is CommandLineMute, not the player's own
            // setting — keep the Tweakable off so the override is the only
            // thing moving the resulting volume.
            Tweakables.SetBool(Tweakables.AudioMute, false);
        }

        [TearDown]
        public void TearDown()
        {
            ForcedField.SetValue(null, null);
            if (_conductorGo != null) Object.Destroy(_conductorGo);
            if (_routerGo != null) Object.Destroy(_routerGo);
        }

        [Test]
        public void MusicConductor_CommandLineMuteActive_ResolvesVolumeZero()
        {
            ForcedField.SetValue(null, true);

            _conductorGo = new GameObject("MusicConductorUnderTest");
            MusicConductor conductor = _conductorGo.AddComponent<MusicConductor>();

            Assert.AreEqual(0f, ReadTrackVolume(conductor), 1e-6f,
                "CommandLineMute.Active must zero the Track AudioSource's volume.");
        }

        [Test]
        public void MusicConductor_CommandLineMuteOff_ResolvesNonZeroVolume()
        {
            ForcedField.SetValue(null, false);

            _conductorGo = new GameObject("MusicConductorUnderTest");
            MusicConductor conductor = _conductorGo.AddComponent<MusicConductor>();

            Assert.Greater(ReadTrackVolume(conductor), 0f,
                "With the flag off and Audio.Mute off, the track must be audible.");
        }

        [Test]
        public void AudioRouter_CommandLineMuteActive_ResolvesVolumeZero()
        {
            ForcedField.SetValue(null, true);

            _routerGo = new GameObject("AudioRouterUnderTest");
            AudioRouter router = _routerGo.AddComponent<AudioRouter>();

            float volume = (float)ComputeVolumeMethod.Invoke(router, new object[] { AudioBus.Sfx, 1f });
            Assert.AreEqual(0f, volume, 1e-6f,
                "CommandLineMute.Active must zero AudioRouter's bus volume math.");
        }

        [Test]
        public void AudioRouter_CommandLineMuteOff_ResolvesNonZeroVolume()
        {
            ForcedField.SetValue(null, false);

            _routerGo = new GameObject("AudioRouterUnderTest");
            AudioRouter router = _routerGo.AddComponent<AudioRouter>();

            float volume = (float)ComputeVolumeMethod.Invoke(router, new object[] { AudioBus.Sfx, 1f });
            Assert.Greater(volume, 0f,
                "With the flag off and Audio.Mute off, the bus math must pass volume through.");
        }

        private static float ReadTrackVolume(MusicConductor conductor)
        {
            // OnEnable runs synchronously from AddComponent (CLAUDE.md
            // Known failure modes), so the Track child + its ApplyVolume
            // call have already happened by the time we get here.
            Transform track = conductor.transform.Find("Track");
            Assert.IsNotNull(track, "MusicConductor.OnEnable must create the Track AudioSource child.");
            AudioSource src = track.GetComponent<AudioSource>();
            Assert.IsNotNull(src, "Track child must carry an AudioSource.");
            return src.volume;
        }
    }
}
