using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using UwEditor.Core;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The editing group's history, floor, object clipboard and lock tools, on a copy of UW1.</summary>
    public class EditingTests
    {
        internal static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            ed.PrefabFolder = System.IO.Path.Combine(g, "prefabs");
            return ed;
        }

        internal static (int x, int y) EmptyOpen(McpEditor ed, int skip = 0)
        {
            var m = ed.Edits.For(1).Model;
            for (int y = 1; y < 63; y++)
                for (int x = 1; x < 63; x++)
                    if (m.Tiles[x, y].Type == TileType.Open && m.TileObjects(x, y).Count == 0 && m.Tiles[x, y].Height < 12 && skip-- <= 0) return (x, y);
            throw new Exception("no empty open tile");
        }

        static int Added(string said) { return int.Parse(said.Substring(said.IndexOf('#') + 1).Split(' ')[0]); }

        [Fact]
        public void Tool_names_are_unique_and_snake_case()
        {
            var names = typeof(EditorTools).GetMethods().Select(m => m.GetCustomAttribute<McpServerToolAttribute>()).Where(a => a != null).Select(a => a.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            foreach (var n in new[] { "redo", "undo_history", "floor_raise", "objects_copy", "objects_paste", "tiles_paste", "prefab_place", "recipe_plate_trap", "trigger_chains", "objects_find", "fixes_apply", "schedules", "view_picture" })
                Assert.Contains(n, names);
        }

        [Fact]
        public void Every_tool_is_made_by_the_sdk_and_answers_through_the_wrapper()
        {
            var tools = new EditorTools(new McpEditor(null));
            foreach (var m in PipeHost.HostedTools()) Assert.NotNull(McpServerTool.Create(m, tools));
            var ed = Opened(); if (ed == null) return;
            var t = new EditorTools(ed);
            var r = t.ViewPicture(1, 3, 3, 0, 0, null, 8, 60, 200, 120);
            Assert.Contains(r.Content, c => c is ModelContextProtocol.Protocol.ImageContentBlock);
            var refused = t.FloorRaise(1, 70, 3, 1);
            Assert.True(refused.IsError);
            Assert.StartsWith("Refused:", EditorTools.Text(refused));
            Assert.StartsWith("Level 1", EditorTools.Text(t.UndoHistory(1)) ?? "Level 1");
        }

        [Fact]
        public void Redo_takes_back_an_undo_and_history_lists_labels()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpen(ed);
            int h = ed.Edits.For(1).Model.Tiles[x, y].Height;
            Assert.Contains("Raised", ed.FloorRaise(1, x, y, null, null, 2));
            Assert.Equal(h + 2, ed.Edits.For(1).Model.Tiles[x, y].Height);
            Assert.Contains("Raise the floor", ed.UndoHistory(1));
            ed.Undo(1, 1);
            Assert.Equal(h, ed.Edits.For(1).Model.Tiles[x, y].Height);
            Assert.Contains("to redo", ed.UndoHistory(1));
            Assert.StartsWith("Redid", ed.Redo(1, 1));
            Assert.Equal(h + 2, ed.Edits.For(1).Model.Tiles[x, y].Height);
            Assert.Throws<McpEditorException>(() => ed.Redo(1, 1));
        }

        [Fact]
        public void Floor_raise_on_a_rectangle_is_one_step()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpen(ed);
            int before = ed.Edits.For(1).UndoLabels.Count;
            ed.FloorRaise(1, x - 1, y - 1, x + 1, y + 1, 1);
            Assert.Equal(before + 1, ed.Edits.For(1).UndoLabels.Count);
            Assert.ThrowsAny<ArgumentException>(() => ed.FloorRaise(1, x, y, null, null, 0));
            Assert.ThrowsAny<ArgumentException>(() => ed.FloorRaise(1, 64, y, null, null, 1));
        }

        [Fact]
        public void Copy_paste_duplicate_and_delete_objects()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpen(ed);
            var (x2, y2) = EmptyOpen(ed, 5);
            int a = Added(ed.ObjectAdd(1, x, y, "0x0B0", null, null, null, null, null, null));
            int b = Added(ed.ObjectAdd(1, x, y, "0x0B0", null, null, null, null, null, null));
            Assert.Contains("Copied", ed.ObjectsCopy(1, a + ", " + b, false));
            Assert.Contains("2 objects", ed.ObjectsClip());
            string pasted = ed.ObjectsPaste(1, x2, y2, null);
            Assert.Contains("Pasted", pasted);
            Assert.Equal(2, ed.Edits.For(1).Model.TileObjects(x2, y2).Count);
            ed.ObjectsDuplicate(1, a.ToString());
            Assert.Equal(3, ed.Edits.For(1).Model.TileObjects(x, y).Count);
            Assert.Contains("Deleted 3", ed.ObjectsDelete(1, string.Join(",", ed.Edits.For(1).Model.TileObjects(x, y).Select(o => o.Index))));
            Assert.Empty(ed.Edits.For(1).Model.TileObjects(x, y));
            Assert.StartsWith("Level 1 passes", ed.Check(1));
            Assert.ThrowsAny<ArgumentException>(() => ed.ObjectsCopy(1, "zz", false));
        }

        [Fact]
        public void Cut_then_paste_into_a_container()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpen(ed);
            int sack = Added(ed.ObjectAdd(1, x, y, "0x080", null, null, null, null, null, null));
            int item = Added(ed.ObjectAdd(1, x, y, "0x0B0", null, null, null, null, null, null));
            Assert.Contains("Cut", ed.ObjectsCopy(1, item.ToString(), true));
            Assert.Single(ed.Edits.For(1).Model.TileObjects(x, y));
            ed.ObjectsPaste(1, null, null, sack);
            Assert.Single(ed.Edits.For(1).Model.Objects[sack].Children);
            int inside = ed.Edits.For(1).Model.Objects[sack].Children[0];
            Assert.Contains("Moved", ed.ObjectTakeOut(1, inside, x, y, null, null));
            Assert.Empty(ed.Edits.For(1).Model.Objects[sack].Children);
            Assert.StartsWith("Level 1 passes", ed.Check(1));
        }

        [Fact]
        public void Locks_copy_and_remove_and_snap_to_wall()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var doors = m.Objects.Where(o => o != null && !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x146).ToList();
            var locked = doors.FirstOrDefault(d => ed.Edits.For(1).LockOf(d.Index) != null);
            var bare = doors.FirstOrDefault(d => ed.Edits.For(1).LockOf(d.Index) == null);
            if (locked == null || bare == null) return;
            Assert.Contains("lock now", ed.LockCopy(1, locked.Index, bare.Index));
            Assert.NotNull(ed.Edits.For(1).LockOf(bare.Index));
            Assert.Contains("no lock now", ed.LockRemove(1, bare.Index));
            Assert.Null(ed.Edits.For(1).LockOf(bare.Index));
            Assert.ThrowsAny<ArgumentException>(() => ed.LockRemove(1, bare.Index));
            var lever = m.Objects.FirstOrDefault(o => o != null && !o.Free && o.OnTile && o.Item >= 0x170 && o.Item < 0x178);
            if (lever != null) Assert.Contains("against its wall", ed.SnapToWall(1, lever.Index));
            Assert.ThrowsAny<ArgumentException>(() => ed.SnapToWall(1, bare.Index));
        }

        [Fact]
        public void Objects_to_floor()
        {
            var ed = Opened(); if (ed == null) return;
            var (x, y) = EmptyOpen(ed);
            int a = Added(ed.ObjectAdd(1, x, y, "0x0B0", null, null, 120, null, null, null));
            Assert.Contains("Put 1", ed.DropToFloor(1, x, y, null, null));
            Assert.True(ed.Edits.For(1).Model.Objects[a].Z < 120);
        }
    }
}
