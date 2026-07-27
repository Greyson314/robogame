"""Offline synthesis of Robogame's combat-music placeholder assets (ADR-0006).

Outputs (44.1 kHz 16-bit WAV) into Assets/_Project/Audio/Generated/:
  track_warpulse_100bpm.wav                 8-bar D-rooted war-drum loop, sample-exact length
  stinger_{pluck,brass,piano,timpani}_{note,flourish,phrase}.wav

All pitched material is rooted at D so MusicalSfx's relative major-pentatonic
pitch multipliers stay consonant with the backing drone.
"""
import numpy as np
import wave
import os

SR = 44100
OUT = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\_Project\Audio\Generated"
# Intensity-layer stems live in StreamingAssets so FMOD Core can stream
# them from disk at runtime (they are not Unity AudioClips).
STREAM_OUT = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\StreamingAssets\Music"

D1, D2, D3, D4, D5 = 36.708, 73.416, 146.832, 293.665, 587.330
A2, A3, A4 = 110.0, 220.0, 440.0
# D major pentatonic ratios from root: D E F# A B
PENT = [1.0, 1.12246, 1.25992, 1.49831, 1.68179, 2.0]

def t_axis(dur):
    return np.arange(int(dur * SR)) / SR

def env_ad(n, attack, decay_tau):
    """Attack-decay envelope: linear attack, exponential decay."""
    e = np.exp(-np.arange(n) / (decay_tau * SR))
    a = int(max(1, attack * SR))
    e[:a] *= np.linspace(0, 1, a)
    return e

def normalize(x, peak=0.85):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x

