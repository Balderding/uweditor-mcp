using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW2's key gem 7 (the Pits of Carnage): Dorstag (level 57 #252) carries it and dies anywhere
    /// (key_gem_7, a fight taken as won), or Blog's way gets it with no fight (blog_friend at Blog,
    /// level 59 #255; key_gem_7_blog at Dorstag). The logic option PitsGemByBlog
    /// (option:pits_gem_by_blog) leaves only Blog's way.
    /// </summary>
    public class PitsGemTests
    {
        [Fact]
        public void The_option_and_the_facts()
        {
            Assert.False(new AbilityOptions().PitsGemByBlog);
            Assert.Contains("pits_gem_by_blog", new AbilityOptions { PitsGemByBlog = true }.Tokens());
            Assert.DoesNotContain("pits_gem_by_blog", new AbilityOptions().Tokens());
            Assert.Contains("pits_gem_by_blog", FactSet.KnownOptions);
            Assert.Contains("Blog", new AbilityOptions { PitsGemByBlog = true }.ToString());
            // nooption:NAME holds while the option is off
            var off = new AbilityZones.FixedContext(new AbilityOptions().ToSolver(), new HashSet<string>());
            var on = new AbilityZones.FixedContext(new AbilityOptions { PitsGemByBlog = true }.ToSolver(), new HashSet<string>());
            Assert.True(Requirement.Parse("nooption:pits_gem_by_blog").Eval(off));
            Assert.False(Requirement.Parse("nooption:pits_gem_by_blog").Eval(on));
            Assert.True(Requirement.Parse("option:pits_gem_by_blog").Eval(on));

            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            Func<string, Fact> F = id => uw2.Facts.Single(f => f.Id == id);
            Assert.Equal("nooption:pits_gem_by_blog", F("key_gem_7").Requires);
            Assert.Equal((57, 784), (F("key_gem_7").Level, F("key_gem_7").Object));
            var blog = F("blog_friend");
            Assert.Equal(("flag:blog_friend", 59, 255, "free", "a"), (blog.Grants, blog.Level, blog.Object, blog.Requires, blog.Lost));
            var way = F("key_gem_7_blog");
            Assert.Equal(("item:key_gem_7", 57, 252, "flag:blog_friend", "a"), (way.Grants, way.Level, way.Object, way.Requires, way.Lost));
        }

        static TimelineEvent GemEvent(TimelineSolve sv) { return sv.Events.First(e => e.Placement != null && e.Token == "item:key_gem_7"); }

        static void Ends(TimelineSolve sv, int zones)
        {
            Assert.True(sv.GoalReached, string.Join(", ", sv.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(sv.TokenSphere.Keys));
            Assert.Empty(sv.CutOff);
            Assert.Equal(zones, Timeline.BaseZonesReached(sv));
        }

        [Fact]
        public void Uw2_ends_with_the_gem_by_either_way_and_by_blogs_alone()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            // Blog and Dorstag, not hostile, and the rocks for Blog's game beside him
            var l59 = s.LoadLevel(59);
            Assert.Equal(0x65, l59.Objects[255].Record[0x1A]);
            Assert.Equal(new[] { 0xAB, 0xAB, 0xAB }, new[] { 973, 974, 975 }.Select(i => l59.Objects[i].Item).ToArray());
            var l57 = s.LoadLevel(57);
            Assert.Equal(0x63, l57.Objects[252].Record[0x1A]);
            Assert.Equal((0x11E, 252), (l57.Objects[784].Item, l57.Objects[784].Parent));
            Assert.Equal((0x187, 226), (l57.Objects[789].Item, l57.Objects[789].Link));      // the trap that makes the Blog of the Pits, who holds gem #782
            Assert.Equal((0x11E, 226), (l57.Objects[782].Item, l57.Objects[782].Parent));

            // the default options: both ways count, the kill comes first (sphere 14); with the option, Blog's way (sphere 16)
            // (334 zones: since 0.1.44 Telekinesis from runes does not reach the two barrier squares of Britannia, WhereMagicWorksTests)
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, variants: false);
            Ends(t.Solve, 334);
            Assert.Equal("facts: key_gem_7", GemEvent(t.Solve).Placement.Source);
            Assert.Equal(14, GemEvent(t.Solve).Sphere);
            var blog = Timeline.Solve(t, new AbilityOptions { PitsGemByBlog = true }, "Blog's way");
            Ends(blog, 334);
            Assert.Equal("facts: key_gem_7_blog", GemEvent(blog).Placement.Source);
            Assert.Equal(16, GemEvent(blog).Sphere);
            Assert.DoesNotContain(blog.Events, e => e.Placement != null && e.Placement.Source == "facts: key_gem_7");
            Assert.True(blog.TokenSphere["flag:blog_friend"] <= GemEvent(blog).Sphere);
            // the walkthrough warns at both of Blog's steps (lost only by a choice)
            var w = Walkthrough.Build(t, blog, null, l => s.LoadLevel(l));
            Assert.Contains(w.Steps, st => st.Event?.Token == "flag:blog_friend" && st.Warning.Contains("killing Blog"));
            Assert.Contains(w.Steps, st => st.Event?.Token == "item:key_gem_7" && st.Warning.Contains("killing Dorstag or Blog"));

            // a pure fighter (rune magic off, of the single-use things only Iron Flesh): the same
            var fighter = new AbilityOptions { Magic = false, SingleUseItems = true, SingleUseOnly = new HashSet<string> { "iron_flesh" }, PitsGemByBlog = true };
            var tf = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), fighter, null, null, variants: false);
            Ends(tf.Solve, 257);
            Assert.Equal("facts: key_gem_7_blog", GemEvent(tf.Solve).Placement.Source);
            Assert.Equal(16, GemEvent(tf.Solve).Sphere);
            var f2 = fighter.Clone(); f2.PitsGemByBlog = false;
            var killed = Timeline.Solve(tf, f2, "fighter, either way");
            Ends(killed, 257);
            Assert.Equal(14, GemEvent(killed).Sphere);
        }
    }
}
