using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Texts that follow their objects (ObjectTextEdits): a string of its own, copied with the object, freed with the last reader.</summary>
    public class ObjectTextEditsTests
    {
        /// <summary>The first object of any level that reads a text of a block, with its level.</summary>
        static (int Level, ObjectInfo O) Reader(GameSession s, LevelEdits edits, StringsEditor st, Func<ObjectInfo, bool> which)
        {
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var m = edits.Model(slot.Level);
                var o = m.Objects.FirstOrDefault(x => !x.Free && x.OnTile && which(x) && !string.IsNullOrEmpty(ObjectTextEdits.TextOf(s.Kind, x, st)?.Text));
                if (o != null) return (slot.Level, o);
            }
            return (-1, null);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_copied_book_takes_a_copy_of_its_text_and_a_deleted_one_frees_it(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var st = StringsEditor.Open(s);
            var found = Reader(s, edits, st, o => (ItemCategories.Of(o.Item, s.Kind) & (ItemCategory.Book | ItemCategory.Scroll)) != 0);
            Assert.NotNull(found.O);
            var book = found.O;
            var e = edits.For(found.Level);
            var was = ObjectTextEdits.TextOf(s.Kind, book, st);
            Assert.Equal(ObjectTexts.TextOf(s, book), st.ToGame(was.Text));
            int users = ObjectTextEdits.Users(edits, was.Block, was.Index);

            // a copy beside it reads the same string, until it is given its own
            var made = e.Paste(e.Copy(new[] { book.Index }), book.TileX, book.TileY);
            Assert.Equal(users + 1, ObjectTextEdits.Users(edits, was.Block, was.Index));
            int shared;
            var copies = ObjectTextEdits.GiveCopiesOwnTexts(edits, found.Level, made, st, out shared);
            var own = Assert.Single(copies);
            Assert.Equal(0, shared);
            Assert.Equal(was.Block, own.Block);
            Assert.NotEqual(was.Index, own.Index);
            Assert.Equal(was.Text, st.Get(own.Block, own.Index));
            var copy = e.Model.Objects[made[0]];
            Assert.Equal(own.Index, ObjectTextEdits.TextOf(s.Kind, copy, st).Index);
            Assert.Equal(was.Index, ObjectTextEdits.TextOf(s.Kind, e.Model.Objects[book.Index], st).Index);
            Assert.Equal(users, ObjectTextEdits.Users(edits, was.Block, was.Index));
            Assert.Equal(1, ObjectTextEdits.Users(edits, own.Block, own.Index));
            bool ok; e.Check(out ok); Assert.True(ok);
            // a second pass finds nothing left to do
            Assert.Empty(ObjectTextEdits.GiveCopiesOwnTexts(edits, found.Level, made, st, out shared));

            // its own text can be changed without touching the original's
            st.Set(own.Block, own.Index, "A NEW TEXT");
            Assert.Equal(was.Text, st.Get(was.Block, was.Index));

            // deleting the copy frees its string; the original's stays while it is read
            var under = ObjectTextEdits.TextsUnder(e.Model, new[] { made[0], book.Index }, st);
            Assert.Equal(2, under.Count);
            e.DeleteObject(made[0]);
            var freed = ObjectTextEdits.FreeUnused(edits, st, under);
            Assert.Equal(own.Index, Assert.Single(freed).Index);
            Assert.Equal("A NEW TEXT", freed[0].Text);
            Assert.Equal("", st.Get(own.Block, own.Index));
            Assert.Equal(was.Text, st.Get(was.Block, was.Index));

            // the freed string is the one the next new text takes
            var again = ObjectTextEdits.GiveOwnText(edits, found.Level, book.Index, st, "SECOND");
            Assert.Equal(own.Index, again.Index);
            Assert.Equal("SECOND", ObjectTextEdits.TextOf(s.Kind, e.Model.Objects[book.Index], st).Text);
        }

        [Fact]
        public void Gravestones_writings_and_text_traps_get_strings_in_their_own_ranges()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var st = StringsEditor.Open(s);
            foreach (var (item, block, lo, hi) in new[] { (0x165, 8, 1, 63), (0x166, 8, 64, 351), (0x190, 9, 1, 2047) })
            {
                var found = Reader(s, edits, st, o => o.Item == item);
                if (found.O == null) continue;
                var was = ObjectTextEdits.TextOf(s.Kind, found.O, st);
                var e = edits.For(found.Level);
                var before = (byte[])e.Data.Clone();
                var own = ObjectTextEdits.GiveOwnText(edits, found.Level, found.O.Index, st, "HERE LIES A TEST");
                Assert.Equal(block, own.Block);
                Assert.InRange(own.Index, lo, hi);
                Assert.NotEqual(was.Index, own.Index);
                var now = ObjectTextEdits.TextOf(s.Kind, e.Model.Objects[found.O.Index], st);
                Assert.Equal(own.Index, now.Index);
                Assert.Equal("HERE LIES A TEST", now.Text);
                Assert.Equal(was.Text, st.Get(was.Block, was.Index));
                bool ok; e.Check(out ok); Assert.True(ok);
                // one undo step puts the object back
                e.Undo();
                Assert.Equal(before, e.Data);
            }
        }

        [Fact]
        public void An_object_without_a_text_and_a_letter_the_file_cannot_write_are_refused()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var st = StringsEditor.Open(s);
            var e = edits.For(1);
            var plain = e.Model.Objects.First(o => !o.Free && o.OnTile && !o.IsMobile && o.Item < 0x40);
            var ex = Assert.Throws<InvalidOperationException>(() => ObjectTextEdits.GiveOwnText(edits, 1, plain.Index, st, "X"));
            Assert.Contains("carries no text", ex.Message);
            var found = Reader(s, edits, st, o => (ItemCategories.Of(o.Item, s.Kind) & (ItemCategory.Book | ItemCategory.Scroll)) != 0);
            var e2 = edits.For(found.Level);
            var before = (byte[])e2.Data.Clone();
            int count = st.Get(3).Count;
            Assert.ThrowsAny<Exception>(() => ObjectTextEdits.GiveOwnText(edits, found.Level, found.O.Index, st, "中文"));
            Assert.Equal(before, e2.Data);
            Assert.Equal(count, st.Get(3).Count);
        }
    }
}
