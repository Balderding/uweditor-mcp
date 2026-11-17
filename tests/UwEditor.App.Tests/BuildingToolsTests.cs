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
    }
}
