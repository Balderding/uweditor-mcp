# UwEditor.Mcp: the editor's tools for AI assistants (MCP)

The editor's level editing as [Model Context Protocol](https://modelcontextprotocol.io) tools, so an AI
assistant (Claude, or any other MCP client) can read and change Ultima Underworld levels with the
editor's own core (`UwEditor.Core`): the same consistency checks, the same undo steps, the same safe
save (`.BKP` / `.ORIG`).

There are two ways to use it:

- **In the editor, watching (Level ▸ Let an AI assistant work here (MCP)…).** The tools work on the game
  open in the editor: each change shows in the views as it is made and goes in Undo; the view stays
  where you put it. The window lists each call; closing it (or Stop) stops serving. The client starts
  the editor itself as a pass-through, `UwEditorAvalonia --mcp`, which reaches the open window through a
  named pipe (`uweditor-mcp`, this computer and this user only). Without the window open it says so on
  stderr and stops (exit code 1). `open` is refused here: open games in the editor.
- **On its own, without a window (`src/UwEditor.Mcp.Server`).** A console program over stdio that opens
  a game folder itself. `--only FOLDER` makes it refuse every other folder: use it to keep the server
  on a copy of the game.

## Setting up a client

A client is given a command to start. For example, in a Claude Code `.mcp.json` (or `claude mcp add`):

    { "mcpServers": { "uweditor": { "command": "<path>/UwEditorAvalonia", "args": ["--mcp"] } } }

or, for the program on its own:

    dotnet build src/UwEditor.Mcp.Server -c Release
    { "mcpServers": { "uweditor": { "command": "dotnet",
        "args": ["<path>/UwEditor.Mcp.Server.dll", "--only", "<a copy of the game>"] } } }

(`UwEditor.Mcp.Server --app` is the same pass-through as the editor's `--mcp`.)

## Tools

| Tool | What it does |
|---|---|
| `open` | opens a game folder (the one holding DATA); lists its level slots (in use, empty, damaged). Refuses while there are unsaved changes, unless `discard` |
| `level_map` | a level as text: a 64x64 grid of tile types, then one of floor heights; north at the top |
| `tile_get` | one tile's fields, its texture numbers and its object count |
| `tile_set` | one field of a tile (a floor height carries the tile's objects with it) |
| `fill` | a rectangle of tiles at once, one undo step; one bad value changes nothing |
| `item_find` | items by number or part of their name |
| `object_list` | the objects on a tile or the whole level, with their contents |
| `object_add` / `object_move` / `object_delete` | objects, with the editor's defaults for each item |
| `recipe_teleport` | the Trap recipes' teleport (with a way back if asked) |
| `recipe_lever_door` | a new lever on a wall that opens a door |
| `recipe_plate` | stepping on a square does damage or opens a door |
| `recipe_spawner` | wandering monsters |
| `picture` | the 2D map as a PNG, the whole level or a rectangle |
| `check` | the game's own checks on a level as it is now |
| `save` | writes the changed levels (in the editor: as Ctrl+S); refuses a level that fails the checks |

Map characters: `#` solid, `.` open, `1`-`4` diagonal open SE SW NE NW, `n s e w` slope rising N S E W.
Heights `0`-`f`. Levels are numbered from 1, tiles 0-63 (x west to east, y south to north). Empty or
damaged level slots are refused for changes.

A refusal (a bad value, no game open, a folder not allowed) is answered as text starting `Refused:`.

## Tests

`tests/UwEditor.Mcp.Tests` (and `McpWindowTests` in `tests/UwEditor.App.Tests`) work on fresh copies of
UW1's `DATA` from `UW_GAMEDATA` or `tests/gamedata`, as the core tests do; the copies go under
`UW_MCP_TMP` (or a `gamecopies` folder beside the tests) and the next run clears copies older than 10
minutes. Without the game files they pass without checking.
