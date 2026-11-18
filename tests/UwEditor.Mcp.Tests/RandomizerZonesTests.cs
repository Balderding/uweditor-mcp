using System.IO;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The zoning tools on a copy of UW1: zones, painting, fill, links, grants, saving and reading back.</summary>
    public class RandomizerZonesTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        [Fact]
        public void Zones_painted_linked_saved_and_read_back()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.Contains("nothing painted yet", ed.ZonesInfo());
            Assert.StartsWith("Zone 3 \"Hall\" added", ed.ZoneAdd("Hall", 0));
            var st = ed.Session.Start;
            int sx = (int)st.X, sy = (int)st.Y;
            Assert.Contains("painted as zone 2", ed.ZonePaint(1, sx, sy, sx, sy, 2));
            Assert.Contains("02", ed.ZoneMap(1));
            Assert.Contains("filled as zone 3", ed.ZoneFill(1, sx + 1, sy, 3) + ed.ZoneFill(1, sx, sy + 1, 3) + ed.ZoneFill(1, sx - 1, sy, 3) + ed.ZoneFill(1, sx, sy - 1, 3));
            Assert.StartsWith("Added link 1", ed.ZoneLinkAdd(2, 3, true));
            Assert.Contains("requires key:5", ed.ZoneLinkSet(1, "requires", "key:5"));
            Assert.Throws<System.ArgumentException>(() => ed.ZoneLinkSet(1, "kind", "flying"));
            Assert.Contains("grants item:medallion", ed.ZoneGrantAdd(3, "item:medallion", null, null));
            Assert.Contains("[0] item:medallion", ed.ZoneGet(3));
            Assert.Contains("colour set", ed.ZoneSetField(3, "colour", "#112233"));
            Assert.Throws<System.FormatException>(() => ed.ZoneSetField(3, "colour", "blue"));
            Assert.Contains("unsaved changes", ed.ZonesInfo());
            Assert.Contains("Saved the zoning", ed.ZonesFile("save", null, false));
            Assert.True(File.Exists(Path.Combine(ed.Session.DataFolder, "uw1-zones.json")));

            var again = new McpEditor(ed.Session.GameFolder); again.Open(ed.Session.GameFolder);
            Assert.Contains("3 \"Hall\"", again.ZonesList());
            Assert.Contains("#112233", again.ZonesList());
            Assert.Contains("key:5", again.ZoneLinks(null));
            Assert.Throws<McpEditorException>(() => again.ZonesUndo());
            again.ZonePaint(1, sx, sy, sx, sy, 3);
            Assert.StartsWith("The last painting", again.ZonesUndo());
            Assert.Throws<McpEditorException>(() => again.ZonesFile("new", null, false) + again.ZonePaint(1, 0, 0, 0, 0, 3) + again.ZonesFile("new", null, false));
            Assert.StartsWith("Removed the grant item:medallion", again.ZoneGrantRemove(3, 0));
            Assert.Contains("Link 1 removed", again.ZoneLinkRemove(1));
            Assert.Contains("removed", again.ZoneRemove(3));
        }

        [Fact]
        public void Key_items_doors_and_refusals()
        {
            var ed = Opened(); if (ed == null) return;
            Assert.Contains("key item(s)", ed.ZoneKeyItems(null));
            Assert.False(string.IsNullOrEmpty(ed.ZoneDoorsList(1)));
            Assert.Throws<McpEditorException>(() => ed.ZoneGet(77));
            Assert.Throws<McpEditorException>(() => ed.ZoneMarkItem(1, 9999, true));
            Assert.Throws<McpEditorException>(() => ed.ZonesFile("example", null, false));
            Assert.StartsWith("Split by level for the solver: on", ed.ZonesSplit(true));
            Assert.StartsWith("Refused: ", EditorTools.Text(new EditorTools(ed).ZoneLinkAdd(2, 99)));
        }
    }
}
