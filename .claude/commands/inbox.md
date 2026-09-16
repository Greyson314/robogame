---
description: Relay a message to the Robogame Factory's INBOX (docs/loop/INBOX.md) — append, commit, push. With no argument, show the last INBOX lines and the board's open entries.
---

# /inbox — relay to the factory

You are relaying Grey's words to the factory loop (docs/loop/CHARTER.md D12). You are the scribe here, not the loop: relay, never do the loop's work, never edit its other files.

## With an argument (`$ARGUMENTS` non-empty)

1. Take the clock from the shell in the same command as the write, never by hand:

       stamp=$(date -u +%FT%TZ); printf -- '- [%s via /inbox] %s\n' "$stamp" '$ARGUMENTS' >> docs/loop/INBOX.md

   (If `$ARGUMENTS` contains a single quote, write the line with a heredoc instead of the printf.) Quote Grey verbatim; add nothing in Grey's name. If you want to add a caution or context of your own, put it on a second line that starts with `  (scribe:` so the author is visible.
2. `git add -- docs/loop/INBOX.md && git commit -m "factory: INBOX +1 via /inbox" -- docs/loop/INBOX.md && git push origin HEAD`
3. If the push is rejected, `git pull --rebase --autostash origin <branch>` once and push again; if it still fails, say so and leave the commit local.
4. Reply with one line: what was relayed and whether it was pushed. The loop reads INBOX at its next checkpoint (minutes during a shift; the next shift otherwise). If Grey needs the loop woken now and a shift is running, tell Grey the tab is `robogame-factory` and a direct message tagged `[INBOX poke]` is the light wake.

Recognized shorthands (any prose also works): `approve CHG-012` · `reject CHG-013: reason` · `play PT-004: <verdict>` · `buy B-002: yes|no` · `decide D-001: <answer>` · `STOP` (ends the shift).

## Without an argument

Show, without editing anything: the last 15 lines of `docs/loop/INBOX.md`, then the open entries of `docs/loop/NEEDS-GREY.md` (the APPROVE, PLAY, BUY and DECIDE sections; skip FYI and ANSWERED). One screen, no commentary.
