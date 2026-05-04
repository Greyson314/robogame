# Session — Save/load foundations + "+ New Robot" button (Pass B kickoff)

**Intent.** User pivoted from art polish back to gameplay: *"Let's begin
work on 1) expanding the garage, 2) adding a 'New Custom Robot' button,
and roadmapping out how we're going to load and save new robots that
we create."* This session lands the **save/load foundation** and the
**"+ New Robot" / "Save Robot"** HUD buttons. Garage geometry expansion
and the in-garage block-placement editor are deferred to follow-on
sessions (see roadmap below).

**What shipped.**

- New [BlueprintSerializer.cs](../../Assets/_Project/Scripts/Block/BlueprintSerializer.cs).
  Pure (no I/O) JSON round-trip for `ChassisBlueprint`. Explicit DTO
  with a `schemaVersion` field so we can migrate the on-disk format
  without breaking older saves. v1 schema:
  `{ schemaVersion, displayName, kind, createdUtc, entries:[{id,x,y,z}] }`.
  Serializes block IDs (stable strings) rather than asset references —
  saves stay valid across asset moves and are netcode-friendly.

- New [UserBlueprintLibrary.cs](../../Assets/_Project/Scripts/Block/UserBlueprintLibrary.cs).
  Disk-backed registry under `Application.persistentDataPath/blueprints/`
  (survives game updates, untouched by reinstalls). `LoadAll()`,
  `Save()`, `Delete()`, `Changed` event. Generates collision-safe
  slugified filenames (`my-robot.robot.json`, `my-robot-2.robot.json`,
  ...). Pure runtime — does not touch `AssetDatabase`, so player
  builds Just Work.

- New [StarterBlueprints.cs](../../Assets/_Project/Scripts/Block/StarterBlueprints.cs).
  `CreateGroundStarter()` mints a fresh runtime blueprint mirroring
  the proven default rover layout (3×3 cube floor, CPU at origin,
  hitscan weapon on top, 4 corner wheels + 2 mid-side wheels with
  steering at the front). The "blank canvas" the **+ New Robot**
  button drops onto the podium.

- Extended [GameStateController.cs](../../Assets/_Project/Scripts/Gameplay/GameStateController.cs).
  Now owns a merged catalog of **presets first, user blueprints
  second**. New API: `UserBlueprints` list, `CreateNewBlueprint()`,
  `SaveCurrentBlueprint()` (overwrite-or-create, repoints
  `CurrentUserFileName` after save), `DeleteCurrentUserBlueprint()`,
  `RefreshUserBlueprints()`, and a `BlueprintCatalogChanged` event.
  `SelectPreset(int)` is now merged-index-aware — `[0..presetCount)`
  are presets, `[presetCount..total)` are user records. Hydrates the
  user catalog on `Awake()`.

- Extended [SceneTransitionHud.cs](../../Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs).
  Two new bottom-left buttons stacked above the existing chassis
  dropdown (garage-only): **+ New Robot** (calls `CreateNewBlueprint`
  → `GarageController.Respawn` via the existing `PresetChanged`
  pipeline) and **Save Robot** (calls `SaveCurrentBlueprint` and logs
  the resulting filename). Dropdown now shows presets followed by
  user blueprints (suffixed with a ◆ glyph). Subscribes to
  `BlueprintCatalogChanged` so the picker refreshes the moment a save
  or delete completes.

**Architecture.** The `GarageController.PresetChanged → Respawn`
contract did all the heavy lifting — every blueprint mutation
(preset swap, user load, "+ New", save-then-overwrite) flows through
`GameStateController.SetCurrentBlueprint` / `SelectPreset` /
`CreateNewBlueprint`, all of which fire `PresetChanged`. The HUD
never has to talk to `GarageController` directly to refresh the
chassis on the podium.

**Save location.** `%USERPROFILE%/AppData/LocalLow/<company>/<product>/blueprints/`
on Windows. Each robot is one human-readable JSON file; users can
hand-edit, share over Discord, or paste into the future cloud-sync
flow.

**Roadmap (remaining Pass B work).**

- *Phase 2 — UX polish on save flow.* Rename inline (currently uses
  `DisplayName` from the SO; no rename UI yet). Confirm-overwrite
  dialog. Delete button next to dropdown for user blueprints. Save
  toast / status line.
- *Phase 3 — In-garage editor.* Raycast block placement tool that
  edits `CurrentBlueprint.Entries` live, validation overlay (CPU
  count, structural connectivity), part palette UI.
- *Phase 4 — Cross-cutting.* Optional cloud sync, Base64-zip
  share-via-clipboard, schema v2 if we add per-block paint colors or
  rotation.
- *Garage geometry expansion.* Awaiting user choice between
  (1) bigger physical bay + turntable, (2) multiple build pads,
  (3) editor-grid overlay, or (4) all of the above.

**Files touched.**

- Added: [BlueprintSerializer.cs](../../Assets/_Project/Scripts/Block/BlueprintSerializer.cs),
  [UserBlueprintLibrary.cs](../../Assets/_Project/Scripts/Block/UserBlueprintLibrary.cs),
  [StarterBlueprints.cs](../../Assets/_Project/Scripts/Block/StarterBlueprints.cs).
- Modified: [GameStateController.cs](../../Assets/_Project/Scripts/Gameplay/GameStateController.cs),
  [SceneTransitionHud.cs](../../Assets/_Project/Scripts/Gameplay/SceneTransitionHud.cs).
- Untouched (deferred): [EnvironmentBuilder.cs](../../Assets/_Project/Scripts/Tools/Editor/EnvironmentBuilder.cs)
  garage geometry — pending user's expansion-scope answer.
