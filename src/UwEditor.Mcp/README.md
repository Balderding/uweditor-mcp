# UwEditor.Mcp: the editor's tools for AI assistants (MCP)

The editor's level editing as [Model Context Protocol](https://modelcontextprotocol.io) tools, so an AI
assistant (Claude, or any other MCP client) can read and change Ultima Underworld levels with the
editor's own core (`UwEditor.Core`): the same consistency checks, the same undo steps, the same safe
save (`.BKP` / `.ORIG`).

There are two ways to use it:

- **In the editor, watching (Level ▸ Let an AI assistant work here (MCP)…).** The tools work on the game
  open in the editor: each change shows in the views as it is made and is an Undo step of its level;
  the view stays where you put it. The window lists each call; closing it (or Stop) stops serving.
  The client starts the editor as `UwEditorAvalonia --mcp`: an MCP server on stdio, with no window,
  that lists the tools itself and passes each call to the open window through a named pipe
  (`uweditor-mcp`, this computer and this user only). The client may be started before the window is
  opened, and the window may be closed and opened again: a call with no window answers, as an error,
  what to open. `open` is not offered there (games are opened in the editor).
- **On its own, without a window (`src/UwEditor.Mcp.Server`).** A console program over stdio that opens
  a game folder itself. `--only FOLDER` makes it refuse every other folder: use it to keep the server
  on a copy of the game. (`--app` makes it the same go-between as the editor's `--mcp`.)

## Setting up a client

The editor's manual (Help ▸ Manual, "An AI assistant at work") has the steps for Claude Code and Claude
Desktop on Windows, Linux and macOS. In short, the command is the editor's path with `--mcp`:

    claude mcp add uweditor -- "C:\Games\UwEditor\UwEditorAvalonia.exe" --mcp

    { "mcpServers": { "uweditor": { "command": "C:/Games/UwEditor/UwEditorAvalonia.exe", "args": ["--mcp"] } } }

The program on its own, built from source:

    dotnet build src/UwEditor.Mcp.Server -c Release
    claude mcp add uweditor-files -- dotnet "<repo>/src/UwEditor.Mcp.Server/bin/Release/net10.0/UwEditor.Mcp.Server.dll" --only "<a copy of the game>"

## Tools

260 tools: everything the editor's menus and windows do, except starting DOSBox or Underworld
Exhumed (the Run and Test tools write the test copies and give the command line; launching is left to you).
Every change is an undo step of its level (or of its table, conversation, picture...), and `save` writes
everything changed: the levels, the game tables, strings, conversations, pictures, sounds and music, each
file keeping `.BKP` and `.ORIG`. Tools that replace files or saved games take `confirm=true`. In the
editor's window the tools share the open windows' data (the Object tables, Game tables, Schedules,
Conversations and Sound windows), so a change shows there and either side's save writes it.

### Levels and tiles (42)

| Tool | What it does |
|---|---|
| `automap_all` | Marks every open tile of a level's automap seen (as the game writes them) or clears it |
| `automap_note_add` | Adds a note to a level's automap on a tile |
| `automap_note_delete` | Deletes a note of a level's automap by its number (from automap_notes) |
| `automap_notes` | A level's automap (the player's map) |
| `automap_undo` | Takes back the last change to a level's automap or its notes (the automap's own undo, apart from the level's) |
| `check` | Runs the game's own checks on a level as it is now (its object lists) |
| `decorate` | Edit > Decorate the selected tiles |
| `decoration_kinds` | The decoration kinds (debris, plants, moss, bones, stalactites) with the game's items for each, and which hang from the ceiling |
| `fill` | Sets a rectangle of tiles at once (corners in any order), one undo step |
| `fill_fields` | Sets the tile flags fill leaves out on a rectangle of tiles (corners in any order) |
| `item_find` | Items by number (0x1A2 or 418) or by a part of their name |
| `level_clear` | Level > Clear the level |
| `level_make` | Level > Make this slot a level |
| `level_map` | A level as text |
| `level_texture_set` | Points one of a level's texture slots at another global texture (as Level > Level textures) |
| `level_textures` | A level's texture slots (UW1 |
| `light_flags_all` | Sets the light flag on every open square of a level, or clears it everywhere (UW2 applies DL.DAT's minimum light by it) |
| `object_add` | Adds an item on an open tile with the editor's defaults for it (a creature, a container, a lock get theirs) |
| `object_delete` | Deletes an object and everything inside it |
| `object_list` | The objects on a tile (give x and y), or on every tile of the level (the first 300) |
| `object_move` | Moves an object lying on a tile to another open tile, keeping its height above the floor |
| `picture` | The editor's 2D map of a level as a picture (floor textures, walls, objects), the whole level or a rectangle of tiles |
| `prefab_list` | Edit > Prefabs |
| `prefab_load` | Loads a prefab as the copied tiles (to turn, mirror and tiles_paste) |
| `prefab_place` | Places a prefab with its south-west corner on (x, y), mirrored and turned first if asked (as tiles_paste |
| `prefab_save` | Saves the copied tiles as a prefab under a name (one of the same name is replaced) |
| `recipe_lever_door` | A new lever on a wall of a tile that toggles a door (its use trigger runs a door trap on the door's square) |
| `recipe_plate` | Stepping on a square runs a trap |
| `recipe_spawner` | Wandering monsters |
| `recipe_teleport` | Stepping on a square sends the player to a square of this or another level (a move trigger and a teleport trap) |
| `save` | Writes the changed levels to LEV.ARK (the old file kept as LEV.ARK.BKP) and the changed game tables and strings (each old file kept as .BKP) |
| `start_get` | Where a new game starts (always level 1 |
| `start_set` | Moves the new-game start square on level 1 to an open tile |
| `textures_find` | The game's wall, floor or door textures with their terrain class from TERRAIN.DAT |
| `tile_get` | One tile's fields |
| `tile_set` | Sets one field of a tile |
| `tiles_clip` | What tiles_copy (or prefab_load) holds now |
| `tiles_copy` | Edit > Copy tiles with what is on them |
| `tiles_mirror` | Edit > Mirror the copied tiles |
| `tiles_paste` | Edit > Paste tiles |
| `tiles_turn` | Edit > Turn the copied tiles |
| `undo` | Takes back the last changes made to a level (by you or in the editor), one step at a time |

### Objects, creatures and locks (18)

| Tool | What it does |
|---|---|
| `floor_raise` | Raises (steps above 0) or lowers (below 0) the floor of a tile or a rectangle by floor steps, kept to 0-15 |
| `lock_copy` | Gives a door or container a copy of another one's lock (number, locked, difficulty, flags) |
| `lock_door` | Locks (or unlocks) a door or a chest |
| `lock_remove` | Takes the lock off a door or container (the right-click menu's Remove its lock) |
| `object_add_inside` | Adds an item inside a container, a creature's inventory or a trigger's chain, with the editor's defaults for it |
| `object_get` | Every field of one object as the editor's properties panel shows it |
| `object_put_inside` | Puts an object (from a tile or another container) inside a container, a door, a creature's inventory or a trigger's chain, at the head of its list |
| `object_set` | Sets fields of an object by the names object_get shows (case and spaces do not matter |
| `object_take_out` | Takes an object out of a container, a creature's inventory or a trigger's chain onto a tile of the level (traps keep their settings) |
| `objects_clip` | What objects_copy holds now |
| `objects_copy` | Edit > Copy (or Cut) |
| `objects_delete` | Deletes several objects with everything inside them as one undo step |
| `objects_duplicate` | Edit > Duplicate |
| `objects_paste` | Edit > Paste |
| `objects_to_floor` | Puts the objects lying on a tile or rectangle down on its floor (by their fine position on a slope) |
| `redo` | Edit > Redo |
| `snap_to_wall` | Puts a lever, button, writing or picture (0x160-0x17F) against the nearest wall of its tile, facing into the wall as the shipped ones do |
| `undo_history` | Edit > Undo history |

### Traps, triggers and recipes (16)

| Tool | What it does |
|---|---|
| `key_text_set` | The description of the keys of one lock number ("a key to the ..." |
| `keys_list` | Tools > Keys and locks |
| `lever_items` | The switches (levers, buttons, pull chains) a new lever of recipe_lever can be |
| `object_link` | Links a trigger, trap, or a lever/switch/button/chain (through the use trigger it holds, made when it has none) to another object that stays where it lies, as the shipped levels do |
| `object_links` | An object's links as the properties panel shows them |
| `object_unlink` | Takes a trigger's or trap's link off (the object it named stays where it lies) |
| `recipe_end_of_game` | UW1 |
| `recipe_lever` | Lever opens a door recipe with every setting |
| `recipe_list` | Edit > Trap recipes |
| `recipe_object_trigger` | Object trigger recipe |
| `recipe_plate_trap` | Pressure plate recipe with every setting |
| `teleport_target` | Edit > Go to the teleport's target |
| `terrain_changes` | Tools > Preview changed terrain |
| `terrain_preview` | The 2D map of a level as it would look after its change-terrain traps ran (all, or numbers from terrain_changes), and the squares that change |
| `trap_target_set` | Edit > Choose the selected trap's target |
| `trigger_chains` | Tools > Triggers and traps |

### Finding, checks and fixes (14)

| Tool | What it does |
|---|---|
| `fixes_apply` | Level > Fixes on one level, one undo step |
| `fixes_checks` | The checks of Level > Fixes |
| `fixes_find` | Level > Fixes |
| `hitbox_overlaps` | View > Collision edges as text |
| `item_cleaner` | Level > Item cleaner |
| `item_slots` | Tools > Item slots |
| `item_uses` | Tools > Item slots for one item id |
| `mod_checks` | The checks run before a mod is made |
| `object_text` | The text a book, scroll, gravestone, writing or text trap reads (its block and string) and how many objects share that string |
| `object_text_set` | Sets what a book, scroll, gravestone, writing or text trap reads |
| `objects_find` | Level > Find in all levels |
| `objects_replace` | Find's Replace |
| `try_jump` | The Try Jump tool |
| `world_check` | Level > World check, on the levels as edited |

### Schedules (UW2) (8)

| Tool | What it does |
|---|---|
| `schedule_row` | One schedule row's fields (time, level, once, event, then the event's parameters) with values, ranges and meanings |
| `schedule_row_add` | Adds a schedule row after a row (-1 |
| `schedule_row_delete` | Deletes a schedule row |
| `schedule_row_move` | Swaps a schedule row with the one above or below it when both have the same time (rows run in time order) |
| `schedule_set` | Sets a field of a schedule row by name (as schedule_row lists them) |
| `schedules` | Tools > Schedules (UW2's SCD.ARK, the save's or DATA's) |
| `schedules_revert` | Drops the unsaved schedule changes |
| `schedules_save` | Writes the changed schedule blocks to SCD.ARK (the old file kept as SCD.ARK.BKP) |

### Views and pictures (3)

| Tool | What it does |
|---|---|
| `editor_selection` | In the editor's window |
| `editor_show` | In the editor's window |
| `view_picture` | The level in 3D as the editor's view draws it (textured walls, floors and ceiling, the objects as models and pictures) |

### Game tables and strings (8)

| Tool | What it does |
|---|---|
| `creature_get` | A kind of creature's stats from OBJECTS.DAT (hit points, attributes, race, attacks, defence, speed, senses, loot, experience...), every field by name with its value |
| `creature_set` | Sets one stat of a kind of creature in OBJECTS.DAT (every creature of that kind, in every level and saved game), range-checked |
| `string_get` | STRINGS.PAK |
| `string_set` | Changes one string of STRINGS.PAK (or adds one, at the index after the last) |
| `table_get` | Every field of one row of a data table with its value (and its meaning, where it has one) |
| `table_revert` | Drops the unsaved changes of one data table (for the game tables the whole file's) |
| `table_set` | Sets one field of one row of a data table, range-checked |
| `tables` | The game's data tables (not a level's) |

### Saved games, the player and globals (35)

| Tool | What it does |
|---|---|
| `backup_list` | The game folder's files that have the copies the editor keeps |
| `backup_restore` | Puts one file back from its .ORIG or .BKP copy (the file now kept as .BKP), then reads the game again |
| `backup_restore_all` | Puts every file the editor changed back to its .ORIG copy (the game as first found), then reads the game again |
| `checkpoint_delete` | Deletes one checkpoint (the editor's copy, not a game file) |
| `checkpoint_list` | The checkpoints (copies of unsaved work the editor keeps every few minutes) of the open game, newest first, numbered from 1 |
| `checkpoint_make` | Keeps a checkpoint of the unsaved work now (changed levels, the player, automaps |
| `checkpoint_restore` | Loads a checkpoint back as unsaved, undoable edits (nothing in the game folder is written) |
| `checkpoint_settings` | How often the editor keeps a checkpoint (minutes, 0 none) and how many it keeps |
| `global_get` | One global |
| `global_list` | Tools > Global variables |
| `global_save` | Writes the changed globals |
| `global_set` | Changes a global in the open saved game (quest flag 0-1, quest byte 0-255, game variable, X clock, skill, a conversation's own) |
| `gog_extract` | File > Open GOG's game.gog |
| `gog_info` | What GOG's game.gog (the CD image GOG's installs play) holds |
| `inventory_add` | Puts a new object into the inventory |
| `inventory_list` | The player's inventory, each object numbered (the numbers the other inventory tools take) |
| `inventory_move` | Moves an inventory object (with what it holds) to a slot, equipping it (an object there goes to the moved one's old slot or the pack), or into a container |
| `inventory_remove` | Takes an object, and whatever is inside it, out of the inventory |
| `inventory_set` | Sets a field of an inventory object |
| `player_check` | Everything about the character the game would notice (UW2 calls attributes over 64 cheating), values play cannot produce, and notes (maxima the game would recompute, inventory prob |
| `player_default` | The editor's built-in default character (used by play tests and save_create when there is no saved game) |
| `player_get` | The open saved game's character |
| `player_move` | Edit > Move the player here |
| `player_recompute` | Sets max vitality, max mana and carrying capacity as the game computes them from the attributes, level and mana skill |
| `player_save` | Writes the player to PLAYER.DAT (the old file kept as PLAYER.DAT.BKP), and the conversations' globals changed here to BGLOBALS.DAT |
| `player_set` | Sets one field of the character or one skill (by name |
| `restore_all` | File > Restore all |
| `save_check` | File > Check and repair the saved game |
| `save_copy` | Copies a saved game onto another slot, as the game copies a whole folder |
| `save_create` | Makes a new saved game in a slot |
| `save_export` | File > Export as a saved game |
| `save_list` | The game's saved games SAVE1-SAVE4 |
| `save_open` | Opens saved game 1-4 (its levels, player and globals) in place of the open game, or 0 for DATA (a new game's levels) |
| `save_rename` | Changes a saved game's description (DESC |
| `save_repair` | Repairs the findings of save_check (by number |

### Conversations (13)

| Tool | What it does |
|---|---|
| `conversation_add` | Gives an empty slot a new conversation (a greeting, one answer and a goodbye, to be changed with conversation_set), and the name it talks under |
| `conversation_check` | Compiles a conversation's source (the dialogue view's language) without applying it |
| `conversation_delete` | Takes a slot's conversation out of CNV.ARK (its strings stay) |
| `conversation_get` | One conversation as the editor shows it |
| `conversation_graph` | A conversation's graph as text (Tools ▸ Conversations ▸ Graph) |
| `conversation_list` | Every conversation of CNV.ARK |
| `conversation_revert` | Puts a conversation and its strings back as last saved, or as shipped (the .ORIG copies the editor kept when it first wrote the files) |
| `conversation_script_set` | Assembles a conversation's assembly text (conversation_get's script view, changed) into it, as the Script tab's Apply |
| `conversation_set` | Compiles a conversation's source and applies it, as the editor's Apply (Ctrl+Enter) |
| `conversation_simulate` | Runs a conversation in the editor's simulator (no game, nothing played) |
| `conversation_string_set` | Changes one string of a conversation's block (or adds one at the index after the last), accents written as letters |
| `conversation_undo` | Takes back the last change made through these tools to a conversation (set, script, string, add, delete, revert), one step at a time |
| `conversation_users` | The creatures in the levels (as saved) that talk through a slot |

### Balance and barter (4)

| Tool | What it does |
|---|---|
| `balance_armour` | The armour and rings overview (Tools ▸ Balance ▸ Armour and rings) |
| `balance_weapons` | The weapons overview (Tools ▸ Balance ▸ Weapons) |
| `barter_simulate` | The barter simulator (Tools ▸ Balance ▸ Barter simulator) |
| `barter_traders` | The creatures of the levels that have a conversation of their own, as barter_simulate's trader (level:index), with how many items their conversation likes and dislikes |

### Graphics, fonts, critters and cutscenes (21)

| Tool | What it does |
|---|---|
| `critter_animations` | A critter file's animations, each as its frames (page:frame) |
| `critter_image` | One frame of a critter as a PNG (or written into a folder) |
| `critter_import` | Replaces a critter frame from a PNG of its size in its colours (critter_image's folder writes one) |
| `critter_list` | The critters' art files (CRIT) |
| `cutscene_frame` | One frame of a cutscene animation as a PNG (or written into a folder) |
| `cutscene_import` | Replaces a frame of a cutscene animation from a PNG of its size in its palette's colours |
| `cutscene_list` | The cutscenes (CUTS) |
| `font_glyph` | One glyph of a font as text (# drawn,  |
| `font_image` | A font's sheet (codes 0-127, 16 a row |
| `font_import` | Replaces a font from its sheet PNG (as font_image's folder writes it, changed) |
| `font_list` | The game's fonts (DATA\*.SYS) |
| `graphics_draw` | Draws on a picture as the pixel editor does |
| `graphics_files` | DATA's picture files (.GR pictures, .TR textures, .BYT and BYT.ARK screens) with how many pictures each holds and how many are changed, not saved |
| `graphics_image` | One picture of a file as a PNG (scaled up to see, by whole pixels), unsaved changes included |
| `graphics_import` | Replaces a picture from a PNG of the same size whose colours are the picture's own (as graphics_image's folder writes it) |
| `graphics_list` | A picture file's pictures |
| `graphics_palette_of` | The colours a picture may use (its palette, or for a 4-bit picture its auxiliary palette's 16), index |
| `graphics_pixels` | A picture's pixels as colour indexes (hex), row by row from the top, with the colours used |
| `graphics_revert` | Drops the unsaved changes of one picture, or of every picture of a file |
| `graphics_sheet` | Many pictures of a file side by side in one PNG (left to right, then down), to find one |
| `palette_get` | A palette of PALS.DAT as text (256 colours, index |

### Sound, music, models and mods (19)

| Tool | What it does |
|---|---|
| `mod_apply` | Applies a .uwmod to the open game (every file checked first |
| `mod_changes` | The saved changes of the game folder a mod would carry |
| `mod_check` | Reads a .uwmod |
| `mod_make` | Packs the saved changes into a .uwmod (only the differences, nothing of the original files), after the game's checks |
| `mod_zip` | Writes the changed files, whole, into a .zip at their game folders (for one's own copy of the game |
| `model_export` | Writes a built-in model (or every one) as Wavefront OBJ with an MTL of its palette colours into a folder |
| `model_get` | One built-in model |
| `model_list` | The built-in 3D models of UW.EXE / UW2.EXE (Tools ▸ 3D models) |
| `music_check` | Checks a theme's files against the game (notes at once, size, loops, timbres) and lists every problem |
| `music_export` | Writes a theme (or all) as standard MIDI files into a folder |
| `music_import` | Makes a theme's files from a MIDI file, as the music panel's Import |
| `music_list` | The music themes |
| `music_revert` | Puts a theme's files back as the originals (the .ORIG copies the editor kept, or the files on disk), written on save |
| `sound_effect_set` | Sets one field of a SOUNDS.DAT effect |
| `sound_effects` | SOUNDS.DAT's effects |
| `sound_export` | Writes a .VOC (unsaved changes included) as an 8-bit mono WAV into a folder, or every sound file with name all |
| `sound_import` | Replaces (or adds) a .VOC from a WAV file, converted as the sound window does (mono, silence trimmed, resampled, normalised, dithered to 8 bits) and checked against the game's limi |
| `sound_list` | The SOUND folder's .VOC files (speech, and in UW2 digital effects and trap sounds) with length, rate, size against the game's limit and their cutscene line |
| `sound_revert` | Drops a sound file's unsaved change |

### Randomizer (22)

| Tool | What it does |
|---|---|
| `randomizer_apply` | Applies a randomizer with its settings now |
| `randomizer_formula` | The math a run makes with the settings now (the window's formula box) |
| `randomizer_help` | A randomizer's settings one a line (name = value, dots for nested ones) with what they may hold |
| `randomizer_list` | The Randomizer menu's tools (Key item scatter > Apply, Automatic zones, Item property shuffle, Swap items, Swap monsters, Named challenges, Randomize mantras, Vowel play) |
| `randomizer_monster_strengths` | The monster swap's strength table |
| `randomizer_objects` | A randomizer's object list |
| `randomizer_objects_edit` | Changes a randomizer's object list (see randomizer_objects) |
| `randomizer_preview` | What a randomizer would change with its settings now |
| `randomizer_reach` | What the last solve reaches |
| `randomizer_report` | A part of a randomizer's last report (preview or apply |
| `randomizer_report_save` | Writes a randomizer's last report (the key scatter's spoiler log |
| `randomizer_seed` | Sets a randomizer's seed (the same seed on the same levels gives the same result) |
| `randomizer_setting` | Sets one setting of a randomizer by its name as randomizer_help lists it (dots for nested ones |
| `randomizer_settings` | A randomizer's whole settings as the settings file has them (Save/Copy settings in its window |
| `randomizer_settings_from_window` | In the editor's window |
| `randomizer_settings_reset` | Puts a randomizer's settings back to the game's defaults, keeping the seed (for the item shuffle every tab follows the levels as they are now |
| `randomizer_settings_set` | Sets a randomizer's settings from JSON |
| `randomizer_solve` | Is the game beatable |
| `randomizer_solve_report` | A part of the last solve's full report (the timeline |
| `randomizer_start_check` | Whether a square can be the key scatter's given start (start = given) |
| `randomizer_undo` | Puts back what a randomizer applied here (as its window's Undo) |
| `randomizer_world` | What the levels hold now, as the windows' world report |

### Zones (25)

| Tool | What it does |
|---|---|
| `zone_add` | Adds a zone (UW2 |
| `zone_doors` | The doors, portcullises and secret doors between zones and what each needs (a key, a lever...) |
| `zone_fill` | Fills with a zone the area reachable from a square that shares its zone (four ways, no rock, no door tiles, no climb above the limit |
| `zone_get` | One zone |
| `zone_grant_add` | A zone grants a token when reached (key:5, item:medallion, flag:gazer_dead, talisman:cup, npc:...), optionally only when a requirement holds |
| `zone_grant_remove` | Removes one of a zone's grants by its number in zone_get |
| `zone_grants_suggest` | Adds to a zone the grants its key items suggest (the Zones window's Add them to the zone's grants) |
| `zone_key_items` | The key items found in the levels (keys, talismans, runes, quest items...) with their place and zone |
| `zone_link_add` | A new link between two zones |
| `zone_link_remove` | Removes a link |
| `zone_link_set` | Changes a link |
| `zone_links` | The zoning's links (the ways between zones and what each requires), or one zone's |
| `zone_map` | A level's zone map as text |
| `zone_mark_goal` | Mark as goal |
| `zone_mark_item` | Marks an object as a key item (true), as not one (false), or takes the mark away (leave important out) |
| `zone_paint` | Paints a rectangle of a level's tiles as a zone (corners in any order |
| `zone_picture` | A level's zones as a picture file (export), or painted from one (import), as the Zones window's picture export and import |
| `zone_remove` | Removes a zone |
| `zone_set` | Changes a zone's name, colour (#RRGGBB |
| `zones_file` | The zoning's file |
| `zones_info` | The zoning (the zones the randomizer's solver uses) |
| `zones_list` | Every zone of the zoning |
| `zones_set_start` | Sets the zoning's start zone (the zone of the start square) |
| `zones_split` | Split multi-level zones by level for the solver (each level its own zone, joined by free two-way links) |
| `zones_undo` | Takes back the last painting, fill or zone removal of the zoning (Undo painting) |

### Run and Test (prepares, never launches) (12)

| Tool | What it does |
|---|---|
| `runtest_dosbox_command` | What Run in DOSBox would use on the game folder |
| `runtest_exhumed_check` | Check in Underworld Exhumed, prepared |
| `runtest_exhumed_settings` | The editor's settings for Underworld Exhumed (key=value lines written into the port's uw1port.cfg/uw2port.cfg in the folder the game runs in, before a run) |
| `runtest_exhumed_settings_set` | Sets the editor's settings for Underworld Exhumed (the whole text |
| `runtest_paths` | Run and Test > Programs and paths |
| `runtest_paths_set` | Sets Programs and paths (the editor's own settings, so only in the editor's window) |
| `runtest_play_test` | Play-test from a tile, prepared |
| `runtest_spot_round` | The spot test's next round |
| `runtest_spot_start` | DOSBox spot test (UW1), prepared |
| `runtest_standable` | Whether a square is one the player can be sent to (the check's test), whether a play test may start there, and the nearest square that can be used |
| `runtest_start_squares` | Each level's square the Exhumed check and spot test send the player to (open, flat, its eight neighbours open at the same height, no water or lava, nothing on it), or that a level  |
| `runtest_stops` | The stops a check in Underworld Exhumed or a spot test makes on levels |

Map characters: `#` solid, `.` open, `1`-`4` diagonal open SE SW NE NW, `n s e w` slope rising N S E W.
Heights `0`-`f`. Levels are numbered from 1, tiles 0-63 (x west to east, y south to north). Empty or
damaged level slots are refused for changes.

A refusal (a bad value, no game open, a folder not allowed) is an error result (`isError`) whose text
starts `Refused:`; anything unexpected is an error result starting `Failed:` with the reason.

## Tests

`tests/UwEditor.Mcp.Tests` (and `McpWindowTests` in `tests/UwEditor.App.Tests`) work on fresh copies of
UW1's `DATA` from `UW_GAMEDATA` or `tests/gamedata`, as the core tests do; the copies go under
`UW_MCP_TMP` (or a `gamecopies` folder beside the tests) and the next run clears copies older than 10
minutes. Without the game files they pass without checking.
