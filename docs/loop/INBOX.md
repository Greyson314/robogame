# INBOX — Grey drops here (or #blue-mao-pow, or `/inbox`). Triaged on merit, never auto-prioritized (D12).

Read by the loop at every checkpoint as a `git fetch` + log of INBOX commits. Lines are appended, never rewritten; the loop marks a line handled by appending ` → <what it did>` in its own commit.

Line forms (any prose works; these are the shorthands the loop recognizes):
- `approve CHG-012` · `reject CHG-013: reason` · `play PT-004: <verdict in your words>` · `buy B-002: yes|no` · `decide D-001: <your answer>`
- `STOP` (optionally `STOP: <note>`) — end the shift: write state, push, stop the loop.
- Anything else is an idea, a report or a steer; the loop triages it.

Discord lines land as `- [discord #blue-mao-pow <UTC>] <user>: <text>`. `/inbox` lines land as `- [<UTC> via /inbox] <text>`.

(empty)
- [2026-09-16T20:48:15Z via /inbox] decide D-001: overage should not be shared.
  (scribe: relayed by hand from the desktop-handoff session, where /inbox was not loaded.) → recorded verbatim in NEEDS-GREY § ANSWERED; D-001 kept open with a narrowed question (the line rules out a shared allowance, not overage itself).
- [2026-09-16T21:44:14Z via Stop-Factory] STOP: Grey, from the loop session
- [2026-09-17T01:42:22Z via /inbox] decide D-001: it's caught on thinking it can't use usage credits. it can.
  (scribe: Grey's words on seeing the runner stall on overage. No daily ceiling was stated; the ceiling half of the narrowed D-001 question is still unanswered.) → recorded verbatim in NEEDS-GREY § ANSWERED; D-001 closed (usage credits allowed; no ceiling named, the D16d per-shift ceiling binds); the "end on a 100 % window" rule withdrawn from LOOP-STATE, LESSONS and /robogame-factory 1b; shift 3 re-armed.
- [2026-09-17T01:52:02Z via /inbox] decide D-002: Singleplayer-only v1
  (scribe: chosen by Grey from options the scribe offered in the Desktop UI, built from the D-002 entry.) → recorded verbatim in NEEDS-GREY § ANSWERED; D-002 closed; LAUNCH-READINESS S1 done, M1–M4 n/a for v1.
- [2026-09-17T01:52:02Z via /inbox] decide D-003: yes
  (scribe: Grey supplied the webhook URL to the scribe; written by the scribe to <clone>/.env as DISCORD_WEBHOOK_FACTORY (gitignored; URL redacted here). Chosen from options built from the D-003 entry.) → recorded verbatim; .env verified by name and length; D-003 closed; pings live from shift 3's report.
- [2026-09-17T01:52:02Z via /inbox] decide D-004: Stylized Nature Pack, Polytope Studio trees, Handpainted Grass and Ground Textures: Unity Asset Store (EULA). FattyPolyTurretFree + Part2Free and Le Tai's TrueShadow: Delete both.
  (scribe: chosen by Grey from options built from the D-004 entry; "Delete both" is Grey's nod under I1 for the two unused packs.) → recorded verbatim; D-004 closed; rows → CHG-001, deletions → CHG-005 (nod recorded on the spec).
- [2026-09-17T02:33:13Z via /inbox] if there's a way to mute it by default so i dont have the headless music from the unity instance playing, that'd be awesome.
  (scribe: Grey means the factory Editor on this clone. One option: an editor-only script that sets EditorUtility.audioMasterMute = true on load, gated to the factory clone so Grey's own checkout is unaffected. Loop's call; a steer, not a spec.)
