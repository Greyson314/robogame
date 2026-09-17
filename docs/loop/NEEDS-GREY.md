# NEEDS-GREY — the board (D12). The one file Grey opens.

How to answer, any of: edit the entry here; a line in INBOX.md (`approve CHG-012` · `reject CHG-013: reason` · `play PT-004: worse, the hook feels floaty` · `buy B-002: yes` · `decide D-001: no overage`); `/inbox <text>` from any Claude session in this repo; a message in Discord #blue-mao-pow (lands in INBOX within five minutes).
Caps: APPROVE 4 open, PLAY 4 open (D12 backpressure). Verdicts are copied VERBATIM to § ANSWERED, then to the change's docs/changes entry.

## APPROVE (0/4) — ASK-class specs awaiting check-off. One line each: id · title · pillar or readiness item · payoff · cost · evidence pointer.

(empty — CHG-005, deleting the two unused packs, carries Grey's D-004 nod and needs no APPROVE entry; CHG-004, the bomb-bay door cue, is the first APPROVE candidate once specced)

## PLAY (1/4) — one question per build, ≤ 5 minutes.

    ### PT-NNN — CHG-NNN title — queued DATE
    Build: commit / branch / exactly how to launch
    Question: one specific question
    Look for: what would make it a yes or a no
    Time: ≤ 5 min

### PT-001 — CHG-011 rig audio mute — which Unity instance is playing the music? — queued 2026-09-17
Build: nothing to install. The parked branch `chg/011-rig-audio-mute` (abf6de35) mutes a batch or factory Editor, but the loop could not show that the batch runs are the source: a batch Editor already reports its master mute as on, and FMOD's bus mirrors it at init (RuntimeManager.cs:532, :1513).
Question: next time you hear the game's music with no game running, which Unity is open? (a) your own Editor on `robogame` (garage scene idles with music), (b) nothing but the factory's rig runs, or (c) the factory's Editor window. If (b), note the UTC minute: `.utmp/factory/loop-tick.txt` and the rig logs carry every run's time.
Look for: the Windows volume mixer names the process; the factory's rig process is `Unity.exe` with `-projectPath ...\robogame-factory\.claude\worktrees\test-rig`.
Time: ≤ 5 min. Answer: `play PT-001: <a|b|c>, <what you saw>`; (b) lands CHG-011 with an FMOD-level mute added, (a)/(c) drops it.

## BUY (0) — purchase proposals (I2 format: exact item priced · what it unlocks · pillar or readiness item · proxy already measured · kill date).

(empty)

## DECIDE — decisions only Grey can make, each with the evidence and a default.


- **D-005 — the factory's Editor: it opened the clone on 2026-09-17T15:50Z (the fourth launch, after the Library database wipe) and was importing (10,104 assets) at shift 4's end; MCP on port 8080 comes up when the import finishes.** Narrowed ask: next time you are at the desktop, confirm the Unity window is there and shows the project (not a dialog), and if so `/inbox decide D-005: editor up`. Then a shift can be started the right way round: `Start-Factory.ps1 -Desktop` first, wait for `netstat -ano | findstr :8080` to show a listener, THEN open the Desktop session and type `/robogame-factory`, so the session dials a live bridge (LESSONS § METHOD 2). The two earlier startup hangs stay unexplained; if it hangs again the loop wipes Library/ArtifactDB + SourceAssetDB and relaunches once before asking. Default: batch-only shifts until then.

## FYI — AUTO-class landings since Grey last looked (cleared when acknowledged; each also in docs/changes).

- FYI-1 2026-09-16 — the orphan `.vscode/settings.json` (the dotnet extension rewriting the solution name for the clone's folder) was reverted and marked `skip-worktree` in the clone; not work, no commit (LESSONS § METHOD 3).
- FYI-2 2026-09-16 — `/robogame-factory` gained step 1b (plan-cap check, recorded in the tick; it refused to arm at 100 % until D-001 was answered on 2026-09-17) and step 1c (UnityMCP reconnect: the bridge was down for all of shift 2 because the preflight starts the Editor after the session has dialled it, F-017); `docs/loop/DESKTOP-HANDOFF.md` deleted, its setup done. Commit "factory: shift 2 …" on main; no docs/changes entry (factory tooling, no product code).
- FYI-3 2026-09-16 — the rig is proven: `run-tests.sh All` 1m28s warm, EditMode 529/530 (1 inconclusive: a stale test path, F-016), PlayMode 152/153 (1 documented skip), 0 failures. Four sweeps ran (provenance, invariants, best-practices, doc-drift): 20 FINDINGS lines, three AUTO changes specced (CHG-001..003), nothing landed yet.
- 2026-09-17 CHG-003 PresetBlueprintTests: guard the preset list, drop the retired Buggy path — docs/changes/174-preset-list-guard.md — FYI-4 2026-09-17 — CHG-003 landed: the suite's only unjustified Inconclusive (a retired Buggy preset path) is gone, with a guard test that fails loudly on the next stale path; EditMode 530/530. docs/changes/174.
- 2026-09-17 CHG-002 Doc drift from the 2026-09-16 sweep: six docs say what the code does — docs/changes/175-doc-drift-sweep.md — FYI-5 2026-09-17 — CHG-002 landed: six docs now say what the code does (tip-blocks damper, scalable-parts status, architecture GravityField, README changelog, best-practices § 12.5, one trace); docs/changes/175. Readiness D1 and D2 done.
- 2026-09-17 CHG-005 Delete the two unused, unlicensed packs (FattyPolyTurret, TrueShadow) on Grey's nod — docs/changes/176-delete-unused-packs.md — FYI-6 2026-09-17 — CHG-005 landed: the two unused packs you said to delete are gone (458 files; nothing referenced them, verified twice); docs/changes/176.
- 2026-09-17 CHG-001 Provenance records: artgen manifest with guards, MCP package origin, three Asset Store rows — docs/changes/177-provenance-records.md — FYI-7 2026-09-17 — CHG-001 landed: every generated model now has a machine-checked provenance row, the MCP package its origin, the three Asset Store packs the rows you named; docs/changes/177. Readiness L1 and L2 done.
- 2026-09-17 CHG-008 Preset test coverage: every scaffolder slot validated, one list, HoverTank quarantined loudly — docs/changes/178-preset-coverage.md — FYI-8 2026-09-17 — CHG-008 landed: the two presets no test validated are covered now, and the coverage found that the Hover Tank preset is not player-buildable-shaped (it drives fine); fix queued as CHG-013; docs/changes/178.
- 2026-09-17 CHG-010 Strip the dead scripting defines left by deleted packs, with a guard — docs/changes/179-dead-defines.md — FYI-9 2026-09-17 — CHG-010 landed: two dead scripting defines left behind by deleted packs are gone, with a guard that flags the next one; docs/changes/179.

## ANSWERED (most recent first; verbatim)

- **D-004**, 2026-09-17T01:52:02Z via /inbox: "decide D-004: Stylized Nature Pack, Polytope Studio trees, Handpainted Grass and Ground Textures: Unity Asset Store (EULA). FattyPolyTurretFree + Part2Free and Le Tai's TrueShadow: Delete both." (scribe: chosen by Grey from options built from the D-004 entry; "Delete both" is Grey's nod under I1 for the two unused packs.) Loop's reading: the three rows are written under CHG-001 (source Unity Asset Store, license the Asset Store EULA); the two deletions are CHG-005 with this line as the I1 nod. D-004 closed.
- **D-003**, 2026-09-17T01:52:02Z via /inbox: "decide D-003: yes" (scribe: Grey supplied the webhook URL to the scribe; written by the scribe to <clone>/.env as DISCORD_WEBHOOK_FACTORY (gitignored; URL redacted here). Chosen from options built from the D-003 entry.) Loop's reading: `.env` verified by name and length (DISCORD_WEBHOOK_FACTORY, 146 bytes); pings are live from shift 3's report on. D-003 closed.
- **D-002**, 2026-09-17T01:52:02Z via /inbox: "decide D-002: Singleplayer-only v1" (scribe: chosen by Grey from options the scribe offered in the Desktop UI, built from the D-002 entry.) Loop's reading: LAUNCH-READINESS S1 done; the multiplayer rows M1–M4 are n/a for v1 and stay listed for a later version. D-002 closed.
- **D-001**, 2026-09-17T01:42:22Z via /inbox: "decide D-001: it's caught on thinking it can't use usage credits. it can." (Loop's reading, not Grey's words: the factory may run on usage credits; the "no overage" default is withdrawn; the plan-usage check stays as a report line in the tick and the ledger; no daily dollar ceiling was stated, so the D16d per-shift ceiling of 4M tokens is the only bound until Grey names a figure. D-001 closed.)
- **D-001**, 2026-09-16T20:48:15Z via /inbox: "decide D-001: overage should not be shared." (Loop's reading, not Grey's words: the factory does not draw on an overage allowance shared with the Cosmonaut. It does not settle the factory's own overage policy, so D-001 stays open with the narrowed question above and the default stands.)
