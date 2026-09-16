# Launch prompt — what Start-Factory.ps1 sends, and what to type if you launch by hand

<!-- Rewritten 2026-09-16 for the shift model on the desktop (README.md
§ Decisions). The kernel's boot topology (tmux on a headless host, systemd)
does not apply; the terminal is the Windows Terminal tab the launcher opens. -->

## The one rule about this prompt

A /loop prompt is NOT a one-time boot message. The harness re-fires it
verbatim on every wakeup, for the life of the shift. So:

- It must be idempotent: an iteration entry point, nothing else.
- Boot-specific instructions live in LOOP-STATE.md § HANDOFF, which the
  first wake consumes and deletes. Relaunch instructions ("pick up these
  in-flight branches") go there too.
- It must not paraphrase the charter. A prompt restating a subset of
  directives is a second, competing constitution.

## The prompt

/loop You are the Robogame Factory. docs/loop/CHARTER.md is your constitution — read it, then the state files it names, then act under it. The context window is scratch; the state files are your only memory, so write state before you stop and schedule your own next wakeup, and end the shift the way the charter says.

## How a shift starts

Double-click (or run) `.claude/scripts/factory/Start-Factory.ps1` from the
factory's clone. It runs the preflight the charter names in D13, opens a
Windows Terminal tab in the clone, starts `claude` with the model and
effort from LOOP-STATE § SHIFT and the permission mode you chose, and
sends the prompt above as the first message. If the first wake reports
that `/loop` did not arm (HANDOFF step 8), paste the prompt into the tab
by hand.

From Claude Desktop instead (the scribe/runner format): run
`Start-Factory.ps1 -Desktop` in the clone. It does the same preflight, writes
the lock and the tick and starts the Editor, then prints the prompt above
instead of opening a tab. Open a Desktop session on the clone folder itself
(never a worktree), pick model and effort in the UI, bypass permissions, and
paste the prompt as the first message. The lock's pid stays null for a
Desktop shift; the shift ends the same ways as below.

By hand, the same thing is:

    cd <factory clone>
    claude --model opus --effort high --permission-mode bypassPermissions
    /loop You are the Robogame Factory. docs/loop/CHARTER.md is your constitution — ...

## How a shift ends

Any of: `Stop-Factory.ps1` (appends a `STOP` line to INBOX.md and pushes;
the loop sees it at its next checkpoint, writes state, pushes, stops);
the `-MaxHours` you gave the launcher; or the loop's own judgement that
the board is full and nothing provable is left (charter D14). The
end-of-shift routine leaves LOOP-STATE § NEXT ITEM set and the shift
report in #blue-mao-pow.

## Notes for Grey

- Keep the tab open. Closing it kills the shift without its end-of-shift
  routine; the next preflight will find the uncommitted work and the
  loop absorbs it (charter D13), so nothing is lost, but the report is.
- THE SILENT STALL. When the plan's cap on a model tier is hit, the
  harness puts a prompt in that tab ("limit reached · continuing on X
  uses usage credits …"). Nobody answers → the turn is discarded, the
  process stays alive, every wakeup re-fires into the same wall. Decide
  the overage policy once (NEEDS-GREY D-001); if a shift goes quiet,
  look at the tab first. The Cosmonaut shares the caps.
- Answer the board one entry at a time, in your own words; the loop
  quotes you. If you go quiet the caps bind and the loop shifts to
  provable work or ends the shift; that is by design.
- Any interactive Claude session in this repo can relay to the loop
  with `/inbox <text>`; it never does the loop's work during a shift.
