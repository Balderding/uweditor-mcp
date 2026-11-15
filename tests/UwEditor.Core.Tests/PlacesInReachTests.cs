using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The key item shuffle with a random or given start (UW1) when the start's reach holds too few
    /// places: the fill moves empty places into reach (KeyShuffleOptions.PlacesInReach), and the
    /// return check from such a start counts the zones it leads out into as back (Engine.ReRoot).
    /// The invariant: every moving item ends in exactly one place, every place gets exactly one item,
    /// and over all levels the objects are the same ones as before (none lost, and none made but the
    /// made items; a place moved to another level takes its object along, so one level has one more and
    /// the other one less).
    /// </summary>
    public class PlacesInReachTests
    {
        static GameSession Session() { string g = GameData.Game("UW"); return g == null ? null : GameSession.Open(g); }

        /// <summary>The sealed room of level 1 at (30, 30): no way in or out but Gate Travel (to the moonstone on level 2).</summary>
        static readonly StartPoint Sealed = new StartPoint { Level = 1, X = 30.5, Y = 32.5, Description = "test start" };

        static KeyShuffleOptions Given(StartPoint at, int seed, bool? inReach = null, int circle = 1)
        {
            return new KeyShuffleOptions { Seed = seed, Retries = 0, Start = StartChoice.Given, StartAt = at, PlacesInReach = inReach, Logic = new AbilityOptions { NewCharacterCircle = circle } };
        }

        /// <summary>The item kinds of every object in the levels, as a sorted list (r.After over r.Before).</summary>
        static List<int> Kinds(GameSession s, KeyShuffleResult r, bool after, out Dictionary<int, int> perLevel)
        {
            var all = new List<int>();
            perLevel = new Dictionary<int, int>();
            foreach (var kv in r.Before)
            {
                byte[] d = after && r.After.TryGetValue(kv.Key, out var a) ? a : kv.Value;
                var m = LevelModel.FromBytes(s, kv.Key, d, r.Textures[kv.Key]);
                var here = m.Objects.Where(o => !o.Free && (o.TileX >= 0 || o.Parent >= 0) && o.Index >= 2).Select(o => o.Item).ToList();
                perLevel[kv.Key] = here.Count;
                all.AddRange(here);
            }
            all.Sort();
            return all;
        }

        /// <summary>Every moving item in exactly one place, every place with exactly one item, the same objects over all levels, and the places moved into reach on their squares.</summary>
        static void AssertInvariant(GameSession s, KeyShuffleResult r)
        {
            var moving = r.Moving.Where(o => o.Made == null).ToList();
            var moves = r.Moves.Where(m => m.Item.Made == null).ToList();
            Assert.Equal(moving.Count, moves.Count);
            Assert.Equal(moving.Count, moves.Select(m => m.Item).Distinct().Count());
            Assert.Equal(moving.Count, moves.Select(m => m.Place).Distinct().Count());
            Assert.True(moves.All(m => moving.Contains(m.Place)));
            Dictionary<int, int> before, after;
            var made = r.Moves.Where(m => m.Item.Made != null).ToList();
            Assert.Equal(Kinds(s, r, false, out before).Concat(made.Select(m => m.Item.Item)).OrderBy(k => k), Kinds(s, r, true, out after));
            foreach (var l in before.Keys)
            {
                int gained = moving.Count(o => o.FromLevel >= 0 && o.Level == l) + made.Count(m => m.Place.Level == l), lost = moving.Count(o => o.FromLevel == l);
                Assert.True(before[l] + gained - lost == after[l], "level " + l + ": " + before[l] + " + " + gained + " - " + lost + " != " + after[l]);
            }
            // the records where the places are: each moving item's own (key items by the fill and the rest alike)
            foreach (var m in moves.Where(m => m.Item != m.Place))
            {
                var lm = LevelModel.FromBytes(s, m.Place.Level, r.After.TryGetValue(m.Place.Level, out var d) ? d : r.Before[m.Place.Level], r.Textures[m.Place.Level]);
                var o = lm.Objects[m.Place.Index];
                Assert.False(o.Free);
                Assert.Equal((m.Item.Record.Item, m.Item.Record.Quality, m.Item.Record.Owner, m.Item.Record.Link), (o.Item, o.Quality, o.Owner, o.Link));
                if (m.Place.InReach) Assert.Equal((m.Place.X, m.Place.Y), (o.TileX, o.TileY));
            }
        }

        /// <summary>
        /// The sealed room is left only by Gate Travel, which a new character cannot cast from runes
        /// (circle 6): the shuffle makes a wand of Gate Travel there (StartAbilities: single-use things
        /// do not count by default, and a wand enchanted with the spell casts without end), on a place
        /// moved into the room. With every circle counted from the start (NewCharacterCircle 8) the
        /// fill puts the rune bag and Vas, Rel and Por in the room instead, as before 0.1.43.
        /// </summary>
        [Fact]
        public void A_sealed_start_room_gets_the_items_of_its_way_out_on_squares_inside_it()
        {
            var s = Session(); if (s == null) return;
            // without places in reach the fill runs out of places the logic reaches
            var off = KeyShuffle.Run(s, null, Given(Sealed, 1, false));
            Assert.False(off.Success);
            Assert.Contains(off.Failures, f => f.Contains("no place left that the logic reaches"));
            Assert.DoesNotContain(off.Failures, f => f.Contains("moved into reach"));
            // with them (the default for a given start): the room holds the wand made for Gate Travel
            var r = KeyShuffle.Run(s, null, Given(Sealed, 1));
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.NotEmpty(r.MovedIntoReach);
            var wand = Assert.Single(r.Moves, m => m.Item.Made != null);
            Assert.Equal("gate_travel", wand.Item.Made.Ability);
            Assert.Equal("perm:gate_travel", wand.Item.Tokens);
            Assert.True(wand.Place.InReach);
            var inReach = r.Moves.Where(m => m.Place.InReach).ToList();
            Assert.Equal(r.MovedIntoReach.Count, inReach.Select(m => m.Place).Distinct().Count());
            int startZone = r.CheckBuild.Set.StartZone;
            Assert.Equal(r.CheckBuild.Set.ZoneAt(1, 30, 32), startZone);
            foreach (var m in inReach)
            {
                // a key item, or what the rest put in the place the made wand shares
                Assert.True(m.ByFill || m.Place == wand.Place);
                Assert.Equal(startZone, r.CheckBuild.Set.ZoneAt(m.Place.Level, m.Place.X, m.Place.Y));
                Assert.Equal(0, r.Check.TokenSphere.Where(kv => m.Item.Gives.Any(g => Requirement.Normalize(g.Grants) == kv.Key)).Select(kv => kv.Value).DefaultIfEmpty(0).Min());
            }
            Assert.Contains("Places in reach: on; " + r.MovedIntoReach.Count + " empty place", r.Log.ToString());
            Assert.Contains("a square within reach", r.Log.ToString());
            Assert.DoesNotContain(inReach, m => m.Item.Category == "runes");
            AssertInvariant(s, r);
            // every circle counted from the start: the runes go in the room, and nothing is made
            var old = KeyShuffle.Run(s, null, Given(Sealed, 1, null, AbilityOptions.MaxCircle));
            Assert.True(old.Success, old.Refused ?? string.Join("; ", old.Failures));
            Assert.DoesNotContain(old.Objects, o => o.Made != null);
            Assert.Contains(old.Moves, m => m.Place.InReach && m.Item.Category == "runes");
            // applied: the levels solve from the room, and Undo puts back every byte
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            var t = Timeline.Build(s, edits.Model, new NavOptions(), new AbilityOptions(), null, null, variants: false);
            Assert.Equal(t.Set.ZoneAt(1, 30, 32), t.Set.StartZone);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.DoesNotContain(t.Solve.Traps, x => x.Strands.Count > 0);
            Assert.Empty(KeyShuffle.Undo(r, edits));
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
        }

        /// <summary>
        /// Level 7 holds only 8 places: a closed room there (47, 43) runs out of empty places on its
        /// own level, and places of other levels move in (their objects with them: the totals stay).
        /// With every circle counted from the start, as the room's way out then takes four items (the
        /// rune bag and three runes); by default it takes one, a wand made for Flameproof.
        /// </summary>
        [Fact]
        public void Places_of_other_levels_move_in_when_the_starts_level_has_none_left()
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.Run(s, null, Given(new StartPoint { Level = 7, X = 47.5, Y = 43.5 }, 1, null, AbilityOptions.MaxCircle));
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            var across = r.Moving.Where(o => o.FromLevel >= 0).ToList();
            Assert.NotEmpty(across);
            Assert.All(across, o => { Assert.Equal(7, o.Level); Assert.NotEqual(7, o.FromLevel); Assert.True(o.InReach); });
            AssertInvariant(s, r);
        }

        /// <summary>The game's own start: off unless asked, and the log says nothing of it; asked for, a fill that finds its places gives the same levels.</summary>
        [Fact]
        public void The_games_own_start_is_unchanged()
        {
            var s = Session(); if (s == null) return;
            var a = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 1 });
            Assert.True(a.Success, string.Join("; ", a.Failures));
            Assert.DoesNotContain("Places in reach", a.Log.ToString());
            var b = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 1, PlacesInReach = true });
            Assert.True(b.Success, string.Join("; ", b.Failures));
            Assert.Empty(b.MovedIntoReach);
            Assert.Equal(a.UsedSeed, b.UsedSeed);
            Assert.Equal(a.After.Keys.OrderBy(k => k), b.After.Keys.OrderBy(k => k));
            foreach (var kv in a.After) Assert.True(kv.Value.AsSpan().SequenceEqual(b.After[kv.Key]), "level " + kv.Key);
        }

        /// <summary>
        /// The return check from a start other than the game's own: from the sealed room every zone
        /// is left one way (Gate Travel), so going back to the room would make every zone a trap.
        /// Nothing in the room is needed after sphere 0, and the only zone it leads out into is the
        /// moonstone's room on level 2, so back there counts as back. The game's own start keeps
        /// the start alone.
        /// </summary>
        [Fact]
        public void The_return_check_from_a_sealed_start_counts_the_zone_it_leads_out_into()
        {
            var s = Session(); if (s == null) return;
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { StartOverride = Sealed }, new AbilityOptions(), null, null, variants: false);
            var held = KeyShuffle.MovingTokens(s, l => s.LoadLevel(l), t, new KeyShuffleOptions());
            // a new character cannot cast Gate Travel from the runes: the room stays shut without the wand the shuffle makes
            Assert.False(Timeline.Solve(t, new AbilityOptions(), "every moving item held", held).GoalReached);
            Assert.Equal(new[] { "gate_travel" }, StartAbilities.Needed(s, t, new AbilityOptions(), held));
            held.Add("perm:gate_travel");
            var sv = Timeline.Solve(t, new AbilityOptions(), "every moving item held", held);
            Assert.True(sv.GoalReached);
            Assert.Empty(sv.Traps);
            Assert.Equal(new[] { t.Set.ZoneAt(2, 8, 34) }, sv.ReturnTo);
            Assert.Contains("back in a zone it leads out into counts as back", Timeline.Report(WithSolve(t, sv)));
            // a pocket: level 6 (16, 10) and the lava beside it, which leads back to it; Gate Travel is the way out of both
            var t6 = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions { StartOverride = new StartPoint { Level = 6, X = 16.5, Y = 10.5 } }, new AbilityOptions(), null, null, variants: false);
            var sv6 = Timeline.Solve(t6, new AbilityOptions(), "every moving item held", KeyShuffle.MovingTokens(s, l => s.LoadLevel(l), t6, new KeyShuffleOptions()).Concat(new[] { "perm:gate_travel" }));
            Assert.True(sv6.GoalReached);
            Assert.Empty(sv6.Traps);
            Assert.Contains(t6.Set.ZoneAt(2, 8, 34), sv6.ReturnTo);
            var own = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            Assert.Empty(own.Solve.ReturnTo);
            Assert.Empty(Timeline.Solve(own, new AbilityOptions(), "held", KeyShuffle.MovingTokens(s, l => s.LoadLevel(l), own, new KeyShuffleOptions())).ReturnTo);
        }

        static TimelineResult WithSolve(TimelineResult t, TimelineSolve sv) { t.Solve = sv; return t; }

        /// <summary>
        /// The walk back joins in the changes that only open ways and that the walk can set off: from
        /// level 8 (10, 38), seed 1, the way on is a free jump into the area at (2, 2), then Gate
        /// Travel to the moonstone's room on level 2, whose floor opens the way out when walked on
        /// (terrain_L2_869). The world at the end of the jump's sphere did not have that opening yet,
        /// so the area counted as a one-way trap holding "a way inside"; the shuffle now passes.
        /// </summary>
        [Fact]
        public void The_walk_back_counts_a_way_it_opens_itself()
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.Run(s, null, Given(new StartPoint { Level = 8, X = 10.5, Y = 38.5 }, 1));
            Assert.True(r.Success, r.Refused ?? string.Join("; ", r.Failures));
            Assert.DoesNotContain(r.Check.Traps, x => x.Strands.Count > 0);
            Assert.Contains(r.Check.Fired, f => string.Equals(f, "flag:terrain_L2_869", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A real strand stays one: from level 7 (51, 24) seed 3 puts the broken blade in the area
        /// of level 8 at (2, 2), which a free jump enters one way and which the player cannot leave
        /// with what he holds then. The start's own zone can be gone back to, so nothing is re-rooted.
        /// </summary>
        [Fact]
        public void A_one_way_pit_holding_what_is_needed_still_strands()
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.Run(s, null, Given(new StartPoint { Level = 7, X = 51.5, Y = 24.5 }, 3));
            Assert.False(r.Success);
            Assert.Contains(r.Failures, f => f.Contains("a one-way trap strands the player") && f.Contains("L8 area at (2, 2)"));
        }
    }
}
