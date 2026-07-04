# 130 — Blender art pipeline: first loop closed

**Date:** July 4, 2026
**Intent:** Start the art phase. Wire Blender into the AI-assisted workflow so props/meshes stop being Unity-primitive approximations.

## What shipped

- **Style exploration mode** — [art-direction.md](../subsystems/art-direction.md) got a status banner suspending the 12-token palette lock and style-driven Forbidden List entries while the direction is re-explored. Engineering rules (MaterialPropertyBlock-only, no extra asset-store shaders, perf budgets) stay in force. Committed in `5628a51c`.
- **blender-mcp wiring** — `.mcp.json` now launches `uvx blender-mcp` (native `mcp__blender__*` tools appear next session). The Blender-side addon (`addon.py` from [ahujasid/blender-mcp](https://github.com/ahujasid/blender-mcp)) is installed in the user's Blender 5.1.2 and listens on `localhost:9876`.
- **Direct socket fallback** — the addon speaks plain JSON over TCP (`{"type": "execute_code", "params": {"code": ...}}`), so Claude can drive Blender without the MCP layer (used this session, since new MCP servers need a session restart). Handlers of note: `get_scene_info`, `execute_code`, `get_viewport_screenshot` (visual iteration loop: run script → screenshot → adjust).
- **`artgen/`** — new repo-root folder for Blender generation scripts (outside `Assets/` so Unity ignores them). Scripts are the source of truth; FBX is a build artifact. Idempotent by convention: re-running replaces the object.
- **First asset:** `artgen/rock_01.py` → `Assets/_Project/Art/Models/Props/Rock_01.fbx`. Icosphere → clouds displace → decimate; 76 faces, ~1.4 m boulder, exported with `FBX_SCALE_ALL` so Unity should import at scale 1 with no fudge factors.

## Open / next

- **Unity import unverified.** Editor wasn't running this session. On next Unity focus: check import scale against the player chassis (~1.6 m), commit the generated `.meta` files, and place via an `EnvironmentBuilder` pass if it reads well.
- Restart Claude Code session to pick up the native blender MCP tools (approve the new project server prompt).
- Material story for generated props is open — palette re-pointing is suspended, so decide per-asset for now.
