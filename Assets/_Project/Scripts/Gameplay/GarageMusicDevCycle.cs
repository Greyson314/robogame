#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Robogame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Dev-only garage theme auditioning: <b>F7</b> steps through every
    /// MIDI in <c>StreamingAssets/Midi/</c>, <b>Shift+F7</b> steps back
    /// (LOG-148). Hearing candidates here matters because an external
    /// MIDI player renders them through the OS wavetable, not the
    /// project's GeneralUser GS bank — the timbres are not comparable.
    /// </summary>
    /// <remarks>
    /// Compile-stripped from release builds, so this can never affect a
    /// shipped game. Picking a winner means changing
    /// <see cref="GarageMusic.StreamingRelativePath"/>; this only
    /// auditions. F7 is free — NetDevHud owns F5 and F8–F11.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GarageMusicDevCycle : MonoBehaviour
    {
        private string[] _tracks;

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.f7Key.wasPressedThisFrame) return;

            _tracks ??= GarageMusic.AvailableTracks();
            if (_tracks.Length == 0)
            {
                Debug.Log("[GarageMusicDevCycle] No MIDIs in StreamingAssets/Midi/.");
                return;
            }

            bool back = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            int current = System.Array.IndexOf(_tracks, GarageMusic.CurrentTrack);
            int next = current < 0
                ? 0
                : (current + (back ? -1 : 1) + _tracks.Length) % _tracks.Length;
            GarageMusic.SwitchTo(_tracks[next]);
        }
    }
}
#endif
