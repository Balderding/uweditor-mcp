using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW2: with no health left on a level above 8 the player wakes by the blackrock gem on castle
    /// level 5 with all he holds (game/SKILLS.C player_is_dead, do_gem). The facts' warp
    /// death_to_gem counts it while the logic option DeathReturns (option:death_returns) is on,
    /// which it is unless set off.
    /// </summary>
    public class DeathWarpTests
    {
        [Fact]
        public void The_option_and_the_fact()
        {
            Assert.True(new AbilityOptions().DeathReturns);
            Assert.Contains("death_returns", new AbilityOptions().Tokens());
            Assert.DoesNotContain("death_returns", new AbilityOptions { DeathReturns = false }.Tokens());
            Assert.Contains("death_returns", FactSet.KnownOptions);
            Assert.Contains("dying", new AbilityOptions { DeathReturns = false }.ToString());
            Assert.DoesNotContain("dying", new AbilityOptions().ToString());

            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            var f = uw2.Facts.Single(x => x.Id == "death_to_gem");
            Assert.Equal(("warp", "death_returns", 8, 0), (f.Kind, f.Option, f.FromAbove, f.FromLevel));
            Assert.Equal((5, 28, 41), (f.To.Level, f.To.X, f.To.Y));
            Assert.False(f.To.IsPlanted);
            Assert.StartsWith("Let the Avatar die", f.Notes);
            // UW1 has no such warp: its death is the silver sapling's
            Assert.DoesNotContain(FactSet.Shipped(false).Facts, x => x.FromAbove > 0 || x.Option == "death_returns");

            // a warp reads fromAbove from any facts file
            var a = FactSet.FromJson("{ \"game\": \"UW2\", \"facts\": [ { \"id\": \"w\", \"kind\": \"warp\", \"from\": \"any\", \"fromAbove\": 8, \"to\": { \"level\": 5, \"x\": 28, \"y\": 40 }, \"requires\": \"free\" } ] }");
            Assert.Equal(8, a.Facts.Single().FromAbove);
        }

        [Fact]
        public void Uw2_death_is_a_way_back_to_the_gem_only_with_the_option()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var t = TestWorld.Build("UW2", new NavOptions(), new AbilityOptions());
            var on = t.Solve;
            Assert.True(on.GoalReached, string.Join(", ", on.GoalMissing));
            Assert.DoesNotContain(on.Warnings, x => x.Contains("death_to_gem"));
            var warp = Assert.Single(on.FreeWarps);
            Assert.Equal("death_to_gem", warp.Key.Id);
            Assert.True(on.ZoneSphere.ContainsKey(warp.Value), "the gem's cavern is reached");

            var off = TestWorld.Solve(t, new AbilityOptions { DeathReturns = false }, "no dying");
            Assert.True(off.GoalReached, string.Join(", ", off.GoalMissing));
            Assert.Empty(off.FreeWarps);
            Assert.DoesNotContain(off.Events, e => e.Warp != null && e.Warp.Id == "death_to_gem");
            // dying only adds ways: nothing reached without it is lost with it, and no zone outside Britannia that
            // the gem's cavern leads back from is a trap any more
            Assert.True(Timeline.BaseZonesReached(on) >= Timeline.BaseZonesReached(off));
            Assert.True(on.Traps.Count <= off.Traps.Count, on.Traps.Count + " traps with dying, " + off.Traps.Count + " without");
        }
    }
}
