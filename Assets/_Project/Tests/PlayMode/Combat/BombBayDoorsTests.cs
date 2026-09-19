using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Robogame.Combat;
using Robogame.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Robogame.Tests.PlayMode.Combat
{
    /// <summary>
    /// CHG-004 (bomb-bay doors: audible open, silent slam — amended shift
    /// 11, Grey: "minimal or nothing for slam"). Proves
    /// <see cref="BombBayDoors.Drop"/> requests
    /// <see cref="AudioCue.BombBayDoorOpen"/> exactly once through the
    /// existing <see cref="AudioRouter"/> one-shot path, and that the
    /// procedural swing (<c>BombBayDoors.Update</c>) never requests audio
    /// on its own — the door's snap-open is heard, the slam stays silent,
    /// and there is no per-frame audio work (INV-6).
    /// </summary>
    /// <remarks>
    /// Listens on AudioRouter's test-only recording seam — an internal
    /// static event fired at the top of the one-shot request path, before
    /// the cue's library entry (and therefore its clip) is even resolved.
    /// That ordering matters here: Assets/Universal Sound FX is absent
    /// from this clone, so the new cue's clip can never actually resolve
    /// on the rig, and a seam fired only on a successful play would never
    /// see anything to assert on. Reached via reflection — the project's
    /// established pattern for a non-public cross-assembly seam (no
    /// InternalsVisibleTo exists here; see RotorThrottleTests' SEAM NOTE,
    /// CommandLineMuteAudioTests).
    /// </remarks>
    public sealed class BombBayDoorsTests
    {
        private static readonly EventInfo OneShotRequestedEvent = typeof(AudioRouter).GetEvent(
            "OneShotRequested", BindingFlags.NonPublic | BindingFlags.Static);

        // EventInfo.Add/RemoveEventHandler go through GetAddMethod() /
        // GetRemoveMethod() WITHOUT a non-public search, so they throw on
        // an internal event ("no public add method exists"). Fetching the
        // accessor MethodInfo with nonPublic: true and invoking it
        // directly is the reflection-safe equivalent.
        private static readonly MethodInfo AddHandlerMethod = OneShotRequestedEvent?.GetAddMethod(nonPublic: true);
        private static readonly MethodInfo RemoveHandlerMethod = OneShotRequestedEvent?.GetRemoveMethod(nonPublic: true);

        private GameObject _host;
        private List<AudioCue> _requested;
        private Action<AudioCue> _handler;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(OneShotRequestedEvent,
                "AudioRouter.OneShotRequested not found — the CHG-004 test-only recording seam must exist.");
            _requested = new List<AudioCue>();
            _handler = cue => _requested.Add(cue);
            AddHandlerMethod.Invoke(null, new object[] { _handler });
        }

        [TearDown]
        public void TearDown()
        {
            RemoveHandlerMethod?.Invoke(null, new object[] { _handler });
            if (_host != null) UnityEngine.Object.Destroy(_host);
        }

        [UnityTest]
        public IEnumerator Drop_RequestsOpenCue_OncePerDrop()
        {
            _host = new GameObject("BombBayDoorsUnderTest");
            BombBayDoors doors = _host.AddComponent<BombBayDoors>();
            // Mirrors BombBayDoors.Attach's post-build state (disabled
            // until the first Drop) — a bare AddComponent defaults to
            // enabled, which would run Update() from frame one.
            doors.enabled = false;

            // A couple of idle frames: nothing has been dropped yet, so
            // nothing should request audio.
            yield return null;
            yield return null;
            Assert.AreEqual(0, _requested.Count,
                "no Drop() has happened yet — the doors must not request audio on their own.");

            doors.Drop();
            Assert.AreEqual(1, _requested.Count,
                "Drop() must request exactly one one-shot.");
            Assert.AreEqual(AudioCue.BombBayDoorOpen, _requested[0],
                "Drop() must request AudioCue.BombBayDoorOpen.");

            // Let the whole snap-open / hold / slam swing play out (the
            // clip's keyframes run through 1.55s — see BombBayDoors.KeyT).
            // The slam is silent by design: Update() must not add a
            // second request, at the slam or anywhere else in the clip.
            yield return new WaitForSeconds(1.6f);
            Assert.AreEqual(1, _requested.Count,
                "the door swing (BombBayDoors.Update) must never request audio on its own — the slam stays silent.");
        }
    }
}
