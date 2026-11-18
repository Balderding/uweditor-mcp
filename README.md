# uweditor-mcp: an MCP server for the Underworld Editor

This adds an [MCP](https://modelcontextprotocol.io) server to the Ultima Underworld level editor, so
an AI assistant (Claude Code, Claude Desktop or any other MCP client) can build and change levels
while you watch them change in the editor.

It is a fork of [Karl Clinckspoor's Underworld Editor](https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia),
made only for this, and the work is offered back to him
([issue #3](https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia/issues/3)). The code is on
the branch [`mcp-server`](https://github.com/Balderding/uweditor-mcp/tree/mcp-server)
([what it changes](https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia/compare/main...Balderding:uweditor-mcp:mcp-server)).

## What it does

- **Level ▸ Let an AI assistant work here (MCP)…** in the editor serves the tools on the game open
  there. Each change the assistant makes shows in the 3D view and the 2D map as it is made, and is an
  ordinary Undo step. A small window lists every call; **Stop** ends it.
- The assistant uses the editor's own code, so it gets the same checks and safety: it is refused what
  the editor refuses (an object on solid rock, a level the game could not load), and nothing is
  written until a save, which keeps the previous files as `.BKP`.
- It works through a named pipe of your own computer and user account; nothing goes over the network.

## Tools

| Tool | What it does |
|---|---|
| `level_map` | the level as text: tile types and floor heights |
| `tile_get`, `tile_set` | one tile's fields |
| `fill` | a rectangle of tiles at once (a room, a wall), one undo step |
| `item_find` | items by name or number |
| `object_list`, `object_add`, `object_move`, `object_delete` | the objects of a level |
| `recipe_teleport`, `recipe_lever_door`, `recipe_plate`, `recipe_spawner` | the editor's trap recipes |
| `picture` | the 2D map as an image |
| `check`, `undo`, `save` | the game's own checks, taking changes back, writing them |

## Using it

1. Build the `mcp-server` branch (`dotnet build -c Release`), or take the editor from it.
2. Tell your assistant to start the editor with `--mcp`. In Claude Code:

       claude mcp add uweditor -- "C:\path\to\UwEditorAvalonia.exe" --mcp

   In Claude Desktop, in `claude_desktop_config.json`:

       { "mcpServers": { "uweditor": { "command": "C:/path/to/UwEditorAvalonia.exe", "args": ["--mcp"] } } }

3. Open a game in the editor (a copy is best), choose **Level ▸ Let an AI assistant work here (MCP)…**,
   and ask the assistant to change the level. The order of these steps does not matter.

There is also a version without the editor's window (`src/UwEditor.Mcp.Server`), which opens a game
folder by itself. The full details are in
[src/UwEditor.Mcp/README.md](https://github.com/Balderding/uweditor-mcp/blob/mcp-server/src/UwEditor.Mcp/README.md)
on the `mcp-server` branch.

Tested on Windows with Ultima Underworld I. The editor and its licence (MIT) are Karl Clinckspoor's.
