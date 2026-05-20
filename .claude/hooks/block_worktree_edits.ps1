# PreToolUse hook: block Edit/Write/MultiEdit calls targeting paths inside
# .claude/worktrees/. Maps to memory editor_target_main_checkout.md — edits
# under worktree paths are invisible to Unity, which only watches the main
# checkout at C:\Users\Grey\Desktop\mutedtuple\robogame\.

$payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $payload.tool_input.file_path
if (-not $path) { exit 0 }

$normalized = $path -replace '/', '\'
if ($normalized -match '\\\.claude\\worktrees\\') {
    [Console]::Error.WriteLine("Blocked: '$path' is inside .claude/worktrees/.")
    [Console]::Error.WriteLine("Unity only watches the main checkout at C:\Users\Grey\Desktop\mutedtuple\robogame\.")
    [Console]::Error.WriteLine("See memory: editor_target_main_checkout.md")
    [Console]::Error.WriteLine("Rewrite the path under the main checkout root before retrying.")
    exit 2
}
exit 0
