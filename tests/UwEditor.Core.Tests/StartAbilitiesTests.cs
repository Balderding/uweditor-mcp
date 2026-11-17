using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// A new character's rune casting (AbilityOptions.NewCharacterCircle): casting from runes asks for
    /// level 2 * circle - 1 (combat/RUNES.C player_cast), and a new character is level 1, so until
    /// the solve has left the start's free area only circle 1 counts. A start whose way out needs
    /// more gets a wand (or, with single-use things counting, a scroll) of the spell, made within
    /// reach by the key item shuffle (StartAbilities).
    /// </summary>
    public class StartAbilitiesTests
    {
        static GameSession Session(string g = "UW") { string p = GameData.Game(g); return p == null ? null : GameSession.Open(p); }

        static readonly StartPoint Sealed = new StartPoint { Level = 1, X = 30.5, Y = 32.5, Description = "test start" };

        [Fact]
        public void The_circles_and_what_a_new_character_cannot_cast()
        {
            var s = Session(); if (s == null) return;
            // UW1: six spells to a circle; Gate Travel is spell 32, circle 6 (level 11); Leap and Light circle 1
            Assert.Equal(6, SpellTables.Circle(false, Abilities.SpellsGiving(false, "gate_travel").Single()));
            Assert.Equal(1, SpellTables.Circle(false, Abilities.SpellsGiving(false, "jump").Single()));
            var w = StartAbilities.Withheld(s, new AbilityOptions());
            Assert.Equal(new[] { "levitate", "waterwalk", "lava", "reveal", "open", "telekinesis", "gate_travel" }, w);
            Assert.Empty(StartAbilities.Withheld(s, new AbilityOptions { NewCharacterCircle = AbilityOptions.MaxCircle }));
            Assert.Empty(StartAbilities.Withheld(s, new AbilityOptions { Magic = false }));
            Assert.Equal(new[] { "reveal", "telekinesis", "gate_travel" }, StartAbilities.Withheld(s, new AbilityOptions { NewCharacterCircle = 5 }));
            Assert.Equal(1, new AbilityOptions().NewCharacterCircle);
            Assert.Equal(int.MaxValue, new SolverOptions().NewCharacterCircle);
            Assert.Equal(int.MaxValue, new AbilityOptions { NewCharacterCircle = 8 }.ToSolver().NewCharacterCircle);
        }

        /// <summary>The objects made: a wand enchanted with the spell (lasting) or, with single-use things counting, a scroll with the fields of the game's own scrolls of Gate Travel (level 5 #899).</summary>
        [Fact]
        public void The_object_for_Gate_Travel_is_a_wand_or_the_games_own_scroll()
        {
            var s = Session(); if (s == null) return;
            string token; int spell;
            var wand = StartAbilities.ObjectFor(s, new AbilityOptions(), "gate_travel", out token, out spell);
            Assert.Equal("perm:gate_travel", token);
            Assert.Equal(32, spell);
            Assert.Equal((0x98, 0x200 + 32, 4, true, true), (wand.Item, wand.Link, wand.Flags, wand.IsQuantity, wand.Enchanted));
            var scroll = StartAbilities.ObjectFor(s, new AbilityOptions { SingleUseItems = true }, "gate_travel", out token, out spell);
            Assert.Equal("once:gate_travel", token);
            var own = s.LoadLevel(5).Objects[899];
            Assert.Equal((own.Item, own.Quality, own.Link, (own.Record[1] >> 1) & 7, own.IsQuantity, own.Enchanted), (scroll.Item, scroll.Quality, scroll.Link, scroll.Flags, scroll.IsQuantity, scroll.Enchanted));
            // only single-use things of other abilities: the wand again
            StartAbilities.ObjectFor(s, new AbilityOptions { SingleUseItems = true, SingleUseOnly = new HashSet<string> { "levitate" } }, "gate_travel", out token, out spell);
            Assert.Equal("perm:gate_travel", token);
            // the levels read the made wand as a lasting source of Gate Travel, the scroll as a single use
            var le = new LevelEditor(s, 1);
            int w = le.AddObject(wand.Spec(), 31, 30);
            int sc = le.AddObject(scroll.Spec(), 31, 31);
            var m = LevelModel.FromBytes(s, 1, le.Data, le.TextureBytes);
            var found = Abilities.Find(s, l => l == 1 ? m : s.LoadLevel(l), null).Where(x => x.Level == 1 && (x.Index == w || x.Index == sc)).ToList();
            Assert.Contains(found, x => x.Index == w && x.Token == "perm:gate_travel" && x.Kind == "used");
            Assert.Contains(found, x => x.Index == sc && x.Token == "once:gate_travel" && x.Kind == "scroll");
        }

        /// <summary>
        /// The game's own start: no spell above circle 1 is cast from runes before the solve leaves the
        /// start's free area, and the solve still reaches the goal in as many spheres as with every
        /// circle counted (UW1 22, UW2 41).
        /// </summary>
        [Fact]
        public void The_games_own_start_casts_nothing_above_circle_1_before_it_leaves_its_free_area()
        {
            foreach (var (g, spheres) in new[] { ("UW", 22), ("UW2", 39) })
            {
                var s = Session(g); if (s == null) continue;
                var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
                var sv = t.Solve;
                Assert.True(sv.GoalReached, g);
                Assert.Equal(spheres, sv.Spheres.Count);
                Assert.NotNull(sv.FreeArea);
                Assert.True(sv.FreeAreaLeft > 0, g);
                foreach (var e in sv.Events.Where(e => e.Kind == "token" && e.Token.StartsWith("cast:")))
                    Assert.True(e.Placement.Circle <= 1 || e.Sphere >= sv.FreeAreaLeft, g + ": " + e.Token + " (circle " + e.Placement.Circle + ") in sphere " + e.Sphere + ", free area left in " + sv.FreeAreaLeft);
                Assert.Contains(sv.Events, e => e.Kind == "token" && e.Token == "cast:light" && e.Sphere < sv.FreeAreaLeft);
                Assert.Contains("A new character casts from runes up to circle 1", Timeline.Report(t));
                var any = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions { NewCharacterCircle = AbilityOptions.MaxCircle }, null, null, variants: false);
                Assert.Null(any.Solve.FreeArea);
                Assert.Equal(spheres, any.Solve.Spheres.Count);
            }
        }

        /// <summary>The sealed room of level 1 (30, 32): with the runes alone it is not a viable start; the picker accepts it with a wand of Gate Travel made, and the shuffle's walkthrough leaves by it.</summary>
        [Fact]
        public void The_sealed_room_is_left_by_a_made_wand_of_Gate_Travel()
        {
            var s = Session(); if (s == null) return;
            var opt = new KeyShuffleOptions { Seed = 1, Retries = 0, Start = StartChoice.Given, StartAt = Sealed };
            var picker = StartPicker.For(s, l => s.LoadLevel(l), opt);
            var c = picker.Check(1, 30, 32);
            Assert.True(c.Viable, c.ToString());
            Assert.Equal(new[] { "gate_travel" }, c.Made);
            Assert.Contains("an object made for gate travel", c.Reason);
            var r = KeyShuffle.Run(s, null, opt);
            Assert.True(r.Success, string.Join("; ", r.Failures));
            var made = Assert.Single(r.Objects, o => o.Made != null);
            Assert.Equal("wand of Gate Travel", made.Name);
            Assert.Equal(r.CheckBuild.Set.StartZone, r.CheckBuild.Set.ZoneAt(made.Level, made.X, made.Y));
            string log = r.Log.ToString();
            Assert.Contains("for the start's way out: the start's free area is left only with gate travel, and a new character cannot cast Gate Travel from runes (circle 6, which asks for level 11", log);
            Assert.Contains("MADE: a new object", log);
            var lines = r.Walkthrough.Lines();
            int take = lines.FindIndex(l => l.Contains("Take the wand (Gate Travel, used)") && l.Contains("(perm:gate_travel)"));
            int warp = lines.FindIndex(l => l.Contains("Warp to zone") && l.Contains("moonstone"));
            Assert.True(take >= 0 && warp > take, string.Join("\n", lines.Take(12)));
            Assert.Contains("met by an item used for gate travel", lines[warp + 1]);
            Assert.DoesNotContain(lines.Take(warp), l => l.Contains("cast:gate_travel"));
            // applied: the wand is in the room; Undo takes it away with the rest
            var edits = new LevelEdits(s);
            KeyShuffle.Apply(r, edits);
            var o = edits.Model(made.Level).Objects[made.Index];
            Assert.Equal((0x98, 0x220), (o.Item, o.Link));
            Assert.Empty(KeyShuffle.Undo(r, edits));
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
        }

        /// <summary>With single-use things counting, the game's own four scrolls of Gate Travel move as key items: one goes into the sealed room and nothing is made.</summary>
        [Fact]
        public void With_single_use_things_the_games_own_scroll_is_put_in_the_sealed_room()
        {
            var s = Session(); if (s == null) return;
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 1, Retries = 0, Start = StartChoice.Given, StartAt = Sealed, Logic = new AbilityOptions { SingleUseItems = true } });
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.DoesNotContain(r.Objects, o => o.Made != null);
            int start = r.CheckBuild.Set.StartZone;
            Assert.Contains(r.Moves, m => m.Item.Tokens.Contains("once:gate_travel") && r.CheckBuild.Set.ZoneAt(m.Place.Level, m.Place.X, m.Place.Y) == start);
        }
    }
}
