# ASSUMPTIONS — the register (D10)

Every load-bearing assumption about the engine, the platform, the rig, the player and the pipeline: unverified / verified / falsified. Unverified assumptions about BEHAVIOR block landing. Unverified assumptions about TASTE block nothing until the playtest; they are the playtest question. Reviewed at every landing and at the start of every shift.

| # | Assumption | Kind | Status | Evidence / plan to verify |
|---|---|---|---|---|
| 1 | PhysX diverges run-to-run for a fixed seed even on one machine, so a recorded-input replay cannot be an exact oracle | behavior | unverified | replay the same 60 s scripted input 5× on this rig; measure position divergence over time; decide the tolerance a replay test may use (BACKLOG 7) |
| 2 | Batch `-nographics` perf rows measure CPU, physics and GC only; render numbers need a graphics session | behavior | unverified | compare a harness row from `run-tests.sh` with one from the live Editor on the same scene |
| 3 | Two Unity Editors (Grey's and the factory's) fit in the desktop's RAM and both register on one MCP server with instance routing | behavior | unverified | HANDOFF (4): open both, `set_active_instance`, `read_console` on each |
| 4 | The Editor's MCP auto-start does not fight a remote server URL, and the plaintext opt-in works over Tailscale | behavior | unverified | the C spike (BACKLOG 8); PARKED |
| 5 | MCP for Unity's Python server at v9.7.3 can be hosted off the desktop with `--http-host` and the desktop Editor connects outbound | behavior | unverified | the C spike; the package's remote-server guide says so for the current version |
| 6 | `/loop` arms from the launcher's initial prompt argument | behavior | unverified | HANDOFF (8) |
| 7 | Grey's playtest is the only feel instrument; no proxy substitutes | taste | verified by charter | END GOAL |
