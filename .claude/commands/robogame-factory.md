---
description: Start a Robogame Factory shift from this session — run the -Desktop preflight (lock, tick, Editor unless "noeditor"), then arm /loop with the charter prompt. Run it in a session opened on the factory clone, never on Grey's checkout.
---

# /robogame-factory — this session becomes the runner

You are about to become the Robogame Factory loop (docs/loop/CHARTER.md). Do these steps in order; stop loudly at the first failure.

1. **Preflight.** From the repo root run, with `-NoEditor` appended only when `$ARGUMENTS` contains `noeditor`:

       powershell -NoProfile -ExecutionPolicy Bypass -File .claude\scripts\factory\Start-Factory.ps1 -Desktop

   If the output contains `PREFLIGHT FAIL`, or the exit code is not 0, print the failing line and stop. Do not arm the loop. (It refuses Grey's own checkout, a live shift lock, a merge in progress, and missing tools.) Warnings are fine; repeat them in one line.
2. **Arm the loop.** Invoke the `loop` skill with no interval and exactly this argument text (it is the prompt in docs/loop/LAUNCH-PROMPT.md; do not paraphrase it):

       You are the Robogame Factory. docs/loop/CHARTER.md is your constitution — read it, then the state files it names, then act under it. The context window is scratch; the state files are your only memory, so write state before you stop and schedule your own next wakeup, and end the shift the way the charter says.

3. From here on you are the loop: the charter governs, not this command. The shift ends by Stop-Factory.ps1, `/inbox STOP`, or charter D14.
