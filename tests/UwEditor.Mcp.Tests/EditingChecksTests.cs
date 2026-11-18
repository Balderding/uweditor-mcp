using System;
using System.Buffers.Binary;
using System.Linq;
using UwEditor.Core;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>Find and replace, the checks, the cleaner, item slots, texts, jumps, hitboxes, schedules, the selection and the 3D picture.</summary>
    public class EditingChecksTests
    {
        [Fact]
        public void Find_and_replace_across_levels()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            string found = ed.ObjectsFind("door", null, null, null, null, null, null, 0, 63, 50);
            Assert.Contains("L1 #", found);
            Assert.Contains("L", ed.ObjectsFind("", "Key", null, null, null, null, null, 0, 63, 10));
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectsFind("", "Spaceship", null, null, null, null, null, 0, 63, 10));
            var (x, y) = EditingTests.EmptyOpen(ed);
            string said = ed.ObjectAdd(1, x, y, "0x0B0", null, null, null, null, null, null);
            int a = int.Parse(said.Substring(said.IndexOf('#') + 1).Split(' ')[0]);
            Assert.Contains("1 object changed", ed.ObjectsReplace("#" + a, null, 1, null, null, null, null, 0, 63, null, "Quality", 33));
            Assert.Equal(33, ed.Edits.For(1).Model.Objects[a].Quality);
            ed.ObjectsReplace("#" + a, null, 1, null, null, null, null, 0, 63, "0x0B1", null, null);
            Assert.Equal(0x0B1, ed.Edits.For(1).Model.Objects[a].Item);
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectsReplace("", null, null, null, null, null, null, 0, 63, "0x0B1", null, null));
        }

        [Fact]
        public void World_check_fixes_and_mod_checks()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            Assert.Contains("mobile objects", ed.WorldCheckRun());
            Assert.Contains("[twice]", ed.FixesChecks());
            Assert.False(string.IsNullOrEmpty(ed.FixesFind("all", null)));
            Assert.False(string.IsNullOrEmpty(ed.FixesFind("1", "key")));
            Assert.Contains("level 1", ed.FixesApply(1, "all", false).ToLowerInvariant());
            Assert.Contains("game as saved", ed.ModChecksRun(false));
            Assert.ThrowsAny<ArgumentException>(() => ed.FixesApply(1, "999", false));
        }

        [Fact]
        public void Item_cleaner_previews_then_removes()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            string preview = ed.ItemClean("1", "Bones, Debris", false, false, 0, 0, 63, 63, null, false);
            Assert.Contains("Would remove", preview);
            int before = ed.Edits.For(1).Model.Objects.Count(o => !o.Free);
            string done = ed.ItemClean("1", "Bones, Debris", false, false, 0, 0, 63, 63, null, true);
            Assert.StartsWith("Removed", done);
            Assert.True(ed.Edits.For(1).Model.Objects.Count(o => !o.Free) <= before);
            Assert.StartsWith("Level 1 passes", ed.Check(1));
            Assert.ThrowsAny<ArgumentException>(() => ed.ItemClean("1", "Gold", false, false, 0, 0, 63, 63, null, false));
        }

        [Fact]
        public void Item_uses_and_texts()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            Assert.Contains("0x0B0", ed.ItemUsesOf("0x0B0"));
            var m = ed.Edits.For(1).Model;
            var book = m.Objects.FirstOrDefault(o => o != null && !o.Free && o.TileX >= 0 && o.IsQuantity && o.Link >= 0x200 && (ItemCategories.Of(o.Item, ed.Session.Kind) & (ItemCategory.Book | ItemCategory.Scroll)) != 0 && !o.Enchanted);
            if (book == null) return;
            Assert.Contains("reads block", ed.ObjectText(1, book.Index));
            Assert.Contains("NEW WORDS", ed.ObjectTextSet(1, book.Index, "NEW WORDS"));
            Assert.Contains("NEW WORDS", ed.ObjectText(1, book.Index));
            Assert.Contains("carries no text", ed.ObjectText(1, m.Objects.First(o => o != null && !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x146).Index));
        }

        [Fact]
        public void Jump_and_hitboxes()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            var (x, y) = EditingTests.EmptyOpen(ed);
            var m = ed.Edits.For(1).Model;
            int nx = x + 1 < 64 && !m.Tiles[x + 1, y].IsSolid ? x + 1 : x;
            int ny = nx == x ? y + 1 : y;
            string j = ed.TryJump(1, x, y, nx, ny);
            Assert.Contains("Plain:", j);
            Assert.Contains("collision boxes", ed.HitboxOverlaps(1));
        }

        [Fact]
        public void Schedules_belong_to_uw2_and_the_selection_to_the_window()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            Assert.Throws<McpEditorException>(() => ed.SchedulesList(null));
            Assert.Throws<McpEditorException>(() => ed.EditorSelectionGet());
            Assert.Throws<McpEditorException>(() => ed.EditorShow(1, 3, 3, null));
            ed.HostSelection = () => new EditorSelection { Level = 1, TileX = 4, TileY = 5 };
            Assert.Contains("(4, 5)", ed.EditorSelectionGet());
        }

        [Fact]
        public void View_picture_draws_the_level_headless()
        {
            var ed = EditingTests.Opened(); if (ed == null) return;
            var start = ed.Session.NewGameStart;
            int sx = (int)start.X, sy = (int)start.Y;
            string said;
            var png = ed.ViewPicturePng(1, sx, sy, 0, 0, null, null, 60, 320, 200, true, true, false, false, true, false, out said);
            Assert.Equal((320, 200), (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))));
            Assert.Contains("Level 1", said);
            // a drawn level is more than one or two colours: decode the IDAT is too much here, so compare with a picture looking elsewhere
            var other = ed.ViewPicturePng(1, sx, sy, 180, 30, null, 6, 60, 320, 200, false, true, false, false, true, false, out said);
            Assert.False(png.SequenceEqual(other));
            // UW_MCP_SHOTS: a folder to keep the pictures in, to look at them
            string shots = Environment.GetEnvironmentVariable("UW_MCP_SHOTS");
            if (!string.IsNullOrEmpty(shots))
            {
                System.IO.Directory.CreateDirectory(shots);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(shots, "view-first-person.png"), png);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(shots, "view-from-afar.png"), other);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(shots, "view-wide.png"), ed.ViewPicturePng(1, sx, sy, 90, 10, null, null, 70, 800, 500, true, true, false, false, true, false, out said));
            }
            Assert.True(png.Length > 2000);
            Assert.ThrowsAny<ArgumentException>(() => ed.ViewPicturePng(1, sx, sy, 0, 0, null, null, 60, 10, 200, true, true, false, false, true, false, out said));
            Assert.Throws<McpEditorException>(() => ed.ViewPicturePng(1, null, null, 0, 0, null, null, 60, 320, 200, true, true, false, false, true, true, out said));
        }
    }
}