def write_wav(name, data, stereo=False, out_dir=None):
    out_dir = out_dir or OUT
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name)
    pcm = (np.clip(data, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2 if stereo else 1)
        w.setsampwidth(2)
        w.setframerate(SR)
        if stereo and pcm.ndim == 1:
            pcm = np.column_stack([pcm, pcm])
        w.writeframes(pcm.tobytes())
    print("wrote", path, f"{len(data)/SR:.3f}s")

# ---------------------------------------------------------------- instruments

def pluck(freq, dur=0.8, bright=0.6):
    """Karplus-Strong pizzicato."""
    n = int(dur * SR)
    period = int(SR / freq)
    rng = np.random.default_rng(int(freq * 7))
    buf = rng.uniform(-1, 1, period)
    out = np.empty(n)
    for i in range(n):
        out[i] = buf[i % period]
        j = i % period
        buf[j] = bright * 0.5 * (buf[j] + buf[(j + 1) % period]) + (1 - bright) * buf[j]
    # Gentle body decay on top of the KS loss
    return out * env_ad(n, 0.002, dur * 0.45)

def brass(freq, dur=0.7, attack=0.04):
    """Detuned-saw brass stab, additive (band-limited-ish rolloff)."""
    t = t_axis(dur)
    out = np.zeros_like(t)
    for det in (-0.4, 0.0, 0.4):   # Hz detune per voice
        f = freq + det
        for h in range(1, 14):
            out += np.sin(2 * np.pi * f * h * t + h) / (h ** 1.15)
    # Slight vibrato swell for the "braaam" quality
    out *= 1 + 0.06 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(t / dur, 1)
    return out * env_ad(len(t), attack, dur * 0.55)

def piano(freq, dur=1.4):
    """Struck-string: inharmonic partials with per-partial decay + hammer noise."""
    t = t_axis(dur)
    out = np.zeros_like(t)
    B = 0.0004  # inharmonicity
    amps = [1.0, 0.55, 0.32, 0.2, 0.14, 0.1, 0.07, 0.05]
    for h, a in enumerate(amps, start=1):
        f = freq * h * np.sqrt(1 + B * h * h)
        out += a * np.sin(2 * np.pi * f * t) * np.exp(-t * (2.2 + 1.4 * h))
    rng = np.random.default_rng(int(freq))
    hammer = rng.uniform(-1, 1, len(t)) * np.exp(-t * 240) * 0.5
    return (out + hammer) * env_ad(len(t), 0.001, dur)

def timpani(freq, dur=1.3, sweep=1.5):
    """Pitch-dropping membrane boom: sweep + modal ring + mallet thump."""
    t = t_axis(dur)
    f_inst = freq * (1 + (sweep - 1) * np.exp(-t * 18))     # sweep down to root
    phase = 2 * np.pi * np.cumsum(f_inst) / SR
    out = np.sin(phase)
    for mode, a in ((1.5, 0.35), (1.98, 0.25), (2.44, 0.15)):
        out += a * np.sin(2 * np.pi * freq * mode * t) * np.exp(-t * 6)
    rng = np.random.default_rng(99)
    out += rng.uniform(-1, 1, len(t)) * np.exp(-t * 90) * 0.6
    return out * env_ad(len(t), 0.002, dur * 0.5)

def strings(freq, dur, attack=0.06):
    """Detuned saw ensemble with a soft bow attack — the layer-2 stem voice."""
    t = t_axis(dur)
    out = np.zeros_like(t)
    rng = np.random.default_rng(int(freq * 3))
    for det_cents in (-1.7, -0.6, 0.5, 1.4):
        f = freq * (1 + det_cents / 1200)
        ph = 2 * np.pi * f * t + rng.uniform(0, 2 * np.pi)
        for h in range(1, 10):
            out += np.sin(h * ph) / (h ** 1.3)
    return out * env_ad(len(t), attack, dur * 0.7)

def warsnare(gain=1.0, dur=0.16, tone=0.22, seed=0):
    """Tight field-drum hit: differentiated (high-tilted) noise burst plus
    a faint 190 Hz shell tone. Deliberately no low body — the percussion
    stem must sit ABOVE the timpani bed, not inside it."""
    n = int(dur * SR)
    rng = np.random.default_rng(seed)
    noise = np.diff(rng.uniform(-1, 1, n), prepend=0.0)
    body = np.sin(2 * np.pi * 190.0 * np.arange(n) / SR) * tone
    return (noise * 0.9 + body) * env_ad(n, 0.001, dur * 0.35) * gain

# ------------------------------------------------------------------ taiko kit
# Three-drum ensemble + kuchi shoga sequencer. The taiko-stem
# experiment was reverted by ear (LOG-147 round 4), but the kit stays:
# the SMG stingers use shime/chudaiko one-shots, and the sequencer is
# the transcription path if taiko patterns return. Registers slot
# around the timpani bed: o-daiko below, chu-daiko above, shime on top.

def odaiko(gain=1.0, dur=1.8, seed=0):
    """O-daiko boom: deep membrane sweep with a long boomy tail.
    Deliberately unpitched (no in-key modes) — the timpani own the
    tuned lows. Depth over slap (LOG-147 round 3)."""
    t = t_axis(dur)
    f = 42.0 * (1 + 1.1 * np.exp(-t * 16))          # ~88 → 42 Hz drop
    phase = 2 * np.pi * np.cumsum(f) / SR
    out = np.sin(phase)
    out += 0.4 * np.sin(phase * 1.52 + 0.6) * np.exp(-t * 6)    # shell knock
    rng = np.random.default_rng(1100 + seed)
    out += rng.uniform(-1, 1, len(t)) * np.exp(-t * 45) * 0.5   # skin slap, restrained
    return out * env_ad(len(t), 0.003, dur * 0.55) * gain

def chudaiko(gain=1.0, dur=0.9, seed=0):
    """Chu-daiko 'don': deep barrel punch with a boomy tail — at 8th
    spacing the tails overlap into a roll, not discrete taps."""
    t = t_axis(dur)
    f = 95.0 * (1 + 0.8 * np.exp(-t * 24))
    phase = 2 * np.pi * np.cumsum(f) / SR
    out = np.sin(phase)
    out += 0.3 * np.sin(phase * 1.48) * np.exp(-t * 10)         # shell mode
    out += 0.2 * np.sin(phase * 2.1) * np.exp(-t * 18)
    rng = np.random.default_rng(1200 + seed)
    out += rng.uniform(-1, 1, len(t)) * np.exp(-t * 70) * 0.35  # soft attack
    return out * env_ad(len(t), 0.002, dur * 0.5) * gain

def shime(gain=1.0, dur=0.12, seed=0):
    """Shime-daiko 'ka': tight rim crack, kept lean so the top never
    outweighs the barrels."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    rng = np.random.default_rng(1300 + seed)
    noise = np.diff(rng.uniform(-1, 1, n), prepend=0.0)
    ping = (np.sin(2 * np.pi * 880 * t) * 0.35 + np.sin(2 * np.pi * 1320 * t) * 0.12)
    return (noise * 0.7 + ping * np.exp(-t * 60)) * env_ad(n, 0.0008, dur * 0.3) * gain

# Kuchi shoga sequencer — taiko's oral notation, so real patterns are
# transcribable verbatim. One token per 8th-note slot; two-syllable
# tokens (doko / kara / tsuku) split the slot into two 16th strokes.
# 'c' strokes go to the stem's center voice, 'r' to its rim voice;
# tsu/tsuku are the ghost notes. UPPERCASE = accent.
STROKES = {
    "su": [],
    "don": [(0.0, "c", 1.0)],   "DON": [(0.0, "c", 1.3)],
    "doko": [(0.0, "c", 0.9), (0.5, "c", 0.7)],
    "tsu": [(0.0, "c", 0.32)],
    "tsuku": [(0.0, "c", 0.3), (0.5, "c", 0.26)],
    "ka": [(0.0, "r", 0.8)],    "KA": [(0.0, "r", 1.15)],
    "kara": [(0.0, "r", 0.7), (0.5, "r", 0.6)],
}

def kuchi(canvas, bar, pattern, center, rim, spb, beats_per_bar):
    tokens = pattern.split()
    assert len(tokens) == beats_per_bar * 2, "want one token per 8th: " + pattern
    b0 = bar * beats_per_bar * spb
    for slot, tok in enumerate(tokens):
        for off, kind, g in STROKES[tok]:
            voice = center if kind == "c" else rim
            place(canvas, voice(g, seed=bar * 31 + slot * 3 + int(off * 2)),
                  b0 + (slot + off) * spb / 2)

def oroshi(canvas, start, dur, voice, n_hits=14, peak=1.1):
    """Accelerating roll: wide ma shrinking into a rapid roll, swelling —
    the classic transition into the next phrase's downbeat. Long-ish
    hits so the tail of the roll booms rather than ticks."""
    for i in range(n_hits):
        at = start + dur * (i / n_hits) ** 0.55
        place(canvas, voice(0.35 + (peak - 0.35) * i / n_hits, dur=0.55, seed=900 + i), at)

def pizz(freq, dur=0.7):
    """Pizzicato string ensemble: two detuned KS voices, rounder and
    darker than the lute — matches the track's string section."""
    a = pluck(freq, dur, bright=0.35)
    b = pluck(freq * 1.004, dur, bright=0.42)
    return a * 0.7 + b * 0.5

INSTR = {"pluck": pluck, "brass": brass, "piano": piano, "timpani": timpani, "pizz": pizz}
ROOT = {"pluck": D4, "brass": D3, "piano": D4, "timpani": D2, "pizz": D4}

def place(canvas, clip, at):
    i = int(at * SR)
    n = min(len(clip), len(canvas) - i)
    if n > 0:
        canvas[i:i + n] += clip[:n]

def sequence(notes, total, instr):
    """notes: list of (time, ratio, gain[, dur]) rendered with the instrument."""
    canvas = np.zeros(int(total * SR))
    f0 = ROOT[instr]
    for note in notes:
        at, ratio, gain = note[0], note[1], note[2]
        kwargs = {"dur": note[3]} if len(note) > 3 else {}
        place(canvas, INSTR[instr](f0 * ratio, **kwargs) * gain, at)
    return normalize(canvas)

# ---------------------------------------------------------------- stingers

def gen_stingers():
    s16 = 0.11  # fast run step (~16th at 136bpm feel — snappy regardless of track)

    # SMG hybrid (LOG-147 round 3): the chip-damage note tier is a
    # shime rim shot that sits inside the taiko ji (rapid-fire weapon,
    # rapid rim shots — the director's pentatonic pitch-walk just
    # varies the tick); flourish/phrase keep the melodic payoff on
    # pizzicato strings. Same filenames — the cue rows don't change.
    write_wav("stinger_pluck_note.wav", normalize(shime(1.0, dur=0.18), 0.7))
    write_wav("stinger_pluck_flourish.wav", sequence(
        [(0, 1, .8), (s16, PENT[1], .8), (2*s16, PENT[2], .9), (3*s16, PENT[3], 1.0)], 1.2, "pizz"))
    phrase = np.zeros(int(2.2 * SR))
    for at, ratio, gain in ((0, 1, .8), (s16, PENT[2], .8), (2*s16, PENT[3], .85),
                            (3*s16, PENT[4], .9), (4*s16, 2.0, 1.0)):
        place(phrase, pizz(D4 * ratio, dur=1.0 if ratio == 2.0 else 0.6) * gain, at)
    place(phrase, chudaiko(0.9, seed=7), 4 * s16)     # kill lands with a barrel don
    place(phrase, shime(0.5, seed=8), 3.5 * s16)      #   ...set up by a grace rim tick
    write_wav("stinger_pluck_phrase.wav", normalize(phrase))

    write_wav("stinger_brass_note.wav", normalize(brass(D3, 0.7)))
    write_wav("stinger_brass_flourish.wav", sequence(
        [(0, 1, .9, .3), (0.14, PENT[3], .9, .3), (0.28, 2.0, 1.0, .8)], 1.3, "brass"))
    write_wav("stinger_brass_phrase.wav", sequence(
        [(0, 1, .9, .25), (0.16, 1, .8, .2), (0.32, PENT[3], .9, .3),
         (0.55, 2.0, 1.0, 1.0), (0.55, PENT[3], .55, 1.0), (0.55, 1.0, .5, 1.0)], 2.0, "brass"))

    write_wav("stinger_piano_note.wav", normalize(piano(D4, 1.2)))
    write_wav("stinger_piano_flourish.wav", sequence(
        [(0, 1, .9), (s16, PENT[1], .8), (2*s16, PENT[2], .85), (3*s16, PENT[4], .9), (4*s16, 2.0, 1.0)],
        1.6, "piano"))
    write_wav("stinger_piano_phrase.wav", sequence(
        [(0, 2.0, .9), (s16, PENT[4], .8), (2*s16, PENT[3], .8), (3*s16, PENT[2], .8),
         (4*s16, PENT[3], .85), (5*s16, PENT[4], .9), (6*s16, 2.0, 1.0, 1.8),
         (6*s16, PENT[3]*2, .55, 1.8)], 2.6, "piano"))

    write_wav("stinger_timpani_note.wav", normalize(timpani(D2, 1.3)))
    write_wav("stinger_timpani_flourish.wav", sequence(
        [(0, 1, .7, .5), (0.18, 1, .8, .5), (0.36, PENT[3], 1.0, 1.2)], 1.8, "timpani"))
    roll = [(i * 0.09, 1, 0.35 + 0.4 * i / 8, 0.35) for i in range(8)]
    write_wav("stinger_timpani_phrase.wav", sequence(
        roll + [(0.78, 1, 1.0, 1.6), (0.78, PENT[3], .5, 1.4)], 2.6, "timpani"))

# ---------------------------------------------------------------- backing track

def haas_stereo(mix):
    """Tiny Haas offset for width; returns an (n, 2) stereo array."""
    off = int(0.0006 * SR)
    right = np.concatenate([np.zeros(off), mix[:-off]])
    return np.column_stack([mix, right])

def gen_track():
    bpm, bars, beats_per_bar = 100, 8, 4
    spb = 60.0 / bpm
    total_samples = int(round(bars * beats_per_bar * spb * SR))   # exact loop length
    mix = np.zeros(total_samples)
    total = total_samples / SR

    # Drone: D2 + D1 sub with slow swell, quiet A2 fifth on odd bars.
    t = np.arange(total_samples) / SR
    swell = 0.75 + 0.25 * np.sin(2 * np.pi * t / (spb * 8) - np.pi / 2)
    drone = np.zeros(total_samples)
    for h, a in ((1, 1.0), (2, 0.35), (3, 0.18), (4, 0.08)):
        drone += a * np.sin(2 * np.pi * D2 * h * t + 0.7 * h)
    drone += 0.8 * np.sin(2 * np.pi * D1 * t)
    fifth = np.sin(2 * np.pi * A2 * t) * 0.25
    bar_idx = (t / (spb * beats_per_bar)).astype(int)
    fifth *= (bar_idx % 2 == 1)          # movement: fifth fades in on odd bars
    drone = (drone + fifth) * swell
    mix += normalize(drone, 0.30)

    # War timpani: tuned kettledrums on D/A (same timpani() voice as the bomb
    # stingers) so the drum bed rings in key with the drone instead of thudding.
    # (LOG-147 round 4: taiko-in-the-bed experiment reverted by ear —
    # this original pattern is the keeper.)
    drums = np.zeros(total_samples)
    for bar in range(bars):
        b0 = bar * beats_per_bar * spb
        place(drums, timpani(D2, 1.6, sweep=1.6) * 1.0, b0)             # big downbeat, long ring
        place(drums, timpani(A2, 1.0, sweep=1.4) * 0.7, b0 + 2 * spb)   # beat 3 on the fifth
        place(drums, timpani(D3, 0.6, sweep=1.3) * 0.45, b0 + 1 * spb)  # beat 2
        place(drums, timpani(D3, 0.6, sweep=1.3) * 0.5, b0 + 3 * spb)   # beat 4
        place(drums, timpani(A3, 0.4, sweep=1.3) * 0.35, b0 + 3.5 * spb)  # & of 4 push
        if bar % 4 == 3:                                                # timpani roll into next phrase
            for k in range(8):
                place(drums, timpani(D2, 0.35, sweep=1.25) * (0.25 + 0.09 * k),
                      b0 + (3.0 + k / 8) * spb)
    mix += normalize(drums, 0.62)

    # Faint taiko rim ticks on off-beat 8ths — the grid players will sting against.
    rng = np.random.default_rng(4)
    for beat in range(bars * beats_per_bar):
        at = (beat + 0.5) * spb
        tick = rng.uniform(-1, 1, int(0.03 * SR)) * env_ad(int(0.03 * SR), 0.001, 0.01)
        place(mix, tick * 0.05, at)

    bed = normalize(mix, 0.8)
    # Legacy single-file track — the conductor's Unity-AudioSource
    # fallback when FMOD stems are unavailable. Identical to the bed.
    write_wav("track_warpulse_100bpm.wav", haas_stereo(bed), stereo=True)
    write_wav("stem_bed.wav", haas_stereo(bed), stereo=True, out_dir=STREAM_OUT)

    # Layer 2 — low-string ostinato: driving 8ths on D2, answering A2 on
    # beat 3, an F#2 pickup lifting into odd bars. Enters at intensity > 0.
    st = np.zeros(total_samples)
    F2s = D2 * 1.25992   # F#2, the pentatonic third
    for bar in range(bars):
        b0 = bar * beats_per_bar * spb
        for eighth in range(beats_per_bar * 2):
            at = b0 + eighth * spb * 0.5
            on_beat = eighth % 2 == 0
            freq = A2 if eighth in (4, 5) else D2
            if bar % 2 == 1 and eighth == 7:
                freq = F2s
            gain = 0.9 if on_beat else 0.55
            place(st, strings(freq, 0.26, attack=0.02) * gain, at)
    write_wav("stem_strings.wav", haas_stereo(normalize(st, 0.55)), stereo=True, out_dir=STREAM_OUT)

    # Layer 3 — brass stabs on beats 1 and 3 (D3 + A2 double-stop), with
    # a held swell opening each 4-bar phrase. Enters at intensity > 1.
    br = np.zeros(total_samples)
    for bar in range(bars):
        b0 = bar * beats_per_bar * spb
        if bar % 4 == 0:
            place(br, brass(D3, 1.6, attack=0.25) * 0.9, b0)
            place(br, brass(A3, 1.6, attack=0.25) * 0.45, b0)
        else:
            place(br, brass(D3, 0.5) * 0.85, b0)
            place(br, brass(A2, 0.5) * 0.5, b0)
        place(br, brass(D3, 0.4) * 0.7, b0 + 2 * spb)
        place(br, brass(A2, 0.4) * 0.45, b0 + 2 * spb)
        if bar % 2 == 1:                                   # pickup 8th into the next bar
            place(br, brass(A2, 0.25) * 0.5, b0 + 3.5 * spb)
    write_wav("stem_brass.wav", haas_stereo(normalize(br, 0.6)), stereo=True, out_dir=STREAM_OUT)

    # Layer — "clockwork lute": Karplus-Strong 16th-note gallop in
    # D4–D5, the register the low stack leaves empty. Escalates rhythmic
    # subdivision (bed/strings own 8ths, this owns 16ths). Texture, not
    # melody — the player's stingers are the soloist. Enters 2→3.
    lu = np.zeros(total_samples)
    s16 = spb / 4
    GALLOP = [0, 2, 3, 4, 6, 7, 8, 10, 11, 12, 14, 15]   # x.xx per beat
    for bar in range(bars):
        b0 = bar * beats_per_bar * spb
        cycle = [1.0, PENT[3], PENT[4]] if bar % 2 == 0 else [1.0, PENT[2], PENT[3]]
        for k, slot in enumerate(GALLOP):
            ratio = cycle[k % 3]
            # Odd bars climb E–F#–A over the last three onsets into the
            # next downbeat; bar 7 walks down instead, resolving to the
            # loop's D.
            if bar % 2 == 1 and slot >= 12:
                run = [PENT[1], PENT[2], PENT[3]] if bar != 7 else [PENT[4], PENT[3], PENT[1]]
                ratio = run[GALLOP.index(slot) - 9]
            gain = 0.85 if slot % 4 == 0 else 0.55
            place(lu, pluck(D4 * ratio, dur=0.22, bright=0.7) * gain, b0 + slot * s16)
    write_wav("stem_lute.wav", haas_stereo(normalize(lu, 0.5)), stereo=True, out_dir=STREAM_OUT)

    # ---------------------------------------------------------- taiko stems
    # Three percussion layers authored in kuchi shoga (one token per 8th
    # slot — see STROKES). Sources for the vocabulary and base patterns:
    # horsebeat ji "DON doko" (San Jose Taiko Conservatory), matsuri ji
    # "DON doko don DON" (Wikipedia: Jiuchi), oroshi accelerating roll
    # (Taiko Colorado glossary). Ghost notes are the tsu strokes.

    # War percussion (enters 2.5→3): tight snare 16ths with a military
    # accent scheme, flam on each downbeat, doubled-stroke roll under
    # the timpani roll bars (%4==3). Top-of-the-range topper.
    pc = np.zeros(total_samples)
    seed = 0
    for bar in range(bars):
        b0 = bar * beats_per_bar * spb
        roll_bar = bar % 4 == 3
        for beat in range(beats_per_bar):
            for slot in range(4):
                at = b0 + (beat + slot / 4) * spb
                accent = 1.0 if (beat == 0 and slot == 0) else (0.8 if slot == 0 else (0.5 if slot == 2 else 0.3))
                seed += 1
                place(pc, warsnare(accent, seed=seed), at)
                if beat == 0 and slot == 0:                 # flam: grace hit 12 ms early
                    place(pc, warsnare(0.45, seed=seed + 991), at - 0.012 if at >= 0.012 else at)
                if roll_bar and beat >= 2:                  # 32nd doubles, ramping
                    seed += 1
                    place(pc, warsnare(0.25 + 0.1 * slot, dur=0.10, seed=seed), at + spb / 8)
    write_wav("stem_percussion.wav", haas_stereo(normalize(pc, 0.45)), stereo=True, out_dir=STREAM_OUT)

gen_stingers()
gen_track()
print("done")
