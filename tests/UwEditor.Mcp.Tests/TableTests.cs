using System;
using System.IO;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The data tables (OBJECTS.DAT, TERRAIN.DAT, ...) and STRINGS.PAK: read, set, saved with save, read back.</summary>
    public class TableTests
    {
        [Fact]
        public void Creature_hp_saved_and_read_back()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            Assert.Contains("Average HP = ", ed.CreatureGet("0x45"));
            int hp = ed.CreatureGet("0x45").Contains("Average HP = 201") ? 202 : 201;
            string said = ed.CreatureSet("0x45", "hp", hp.ToString());
            Assert.Contains("Average HP", said);
            Assert.Contains("[changed, not saved]", ed.TableGet("Creatures", "5"));
            Assert.Equal("Saved OBJECTS.DAT (the old file kept as .BKP, the first as .ORIG).", ed.Save());
            Assert.True(File.Exists(Path.Combine(g, "DATA", "OBJECTS.DAT.BKP")));
            Assert.Equal("Nothing to save.", ed.Save());

            var again = new McpEditor(g); again.Open(g);
            Assert.Contains("Average HP = " + hp + " ", again.TableGet("Creatures", "0x045"));
            Assert.Contains("Average HP = " + hp + " ", again.CreatureGet("69"));
        }

        [Fact]
        public void Set_refuses_a_wrong_field_or_value()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            var e = Assert.Throws<ArgumentException>(() => ed.TableSet("Creatures", "5", "no such field", "1"));
            Assert.Contains("Average HP", e.Message);
            Assert.ThrowsAny<ArgumentException>(() => ed.TableSet("Creatures", "5", "Average HP", "100000"));
            Assert.ThrowsAny<ArgumentException>(() => ed.TableSet("Creatures", "5", "Average HP", "lots"));
            Assert.ThrowsAny<ArgumentException>(() => ed.TableSet("Creatures", "64", "Average HP", "5"));
            Assert.ThrowsAny<ArgumentException>(() => ed.TableSet("Weapons", "0x45", "Durability", "5"));
            Assert.ThrowsAny<ArgumentException>(() => ed.TableSet("Shades", "0", "curvrad", "99"));
            Assert.StartsWith("Refused: ", EditorTools.Text(new EditorTools(ed).TableSet("Nope", "0", "x", "1")));
            Assert.Equal("Nothing to save.", ed.Save());
        }

        [Fact]
        public void Tables_lists_every_table()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            string t = ed.Tables(null);
            foreach (var name in new[] { "Weapons", "Creatures", "Common properties", "Terrain", "Skills", "Combinations", "Shades" }) Assert.Contains(name + " (", t);
            Assert.Contains("Average HP", ed.Tables("creatures"));
            Assert.Contains("Strength", ed.TableGet("Skills", "Fighter"));
            Assert.Contains("First", ed.TableGet("Combinations", "1"));
        }

        [Fact]
        public void Game_tables_saved_with_levels()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            ed.TableSet("Terrain", "wall 7", "Value", "0x9");
            ed.TableSet("Skills", "0", "Bonus points", "40");
            ed.Fill(1, 2, 2, 3, 3, "open", 3, null, null);
            string said = ed.Save();
            Assert.StartsWith("Saved level 1. Saved ", said);
            Assert.Contains("TERRAIN.DAT", said);
            Assert.Contains("SKILLS.DAT", said);

            var again = new McpEditor(g); again.Open(g);
            Assert.StartsWith("0x9,", Value(again.TableGet("Terrain", "7"), "Value"));
            Assert.Equal("40", Value(again.TableGet("Skills", "Fighter"), "Bonus points"));
        }

        [Fact]
        public void Strings_saved_and_read_back()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            ed.StringSet("4", 0x45, "test beast");
            Assert.Equal("test beast", ed.StringGet("0x0004", 0x45));
            Assert.Contains("STRINGS.PAK", ed.Save());

            var again = new McpEditor(g); again.Open(g);
            Assert.Equal("test beast", again.StringGet("4", 0x45));
            Assert.Contains("test beast", again.ItemFind("0x45"));
        }

        [Fact]
        public void Save_refuses_over_a_file_changed_on_disk()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            ed.TableSet("Creatures", "5", "Average HP", "33");
            string path = Path.Combine(g, "DATA", "OBJECTS.DAT");
            var b = File.ReadAllBytes(path);
            b[0x132 + 6 * 0x30 + 4] ^= 1;      // another creature's byte, as if saved from the editor
            File.WriteAllBytes(path, b);
            Assert.Throws<McpEditorException>(() => ed.Save());
            Assert.Equal(b, File.ReadAllBytes(path));
            ed.TableRevert("Creatures");
            Assert.Equal("Nothing to save.", ed.Save());
        }

        /// <summary>The value of a field in a table_get answer.</summary>
        static string Value(string text, string field)
        {
            foreach (var line in text.Split('\n'))
                if (line.StartsWith(field + " = ", StringComparison.Ordinal)) return line.Substring(field.Length + 3).Replace("  [changed, not saved]", "");
            return null;
        }
    }
}
