using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MidiPlayerTK;

namespace DemoMPTK
{

    //! [SimplestMidiExternalPlayer]
    public class SimplestMidiExternalPlayer : MonoBehaviour
    {
        /// Play a Local MIDI file or from a Web site. This class must be used with the prefab MidiExternalPlayer\n 
        public MidiExternalPlayer midiExternalPlayer;

        // Coroutine handle so we can stop it cleanly
        private Coroutine statusCoroutine;

        void Start()
        {
            if (midiExternalPlayer == null)
            {
                Debug.LogWarning("SimplestMidiExternalPlayer: midiExternalPlayer is not set in the inspector.");
                return;
            }

            // Set the URL of the MIDI file to play or defined a default URL in the inspector.
            midiExternalPlayer.MPTK_MidiName = "https://mptkapi.paxstellar.com/MIDI/Dreams.1.mid";

            // Start playing the MIDI file immediately (no auto start defined in the MidiExternalPlayer inspector)
            midiExternalPlayer.MPTK_Play();

            statusCoroutine = StartCoroutine(DisplayMidiStatusCoroutine());
        }

        void OnDisable()
        {
            // Stop the coroutine when this component is disabled to avoid dangling coroutines
            if (statusCoroutine != null)
            {
                StopCoroutine(statusCoroutine);
                statusCoroutine = null;
            }
        }

        void OnDestroy()
        {
            // Ensure coroutine is stopped on destroy as well
            if (statusCoroutine != null)
            {
                StopCoroutine(statusCoroutine);
                statusCoroutine = null;
            }
        }

        void Update()
        {
            // Press the space bar to play the MIDI file.
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (!midiExternalPlayer.MPTK_IsPlaying)
                    midiExternalPlayer.MPTK_Play();
                else
                    midiExternalPlayer.MPTK_Stop();
            }
        }

        // Coroutine that runs every second and logs MIDI status
        private IEnumerator DisplayMidiStatusCoroutine()
        {
            while (true)
            {
                if (midiExternalPlayer != null)
                {
                    string name = midiExternalPlayer.MPTK_MidiName ?? "(none)";
                    bool isPlaying = midiExternalPlayer.MPTK_IsPlaying;
                    bool isPaused = midiExternalPlayer.MPTK_IsPaused;
                    double positionMs = midiExternalPlayer.MPTK_Position;
                    string duration = midiExternalPlayer.MPTK_Duration != null ? midiExternalPlayer.MPTK_Duration.ToString() : "(unknown)";
                    Debug.Log($"[MIDI Status {midiExternalPlayer.MPTK_StatusLastMidiLoaded}] name:{name} playing:{isPlaying} paused:{isPaused} pos:{positionMs:F0} ms duration:{duration}");
                }
                else
                {
                    Debug.Log("[MIDI Status] midiExternalPlayer reference is null");
                }

                yield return new WaitForSeconds(1f);
            }
        }
    }
    //! [SimplestMidiExternalPlayer]
}