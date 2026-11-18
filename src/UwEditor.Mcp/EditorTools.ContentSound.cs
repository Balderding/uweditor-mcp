using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>Sound and music, 3D models and mods.</summary>
    public sealed partial class EditorTools
    {
        [McpServerTool(Name = "sound_list"), Description("The SOUND folder's .VOC files (speech, and in UW2 digital effects and trap sounds) with length, rate, size against the game's limit and their cutscene line; and SOUNDS.DAT. Nothing is ever played: sound_export writes WAV files.")]
        public CallToolResult SoundList() { return Answer(() => ed.SoundList()); }

        [McpServerTool(Name = "sound_export"), Description("Writes a .VOC (unsaved changes included) as an 8-bit mono WAV into a folder, or every sound file with name all.")]
        public CallToolResult SoundExport([Description("The file (12.VOC, BSP05.VOC, SP10.VOC, UW03.VOC) or all.")] string name, [Description("The folder to write into.")] string folder)
        {
            return Answer(() => ed.SoundExport(name, folder));
        }

        [McpServerTool(Name = "sound_import"), Description("Replaces (or adds) a .VOC from a WAV file, converted as the sound window does (mono, silence trimmed, resampled, normalised, dithered to 8 bits) and checked against the game's limit for that file. Not written until save.")]
        public CallToolResult SoundImport([Description("The file (12.VOC; UW2 BSPnn, SPnn, UWnn).")] string name, [Description("The WAV file.")] string wav,
            [Description("Normalise (default true).")] bool? normalise = null, [Description("Trim silence (default true).")] bool? trimSilence = null,
            [Description("Dither (default true).")] bool? dither = null,
            [Description("Over the game's limit: refuse (default), shorten or lowerRate.")] string overLimit = "refuse")
        {
            return Answer(() => ed.SoundImport(name, wav, normalise, trimSilence, dither, overLimit));
        }

        [McpServerTool(Name = "sound_revert"), Description("Drops a sound file's unsaved change; with original, the file goes back to the original (the .ORIG the editor kept, or none for one it added) on save.")]
        public CallToolResult SoundRevert([Description("The file.")] string name, [Description("Back to the original, not only the unsaved change dropped.")] bool original = false)
        {
            return Answer(() => ed.SoundRevert(name, original));
        }

        [McpServerTool(Name = "sound_effects"), Description("SOUNDS.DAT's effects: patch, note, volume, length (UW2: digital, priority), what the game plays each for, and the creatures whose steps or death cry it is.")]
        public CallToolResult SoundEffects() { return Answer(() => ed.SoundEffects()); }

        [McpServerTool(Name = "sound_effect_set"), Description("Sets one field of a SOUNDS.DAT effect: patch, note, volume (0-255), length (0-65535); UW2 also digital (0/1: plays SPnn.VOC) and priority. Not written until save.")]
        public CallToolResult SoundEffectSet([Description("The effect's number.")] int effect, [Description("patch, note, volume, length, digital or priority.")] string field, [Description("The value.")] int value)
        {
            return Answer(() => ed.SoundEffectSet(effect, field, value));
        }

        [McpServerTool(Name = "music_list"), Description("The music themes: when the game plays each, its FM and MT-32 files (.XMI) with size and the errors and warnings checking them against the game finds.")]
        public CallToolResult MusicList() { return Answer(() => ed.MusicList()); }

        [McpServerTool(Name = "music_check"), Description("Checks a theme's files against the game (notes at once, size, loops, timbres) and lists every problem.")]
        public CallToolResult MusicCheck([Description("The theme's number.")] int theme, [Description("fm, mt32 or both (default both).")] string version = "both")
        {
            return Answer(() => ed.MusicCheck(theme, version));
        }

        [McpServerTool(Name = "music_export"), Description("Writes a theme (or all) as standard MIDI files into a folder.")]
        public CallToolResult MusicExport([Description("The theme's number, or all.")] string theme, [Description("The folder.")] string folder,
            [Description("fm, mt32 or both (default both).")] string version = "both")
        {
            return Answer(() => ed.MusicExport(theme, version, folder));
        }

        [McpServerTool(Name = "music_import"), Description("Makes a theme's files from a MIDI file, as the music panel's Import: converted to the game's XMI and checked; refused, changing nothing, when one has an error. Not written until save.")]
        public CallToolResult MusicImport([Description("The theme's number.")] int theme, [Description("The MIDI file.")] string midi,
            [Description("A separate MIDI for the MT-32 version (its programs kept).")] string mt32Midi = null,
            [Description("fm, mt32 or both (default both).")] string version = "both",
            [Description("Loop the whole theme.")] bool? loopWholeTheme = null, [Description("Map General MIDI programs to the MT-32's (default true).")] bool? mapGmToMt32 = null,
            [Description("Move channels the game keeps for effects (default true).")] bool? moveChannels = null,
            [Description("Replace drums the game lacks (default true).")] bool? replaceMissingDrums = null)
        {
            return Answer(() => ed.MusicImport(theme, midi, mt32Midi, version, loopWholeTheme, mapGmToMt32, moveChannels, replaceMissingDrums));
        }

        [McpServerTool(Name = "music_revert"), Description("Puts a theme's files back as the originals (the .ORIG copies the editor kept, or the files on disk), written on save.")]
        public CallToolResult MusicRevert([Description("The theme's number.")] int theme, [Description("fm, mt32 or both (default both).")] string version = "both")
        {
            return Answer(() => ed.MusicRevert(theme, version));
        }

        [McpServerTool(Name = "model_list"), Description("The built-in 3D models of UW.EXE / UW2.EXE (Tools ▸ 3D models): name, vertices, faces, bytes, and the items drawn with each. They are code in the executable: read only.")]
        public CallToolResult ModelList() { return Answer(() => ed.ModelList()); }

        [McpServerTool(Name = "model_get"), Description("One built-in model: its extent, the items drawn with it, every vertex (x y z, z up, a tile = 1) and face (its vertices, palette colour or textured).")]
        public CallToolResult ModelGet([Description("The model's number.")] int index) { return Answer(() => ed.ModelGet(index)); }

        [McpServerTool(Name = "model_export"), Description("Writes a built-in model (or every one) as Wavefront OBJ with an MTL of its palette colours into a folder.")]
        public CallToolResult ModelExport([Description("The folder.")] string folder, [Description("The model's number; leave out for all.")] int? index = null)
        {
            return Answer(() => ed.ModelExport(index, folder));
        }

        [McpServerTool(Name = "mod_changes"), Description("The saved changes of the game folder a mod would carry: against the .ORIG copies the editor kept, or a folder of an untouched copy; with the problems that stop them being a mod.")]
        public CallToolResult ModChanges([Description("A folder of an untouched copy of the game (default: the .ORIG copies).")] string reference = null)
        {
            return Answer(() => ed.ModChanges(reference));
        }

        [McpServerTool(Name = "mod_make"), Description("Packs the saved changes into a .uwmod (only the differences, nothing of the original files), after the game's checks; the mod manager and mod_apply apply it to the same release.")]
        public CallToolResult ModMake([Description("The .uwmod file to write.")] string path, [Description("An id, e.g. me.bettertraps.")] string id = "my.mod",
            [Description("The mod's name.")] string name = "My mod", [Description("Its version.")] string version = "1.0.0",
            [Description("What it does.")] string description = "", [Description("Authors, comma-separated.")] string authors = "",
            [Description("A folder of an untouched copy to compare with (default: the .ORIG copies).")] string reference = null)
        {
            return Answer(() => ed.ModMake(path, id, name, version, description, authors, reference));
        }

        [McpServerTool(Name = "mod_zip"), Description("Writes the changed files, whole, into a .zip at their game folders (for one's own copy of the game: not for sharing, it holds game data).")]
        public CallToolResult ModZip([Description("The .zip file to write.")] string path, [Description("A folder of an untouched copy (default: the .ORIG copies).")] string reference = null)
        {
            return Answer(() => ed.ModZip(path, reference));
        }

        [McpServerTool(Name = "mod_check"), Description("Reads a .uwmod: its name, files and size, and for each file whether the open game has the original it was made from (it applies), its result already, or neither.")]
        public CallToolResult ModCheck([Description("The .uwmod file.")] string path) { return Answer(() => ed.ModCheck(path)); }

        [McpServerTool(Name = "mod_apply"), Description("Applies a .uwmod to the open game (every file checked first; each original kept as .ORIG), then opens the game again. Refused while there are unsaved changes.")]
        public CallToolResult ModApply([Description("The .uwmod file.")] string path) { return Answer(() => ed.ModApply(path)); }
    }
}
