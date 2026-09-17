# NEEDS-GREY — the board (D12). The one file Grey opens.

How to answer, any of: edit the entry here; a line in INBOX.md (`approve CHG-012` · `reject CHG-013: reason` · `play PT-004: worse, the hook feels floaty` · `buy B-002: yes` · `decide D-001: no overage`); `/inbox <text>` from any Claude session in this repo; a message in Discord #blue-mao-pow (lands in INBOX within five minutes).
Caps: APPROVE 4 open, PLAY 4 open (D12 backpressure). Verdicts are copied VERBATIM to § ANSWERED, then to the change's docs/changes entry.

## APPROVE (0/4) — ASK-class specs awaiting check-off. One line each: id · title · pillar or readiness item · payoff · cost · evidence pointer.

(empty — CHG-005, deleting the two unused packs, carries Grey's D-004 nod and needs no APPROVE entry; CHG-004, the bomb-bay door cue, is the first APPROVE candidate once specced)

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


- **D-005 — the factory's Editor will not open the clone; please look at the desktop screen once.** Three GUI launches of `Unity.exe -projectPath <clone>` on 2026-09-16/17: one crashed initializing the asset database (a corrupt Library/ArtifactDB, since wiped), two hung right after licensing with no window and a frozen log, before and after the wipe; the batch Editor opens the test-rig worktree fine and the suite is green. A startup dialog nobody can see is the leading guess (the loop has no screen access). Ask: next time you are at the desktop, run `powershell -File .claude\scripts\factory\Start-Factory.ps1 -Desktop -DryRun`, then start the Editor on the factory clone by hand (Unity Hub → Open → the clone folder) and tell the loop what it shows (`/inbox decide D-005: <what you saw>`). Until then every shift is batch-only: tests and doc/test/tooling changes land; console, visual and perf-in-Editor sweeps do not run. Default: batch-only.

## FYI — AUTO-class landings since Grey last looked (cleared when acknowledged; each also in docs/changes).

- FYI-1 2026-09-16 — the orphan `.vscode/settings.json` (the dotnet extension rewriting the solution name for the clone's folder) was reverted and marked `skip-worktree` in the clone; not work, no commit (LESSONS § METHOD 3).
- FYI-2 2026-09-16 — `/robogame-factory` gained step 1b (plan-cap check, recorded in the tick; it refused to arm at 100 % until D-001 was answered on 2026-09-17) and step 1c (UnityMCP reconnect: the bridge was down for all of shift 2 because the preflight starts the Editor after the session has dialled it, F-017); `docs/loop/DESKTOP-HANDOFF.md` deleted, its setup done. Commit "factory: shift 2 …" on main; no docs/changes entry (factory tooling, no product code).
- FYI-3 2026-09-16 — the rig is proven: `run-tests.sh All` 1m28s warm, EditMode 529/530 (1 inconclusive: a stale test path, F-016), PlayMode 152/153 (1 documented skip), 0 failures. Four sweeps ran (provenance, invariants, best-practices, doc-drift): 20 FINDINGS lines, three AUTO changes specced (CHG-001..003), nothing landed yet.
- 2026-09-17 CHG-003 PresetBlueprintTests: guard the preset list, drop the retired Buggy path — docs/changes/174-preset-list-guard.md — FYI-4 2026-09-17 — CHG-003 landed: the suite's only unjustified Inconclusive (a retired Buggy preset path) is gone, with a guard test that fails loudly on the next stale path; EditMode 530/530. docs/changes/174.

## ANSWERED (most recent first; verbatim)

- **D-004**, 2026-09-17T01:52:02Z via /inbox: "decide D-004: Stylized Nature Pack, Polytope Studio trees, Handpainted Grass and Ground Textures: Unity Asset Store (EULA). FattyPolyTurretFree + Part2Free and Le Tai's TrueShadow: Delete both." (scribe: chosen by Grey from options built from the D-004 entry; "Delete both" is Grey's nod under I1 for the two unused packs.) Loop's reading: the three rows are written under CHG-001 (source Unity Asset Store, license the Asset Store EULA); the two deletions are CHG-005 with this line as the I1 nod. D-004 closed.
- **D-003**, 2026-09-17T01:52:02Z via /inbox: "decide D-003: yes" (scribe: Grey supplied the webhook URL to the scribe; written by the scribe to <clone>/.env as DISCORD_WEBHOOK_FACTORY (gitignored; URL redacted here). Chosen from options built from the D-003 entry.) Loop's reading: `.env` verified by name and length (DISCORD_WEBHOOK_FACTORY, 146 bytes); pings are live from shift 3's report on. D-003 closed.
- **D-002**, 2026-09-17T01:52:02Z via /inbox: "decide D-002: Singleplayer-only v1" (scribe: chosen by Grey from options the scribe offered in the Desktop UI, built from the D-002 entry.) Loop's reading: LAUNCH-READINESS S1 done; the multiplayer rows M1–M4 are n/a for v1 and stay listed for a later version. D-002 closed.
- **D-001**, 2026-09-17T01:42:22Z via /inbox: "decide D-001: it's caught on thinking it can't use usage credits. it can." (Loop's reading, not Grey's words: the factory may run on usage credits; the "no overage" default is withdrawn; the plan-usage check stays as a report line in the tick and the ledger; no daily dollar ceiling was stated, so the D16d per-shift ceiling of 4M tokens is the only bound until Grey names a figure. D-001 closed.)
- **D-001**, 2026-09-16T20:48:15Z via /inbox: "decide D-001: overage should not be shared." (Loop's reading, not Grey's words: the factory does not draw on an overage allowance shared with the Cosmonaut. It does not settle the factory's own overage policy, so D-001 stays open with the narrowed question above and the default stands.)
