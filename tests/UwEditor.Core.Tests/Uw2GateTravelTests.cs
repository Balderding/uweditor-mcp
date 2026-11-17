using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW2's Gate Travel (combat/SPELLS.C obj_spells case 15): aimed at a moonstone, it takes the
    /// player to the level of the other moonstone, and do_mstone lands on the first moonstone
    /// found there, inside containers too (obj/OBJECTS.C Obj_FindInMap). The facts count the way
    /// from level 3's moonstone (in a locked chest, taken and kept: moonstone_l3) to level 45's
    /// (gate_travel). Also Altara's door (conversation 45): a link from wherever she stands,
    /// which adds nothing while she stands in one zone with its end.
    /// </summary>
    public class Uw2GateTravelTests
    {
        [Fact]
        public void The_facts()
        {
            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            var w = uw2.Facts.Single(x => x.Id == "gate_travel");
            Assert.Equal("warp", w.Kind);
            Assert.Equal((45, 975), (w.To.Level, w.To.Object));
            Assert.Equal((0, 0, ""), (w.FromLevel, w.FromAbove, w.Option));
            var r = Requirement.TryParse(w.Requires, out _);
            Assert.NotNull(r);
            var toks = new List<string>();
            r.Tokens(toks);
            Assert.Contains("spell:gate_travel", toks);
            Assert.Contains("flag:moonstone_l3", toks);
            var m = uw2.Facts.Single(x => x.Id == "moonstone_l3");
            Assert.Equal(("flag", "flag:moonstone_l3", 3, 766), (m.Kind, m.Grants, m.Level, m.Object));
            Assert.Equal("key:3 | pick:3", m.Requires);

            var a = uw2.Facts.Single(x => x.Id == "altara_door");
            Assert.Equal("link", a.Kind);
            Assert.Equal((17, 243), (a.From.Level, a.From.Object));
            Assert.Equal((17, 19, 49), (a.To.Level, a.To.X, a.To.Y));
            Assert.True(a.Once);
            Assert.False(a.TwoWay);
            Assert.Equal("item:jewelled_dagger & npc:mystell", a.Requires);
            Assert.Equal("a", a.Lost);
            // UW1's Gate Travel is its own fact, a warp to its one moonstone
            Assert.Equal((2, 868), FactSet.Shipped(false).Facts.Where(x => x.Id == "gate_travel").Select(x => (x.To.Level, x.To.Object)).Single());
        }

        [Fact]
        public void Where_the_moonstones_lie()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var l45 = s.LoadLevel(45).Objects[975];
            Assert.Equal((0x126, 18, 48, -1), (l45.Item, l45.TileX, l45.TileY, l45.Parent));
            // level 3's lies in the locked chest #734: a contained object has its container's square, which is
            // where Gate Travel lands for a moonstone in a container (Obj_FindInMap looks inside)
            var l3 = s.LoadLevel(3);
            var ms = l3.Objects[766];
            Assert.Equal((0x126, 734, 34, 16), (ms.Item, ms.Parent, ms.TileX, ms.TileY));
            Assert.Equal("(key:3 | pick:3)", AbilityZones.ContainerLock(l3, ms));
        }

        [Fact]
        public void Uw2_gate_travel_goes_to_level_45_once_level_3s_moonstone_is_held()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            var sv = t.Solve;
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            Assert.DoesNotContain(t.Warnings, x => x.Contains("gate_travel") || x.Contains("moonstone_l3") || x.Contains("altara_door"));
            Assert.DoesNotContain(sv.Warnings, x => x.Contains("gate_travel"));
            int target = t.Set.ZoneAt(45, 18, 48);
            Assert.True(target > ZoneSet.Unassigned);
            Assert.Contains(t.Warps, f => f.Id == "gate_travel");
            // a way that asks for something is no free warp
            Assert.DoesNotContain(sv.FreeWarps, x => x.Key.Id == "gate_travel");
            int held = sv.TokenSphere["flag:moonstone_l3"];
            var ev = sv.Events.FirstOrDefault(e => e.Warp != null && e.Warp.Id == "gate_travel");
            Assert.NotNull(ev);
            Assert.Equal(target, ev.Zone);
            Assert.True(ev.Sphere >= held, "the warp waits for level 3's moonstone");
            Assert.Contains("flag:moonstone_l3", ev.Requires);
            Assert.True(sv.TokenSphere.ContainsKey("cast:gate_travel") && ev.Sphere >= sv.TokenSphere["cast:gate_travel"]);

            // without magic no runes cast it: only a scroll or a wand of Gate Travel held before could
            var off = Timeline.Solve(t, new AbilityOptions { Magic = false }, "no magic");
            var evOff = off.Events.FirstOrDefault(e => e.Warp != null && e.Warp.Id == "gate_travel");
            if (evOff != null)
                Assert.Contains(off.TokenSphere, kv => (kv.Key == "once:gate_travel" || kv.Key == "perm:gate_travel" || kv.Key == "worn:gate_travel") && kv.Value <= evOff.Sphere);
            // the way only adds: nothing reached without the moonstone is lost with it
            var noStone = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null,
                Without("gate_travel"), variants: false);
            Assert.True(Timeline.BaseZonesReached(sv) >= Timeline.BaseZonesReached(noStone.Solve));
            Assert.True(sv.ZoneSphere[target] <= noStone.Solve.ZoneSphere[target]);
        }

        [Fact]
        public void Altaras_door_counts_only_where_its_ends_lie_in_two_zones()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            // as shipped: Altara (L17 #243) and the square outside her door share a zone, so no link and no warning
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            Assert.Equal(t.Set.ZoneAt(17, 19, 49), t.Set.ZoneOf(17, s.LoadLevel(17), s.LoadLevel(17).Objects[243]));
            Assert.DoesNotContain(t.FactLinks, k => (k.Notes ?? "").Contains("altara_door"));
            Assert.DoesNotContain(t.Warnings, x => x.Contains("altara_door"));
            // with its end somewhere else (as if Altara stood elsewhere) the link stands from her zone, once, one way
            var moved = FactSet.FromJson(@"{ ""game"": ""UW2"", ""facts"": [ { ""id"": ""altara_door"", ""kind"": ""link"", ""from"": { ""level"": 17, ""object"": 243 },
                ""to"": { ""level"": 5, ""x"": 28, ""y"": 41 }, ""requires"": ""item:jewelled_dagger & npc:mystell"", ""twoWay"": false, ""once"": true, ""linkKind"": ""teleport"" } ] }");
            Assert.Empty(moved.Check());
            var t2 = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, moved, variants: false);
            var k2 = Assert.Single(t2.FactLinks, k => (k.Notes ?? "").Contains("altara_door"));
            Assert.Equal((t2.Set.ZoneAt(17, 19, 49), t2.Set.ZoneAt(5, 28, 41)), (k2.From, k2.To));
            Assert.True(k2.Once);
            Assert.False(k2.TwoWay);
            Assert.Equal("teleport", k2.Kind);
        }

        /// <summary>The shipped UW2 facts with one left out.</summary>
        static FactSet Without(string id)
        {
            var f = FactSet.Shipped(true);
            f.Facts.RemoveAll(x => x.Id == id);
            return f;
        }
    }
}
