using System.IO;
using System.Linq;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The player, its inventory and the global variables of a saved game made in a temp copy.</summary>
    public class SavesPlayerTests
    {
        [Fact]
        public void Saves_player_set_save_reload()
        {
            var ed = SavesTests.WithSave(out string g); if (ed == null) return;
            Assert.Contains("strength: ", ed.PlayerGet());
            Assert.StartsWith("strength: ", ed.PlayerSet("Strength", "20"));
            Assert.StartsWith("Attack: ", ed.PlayerSet("attack", "12"));
            Assert.StartsWith("class: ", ed.PlayerSet("class", "Mage"));
            Assert.StartsWith("name: ", ed.PlayerSet("name", "Tester"));
            ed.PlayerSet("max mana", "9");
            ed.PlayerSet("experience", "1234");
            ed.PlayerSet("female", "true");
            ed.PlayerSet("garamon buried", "true");
            Assert.Contains("Max vitality / mana / weight", ed.PlayerRecompute());
            Assert.False(string.IsNullOrEmpty(ed.PlayerCheck()));
            var tools = new EditorTools(ed);
            Assert.StartsWith("Refused: strength", EditorTools.Text(tools.PlayerSet("strength", "300")));
            Assert.StartsWith("Refused: No player field", EditorTools.Text(tools.PlayerSet("charisma", "3")));
            Assert.StartsWith("Refused: weight is read only", EditorTools.Text(tools.PlayerSet("weight", "3")));
            Assert.StartsWith("Saved PLAYER.DAT", ed.PlayerSave());
            Assert.True(File.Exists(Path.Combine(g, "SAVE1", "PLAYER.DAT.BKP")));
            Assert.Equal("Nothing to save: the player is as on disk.", ed.PlayerSave());

            var again = new McpEditor(g); again.Open(g); again.SaveOpen(1, false);
            string p = again.PlayerGet();
            Assert.Contains("name: Tester", p);
            Assert.Contains("strength: 20", p);
            Assert.Contains("class: 1 Mage", p);
            Assert.Contains("experience: 1234", p);
            Assert.Contains("female: true", p);
            Assert.Contains("garamon buried: true", p);
            Assert.Contains("attack 12", p);
        }

        [Fact]
        public void Saves_player_move()
        {
            var ed = SavesTests.WithSave(out string g); if (ed == null) return;
            var m = ed.Edits.Model(2);
            int tx = -1, ty = -1;
            for (int y = 1; y < 63 && tx < 0; y++)
                for (int x = 1; x < 63; x++) if (!m.Tiles[x, y].IsSolid) { tx = x; ty = y; break; }
            Assert.StartsWith("Player moved to", ed.PlayerMove(2, tx, ty, null, null, 2));
            Assert.Equal(2, ed.Session.PlayerPoint.Level);
            ed.PlayerSave();
            var pd = UltimateEditor.Games.PlayerDat.Parse(File.ReadAllBytes(Path.Combine(g, "SAVE1", "PLAYER.DAT")), ed.Session.Kind);
            Assert.Equal(2, pd.SavedLevel); Assert.Equal(tx, pd.TileX); Assert.Equal(ty, pd.TileY); Assert.Equal(2, pd.Heading);
            int sx = -1, sy = -1;
            for (int y = 1; y < 63 && sx < 0; y++)
                for (int x = 1; x < 63; x++) if (m.Tiles[x, y].IsSolid) { sx = x; sy = y; break; }
            Assert.StartsWith("Refused:", EditorTools.Text(new EditorTools(ed).PlayerMove(2, sx, sy)));
        }

        [Fact]
        public void Saves_inventory()
        {
            var ed = SavesTests.WithSave(out string g); if (ed == null) return;
            // a container (0x080 a sack), a sword in hand 1, a thing in the sack
            if (ed.InventoryList().Contains("[pack 8]")) ed.InventoryRemove(Number(ed.InventoryList(), "[pack 8]"));
            string bag = ed.InventoryAdd("0x080", "pack8", null, null, null, null, null, null);
            Assert.StartsWith("Added", bag);
            Assert.StartsWith("Refused: The pack 8 slot holds", EditorTools.Text(new EditorTools(ed).InventoryAdd("0x080", "pack8")));
            var inv = ed.InventoryList();
            int bagNo = Number(inv, "[pack 8]");
            Assert.StartsWith("Added", ed.InventoryAdd("0x00A", null, bagNo, 30, null, 3, null, null));
            if (ed.InventoryList().Contains("[hand 1]")) ed.InventoryRemove(Number(ed.InventoryList(), "[hand 1]"));
            Assert.StartsWith("Added", ed.InventoryAdd("0x004", "hand1", null, 50, null, null, null, true));
            inv = ed.InventoryList();
            int sword = Number(inv, "[hand 1]");
            Assert.Contains("enchanted", inv.Split('\n')[sword - 1]);
            Assert.StartsWith("Changed", ed.InventorySet(sword, "quality", "60"));
            Assert.StartsWith("Refused:", EditorTools.Text(new EditorTools(ed).InventorySet(sword, "quality", "99")));
            Assert.StartsWith("Moved", ed.InventoryMove(sword, null, Number(ed.InventoryList(), "[pack 8]")));
            Assert.DoesNotContain("[hand 1]", ed.InventoryList());
            ed.PlayerSave();

            var again = new McpEditor(g); again.Open(g); again.SaveOpen(1, false);
            string after = again.InventoryList();
            int bagAgain = Number(after, "[pack 8]");
            var lines = after.Split('\n');
            Assert.Contains("container of 2", lines[bagAgain - 1]);
            Assert.Contains(lines, l => l.TrimStart().Contains(" 0x00A ") && l.Contains("quantity 3") && l.Contains("quality 30"));
            Assert.Contains(lines, l => l.TrimStart().Contains(" 0x004 ") && l.Contains("quality 60"));
            Assert.StartsWith("Removed", again.InventoryRemove(bagAgain));
            Assert.DoesNotContain("[pack 8]", again.InventoryList());
        }

        static int CountItems(string list) { return list.Split('\n').Count(l => l.Length > 0 && char.IsDigit(l.TrimStart()[0])); }

        /// <summary>The number of the inventory line holding text.</summary>
        static int Number(string list, string text)
        {
            var l = list.Split('\n').First(x => x.Contains(text)).TrimStart();
            return int.Parse(l.Substring(0, l.IndexOf('.')));
        }

        [Fact]
        public void Saves_globals()
        {
            var ed = SavesTests.WithSave(out string g); if (ed == null) return;
            Assert.Contains("Quest 3", ed.GlobalList("quest", null, null, false, false));
            Assert.StartsWith("Quest 3", ed.GlobalSet("quest", 3, 1, null, null));
            Assert.Contains("= 1", ed.GlobalGet("quest", 3, null, null));
            ed.GlobalSet("variable", 5, 17, null, null);
            Assert.StartsWith("Refused:", EditorTools.Text(new EditorTools(ed).GlobalSet("quest", 3, 7)));
            Assert.StartsWith("Refused:", EditorTools.Text(new EditorTools(ed).GlobalSet("nonsense", 3, 1)));
            // a conversation's own global, when the new game's BGLOBALS.DAT keeps one
            string priv = ed.GlobalList("private", null, null, false, false);
            var line = priv.Split('\n').FirstOrDefault(l => l.StartsWith("Private slot ") && !l.Contains("read only"));
            int slot = -1, addr = -1;
            if (line != null)
            {
                var parts = line.Substring("Private slot ".Length).Split(' ');
                slot = int.Parse(parts[0]);
                addr = int.Parse(parts[1].TrimStart('#'));
                ed.GlobalSet("private", addr, 42, slot, null);
            }
            Assert.StartsWith("Saved", ed.GlobalSave());

            var again = new McpEditor(g); again.Open(g); again.SaveOpen(1, false);
            Assert.Contains("= 1", again.GlobalGet("quest", 3, null, null));
            Assert.Contains("= 17", again.GlobalGet("variable", 5, null, null));
            if (slot >= 0) Assert.Contains("= 42", again.GlobalGet("private", addr, slot, null));
            again.SaveOpen(0, false);
            Assert.Contains("DATA is open", again.GlobalList("quest", null, null, false, false));
        }

        [Fact]
        public void Saves_player_default()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            Assert.Contains("default", ed.PlayerDefault());
        }
    }
}
