using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Karl's rule: no solve may ever depend on the deaths of Killorn Keep's two brain creatures
    /// (UW2, whoami 0x3A, level 18 #241 and #253: killing both makes the keep fall, quest 50 then
    /// quest 54). The facts' kind "spare" names them; what they carry and anything granted on them
    /// is left out where creatures' deaths become sources (AbilityZones.Spare), and no requirement
    /// may ask for the token that would stand for the fall (FactSet.Check).
    /// </summary>
    public class SpareCreaturesTests
    {
        const string Fall = "flag:killorn_fallen";

        [Fact]
        public void The_check_refuses_a_requirement_on_a_spared_creatures_death()
        {
            var set = FactSet.FromJson(@"{ ""format"": ""uw2-facts"", ""facts"": [
                { ""id"": ""spare"", ""kind"": ""spare"", ""whoami"": ""0x3A"", ""level"": 18, ""token"": """ + Fall + @""", ""notes"": ""they hold the keep up"" },
                { ""id"": ""needs_fall"", ""kind"": ""flag"", ""grants"": ""item:x"", ""level"": 18, ""object"": 5, ""requires"": ""npc:a & " + Fall + @""" },
                { ""id"": ""link_fall"", ""kind"": ""link"", ""from"": [18, 1, 1], ""to"": [18, 2, 2], ""requires"": ""spell:levitate | " + Fall + @""" },
                { ""id"": ""window"", ""kind"": ""flag"", ""grants"": ""item:y"", ""level"": 17, ""object"": 6, ""requires"": ""free"",
                  ""mayBeLost"": { ""kind"": ""a"", ""when"": """ + Fall + @""", ""why"": ""gone with the keep"" } },
                { ""id"": ""bad_spare"", ""kind"": ""spare"" } ] }");
            var spare = set.Facts.Single(f => f.Id == "spare");
            Assert.Equal(0x3A, spare.Whoami);
            Assert.Equal(new[] { Fall }, set.SpareTokens().ToArray());
            var problems = set.Check();
            Assert.Contains(problems, x => x.StartsWith("needs_fall: requires " + Fall));
            Assert.Contains(problems, x => x.StartsWith("link_fall: requires " + Fall));
            Assert.Contains(problems, x => x.StartsWith("bad_spare: a spare fact names the creatures"));
            Assert.Contains(problems, x => x.StartsWith("bad_spare: a spare fact needs 'notes'"));
            // a window may name the fall: it says when a thing is gone, not what progress needs
            Assert.DoesNotContain(problems, x => x.StartsWith("window:") || x.StartsWith("spare:"));

            // the shipped UW2 facts carry the rule and pass the check
            var uw2 = FactSet.Shipped(true);
            Assert.Empty(uw2.Check());
            var brain = uw2.Facts.Single(f => f.Id == "spare_brain_creatures");
            Assert.Equal("spare", brain.Kind);
            Assert.Equal((0x3A, 18), (brain.Whoami, brain.Level));
            Assert.Contains(Fall, uw2.SpareTokens());
            Assert.DoesNotContain(FactSet.Shipped(false).Facts, f => f.Kind == "spare");
        }

        static readonly Regex PlacedObject = new Regex(@"^#(\d+) .*?level (\d+) \(");

        [Fact]
        public void Uw2_no_source_or_requirement_depends_on_the_brain_creatures()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var l18 = s.LoadLevel(18);
            // the two brain creatures and what they carry (two eyeballs each)
            foreach (int i in new[] { 241, 253 }) { Assert.Equal(0x3A, l18.Objects[i].Record[0x1A]); Assert.Equal(0x58, l18.Objects[i].Item); }
            var facts = FactSet.Shipped(true);
            foreach (int i in new[] { 241, 253, 929, 930, 981, 982 }) Assert.Equal("spare_brain_creatures", facts.Spared(18, l18, l18.Objects[i])?.Id);
            Assert.Null(facts.Spared(17, s.LoadLevel(17), s.LoadLevel(17).Objects[971]));     // Altara's dagger: not theirs
            Assert.Null(facts.Spared(18, l18, l18.Objects[907]));                              // the key gem of level 18 lies on the floor

            // a fact granted on what a brain creature carries is left out too
            facts.Add(FactSet.FromJson(@"{ ""facts"": [ { ""id"": ""test_on_brain"", ""kind"": ""flag"", ""grants"": ""flag:test_on_brain"", ""level"": 18, ""object"": 929, ""requires"": ""free"" } ] }"));
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, facts, variants: false);
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Equal(16, Requirement.PlotClock(t.Solve.TokenSphere.Keys));
            Assert.Empty(t.Solve.CutOff);
            var spared = t.Abilities.Spared;
            foreach (var n in new[] { "#929 eyeball", "#930 eyeball", "#981 eyeball", "#982 eyeball", "facts: test_on_brain" }) Assert.Contains(spared, x => x.Contains(n));
            Assert.DoesNotContain(t.Set.Placements, p => p.Grants == "flag:test_on_brain");

            // no placement left comes from them
            var byId = facts.Facts.Where(f => f.Id.Length > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var p in t.Set.Placements)
            {
                int l = -1, i = -1;
                var mm = PlacedObject.Match(p.Notes ?? "");
                Fact own;
                if (p.Source == "levels" && mm.Success) { l = int.Parse(mm.Groups[2].Value); i = int.Parse(mm.Groups[1].Value); }
                else if ((p.Source ?? "").StartsWith("facts: ") && byId.TryGetValue(p.Source.Substring(7), out own) && own.Kind == "flag" && own.Object >= 0) { l = own.Level; i = own.Object; }
                if (l < 0) continue;
                var m = s.LoadLevel(l);
                Assert.True(i >= m.Objects.Length || facts.Spared(l, m, m.Objects[i]) == null, p.Grants + " from " + p.Notes);
            }
            // no requirement anywhere, in the facts or generated, asks for their deaths or the fall
            var texts = new List<string>();
            texts.AddRange(t.Set.Links.Select(k => k.Requires));
            texts.AddRange(t.FactLinks.Select(k => k.Requires));
            texts.AddRange(t.Set.Placements.Select(p => p.Requires));
            foreach (var f in facts.Facts) { texts.Add(f.Requires); texts.Add(f.Otherwise); texts.Add(f.Until); }
            foreach (var mech in t.Abilities.Auto.Graph.Mechanisms) { texts.Add(mech.Conditions); texts.Add(Regex.Replace(mech.SetOff ?? "", @"region:\d+", "free")); }
            texts.Add(t.GoalFor(Timeline.SolverFor(t, new AbilityOptions(), null)));
            var toks = new List<string>();
            foreach (var x in texts.Where(x => !string.IsNullOrWhiteSpace(x))) Requirement.Parse(x).Tokens(toks);
            Assert.DoesNotContain(Fall, toks);
            Assert.DoesNotContain(t.Set.Placements, p => Requirement.Normalize(p.Grants) == Fall);
            // their room is walked through: nothing blocks the solve there
            Assert.True(t.Solve.ZoneSphere.ContainsKey(t.Set.ZoneAt(18, 29, 30)));
            Assert.True(t.Solve.ZoneSphere.ContainsKey(t.Set.ZoneAt(18, 32, 28)));
            // and the walkthrough still warns not to kill them (lost_killorn_18, kind a)
            var w = Walkthrough.Build(t, t.Solve, null, l => s.LoadLevel(l));
            Assert.Contains(w.Steps, st => st.Warning.Contains("brain creatures") && st.Warning.Contains("do not kill them"));
        }
    }
}
