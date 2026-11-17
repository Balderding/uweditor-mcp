# Roadmap: November 2026

Written 2026-11-09, at commit 2747502e (editor 0.1.x, manager 0.2.x). The previous roadmap
planned the port of the Unity editor's features; that port is essentially done, so this one
plans what comes next, in five parts:

1. **Features:** what is still missing from the Unity editor, and what other editors have
   that would save time here.
2. **Breaking the editor:** every way found to break it, a test for each, and the fixes the
   survey already calls for.
3. **Several mods at once:** a portraits mod with a music mod, and how conflicts are resolved.
4. **Underworld Revisited** in the mod manager.
5. **A study of new levels:** stitching, turning and mirroring areas of UW1's levels, and
   areas made by maze algorithms.

Section 6 puts it all in order, day by day. It was planned from four surveys made the same day:
the Unity editor's `modding` branch against this code (about 130 features), the code's fragile
spots (with file:line references), Underworld Revisited's releases and its mod plan, and the
mod manager's model.

House rules still apply to every step: bump the app versions, update `docs/MANUAL.md` with
each user-facing change, commit only when asked, LF line endings.

---

## 0. Fix first

**Dragging a trap or trigger changes what it does.** `LevelEditor.MoveObject` /
`MoveObjectAnywhere` (`src/UwEditor.Core/Editing/LevelEditor.cs:600-619`, 761 on) set the fine X
and Y to where the object is dropped and the Z to the floor there plus its old height above
the floor (the drop in `MainWindow.Selection.cs` passes the fine square under the mouse). For
traps whose fine position or height is data (the Unity editor kept a list: `MapCreator`; check
each against event/TRAP.C in Underworld Exhumed), a drag silently changes their parameters.
- **Fix:** such objects change tile only, keeping their fine X, fine Y and Z, as the Unity
  editor did; the drop marker shows the tile, not the eighth; paste and duplicate the same.
- **Test:** for every trap and trigger kind of both games, move, drag, paste and duplicate it;
  the fields other than the tile and the chain are byte-identical before and after.
- Half a day. Do it before anything else. **Done 2026-11-09** (`Triggers.HeightIsData`,
  `FinePositionIsData`; `tests/UwEditor.Core.Tests/TrapMoveTests.cs`). Raising the floor under
  a trap no longer moves it either.

