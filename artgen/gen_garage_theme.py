"""Composes the garage theme — a foggy Victorian waltz (LOG-149).

Writes Assets/StreamingAssets/Midi/garage-gaslamp-waltz.mid, which
GarageMusic renders live through GeneralUser GS (see
docs/subsystems/music.md). MIDI rather than synthesised WAV because the
soundfont gives real clarinet / harp / harpsichord samples — the
sine-and-noise approach was rejected by ear for sounding lightweight
(LOG-147).

Reference brief: Marleybone (Wizard101, Nelson Everhart) — clarinet
melody over fluttering harp and chiming bells, harpsichord keeping
time, strings crescendoing in, melody handed to horns, a waltz, minor
key with one brightening modal interchange. Original melodic material;
only the palette and form are borrowed.

Pure stdlib: a minimal Standard MIDI File writer, no dependencies.
"""
import os

OUT = r"C:\Users\Grey\Desktop\mutedtuple\robogame\Assets\StreamingAssets\Midi"
NAME = "garage-gaslamp-waltz.mid"

TPQ = 480                  # ticks per quarter note
BEATS_PER_BAR = 3          # 3/4 waltz
BPM = 88                   # slow enough to feel like fog, not a dance
BAR = TPQ * BEATS_PER_BAR

# ---------------------------------------------------------------- SMF writer

def vlq(n):
    """MIDI variable-length quantity."""
    out = bytearray([n & 0x7F])
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))

class Track:
    """One MIDI track. Events are collected with absolute ticks and
    sorted on render; note-offs sort before note-ons at the same tick so
    a repeated pitch retriggers instead of cutting itself off."""

    def __init__(self):
        self.events = []

    def add(self, tick, data, order=1):
        self.events.append((int(round(tick)), order, data))

    def meta(self, tick, data):
        self.add(tick, data, order=-1)

    def program(self, ch, prog):
        self.add(0, bytes([0xC0 | ch, prog]), order=-1)

    def control(self, ch, cc, value, tick=0):
        self.add(tick, bytes([0xB0 | ch, cc, value]), order=-1)

    def note(self, ch, tick, dur, pitch, vel):
        if pitch < 0 or pitch > 127:
            raise ValueError("pitch out of range: %s" % pitch)
        self.add(tick, bytes([0x90 | ch, pitch, max(1, min(127, int(vel)))]), 1)
        self.add(tick + dur, bytes([0x80 | ch, pitch, 0]), 0)

    def render(self):
        body = bytearray()
        last = 0
        for tick, _, data in sorted(self.events, key=lambda e: (e[0], e[1])):
            body += vlq(tick - last)
            body += data
            last = tick
        body += vlq(0) + b"\xff\x2f\x00"          # end of track
        return b"MTrk" + len(body).to_bytes(4, "big") + bytes(body)

def write_midi(path, tracks):
    header = (b"MThd" + (6).to_bytes(4, "big") + (1).to_bytes(2, "big") +
              len(tracks).to_bytes(2, "big") + TPQ.to_bytes(2, "big"))
    with open(path, "wb") as f:
        f.write(header)
        for t in tracks:
            f.write(t.render())

# ---------------------------------------------------------------- harmony

# D minor. Chord tones are written out rather than derived so voicings
# stay under hand control — the Neapolitan (Eb) and the harmonic-minor
# C# in the A7 are the two "Victorian mystery" colours.
CHORDS = {
    "Dm":  {"bass": 38, "tones": [62, 65, 69]},          # D  F  A
    "Gm":  {"bass": 43, "tones": [62, 67, 70]},          # G  Bb D
    "A7":  {"bass": 45, "tones": [64, 69, 73]},          # A  C# E  (7th in melody)
    "Bb":  {"bass": 46, "tones": [62, 65, 70]},          # Bb D  F
    "F":   {"bass": 41, "tones": [65, 69, 72]},          # F  A  C
    "C":   {"bass": 36, "tones": [64, 67, 72]},          # C  E  G
    "Eb":  {"bass": 39, "tones": [63, 67, 70]},          # Eb G  Bb — Neapolitan
}

