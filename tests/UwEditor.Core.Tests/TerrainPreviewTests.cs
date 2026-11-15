using System;
using System.Linq;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Tools ▸ Preview changed terrain: the list of a level's terrain changes and the changed copy.</summary>
    public class TerrainPreviewTests
    {
        readonly ITestOutputHelper output;
        public TerrainPreviewTests(ITestOutputHelper output) { this.output = output; }

        static GameSession Open(string name)
        {
            string g = GameData.Game(name);
            return g == null ? null : GameSession.Open(g);
        }

        [Fact]
        public void Uw2_level_26_lists_the_trap_that_opens_33_33_and_applying_it_opens_a_copy_only()
        {
            var s = Open("UW2");
            if (s == null) return;
            var m = s.LoadLevel(26);
            var list = TerrainPreview.List(s, m);
            foreach (var c in list) output.WriteLine(c.Text);
            Assert.True(m.Tiles[33, 33].IsSolid);
            var opens = list.Where(c => !c.Oscillator && c.Covers(33, 33)).ToList();
            Assert.NotEmpty(opens);
            var open = opens.First(c => { int t, h, f, w; TerrainPreview.Values(s, c.Trap, out t, out h, out f, out w); return t > 0; });
            Assert.Equal(TerrainPreview.ChangeTerrainTrap, open.Trap.Item);
            Assert.Contains("(33, 33)", open.What);
            Assert.Contains("open", open.What);
            Assert.Contains("#" + open.Trap.Index, open.Text);

            byte[] before = (byte[])m.Block.Data.Clone();
            var copy = TerrainPreview.Apply(m, new[] { open });
            Assert.NotSame(m, copy);
            Assert.False(copy.Tiles[33, 33].IsSolid);
            Assert.True(m.Tiles[33, 33].IsSolid);                      // the level itself is untouched
            Assert.Equal(before, m.Block.Data);
            Assert.Contains(33 * 64 + 33, TerrainPreview.Differences(m, copy));
            // only squares of the rectangle changed
            Assert.All(TerrainPreview.Differences(m, copy), t => Assert.True(open.Covers(t % 64, t / 64)));
        }

        [Theory]
        [InlineData("UW", 1)]
        [InlineData("UW", 7)]
        [InlineData("UW2", 26)]
        [InlineData("UW2", 1)]
        public void Applying_nothing_gives_an_identical_copy(string game, int level)
        {
            var s = Open(game);
            if (s == null) return;
            var m = s.LoadLevel(level);
            foreach (var none in new[] { null, new TerrainChange[0] })
            {
                var copy = TerrainPreview.Apply(m, none);
                Assert.NotSame(m, copy);
                Assert.NotSame(m.Tiles[5, 5], copy.Tiles[5, 5]);
                Assert.Empty(TerrainPreview.Differences(m, copy));
                Assert.Equal(m.Block.Data, copy.Block.Data);
                Assert.Equal(m.OpenTileCount, copy.OpenTileCount);
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_level_lists_its_change_terrain_traps_in_index_order_and_all_apply(string game)
        {
            var s = Open(game);
            if (s == null) return;
            int total = 0;
            for (int l = 1; l <= s.Profile.LevelSlots; l++)
            {
                var m = s.LoadLevel(l);
                if (!m.Present) { Assert.Empty(TerrainPreview.List(s, m)); continue; }
                var list = TerrainPreview.List(s, m);
                total += list.Count;
                // every change-terrain trap lying on the level is in the list
                foreach (var o in m.Objects.Where(o => !o.Free && o.TileX >= 0 && o.Item == TerrainPreview.ChangeTerrainTrap))
                    Assert.Contains(list, c => c.Trap == o);
                Assert.Equal(list.Select(c => c.Trap.Index).OrderBy(i => i), list.Select(c => c.Trap.Index));
                Assert.All(list, c => Assert.False(string.IsNullOrEmpty(c.What)));
                if (!s.IsUw2) Assert.DoesNotContain(list, c => c.Oscillator);
                var copy = TerrainPreview.Apply(m, list);
                var diff = TerrainPreview.Differences(m, copy);
                Assert.All(diff, t => Assert.Contains(list, c => c.Covers(t % 64, t / 64)));
                // the order given does not matter
                var back = TerrainPreview.Apply(m, Enumerable.Reverse(list));
                Assert.Empty(TerrainPreview.Differences(copy, back));
            }
            Assert.True(total > 0);
        }

        [Fact]
        public void An_oscillator_in_height_mode_sets_its_square_to_the_chosen_height()
        {
            var s = Open("UW2");
            if (s == null) return;
            bool found = false;
            for (int l = 1; l <= s.Profile.LevelSlots && !found; l++)
            {
                var m = s.LoadLevel(l);
                if (!m.Present) continue;
                var osc = TerrainPreview.List(s, m).FirstOrDefault(c => c.Oscillator && c.Low < c.High && c.X < 64 && c.Y < 64);
                if (osc == null) continue;
                found = true;
                output.WriteLine("level " + l + ": " + osc.Text);
                Assert.Equal(TerrainPreview.OscillatorTrap, osc.Trap.Item);
                Assert.InRange(osc.Height, osc.Low, osc.High);
                foreach (int h in new[] { osc.Low, osc.High })
                {
                    osc.Height = h;
                    var copy = TerrainPreview.Apply(m, new[] { osc });
                    Assert.Equal(h, copy.Tiles[osc.X, osc.Y].Height);
                    Assert.All(TerrainPreview.Differences(m, copy), t => Assert.Equal(osc.Y * 64 + osc.X, t));
                }
                osc.Height = 99;                                       // kept within its ends
                Assert.Equal(osc.High, TerrainPreview.Apply(m, new[] { osc }).Tiles[osc.X, osc.Y].Height);
            }
            Assert.True(found, "no height oscillator in UW2");
        }
    }
}
