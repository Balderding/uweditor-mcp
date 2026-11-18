using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>A game copy with more than DATA: the SOUND, CRIT and CUTS folders and UW.EXE as asked (the content tools read them).</summary>
    static class ContentCopy
    {
        public static string Make(bool sound = false, bool crit = false, bool cuts = false, bool exe = false)
        {
            string g = GameCopy.Make();
            if (g == null) return null;
            string root = Environment.GetEnvironmentVariable("UW_GAMEDATA");
            if (string.IsNullOrEmpty(root)) return g;
            string game = Path.Combine(root, "UW");
            if (sound) Copy(Path.Combine(game, "SOUND"), Path.Combine(g, "SOUND"));
            if (crit) Copy(Path.Combine(game, "CRIT"), Path.Combine(g, "CRIT"));
            if (cuts) Copy(Path.Combine(game, "CUTS"), Path.Combine(g, "CUTS"));
            if (exe && File.Exists(Path.Combine(game, "UW.EXE"))) File.Copy(Path.Combine(game, "UW.EXE"), Path.Combine(g, "UW.EXE"));
            return g;
        }

        static void Copy(string from, string to)
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        }

        public static McpEditor Open(string g) { var ed = new McpEditor(g); ed.Open(g); return ed; }

        public static int FirstSlot(McpEditor ed)
        {
            var m = Regex.Match(ed.ConversationList(false), @"^(\d+): ", RegexOptions.Multiline);
            return int.Parse(m.Groups[1].Value);
        }
    }

    /// <summary>Conversations: read, compile and apply, simulate, graph, add, saved and read back.</summary>
    public class ContentConversationTests
    {
        [Fact]
        public void List_get_and_graph()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            string list = ed.ConversationList(false);
            Assert.Contains("conversations in", list);
            int slot = ContentCopy.FirstSlot(ed);
            Assert.Contains("say ", ed.ConversationGet(slot, "dialogue"));
            Assert.Contains(".slot", ed.ConversationGet(slot, "script"));
            Assert.Contains("0: ", ed.ConversationGet(slot, "strings"));
            Assert.Contains("->", ed.ConversationGraph(slot, true));
            Assert.Throws<ArgumentException>(() => ed.ConversationGet(slot, "nope"));
            Assert.ThrowsAny<Exception>(() => ed.ConversationGet(9999, "dialogue"));
        }

        [Fact]
        public void Set_saved_and_read_back()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            int slot = ContentCopy.FirstSlot(ed);
            string src = ed.ConversationGet(slot, "dialogue");
            src = src.Substring(src.IndexOf("\n\n", StringComparison.Ordinal) + 2);
            var say = Regex.Match(src, "say \"([^\"]+)\"");
            Assert.True(say.Success);
            string changed = src.Substring(0, say.Index) + "say \"Greetings from the test.\"" + src.Substring(say.Index + say.Length);
            Assert.Contains("compiles", ed.ConversationCheck(slot, changed));
            Assert.Contains("applied", ed.ConversationSet(slot, changed));
            Assert.Contains("changed, not saved", ed.ConversationList(false));
            Assert.Contains("Greetings from the test.", ed.ConversationGet(slot, "dialogue"));
            string said = ed.Save();
            Assert.Contains("CNV.ARK", said);
            Assert.Equal("Nothing to save.", ed.Save());

            var again = ContentCopy.Open(g);
            Assert.Contains("Greetings from the test.", again.ConversationGet(slot, "dialogue"));
            Assert.Contains("Greetings from the test.", again.ConversationSimulate(slot, null, null, 0, null, null, null, null, null, null, null, 500000, false) + again.ConversationGet(slot, "strings"));
        }

        [Fact]
        public void Bad_source_refused_and_undo()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            int slot = ContentCopy.FirstSlot(ed);
            Assert.Contains("does not compile", ed.ConversationCheck(slot, "main() { say "));
            Assert.Throws<McpEditorException>(() => ed.ConversationSet(slot, "main() { say "));
            Assert.StartsWith("Refused: ", EditorTools.Text(new EditorTools(ed).ConversationSet(slot, "main() { say ")));
            ed.ConversationStringSet(slot, 0, "A changed string.");
            Assert.Contains("A changed string.", ed.ConversationGet(slot, "strings"));
            Assert.Contains("Undid", ed.ConversationUndo(slot));
            Assert.DoesNotContain("A changed string.", ed.ConversationGet(slot, "strings"));
            Assert.Throws<McpEditorException>(() => ed.ConversationUndo(slot));
            Assert.Equal("Nothing to save.", ed.Save());
        }

        [Fact]
        public void Added_conversation_simulates()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            string said = ed.ConversationAdd(null, "Testy", "Hello there.", "Bye.", "So long.");
            int slot = int.Parse(Regex.Match(said, @"Conversation (\d+) added").Groups[1].Value);
            string first = ed.ConversationSimulate(slot, null, null, 0, null, null, null, null, null, null, null, 500000, false);
            Assert.Contains("NPC: Hello there.", first);
            Assert.Contains("WAITING FOR A CHOICE", first);
            Assert.Contains("1. Bye.", first);
            string run = ed.ConversationSimulate(slot, "1", null, 0, null, null, null, null, null, null, null, 500000, false);
            Assert.Contains("So long.", run);
            Assert.Contains("ENDED", run);
            Assert.Contains("Testy", ed.ConversationList(false));
            Assert.Contains("CNV.ARK", ed.Save());
            Assert.Contains("Testy", ContentCopy.Open(g).ConversationList(false));
        }

        [Fact]
        public void Strings_shared_with_string_set()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            ed.StringSet("4", 0x45, "test beast");
            Assert.Throws<McpEditorException>(() => ed.ConversationList(false));
            ed.Save();
            int slot = ContentCopy.FirstSlot(ed);
            var m = Regex.Match(ed.ConversationGet(slot, "dialogue"), @"String block 0x([0-9A-F]{4})");
            ed.ConversationStringSet(slot, 0, "Shared string.");
            Assert.Contains("Shared string.", ed.StringGet("0x" + m.Groups[1].Value, 0));
            string said = ed.Save();
            Assert.Contains("STRINGS.PAK", said);
            Assert.Contains("Shared string.", ContentCopy.Open(g).ConversationGet(slot, "strings"));
            Assert.Contains("test beast", ContentCopy.Open(g).StringGet("4", 0x45));
        }
    }

    /// <summary>The balance overviews and the barter simulator.</summary>
    public class ContentBalanceTests
    {
        [Fact]
        public void Overviews_and_barter()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            string w = ed.BalanceWeapons("sword=20, strength=25", "0x45", null, null, false, 0.3, false);
            Assert.Contains("MELEE", w);
            Assert.Contains("LAUNCHERS", w);
            Assert.Contains("Armour at quality 40", ed.BalanceArmour(null, null, 40, 10, 12, false));
            Assert.Throws<ArgumentException>(() => ed.BalanceWeapons("nonsense=3", null, null, null, false, 0.3, false));
            Assert.Contains("level:index", ed.BarterTraders());
            string b = ed.BarterSimulate("0xA0 x 10", "0x00", "0x41", "charm=10", null, "greed");
            Assert.Contains("Worth: ", b);
            Assert.Throws<ArgumentException>(() => ed.BarterSimulate("", "", "0x41", null, null, "none"));
        }
    }

    /// <summary>Pictures, palettes, fonts, critters and cutscenes.</summary>
    public class ContentGraphicsTests
    {
        [Fact]
        public void Picture_drawn_saved_and_read_back()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            Assert.Contains("OBJECTS.GR", ed.GraphicsFiles());
            Assert.Contains("0: ", ed.GraphicsList("OBJECTS.GR", 0, 10));
            string said;
            var png = ed.GraphicsImagePng("OBJECTS.GR", 1, null, null, null, out said);
            Assert.Equal(0x89, png[0]);
            Assert.Contains("Picture 1 of OBJECTS.GR", said);
            Assert.NotNull(ed.GraphicsSheetPng("OBJECTS.GR", 0, 32, null, 1, out said));
            string pixels = ed.GraphicsPixels("OBJECTS.GR", 1, null, 0, 0, null, null);
            Assert.Contains("Colours used", pixels);
            Assert.Contains("set to", ed.GraphicsDraw("OBJECTS.GR", 1, null, "rect", "0,0;3,3", "5", 1, true));
            Assert.Contains("OBJECTS.GR", ed.Save());
            Assert.True(File.Exists(Path.Combine(g, "DATA", "OBJECTS.GR.BKP")));
            var again = ContentCopy.Open(g);
            Assert.StartsWith("05 05 05 05", again.GraphicsPixels("OBJECTS.GR", 1, null, 0, 0, 4, 1).Split('\n')[1]);
        }

        [Fact]
        public void Picture_export_and_import()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            string folder = Path.Combine(g, "out");
            string said;
            Assert.Null(ed.GraphicsImagePng("W64.TR", 0, null, null, folder, out said));
            string file = Directory.GetFiles(folder, "*.png").Single();
            Assert.Contains("replaced", ed.GraphicsImport("W64.TR", 1, file, false, null));
            Assert.Contains("are back as saved", ed.GraphicsRevert("W64.TR", null));
            Assert.Throws<McpEditorException>(() => ed.GraphicsImport("W64.TR", 1, Path.Combine(folder, "none.png"), false, null));
            Assert.Throws<McpEditorException>(() => ed.GraphicsList("NOPE.GR", 0, 10));
            Assert.Contains("Palette 0", ed.PaletteGet(0, null));
            Assert.Contains("Auxiliary palette 3", ed.PaletteGet(0, 3));
            Assert.NotNull(ed.PalettePng(0, out said));
            Assert.Equal("Nothing to save.", ed.Save());
        }

        [Fact]
        public void Image_tool_answers_a_picture()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var tools = new EditorTools(ContentCopy.Open(g));
            var r = tools.GraphicsImage("OBJECTS.GR", 1);
            Assert.Contains(r.Content, c => c is ImageContentBlock);
            Assert.StartsWith("Refused: ", EditorTools.Text(tools.GraphicsImage("OBJECTS.GR", 99999)));
        }

        [Fact]
        public void Fonts()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            Assert.Contains("FONT5X6P.SYS", ed.FontList());
            string said;
            Assert.NotNull(ed.FontPng("FONT5X6P.SYS", null, null, null, out said));
            Assert.NotNull(ed.FontPng("FONT5X6P.SYS", "Hello", null, null, out said));
            string folder = Path.Combine(g, "fonts");
            Assert.Null(ed.FontPng("FONTBIG.SYS", null, null, folder, out said));
            Assert.Contains("replaced", ed.FontImport("FONTBIG.SYS", Path.Combine(folder, "FONTBIG.png")));
            string glyph = ed.FontGlyph("FONT5X6P.SYS", 'A', "0,0,1", null);
            Assert.StartsWith("#", glyph.Split('\n')[1]);
            string saved = ed.Save();
            Assert.Contains("FONT5X6P.SYS", saved);
            Assert.StartsWith("#", ContentCopy.Open(g).FontGlyph("FONT5X6P.SYS", 'A', null, null).Split('\n')[1]);
        }

        [Fact]
        public void Critters_and_cutscenes()
        {
            string g = ContentCopy.Make(crit: true, cuts: true); if (g == null) return;
            if (!Directory.Exists(Path.Combine(g, "CRIT"))) return;
            var ed = ContentCopy.Open(g);
            string list = ed.CritterList();
            Assert.Contains("pages", list);
            Assert.Contains("every frame", ed.CritterAnimations(0));
            var m = Regex.Match(ed.CritterAnimations(0), @"every frame: (\d+):(\d+)");
            int page = int.Parse(m.Groups[1].Value), frame = int.Parse(m.Groups[2].Value);
            string said, folder = Path.Combine(g, "crit-out");
            Assert.NotNull(ed.CritterImagePng(0, page, frame, null, null, null, out said));
            Assert.Null(ed.CritterImagePng(0, page, frame, null, null, folder, out said));
            Assert.Contains("replaced", ed.CritterImport(0, page, frame, Directory.GetFiles(folder).Single(), false, null));
            Assert.Contains("CRIT\\", ed.Save());

            Assert.Contains("CS000.N01", ed.CutsceneList());
            Assert.NotNull(ed.CutsceneFramePng("CS000.N01", 0, null, null, out said));
            string cf = Path.Combine(g, "cut-out");
            Assert.Null(ed.CutsceneFramePng("CS000.N01", 0, null, cf, out said));
            Assert.Contains("replaced", ed.CutsceneImport("CS000.N01", 0, Directory.GetFiles(cf).Single(), false));
            Assert.Contains("CUTS\\CS000.N01", ed.Save());
        }
    }

    /// <summary>Sound, music, models and mods.</summary>
    public class ContentSoundTests
    {
        [Fact]
        public void Sound_exported_imported_and_saved()
        {
            string g = ContentCopy.Make(sound: true); if (g == null) return;
            if (!Directory.Exists(Path.Combine(g, "SOUND"))) return;
            var ed = ContentCopy.Open(g);
            Assert.Contains("00.VOC", ed.SoundList());
            string folder = Path.Combine(g, "wav");
            Assert.Contains("00.WAV", ed.SoundExport("00.VOC", folder));
            Assert.Contains("replaced", ed.SoundImport("01", Path.Combine(folder, "00.WAV"), null, null, null, "shorten"));
            Assert.Contains("patch", ed.SoundEffects());
            Assert.Contains("volume 100", ed.SoundEffectSet(0, "volume", 100));
            Assert.Throws<ArgumentException>(() => ed.SoundEffectSet(0, "nope", 1));
            string said = ed.Save();
            Assert.Contains("SOUND\\01.VOC", said);
            Assert.Contains("SOUNDS.DAT", said);
            Assert.True(File.Exists(Path.Combine(g, "SOUND", "01.VOC.BKP")));
            Assert.Contains("volume 100", ContentCopy.Open(g).SoundEffects().Split('\n')[1]);
        }

        [Fact]
        public void Music_exported_and_imported()
        {
            string g = ContentCopy.Make(sound: true); if (g == null) return;
            if (!Directory.Exists(Path.Combine(g, "SOUND"))) return;
            var ed = ContentCopy.Open(g);
            string list = ed.MusicList();
            int theme = int.Parse(Regex.Match(list, @"^(\d+): ", RegexOptions.Multiline).Groups[1].Value);
            Assert.Contains(":", ed.MusicCheck(theme, "both"));
            string folder = Path.Combine(g, "midi");
            Assert.Contains(".mid", ed.MusicExport(theme.ToString(), "fm", folder));
            string mid = Directory.GetFiles(folder, "*.mid").First();
            try { Assert.Contains("imported", ed.MusicImport(theme, mid, null, "fm", null, null, null, null)); }
            catch (McpEditorException e) { Assert.Contains("Not imported", e.Message); return; }
            bool changed = ed.MusicList().Contains("changed, not saved");
            Assert.Equal(changed, ed.Save().Contains("SOUND\\"));
        }

        [Fact]
        public void Models()
        {
            string g = ContentCopy.Make(exe: true); if (g == null) return;
            if (!File.Exists(Path.Combine(g, "UW.EXE"))) return;
            var ed = ContentCopy.Open(g);
            Assert.Contains("vertices", ed.ModelList());
            Assert.Contains("Vertices", ed.ModelGet(1));
            string folder = Path.Combine(g, "obj");
            Assert.Contains(".obj", ed.ModelExport(null, folder));
            Assert.NotEmpty(Directory.GetFiles(folder, "*.mtl"));
        }

        [Fact]
        public void Mod_made_checked_and_applied()
        {
            string g = ContentCopy.Make(); if (g == null) return;
            var ed = ContentCopy.Open(g);
            ed.GraphicsDraw("OBJECTS.GR", 1, null, "pixel", "0,0", "7", 1, false);
            Assert.Throws<McpEditorException>(() => ed.ModApply(Path.Combine(g, "x.uwmod")));
            ed.Save();
            Assert.Contains("DATA/OBJECTS.GR", ed.ModChanges(null));
            string mod = Path.Combine(g, "test.uwmod");
            Assert.Contains("Written: ", ed.ModMake(mod, "test.mod", "Test", "1.0.0", "a test", "balderding", null));
            Assert.Contains("Already applied.", ed.ModCheck(mod));
            Assert.Contains("Nothing to write", ed.ModApply(mod));

            string other = ContentCopy.Make();
            var ed2 = ContentCopy.Open(other);
            Assert.Contains("It applies", ed2.ModCheck(mod));
            Assert.Contains("Applied Test", ed2.ModApply(mod));
            Assert.StartsWith("07", ed2.GraphicsPixels("OBJECTS.GR", 1, null, 0, 0, 1, 1).Split('\n')[1]);
            Assert.Contains("Already applied.", ed2.ModCheck(mod));
        }
    }

    /// <summary>The tools themselves: every name once, each one made into an MCP tool (its parameters' schema).</summary>
    public class ContentToolTests
    {
        [Fact]
        public void Every_tool_name_once_and_each_tool_made()
        {
            var tools = new EditorTools(new McpEditor(null));
            var all = PipeHost.HostedTools();
            var names = all.Select(m => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<ModelContextProtocol.Server.McpServerToolAttribute>(m).Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            foreach (var n in new[] { "conversation_list", "conversation_simulate", "conversation_graph", "balance_weapons", "barter_simulate", "graphics_image", "graphics_draw", "critter_image", "font_glyph", "model_export", "sound_import", "music_import", "mod_apply" })
                Assert.Contains(n, names);
            foreach (var m in all) Assert.NotNull(ModelContextProtocol.Server.McpServerTool.Create(m, tools));
        }
    }
}
