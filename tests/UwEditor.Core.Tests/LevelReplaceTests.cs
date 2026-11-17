using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The finder's extra filters and Replace across levels (ObjectQuery, LevelReplace).</summary>
    public class LevelReplaceTests
    {
        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_filters_narrow_the_search_three_ways_and_by_quality_and_level(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            int all = LevelSearch.Run(s, new ObjectQuery(), 100000).Count;
            Func<ObjectQuery, List<SearchHit>> run = q => LevelSearch.Run(s, q, 100000);
            Func<SearchHit, ObjectInfo> obj = h => s.LoadLevel(h.Level).Objects[h.Index];

            // each three-way filter splits the whole into two parts
            foreach (var pick in new Func<bool?, ObjectQuery>[] {
                v => new ObjectQuery { Enchanted = v }, v => new ObjectQuery { Invisible = v }, v => new ObjectQuery { IsQuantity = v }, v => new ObjectQuery { InContainer = v } })
            {
                int yes = run(pick(true)).Count, no = run(pick(false)).Count;
                Assert.Equal(all, yes + no);
                Assert.True(yes > 0 && no > 0);
            }
            Assert.All(run(new ObjectQuery { Enchanted = true }), h => Assert.True(obj(h).Enchanted));
            Assert.All(run(new ObjectQuery { InContainer = true }), h => Assert.True(obj(h).Parent >= 0));
            Assert.All(run(new ObjectQuery { Invisible = false, IsQuantity = true }), h => Assert.True(!obj(h).Invisible && obj(h).IsQuantity));
            // owned asks items only: creatures and traps use the owner field for something else
            var owned = run(new ObjectQuery { Owned = true });
            Assert.NotEmpty(owned);
            Assert.All(owned, h => Assert.True(obj(h).Owner != 0 && !obj(h).IsCritter && !obj(h).IsTrapOrTrigger));
            // quality range and one level
            var q10 = run(new ObjectQuery { QualityMin = 10, QualityMax = 20 });
            Assert.All(q10, h => Assert.InRange(obj(h).Quality, 10, 20));
            Assert.Equal(all, run(new ObjectQuery { QualityMax = 9 }).Count + q10.Count + run(new ObjectQuery { QualityMin = 21 }).Count);
            int level = s.LevelSlots().First(x => x.Present).Level;
            var one = run(new ObjectQuery { OnlyLevel = level });
            Assert.NotEmpty(one);
            Assert.All(one, h => Assert.Equal(level, h.Level));
            Assert.False(new ObjectQuery { OnlyLevel = level }.IsEmpty);
            Assert.True(new ObjectQuery().IsEmpty);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Replace_changes_every_result_on_every_level_as_one_step_and_refuses_what_does_not_fit(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            // the food item with the most copies in the world becomes another food item
            var foods = LevelSearch.Run(s, new ObjectQuery { Categories = ItemCategory.Food }, 100000);
            int from = foods.GroupBy(h => h.Item).OrderByDescending(x => x.Count()).First().Key;
            int to = foods.Select(h => h.Item).Distinct().First(i => i != from);
            var hits = LevelSearch.Run(s, new ObjectQuery { Text = "0x" + from.ToString("X") }, 100000);
            Assert.True(hits.Select(h => h.Level).Distinct().Count() > 1);
            var before = hits.Select(h => h.Level).Distinct().ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());

            var res = LevelReplace.Apply(edits, hits, new ReplaceAction { NewItem = to });
            Assert.Equal(hits.Count, res.Changed);
            Assert.Equal(0, res.Refused);
            Assert.Equal(before.Count, res.Levels.Count);
            foreach (var h in hits) Assert.Equal(to, edits.Model(h.Level).Objects[h.Index].Item);
            foreach (int l in before.Keys) { bool ok; edits.For(l).Check(out ok); Assert.True(ok); Assert.Equal(1, edits.For(l).UndoLabels.Count); }
            Assert.Empty(LevelSearch.Run(s, new ObjectQuery { Text = "0x" + from.ToString("X") }, 100000, l => edits.Model(l)));
            // again: nothing left to change
            var again = LevelReplace.Apply(edits, hits, new ReplaceAction { NewItem = to });
            Assert.Equal(0, again.Changed);
            Assert.Equal(hits.Count, again.Same);
            // one undo on any of the levels takes the whole replace back
            string refused;
            var undone = edits.Undo(before.Keys.First(), out refused);
            Assert.Equal(before.Count, undone.Count);
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);

            // a field set on all: quality
            var set = LevelReplace.Apply(edits, hits, new ReplaceAction { Field = "Quality", Value = 9 });
            Assert.Equal(hits.Count, set.Changed + set.Same);
            foreach (var h in hits) Assert.Equal(9, edits.Model(h.Level).Objects[h.Index].Quality);
            edits.Undo(before.Keys.First(), out refused);

            // what does not fit is refused and counted, the rest goes through: items cannot become creatures
            var mixed = LevelReplace.Apply(edits, hits.Take(5), new ReplaceAction { NewItem = 0x40 });
            Assert.Equal(0, mixed.Changed);
            Assert.Equal(5, mixed.Refused);
            Assert.Contains("creature", mixed.Why, StringComparison.OrdinalIgnoreCase);
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
            // values out of range and unknown fields are refused before anything changes
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelReplace.Apply(edits, hits, new ReplaceAction { Field = "Quality", Value = 64 }));
            Assert.Throws<ArgumentException>(() => LevelReplace.Apply(edits, hits, new ReplaceAction { Field = "Next", Value = 1 }));
            Assert.Contains("Quality", LevelReplace.Fields);
            Assert.DoesNotContain("Next", LevelReplace.Fields);
        }
    }
}
