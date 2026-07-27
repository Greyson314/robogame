using System;
using Robogame.Combat;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Turns damage events into beat-quantised instrument stingers —
    /// the musical damage-feedback layer (ADR-0006). Subscribes to
    /// <see cref="MusicalHits"/>, accumulates hits per instrument
    /// inside one quantise window, and schedules ONE stinger per
    /// window on the next off-beat 8th of the
    /// <see cref="MusicConductor"/>'s grid. Kills upgrade the window
    /// to a full phrase on the next on-beat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The accumulate-then-flush shape is the anti-spam contract: a
    /// 12 Hz SMG stream reads as one swelling pluck per half-bar, not
    /// twelve plucks a second. Tiers: chip window → single note
    /// (walking the global pentatonic), heavy window → flourish,
    /// kill → phrase. Incoming hits (enemy → player) reuse the same
    /// instrument an octave down at reduced gain — register, not key,
    /// separates the teams (ADR-0006 § Decision).
    /// </para>
    /// <para>
    /// Lives on the arena camera and is bound by
    /// <c>ArenaController</c> like the other match consumers.
    /// Fixed-size buckets, no allocations at steady state (INV-6).
    /// Entirely cosmetic — nothing here feeds back into gameplay.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MusicalHitDirector : MonoBehaviour
    {
        // Quantise policy. Off-beat 8ths are the design's default slot —
        // the backing track owns the downbeats, player hits answer them.
        private const double SubdivisionBeats = 1.0;
        private const double OffbeatOffsetBeats = 0.5;
        private const double OnbeatOffsetBeats = 0.0;      // kills take the beat itself
        // Arm lead must exceed the finalize lookahead by at least a
        // frame, or a bucket could arm inside its own flush margin.
        private const double MinLeadSeconds = 0.30;
        private const double FinalizeLookaheadSeconds = 0.15;

        private const float IncomingPitch = 0.5f;          // octave down — darker register
        private const float IncomingVolumeScale = 0.8f;

        private struct Bucket
        {
            public bool Armed;
            public double SlotDsp;
            public float Damage;
            public bool Kill;
        }

        // One bucket per instrument (== ProjectileKind), outgoing and
        // incoming kept apart so trading fire yields call-and-response
        // rather than one blended blob.
        private const int InstrumentCount = 4;
        private readonly Bucket[] _outgoing = new Bucket[InstrumentCount];
        private readonly Bucket[] _incoming = new Bucket[InstrumentCount];

        // [instrument, tier] → cue. Indexed by InstrumentFor + StingerTier.
        private static readonly AudioCue[,] s_cues =
        {
            { AudioCue.StingerPluckNote,   AudioCue.StingerPluckFlourish,   AudioCue.StingerPluckPhrase   },
            { AudioCue.StingerBrassNote,   AudioCue.StingerBrassFlourish,   AudioCue.StingerBrassPhrase   },
            { AudioCue.StingerPianoNote,   AudioCue.StingerPianoFlourish,   AudioCue.StingerPianoPhrase   },
            { AudioCue.StingerTimpaniNote, AudioCue.StingerTimpaniFlourish, AudioCue.StingerTimpaniPhrase },
        };

        private MatchController _match;
        private Func<Robot, MatchSide> _sideLookup;
        private int _lastOutgoingInstrument = 1;   // brass — the default kill voice
        private int _pentStep;                     // note-tier walk up the scale

        // Combat heat → backing-track intensity (ADR-0007). Any musical
        // hit in either direction adds heat; heat decays exponentially,
        // so the track escalates while a brawl is live and cools when it
        // ends. Purely cosmetic — nothing reads this back into gameplay.
        private const float HeatHalfLifeSeconds = 4f;
        private const float HeatPerKill = 80f;
        // Heat per intensity unit (sustained-brawl damage scale). The
        // conductor clamps to the running track's authored range, so a
        // taller stem stack just keeps climbing at the same rate — a
        // mid-brawl kill (~60 heat + 80) tops out a 0..3 track.
        private const float HeatPerIntensityStep = 45f;
        private float _heat;

        public void Bind(MatchController match, Func<Robot, MatchSide> sideLookup)
        {
            if (_match != null) _match.KillRegistered -= HandleKill;
            _match = match;
            _sideLookup = sideLookup;
            if (_match != null) _match.KillRegistered += HandleKill;
            ClearBuckets();
        }

        private void OnEnable()
        {
            MusicalHits.Reported += HandleHit;
        }

        private void OnDisable()
        {
            MusicalHits.Reported -= HandleHit;
            if (_match != null) _match.KillRegistered -= HandleKill;
        }

        // -----------------------------------------------------------------
        // Event intake
        // -----------------------------------------------------------------

        private void HandleHit(Robot attacker, Robot victim, ProjectileKind kind, float amount)
        {
            if (_sideLookup == null || !MusicConductor.IsPlaying) return;

            MatchSide attackerSide = _sideLookup(attacker);
            MatchSide victimSide = _sideLookup(victim);
            int instrument = InstrumentFor(kind);

            if (attackerSide == MatchSide.Player && victimSide == MatchSide.Enemy)
            {
                Accumulate(_outgoing, instrument, amount, OffbeatOffsetBeats);
                _lastOutgoingInstrument = instrument;
                _heat += amount;
            }
            else if (attackerSide == MatchSide.Enemy && victimSide == MatchSide.Player)
            {
                Accumulate(_incoming, instrument, amount, OffbeatOffsetBeats);
                _heat += amount;
            }
        }

        private void HandleKill(MatchSide killerSide, MatchSide victimSide)
        {
            if (!MusicConductor.IsPlaying) return;
            _heat += HeatPerKill;
            // Player lands a kill → last-used instrument sings the full
            // phrase ON the beat (weight over syncopation). Player dies →
            // the dark mirror: timpani phrase, octave down.
            if (killerSide == MatchSide.Player)
                ArmKill(_outgoing, _lastOutgoingInstrument);
            else if (victimSide == MatchSide.Player)
                ArmKill(_incoming, 3 /* timpani */);
        }

        private static void Accumulate(Bucket[] buckets, int instrument, float amount, double offsetBeats)
        {
            ref Bucket b = ref buckets[instrument];
            b.Damage += amount;
            if (b.Armed) return;
            double slot = MusicConductor.NextSlotDsp(SubdivisionBeats, offsetBeats, MinLeadSeconds);
            if (slot < 0) { b = default; return; }
            b.Armed = true;
            b.SlotDsp = slot;
        }

        private static void ArmKill(Bucket[] buckets, int instrument)
        {
            ref Bucket b = ref buckets[instrument];
            double slot = MusicConductor.NextSlotDsp(SubdivisionBeats, OnbeatOffsetBeats, MinLeadSeconds);
            if (slot < 0) return;
            b.Armed = true;
            b.Kill = true;
            b.SlotDsp = slot;   // re-target: the phrase takes the downbeat even if a note was pending
        }

        // -----------------------------------------------------------------
        // Flush — schedule armed buckets whose slot is imminent
        // -----------------------------------------------------------------

        private void Update()
        {
            if (!MusicConductor.IsPlaying) return;

            // Exponential heat decay: half-life form so the fall-off is
            // frame-rate independent.
            _heat *= Mathf.Pow(0.5f, Time.unscaledDeltaTime / HeatHalfLifeSeconds);
            MusicConductor.SetIntensity(_heat / HeatPerIntensityStep);

            double now = AudioSettings.dspTime;
            Flush(_outgoing, now, incoming: false);
            Flush(_incoming, now, incoming: true);
        }

        private void Flush(Bucket[] buckets, double nowDsp, bool incoming)
        {
            for (int i = 0; i < buckets.Length; i++)
            {
                ref Bucket b = ref buckets[i];
                if (!b.Armed || nowDsp < b.SlotDsp - FinalizeLookaheadSeconds) continue;

                MusicMath.StingerTier tier = MusicMath.TierFor(b.Damage, b.Kill);
                AudioCue cue = s_cues[i, (int)tier];

                // Note tier walks the pentatonic (a volley climbs the
                // scale); flourish / phrase are runs in key. Incoming
                // drops everything an octave. Preferred voice is the
                // MPTK soundfont synth (real timbres, ADR-0007); the
                // baked-WAV pitch-shift path remains the fallback until
                // a soundfont is imported.
                if (tier == MusicMath.StingerTier.Note && !incoming)
                    _pentStep = (_pentStep + 1) % MusicalSfx.ScaleSteps;

                if (MusicMidi.IsAvailable)
                {
                    MusicMidi.PlayStinger(i, tier, _pentStep, incoming, b.SlotDsp,
                        incoming ? IncomingVolumeScale : 1f);
                }
                else
                {
                    float pitch;
                    if (incoming) pitch = IncomingPitch;
                    else if (tier == MusicMath.StingerTier.Note) pitch = MusicalSfx.ScalePitch(_pentStep);
                    else pitch = 1f;

                    AudioRouter.PlayScheduled(cue, b.SlotDsp, pitch,
                        incoming ? IncomingVolumeScale : 1f);
                }
                b = default;
            }
        }

        private void ClearBuckets()
        {
            Array.Clear(_outgoing, 0, _outgoing.Length);
            Array.Clear(_incoming, 0, _incoming.Length);
        }

        private static int InstrumentFor(ProjectileKind kind) => kind switch
        {
            ProjectileKind.SmgPellet   => 0,   // pluck
            ProjectileKind.Cannonball  => 1,   // brass
            ProjectileKind.MortarShell => 2,   // piano
            ProjectileKind.Bomb        => 3,   // timpani
            _                          => 1,
        };
    }
}
