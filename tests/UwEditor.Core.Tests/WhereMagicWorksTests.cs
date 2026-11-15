using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Where a spell is cast decides whether it works (AbilityZones.CastAt): UW2's Britannia
    /// (levels 1 to 8) refuses rune spells above circle 3 (combat/RUNES.C player_cast), while
    /// scrolls, wands and worn things still work there; a no-magic square refuses every spell, cast
    /// or from a thing used, in both games (combat/SPELLS.C do_spell, anti_magic_p); the ways, the
    /// facts' links, the placements and the warps are narrowed by where they start.
    /// </summary>
    public class WhereMagicWorksTests
    {
        static GameSession Session(string g) { string p = GameData.Game(g); return p == null ? null : GameSession.Open(p); }

        static bool Holds(string req, bool singleUse, params string[] held)
        {
            var o = new SolverOptions { AllowBash = true, LockpickSkill = -1, AbilitySources = true, Magic = true, SingleUseItems = singleUse };
            return Requirement.Parse(req).Eval(new AbilityZones.FixedContext(o, new HashSet<string>(held)));
        }

        [Fact]
        public void In_Britannia_the_runes_cast_up_to_circle_3_and_things_used_still_work()
        {
            var s = Session("UW2"); if (s == null) return;
            var facts = FactSet.Shipped(true);
            Func<int, LevelModel> lv = l => s.LoadLevel(l);
            Func<string, int, string> at = (req, l) => AbilityZones.CastAt(s, lv, facts, req, l, 20, 20);
            // every spell giving these is above circle 3 (Levitate 5, Fly 8, Flameproof 4, Open 5, Telekinesis 5, Gate Travel 6, Reveal 7, Portal 7, Iron Flesh 8)
            foreach (var ab in new[] { "levitate", "lava", "open", "telekinesis", "gate_travel", "reveal", "portal", "iron_flesh" })
            {
                Assert.Null(AbilityZones.BritanniaCast(s, ab));
                foreach (int l in new[] { 1, 5, 8 }) Assert.Equal("use:" + ab, at("spell:" + ab, l));
                // the next world, and the worlds after it, as before
                foreach (int l in new[] { 9, 17, 41 }) Assert.Equal("spell:" + ab, at("spell:" + ab, l));
            }
            // Leaping (circle 2) and Water Walk (3) are cast there as anywhere
            foreach (var ab in new[] { "jump", "waterwalk" }) { Assert.Equal("", AbilityZones.BritanniaCast(s, ab)); Assert.Equal("spell:" + ab, at("spell:" + ab, 1)); }
            // Light: the runes of Light (circle 1) or Night Vision (3), not Daylight's alone (6)
            Assert.Equal(Requirement.Parse("(cast:light & ((" + Abilities.CastRequirement(s, 5) + ") | (" + Abilities.CastRequirement(s, 20) + "))) | use:light").ToString(), Requirement.Parse(at("spell:light", 3)).ToString());
            // casting Open from runes opens nothing in the castle; a weapon or wand of it does, and with single-use things a scroll
            string open = at("spell:open", 2);
            Assert.False(Holds(open, true, "cast:open"));
            Assert.True(Holds(open, false, "perm:open"));
            Assert.True(Holds(open, false, "worn:open"));
            Assert.False(Holds(open, false, "once:open"));
            Assert.True(Holds(open, true, "once:open"));
            Assert.True(Holds(at("spell:open", 10), false, "cast:open"));
            // a requirement with no spell is left alone; UW1 has no such rule
            Assert.Equal("key:3 | bash", at("key:3 | bash", 1));
            Assert.False(AbilityZones.InBritannia(s, 9));
            var s1 = Session("UW");
            if (s1 != null)
            {
                Assert.False(AbilityZones.InBritannia(s1, 1));
                Assert.Equal("spell:levitate", AbilityZones.CastAt(s1, l => s1.LoadLevel(l), FactSet.Shipped(false), "spell:levitate", 1, 20, 20));
            }
        }

        /// <summary>
        /// The default UW2 solve still ends the game (plot 16, 41 spheres, no cut-off). It loses the
        /// two barrier squares Telekinesis from runes reached in Britannia (level 1 (31, 53), level 5
        /// (28, 40), the large blackrock gem's square), which nothing needs; with single-use things
        /// counting, the game's scroll of Telekinesis reaches one of them.
        /// </summary>
        [Fact]
        public void The_default_UW2_solve_ends_the_game_with_the_Britannia_rule()
        {
            var s = Session("UW2"); if (s == null) return;
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            Assert.True(t.Abilities.BritanniaEdges > 0);
            var high = new Regex(@"\b(cast|spell):(levitate|lava|open|telekinesis|gate_travel|reveal|portal|iron_flesh)\b");
            var castle = t.Set.Links.Where(k => k.Level >= 1 && k.Level <= 8).ToList();
            Assert.DoesNotContain(castle, k => high.IsMatch(k.Requires ?? ""));
            Assert.Contains(castle, k => (k.Requires ?? "").Contains("use:telekinesis"));
            Assert.Contains(t.Set.Links, k => k.Level > 8 && high.IsMatch(k.Requires ?? ""));
            Assert.Contains("Britannia (levels 1 to 8): no spell above circle 3", AbilityZones.Report(t.Abilities));
            var sv = t.Solve;
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(sv.TokenSphere.Keys));
            Assert.Empty(sv.CutOff);
            Assert.Equal(41, sv.Spheres.Count);
            Assert.Equal(334, Timeline.BaseZonesReached(sv));
            int ledge = t.Set.ZoneAt(1, 31, 53), gem = t.Set.ZoneAt(5, 28, 40);
            Assert.DoesNotContain(ledge, sv.ZoneSphere.Keys);
            Assert.DoesNotContain(gem, sv.ZoneSphere.Keys);
            // the game's one scroll of Telekinesis does it for one of them (each reach uses one up)
            var once = Timeline.Solve(t, new AbilityOptions { SingleUseItems = true }, "single-use things");
            Assert.True(once.ZoneSphere.ContainsKey(ledge) || once.ZoneSphere.ContainsKey(gem));
            Assert.Contains("once:telekinesis", once.OnceSpent);
        }

        /// <summary>
        /// The tile's no-magic bit (bit 14 of the tile word) refuses every spell at the square in both
        /// games, the runes and the things the player uses alike (who is the player, tested at his
        /// own square); only worn enchantments go on. UW1 sets it on sixteen squares of level 6, UW2
        /// on none. A wand made for a start (perm:X) is no use there.
        /// </summary>
        [Fact]
        public void A_no_magic_square_leaves_only_worn_things_in_both_games()
        {
            var s1 = Session("UW");
            if (s1 != null)
            {
                var counts = s1.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => { var m = s1.LoadLevel(x.Level); int n = 0; for (int y = 0; y < 64; y++) for (int x2 = 0; x2 < 64; x2++) if (m.Tiles[x2, y].NoMagic) n++; return n; });
                Assert.Equal(16, counts[6]);
                Assert.Equal(16, counts.Values.Sum());
                Assert.True(s1.LoadLevel(6).Tiles[6, 17].NoMagic);
                var facts = FactSet.Shipped(false);
                Func<int, LevelModel> lv = l => s1.LoadLevel(l);
                string there = AbilityZones.CastAt(s1, lv, facts, "spell:gate_travel", 6, 6, 17);
                Assert.Equal("worn:gate_travel", there);
                Assert.False(Holds(there, true, "cast:gate_travel", "perm:gate_travel", "once:gate_travel"));
                string beside = AbilityZones.CastAt(s1, lv, facts, "spell:gate_travel", 6, 24, 3);
                Assert.Equal("spell:gate_travel", beside);
                Assert.True(Holds(beside, false, "perm:gate_travel"));
            }
            var s2 = Session("UW2");
            if (s2 != null)
            {
                foreach (var x in s2.LevelSlots().Where(x => x.Present))
                {
                    var m = s2.LoadLevel(x.Level);
                    if (m == null || !m.Present) continue;
                    for (int y = 0; y < 64; y++) for (int x2 = 0; x2 < 64; x2++) Assert.False(m.Tiles[x2, y].NoMagic, "UW2 L" + x.Level + " (" + x2 + ", " + y + ")");
                }
                // the bit set by hand: UW2's do_spell reads it as UW1's does, in Britannia too
                foreach (int l in new[] { 3, 10 })
                {
                    var m = s2.LoadLevel(l);
                    m.Tiles[30, 30].NoMagic = true;
                    Assert.Equal("worn:levitate", AbilityZones.CastAt(s2, k => k == l ? m : s2.LoadLevel(k), FactSet.Shipped(true), "spell:levitate", l, 30, 30));
                }
            }
        }

        /// <summary>
        /// A warp is cast where the player stands (Timeline's WarpRequires): a Gate Travel that may
        /// start only on UW1's level 7 needs the orb broken (no mana while it stands), while one from
        /// level 6 works as soon as the runes are held. The target is the sealed room of level 1 at
        /// (30, 32), which nothing else reaches.
        /// </summary>
        [Fact]
        public void Gate_Travel_from_level_7_waits_for_the_orb()
        {
            var s = Session("UW"); if (s == null) return;
            Func<int, TimelineSolve> solve = from =>
            {
                var facts = FactSet.Shipped(false);
                facts.Add(FactSet.FromJson("{\"format\":\"uw1-facts\",\"facts\":[{\"id\":\"test_gate_from_" + from + "\",\"kind\":\"warp\",\"fromLevel\":" + from + ",\"to\":{\"level\":1,\"x\":30,\"y\":32},\"requires\":\"spell:gate_travel\"}]}", "test"));
                return Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, facts, variants: false).Solve;
            };
            var t0 = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            int room = t0.Set.ZoneAt(1, 30, 32);
            Assert.DoesNotContain(room, t0.Solve.ZoneSphere.Keys);
            var six = solve(6);
            Assert.Contains(room, six.ZoneSphere.Keys);
            Assert.True(six.ZoneSphere[room] <= six.TokenSphere["cast:gate_travel"] + 1, "from level 6 in sphere " + six.ZoneSphere[room]);
            var seven = solve(7);
            int orb;
            if (seven.TokenSphere.TryGetValue("flag:orb_destroyed", out orb))
            {
                Assert.Contains(room, seven.ZoneSphere.Keys);
                Assert.True(seven.ZoneSphere[room] > orb, "from level 7 in sphere " + seven.ZoneSphere[room] + ", the orb in " + orb);
                Assert.True(seven.ZoneSphere[room] > six.ZoneSphere[room]);
            }
            else Assert.DoesNotContain(room, seven.ZoneSphere.Keys);
        }
    }
}
