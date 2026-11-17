using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Gifts a conversation hands over only for a time (the facts' mayBeLost of kind b): which of
    /// them a start cannot get (TimedGifts.Missed), the object a fact names for its gift ("gift"),
    /// and the key item shuffle's made items, which stand in for a gift the start misses.
    /// </summary>
    public class TimedGiftTests
    {
        [Fact]
        public void The_gift_is_read_from_the_facts()
        {
            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            Func<string, FactGift> G = id => uw2.Facts.Single(f => f.Id == id).Gift;
            Assert.Equal((0x10D, 28, 63, 1, true), (G("dupre_sewer_key").Item, G("dupre_sewer_key").Owner, G("dupre_sewer_key").Quality, G("dupre_sewer_key").Link, G("dupre_sewer_key").IsQuantity));
            Assert.Equal((0x10D, 2), (G("charles_key").Item, G("charles_key").Owner));
            Assert.Equal((0x0A, 40, 707, true), (G("altara_dagger").Item, G("altara_dagger").Quality, G("altara_dagger").Link, G("altara_dagger").Enchanted));
            Assert.Equal((0x112, 576, 4, true), (G("altara_rod").Item, G("altara_rod").Link, G("altara_rod").Flags, G("altara_rod").Enchanted));
            var spec = G("dupre_sewer_key").Spec();
            Assert.Equal((0x10D, 28, 63, 1, true), (spec.Item, spec.Owner, spec.Quality, spec.Link, spec.IsQuantity));
            var bad = FactSet.FromJson(@"{ ""facts"": [
                { ""id"": ""no_item"", ""kind"": ""flag"", ""grants"": ""key:3"", ""level"": 1, ""object"": 5, ""requires"": ""free"", ""gift"": { ""owner"": 3 } },
                { ""id"": ""not_flag"", ""kind"": ""craft"", ""grants"": ""key:3"", ""requires"": ""free"", ""gift"": { ""item"": ""0x10D"" } } ] }").Check();
            Assert.Contains(bad, x => x.StartsWith("no_item: a gift needs 'item'"));
            Assert.Contains(bad, x => x.StartsWith("not_flag: only a flag fact"));
        }

        /// <summary>
        /// UW2: from the game's own start the solve takes Dupre's key at castle plot 0. From level 1
        /// (16, 30) it steps on level 3's sewer squares (15, 53), (16, 53) (move triggers #571, #573:
        /// set variable trap #572, plot 1) before Lord British's speech lets Dupre speak, so the key
        /// is missed; the game still ends, since no way of the logic needs key 28 (the door #947 at
        /// level 1 (15, 53) has both sides in one zone: the sewers are reached from level 3), so
        /// no key is made for it.
        /// </summary>
        [Fact]
        public void Uw2_a_start_away_from_dupre_misses_his_key_and_does_not_need_it()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var own = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions());
            Assert.DoesNotContain(TimedGifts.Missed(own, own.Solve), m => m.Fact.Id == "dupre_sewer_key");
            var at = new StartPoint { Level = 1, X = 16.5, Y = 30.5, Description = "test start" };
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { StartOverride = at }, new AbilityOptions(), null, null, variants: false);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Empty(t.Solve.CutOff);
            var missed = TimedGifts.Missed(t, t.Solve);
            var dupre = missed.Single(m => m.Fact.Id == "dupre_sewer_key");
            Assert.Equal("key:28", dupre.Token);
            Assert.True(dupre.GiverReached);
            Assert.Contains("after the window has closed", dupre.Why);
            Assert.Equal(28, dupre.Gift.Owner);
            Assert.False(t.Solve.TokenSphere.ContainsKey("key:28"));
            // Charles's key comes from level 3 and Altara's gifts in their windows: not missed
            Assert.Equal(new[] { "dupre_sewer_key" }, missed.Select(m => m.Fact.Id).ToArray());
            Assert.DoesNotContain(t.Set.Links.Concat(t.FactLinks), k => (k.Requires ?? "").Contains("key:28"));
            Assert.Empty(KeyShuffle.MadeFor(s, t, new AbilityOptions()));
        }

        /// <summary>
        /// UW1, a synthetic case: a test fact makes Bragit (level 1 #240) hand over key 40 (a lock
        /// number no key in the levels has), only for a time the start misses (kind b, no window
        /// known: never counted), and names the key as its gift; the goal asks for key 40 through
        /// the orb option's goal fact. The shuffle makes a key 40, the fill places it within reach,
        /// the fresh solve of the shuffled levels ends the game without Bragit's gift, Apply writes
        /// the key and Undo takes it away.
        /// </summary>
        [Fact]
        public void Uw1_a_made_key_stands_in_for_a_gift_the_start_misses()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            int keyItem = s.LoadLevel(1).Objects[948].Item;
            Assert.True(KeyIndex.IsKey(keyItem, s.Kind));
            Assert.Equal("Bragit", ObjectTexts.NpcName(s, s.LoadLevel(1).Objects[240]));
            var facts = FactSet.Shipped(false);
            facts.Facts.Single(f => f.Kind == "goal" && f.Option == "require_orb").Requires = "key:40";
            facts.Add(FactSet.FromJson(@"{ ""facts"": [ { ""id"": ""test_timed_gift"", ""kind"": ""flag"", ""grants"": ""key:40"", ""level"": 1, ""object"": 240, ""requires"": ""free"",
                ""mayBeLost"": { ""kind"": ""b"", ""why"": ""test: only before the game starts"" }, ""gift"": { ""item"": " + keyItem + @", ""owner"": 40, ""quality"": 40 } } ] }"));
            Assert.Empty(facts.Check());
            var opt = new KeyShuffleOptions { Seed = 3, Logic = new AbilityOptions { RequireOrb = true } };
            // the unshuffled game: the gift is never counted, so the goal is out of reach
            var bare = Timeline.Build(s, l => s.LoadLevel(l), opt.Nav, opt.Logic, null, facts, variants: false);
            Assert.False(bare.Solve.GoalReached);
            Assert.Equal(new[] { "test_timed_gift" }, TimedGifts.Missed(bare, bare.Solve).Select(m => m.Fact.Id).ToArray());
            Assert.Single(KeyShuffle.MadeFor(s, bare, opt.Logic));

            var r = KeyShuffle.Run(s, null, opt, facts);
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            var made = r.Objects.Single(o => o.Made != null);
            Assert.Equal("test_timed_gift", made.Made.Fact.Id);
            Assert.Equal("key:40", made.Tokens);
            var move = r.Moves.Single(m => m.Item == made);
            Assert.True(move.ByFill);
            Assert.True(move.Place.OnFloor);
            Assert.True(move.Sphere >= 0);
            // the fresh solve of the shuffled levels: the made key within reach, the goal reached, the timed source never used
            var check = r.Check;
            Assert.True(check.GoalReached, string.Join(", ", check.GoalMissing));
            Assert.True(check.ZoneSphere.ContainsKey(r.CheckBuild.Set.ZoneAt(move.Place.Level, move.Place.X, move.Place.Y)));
            Assert.True(check.TokenSphere.ContainsKey("key:40"));
            Assert.DoesNotContain(check.Events, e => e.Placement != null && e.Placement.Source == "facts: test_timed_gift");
            string log = r.Log.ToString();
            Assert.Contains("Made anew", log);
            Assert.Contains("MADE: a new object", log);
            Assert.Contains("a player who does gets a second one", log);

            // Apply writes the key on the place's square; Undo takes it away
            var edits = new LevelEdits(s);
            Func<bool> there = () => edits.Model(move.Place.Level).Objects.Any(o => !o.Free && o.Index == made.Index && o.Item == keyItem && o.Owner == 40 && o.TileX == move.Place.X && o.TileY == move.Place.Y);
            Assert.False(there());
            KeyShuffle.Apply(r, edits);
            Assert.True(there());
            Assert.Empty(KeyShuffle.Undo(r, edits));
            Assert.False(there());
        }
    }
}