# 32 bars: A (fog) / A' (harp joins, Neapolitan) / B (strings + horn,
# brightens to the relative major) / A'' (sinks back, A7 hands to the
# loop's opening Dm).
PROG = [
    "Dm", "Dm", "Gm", "Dm", "Bb", "Gm", "A7", "Dm",
    "Dm", "F",  "Gm", "Eb", "Dm", "A7", "Dm", "A7",
    "F",  "C",  "Dm", "Bb", "F",  "C",  "Gm", "A7",
    "Dm", "Dm", "Gm", "Eb", "Dm", "A7", "Dm", "A7",
]
BARS = len(PROG)

# ---------------------------------------------------------------- melody
# (bar, beat, pitch, beats). Clarinet stays in its chalumeau/throat
# register — dark and woody, which is where the fog lives.

MELODY_A = [
    (0, 0, 57, 2), (0, 2, 62, 1),
    (1, 0, 65, 1.5), (1, 1.5, 64, .5), (1, 2, 62, 1),
    (2, 0, 58, 2), (2, 2, 60, 1),
    (3, 0, 62, 2.5),
    (4, 0, 65, 2), (4, 2, 67, 1),
    (5, 0, 70, 2), (5, 2, 67, 1),
    (6, 0, 69, 2), (6, 2, 73, 1),          # C# — harmonic-minor bite
    (7, 0, 62, 2.5),
]

MELODY_A2 = [
    (8, 0, 62, 1), (8, 1, 65, 1), (8, 2, 69, 1),
    (9, 0, 72, 2), (9, 2, 69, 1),
    (10, 0, 70, 1.5), (10, 1.5, 69, .5), (10, 2, 67, 1),
    (11, 0, 63, 2), (11, 2, 67, 1),        # Neapolitan Eb sung directly
    (12, 0, 65, 2), (12, 2, 62, 1),
    (13, 0, 64, 2), (13, 2, 61, 1),        # C# leading tone
    (14, 0, 62, 3),
    (15, 0, 69, 2),
]

# B: horn takes it, up an octave-ish and broader — the one bright turn.
MELODY_B = [
    (16, 0, 65, 2), (16, 2, 69, 1),
    (17, 0, 72, 2), (17, 2, 76, 1),
    (18, 0, 74, 2), (18, 2, 72, 1),
    (19, 0, 70, 3),
    (20, 0, 69, 1.5), (20, 1.5, 72, .5), (20, 2, 74, 1),
    (21, 0, 76, 2), (21, 2, 72, 1),
    (22, 0, 70, 2), (22, 2, 67, 1),
    (23, 0, 69, 2.5),
]

MELODY_A3 = [
    (24, 0, 57, 2), (24, 2, 62, 1),
    (25, 0, 65, 1.5), (25, 1.5, 64, .5), (25, 2, 62, 1),
    (26, 0, 58, 2), (26, 2, 60, 1),
    (27, 0, 63, 2.5),                       # Neapolitan again, unresolved
    (28, 0, 65, 2), (28, 2, 62, 1),
    (29, 0, 64, 2), (29, 2, 61, 1),
    (30, 0, 62, 3),
    (31, 0, 69, 1),                         # bare A — hands back to the loop
]

def at(bar, beat=0.0):
    return bar * BAR + beat * TPQ

