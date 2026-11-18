# uweditor-mcp: an MCP server for the Underworld Editor

This adds an [MCP](https://modelcontextprotocol.io) server to the Ultima Underworld level editor, so
an AI assistant (Claude Code, Claude Desktop or any other MCP client) can build and change levels
while you watch them change in the editor.

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
- Each change the assistant makes shows in the 3D view and the 2D map as it is made, and is an
  ordinary Undo step. The view stays where you put it.
- A small window lists every call. **Stop** ends it, and anything asked after that is refused.
- The assistant goes through the editor's own code, so it gets the same checks:
  - It is refused whatever the editor refuses: an object on solid rock, a level the game could not load,
    an empty or damaged level slot.
  - Nothing is written until a save. The save is the editor's own (Ctrl+S), and it keeps the previous
    files as `.BKP`.
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
   and ask the assistant to change the level.

## The tools

| Tool | What it does |
|---|---|
| `level_map` | the level as text: a 64x64 grid of tile types, then one of floor heights; north at the top |
| `tile_get`, `tile_set` | one tile's fields: type, floor height, textures, light, no magic, door. A raised floor carries its objects with it |
| `fill` | a rectangle of tiles at once (a room, a wall), one undo step. One bad value changes nothing |
| `item_find` | items by name or number |
| `object_list`, `object_add`, `object_move`, `object_delete` | the objects of a level, with the editor's defaults for each item |
| `recipe_teleport` | a teleport from one square to another (any level), with a way back if asked |
| `recipe_lever_door` | a new lever on a wall that opens a door |
| `recipe_plate` | stepping on a square does damage or opens a door |
| `recipe_spawner` | wandering monsters |
| `picture` | the 2D map as an image, the whole level or a rectangle |
| `check` | the game's own checks on a level |
| `undo` | takes back a level's last changes |
| `save` | writes the changed levels |

A refusal comes back as an error result starting `Refused:`, with the reason.

## What we added, file by file

### `src/UwEditor.Mcp/` — the tools (a library, no window)

| File | Lines | What it is |
|---|---:|---|
| [`McpEditor.cs`](src/UwEditor.Mcp/McpEditor.cs) | 139 | The game it works on, which is either a folder it opens itself or the game open in the editor. Also the guards: one folder only (`--only`), no unsaved edits lost, no empty or damaged slots |
| [`McpEditor.Tiles.cs`](src/UwEditor.Mcp/McpEditor.Tiles.cs) | 100 | `level_map`, `tile_get`, `tile_set` |
| [`McpEditor.Fill.cs`](src/UwEditor.Mcp/McpEditor.Fill.cs) | 80 | `fill`: every value checked first, then one change of the level |
| [`McpEditor.Objects.cs`](src/UwEditor.Mcp/McpEditor.Objects.cs) | 147 | `item_find` and the object tools |
| [`McpEditor.Recipes.cs`](src/UwEditor.Mcp/McpEditor.Recipes.cs) | 104 | The trap recipes, over the editor's own `TrapRecipes` |
| [`McpEditor.Picture.cs`](src/UwEditor.Mcp/McpEditor.Picture.cs) | 33 | `picture`, from the editor's 2D map |
| [`McpEditor.Save.cs`](src/UwEditor.Mcp/McpEditor.Save.cs) | 44 | `check` and `save` |
| [`EditorTools.cs`](src/UwEditor.Mcp/EditorTools.cs) | 214 | The tools as the assistant sees them: names, descriptions, parameters. Each call runs one at a time, on the editor's thread when it runs in the editor |
| [`PipeHost.cs`](src/UwEditor.Mcp/PipeHost.cs) | 139 | Serves the tools on the pipe for the editor's window, one assistant at a time |
| [`PipeRelay.cs`](src/UwEditor.Mcp/PipeRelay.cs) | 174 | `UwEditorAvalonia --mcp`: the server the assistant starts. It lists the tools and passes each call on to the window |
| [`README.md`](src/UwEditor.Mcp/README.md) | 69 | The details |
| [`UwEditor.Mcp.csproj`](src/UwEditor.Mcp/UwEditor.Mcp.csproj) | 17 | Uses the official C# MCP SDK (`ModelContextProtocol.Core` 2.3.0) |

### `src/UwEditor.Mcp.Server/` — the same tools without the editor's window

| File | Lines | What it is |
|---|---:|---|
| [`Program.cs`](src/UwEditor.Mcp.Server/Program.cs) | 72 | A console MCP server that opens a game folder by itself (`--only FOLDER` keeps it to one) |
| [`UwEditor.Mcp.Server.csproj`](src/UwEditor.Mcp.Server/UwEditor.Mcp.Server.csproj) | 15 | |

### `src/UwEditor.App/` — in the editor

| File | Lines | What it is |
|---|---:|---|
| [`McpWindow.cs`](src/UwEditor.App/McpWindow.cs) | 110 | The Let an AI assistant work here window: whether an assistant is connected, the game it works on, the list of calls, and Stop |
| [`MainWindow.Mcp.cs`](src/UwEditor.App/MainWindow.Mcp.cs) | 21 | Opens that window from the Level menu |

### Tests

| File | Lines | What it tests |
|---|---:|---|
| [`tests/UwEditor.Mcp.Tests/`](tests/UwEditor.Mcp.Tests) | 1,240 | 66 tests on copies of UW1's game files: every tool, the guards, the pipe, the go-between started in either order, the console server |
| [`tests/UwEditor.App.Tests/McpWindowTests.cs`](tests/UwEditor.App.Tests/McpWindowTests.cs) | 89 | The window in the real editor (headless): a call through the pipe changes the open level, and a call through `--mcp` reaches the window |

### The few lines changed in his files ([`changes-to-existing-files.patch`](changes-to-existing-files.patch))

| File | Change |
|---|---|
| `src/UwEditor.App/MainWindow.axaml` | 1 line: the menu item |
| `src/UwEditor.App/UwEditor.App.csproj` | 1 line: the reference to `UwEditor.Mcp` |
| `src/UwEditor.App/Program.cs` | 2 lines: `--mcp` starts the go-between instead of the window |
| `UltimaUnderworldEditorAvalonia.sln` | the three new projects |
| `Directory.Build.props` | the version, 0.1.55 to 0.1.56 |
| `README.md`, `docs/MANUAL.md` | a section each on the MCP server and how to set it up |

## Tested

- All 66 tests and the window test pass.
- A full run of every tool, done the way an assistant would, builds and saves a room with 0 failures.
- The editor's own tests still pass: 891 core and 263 interface tests.
- The single-file release build works as `--mcp`.

All of this is on Windows with Ultima Underworld I. Linux, macOS and Ultima Underworld II are not
tested yet.

The editor and its licence (MIT) are Karl Clinckspoor's. The files here are released under the same
licence.
