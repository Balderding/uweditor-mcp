using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The main window's tile clipboard, texts of new objects, links from the menu, and editing several objects at once.</summary>
    public class BuildingToolsTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        static List<string> Headers(IEnumerable<Control> items)
        {
            return items.OfType<MenuItem>().Select(m => (m.Header ?? "").ToString().Replace("_", "")).ToList();
        }

        [AvaloniaFact]
        public void Tiles_are_copied_turned_and_pasted_from_the_window_as_one_undo_step()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            Assert.Null(w.PasteTiles(5, 5));
            Assert.Contains("copy tiles first", vm.EditStatus);
            // a 3 × 2 block round an open tile with objects
            var o = vm.Level.Objects.First(x => !x.Free && x.OnTile && !x.IsMobile && !x.IsTrapOrTrigger && x.TileX > 2 && x.TileX < 60 && x.TileY > 2 && x.TileY < 60);
            var tiles = new List<int>();
            for (int y = o.TileY; y < o.TileY + 2; y++) for (int x = o.TileX; x < o.TileX + 3; x++) tiles.Add(x + 64 * y);
            w.AddToSelection(null, tiles);
            Assert.True(w.CopyTiles());
            Assert.Equal(3, w.CopiedTiles.Width);
            Assert.Contains("3 × 2", vm.EditStatus);
            Assert.True(w.TurnTileClip(false));
            Assert.Equal(2, w.CopiedTiles.Width);
            Assert.True(w.TurnTileClip(true));
            var before = (byte[])vm.Editor.Data.Clone();
            int px = o.TileX < 30 ? 55 : 4, py = o.TileY < 30 ? 55 : 4;
            var res = w.PasteTiles(px, py);
            Assert.NotNull(res);
            Assert.Equal(6, res.Tiles);
            Assert.True(res.Objects >= 1);
            Assert.Equal(6, w.SelectedTiles.Count);
            Assert.Contains("Paste", vm.UndoText);
            bool ok; vm.Editor.Check(out ok); Assert.True(ok);
            w.Undo();
            Assert.Equal(before, vm.Editor.Data);
            // the right-click menu on a tile offers the clipboard
            var h = Headers(w.ContextItems(new PickResult { Object = -1, Tile = o.TileX + 64 * o.TileY }));
            Assert.Contains(h, x => x.StartsWith("Copy th"));
            Assert.Contains(h, x => x.StartsWith("Paste the copied tiles here"));
            Assert.Contains("Mirror the copied tiles", h);
            w.Close();
        }

        [AvaloniaFact]
        public void Select_similar_and_a_value_typed_once_goes_to_every_selected_object()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            // the item with the most copies lying about on level 1, with differing qualities
            var grp = vm.Level.Objects.Where(o => !o.Free && o.TileX >= 0 && !o.IsMobile && !o.IsTrapOrTrigger && o.Item < 0x140)
                .GroupBy(o => o.Item).Where(g => g.Select(o => o.Quality).Distinct().Count() > 1).OrderByDescending(g => g.Count()).First();
            var first = grp.First();
            int n = w.SelectSimilar(first.Index, "item");
            Assert.Equal(grp.Count(), n);
            Assert.Equal(n, w.SelectedObjects.Count);
            // the panel shows what they do not share as mixed
            var q = vm.ObjectRows.First(r => r.Name == "Quality");
            Assert.Equal("", q.Value);
            Assert.Equal("mixed", q.Hex);
            Assert.Equal(first.Item.ToString(), vm.ObjectRows.First(r => r.Name == "Item id").Value);
            var before = (byte[])vm.Editor.Data.Clone();
            int steps = vm.Editor.UndoLabels.Count;
            Assert.Equal(n, vm.EditFieldOnAll(q, "7", w.SelectedObjects));
            Assert.All(grp, o => Assert.Equal(7, vm.Editor.Model.Objects[o.Index].Quality));
            Assert.Equal(steps + 1, vm.Editor.UndoLabels.Count);
            Assert.Equal("7", vm.ObjectRows.First(r => r.Name == "Quality").Value);
            w.Undo();
            Assert.Equal(before, vm.Editor.Data);
            // a value out of range is refused for all, and says so
            Assert.Equal(0, vm.EditFieldOnAll(vm.ObjectRows.First(r => r.Name == "Quality"), "99", w.SelectedObjects));
            Assert.Contains("left as", vm.EditStatus);
            Assert.Equal(before, vm.Editor.Data);
            // the same kind takes in at least as many; the menu offers it
            Assert.True(w.SelectSimilar(first.Index, "kind") >= n);
            var items = w.ContextItems(new PickResult { Object = first.Index, Tile = first.TileX + 64 * first.TileY });
            Assert.Contains("Select similar", Headers(items));
            // tiles: every tile with one floor texture, set to another slot at once
            var t = vm.Level.Tiles[first.TileX, first.TileY];
            int tiles = w.SelectSimilarTiles(first.TileX, first.TileY, false);
            Assert.True(tiles > 1);
            Assert.Equal(tiles, w.SelectedTiles.Count);
            int other = t.FloorIndex == 0 ? 1 : 0;
            var frow = vm.TileRows.First(r => r.Field != null && r.Field.Name == "Floor texture");
            Assert.Equal(tiles, vm.EditFieldOnAll(frow, other.ToString(), w.SelectedTiles));
            Assert.All(w.SelectedTiles, k => Assert.Equal(other, vm.Editor.Model.Tiles[k % 64, k / 64].FloorIndex));
            w.Undo();
            Assert.Equal(before, vm.Editor.Data);
            w.Close();
        }

        [AvaloniaFact]
        public void A_pasted_book_has_its_own_text_which_undo_takes_back_and_delete_frees()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var st = w.Strings();
            // a level with a book or scroll that has a text
            ObjectInfo book = null;
            foreach (var slot in vm.Session.LevelSlots().Where(x => x.Present))
            {
                vm.LoadLevel(slot.Level);
                book = vm.Level.Objects.FirstOrDefault(o => !o.Free && o.OnTile && (ItemCategories.Of(o.Item, vm.Session.Kind) & (ItemCategory.Book | ItemCategory.Scroll)) != 0 && !string.IsNullOrEmpty(w.TextOf(o.Index)?.Text));
                if (book != null) break;
            }
            Assert.NotNull(book);
            var was = w.TextOf(book.Index);
            vm.Select(book.Index, -1, -1, true);
            Assert.True(w.CopySelection(false));
            var made = w.PasteClip(book.TileX, book.TileY, -1);
            var own = w.TextOf(made[0]);
            Assert.NotEqual(was.Index, own.Index);
            Assert.Equal(was.Text, own.Text);
            Assert.Contains("own string", vm.EditStatus);
            // the panel shows the text as edited, before anything is saved
            Assert.Contains(vm.ObjectRows, r => (r.Value ?? "").Contains(was.Text.Split((char)10)[0].Trim()));
            // undo takes the copy and its string back; redo brings both again
            w.Undo();
            Assert.True(vm.Level.Objects[made[0]].Free);
            Assert.Equal("", st.Get(own.Block, own.Index));
            w.Redo();
            Assert.False(vm.Level.Objects[made[0]].Free);
            Assert.Equal(was.Text, st.Get(own.Block, own.Index));
            // its text changed alone; the original keeps its own
            Assert.True(w.SetObjectText(made[0], "MINE ALONE"));
            Assert.Equal("MINE ALONE", w.TextOf(made[0]).Text);
            Assert.Equal(own.Index, w.TextOf(made[0]).Index);
            Assert.Equal(was.Text, w.TextOf(book.Index).Text);
            // the menu offers it
            Assert.Contains("Edit its text…", Headers(w.ContextItems(new PickResult { Object = made[0], Tile = book.TileX + 64 * book.TileY })));
            // deleting the copy frees its string, and undo puts the text back
            vm.Select(made[0], -1, -1, true);
            w.DeleteSelected();
            Assert.Equal("", st.Get(own.Block, own.Index));
            Assert.Contains("freed", vm.EditStatus);
            w.Undo();
            Assert.Equal("MINE ALONE", st.Get(own.Block, own.Index));
            // an object read by several gets a copy when its text is set
            var again = w.PasteClip(book.TileX, book.TileY, -1);
            int shared = w.TextOf(book.Index).Index;
            Assert.True(w.SetObjectText(book.Index, "THE ORIGINAL, CHANGED"));
            Assert.Equal(shared, w.TextOf(book.Index).Index);      // it alone read its string: changed in place
            Assert.NotEqual(shared, w.TextOf(again[0]).Index);
            w.Close();
        }

        [AvaloniaFact]
        public void A_trigger_is_linked_from_the_menu_and_the_panel_lists_its_links_to_go_to()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var open = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).Where(t => !vm.Level.Tiles[t.X, t.Y].IsSolid).ToList();
            var a = open[open.Count / 4]; var b = open[open.Count / 2];
            int trap = -1, trig = -1;
            Assert.True(vm.Edit(e => trap = e.AddObject(new ObjectSpec { Item = 0x190, IsQuantity = true, Link = 0 }, a.X, a.Y)));
            Assert.True(vm.Edit(e => trig = e.AddObject(new ObjectSpec { Item = TrapRecipes.MoveTrigger, IsQuantity = true, Link = 0 }, b.X, b.Y)));
            vm.FlushRefresh();
            // the menu of a trigger offers the link that leaves the trap where it lies
            var h = Headers(w.ContextItems(new PickResult { Object = trig, Tile = b.X + 64 * b.Y }));
            Assert.Contains(h, x => x.StartsWith("Link it to an object that stays where it lies"));
            Assert.True(w.LinkTo(trig, trap));
            Assert.Equal(trap, vm.Level.Objects[trig].Link);
            Assert.True(vm.Level.Objects[trap].OnTile);
            Assert.Equal(trig, vm.SelectedObject);
            // the panel: the trigger acts on the trap's square; the trap is linked from the trigger
            Assert.True(vm.HasLinks);
            var acts = vm.Links.First(l => l.Object < 0);
            Assert.Equal((a.X, a.Y), (acts.X, acts.Y));
            w.GoToLink(acts);
            Assert.Equal((a.X, a.Y), (vm.SelectedTileX, vm.SelectedTileY));
            vm.Select(trap, -1, -1, true);
            var from = vm.Links.First(l => l.Object == trig);
            w.GoToLink(from);
            Assert.Equal(trig, vm.SelectedObject);
            // an object that links to nothing is refused, with the reason in the status line
            var plain = vm.Level.Objects.First(o => !o.Free && o.OnTile && !o.IsMobile && o.Item < 0x40);
            Assert.False(w.LinkTo(plain.Index, trap));
            Assert.Contains("links to nothing", vm.EditStatus);
            w.Close();
        }

        [AvaloniaFact]
        public void The_conversation_window_adds_a_conversation_to_an_empty_slot_and_deletes_it()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var cw = w.ShowConversations();
            Ui.Pump();
            int before = cw.Model.All.Count;
            int slot = w.Conversations().EmptySlots()[0];
            Assert.DoesNotContain(cw.Model.All, r => r.Slot == slot);
            Assert.True(cw.AddConversation(slot, "Testa"));
            Ui.Pump();
            Assert.Equal(before + 1, cw.Model.All.Count);
            Assert.Equal(slot, cw.Model.Selected.Slot);
            Assert.Contains("Testa", cw.Model.Selected.Title);
            Assert.Contains("added from the template", cw.Model.Status);
            Assert.True(w.Conversations().HasChanges);
            // a slot that has one is refused, with the reason
            Assert.False(cw.AddConversation(slot, "Again"));
            Assert.Contains("already has a conversation", cw.Model.Status);
            Assert.True(cw.DeleteConversation(slot));
            Ui.Pump();
            Assert.Equal(before, cw.Model.All.Count);
            Assert.DoesNotContain(cw.Model.All, r => r.Slot == slot);
            Assert.NotNull(cw.Model.Selected);
            cw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_command_palette_finds_a_menu_command_by_part_of_its_name_and_runs_it()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var all = w.PaletteCommands();
            Assert.True(all.Count > 80);
            Assert.Contains(all, c => c.Path == "Edit ▸ Paste tiles" && c.Gesture.Contains("Alt"));
            Assert.All(all, c => Assert.Contains(" ▸ ", c.Path));
            // every word typed must be in the command; the command's own name ranks first
            var m = CommandPalette.Match(all, "paste tiles");
            Assert.Equal("Edit ▸ Paste tiles", m[0].Path);
            Assert.Empty(CommandPalette.Match(all, "no such command at all"));
            Assert.Equal(all.Count, CommandPalette.Match(all, "").Count);
            // the palette runs what is picked: a ticked item toggles, a command gets its click
            bool grid = vm.ShowGrid;
            var p = w.ShowCommandPalette();
            Ui.Pump();
            p.Text = "grid";
            Ui.Pump();
            Assert.Contains(p.Shown, c => c.Path == "View ▸ Grid");
            Assert.Equal("View ▸ Grid", p.Shown[0].Path);
            Assert.True(p.Run());
            Ui.Pump();
            Assert.NotEqual(grid, vm.ShowGrid);
            // a command that cannot run now stays greyed and does not run
            var none = w.PaletteCommands().First(c => c.Path == "Edit ▸ Delete the object");
            vm.Select(-1, -1, -1, true);
            Assert.False(w.PaletteCommands().First(c => c.Path == "Edit ▸ Delete the object").Enabled);
            w.Close();
        }

        [AvaloniaFact]
        public void The_strings_window_writes_a_translation_table_and_reads_a_translation_back()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var sw = w.ShowStrings(4, 0);
            Ui.Pump();
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uwed-table-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                foreach (string ext in new[] { "tsv", "csv", "po" })
                {
                    string path = System.IO.Path.Combine(dir, "xx.strings." + ext);
                    Assert.True(sw.ExportTable(path));
                    Assert.True(new System.IO.FileInfo(path).Length > 10000);
                    var same = sw.ImportTable(path);
                    Assert.NotNull(same);
                    Assert.Equal(0, same.Changed);
                }
                Assert.False(sw.Model.HasChanges);
                // a translation of one item name
                string tr = System.IO.Path.Combine(dir, "de.strings.tsv");
                System.IO.File.WriteAllText(tr, "4" + (char)9 + "0" + (char)9 + "eine_Axt" + (char)10);
                var res = sw.ImportTable(tr);
                Assert.Equal(1, res.Changed);
                Assert.True(sw.Model.HasChanges);
                Assert.Equal("eine_Axt", w.Strings().Get(4, 0));
                Assert.Contains("1 string changed", sw.Model.Status);
                Assert.Null(sw.ImportTable(System.IO.Path.Combine(dir, "strings.json")));
            }
            finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
            sw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void A_prefab_is_saved_taken_and_pasted_and_a_decoration_is_rolled_again_in_place()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            string name = "test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var o = vm.Level.Objects.First(x => !x.Free && x.OnTile && !x.IsMobile && !x.IsTrapOrTrigger && x.TileX > 2 && x.TileX < 58 && x.TileY > 2 && x.TileY < 58);
            var tiles = new List<int>();
            for (int y = o.TileY; y < o.TileY + 3; y++) for (int x = o.TileX; x < o.TileX + 3; x++) tiles.Add(x + 64 * y);
            try
            {
                Assert.False(w.SavePrefab(name));                       // nothing copied yet
                w.AddToSelection(null, tiles);
                Assert.True(w.CopyTiles());
                Assert.True(w.SavePrefab(name));
                var pw = w.ShowPrefabs();
                Ui.Pump();
                var info = pw.Shown.First(p => p.Name == name);
                Assert.Equal(9, info.Tiles);
                // another copy replaces the clip; taking the prefab brings the nine tiles back
                w.ClearSelectionAll();
                vm.Select(-1, o.TileX, o.TileY, true);
                Assert.True(w.CopyTiles());
                Assert.Equal(1, w.CopiedTiles.TileCount);
                Assert.True(w.UsePrefab(info));
                Assert.Equal(9, w.CopiedTiles.TileCount);
                var res = w.PasteTiles(o.TileX < 30 ? 56 : 3, o.TileY < 30 ? 56 : 3);
                Assert.Equal(9, res.Tiles);
                w.Undo();
                Assert.True(w.DeletePrefab(info));
                Assert.DoesNotContain(w.ListPrefabs(), p => p.Name == name);
                pw.Close();
            }
            finally { foreach (var p in w.ListPrefabs().Where(p => p.Name == name)) System.IO.File.Delete(p.Path); }

            // decorate: a roll, another roll in its place, thicker, and removed
            var dw = w.ShowDecorate();
            Ui.Pump();
            Assert.Equal(Decoration.Kinds, dw.KindsOffered.ToArray());
            w.ClearSelectionAll();
            Assert.Null(dw.Scatter());                                  // no tiles selected
            var region = new List<int>();
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (!vm.Level.Tiles[x, y].IsSolid && region.Count < 120) region.Add(x + 64 * y);
            w.AddToSelection(null, region);
            var before = (byte[])vm.Editor.Data.Clone();
            int steps = vm.Editor.UndoLabels.Count;
            dw.Density = 20;
            var first = dw.Scatter();
            Assert.True(first.Made.Count > 10);
            Assert.Equal(steps + 1, vm.Editor.UndoLabels.Count);
            Assert.True(w.DecorationCanBeReplaced);
            var afterFirst = (byte[])vm.Editor.Data.Clone();
            var second = dw.NewRoll();
            Assert.Equal(steps + 1, vm.Editor.UndoLabels.Count);      // in the first one's place, not on top of it
            Assert.Equal(first.Made.Count, second.Made.Count);
            Assert.NotEqual(afterFirst, vm.Editor.Data);
            var thick = dw.Thicker();
            Assert.True(thick.Made.Count > second.Made.Count);
            Assert.Equal(steps + 1, vm.Editor.UndoLabels.Count);
            dw.SetKind("bones", false); dw.SetKind("debris", false); dw.SetKind("plants", false); dw.SetKind("stalactites", false);
            var moss = dw.Thinner();
            Assert.Single(moss.ByKind);
            Assert.True(moss.ByKind.ContainsKey("moss"));
            Assert.True(w.RemoveDecoration());
            Assert.Equal(before, vm.Editor.Data);
            Assert.False(w.RemoveDecoration());
            // after another edit the next roll adds a step of its own
            dw.Scatter();
            vm.Select(o.Index, -1, -1, true);
            vm.EditField(vm.ObjectRows.First(r => r.Name == "Quality"), "3");
            Assert.False(w.DecorationCanBeReplaced);
            dw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public async System.Threading.Tasks.Task The_find_window_filters_three_ways_and_replaces_in_its_results()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var find = w.ShowFind();
            Ui.Pump();
            Assert.False(find.Model.CanReplace);
            // food on this level only, lying about (not in a container)
            find.Model.KindIndex = find.Model.Kinds.IndexOf("Food");
            find.Model.ThisLevelOnly = true;
            find.Model.InContainer = false;
            await find.SearchAsync();
            Assert.NotEmpty(find.Model.Hits);
            Assert.All(find.Model.Hits, h => Assert.Equal(vm.Level.Level, h.Hit.Level));
            Assert.All(find.Model.Hits, h => Assert.True(vm.Level.Objects[h.Hit.Index].Parent < 0));
            Assert.True(find.Model.CanReplace);
            int n = find.Model.Hits.Count;
            var before = (byte[])vm.Editor.Data.Clone();
            // what the boxes do not say is refused with the reason
            find.Model.ReplaceMode = 1;
            find.Model.ReplaceFieldIndex = find.Model.ReplaceFields.IndexOf("Quality");
            find.Model.ReplaceValue = "";
            Assert.Null(await find.ReplaceAsync());
            Assert.Contains("Type the value", find.Model.Status);
            find.Model.ReplaceValue = "33";
            var res = await find.ReplaceAsync();
            Assert.Equal(n, res.Changed + res.Same);
            Assert.All(find.Model.Hits, h => Assert.Equal(33, vm.Editor.Model.Objects[h.Hit.Index].Quality));
            Assert.Contains("Replaced", find.Model.Status);
            w.Undo();
            Assert.Equal(before, vm.Editor.Data);
            // become another item, named
            find.Model.ReplaceMode = 0;
            find.Model.ReplaceItem = "no such thing";
            Assert.Null(await find.ReplaceAsync());
            int to = find.Model.Hits[0].Hit.Item;
            find.Model.ReplaceItem = "0x" + to.ToString("X");
            var res2 = await find.ReplaceAsync();
            Assert.Equal(n, res2.Changed + res2.Same);
            Assert.All(find.Model.Hits, h => Assert.Equal(to, h.Hit.Item));
            find.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_panel_names_a_traps_fields_and_the_menu_adds_copies_and_removes_a_doors_lock()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            // a level with a teleport trap, a bare door and a locked door
            ObjectInfo tp = null, bare = null, locked = null;
            foreach (var slot in vm.Session.LevelSlots().Where(x => x.Present))
            {
                vm.LoadLevel(slot.Level);
                var m = vm.Level;
                Func<ObjectInfo, bool> door = o => !o.Free && o.OnTile && (ItemCategories.Of(o.Item, vm.Session.Kind) & ItemCategory.Door) != 0;
                tp = m.Objects.FirstOrDefault(o => !o.Free && o.TileX >= 0 && o.Item == 0x181);
                bare = m.Objects.FirstOrDefault(o => door(o) && vm.Editor.LockOf(o.Index) == null && o.Children.Count == 0);
                locked = m.Objects.FirstOrDefault(o => door(o) && vm.Editor.LockOf(o.Index) != null);
                if (tp != null && bare != null && locked != null) break;
            }
            Assert.NotNull(tp); Assert.NotNull(bare); Assert.NotNull(locked);
            // the typed row edits the record's field
            vm.Select(tp.Index, -1, -1, true);
            var dest = vm.ObjectRows.First(r => r.Name == "Destination X");
            Assert.Equal(tp.Quality.ToString(), dest.Value);
            vm.EditField(dest, "11");
            vm.FlushRefresh();
            Assert.Equal(11, vm.Level.Objects[tp.Index].Quality);
            Assert.Equal("11", vm.ObjectRows.First(r => r.Name == "Quality").Value);
            w.Undo();
            // the door's menu: add, copy, remove
            var h = Headers(w.ContextItems(new PickResult { Object = bare.Index, Tile = bare.TileX + 64 * bare.TileY }));
            Assert.Contains("Add a lock…", h);
            Assert.DoesNotContain("Remove its lock", h);
            Assert.True(w.AddLock(bare.Index, 44));
            var lk = vm.Level.Objects[vm.SelectedObject];
            Assert.Equal(0x10F, lk.Item);
            Assert.Equal("44", vm.ObjectRows.First(r => r.Name == "Lock number").Value);
            Assert.Contains("Remove its lock", Headers(w.ContextItems(new PickResult { Object = bare.Index, Tile = bare.TileX + 64 * bare.TileY })));
            Assert.True(w.CopyLock(locked.Index, bare.Index));
            Assert.Equal(vm.Editor.LockOf(locked.Index).Link, vm.Editor.LockOf(bare.Index).Link);
            Assert.True(w.RemoveLock(bare.Index));
            Assert.Null(vm.Editor.LockOf(bare.Index));
            Assert.False(w.RemoveLock(bare.Index));
            bool ok; vm.Editor.Check(out ok); Assert.True(ok);
            w.Close();
        }
    }
}
