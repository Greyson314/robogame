# 126 — Edit-Block button replaces middle-click instance-edit

The session-125 middle-click instance-edit felt fiddly in play (a cube would
"select" into an orange box with no obvious way to mess with it). Replaced
with an explicit, discoverable mode.

## What changed

**Middle-click is a plain eyedropper again.** It copies the pointed block's
type + per-instance settings (dims / pitch / teeter / config) onto the next
placement — the original session-123 request — with no orange highlight and
no instance binding.

**New `BuildEditMode`** (state + a clickable HUD button + `E` hotkey),
modeled on `BuildMirrorMode`. The button sits top-centre under the mirror
banner; label flips "EDIT BLOCK" ⇄ "EDIT: ON" and tints accent when active.
Leaving build mode forces it off.

**Edit mode behaviour** (in `BlockEditor`): while the toggle is on, a
left-click *binds* the pointed block to the variant panel for in-place
editing (`BuildSession.EditingInstance`) instead of placing — place/remove
are suppressed so a tweak-click can't drop or delete a block. Sliders then
drive that one block live (propagation already filters to `EditingInstance`,
session 125). Only blocks with tunable variants bind; clicking a plain cube
flashes invalid rather than binding an empty "EDITING" panel. Clicking a
different block rebinds; toggling the button (or `E`, or leaving build mode)
exits and drops the highlight.

The orange highlight shell + "EDITING" panel title from 125 are reused as-is.

## Net

Editing an existing block is now: click **EDIT BLOCK** → click the block →
move sliders → click **EDIT: ON** to leave. No keyboard archaeology, no
delete-and-replace, no orphaning. The middle-click eyedropper and the
explicit edit mode are cleanly separated: one copies onto a *new* placement,
the other tweaks an *existing* block.

## Removed

- Middle-click instance binding + its implicit exits (re-click toggle,
  empty-click, hotbar-type-switch). Esc was never wired (it's owned by the
  settings panel); the button is the exit now.
