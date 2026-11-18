using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Balance;
using UwEditor.Core.Conversations;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Balance (Tools ▸ Balance): the weapons and the armour overviews and the barter simulator,
    /// worked out as the windows do from the object tables as edited (table_set's unsaved changes
    /// included) and the levels as edited. Their numbers change through table_set / creature_set.
    /// </summary>
    public sealed partial class McpEditor
    {
        Func<int, LevelModel> CtLevels()
        {
            var cache = new Dictionary<int, LevelModel>();
            return l =>
            {
                LevelModel m;
                if (!cache.TryGetValue(l, out m)) cache[l] = m = Edits.Model(l);
                return m;
            };
        }

        static string F1(double v) { return v.ToString("0.0", Ci); }
        static string F2(double v) { return v.ToString("0.00", Ci); }

        /// <summary>The player: the open game's PLAYER.DAT (DATA's template otherwise), then the changes given ("sword=20, strength=25, easy").</summary>
        PlayerStats CtStats(string stats)
        {
            var p = PlayerStats.From(Session.Player);
            foreach (var part in (stats ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                if (t.Equals("easy", StringComparison.OrdinalIgnoreCase)) { p.Easy = true; continue; }
                int eq = t.IndexOf('=');
                if (eq < 0) throw new ArgumentException("\"" + t + "\": give stats as name=value (strength, dexterity, or a skill: " + string.Join(", ", PlayerDat.SkillNames) + "), or easy.");
                string name = t.Substring(0, eq).Trim();
                int v = CtInt(t.Substring(eq + 1), name);
                if (name.Equals("strength", StringComparison.OrdinalIgnoreCase)) p.Strength = v;
                else if (name.Equals("dexterity", StringComparison.OrdinalIgnoreCase)) p.Dexterity = v;
                else
                {
                    int k = Array.FindIndex(PlayerDat.SkillNames, s => s.Equals(name, StringComparison.OrdinalIgnoreCase) || (name.Equals("defence", StringComparison.OrdinalIgnoreCase) && s == "Defense"));
                    if (k < 0 || k >= p.Skills.Length) throw new ArgumentException("No stat \"" + name + "\": strength, dexterity, easy, or a skill: " + string.Join(", ", PlayerDat.SkillNames.Take(p.Skills.Length)) + ".");
                    if (v < 0 || v > 30) throw new ArgumentOutOfRangeException(null, "A skill is 0-30.");
                    p[k] = v;
                }
            }
            return p;
        }

        static string CtStatsText(PlayerStats p)
        {
            return string.Format(Ci, "Strength {0}, Dexterity {1}, {2}{3}", p.Strength, p.Dexterity,
                string.Join(", ", Enumerable.Range(0, p.Skills.Length).Where(i => p.Skills[i] > 0).Select(i => PlayerDat.SkillNames[i] + " " + p.Skills[i])), p.Easy ? ", easy" : "");
        }

        int CtCreatureClass(string creature)
        {
            string t = (creature ?? "").Trim();
            int n;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || t.All(char.IsDigit))
            {
                n = CtInt(t, "The creature");
                if (n >= 0x40 && n < 0x80) return n - 0x40;
                if (n >= 0 && n < 64) return n;
                throw new ArgumentOutOfRangeException(null, "A creature is 0x40-0x7F (or its class 0-63).");
            }
            var hits = Enumerable.Range(0, 64).Where(c => (Session.Names.Get(0x40 + c) ?? "").Equals(t, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) hits = Enumerable.Range(0, 64).Where(c => (Session.Names.Get(0x40 + c) ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count == 0) throw new ArgumentException("No creature is called \"" + t + "\".");
            throw new ArgumentException("\"" + t + "\" names several creatures: " + string.Join(", ", hits.Select(c => string.Format(Ci, "0x{0:X2} {1}", 0x40 + c, Session.Names.Get(0x40 + c)))) + ".");
        }

        public string BalanceWeapons(string stats, string creature, int? defence, int? armour, bool powerful, double aimSeconds, bool inWorldOnly)
        {
            Require();
            var t = TblObjects();
            var p = CtStats(stats);
            var target = new BalanceTarget();
            string against = "a custom target";
            if (!string.IsNullOrWhiteSpace(creature))
            {
                int cls = CtCreatureClass(creature);
                target = WeaponBalance.TargetOf(t, cls);
                against = string.Format(Ci, "{0} (0x{1:X2})", Session.Names.Get(0x40 + cls), 0x40 + cls);
            }
            if (defence.HasValue) target.Defence = defence.Value;
            if (armour.HasValue) target.Armour = armour.Value;
            target.Powerful = powerful;
            if (aimSeconds < 0 || aimSeconds > 3) throw new ArgumentOutOfRangeException(null, "Aiming takes 0-3 seconds.");
            var rows = WeaponBalance.Rows(Session, t, CtLevels(), p, target, aimSeconds);
            if (inWorldOnly) rows = rows.Where(r => r.Total > 0).ToList();
            var b = new StringBuilder();
            b.AppendFormat(Ci, "Weapons for a player of {0}, against {1}: defence {2}, armour {3}{4}. Expected damage misses and criticals counted; a swing released at full charge.\n",
                CtStatsText(p), against, target.Defence, target.Armour, target.Powerful ? ", powerful" : "");
            b.Append("\nMELEE: weapon [enchantment: effect] | skill | base slash/bash/stab | charge | speed | swing s | per swing slash/bash/stab | per second slash/bash/stab | best/s | in world (plain/ench./special/other)\n");
            foreach (var r in rows.Where(r => !r.Ranged))
                b.AppendFormat(Ci, "{0}{1} | {2} | {3} | {4} | {5} | {6} | {7}/{8}/{9} | {10}/{11}/{12} | {13} | {14} ({15}/{16}/{17}/{18})\n",
                    r.Enchantment.Length > 0 ? "  " : "", r.Name + (r.Enchantment.Length > 0 ? " [" + r.Enchantment + ": " + r.Effect + "]" : ""), r.Skill, string.Join("/", r.Base), F2(r.MaxCharge / 128.0), r.Speed,
                    Math.Abs(r.Seconds[2] - r.Seconds[0]) < 0.001 ? F2(r.Seconds[0]) : F2(r.Seconds[0]) + "/" + F2(r.Seconds[2]),
                    F1(r.PerSwing[0]), F1(r.PerSwing[1]), F1(r.PerSwing[2]), F2(r.PerSecond[0]), F2(r.PerSecond[1]), F2(r.PerSecond[2]), F2(r.BestPerSecond), r.Total, r.Plain, r.Enchanted, r.Special, r.Other);
            b.Append("\nLAUNCHERS: launcher [enchantment] | ammunition | damage | per shot | shot s | per s | in world | ammunition in world\n");
            foreach (var r in rows.Where(r => r.Ranged))
                b.AppendFormat(Ci, "{0}{1} | {2} | {3} | {4} | {5} | {6} | {7} | {8}\n", r.Enchantment.Length > 0 ? "  " : "", r.Name + (r.Enchantment.Length > 0 ? " [" + r.Enchantment + "]" : ""),
                    r.AmmoName, r.Base[0], F1(r.PerSwing[0]), F2(r.Seconds[0]), F2(r.PerSecond[0]), r.Total, r.AmmoCount);
            var heads = rows.Where(r => r.Enchantment.Length == 0).ToList();
            var bestM = heads.Where(r => !r.Ranged && r.Total > 0).OrderByDescending(r => r.BestPerSecond).FirstOrDefault();
            var bestL = heads.Where(r => r.Ranged && r.Total > 0).OrderByDescending(r => r.BestPerSecond).FirstOrDefault();
            b.AppendFormat(Ci, "\nBest melee weapon in the world: {0}; best launcher: {1}. The weapons' numbers are the Weapons and Ranged tables (table_set).\n",
                bestM == null ? "none" : bestM.Name + " (" + F2(bestM.BestPerSecond) + " a second)", bestL == null ? "none" : bestL.Name + " (" + F2(bestL.BestPerSecond) + " a second)");
            return b.ToString();
        }

        public string BalanceArmour(string stats, int? weaponSkill, int quality, int toHit, int damage, bool inWorldOnly)
        {
            Require();
            var t = TblObjects();
            var p = CtStats(stats);
            if (quality < 0 || quality > 63) throw new ArgumentOutOfRangeException(null, "Quality is 0-63.");
            if (toHit < 0 || toHit > 40) throw new ArgumentOutOfRangeException(null, "The blow's to-hit is 0-40.");
            if (damage < 1 || damage > 60) throw new ArgumentOutOfRangeException(null, "The blow's damage is 1-60.");
            if (weaponSkill.HasValue) { if (weaponSkill < 0 || weaponSkill > 30) throw new ArgumentOutOfRangeException(null, "A skill is 0-30."); p[PlayerStats.Sword] = weaponSkill.Value; }
            var blow = new BalanceBlow { ToHit = toHit, Damage = damage };
            var rows = ArmourBalance.Rows(Session, t, CtLevels(), p, PlayerStats.Sword, blow, quality);
            if (inWorldOnly) rows = rows.Where(r => r.Total > 0).ToList();
            var b = new StringBuilder();
            b.AppendFormat(Ci, "Armour at quality {0}, worn by a player of {1} (weapon skill {2}), against a blow of to-hit {3} and damage {4}. Bare: {5} damage a blow.\n",
                quality, CtStatsText(p), p[PlayerStats.Sword], toHit, damage, rows.Count > 0 ? F2(rows[0].Bare) : "?");
            b.Append("item [enchantment: effect] | slot | protection | armour (intended) | to-hit penalty | mass st | wear class | taken worn (intended) | saved % | in world (plain/ench./other)\n");
            foreach (var r in rows)
                b.AppendFormat(Ci, "{0}{1} | {2} | {3} | {4}{5} | {6} | {7} | {8} | {9}{10} | {11} | {12} ({13}/{14}/{15}){16}\n",
                    r.Enchantment.Length > 0 ? "  " : "", r.Name + (r.Enchantment.Length > 0 ? " [" + r.Enchantment + ": " + r.Effect + "]" : ""), r.Slot, r.Protection, r.Armour,
                    r.IntendedArmour != r.Armour ? " (" + r.IntendedArmour + ")" : "", r.ToHitPenalty, F1(r.MassStones), r.QualityClass >= 3 ? "never" : "2d4>>" + r.QualityClass,
                    F2(r.Worn), Math.Abs(r.IntendedWorn - r.Worn) > 0.001 ? " (" + F2(r.IntendedWorn) + ")" : "", F1(r.SavedPercent), r.Total, r.Plain, r.Enchanted, r.Other, r.Bugged ? "  [the game's slip: see intended]" : "");
            b.Append("The armour's numbers are the Armour and Common properties tables (table_set).\n");
            return b.ToString();
        }

        // ---- barter

        public string BarterTraders()
        {
            Require();
            var lib = CtLibOrNull();
            var b = new StringBuilder("Creatures of the levels with a conversation of their own (trader \"level:index\"); any creature class also trades by number or name:\n");
            var levels = CtLevels();
            foreach (var slot in Session.LevelSlots().Where(x => x.Present))
            {
                LevelModel m;
                try { m = levels(slot.Level); } catch (Exception) { continue; }
                if (m == null) continue;
                foreach (var o in m.Objects)
                {
                    if (o == null || o.Free || !o.IsCritter || !o.IsMobile || o.Index == 0 || o.Record == null || o.Record.Length <= 0x1A || o.Record[0x1A] == 0) continue;
                    int cs = Barter.ConversationSlot(o);
                    var likes = lib == null ? null : Barter.LikesOf(lib, cs);
                    b.AppendFormat(Ci, "{0}:{1}  {2} ({3}), conversation {4}{5}\n", slot.Level, o.Index, lib?.Name(cs) ?? "whoami " + o.Record[0x1A], Session.Names.Get(o.Item), cs,
                        likes != null && likes.Any ? string.Format(Ci, ", likes {0}, dislikes {1}", likes.Likes.Count, likes.Dislikes.Count) : "");
                }
            }
            return b.ToString();
        }

        ConversationLibrary CtLibOrNull()
        {
            try { return CtLib(); } catch (McpEditorException) { return null; }
        }

        List<BarterItem> CtItems(string list, string side)
        {
            var r = new List<BarterItem>();
            foreach (var part in (list ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                int q = 63, n = 1;
                int at = t.LastIndexOf('@');
                if (at > 0) { q = CtInt(t.Substring(at + 1), "A quality"); t = t.Substring(0, at).Trim(); }
                int x = t.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
                if (x > 0 && t.Substring(x + 2).Trim().All(char.IsDigit) && t.Length > x + 2) { n = CtInt(t.Substring(x + 2), "A quantity"); t = t.Substring(0, x).Trim(); }
                if (q < 0 || q > 63) throw new ArgumentOutOfRangeException(null, "A quality is 0-63.");
                if (n < 1 || n > 511) throw new ArgumentOutOfRangeException(null, "A quantity is 1-511.");
                int item = CtItem(t);
                r.Add(new BarterItem { Item = item, Quantity = n, Quality = q, From = side });
            }
            return r;
        }

        /// <summary>An item by number (0xA0 or 160) or by its exact (or only partly matching) name.</summary>
        int CtItem(string t)
        {
            t = (t ?? "").Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || (t.Length > 0 && t.All(char.IsDigit)))
            {
                int v = CtInt(t, "An item");
                if (v < 0 || v >= 0x200) throw new ArgumentOutOfRangeException(null, "Items are 0x000-0x1FF.");
                return v;
            }
            var exact = Enumerable.Range(0, 0x200).Where(i => (Session.Names.Get(i) ?? "").Equals(t, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count >= 1) return exact[0];
            var part = Enumerable.Range(0, 0x200).Where(i => (Session.Names.Get(i) ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (part.Count == 1) return part[0];
            if (part.Count == 0) throw new ArgumentException("No item is called \"" + t + "\" (item_find lists items).");
            throw new ArgumentException("\"" + t + "\" names several items: " + string.Join(", ", part.Take(10).Select(i => string.Format(Ci, "0x{0:X3} {1}", i, Session.Names.Get(i)))) + (part.Count > 10 ? "..." : "") + ".");
        }

        public string BarterSimulate(string playerItems, string traderItems, string trader, string stats, int? coinQuality, string settle)
        {
            Require();
            var t = TblObjects();
            var classes = Barter.Classes(Session, t);
            var p = CtStats(stats);
            int cls, objectIndex = 0, slot;
            string who;
            string tr = (trader ?? "").Trim();
            if (tr.Length == 0) throw new ArgumentException("No trader given: \"level:index\" of a creature (barter_traders lists them), or a creature by number or name.");
            int colon = tr.IndexOf(':');
            if (colon > 0)
            {
                int lev = CtInt(tr.Substring(0, colon), "The level"), idx = CtInt(tr.Substring(colon + 1), "The object index");
                var m = ReadModel(lev);
                if (idx <= 0 || idx >= m.Objects.Length || m.Objects[idx] == null || m.Objects[idx].Free || !m.Objects[idx].IsCritter || !m.Objects[idx].IsMobile)
                    throw new McpEditorException("Object " + idx + " of level " + lev + " is not a creature.");
                var o = m.Objects[idx];
                cls = o.Item & 0x3F; objectIndex = o.Index; slot = Barter.ConversationSlot(o);
                who = string.Format(Ci, "{0} #{1} of level {2}", Session.Names.Get(o.Item), o.Index, lev);
            }
            else
            {
                cls = CtCreatureClass(tr);
                slot = 0x100 + cls;
                who = "any " + Session.Names.Get(0x40 + cls) + " (the class's typical figures)";
            }
            var lib = CtLibOrNull();
            var likes = lib == null ? null : Barter.LikesOf(lib, slot);
            var temper = Barter.Temper(classes[cls], objectIndex, p[PlayerStats.Charm]);
            var player = CtItems(playerItems, "player");
            var npc = CtItems(traderItems, "trader");
            Func<int, int> com = i => t.Get("Common properties", i, "Value");
            var b = new StringBuilder();
            b.AppendFormat(Ci, "Trading with {0}, conversation {1}{2}. Player: Charm {3}, Appraise {4}.\n", who, slot, lib != null && lib.Name(slot) != null ? " (" + lib.Name(slot) + ")" : "", p[PlayerStats.Charm], p[PlayerStats.Appraise]);
            b.AppendFormat(Ci, "The trader wants {0}% more than it gives (greed), takes {1} bad offers (patience), may misjudge a value by up to {2}%{3}.\n", temper.Greed, temper.Patience, temper.Assess,
                temper.Exact ? " (drawn for this object as the game draws them)" : string.Format(Ci, " (greed {0} to {1}, patience {2} to {3}, assess {4} to {5} for one of the levels)", temper.GreedMin, temper.GreedMax, temper.PatienceMin, temper.PatienceMax, temper.AssessMin, temper.AssessMax));
            if (likes != null && likes.Any)
                b.Append("Likes (worth 1.5 times to it): ").Append(likes.Likes.Count == 0 ? "none" : string.Join(", ", likes.Likes.Select(e => BarterLikes.Describe(e, Session.Names))))
                 .Append("; dislikes (worth nothing): ").Append(likes.Dislikes.Count == 0 ? "none" : string.Join(", ", likes.Dislikes.Select(e => BarterLikes.Describe(e, Session.Names)))).Append(".\n");
            string mode = (settle ?? "none").Trim().ToLowerInvariant();
            if (mode != "none" && mode != "greed" && mode != "even") throw new ArgumentException("settle is none, greed (the fewest coins the trader accepts) or even (an even trade).");
            if (mode != "none")
            {
                int q = coinQuality ?? Barter.DefaultCoinQuality(Session, CtLevels());
                bool toPlayer; string note;
                var coins = Barter.Settle(Session.Kind, player, npc, com, likes, temper, q, mode == "even", out toPlayer, out note);
                if (coins.Count > 0)
                {
                    (toPlayer ? player : npc).AddRange(coins);
                    b.AppendFormat(Ci, "Coins added to the {0}'s side: {1} (quality {2}).\n", toPlayer ? "player" : "trader", string.Join(" + ", coins.Select(c => c.Quantity)), q);
                }
                if (note != null) b.Append(note).Append('\n');
            }
            if (player.Count == 0 && npc.Count == 0) throw new ArgumentException("Give the items of at least one side, as \"item x quantity @ quality, ...\" (e.g. \"0xA0 x 20, sword @ 40\").");
            var r = Barter.Offer(Session.Kind, player, npc, com, likes, temper, p[PlayerStats.Appraise], Session.Strings);
            for (int i = 0; i < player.Count; i++) b.AppendFormat(Ci, "  player gives {0} x{1} q{2}: worth {3} to the trader\n", Session.Names.Get(player[i].Item), player[i].Quantity, player[i].Quality, r.PlayerItems[i]);
            for (int i = 0; i < npc.Count; i++) b.AppendFormat(Ci, "  trader gives {0} x{1} q{2}: worth {3}\n", Session.Names.Get(npc[i].Item), npc[i].Quantity, npc[i].Quality, r.TraderItems[i]);
            b.AppendFormat(Ci, "Worth: player's side {0}, trader's side {1}; evaluation {2} against greed {3}: {4}. With the trader's misjudging: {5} at worst ({6}), {7} at best ({8}).\n",
                r.PlayerWorth, r.TraderWorth, r.Evaluation, temper.Greed, r.Accepted ? "ACCEPTED" : "REFUSED", r.WorstEvaluation, r.AcceptedAtWorst ? "accepted" : "refused", r.BestEvaluation, r.AcceptedAtBest ? "accepted" : "refused");
            if (r.Judgement != null) b.AppendFormat(Ci, "The player's own remark: \"{0}\" (Appraise {1}: off by up to {2}%).\n", r.Judgement.Trim(), p[PlayerStats.Appraise], Barter.AppraiseError(p[PlayerStats.Appraise]));
            foreach (var n in r.Notes) b.Append(n).Append('\n');
            return b.ToString();
        }
    }
}
