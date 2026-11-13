using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW2's floors that move the player, and its abilities: water with a current is one way
    /// unless the player is over the water, an ice slope is not walked up, a spring pad throws
    /// him onto what it reaches, and the spells and enchanted things of UW2 give the abilities
    /// the ways ask for.
    /// </summary>
    public class Uw2TerrainTests
    {
        static NavGraph graph;
        static bool tried;

        static NavGraph Graph()
        {
            if (tried) return graph;
            tried = true;
            string g = GameData.Game("UW2");
            if (g == null) return null;
            var s = GameSession.Open(g);
            return graph = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), null);
        }

        [Fact]
        public void A_stream_is_one_way()
        {
            var g = Graph(); if (g == null) return;
            Assert.Equal(new[] { 4, 5, 25, 26, 42, 50, 52, 57, 65 }, g.StreamTiles.Keys.OrderBy(x => x).ToArray());
            Assert.Equal(38, g.StreamTiles[42]);
            // the Academy, level 42: the stream by (8, 22) is a region of its own
            int stream = g.RegionOf(42, 8, 22), bank = g.RegionOf(42, 8, 21);
            Assert.NotEqual(stream, bank);
            var ways = g.Edges.Where(e => e.Level == 42 && (e.From == stream || e.To == stream)).ToList();
            // in with the current and out where it brings the swimmer are free
            Assert.Contains(ways, e => e.From == bank && e.To == stream && e.X == 8 && e.Y == 21 && e.Requires == "free");
            Assert.Contains(ways, e => e.From == stream && e.Requires == "free" && e.Notes.StartsWith("out of the stream, where"));
            // against it only over the water
            var against = ways.Where(e => e.Notes.Contains("against")).ToList();
            Assert.NotEmpty(against);
            Assert.All(against, e => Assert.Contains("spell:waterwalk | spell:levitate", e.Requires));
            // nothing free leads into a stream against its current, anywhere
            Assert.All(g.Edges.Where(e => e.Notes == "into the stream against its current"), e => Assert.NotEqual("free", e.Requires));
            // a bank the current carries the swimmer to is a way of its own, and never back the same way for free
            var carried = g.Edges.Where(e => e.Notes.StartsWith("carried by the current")).ToList();
            Assert.NotEmpty(carried);
            Assert.All(carried, e => Assert.Equal("free", e.Requires));
            Assert.All(carried, e => Assert.NotEqual(e.From, e.To));
            // UW1 has no currents
            string uw1 = GameData.Game("UW");
            if (uw1 != null)
            {
                var s1 = GameSession.Open(uw1);
                var g1 = NavGraph.Build(s1, l => s1.LoadLevel(l), new NavOptions(), new[] { 1, 4 });
                Assert.Empty(g1.StreamTiles);
                Assert.Empty(g1.IceSlopes);
            }
        }

        [Fact]
        public void An_ice_slope_is_not_walked_up()
        {
            var g = Graph(); if (g == null) return;
            Assert.Equal(new[] { 4, 33, 65, 72 }, g.IceSlopes.Keys.OrderBy(x => x).ToArray());
            // level 4: the slope rising east from (50, 61): each tile a region, up only off the ground, down free
            int low = g.RegionOf(4, 50, 61), high = g.RegionOf(4, 51, 61);
            Assert.NotEqual(low, high);
            Assert.Contains(g.Edges, e => e.From == low && e.To == high && e.Requires == "spell:levitate");
            Assert.Contains(g.Edges, e => e.From == high && e.To == low && e.Requires == "free");
            Assert.DoesNotContain(g.Edges, e => e.From == low && e.To == high && e.Requires == "free");
        }

        [Fact]
        public void A_spring_pad_throws_the_player_up()
        {
            var g = Graph(); if (g == null) return;
            var pads = g.Edges.Where(e => e.Notes.StartsWith("spring pad")).ToList();
            Assert.Equal(new[] { 33, 57, 65 }, pads.Select(e => e.Level).Distinct().OrderBy(x => x).ToArray());
            Assert.All(pads, e => Assert.Equal("jump", e.Kind));
            Assert.All(pads, e => Assert.Equal("free", e.Requires));
            // Talorus, level 33: the pad at (43, 39) lifts the player over the ice slope to (45, 39)
            Assert.Contains(pads, e => e.X == 43 && e.Y == 39 && e.From == g.RegionOf(33, 43, 39) && e.To == g.RegionOf(33, 45, 39));
            // a pad that stops the player dead only lifts him: level 46's is left out
            Assert.DoesNotContain(pads, e => e.Level == 46);
        }

        [Fact]
        public void Uw2_spells_give_the_abilities()
        {
            // uw2 combat/SPELLS.C spells[]: 14 Leaping, 23 Water Walk, 28 Flameproof, 32 Levitate, 35 Open, 39 Telekinesis, 43 Gate Travel, 50 Reveal, 57 Fly, 6 Bouncing
            Assert.Equal(69, Abilities.SpellCount(true));
            Assert.Equal("jump", Abilities.OfSpell(true, 14));
            Assert.Equal("waterwalk", Abilities.OfSpell(true, 23));
            Assert.Equal("lava", Abilities.OfSpell(true, 28));
            Assert.Equal("levitate", Abilities.OfSpell(true, 32));
            Assert.Equal("levitate", Abilities.OfSpell(true, 57));
            Assert.Equal("open", Abilities.OfSpell(true, 35));
            Assert.Equal("telekinesis", Abilities.OfSpell(true, 39));
            Assert.Equal("gate_travel", Abilities.OfSpell(true, 43));
            Assert.Equal("reveal", Abilities.OfSpell(true, 50));
            Assert.Null(Abilities.OfSpell(true, 6));        // Bouncing is not in the logic
            Assert.Null(Abilities.OfSpell(true, 69));
            // the runes: Leaping is Uus Por, Open Ex Ylem
            Assert.Equal(new[] { "Uus", "Por" }, Abilities.Runes(true, 14).Select(r => Abilities.RuneNames[r]).ToArray());
            Assert.Equal(new[] { "Ex", "Ylem" }, Abilities.Runes(true, 35).Select(r => Abilities.RuneNames[r]).ToArray());
            Assert.Equal(new[] { 32, 57 }, Abilities.SpellsGiving(true, "levitate").ToArray());
            // UW1's table is as it was
            Assert.Equal(53, Abilities.SpellCount(false));
            Assert.Equal(Abilities.Uw1Runes(4), Abilities.Runes(false, 4));
        }

        [Fact]
        public void Uw2_things_give_the_abilities_and_the_solve_uses_them()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            var src = t.Abilities.Sources;
            // a scroll and a potion are used up, a thing worn works while worn; a spell object counts through its wand only
            Assert.Contains(src, x => x.Level == 25 && x.Token == "once:waterwalk" && x.Kind == "scroll");
            Assert.Contains(src, x => x.Item == 0xE2 && x.Token == "once:lava" && x.Kind == "potion");
            Assert.Contains(src, x => x.Token == "worn:lava" && x.Worn);
            Assert.DoesNotContain(src, x => x.Item == 0x120);
            // the runes cast: the rune bag and the stones
            Assert.Contains(t.Set.Placements, p => p.Grants == "cast:jump" && p.Requires.Contains("item:rune_bag") && p.Requires.Contains("rune:uus") && p.Requires.Contains("rune:por"));
            Assert.Contains("cast:open", t.Solve.TokenSphere.Keys);
            // with them the solve gets through the Academy
            var levels = t.Solve.ZoneSphere.Keys.Where(z => z < 1000000).SelectMany(z => t.Set.LevelsOf(z)).Distinct().ToList();
            foreach (int l in new[] { 44, 45, 46, 47, 48 }) Assert.Contains(l, levels);
            Assert.True(t.Solve.ZoneSphere.Count > 150, t.Solve.ZoneSphere.Count + " zones");
        }

        [Fact]
        public void The_jump_is_tried_plain_or_with_the_spell()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var m = s.LoadLevel(65);
            // level 65, (29, 30) to (32, 27), diagonally: whatever lands, a plain try never uses the spell and a spell try always does
            var plain = JumpTrace.Try(m, 29, 30, 32, 27, spell: false);
            var spell = JumpTrace.Try(m, 29, 30, 32, 27, spell: true);
            var either = JumpTrace.Try(m, 29, 30, 32, 27);
            Assert.False(plain.Leap);
            Assert.True(spell.Leap);
            Assert.True(plain.Path.Count > 0 && spell.Path.Count > 0);
            Assert.Equal(plain.Lands || spell.Lands, either.Lands);
            if (!plain.Lands) Assert.DoesNotContain("spell does not help", plain.Text);
            if (!spell.Lands) Assert.Contains("with the jump spell", spell.Text);
        }
    }
}