def place_melody(track, ch, notes, vel, gap=0.06):
    """Slightly detached notes — a hair of silence keeps the line from
    smearing on sustained soundfont patches."""
    for bar, beat, pitch, beats in notes:
        dur = max(TPQ // 8, int(beats * TPQ - gap * TPQ))
        track.note(ch, at(bar, beat), dur, pitch, vel)

# ---------------------------------------------------------------- build

def build():
    conductor = Track()
    conductor.meta(0, b"\xff\x58\x04" + bytes([BEATS_PER_BAR, 2, 24, 8]))   # 3/4
    conductor.meta(0, b"\xff\x51\x03" + int(60_000_000 / BPM).to_bytes(3, "big"))
    conductor.meta(0, b"\xff\x03" + bytes([len(b"Gaslamp Waltz")]) + b"Gaslamp Waltz")

    clarinet, horn, harp = Track(), Track(), Track()
    harpsi, bells, strings, bass = Track(), Track(), Track(), Track()

    clarinet.program(0, 71)   # Clarinet
    horn.program(1, 60)       # French Horn
    harp.program(2, 46)       # Orchestral Harp
    harpsi.program(3, 6)      # Harpsichord
    bells.program(4, 14)      # Tubular Bells
    strings.program(5, 48)    # String Ensemble 1
    bass.program(6, 43)       # Contrabass

    # Reverb/chorus sends — a wash helps sell "damp foggy street".
    for t, ch, rev in ((clarinet, 0, 72), (horn, 1, 84), (harp, 2, 80),
                       (harpsi, 3, 56), (bells, 4, 96), (strings, 5, 88),
                       (bass, 6, 48)):
        t.control(ch, 91, rev)

    # --- Harpsichord: the clock of the piece. Oom-pah-pah every bar,
    # softer under the B section so the strings can breathe.
    for bar, name in enumerate(PROG):
        c = CHORDS[name]
        loud = 66 if not (16 <= bar < 24) else 52
        harpsi.note(3, at(bar, 0), int(TPQ * .9), c["bass"] + 12, loud)
        for beat in (1, 2):
            for i, p in enumerate(c["tones"]):
                harpsi.note(3, at(bar, beat), int(TPQ * .55), p, loud - 14 - i * 3)

    # --- Contrabass: one long root per bar, the fog's floor.
    for bar, name in enumerate(PROG):
        bass.note(6, at(bar, 0), int(BAR * .92), CHORDS[name]["bass"], 58)

    # --- Harp: silent through the opening A (space is the point), then
    # rolling triplet arpeggios that flutter under everything after.
    for bar, name in enumerate(PROG):
        if bar < 8:
            continue
        tones = CHORDS[name]["tones"]
        shape = [0, 1, 2, 1] if bar % 2 == 0 else [2, 1, 0, 1]
        for beat in range(BEATS_PER_BAR):
            for k in range(2):
                idx = shape[(beat * 2 + k) % len(shape)]
                pitch = tones[idx] + (12 if (beat + k) % 2 else 0)
                harp.note(2, at(bar, beat + k * .5), int(TPQ * .45), pitch,
                          46 + (6 if k == 0 else 0))

    # --- Bells: one chime per 8-bar phrase, plus the Neapolitan bars.
    # Sparse on purpose — a distant clocktower, not a glockenspiel part.
    for bar in (0, 8, 16, 24):
        bells.note(4, at(bar, 0), BAR, CHORDS[PROG[bar]]["tones"][0] + 12, 62)
    for bar in (11, 27):
        bells.note(4, at(bar, 0), BAR, 63 + 12, 50)      # Eb — the odd colour

    # --- Strings: absent until B, then sustained swells; a thin tail
    # carries into A'' and fades out so the loop seam is clean.
    for bar in range(16, 28):
        c = CHORDS[PROG[bar]]
        vel = 74 if bar < 24 else max(40, 74 - (bar - 23) * 8)
        for i, p in enumerate(c["tones"]):
            strings.note(5, at(bar, 0), int(BAR * .97), p - 12 + (12 if i == 2 else 0),
                         vel - i * 4)

    # --- Melody hand-off: clarinet owns the fog, horn owns the brightening.
    place_melody(clarinet, 0, MELODY_A, 74)
    place_melody(clarinet, 0, MELODY_A2, 78)
    place_melody(horn, 1, MELODY_B, 86)
    # Clarinet shadows the horn an octave down — thickens the one loud moment.
    place_melody(clarinet, 0, [(b, t, p - 12, d) for b, t, p, d in MELODY_B], 54)
    place_melody(clarinet, 0, MELODY_A3, 70)

    return [conductor, clarinet, horn, harp, harpsi, bells, strings, bass]

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, NAME)
    write_midi(path, build())
    seconds = BARS * BEATS_PER_BAR * 60.0 / BPM
    print("wrote %s  (%d bars, %.1f s loop, %d BPM, 3/4)" %
          (path, BARS, seconds, BPM))
