# Garage theme candidates

Public-domain MIDIs from [the Mutopia Project](https://www.mutopiaproject.org/),
rendered at runtime through `StreamingAssets/SoundFont/GeneralUser-GS.sf2`
by `GarageMusic` (see [music.md](../../../docs/subsystems/music.md)).

**Every file here is Public Domain** — no attribution or share-alike
obligation. That was deliberate: Mutopia also hosts CC-BY-SA editions
(Bach's famous Invention No. 1 among them) which carry share-alike
terms we don't want on a shipped asset. If you add files, check the
licence field on the Mutopia listing first and keep this table current.

The two `garage-gaslamp-*` files are **ours** — composed for this
project by [`artgen/gen_garage_theme.py`](../../../artgen/gen_garage_theme.py),
not downloaded. They are two cuts of the same piece (same harmony,
same tunes, different production); the script emits both. Edit the
harmony/melody tables there and re-run, never the `.mid`.

| File | Piece | Feel |
| --- | --- | --- |
| `garage-gaslamp-glitch.mid` | **Gaslamp Waltz (Glitch)** (D minor, 3/4, 104 BPM) | **Current theme.** Blown-speaker inventor cut: blown-out tuba + timpani blasts, synth bass, electro kit with buffer stutters, ornamented melody |
| `garage-gaslamp-waltz.mid` | **Gaslamp Waltz** (D minor, 3/4, 88 BPM) | The clean parlour cut: clarinet over harpsichord, one bright turn, back to mist |
| `bach-invention-08.mid` | Two-Part Invention No. 8 in F, BWV 779 | Bright, bouncy, perpetual motion |
| `bach-invention-04.mid` | Two-Part Invention No. 4 in D minor, BWV 775 | Driving and restless; D root matches the project scale |
| `bach-invention-10.mid` | Two-Part Invention No. 10 in G, BWV 781 | Quick and light, the most cheerful of the set |
| `bach-invention-13.mid` | Two-Part Invention No. 13 in A minor, BWV 784 | Flowing arpeggios, calmest — least busy under UI sound |
| `Bach_Prelude_BWV999.mid` | Prelude in D minor, BWV 999 (lute) | Steady arpeggiated mechanism; also D-rooted |
| `entertainer.mid` | Joplin, The Entertainer (c. 1902) | Jaunty clockwork ragtime — a different flavour entirely |
| `magnetic.mid` | Joplin, Magnetic Rag (c. 1914) | Ragtime again, moodier and more wandering |

## Switching the theme

Change `GarageMusic.StreamingRelativePath`, or in play mode press the
dev cycle key (see `GarageMusic` — editor/dev builds only) to step
through this folder and hear each one in the garage through the real
soundfont. Auditioning in an external player is misleading: Windows
renders MIDI with the Microsoft GS wavetable, which sounds nothing
like GeneralUser GS.
