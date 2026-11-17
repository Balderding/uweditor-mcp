The Avalonia editor for **Ultima Underworld I and II**, and now the **Ultima Underworlds Mod Manager** beside it. Self-contained builds: no .NET install needed.

| Platform | Editor | Mod manager (with the `uwmod` command line) |
|---|---|---|
| Windows 10/11 x64 | `UwEditorAvalonia-VERSION-win-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-win-x64.zip` |
| Linux x64 | `UwEditorAvalonia-VERSION-linux-x64.tar.gz` | `UltimaUnderworldsModManager-MMVERSION-linux-x64.tar.gz` |
| macOS Apple Silicon | `UwEditorAvalonia-VERSION-osx-arm64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-arm64.zip` |
| macOS Intel | `UwEditorAvalonia-VERSION-osx-x64.zip` | `UltimaUnderworldsModManager-MMVERSION-osx-x64.zip` |

**Editor:** unpack, run `UwEditorAvalonia`, then **File ▸ Open game folder** and pick the game folder, its `DATA` folder or a `SAVEn` folder. The manual is under **Help ▸ Manual** (F1), and the version under **Help ▸ About**.

**Mod manager:** unpack, run `UltimaUnderworldsModManager`, add a DOS game folder or an Underworld Exhumed program, import `.uwmod` files and pick one per game. The version is at the top of **Settings**.

### New since 0.1.21-beta
**Randomizer**
- **Key item scatter on Ultima Underworld 2**, across all nine worlds. The logic follows UW2's trap chains and variables, the castle's plot in its order, the blackrock gem's facets, what each NPC carries or gives, locked chests, Gate Travel between the two moonstones, and dying outside Britannia as a way back to the gem (an option, on at first). **This is new:** the logic is tested against the game's files, but a full game has not yet been played through on a scattered UW2. Reports are welcome.
- **Start anywhere (both games):** the game's own start, a square you give, or one the seed picks, on the first levels or anywhere. The first step of the new game teleports the player there; starts that cannot lead to the end are refused, and a start with nothing in reach gets what it needs made for it.
- **A walkthrough in the spoiler log:** the steps of one way through the scattered game, in an order that can be walked, kept out of one-way traps.
- **Where magic works:** the logic knows where spells can be cast (no mana areas, a new character's circles, runes in Britannia), with options for each.
- **Doors that can be bashed** count as a way through, as an option; spiked doors do not.
- **Settings saved and shared:** every randomizer window saves its settings, the seed included, to a file or a line of text. Example files are in `docs/examples/settings`.
- **Automatic zones** use the game's own jump (not tile counts), and know bridges, barriers and the head's height in a jump. The solver handles single-use items, zones cut off behind the player, traps and the Open spell soundly.

**Editor**
- **Try Jump:** a tool that draws a jump between two squares as the game would make it. Select, Move, Paint and Try Jump are four icon buttons.
- **Go to** box for a level and square, and a terrain preview.
- **Play test** in DOSBox or in Underworld Exhumed.
- **Strings:** export every block as JSON.

**Safety**
- Damaged game files are refused with a message naming them; a damaged object list is reported, commands on it refuse, and saving never writes a broken level. Every kind of game file was fuzzed and what that found is fixed.
- Saving uses unique temporary files, cleaned up on failure and swept when a game is opened. A damaged `settings.json` is kept as `settings.json.bad`.
- Traps and triggers keep their settings when moved.

### Beta notes
- **Back up your game** (or work through Tools ▸ Mods or the mod manager) before saving.
- Tested on Windows. The editor's Linux build has been started headless; the manager's Linux build and all macOS builds are untested.
- **macOS:** the builds are not notarised. Run `xattr -d com.apple.quarantine` on the program, then start it from Terminal (`./UwEditorAvalonia`, `./UltimaUnderworldsModManager`).
- **Linux:** the editor needs OpenGL 3.3; run `chmod +x` on the programs if the executable bit was lost.
- **Windows:** if SmartScreen warns about an unknown publisher, choose More info ▸ Run anyway.
