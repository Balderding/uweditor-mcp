using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Editing
{
    /// <summary>What the checks before making a mod found in the saved game.</summary>
    public sealed class ModCheckResult
    {
        /// <summary>The problems, worst first: levels that fail the game's list check, then what the game will misbehave on, then the world check's.</summary>
        public readonly List<string> Problems = new List<string>();
        /// <summary>Things that are odd but may be meant, counted by kind.</summary>
        public readonly List<string> Warnings = new List<string>();
        public int LevelsChecked;

        /// <summary>The report for the Make a mod preview: the problems first, each on a line.</summary>
        public string Report(int most = 40)
        {
            var sb = new System.Text.StringBuilder();
            if (Problems.Count == 0)
                sb.AppendFormat("Checked {0} level{1}: the object lists pass the game's check, and the world check and the Fixes found no problem.\n", LevelsChecked, LevelsChecked == 1 ? "" : "s");
            else
            {
                sb.AppendFormat("CHECK FIRST: {0} problem{1} in the saved game (Level ▸ Fixes and Level ▸ World check show and repair them):\n", Problems.Count, Problems.Count == 1 ? "" : "s");
                foreach (var p in Problems.Take(most)) sb.Append("  ").Append(p).Append('\n');
                if (Problems.Count > most) sb.AppendFormat("  … and {0} more.\n", Problems.Count - most);
            }
            if (Warnings.Count > 0) sb.Append("Maybe meant: ").Append(string.Join("; ", Warnings)).Append(".\n");
            return sb.ToString();
        }
    }

    /// <summary>
    /// The checks run when a mod is made, on the game as saved (a mod holds only what is saved):
    /// the game's own object list check on every level, the Fixes window's checks, and the world
    /// check (locks without a key, levels full of objects, levels that cannot be read).
    /// </summary>
    public static class ModChecks
    {
        public static ModCheckResult Run(GameSession s, Func<int, LevelModel> level = null)
        {
            level = level ?? (l => s.LoadLevel(l));
            var r = new ModCheckResult();
            var lists = new List<string>(); var problems = new List<string>();
            var warn = new Dictionary<string, int>();
            FixContext ctx = null;
            try { ctx = FixContext.Build(s, level); } catch (Exception) { }
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present || slot.Damage != null) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception ex) { lists.Add("Level " + slot.Level + " could not be read: " + ex.Message); continue; }
                r.LevelsChecked++;
                if (!m.CheckOk) lists.Add(string.Format("Level {0} fails the game's object list check: {1}", slot.Level, m.CheckReport));
                List<LevelFinding> found;
                try { found = LevelFixes.Find(s, slot.Level, m.Block.Data, ctx); } catch (Exception) { continue; }
                foreach (var f in found)
                {
                    if (f.Severity == FixSeverity.Warning) { int n; warn.TryGetValue(f.Check.Title, out n); warn[f.Check.Title] = n + 1; }
                    else (f.Severity == FixSeverity.ListError ? lists : problems).Add(string.Format("Level {0}, {1}: {2}", slot.Level, f.Check.Title, f.Text));
                }
            }
            r.Problems.AddRange(lists);
            r.Problems.AddRange(problems);
            try
            {
                foreach (var w in WorldCheck.Run(s, level))
                {
                    if (w.Severity >= 2) r.Problems.Add(w.Text);
                    else if (w.Severity == 1) { int n; warn.TryGetValue("world check: " + w.Kind, out n); warn["world check: " + w.Kind] = n + 1; }
                }
            }
            catch (Exception ex) { r.Problems.Add("The world check could not run: " + ex.Message); }
            foreach (var kv in warn.OrderByDescending(k => k.Value)) r.Warnings.Add(kv.Value + " × " + kv.Key.ToLowerInvariant());
            return r;
        }
    }
}
