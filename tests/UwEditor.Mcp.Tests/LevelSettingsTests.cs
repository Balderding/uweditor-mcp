using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>level_textures, level_texture_set, textures_find, level_clear, level_make, start_get/set, fill_fields, light_flags_all and the automap tools on a copy of UW1.</summary>
    public class LevelSettingsTests
    {
        static McpEditor Opened(string g)
        {
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        static McpEditor Opened() { string g = GameCopy.Make(); return g == null ? null : Opened(g); }

        static int ObjectCount(McpEditor ed, int level)
        {
            var m = ed.Edits.Model(level);
            int n = 0;
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) n += m.TileObjects(x, y).Count;
            return n;
        }

        [Fact]
        public void Textures_lists_every_slot()
        {
            var ed = Opened(); if (ed == null) return;
            var lines = ed.LevelTextures(1).Replace("\r", "").Split('\n');
            Assert.Equal(48, lines.Count(l => l.StartsWith("wall ") && l.Contains(": texture")));
            Assert.Equal(10, lines.Count(l => l.StartsWith("floor ") && l.Contains(": texture")));
            Assert.Equal(6, lines.Count(l => l.StartsWith("door ") && l.Contains(": texture")));
            var m = ed.Edits.Model(1);
            Assert.Contains(lines, l => l.StartsWith("floor 9: texture " + m.FloorMap[9] + ", the ceiling"));
            Assert.Contains(lines, l => l.StartsWith("wall 0: texture " + m.WallMap[0] + ","));
            Assert.Equal(6, ed.LevelTextures(1, "door").Replace("\r", "").Split('\n').Count(l => l.StartsWith("door ") && l.Contains(": texture")));
        }

        [Fact]
        public void Textures_find_lava_and_water()
        {
            var ed = Opened(); if (ed == null) return;
            string lava = ed.TexturesFind("floor", "lava");
            foreach (int t in new[] { 23, 24, 25 }) Assert.Contains("texture " + t + ": lava", lava);
            Assert.DoesNotContain("water", lava);
            string water = ed.TexturesFind("floor", "water");
            foreach (int t in new[] { 16, 17, 32, 33, 34 }) Assert.Contains("texture " + t + ": water", water);
            Assert.Throws<ArgumentException>(() => ed.TexturesFind("roof"));
        }

        [Fact]
        public void Texture_set_is_one_undo_step_and_saved()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = Opened(g);
            int old = ed.Edits.Model(1).FloorMap[2];
            int nv = old == 24 ? 23 : 24;
            string a = ed.LevelTextureSet(1, "floor", 2, nv);
            Assert.Contains("-> " + nv, a);
            Assert.Contains("lava", a);
            Assert.Equal(nv, ed.Edits.Model(1).FloorMap[2]);
            ed.Undo(1, 1);
            Assert.Equal(old, ed.Edits.Model(1).FloorMap[2]);
            Assert.False(ed.Edits.For(1).CanUndo);

            ed.LevelTextureSet(1, "wall", 5, 7);
            ed.LevelTextureSet(1, "door", 1, 3);
            ed.Save();
            var again = Opened(g);
            Assert.Equal(7, again.Edits.Model(1).WallMap[5]);
            Assert.Equal(3, again.Edits.Model(1).DoorMap[1]);
        }

        [Fact]
        public void Texture_set_refuses_bad_values()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.Throws<ArgumentOutOfRangeException>(() => ed.LevelTextureSet(1, "floor", 10, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ed.LevelTextureSet(1, "wall", 0, 100000));
            Assert.Throws<ArgumentException>(() => ed.LevelTextureSet(1, "sky", 0, 1));
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Clear_removes_objects_and_tiles()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.True(ObjectCount(ed, 2) > 0);
            string a = ed.LevelClear(2, false);
            Assert.Contains("cleared", a);
            Assert.Equal(0, ObjectCount(ed, 2));
            Assert.True(ed.Edits.Model(2).CheckOk);
            ed.Undo(2, 1);
            Assert.True(ObjectCount(ed, 2) > 0);

            ed.LevelClear(2, true);
            var m = ed.Edits.Model(2);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) Assert.True(m.Tiles[x, y].IsSolid);
            Assert.Equal(0, ObjectCount(ed, 2));
        }

        [Fact]
        public void Make_refuses_a_level_in_use()
        {
            var ed = Opened(); if (ed == null) return;
            var e = Assert.Throws<McpEditorException>(() => ed.LevelMake(1, true));
            Assert.Contains("already holds a level", e.Message);
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void Start_set_refuses_without_executable()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.Contains("square (32, 2)", ed.StartGet());
            Assert.Contains("cannot move it", ed.StartGet());
            var e = Assert.Throws<McpEditorException>(() => ed.StartSet(31, 2));
            Assert.Contains("executable", e.Message);
        }

        [Fact]
        public void Start_set_patches_the_executable()
        {
            string g = GameCopy.Make(); if (g == null) return;
            string root = Environment.GetEnvironmentVariable("UW_GAMEDATA");
            string exe = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "UW", "UW.EXE");
            if (exe == null || !File.Exists(exe)) return;
            File.Copy(exe, Path.Combine(g, "UW.EXE"));
            var ed = Opened(g);
            Assert.Contains("square (32, 2), as shipped", ed.StartGet());
            var m = ed.Edits.Model(1);
            int tx = -1, ty = -1;
            for (int y = 0; y < 64 && tx < 0; y++) for (int x = 0; x < 64; x++) if (!m.Tiles[x, y].IsSolid && (x != 32 || y != 2)) { tx = x; ty = y; break; }
            int rx = -1, ry = -1;
            for (int y = 0; y < 64 && rx < 0; y++) for (int x = 0; x < 64; x++) if (m.Tiles[x, y].IsSolid) { rx = x; ry = y; break; }
            Assert.Throws<McpEditorException>(() => ed.StartSet(rx, ry));
            string a = ed.StartSet(tx, ty);
            Assert.Contains("written to UW.EXE", a);
            Assert.Contains("square (" + tx + ", " + ty + ")", ed.StartGet());
            Assert.Contains("square (" + tx + ", " + ty + ")", Opened(g).StartGet());
            Assert.True(File.Exists(Path.Combine(g, "UW.EXE.ORIG")) || File.Exists(Path.Combine(g, "UW.EXE.BKP")));
        }

        [Fact]
        public void Fill_fields_sets_flags_in_one_step()
        {
            var ed = Opened(); if (ed == null) return;
            var e = ed.Edits.For(1);
            int steps = e.UndoLabels.Count;
            string a = ed.FillFields(1, 10, 10, 12, 11, 1, true, true, false);
            Assert.Contains("Set 6 tiles", a);
            Assert.Equal(steps + 1, e.UndoLabels.Count);
            for (int y = 10; y <= 11; y++)
                for (int x = 10; x <= 12; x++)
                {
                    Assert.Equal(1, e.TileField(x, y, "Light"));
                    Assert.Equal(1, e.TileField(x, y, "No magic"));
                    Assert.Equal(1, e.TileField(x, y, "Door"));
                }
            ed.FillFields(1, 10, 10, 12, 11, null, false, null, false);
            Assert.Equal(0, e.TileField(11, 10, "No magic"));
            Assert.Equal(1, e.TileField(11, 10, "Door"));
            Assert.Throws<ArgumentOutOfRangeException>(() => ed.FillFields(1, 0, 0, 1, 1, 4, null, null, false));
            Assert.Throws<ArgumentException>(() => ed.FillFields(1, 0, 0, 1, 1, null, null, null, false));
        }

        [Fact]
        public void Light_flags_all_counts_open_squares()
        {
            var ed = Opened(); if (ed == null) return;
            string a = ed.LightFlagsAll(1, true);
            int open;
            int n = UwEditor.Core.Editing.LightFlags.Count(ed.Edits.Model(1), out open);
            Assert.Equal(open, n);
            Assert.Contains("on " + open + " of " + open, a);
            ed.LightFlagsAll(1, false);
            Assert.Equal(0, UwEditor.Core.Editing.LightFlags.Count(ed.Edits.Model(1), out open));
        }

        [Fact]
        public void Automap_marks_and_notes_are_saved()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = Opened(g);
            ed.AutomapAll(1, true);
            int seen = ed.Edits.AutomapFor(1).SeenCount;
            Assert.True(seen > 0);
            string note = ed.AutomapNoteAdd(1, 32, 3, "hello there");
            Assert.Contains("HELLO THERE", note);
            Assert.Contains("\"HELLO THERE\"", ed.AutomapNotes(1));
            Assert.Contains("tile (32, 3)", ed.AutomapNotes(1));
            ed.AutomapNoteAdd(1, 33, 3, "GONE");
            int count = ed.Edits.AutomapFor(1).Notes.Count;
            ed.AutomapNoteDelete(1, count - 1);
            ed.AutomapNoteAdd(1, 34, 3, "UNDONE");
            ed.AutomapUndo(1);
            Assert.Equal(count - 1, ed.Edits.AutomapFor(1).Notes.Count);
            Assert.Contains("1", ed.Save());
            var again = Opened(g);
            Assert.Equal(seen, again.Edits.AutomapFor(1).SeenCount);
            Assert.Contains("\"HELLO THERE\"", again.AutomapNotes(1));
            Assert.DoesNotContain("GONE", again.AutomapNotes(1));
            Assert.DoesNotContain("UNDONE", again.AutomapNotes(1));
        }

        [Fact]
        public void Tools_answer_through_the_wrappers()
        {
            var ed = Opened(); if (ed == null) return;
            var tools = new EditorTools(ed);
            var r = tools.LevelTextureSet(1, "floor", 99, 1);
            Assert.True(r.IsError);
            Assert.StartsWith("Refused:", EditorTools.Text(r));
            Assert.Null(tools.StartGet().IsError);
            Assert.True(tools.StartSet(31, 2).IsError);
            Assert.Null(tools.LevelTextures(1, null).IsError);
        }
    }
}
