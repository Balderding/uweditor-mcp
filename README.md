# uweditor-mcp: an MCP server for the Underworld Editor

This adds an [MCP](https://modelcontextprotocol.io) server to the Ultima Underworld editor, so an AI
assistant (Claude Code, Claude Desktop or any other MCP client) can use the whole editor while you
watch: build and change levels, objects and creatures, the game's tables, saved games, conversations,
pictures, sounds, mods and the randomizers. It has **260 tools**.

**This page holds only what we added.** The editor itself is
[Karl Clinckspoor's Underworld Editor](https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia);
everything else is his. The work is offered back to him in
[issue #3](https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia/issues/3).

- The files on this branch are our new files only, in the folders they go in, plus
  [`changes-to-existing-files.patch`](changes-to-existing-files.patch) with the few lines we changed in
  his files.
- To build or run it, use the branch [`mcp-server`](https://github.com/Balderding/uweditor-mcp/tree/mcp-server):
  his editor with this work added.
- [Our changes against his code, in one view.](https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia/compare/main...Balderding:uweditor-mcp:mcp-server)

## What it does

- **Level ▸ Let an AI assistant work here (MCP)…** serves the tools on the game open in the editor.
- Each change the assistant makes shows in the editor as it is made, and is an ordinary Undo step. The
  view stays where you put it.
- A small window lists every call. **Stop** ends it, and anything asked after that is refused.
- The assistant goes through the editor's own code, so it gets the same checks:
  - It is refused whatever the editor refuses: an object on solid rock, a level the game could not load,
    an empty or damaged level slot.
  - Nothing is written until a save. `save` writes everything changed (levels, tables, strings,
    conversations, pictures, sounds, music) and keeps each previous file as `.BKP`, and the first as
    `.ORIG`. Tools that replace files or a saved game need `confirm=true`.
  - In the editor, the tools share the data of the windows you have open (object tables, game tables,
    schedules, conversations, sound), so a change shows there too.
- It never starts a program: the Run and Test tools write the test copies and give the command line,
  and launching is left to you.
- It uses a named pipe of your own computer and user account. Nothing goes over the network.
- The assistant starts the editor a second time as `UwEditorAvalonia --mcp`. That copy has no window
  and passes requests on to the editor you have open. The two can start in either order, and the
  window can be closed and opened again.

## Using it

1. Build the [`mcp-server`](https://github.com/Balderding/uweditor-mcp/tree/mcp-server) branch
   (`dotnet build -c Release`).
2. Tell your assistant to start the editor with `--mcp`. In Claude Code:

       claude mcp add uweditor -- "C:\path\to\UwEditorAvalonia.exe" --mcp

   In Claude Desktop, in `claude_desktop_config.json`:

       { "mcpServers": { "uweditor": { "command": "C:/path/to/UwEditorAvalonia.exe", "args": ["--mcp"] } } }

3. Open a game in the editor (a copy is best), choose **Level ▸ Let an AI assistant work here (MCP)…**,
   and ask the assistant.

## The tools

Every one of them, with what it does, is in [src/UwEditor.Mcp/README.md](src/UwEditor.Mcp/README.md#tools).

| Area | Tools | Some of them |
|---|---:|---|
| Levels and tiles | 42 | `level_map`, `fill`, `floor_raise`, `level_texture_set`, `level_clear`, `start_set`, `tiles_copy` / `tiles_turn` / `tiles_paste`, `prefab_place`, `decorate`, `automap_note_add` |
| Objects, creatures and locks | 18 | `object_get`, `object_set` (any field, a creature's hp, goal, attitude too), `object_add_inside`, `objects_copy` / `objects_paste`, `lock_door` |
| Traps, triggers and recipes | 16 | every Trap recipe (`recipe_plate_trap`, `recipe_object_trigger`, `recipe_lever`, teleports, spawners), `trigger_chains`, `trap_target_set`, `terrain_preview`, `keys_list` |
| Finding, checks and fixes | 14 | `objects_find`, `objects_replace`, `world_check`, `fixes_apply`, `item_cleaner`, `try_jump` |
| Schedules (UW2) | 8 | `schedules`, `schedule_set`, `schedules_save` |
| Views and pictures | 3 | `picture` (the 2D map), `view_picture` (the level in 3D from any square, drawn without a window) |
| Game tables and strings | 8 | `table_get` / `table_set` (weapons, armour, terrain, skills...), `creature_set`, `string_set` |
| Saved games, the player and globals | 35 | `save_list`, `save_create`, `save_export`, `player_set`, `inventory_add`, `global_set`, `checkpoint_make`, `backup_restore` |
| Conversations | 13 | `conversation_get`, `conversation_set` (compiles and applies), `conversation_simulate`, `conversation_graph` |
| Balance and barter | 4 | `balance_weapons`, `balance_armour`, `barter_simulate` |
| Graphics, fonts, critters and cutscenes | 21 | `graphics_image`, `graphics_import`, `graphics_draw`, `palette_get`, `critter_image`, `font_image` |
| Sound, music, models and mods | 19 | `sound_export` / `sound_import`, `music_import`, `model_export`, `mod_make`, `mod_apply` |
| Randomizer | 22 | `randomizer_settings`, `randomizer_preview`, `randomizer_apply`, `randomizer_solve` (is it beatable), `randomizer_reach` |
| Zones | 25 | `zone_paint`, `zone_fill`, `zone_link_add`, `zone_grant_add`, `zone_map` |
| Run and Test | 12 | `runtest_play_test`, `runtest_spot_start`, `runtest_standable`, `runtest_dosbox_command` (they prepare; nothing is launched) |

A refusal comes back as an error result starting `Refused:`, with the reason.

## What we added, file by file

### `src/UwEditor.Mcp/` — the tools (a library, no window)

| File | Lines | What it is |
|---|---:|---|
| [`EditorTools.ContentBalance.cs`](src/UwEditor.Mcp/EditorTools.ContentBalance.cs) | 49 | The tools as the assistant sees them: ContentBalance |
| [`EditorTools.ContentConversations.cs`](src/UwEditor.Mcp/EditorTools.ContentConversations.cs) | 96 | The tools as the assistant sees them: ContentConversations |
| [`EditorTools.ContentGraphics.cs`](src/UwEditor.Mcp/EditorTools.ContentGraphics.cs) | 162 | The tools as the assistant sees them: ContentGraphics |
| [`EditorTools.ContentSound.cs`](src/UwEditor.Mcp/EditorTools.ContentSound.cs) | 115 | The tools as the assistant sees them: ContentSound |
| [`EditorTools.Editing.cs`](src/UwEditor.Mcp/EditorTools.Editing.cs) | 100 | The tools as the assistant sees them: Editing |
| [`EditorTools.EditingChecks.cs`](src/UwEditor.Mcp/EditorTools.EditingChecks.cs) | 99 | The tools as the assistant sees them: EditingChecks |
| [`EditorTools.EditingRecipes.cs`](src/UwEditor.Mcp/EditorTools.EditingRecipes.cs) | 71 | The tools as the assistant sees them: EditingRecipes |
| [`EditorTools.EditingSchedules.cs`](src/UwEditor.Mcp/EditorTools.EditingSchedules.cs) | 43 | The tools as the assistant sees them: EditingSchedules |
| [`EditorTools.EditingTiles.cs`](src/UwEditor.Mcp/EditorTools.EditingTiles.cs) | 69 | The tools as the assistant sees them: EditingTiles |
| [`EditorTools.EditingTriggers.cs`](src/UwEditor.Mcp/EditorTools.EditingTriggers.cs) | 64 | The tools as the assistant sees them: EditingTriggers |
| [`EditorTools.EditingView.cs`](src/UwEditor.Mcp/EditorTools.EditingView.cs) | 38 | The tools as the assistant sees them: EditingView |
| [`EditorTools.LevelSettings.cs`](src/UwEditor.Mcp/EditorTools.LevelSettings.cs) | 99 | The tools as the assistant sees them: LevelSettings |
| [`EditorTools.ObjectFields.cs`](src/UwEditor.Mcp/EditorTools.ObjectFields.cs) | 50 | The tools as the assistant sees them: ObjectFields |
| [`EditorTools.Randomizer.cs`](src/UwEditor.Mcp/EditorTools.Randomizer.cs) | 176 | The tools as the assistant sees them: Randomizer |
| [`EditorTools.RandomizerRun.cs`](src/UwEditor.Mcp/EditorTools.RandomizerRun.cs) | 85 | The tools as the assistant sees them: RandomizerRun |
| [`EditorTools.RandomizerZones.cs`](src/UwEditor.Mcp/EditorTools.RandomizerZones.cs) | 128 | The tools as the assistant sees them: RandomizerZones |
| [`EditorTools.Saves.cs`](src/UwEditor.Mcp/EditorTools.Saves.cs) | 110 | The tools as the assistant sees them: Saves |
| [`EditorTools.SavesPlayer.cs`](src/UwEditor.Mcp/EditorTools.SavesPlayer.cs) | 99 | The tools as the assistant sees them: SavesPlayer |
| [`EditorTools.Tables.cs`](src/UwEditor.Mcp/EditorTools.Tables.cs) | 63 | The tools as the assistant sees them: Tables |
| [`EditorTools.cs`](src/UwEditor.Mcp/EditorTools.cs) | 214 | The tools as the assistant sees them: the first ones, and how each call runs (one at a time, on the editor's thread in the editor) |
| [`McpEditor.Content.cs`](src/UwEditor.Mcp/McpEditor.Content.cs) | 231 | What the content tools share (holding and saving their files) |
| [`McpEditor.ContentBalance.cs`](src/UwEditor.Mcp/McpEditor.ContentBalance.cs) | 281 | The weapon and armour balance reports and the barter simulator |
| [`McpEditor.ContentConversations.cs`](src/UwEditor.Mcp/McpEditor.ContentConversations.cs) | 449 | Conversations: read (dialogue, script, strings), compile, apply, add, delete, simulate, graph |
| [`McpEditor.ContentGraphics.cs`](src/UwEditor.Mcp/McpEditor.ContentGraphics.cs) | 600 | Pictures (GR, TR, BYT), pixels, palettes, critters, cutscenes, fonts |
| [`McpEditor.ContentMods.cs`](src/UwEditor.Mcp/McpEditor.ContentMods.cs) | 131 | Mods: changes, make, zip, check, apply |
| [`McpEditor.ContentSound.cs`](src/UwEditor.Mcp/McpEditor.ContentSound.cs) | 402 | Sounds (VOC and WAV), SOUNDS.DAT, music (XMI and MIDI), 3D models (OBJ export) |
| [`McpEditor.Editing.cs`](src/UwEditor.Mcp/McpEditor.Editing.cs) | 326 | Redo, undo history, floors, the object clipboard, duplicate, locks, snapping to walls |
| [`McpEditor.EditingChecks.cs`](src/UwEditor.Mcp/McpEditor.EditingChecks.cs) | 360 | Find and replace in all levels, the world check, the fixes, mod checks, the item cleaner, item uses and slots, object texts, the jump test, collision overlaps |
| [`McpEditor.EditingRecipes.cs`](src/UwEditor.Mcp/McpEditor.EditingRecipes.cs) | 170 | Every Trap recipe with all its options |
| [`McpEditor.EditingSchedules.cs`](src/UwEditor.Mcp/McpEditor.EditingSchedules.cs) | 149 | UW2's schedules |
| [`McpEditor.EditingTiles.cs`](src/UwEditor.Mcp/McpEditor.EditingTiles.cs) | 204 | The tile clipboard (turn, mirror), prefabs, Decorate |
| [`McpEditor.EditingTriggers.cs`](src/UwEditor.Mcp/McpEditor.EditingTriggers.cs) | 210 | Trigger chains, links, trap targets, teleport targets, terrain changes and their preview, keys |
| [`McpEditor.EditingView.cs`](src/UwEditor.Mcp/McpEditor.EditingView.cs) | 334 | The 3D picture from any square: a small software renderer of the editor's `LevelScene` (no window, no GL); the selection and camera in the editor |
| [`McpEditor.Fill.cs`](src/UwEditor.Mcp/McpEditor.Fill.cs) | 80 | `fill`: every value checked first, then one change of the level |
| [`McpEditor.LevelSettings.cs`](src/UwEditor.Mcp/McpEditor.LevelSettings.cs) | 341 | Texture slots, clear or make a level, the new-game start, tile bits, light flags, the automap and its notes |
| [`McpEditor.ObjectFields.cs`](src/UwEditor.Mcp/McpEditor.ObjectFields.cs) | 281 | Every field of an object or creature by the properties panel's own names (`object_get`, `object_set`), putting inside, `lock_door` |
| [`McpEditor.Objects.cs`](src/UwEditor.Mcp/McpEditor.Objects.cs) | 147 | `item_find` and the basic object tools |
| [`McpEditor.Picture.cs`](src/UwEditor.Mcp/McpEditor.Picture.cs) | 33 | `picture`, the 2D map |
| [`McpEditor.Randomizer.cs`](src/UwEditor.Mcp/McpEditor.Randomizer.cs) | 804 | The randomizers: settings, seeds, object lists, preview, apply, undo, reports |
| [`McpEditor.RandomizerRun.cs`](src/UwEditor.Mcp/McpEditor.RandomizerRun.cs) | 363 | Run and Test: paths, standable squares, play-test, check and spot-test copies with their command lines (never launches anything) |
| [`McpEditor.RandomizerScatter.cs`](src/UwEditor.Mcp/McpEditor.RandomizerScatter.cs) | 334 | The key item scatter and the solver (is it beatable, what is reachable) |
| [`McpEditor.RandomizerZones.cs`](src/UwEditor.Mcp/McpEditor.RandomizerZones.cs) | 661 | Zones: paint, fill, links, grants, key items, goals, files, pictures |
| [`McpEditor.Recipes.cs`](src/UwEditor.Mcp/McpEditor.Recipes.cs) | 104 | The first trap recipes, over the editor's `TrapRecipes` |
| [`McpEditor.Save.cs`](src/UwEditor.Mcp/McpEditor.Save.cs) | 53 | `check` and `save` (levels, tables, strings, conversations, pictures, sounds, music); refuses over files changed on disk |
| [`McpEditor.Saves.cs`](src/UwEditor.Mcp/McpEditor.Saves.cs) | 311 | Saved games: list, open, rename, copy, create, export, check and repair |
| [`McpEditor.SavesFiles.cs`](src/UwEditor.Mcp/McpEditor.SavesFiles.cs) | 410 | Checkpoints, Restore all, the `.BKP` / `.ORIG` backups, GOG images |
| [`McpEditor.SavesGlobals.cs`](src/UwEditor.Mcp/McpEditor.SavesGlobals.cs) | 158 | Global variables and quest flags, with the conversations that use them |
| [`McpEditor.SavesPlayer.cs`](src/UwEditor.Mcp/McpEditor.SavesPlayer.cs) | 448 | The player: every field and skill, the position, the inventory and equip slots |
| [`McpEditor.Tables.cs`](src/UwEditor.Mcp/McpEditor.Tables.cs) | 731 | The game tables (OBJECTS.DAT, COMOBJ.DAT, TERRAIN.DAT, skills, combinations, shades, minimum light) and STRINGS.PAK, shared with the open table windows |
| [`McpEditor.Tiles.cs`](src/UwEditor.Mcp/McpEditor.Tiles.cs) | 100 | `level_map`, `tile_get`, `tile_set` |
| [`McpEditor.cs`](src/UwEditor.Mcp/McpEditor.cs) | 139 | The game it works on (a folder it opens itself, or the game open in the editor) and the guards: one folder only (`--only`), no unsaved edits lost, no empty or damaged slots |
| [`PipeHost.cs`](src/UwEditor.Mcp/PipeHost.cs) | 139 | Serves the tools on the pipe for the editor's window, one assistant at a time |
| [`PipeRelay.cs`](src/UwEditor.Mcp/PipeRelay.cs) | 174 | `UwEditorAvalonia --mcp`: the server the assistant starts; lists the tools and passes each call to the window |
| [`README.md`](src/UwEditor.Mcp/README.md) | 393 | The details and the full list of tools |
| [`UwEditor.Mcp.csproj`](src/UwEditor.Mcp/UwEditor.Mcp.csproj) | 17 | Uses the official C# MCP SDK (`ModelContextProtocol.Core` 2.3.0) |

### `src/UwEditor.Mcp.Server/` — the same tools without the editor's window

| File | Lines | What it is |
|---|---:|---|
| [`Program.cs`](src/UwEditor.Mcp.Server/Program.cs) | 72 | A console MCP server that opens a game folder by itself (`--only FOLDER` keeps it to one) |
| [`UwEditor.Mcp.Server.csproj`](src/UwEditor.Mcp.Server/UwEditor.Mcp.Server.csproj) | 15 |  |

### `src/UwEditor.App/` — in the editor

| File | Lines | What it is |
|---|---:|---|
| [`MainWindow.Mcp.cs`](src/UwEditor.App/MainWindow.Mcp.cs) | 21 | Opens that window from the Level menu |
| [`MainWindow.McpSaves.cs`](src/UwEditor.App/MainWindow.McpSaves.cs) | 75 | Small accessors the save tools need from the main window (opens no window) |
| [`McpWindow.Content.cs`](src/UwEditor.App/McpWindow.Content.cs) | 72 | Hooks: the conversation library, the sound and music windows |
| [`McpWindow.Editing.cs`](src/UwEditor.App/McpWindow.Editing.cs) | 55 | Hooks: the prefab folder, the selection, the camera, the schedules |
| [`McpWindow.Hooks.cs`](src/UwEditor.App/McpWindow.Hooks.cs) | 25 | Where each tool group gets what only the editor holds |
| [`McpWindow.Randomizer.cs`](src/UwEditor.App/McpWindow.Randomizer.cs) | 95 | Hooks: the randomizer windows' settings, the zoning, the program paths |
| [`McpWindow.Saves.cs`](src/UwEditor.App/McpWindow.Saves.cs) | 29 | Hooks: the player sheet, the globals, opening a save, Restore all |
| [`McpWindow.cs`](src/UwEditor.App/McpWindow.cs) | 123 | The Let an AI assistant work here window: connection, the game, the list of calls, Stop |

### Tests

| File | Lines | What it tests |
|---|---:|---|
| [`tests/UwEditor.Mcp.Tests/`](tests/UwEditor.Mcp.Tests) | 3,160 | 154 tests on copies of the UW1 game files: every tool group, the guards, the pipe, the go-between started in either order, the console server, saving and reading back, no tool name used twice |
| [`McpWindowTests.cs`](tests/UwEditor.App.Tests/McpWindowTests.cs) | 89 | The window in the real editor (headless): a call through the pipe changes the open level, and a call through `--mcp` reaches the window |

### The few lines changed in his files ([`changes-to-existing-files.patch`](changes-to-existing-files.patch))

| File | Change |
|---|---|
| `src/UwEditor.App/MainWindow.axaml` | 1 line: the menu item |
| `src/UwEditor.App/UwEditor.App.csproj` | 1 line: the reference to `UwEditor.Mcp` |
| `src/UwEditor.App/Program.cs` | 2 lines: `--mcp` starts the go-between instead of the window |
| `UltimaUnderworldEditorAvalonia.sln` | the three new projects |
| `Directory.Build.props` | the version, 0.1.55 to 0.1.57 |
| `README.md`, `docs/MANUAL.md` | a section each on the MCP server, what it can do and how to set it up |

No file of `UwEditor.Core` is changed: every tool calls the editor's own code.

## Tested

- All 154 MCP tests pass, on copies of the UW1 game files.
- The editor's own tests still pass: 889 core and 263 interface tests.
- The single-file release build serves all 260 tools as `--mcp`, each name once.

All of this is on Windows with Ultima Underworld I. Linux, macOS and Ultima Underworld II are not
tested yet.

The editor and its licence (MIT) are Karl Clinckspoor's. The files here are released under the same
licence.
