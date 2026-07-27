# 148 — GM soundfont live-loading + Bach garage theme

**Date.** 2026-07-27
**Intent.** Close the long-standing "no soundfont imported" gap from
145 and connect the project to public-domain MIDI. User approved both
downloads.

## Assets added

- **`StreamingAssets/SoundFont/GeneralUser-GS.sf2`** — 30.8 MB,
  [mrbumpy409/GeneralUser-GS](https://github.com/mrbumpy409/GeneralUser-GS)
  (S. Christian Collins' bank). GeneralUser GS License v2.0: free for
  commercial software including games, no royalty. 141 presets / 920
  waves load in practice.
- **`StreamingAssets/Midi/bach-invention-08.mid`** — 5 KB, Mutopia,
  **Public Domain** (BWV 779, F major). Two-Part Invention: the name
  and the clockwork counterpoint both fit the inventor's workshop.
  Deliberately not Invention No. 1, which is CC-BY-SA.
- `.gitattributes`: `*.sf2 *.sf3 *.mid *.midi` now LFS-tracked.

## What landed

- **`MusicSoundFont`** (Core) — binds the bank to a synth via
  `synth.MPTK_SoundFont.Load("file://…")`. **Per-synth, not global:**
  `MPTK_LoadLiveSF` binds only to synths existing at call time, and
  both our players are created lazily, so the global route left them
  bankless and silently voiceless.
- **`GarageMusic`** (Core) — loops the theme in the garage, hands MPTK
  raw bytes (no MidiDB import, so swapping the theme is a file drop),
  volume on the Tweakables chain, stops on scene unload. Bound from
  `GarageController.Start`. Garage had no music at all before this.
- **`MusicMidi` fixes** — now instantiates MPTK's prefab instead of
  AddComponent-ing a synth onto a bare GameObject, and gates
  `IsAvailable` on its own synth's bank rather than the global flag.
- **SMG hybrid reconciled on the MPTK path** (147 carry-forward): note
  tier = GM side-stick on the drum channel, phrase lands its top note
  on a low tom. Without this, importing a soundfont would have
  silently reverted the hybrid the user approved.
- Prefab copies at `Resources/Music/Mptk{Stream,File}Player.prefab`.

## The bug that ate the session

A synth built by hand (`new GameObject` + `AddComponent<MidiStreamPlayer>`
+ an AudioSource) reports `MPTK_SoundFontLoaded`, `IsReady`, and even
allocates voices for `MPTK_PlayDirectEvent` — but produces **no audio**
and drops queued `MPTK_PlayEvent` entirely (`StatVoicePlayed` stuck at
0). MPTK's prefab carries a specific AudioSource + child
`VoiceAudioSource` template arrangement that the synth needs to reach
the output; replicating it by hand is not worth it. **Instantiate the
prefab.** This was latent in the 145 `MusicMidi` code — it had never
run with a bank, so it had never been observably broken.

## Verification

- Live (bridge, play mode): bank loads (141 presets / 920 waves,
  status Success); stinger volley via the real code path →
  `played=12 active=4`; garage theme → `played=201` and climbing with
  7 voices sustained. Compile clean, no exceptions.
- Headless: EditMode 453/454, PlayMode 120/121, 0 failed (the 1
  inconclusive + 1 ignore are the documented carry-overs).
- Play-mode cycles re-scattered procedural decor in four scene files
  (backdrop rocks / stones get fresh random transforms on load);
  reverted — no authored values were touched.

## Theme candidates + dev audition (follow-up in-session)

User asked what MIDIs were available locally: only the one. Added six
more Public Domain pieces from Mutopia into the same folder, with
`StreamingAssets/Midi/README.md` holding the roster, licences and a
one-line feel description each:

- Bach Inventions No. 4 (D minor), No. 10 (G), No. 13 (A minor)
- Bach Prelude BWV 999 (D minor, lute) — steady arpeggiated mechanism
- Joplin, The Entertainer + Magnetic Rag — ragtime contrast

Two substitutions from the proposed list: Mutopia has no Maple Leaf
Rag (used Magnetic Rag) and no BWV 846 prelude (used BWV 999). All
seven files are Public Domain — deliberately avoiding Mutopia's
CC-BY-SA editions, which would put share-alike terms on a shipped
asset.

`GarageMusicDevCycle` (Gameplay, compile-stripped from release):
**F7** cycles the folder, **Shift+F7** steps back. It lives in
Gameplay because Core doesn't reference the Input System and this
wasn't worth widening Core's dependencies for; `GarageMusic` exposes
`AvailableTracks` / `CurrentTrack` / `SwitchTo` for it. F7 was free —
NetDevHud owns F5 and F8–F11. Auditioning in an external player is
misleading: Windows renders MIDI on the Microsoft GS wavetable, not
GeneralUser GS.

Verified live: enumeration returns all 7, `SwitchTo("entertainer")`
restarted playback with 16 voices active.

## Notes / follow-ups

- **Timbres still unjudged by ear** — this session verified voices
  fire, not that they sound good. GM patches (45 pizz / 61 brass /
  0 piano / 47 timpani) are a first mapping.
- MPTK logs "No global SoundFont ready found" once at startup — its
  legacy MidiDB path, harmless under per-synth loading. Silenceable by
  disabling `MPTK_LoadSoundFontAtStartup` if it becomes noise.
- MPTK synth CPU cost still unprofiled with a live bank (INV-7) —
  do it before shipping stingers on this path in a busy fight.
- Offline MIDI mining (parse with `mido`, quantise onto the combat
  grid, filter to D pentatonic) remains the route for siphoning real
  arrangements into the generator; not started.
