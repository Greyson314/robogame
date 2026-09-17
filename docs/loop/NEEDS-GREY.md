# NEEDS-GREY — the board (D12). The one file Grey opens.

How to answer, any of: edit the entry here; a line in INBOX.md (`approve CHG-012` · `reject CHG-013: reason` · `play PT-004: worse, the hook feels floaty` · `buy B-002: yes` · `decide D-001: no overage`); `/inbox <text>` from any Claude session in this repo; a message in Discord #blue-mao-pow (lands in INBOX within five minutes).
Caps: APPROVE 4 open, PLAY 4 open (D12 backpressure). Verdicts are copied VERBATIM to § ANSWERED, then to the change's docs/changes entry.

## APPROVE (0/4) — ASK-class specs awaiting check-off. One line each: id · title · pillar or readiness item · payoff · cost · evidence pointer.

(empty — CHG-004, the bomb-bay door cue, is the first candidate; its spec is written next shift)

## PLAY (0/4) — one question per build, ≤ 5 minutes.

    ### PT-NNN — CHG-NNN title — queued DATE
    Build: commit / branch / exactly how to launch
    Question: one specific question
    Look for: what would make it a yes or a no
    Time: ≤ 5 min

(empty)

## BUY (0) — purchase proposals (I2 format: exact item priced · what it unlocks · pillar or readiness item · proxy already measured · kill date).

(empty)

## DECIDE — decisions only Grey can make, each with the evidence and a default.

- **D-002 — is the v1 launch singleplayer-only?** README says multiplayer is planned (Phase 2 Relay/Lobby and Phase 5 Steam not started). LAUNCH-READINESS.md is seeded with MP items marked "if v1 includes MP". Default until answered: singleplayer-only v1; MP items stay listed, not worked.
- **D-003 — Discord channel.** `.env` is absent in the factory clone (README § Desktop setup, step 3), so `DISCORD_WEBHOOK_FACTORY` is unset and every ping dry-runs: shift 2's report was printed to the session and saved under `.utmp/factory/pings/`, where you never look (F-018). Options: (a) make a fresh webhook for #blue-mao-pow (channel → Integrations → Webhooks → New) and put the one line `DISCORD_WEBHOOK_FACTORY=<url>` in `<clone>/.env`, never in chat; or (b) `decide D-003: no Discord`, and the shift report lives as the last bullet of LOOP-STATE § SHIFT LOG plus this board. Default until answered: (b).
- **D-004 — provenance of five imported packs (I6).** None has a license on disk or a row in art-direction.md § Imported Assets (F-001–F-005): Stylized Nature Pack (wired: ArenaProps.cs:57), Polytope Studio trees (ArenaProps.cs:276), Handpainted Grass and Ground Textures (FluffGround.cs:275), FattyPolyTurretFree + Part2Free (unused by any script; the Free pack's readme is the Part2 one), Le Tai's TrueShadow (unused, paid). For each: where it came from and under which license (the Asset Store EULA counts; name it), or `delete` for the two unused ones (deleting assets needs your nod, I1). The loop then writes the rows (AUTO). No default: the rows cannot be invented, and LAUNCH-READINESS L1 stays `grey` until then.

## FYI — AUTO-class landings since Grey last looked (cleared when acknowledged; each also in docs/changes).

- FYI-1 2026-09-16 — the orphan `.vscode/settings.json` (the dotnet extension rewriting the solution name for the clone's folder) was reverted and marked `skip-worktree` in the clone; not work, no commit (LESSONS § METHOD 3).
- FYI-2 2026-09-16 — `/robogame-factory` gained step 1b (plan-cap check, recorded in the tick; it refused to arm at 100 % until D-001 was answered on 2026-09-17) and step 1c (UnityMCP reconnect: the bridge was down for all of shift 2 because the preflight starts the Editor after the session has dialled it, F-017); `docs/loop/DESKTOP-HANDOFF.md` deleted, its setup done. Commit "factory: shift 2 …" on main; no docs/changes entry (factory tooling, no product code).
- FYI-3 2026-09-16 — the rig is proven: `run-tests.sh All` 1m28s warm, EditMode 529/530 (1 inconclusive: a stale test path, F-016), PlayMode 152/153 (1 documented skip), 0 failures. Four sweeps ran (provenance, invariants, best-practices, doc-drift): 20 FINDINGS lines, three AUTO changes specced (CHG-001..003), nothing landed yet.

## ANSWERED (most recent first; verbatim)

- **D-001**, 2026-09-17T01:42:22Z via /inbox: "decide D-001: it's caught on thinking it can't use usage credits. it can." (Loop's reading, not Grey's words: the factory may run on usage credits; the "no overage" default is withdrawn; the plan-usage check stays as a report line in the tick and the ledger; no daily dollar ceiling was stated, so the D16d per-shift ceiling of 4M tokens is the only bound until Grey names a figure. D-001 closed.)
- **D-001**, 2026-09-16T20:48:15Z via /inbox: "decide D-001: overage should not be shared." (Loop's reading, not Grey's words: the factory does not draw on an overage allowance shared with the Cosmonaut. It does not settle the factory's own overage policy, so D-001 stays open with the narrowed question above and the default stands.)