**Found while fixing it: a trigger's link is treated as a quantity.** Every shipped UW1 trigger
has is-quantity set (292 of 292; the traps: 264 without, 32 with), and so does a trigger made
with Add (`ObjectSpec.Default`: is-quantity, link 1, so a new trigger runs object #1). The
game follows a trap's or trigger's link whatever is-quantity says (`Triggers.cs`), but
`LevelEditor` decides "holds a list" by is-quantity alone (`IsQuant`, used at the
`AddObject`, `CanPutInside`, `Copy`, `Paste` and delete checks). So:
- **Make it run a trap** refuses every shipped trigger ("cannot hold");
- copying a shipped trigger does not copy its traps, and the pasted trigger runs the original's;
- a new trigger from Add points at object #1 until its link is typed.
- **Fix (1 day, with §1.1's linking):** a trap's or trigger's link is always an object (or 0);
  new traps and triggers get link 0; Make it run a trap sets the trigger's link to the trap and
  the trap's link to the old first trap (chains run link to link, `Triggers.Chain`), not the
  container's next-list; copy, paste and delete follow `Triggers.Chain`. Tests: on shipped
  triggers of both games, link a trap, copy and paste the trigger (the paste has its own traps),
  delete it (its traps go, a trap another trigger also runs stays).

---

## 1. Features

### 1.1 What the Unity editor had that this one lacks

About 107 of the Unity editor's 130 features are here (several in a better form: checkpoints
for autosave copies, the bullseye for the magenta crosshairs, the Paint tools for the
template painting, zones for sectors). The rest, most valuable first:

| Pri | Feature | Unity source | What this editor has | Days |
| --- | --- | --- | --- | --- |
| done 0.1.54 | **Texts follow their objects:** a new book, scroll, writing, grave or text trap gets a string of its own; copying one copies its text; deleting frees it when no other object uses it | manual §3.5 | only trap recipes add strings; an added book can only point at an existing text | 1.5 |
| done 0.1.54 | **Add and delete conversations:** give an empty slot a conversation (from a template: a greeting and goodbye), delete one | §3.6, `UIManager.Conversations.cs` | the list shows only slots that already have one | 1 |
| done 0.1.54 (not the door to its door trap: Trap recipes does that) | **Linking from the right-click menu:** link a trigger to a trap that stays where it lies (target square set to the trap's tile), a door to its door trap, a lever through its use trigger, a delete or inventory trap to its target object; a rubber-band line while choosing | `UIManager.ContextMenu.cs` | "Make it run a trap" moves the trap into the chain | 1.5 |
| done 0.1.54 | **"Linked from" in the properties panel:** the triggers that run this trap, the triggers on this square and the squares it acts on, each with a go-to button | §2.3 | only in the Triggers and traps window | 1 |
| A | **Typed editors on the properties panel:** add a lock, copy a lock from a door, add / remove a door trap, door type; per trap kind: set-variable operation, change-terrain tile and textures, teleport target, spawn chance, hack-trap mode; picture pickers for levers, writings, graves, bridges, pillars; Identified, Indestructible, Quantity vs Special (+512), potion and scroll traps; Snap to nearest wall | `PropertiesPanel.cs` | raw fields with descriptions and some lists | 3 |
| A | **Add form:** Goal, place at the ceiling, is a trap (potion, scroll), identified, height; new text for books, scrolls, writings and graves | `UIManager.CreateMenus.cs` | most fields, not these | 1 (with the texts) |
| B | **Dated backups:** `Editor backups\<game>\NAME.<date-time>`, 10 kept, up to 1000 | §5.2 | one `.BKP` and the `.ORIG` | 0.5 |
| B | **Object list colours and filters:** active critters green, animated objects yellow; Invisible, Owned, In a container | `ObjectListFilter.cs` | kinds and Enchanted | 0.5 |
| B | **Find in all levels filters:** tri-state Enchanted, Invisible, Quantity, Owned, In a container; quality range; one level only | `ItemFinderWindow.cs` | text, kind, Enchanted | 1 (with 1.2's replace) |
| B | **Export all pictures** of a file (or every file) to PNG | §3.4 | one at a time; cutscenes have Export all frames | 0.5 |
| B | **Another auxiliary palette** for an imported object sprite | `UIManager.Textures.cs` | refuse or nearest colour | 0.5 |
| B | **UW1: add textures** (up to 256) | §3.4 | replace only | 1 |
| B | **Mantras** with a description for each (what it does, the shrine's answer) | `UIManager.Strings.cs` | raw block 1, Randomize mantras | 0.5 |
| B | **Clear all levels** at once, optionally the conversations too | `UIManager.Levels.cs` | one level at a time | 0.5 |
| B | **Mod details:** licence; "Exhumed enhancements to recommend" | `ModExportWindow.cs` | name, id, version, authors, description | 0.5 |
| B | **The player's heading** in a saved game | `PlayerWindow.cs` | position only | 0.25 |
| C | **Automap:** paint "known, not seen"; UW2 map scraps (which map a scrap copies, its sections) | `AutomapWindow.cs` | seen squares and notes | 1 |
| C | **Walk mode extras:** fly (F), eye height, click to select while walking | §2.7 | walk and turn | 0.5 |
| C | **Conversation side panel:** NPC name, liked and disliked items in their own fields | §6.2 | Strings window and `set_likes_dislikes` in the script | 1 |
| C | **Graph editing of conversations** (add nodes, conditions, start node) | §6.1–6.5 | text editing on the Dialogue tab; the graph views | 4+, probably not worth it: the Dialogue tab does more |
| C | **Levels to and from JSON** | `UIManager.Save.cs` | — | folded into 1.2's text export |
| — | Saved games as mods | `ModExportWindow`, `SaveMods.cs` | decided against on 2026-11-05 (mod-format.md) | — |

UltimaUnderworldEditorFree (the folder) is empty; nothing to compare.

### 1.2 Time savers from other editors

None of these exist yet (checked in the manual and the code). Sources: Hammer and TrenchBroom
(brush editors), Ultimate Doom Builder, Tiled, DromEd, the Unity editor, Blender, VS Code.

**Editing the level faster**

| Pri | Feature | Seen in | Notes | Days |
| --- | --- | --- | --- | --- |
| done 0.1.54 | **Copy and paste tiles with their objects** (a rectangle of the level: types, heights, textures, flags, objects and their links), to another place, level or game; **rotate 90° and mirror** the clipboard before pasting | Hammer, UDB, Tiled | Copy works on objects only today. Diagonal and slope tile types turn with it; trigger targets inside the region move with it | 2.5 |
| A | **Prefabs (stamps):** save a clipboard as a named piece (a shrine, a trapped corridor, a fallen adventurer's cache) in the settings folder, and place it from a palette | Hammer prefabs, Tiled stamps, UDB prefabs | builds on the tile clipboard; the item caches (see Reminders) could use the same | 1 |
| done 0.1.54 | **Edit a field on all selected objects** at once (mixed values shown blank) | Hammer, Unity inspector | one undo step | 1 |
| A | **Find and replace objects across levels:** replace item X with Y (or change a field) where the filters match, with a preview list | UDB, Hammer's entity report | with 1.1's finder filters | 1.5 |
| done 0.1.54 | **Select similar:** every object of the same item, the same kind, or the same owner on this level; every tile with this texture | UDB, Blender | then delete, move or edit them together | 0.5 |
| B | **Camera bookmarks:** Ctrl+1–9 saves the view, 1–9 goes back, per game | UDB, Blender, Unity | kept in the settings | 0.5 |
| B | **Reopen where I left off:** the last game, level, camera and open tool windows at start | most editors | an option, on by default | 0.5 |
| B | **What is under the mouse** in the status line: tile, fine place, height, object name, before clicking | Hammer, UDB | | 0.25 |
| B | **Measure:** distance and height difference between two points, in tiles and fine units | Hammer, Tiled | for jumps and the randomizer's reach checks | 0.5 |
| B | **Texture use:** which tiles use a texture slot, selected in one click; replace one slot by another on this level | UDB | | 0.5 |
| B | **Favourite and recent items** in the Add form | Hammer's recent entities | | 0.25 |
| C | **Editor notes on the map:** notes for the modder (not the game's map notes), kept beside the game, shown as pins | UDB comments, DromEd | | 0.5 |
| C | **Lay out tiles from a picture:** a PNG whose colours mean tile types and heights | Tiled, heightmap importers | the reverse of Export the map as a picture | 1 |
| C | **Room and cave generator** for a selected rectangle | DromEd tools, map generators | optional fun | 2 |
| C | **Export the 3D view** as a picture | all | for mod pages | 0.25 |

**Seeing and checking**

| Pri | Feature | Seen in | Notes | Days |
| --- | --- | --- | --- | --- |
| A | **Show changes:** tiles and objects that differ from the original (`.ORIG`) highlighted in the view and listed per level | VS Code's diff gutter, git tools | modders see what their mod does; the same comparison as Make a mod | 1.5 |
| A | **What a mod changes, in words:** "Level 3: 12 objects added, 40 tiles changed; strings: 7 changed in block 3; music theme 5 replaced", in Make a mod, Load a mod and the manager | — | `mod-format.md` says readers work it out from the bytes; nothing does yet. Shared code in `Underworld.Mods` (also part 3's conflict names) | 1.5 |
| A | **Check before making a mod:** World check, Fixes and the object list check run on Make a mod, with the problems listed first | UDB's map analysis on save | | 0.5 |
| B | **Level statistics:** objects by kind, slots used, gold and item value, monsters' total XP and hit points, locks and keys | UDB map analysis, Hammer map information | | 0.5 |
| B | **Overlays:** danger (monster strength per area), loot value, height, UW2 light, what the automap has seen | UDB visual modes | drawn like the zones and light flags | 1.5 |

**Words and translation**

| Pri | Feature | Notes | Days |
| --- | --- | --- | --- |
| A | **Export and import all strings** as a translation table: CSV and PO (Poedit, Weblate), and Underworld Revisited's `Languages/<code>.strings.tsv`, so one translation serves both | with the accent table; ties in with the font work | 1.5 |
| B | **Spell check** strings and conversation text (a word list per language; the game's own words allowed) | | 1 |

**Working with the editor**

| Pri | Feature | Seen in | Notes | Days |
| --- | --- | --- | --- | --- |
| A | **Command palette:** Ctrl+K, type part of any menu item or tool window and press Enter | VS Code, Blender F3 | Ctrl+Shift+P is taken (player) | 0.5 |
| B | **Rebindable keys** (Help ▸ Controls becomes editable; a JSON in the settings) | all | | 1 |
| B | **Drag and drop** a game folder, `game.gog`, `.uwmod` or PNG onto the window | all | | 0.25 |
| B | **Check for updates** (GitHub releases, opt-in) in both apps | all | | 0.5 |
| C | **Scripting console:** C# scripts (Roslyn) with the Core API, for bulk edits | Unity editor scripts, Blender Python, UDB's JavaScript | large; only if modders ask | 3 |
| C | **Levels as text** (JSON) for version control and diffing; also covers the Unity editor's Save to JSON | Unity editor | | 1.5 |

### 1.3 The mod manager

| Pri | Feature | Days |
| --- | --- | --- |
| A | Mod sets with load order and conflicts (part 3) | see part 3 |
| A | Underworld Revisited (part 4) | see part 4 |
| A | **Mod details page:** what it changes in words (1.2), its readme, pictures (an optional `images/` folder in the package, ignored by old managers) | 1 |
| B | **Open `.uwmod` files with the manager** (file association, drag and drop) | 0.5 |
| B | **A desktop shortcut per mod** ("Play Garamon moved": `uwmod play`) | 0.5 |
| B | **Saved games per mod:** swap the slots when the mod changes (a backup exists; this makes it automatic) | 1 |
| B | **Steam's release** (`game.bin`, read like `game.gog`), in the manager and the editor | 1 |
| C | **A list of known mods** (a JSON index on GitHub that the manager can read, opt-in) | 1 |

---

## 2. Breaking the editor: tests, and the fixes they call for

A survey of the code on 2026-11-09 (with file:line references) found where the editor is
fragile. Each group below gives **what breaks it**, **the test** that proves it, and **the
fix** where the survey already shows a problem. The rule for every test: the editor may refuse
the input or the action with a message, but it must never crash, hang, eat all memory, write a
damaged file, or lose unsaved work without asking.

What already exists: about 410 Core tests, 200 headless App tests, 25 mod round trips, 23 Mods
tests, 6 screenshot tests and 39 manager tests. They test that correct files round-trip byte
for byte. **No test feeds the editor a damaged file**, and none covers a read-only folder, a
locked file or a full disk. `RobustnessTests.cs` covers only the UI error handler, the run
marker and the save repair.

### 2.1 Damaged and hostile files (fuzzing)

**Breaks it:** a truncated, zero-length, oversized or random-byte `LEV.ARK`, `CNV.ARK`,
`STRINGS.PAK`, `*.GR`, `OBJECTS.DAT`, `PLAYER.DAT`; a mod's or a downloaded saved game's files;
a `game.gog` from elsewhere.

Found by the survey:
- `Archive` trusts the header: block count, offsets and stored lengths go straight into
  `new byte[StoredLength]` and `Array.Copy` (`Underworld.Formats/Games/Archive.cs:54,170-182`).
- `StringsPak`: `ReadByte()` = -1 at the end of the file is used as data; a truncated file
  gives `new HuffmanNode[-257]` (`StringsPak.cs:23-28`); block offsets, counts and child indexes
  are unchecked (:46-86, :134, :150-155); `ConvertStringSlow` loops forever on a cyclic tree
  (:281-293) while the fast path stops after 64 steps (:214). `StringsPakFile.FromBytes` can
  size an array negatively (`UwEditor.Core/Conversations/StringsPakFile.cs:48-52`).
- `GraphicsCodec` (the old picture reader) takes a count from `ReadByte` unchecked and leaves
  file streams open (`Graphics/GraphicsCodec.cs:51,77,86-88`). `GrFile` is already safe.
- `XmiFile` recursion has no depth limit (`Sound/XmiFile.cs:98`); BDF import uses `int.Parse`
  (`FontExport.cs:194`).
- `LevelBlock`: a corrupt free-list top makes `MobileFree` read outside the list or return a
  slot in use (`LevelEditor.cs:369-371`, `LevelBlock.cs:55-59`); `FreeIndex` does not bound
  `top+1` (`LevelEditor.cs:393-395`); `Clear` calls `ObjectOffset` on an unchecked index
  (`LevelBlock.cs:167`).
- Damage is hidden instead of reported: a string block that fails becomes empty
  (`GameStrings.cs:27-28`); a `PLAYER.DAT` that fails becomes "no player"
  (`GameSession.cs:160`); whole-game scans skip a level that fails with `continue` (Keys.cs:74,
  WorldCheck.cs:63, ItemCleaner.cs:141/204, AbilityZones.cs:172/522/641, Challenges.cs:140/602,
  ConversationLibrary.cs:225), so the randomizer and the checks quietly leave a level out.

**Tests** (new project `tests/Robustness.Tests`, runs in a few minutes, seeds logged so a
failure can be replayed):
1. **Truncation sweep:** every game file of both games cut at 0, 1, 2, 4, 16 bytes, at every
   header field's end, at each block boundary, and at 25/50/75/99% of its length. Open the game:
   must give a readable message naming the file, never an exception other than the editor's
   own `GameFileException`, never hang (each case under a 10 s timeout).
2. **Bit-flip and field fuzz:** 2,000 seeded runs per file kind: flip random bytes; set header
   counts, offsets and lengths to 0, -1, 0x7FFFFFFF, file length + 1; for `STRINGS.PAK`, make
   the tree cyclic and point children out of range. Same rule as 1. Memory under 500 MB.
3. **Level block fuzz:** corrupt free-list tops and entries, chains that loop, links out of
   range, an object in two chains; then **Check the object lists**, add, delete, paste, save.
   Must refuse with the check's message, never write a level that `LevelBlock.Check` rejects.
4. **Saved games from the wild:** `PLAYER.DAT` too short, positions outside the map, level
   number 0 or 99, inventory links looping. Opening must name the problem and offer the repair.
5. **Imports:** PNG of 0×0, 1×100000, 16-bit, palette with 300 entries, truncated; WAV with a
   lying header, 0 samples, 4 GB claimed; MIDI with 1,000 nested tracks, a running status at the
   start, a length past the end; BDF with letters for numbers; TrueType renamed `.bdf`.
6. **Mods and images:** `.uwmod` with `../` paths, absolute paths, upper/lower case clashes
   (`data/lev.ark` and `DATA/LEV.ARK`), a 4 GB entry claimed, a zip bomb, a delta reading past
   its base, a `mod.json` 100 levels deep; `game.gog` with a looping folder. (Many are already
   guarded; the test keeps them guarded.)
7. **Settings and state files:** `settings.json` empty, truncated, wrong types, 10 MB; the
   recent list naming missing folders and UNC paths; a checkpoint folder with a broken
   `meta.json`.

**Fixes:**
- A `GameFileException(file, offset, what)` thrown by every reader; `Archive`, `StringsPak`,
  `StringsPakFile` and `GraphicsCodec` check every count, offset and length against the file
  before allocating; the slow string walk gets the fast path's 64-step cap; XMI gets a depth
  limit; BDF uses `TryParse`.
- `LevelBlock` free-list access bounds-checks and refuses ("level 3's free list is damaged; run
  Level ▸ Fixes").
- Damage is shown, not hidden: the status line and the World check list the files and levels
  that failed to read; the randomizer refuses to run with a level it could not read.
- A corrupt `settings.json` is kept as `settings.json.bad` before the defaults are written.

### 2.2 Saving under bad conditions

**Breaks it:** the folder is read-only (Program Files, a CD copy, OneDrive placeholder); the
file is locked because DOSBox or the game has it open; the disk fills up half way; the path is
longer than 260 characters or has non-ASCII letters; the game folder disappears (USB stick
pulled, network share gone); antivirus holds the `.tmp` file.

Found by the survey:
- `SafeFile.Write` (`ConversationArchive.cs:140-155`) writes `NAME.tmp` and `File.Replace`s;
  when that throws it falls back to copy + move, which is not atomic (:151-152), and a failed
  write leaves `NAME.tmp` behind.
- Writes that skip SafeFile: `Archive.Save` (`Archive.cs:26`), `ObjectDataCodec` saves
  (`ObjectDataCodec.cs:326,362,539`), the zones file after its first `.BKP`
  (`ZoningState.cs:118-119`), settings (`Settings.cs:134`).
- **Save all is not all or nothing:** it writes levels, then the player, then strings and so on;
  a failure leaves the earlier ones written and the message does not say which
  (`MainWindow.Editing.cs:648-698`).
- Three edits go to disk at once, outside Save all and undo: the start square
  (`MainWindow.Editing.cs:491`), the pixel editor's "Applied and saved"
  (`PixelEditorWindow.cs:417`), item slot names (`ItemSlotsWindow.cs:96`).
- **Stale base:** `LEV.ARK` is rebuilt from the archive read when the game opened
  (`LevelEditor.cs:949`). A save the game made in DOSBox meanwhile (Run in DOSBox runs on the
  folder itself) is overwritten without a word. There is no file watcher anywhere.

**Tests:**
1. Read-only folder and read-only file: every save path (levels, strings, conversations,
   player, globals, tables, pictures, sound, EXE) gives a message naming the file; the unsaved
   edits stay unsaved (the window still says so) and a checkpoint is taken.
2. Locked file (the test opens it with `FileShare.None`): same as 1, plus the message says what
   to close.
3. Full disk: a `SafeFile` test seam (`IFileSystem` or a write hook) that throws `IOException`
   after N bytes, for N at every point of a save. Afterwards every game file is either the old
   version or the new one, byte for byte, and no `.tmp` is left.
4. Save all with a failure at each of its steps: the files written before are rolled back to
   their `.BKP` (or the message lists exactly which are saved and which are not).
5. File changed outside: open, change `LEV.ARK` behind the editor's back (another level's
   bytes), edit, save: the editor must notice and ask.
6. Long and Unicode paths: game in `C:\Jogos\Ultima Underworld — cópia\` and in a folder at
   250 characters; open, save, make a mod, play test.
7. Folder gone: delete the game folder while it is open; save, checkpoint and close must not
   crash, and the checkpoint must still be made.

**Fixes:**
- Every write through `SafeFile`; `.tmp` names unique (`NAME.EXT.<pid>.tmp`) and swept at open.
- Save all becomes a transaction: write every `.tmp` first, then replace them all; if a
  replace fails, put back the ones replaced from their `.BKP`.
- The start square, pixel editor and item slot names join the unsaved changes (and undo)
  instead of writing at once.
- Each loaded file remembers its size and time (and hash) at reading; before saving, a
  changed file is reported ("UW.EXE wrote LEV.ARK after you opened it: reload / overwrite /
  save elsewhere"). A `FileSystemWatcher` gives the same warning early.
- Write failures name the file and the reason (read-only, in use, disk full) and point to
  "Save a copy to…".

### 2.3 Undo, redo and unsaved state

**Found:** levels snapshot their whole block per step (200 per level, so memory grows with
levels × steps); when a change throws inside a batch the texture map is not rolled back
(`LevelEditor.cs:178`); strings, player, globals, object tables, game tables, schedules,
sound/music and the graphics pages have **no undo**; checkpoints do not hold pictures, fonts,
object tables, game tables, schedules, sound, BGLOBALS or zones.

**Tests:**
1. **Model-based random sessions:** a seeded script of 5,000 random editor commands (add,
   delete, move, paste, paint, fill, change fields, change level, undo, redo, undo history
   jumps, cross-level randomizer applies) run through the view-models headless. After every
   step: `LevelBlock.Check` passes on every level; after undoing everything, every level is
   byte-identical to the start; after redoing everything, identical to the state before the
   undos. Failures print the seed and the shortest failing prefix (shrinking).
2. A command that throws half way (injected) leaves the level, its texture map and its
   animation block as before.
3. Memory: 200 steps on all 80 UW2 levels stays under a set budget (measure first, then fix
   the number in the test).
4. Every unsaved-change source (each tool window) is listed by the close prompt and taken by
   Save all; every one is in a checkpoint (one test per kind, driven from a table, so a new
   window without checkpoint support fails the test).

**Fixes:** roll back the texture map in the batch's catch; undo for strings, player, globals
and tables through one small "document" undo stack per file (bytes before/after, as levels
do); store level steps as byte ranges once memory is measured, if it matters; checkpoints for
every unsaved kind.

### 2.4 Unexpected order, timing and double actions

**Breaks it:** double-clicking Save; pressing Ctrl+S while the solver or a Find in all levels
runs on another thread; closing the window while an apply runs; opening a second game during
the key shuffle; switching level during a drag; Esc during a target choice inside a paste;
dragging an object onto itself or into its own contents; deleting the selected object while
its properties have an unfinished typed value; resizing the window to 1×1; dragging the window
to a second monitor with another DPI; minimising during a first-person walk.

**Found:** background `Task.Run` in the solver (`MainWindow.Zones.cs:75`), key shuffle
(`KeyShuffleWindow.cs:238,357`), find (`FindWindow.axaml.cs:98`), conversation users and auto
zones read `GameSession`/`Archive`, which have no locks, while `ReplaceLevArk` swaps the
archive on save (`GameSession.cs:272`). UI-thread errors are marked handled and the editor
carries on, perhaps half applied (`App.axaml.cs:17-23`). `OpenSession` builds the texture
atlas outside its try (`MainWindow.axaml.cs:228`).

**Tests:** headless App tests for each pair above (start background work with a slow fake,
then do the second action); a "monkey" test that sends 10,000 random clicks, keys and drags to
the main window and every tool window with a seed, checking for unhandled errors, hangs (no
frame for 5 s) and `Check` failures; two editors on one game folder (two `GameSession`s).

**Fixes:** background jobs take an immutable snapshot of the levels they read (they already
copy in places; make it the rule) or the session gets a read/write lock; Save, Open and Close
wait for or cancel running jobs (with a message); after an unexpected UI-thread error, roll the
current command back (levels have the snapshot) and say so, instead of carrying on; the atlas
inside the try; a lock file per game folder so a second editor says "already open in another
editor"; checkpoints per instance so `Prune` cannot delete another instance's checkpoints
(`Checkpoints.cs:55,120-127`).

### 2.5 The game's own limits

**Breaks it:** filling a level to 256 mobile / 768 static objects and then pasting, scattering
keys, swapping monsters or adding a trap chain; a string block over 64 KB or 128 distinct
characters; a conversation over 65,535 words or a menu over 9 choices; UW2 level blocks that
grow; a book whose string is shared by another object, then deleted.

**Tests:** each limit reached exactly and exceeded by one, through every command that can add
(add, paste, duplicate, the randomizer's places, the item shuffle, create-object traps in
recipes): the command refuses whole (nothing half added) with the limit named. The World check
warns at 90%. A round trip of every limit case through Save and reopen.

**Fixes:** a pre-check in each multi-object command (paste already does it, `LevelEditor.cs:722`);
the randomizer's Apply counts slots per level before it starts.

### 2.6 The mod manager

**Breaks it:** the game folder moved or deleted after adding it; the vault folder deleted or a
vault file changed; two managers at once; the manager killed during Use (already recovered,
keep it tested); a mod imported twice with the same id and version but other bytes; Exhumed's
port replaced by another build; DOSBox missing; `state.json` damaged; a target whose folder is
now on a drive that is not connected.

**Tests:** one per case above; state and vault fuzz as in 2.1; kill at every write of Use
(seam as in 2.2.3) and check `Recover` gives back the exact folder. **Fixes:** vault files are
checked against their hash when read (a changed vault file is a clear error, never applied);
a lock file for the manager's home; a damaged `state.json` is kept aside and rebuilt from the
folders' `.uwmm-target.json` markers.

### 2.7 Running it all

- `tests/Robustness.Tests` in the solution, quick cases by default; the long fuzz runs (many
  thousand seeds) behind `UW_FUZZ_LONG=1`, run before each release.
- The CI runs with `UW_REQUIRE_GAMEDATA=1` where game data is available, so tests that need it
  never pass silently (108 test files check for it).
- Every crash found gets its smallest input saved under `tests/Robustness.Tests/cases/` (bytes
  generated from the test data by a recipe, never the game's own files, since the repository
  does not ship them).

---

## 3. Several mods at once, with conflicts resolved

### Where things stand

- `Target.ActiveMod` holds one mod, or none. **Play** runs the original game or one mod.
- Every delta names the SHA-256 of the exact original it applies to (`docs/mod-format.md`,
  rule 3). A second mod on top of the first is refused, because the files it would patch are
  no longer the originals.
- What makes merging possible: each delta's base is the **original**, never another mod. So
  two mods are two independent descriptions of "original → result", and the manager holds the
  original in its vault. A three-way merge (original, mod A's result, mod B's result) is
  always possible to compute; the question is only how fine the pieces are.

### Verdict

Feasible, and worth doing in three steps of rising difficulty. The common case (a portraits
mod with a music mod, a font mod with a translation, a texture pack with a level mod) changes
**different files** and needs no merging at all: only a new model and a new page in the app.
Mods that change the **same file** can be merged where the file is made of separate pieces
(archive blocks, strings, picture frames, table records), and each piece decides alone.
Objects added to the same level by two mods are the one hard case; it comes last.

### The pieces ("units") of each file

A merge splits each file into units, compares each unit across the original and the mods,
and builds the file again. Same unit changed by one mod: that mod's. Changed by several to the
same bytes: no conflict. Changed by several differently: a **conflict**.

| File | Unit | Rebuilt by | Notes |
| --- | --- | --- | --- |
| Any file changed by one mod only | the whole file | the mod's own delta | step 1; byte-exact as today |
| `SOUND/*.XMI`, `*.VOC`, CUTS `*.N0x`, CRIT animation files | the whole file | copy | each is one theme, sound, cutscene or critter |
| `UW.EXE` / `UW2.EXE` | the start square; other changed runs of bytes | `StartPosition.Patch`; byte runs | `ExeChanges` already splits the start square from the rest |
| `*.GR` (CHARHEAD, OBJECTS, PANELS…) | one frame | `GrFile` writer | a portraits mod and an icons mod meet here |
| `*.TR` (W64, F32, T64) | one texture | the texture writer | |
| `STRINGS.PAK` | one string (block, index); a block's string count | `StringsPak` writer, one new Huffman tree | added strings: appended in load order, then renumbered references are a conflict (see "coupled units") |
| `CNV.ARK` | one conversation | `Archive` writer (UW2: LZSS per block) | |
| `LEV.ARK` | step 2: one block (a level, its texture list, its automap); step 3: one tile, one object | `Archive` writer; step 3 the editor's object list operations | |
| `BYT.ARK`, `SCD.ARK` | one block | `Archive` writer | |
| `OBJECTS.DAT`, `COMOBJ.DAT`, `TERRAIN.DAT`, `SKILLS.DAT`, `SHADES.DAT`, `DL.DAT`, `SOUNDS.DAT`, `CMB.DAT` | one record | byte ranges by the record size | |
| Anything else, same length | runs of changed bytes | byte runs | the fallback |
| Anything else, length changed | the whole file | — | conflict if two mods change it |

**Coupled units** must come from the same mod, or the game breaks. The merge knows these
couplings and treats a group as one unit when it reports a conflict:
- a conversation (`CNV.ARK` block n), the string block its header names, and its NPC name
  (string `0x10 + n` of block 7);
- a level block and the strings its books, scrolls and traps point to (by string number);
- a mod's added strings and every place that names them (book links, conversation strings);
- `OBJECTS.DAT` and `COMOBJ.DAT` records of the same item;
- the start square and the level it points into (a warning only).

### Resolving conflicts

- **Load order.** The mods of a set are listed in order; for every conflict the later mod wins
  by default (as in Mod Organizer, Vortex and the Bethesda tools). Drag to reorder.
- **Per conflict.** The conflicts page lists each one in the editor's words: "Level 3, tile
  (12, 40)", "String 7:113 *Garamon*", "Portrait 42", "Conversation 5 (Bragit)", "Item 0x8A in
  OBJECTS.DAT". Each row has a choice of winner; a coupled group is one row. Choices are kept in
  the set and survive reordering.
- **Whole file.** For any file, "take it all from this mod".
- **Same change is no conflict.** Two mods that make the same change (a common bug fix) merge
  quietly.
- **Nothing silently lost.** Before Use, the manager shows what each mod loses ("Music+ loses
  2 of its 40 changes: themes 3 and 7 come from Remix"). A set with unresolved conflicts that
  the load order did not decide cannot be used; with the load order deciding, it can, and the
  page says so.
- **Checks after merging:** `LevelBlock.Check` on every merged level, every conversation
  disassembles, every string block reads, every picture reads. A failure names the units and
  the mods involved and the set is refused.

### The model

- `Target.ActiveMod` becomes `Target.Active`: either one mod (as today) or a **mod set** (an id).
- `ModSet` in the manager's state: id, name, game, the ordered mod keys, the per-conflict
  choices (`unit id → mod key`), and the hash of every merged file the last time it was built.
- `Underworld.Mods.Merge`: `Plan(originals, mods[], choices) → MergePlan` (units, conflicts,
  coupled groups, what each mod loses) and `Build(plan) → files`. Shared, so the editor can use
  it too. Pure functions over bytes, no UI.
- DOS: the merged files go through the same `ApplyPlan` and journal as one mod does, so
  "all or nothing" and crash recovery stay as they are. Exhumed: one home per set
  (`homes\<target>\set-<id>`).
- **Byte-exactness changes meaning.** One mod alone stays byte-exact with its author's save.
  A set cannot be (its files are nobody's save), so the guarantee becomes: every unit is
  byte-exact with the winning mod's unit, and the merged file reads back with the editor's
  readers. A set whose mods touch disjoint files is still byte-exact per file.
- **Export the set as one mod** (a "merged mod"): Make a mod from the merged files against the
  originals. It is a normal format 2 mod, so it can be shared or played with UltimaHacks and
  Exhumed as any other.

### Mod metadata (format 2, optional new fields, read by old managers as unknown and ignored)

- `"tags"`: what the mod is about (`portraits`, `music`, `levels`, `translation`, `fonts`,
  `balance`, `fix`), for the library's filter and for "these two should combine".
- `"load_after"` / `"load_before"`: mod ids, to sort a set by itself.
- `"conflicts"`: mod ids that the author knows do not work together.
- `"requires"`: mod ids that must be in the set (a translation patch for a level mod), each
  optionally with a lowest version (`"karl.levels >= 1.0"`), the syntax Underworld Revisited's
  `mod.json` already uses (part 4), so a `.uwmod` means the same in both programs.
- None of these change how a file is applied; the deltas stay as they are.

### The editor's side

- **Load a mod** on a game already changed: today refused file by file. With `Merge`, it can
  apply the mod's units where the open game still has the original's units, and list the
  conflicting units for a choice (same page, same words).
- **Make a mod** gains "the changes made after loading mod X only" (a patch mod), using the
  loaded mod's result as the comparison: a later step, needs a `base` that names another mod's
  result, so it is format 3 and is planned separately.

### Steps

1. **Disjoint sets (2–3 days).** `ModSet`, the set page (add mods, order them), building a set
   whose mods change different files (and the executable's start square vs other runs), Use and
   Play for DOS and Exhumed, `uwmod set create|add|order|use`. Refuse a set with any file in
   common, naming the files. Tests: portraits + music, font + strings, round trip back to the
   original game; crash in the middle of using a set is recovered.
2. **Structured merges (4–5 days).** Units for strings, pictures, textures, archives by block,
   tables by record, executable runs; coupled groups; load order; the conflicts page; checks
   after merging; export as one mod. Tests: for each file kind, two mods changing different
   units merge, the same unit conflicts, the later one wins, a per-conflict choice wins over
   the order; a coupled conversation + strings pair cannot be split; the merged result passes
   the checks; the round-trip harness (`tests/ModRoundTrip.Tests`) gets a "two mods" variant.
3. **Objects in the same level (4–6 days, optional).** Merge two mods that both edit one level:
   tiles by tile; objects matched by (index, item, position) to the original; objects a mod added
   are re-added with the editor's object list operations so they get free slots of their own;
   deleted objects stay deleted unless the other mod changed them (a conflict). Needs the
   object list operations from `UwEditor.Core/Editing` reachable from the merge (move them to
   a shared project, or let the merge call into `UwEditor.Core`, which the manager's tests
   already use). Check with the slot limits and `LevelBlock.Check`.

---

## 4. Underworld Revisited in the mod manager

### What it is (checked 2026-11-09)

- An open-source (MIT) **Unity 6 reimplementation of Ultima Underworld** by PQMarine:
  github.com/PQMarine/UnderworldRevisited, pqmarine.github.io. UW1 is complete; UW2 has been
  a preview since 0.13.0. Windows x64 and Linux x64. Releases come every day or two: **0.18.0
  "Making room" came out on 2026-11-09**. Karl's copy is an old 0.4.0
  (`OneDrive\Armazenamento\Jogos\Ultima\UnderworldRevisited`, with a source checkout of 0.4.0).
- **Program:** `UR.exe` (Linux `UR.x86_64`) with `UR_Data\`. Arguments: `-portable` and a
  language; Steam's launch option is `"…\UR.exe" %command%`.
- **Game data:** it needs GOG's `game.gog` (or Steam's `game.bin`), found in GOG folders and
  Steam libraries or chosen once (`GogInstallPath` / `GogInstallPathUw2` in its
  `settings.json`). On the first start it extracts `CRIT`, `CUTS`, `DATA`, `SOUND` and `UW.EXE`
  into `<user folder>\GameData\UW1` (marker `extracted.txt`: version, image size, time), and
  never writes the install. It refuses a `UW.EXE` other than GOG's (547,248 bytes, SHA-1
  `226fed4d…`). A loose DOS folder is not supported (only a source build can point at one).
- **User folder:** `%USERPROFILE%\AppData\LocalLow\Underworld Revisited\Underworld Revisited`
  (Linux: Unity's `~/.config/unity3d/Underworld Revisited/Underworld Revisited`), or
  `UserData\` beside the program when `portable.txt` or `UserData\` is there, or with
  `-portable` (`Assets/UWScripts/UWUserFolder.cs`). It holds `settings.json` (flat JSON),
  `graphics.json`, `Player.log`, the extracted game, `UW1\rules.json` (about 248 rules), `UW1\Music`
  (recordings by piece number replace the music) and, since 0.18.0, `Mods\`.
- **Saved games** go into the **GOG install's** `UNDEROM1\SAVE1-4` (DOS-compatible, with a
  `UWR.json` beside them). That is the one place it writes the install.
- **It reads the original formats directly:** LEV.ARK, STRINGS.PAK, CNV.ARK, OBJECTS/COMOBJ/CMB,
  `.GR`/`.BYT`, fonts, palettes, sounds, cutscenes, the 3D models from `UW.EXE`, and (since
  0.18.0) the start square from `UW.EXE`. Its plan warns that a stray file in `DATA` throws.

### Its mod support, and our `.uwmod`

From its `Docs/MOD-SUPPORT-PLAN.md` (read 2026-11-09):
- **Mods folder:** `<user folder>\Mods\<mod-id>\mod.json` (id, name, version, author,
  loadOrder, requires, description, kind) with `rules.json`, language files, table patches and
  pictures of its own; folders or zips. Enabled per installation in `settings.json`
  (`EnabledMods`, ordered), with an in-game Mods window to come. `requires` takes
  `"author.mod >= 1.0"` and loads the required mod first (after OpenXcom's `master`).
- **`.uwmod` "supported out of the box", decided 2026-11-08:** a `.uwmod` in the Mods folder is
  read as it is; its deltas are applied **in memory** to the extracted originals, base and
  result SHA-256 checked, a mod for another release refused, game files never written. The
  supported-build check runs on the original executable before the deltas, so a moved start
  square comes through.
- **But in 0.18.0 nothing reads the mods yet** (its release notes): the folder and the
  catalogue (`UWModManifest.cs` with `PackageExtension = ".uwmod"`, `UWModCatalog.cs`,
  `UWMods.cs`) exist, the delta reader is the next step.

So the right integration is **not** to write files anywhere: the manager puts the package into
Revisited's Mods folder and enables it; Revisited applies it. That keeps the install and the
extracted cache untouched, and needs no copy of the game. Writing into `GameData\UW1` would
work today for some files, but a re-extraction wipes it, an extra file can crash the loader,
and it would fight Revisited's own design. Not offered.

### The plan

**A new kind of game, `TargetKind.Revisited`** (beside Dos and Exhumed):
- **Add Underworld Revisited…** (app) / `uwmod add-revisited FOLDER [--game uw1|uw2]`: the
  folder of `UR.exe` or `UR.x86_64` with `UR_Data`. The manager works out the user folder
  (portable if `portable.txt` or `UserData\` is there, else Unity's folder for the platform) and
  shows it; a box lets the player point elsewhere.
- **Version:** read from the build (to be agreed with PQMarine: a `-features` / `--version`
  answer as Exhumed's `--features`, or a small `version.txt`; meanwhile `README.txt`'s first
  line, or `UR_Data\globalgamemanagers`' bundle version). The game page says which version and
  whether it applies `.uwmod` yet ("Revisited 0.18.0 keeps the mod but does not apply it yet;
  0.19 or later will").
- **Use a mod:** copy the `.uwmod` from the library to `Mods\uwmm-<id>@<version>.uwmod`
  (prefixed so the manager knows its own), and in `settings.json` put that id into
  `EnabledMods`, removing any other `uwmm-` entry of this game. Mods added by hand
  stay where they are, enabled or not; the manager never touches them. **The original game**
  removes the manager's entry and file.
- **`settings.json` is edited, not rewritten:** read as JSON, change only `EnabledMods`, keep
  every other key and its order, write through `Files.WriteAtomic`, and refuse (with a message)
  while Revisited is running, since it saves its settings on exit.
- **A mod set** (part 3): the merged files are exported as one `.uwmod` and that one is used, so
  Revisited never has to stack two of our packages over one file. If Revisited learns to stack
  `.uwmod`s itself, the manager could pass the ordered list instead; agree with PQMarine first.
- **Checks before Use:** the mod is for UW1 (UW2 only once Revisited's UW2 leaves preview, with a
  warning till then); the game Revisited extracted is GOG's (the hashes in `GameData\UW1` against
  the mod's bases: the same originals as the DOS release, checked on 2026-11-06); a mod whose
  executable change is more than the start square gets a note that Revisited reads only the start
  square and the 3D models from the executable.
- **Play:** `UR.exe` (with the player's start options, as for Exhumed); `-portable` when the
  manager's target is portable. The early-exit check (`Launcher.EarlyExit`) applies.
- **Saved games:** Revisited saves into the GOG install's `UNDEROM1\SAVE1-4`, shared with the DOS
  game and between mods. The game page says so; before switching mods the manager backs up the
  four slots (the existing saved-game backup), and offers to restore the mod's own slots after
  switching back (a "saves per mod" option, off by default, that swaps the slots when the mod
  changes).
- **Exhumed-like homes are not needed:** Revisited's own settings and rules stay the player's.

**Coordination with PQMarine** (an issue on their repository, after Karl agrees):
1. A machine-readable version and capability answer (`UR.exe -features`: `uwmod 2`,
   `start-square`, `uw2`, `portable`), so the manager does not guess from file names.
2. Confirm the manager may add and remove `EnabledMods` entries, and how they want the ids named.
3. What happens with two `.uwmod`s changing the same file (refuse, or the later wins per file):
   the manager will send one merged package either way.
4. Share the optional metadata fields (`tags`, `requires` with `>=` versions, `load_after`) so a
   `.uwmod` means the same in both programs; our part 3 adopts their `requires` syntax.

**Steps (2–3 days, the first two usable before Revisited applies mods):**
1. `TargetKind.Revisited`, detection, user folder, version, Add/Forget, game page, CLI (1 day).
2. Use / original game through `Mods\` and `EnabledMods`, Play, the saved-game note and backup
   (1 day). Tests: a fake Revisited folder (an empty `UR.exe`, a `settings.json` with other keys
   and another mod enabled): Use writes only our file and entry and keeps the rest byte for byte
   apart from `EnabledMods`; the original game removes them; portable and normal folders; a
   running Revisited refuses.
3. When Revisited's reader ships: a real-game test (`UWMM_TEST_REVISITED` set to a build, as
   `UWMM_TEST_UW1PORT` is for Exhumed) that starts it with a start-square mod and reads its
   `Player.log` for the square, then the manual and README.

**Related, for the editor:** **Run and Test ▸ Run in Underworld Revisited** later: make a mod of
the unsaved and saved changes in memory, drop it into a portable copy's `Mods\` and start it.
And `GogImage` could read Steam's `game.bin` (a raw CD image) as Revisited does, so the editor
and the manager also open the Steam release.

---

## 5. Study: new UW1 levels from pieces of old ones, and from maze algorithms

**The question.** How feasible is it to make new UW1 levels by stitching together areas of the
shipped levels (rotated and mirrored), joined by fillers and connecting corridors, or with new
areas built by the algorithms roguelikes use (NetHack's rooms and corridors and others)?
The levels are often laid out as four quadrants, and the randomizer's zone tools already know how
the areas connect. Such a generator would be the randomizer's next step: a "new dungeon" mode,
on top of the key item scatter and the swaps.

This part is a **study with prototypes**, not a feature yet: it ends with a verdict per approach
and a plan for whichever is worth building.

### What the game expects of a level (known now; the study completes the list)

**Geometry and textures**
- 64 × 64 tiles. A tile is solid, open, one of four diagonals or one of four slopes, with a floor
  height of 0–15 under a ceiling at 16. To check: whether the game needs an outer ring of rock
  (every shipped level seems to have one).
- Each level has its own texture map: **48 wall, 10 floor and 6 door textures**
  (`LevelModel.ReadTextureMap`). A tile names a slot, not a texture. So pieces from several
  levels must share one map: their textures together must fit 48 / 10 / 6, after renumbering
  every tile's slots. This is the hardest budget for stitching.
- Walls are textured from the top down, one texture every 4 floor steps. So a piece moved to
  another floor height can show cut textures. Keeping each piece's heights, or shifting them in
  whole steps of 4, avoids that.
- **Mirroring is safe for writing**, because the game never draws a texture mirrored: a texture
  reads left to right from whichever side you look. But pictures made of two textures side by
  side (the arched double door, wall textures 38 and 39) come out in the wrong order and must be
  swapped. The study lists every such pair.
- Water and lava are textures (`TERRAIN.DAT`), so they follow the floor slots.

**Objects**
- 256 mobile and 768 static objects, the active creature list, and the 0x180-byte animation
  list. Copying with the editor's own operations (`Copy` / `Paste`, which keep contents, chains
  and the free lists right) already handles the bookkeeping, within those limits.
- **Many fields are coordinates**, and every one must follow a rotation, a mirror or a move:
  - a trigger's target square (quality and owner);
  - teleports, and stairs to the levels above and below, with their destination square and level;
  - the change-terrain trap's area;
  - the delete trap's square;
  - a creature's home and its goal targets;
  - the start square in `UW.EXE` and the player in a save.

  The study builds the full table, field by field, from `Uw1Traps`, `Triggers` and the creature
  fields. This is also where the trap-link bug found on day 1 (§0) matters.
- **Headings** (3 bits, eighths of a turn) turn by 2 for each 90° and are reflected by a mirror.
  This includes doors, levers and switches, and writings, pictures and bridges on walls, which
  must keep facing the open side. Diagonal and slope tile types turn and reflect the same way.
- Fine positions inside a tile turn and reflect too (0–7 becomes 7 − x). For some traps the fine
  position is a setting, not a place (`Triggers.FinePositionIsData`), and must not be turned.

**Things tied to a level number or a fixed spot.** The engine keys about 25 behaviours on level
numbers, about 10 on fixed coordinates, and about 25 on fixed object, creature or conversation
ids. That is Underworld Revisited's survey (§2.3 of their mod plan); the study cross-checks it
in Exhumed's `game/GAMEWRAP.C` `do_level_hacks` and the rest. Known ones:
- level 7's orb and mana;
- level 9, the Void (no spells, no automap);
- Tybal's prison;
- Garamon's burial trigger;
- the talking door;
- the Cup of Wonder;
- the bullfrog puzzle's field on level 4;
- the volcano's centre and radius on level 8;
- the moonstone and tree levels (4-bit level numbers in `PLAYER.DAT`);
- the Pac-Man maze's glowing rocks;
- the start square.

Areas holding such a thing are **anchored**: they stay on their level at their coordinates, or
are left out of the piece library.

**Between levels.** The stairs are move triggers with teleport traps naming squares on the next
level. A generated level must keep its up and down stairs, and the neighbouring levels'
teleports must be rewritten to point at the new squares. The level's automap starts unexplored
and its map notes empty. New levels reach only new games, as with any change to `DATA\LEV.ARK`.

### The tools already there

- **Zones** (`NavRegions`, `AutoZones`): regions and edges of every kind (door, climb, drop,
  jump, walk, portcullis, secret door, stairs, teleport), plus mechanisms and levers. The solver
  checks what is reachable under requirements.
  - This is what makes generation testable: every generated level must pass the logic check.
    The start and the stairs must be reachable, with no softlock from a one-way drop, and the
    key item scatter must still find places.
  - UW1 has 685 regions, 240 zones and 496 zone links over its 9 levels (`docs/autozones-uw1.md`).
- **Place anywhere**, the item pools and the spoiler log of the randomizer, to stock new areas.
- **Testing in the game:** Check in Underworld Exhumed (the port's `goto` cheat on every level),
  the DOSBox spot test, and Play test.
- Planned in §1.2: the tile clipboard with rotate and mirror, and prefabs. These need the same
  transform layer as this study. Whichever comes first builds it, and the other reuses it.

### Approaches, from the least to the most ambitious

| | Approach | What it needs | First guess |
| --- | --- | --- | --- |
| A | **Turn or mirror a whole level** in place, and fix the stairs on the levels next to it | the transform layer only; no texture budget, no seams | feasible: bookkeeping, and the best first prototype |
| B | **Shuffle a level's own quadrants or areas** (swap them, turn them) and reconnect them | areas with few openings ("ports"), seam matching (floor height and corridor width), connectors | likely feasible where the quadrant hypothesis holds |
| C | **Stitch areas from different levels** into a new level | B, plus merging the texture maps within 48 / 10 / 6, the object budget, leaving out anchored areas, and themes so a level does not look patched | feasible with limits; the texture budget decides how many sources can meet |
| D | **New areas by algorithm**, as fillers or as whole areas | geometry generators plus UW-specific dressing (heights, slopes, doors, textures by theme, water and lava, light, monsters and loot by depth) | the geometry is easy; making it feel like Underworld is the real work |
| E | **A whole new dungeon** (levels 1–8, with the story's anchors kept and the Void as it is) | all of the above, plus every quest, conversation and fact still solvable | the end goal; decided after A–D |

Generators worth trying for D:
- **BSP rooms and corridors** (NetHack, Rogue): rooms in a split rectangle, joined by corridors.
- **Cellular automata caves**: UW1 levels 2 and 3 are cave-like.
- **Drunkard's walk** for tunnels.
- **Recursive backtracker** or **growing tree** mazes: UW1 already has mazes, for example the
  Pac-Man maze.
- **Wave function collapse** trained on the shipped levels' tiles. It learns the tile
  neighbourhoods with heights and textures, so the result looks like Underworld.
- **Heights:** rooms raised or sunk with slopes and stairs between them, the climb rule (at most
  2 steps up) checked by the zones.

### The study's steps (about 7 days)

1. **Survey the engine (1 day).** Every level-number check, fixed coordinate and fixed id in the
   game's code (Exhumed's sources, cross-checked with Revisited's survey). Every coordinate- and
   heading-carrying field, by object kind. The outer-ring question. Written to
   `docs/plans/map-generation.md`.
2. **Measure the levels (1 day).** With `NavRegions`, for each UW1 level:
   - the quadrant hypothesis: zone links crossing the 32-tile lines, and the natural cuts
     (zones joined by one or two links);
   - the pieces such cuts give: their ports, their texture use, object counts and anchors, and
     the references that leave them (triggers aiming outside, teleports).

   The report has the same shape as `docs/autozones-uw1.md`.
3. **Prototype A (1.5 days).** Turn and mirror a whole level: tiles, slots, the texture pair
   swaps, objects, headings, every coordinate field, the animation list, the stairs on the
   neighbouring levels. Tests:
   - four turns, or two mirrors, give the level back byte for byte;
   - `LevelBlock.Check` passes;
   - the turned level's zones equal the original's zones turned;
   - the turned level loads and plays in the DOSBox spot test and the Exhumed check.
4. **Prototype C (2 days).** Two areas from different levels in one level, with the texture
   maps merged and a generated connector between their ports; the logic check passes.
5. **Prototype D (1 day).** One generator (BSP rooms first, or WFC if step 2 shows the tile
   statistics are rich enough), dressed with heights, doors and textures from one level's map,
   and sparsely stocked.
6. **Verdict (0.5 day).** For each approach: what works, what the limits are (textures, objects,
   anchors, seams), a playtest note for each prototype, and a plan for a "new dungeon"
   randomizer mode, if one is worth building.

**Risks:**
- The game may assume more than the code shows: rock edges, how far creatures path, how much
  of a large open area the 3D view draws.
- Conversations may check where things are; the survey looks.
- The stairs between levels must stay consistent across the whole dungeon.
- A "patchwork" look where pieces with different texture themes meet.
- None of it ships until the zone logic check passes on every generated level and a playtest
  agrees.

---

## 6. Order

Safety first (a broken file costs a modder more than any missing feature), then Underworld
Revisited while its `.uwmod` reader is being written (its releases come daily, and part 4's
first two steps work before the reader ships), then mod sets (Revisited needs one merged
package from them), then features. Days are working days of one session each; the numbers
are estimates.

| Days | Work | Ends with |
| --- | --- | --- |
| 1 | §0 trap drag fix. §2.1 fixes in the readers (`Archive`, `StringsPak`, `StringsPakFile`, `GraphicsCodec`, free lists, XMI depth, BDF) and `GameFileException`; `settings.json.bad`; `tests/Robustness.Tests` with the truncation sweep | no reader crashes on a cut file. **Done 2026-11-09** (editor 0.1.22, manager 0.2.5); the sweep also found `GrFile.Decode` reading past a cut picture file, now refused |
| 2 | §2.1 field fuzz and level block fuzz; damage reported instead of skipped (World check, randomizer). §2.2 every write through `SafeFile`, unique `.tmp` names swept at open | fuzz runs clean on 2,000 seeds per file kind. **Done 2026-11-09** (editor 0.1.23, manager 0.2.6). The fuzz found a STRINGS.PAK block shorter than its own table, a string with code 1 taken for a menu marker in the decompiler, and object copying that grew exponentially on looping lists; all fixed, their seeds kept as cases. Found too: the conversation structurer costs 5 million steps and about 700 MB of short-lived allocations on UW1's conversation #4 (its tentative loop pass, `StructuredBuild.cs`); now capped at four times that, but worth making cheaper |
| 3 | §2.2 Save all as a transaction; the start square, pixel editor and slot names into the unsaved changes; files changed outside detected; read-only, locked, full-disk, long and Unicode path tests | no save can leave a half-written game |
| 4 | Part 4 steps 1–2: `TargetKind.Revisited`, Use and the original game through `Mods\` and `EnabledMods`, Play, saved-game note; the issue for PQMarine drafted for Karl | the manager hands Revisited a mod |
| 5–6 | Part 3 step 1 (sets of mods with no file in common), "what a mod changes in words" (§1.2, shared), export a set as one `.uwmod` | portraits + music play together, in DOS, Exhumed and Revisited |
| 7–10 | Part 3 step 2 (structured merges, conflicts page, load order, coupled units, checks after merging) | two mods on one file merge or show their conflicts |
| 11–12 | §2.3 random command sessions with shrinking; texture-map rollback; undo for strings, player, globals and tables; checkpoints for every unsaved kind | everything undoable, everything checkpointed |
| 13 | §2.4 background jobs on snapshots, Save/Open/Close wait for jobs, roll back after an unexpected error, one editor per folder, the monkey test. §2.5 limits. §2.6 manager cases | the robustness suite complete |
| 14–22 | §1.1 priority A: texts follow objects, add and delete conversations, linking, Linked from, typed editors, the Add form | the Unity gaps that matter closed |
| 23–33 | §1.2 priority A: tile clipboard with rotate and mirror, prefabs, edit all selected, find and replace, select similar, show changes, check before a mod, strings export/import, command palette; §1.3 mod details page | the big time savers |
| 34–40 | Part 5, the study of new levels (if §1.2's tile clipboard is built, its transform layer is ready; otherwise the study's step 3 builds it) | a verdict per approach, and the plan for a "new dungeon" mode |
| then | §1.1 and §1.2 priority B, §1.3 B, part 3 step 3 (objects in one level), part 4 step 3 (when Revisited applies mods), priority C as wanted | |

Release points: a beta after day 3 (safety), after day 10 (mod sets, Revisited), and after
day 33.

---

## Reminders

- **Item caches** for the randomizer (fallen-adventurer groups): planned, on hold since
  2026-11-07 until Karl decides their consequences. Prefabs (§1.2) could place them.
- **Underworld Exhumed's font range:** propose the change so fonts draw codes 1–31 and 123–127
  (accented letters) when fonts or translations come up again; the strings export (§1.2)
  will.
- **From the last handoffs, still to try by hand:** File ▸ Open GOG's game.gog with both folder
  choices; the manager's Add a DOS game on a GOG install, apply, Play, Forget; the Balance
  windows on a real mod; the Start options panel. Nothing is pushed since 0218cc1e.
- **Underworld Revisited moves fast:** check its `Docs/MOD-SUPPORT-PLAN.md` and release notes
  before part 4's day; what is written here is as of 0.18.0.
