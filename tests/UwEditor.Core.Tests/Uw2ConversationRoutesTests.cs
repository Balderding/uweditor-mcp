using System;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW2's ways that conversations open and the quests the facts spell out step by step: the
    /// Prison Tower's password doors (the facts' kind "door", an "or" beside the key), the
    /// Talorus chamber, Zoranthus's bottle, the Pits' gem and the Academy's eighth level.
    /// </summary>
    public class Uw2ConversationRoutesTests
    {
        /// <summary>The levels with every key 6 of the Prison Tower gone (as a key shuffle may take them away), and the voucher too if asked.</summary>
        static Func<int, LevelModel> WithoutKey6(GameSession s, bool voucher)
        {
            return l =>
            {
                var m = s.LoadLevel(l);
                if (l >= 9 && l <= 16)
                    foreach (var o in m.Objects)
                        if (o != null && !o.Free && KeyIndex.IsKey(o.Item, s.Kind) && o.Owner == 6) o.Free = true;
                if (l == 9 && !voucher) m.Objects[986].Free = true;
                return m;
            };
        }

        [Fact]
        public void The_password_doors_take_the_guards_word_beside_the_key()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var facts = FactSet.Shipped(true);
            Assert.Empty(facts.Check());
            // the gate's portcullis (level 10 #1018) and the door on Borne's floor (level 14 #1004)
            Assert.Equal(0x146, s.LoadLevel(10).Objects[1018].Item);
            Assert.Equal(0x144, s.LoadLevel(14).Objects[1004].Item);
            var plain = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), new[] { 10, 14 });
            var g = NavGraph.Build(s, l => s.LoadLevel(l), AbilityZones.FreeChains(new NavOptions(), facts), new[] { 10, 14 });
            Assert.Equal("key:6 | pick:11", plain.Regions[plain.RegionOf(10, 30, 34)].DoorRequires);
            Assert.Equal("key:6 | pick:11 | flag:prison_gate_opened", g.Regions[g.RegionOf(10, 30, 34)].DoorRequires);
            Assert.Equal("key:6 | pick:12 | flag:prison_l14_door_opened", g.Regions[g.RegionOf(14, 29, 32)].DoorRequires);
            Assert.Contains("opened by a conversation", g.Regions[g.RegionOf(10, 30, 34)].DoorNotes);
            // the guards' word: the password (Borne or the armorer) or the voucher at the gate; the password alone on level 14
            Assert.Equal("flag:prison_password | item:delivery_voucher", facts.Facts.Single(f => f.Id == "prison_gate_guard").Requires);
            Assert.Equal("flag:prison_password", facts.Facts.Single(f => f.Id == "prison_l14_guard").Requires);
            Assert.Equal(2, facts.Facts.Count(f => f.Grants == "flag:prison_password"));
        }

        [Fact]
        public void With_the_keys_gone_the_tower_opens_by_the_voucher_and_the_password_and_not_otherwise()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            // no key 6 anywhere in the tower, and a failed pick not tried again (so the Open spell, a pick of skill 45, opens neither the gate,
            // pick 11, nor level 14's door, pick 12): the voucher gets the Avatar past the gate, the password (Borne or the armorer) past the door
            var opt = new AbilityOptions { PickRetry = false };
            var t = Timeline.Build(s, WithoutKey6(s, true), null, opt, null, null, variants: false);
            Assert.DoesNotContain("key:6", t.Solve.TokenSphere.Keys);
            int inside = t.Set.ZoneAt(10, 29, 36), ledge = t.Set.ZoneAt(14, 27, 31);
            Assert.Contains(inside, t.Solve.ZoneSphere.Keys);
            Assert.Contains(ledge, t.Solve.ZoneSphere.Keys);
            Assert.True(t.Solve.TokenSphere["flag:prison_gate_opened"] <= t.Solve.ZoneSphere[inside]);
            Assert.True(t.Solve.TokenSphere["flag:prison_password"] <= t.Solve.ZoneSphere[ledge]);
            Assert.Contains("item:key_gem_1", t.Solve.TokenSphere.Keys);
            // without the voucher too, the password lies behind the gate it opens: the tower stays shut
            var u = Timeline.Build(s, WithoutKey6(s, false), null, opt, null, null, variants: false);
            Assert.DoesNotContain("flag:prison_gate_opened", u.Solve.TokenSphere.Keys);
            Assert.DoesNotContain("flag:prison_password", u.Solve.TokenSphere.Keys);
            Assert.DoesNotContain(u.Set.ZoneAt(10, 29, 36), u.Solve.ZoneSphere.Keys);
            Assert.DoesNotContain("item:key_gem_1", u.Solve.TokenSphere.Keys);
            Assert.False(u.Solve.GoalReached);
        }

        [Fact]
        public void The_quests_ask_for_their_real_steps()
        {
            string game = GameData.Game("UW2"); if (game == null) return;
            var s = GameSession.Open(game);
            var t = TestWorld.Build("UW2", null, null);
            var facts = t.Abilities.Facts;
            var sp = t.Solve.TokenSphere;
            Assert.True(t.Solve.GoalReached, string.Join(", ", t.Solve.GoalMissing));
            Assert.Empty(t.Solve.CutOff);
            // Talorus: the Historian's teleport waits for the new Ductosnore, which needs both crystals, the Delgnizator and the old one dead
            Assert.Equal("flag:talorus_new_ductosnore", facts.Facts.Single(f => f.Id == "talorus_historian_island").Requires);
            var skup = facts.Facts.Single(f => f.Id == "talorus_new_ductosnore");
            foreach (var tok in new[] { "npc:bliy_skup_ductosnore", "item:crystal_e0y2", "item:controller_crystal", "item:delgnizator" }) Assert.Contains(tok, skup.Requires);
            Assert.Equal(6, s.LoadLevel(34).Objects[947].Quality);
            Assert.Equal(2, s.LoadLevel(33).Objects[819].Quality);
            Assert.Equal(0xAE, s.LoadLevel(34).Objects[1005].Item);
            int made = sp["flag:talorus_new_ductosnore"];
            foreach (var tok in new[] { "item:crystal_e0y2", "item:controller_crystal", "item:delgnizator" }) Assert.True(sp[tok] <= made, tok);
            Assert.True(made < sp["item:key_gem_4"]);
            Assert.True(t.Solve.ZoneSphere[t.Set.ZoneAt(34, 31, 35)] > made);
            // Zoranthus's bottle asks for the Sceptre of the Deadly Seeker (level 65 #844: its spell, link 512 + 10)
            var m65 = s.LoadLevel(65);
            Assert.Equal(0xAA, m65.Objects[844].Item);
            Assert.Equal(512 + 10, m65.Objects[m65.Objects[844].Link].Link);
            var bottle = t.Set.Placements.Single(p => p.Source == "facts: bottle_zoranthus");
            Assert.Equal("item:sceptre_deadly_seeker & plotstep:miranda_7", bottle.Requires);   // since 0.1.49 after Miranda's step at 7 too
            Assert.Contains("item:sceptre_deadly_seeker", sp.Keys);
            // the Pits' gem: carried by Dorstag (whoami 0x63), whom a fight anywhere takes it from
            var m57 = s.LoadLevel(57);
            Assert.Equal(252, m57.Objects[784].Parent);
            Assert.Equal(0x63, m57.Objects[252].Record[0x1A]);
            // free unless the logic option pits_gem_by_blog leaves only Blog's way (PitsGemTests)
            Assert.Equal("nooption:pits_gem_by_blog", facts.Facts.Single(f => f.Id == "key_gem_7").Requires);
            // the Academy's eighth level restores no mana west of x 25
            Assert.NotNull(facts.NoMana(48, 10, 30));
            Assert.Null(facts.NoMana(48, 30, 30));
            Assert.NotNull(facts.NoMana(45, 30, 30));
        }
    }
}
