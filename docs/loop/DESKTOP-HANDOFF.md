# DESKTOP-HANDOFF — for the Claude Code session on Grey's Windows desktop (one-time setup)

You are a Claude Code session on Grey's Windows desktop, in his main checkout
`C:\Users\Grey\Desktop\mutedtuple\robogame`. Grey is tired and has asked you to do
the factory's one-time setup for him. Do every step; stop only where a step says
"ask Grey". Report at the end in six lines or fewer. Read docs/loop/README.md if
you need context; do not read the whole charter for this.

1. **Push Grey's work.** In the main checkout: `git status`, `git log origin/main..HEAD --oneline`.
   If there are local commits not on origin, `git push origin main`. If there are
   uncommitted changes, tell Grey what they are and do NOT commit them; continue.
2. **Merge the factory branch.** The PR named in the terminal prompt that started you
   (or `gh pr list`) contains only docs, scripts and agent files, no C#. Merge it
   (`gh pr merge <n> --merge --delete-branch`), then `git pull origin main` here.
3. **Clone the factory copy** beside this checkout:
   `git clone https://github.com/Greyson314/robogame.git C:\Users\Grey\Desktop\mutedtuple\robogame-factory`.
   Do not open it in Unity yourself.
4. **Python.** Run `python --version`. If that is not Python 3 (the App Execution Alias
   shim prints nothing or opens the Store), run `winget install --id Python.Python.3.12 -e`
   and then re-check in a NEW shell. If winget needs an interactive accept, accept it.
5. **.env** in the factory clone root. It needs one line, `DISCORD_WEBHOOK_FACTORY=<url>`.
   Ask Grey for a fresh webhook URL for #blue-mao-pow (Discord: channel settings →
   Integrations → Webhooks → New; the one he pasted on 2026-09-16 was retired). If he is
   not around, skip this step: pings dry-run without it and nothing else breaks.
6. **Tests of the tools:** in the factory clone,
   `python -m unittest discover -s .claude\scripts\factory\tests -t .claude\scripts\factory\tests`
   must print OK.
7. **Preflight, dry:** in the factory clone, `powershell -NoProfile -ExecutionPolicy Bypass -File .claude\scripts\factory\Start-Factory.ps1 -DryRun`.
   Fix anything it FAILs on (a missing tool on PATH, a wrong Unity path in
   `.claude/scripts/run-tests.sh`). Warnings are fine. If PowerShell itself rejects the
   script (a syntax error: it has never run on Windows), fix the script, commit the fix
   on main with the message `factory: Start-Factory.ps1 first-run fix`, and push.
8. **Prove the rig once, unattended:** in the factory clone, from Git Bash,
   `git worktree add .claude/worktrees/test-rig -b test-rig main` once (the rig refuses to
   run without it), then `.claude/scripts/run-tests.sh EditMode`. The first run warms the
   rig's Library (~5 min). Report the pass/fail line.
9. **Report** (six lines max): what was pushed, PR merged or not, clone path, Python
   version, .env present or skipped, preflight result, EditMode result. Then tell Grey
   the first shift is one command in the factory clone:
   `powershell -NoProfile -ExecutionPolicy Bypass -File .claude\scripts\factory\Start-Factory.ps1`
   and that the board is `docs/loop/NEEDS-GREY.md`.

Do not start a shift yourself; Grey starts the first one when he is awake to watch it.
