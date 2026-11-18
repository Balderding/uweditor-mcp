using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The randomizer's heavy runs, as jobs of three stages: set up on the host's thread (a
    /// snapshot of the levels with their unsaved edits, copies of the options and the zoning), the
    /// work on the caller's thread (so the editor's window keeps answering), and the answer back
    /// on the host's thread (an apply only when the levels did not change meanwhile). The key item
    /// scatter (Randomizer > Scatter > Apply), the automatic zones, the solve over time (Check if
    /// solvable / Check the logic) and what it reaches.
    /// </summary>
    public sealed partial class McpEditor
    {
        /// <summary>The last solve over time: its result, what it was made with, the levels' hash.</summary>
        sealed class RzSolveRun
        {
            public TimelineResult Result;
            public string With = "", Levels = "";
        }

        RzSolveRun rzSolve;

        /// <summary>The key scatter's options for a run: a copy, with the zoning (the zoning's when it has zones, else automatic) and the names of the places.</summary>
        KeyShuffleOptions RzScatterOptions(RzTool t)
        {
            var o = ((KeyShuffleOptions)t.Options).Clone();
            var z = RzZoning();
            o.Names = null; o.NamesFrom = "";
            if (z.HasZones)
            {
                o.Zoning = z.ForSolver();
                o.ZoningFrom = Path.GetFileName(z.Path) + (z.Dirty ? " with its unsaved changes" : "") + (z.SplitByLevel ? ", multi-level zones split by level" : "");
                if (!AutoZones.IsAutomatic(o.Zoning)) { o.Names = o.Zoning; o.NamesFrom = "the zoning (" + o.ZoningFrom + ")"; }
            }
            else { o.Zoning = null; o.ZoningFrom = ""; }
            if (o.Names == null)
            {
                try
                {
                    string path = Path.Combine(Session.DataFolder, ZoneSet.DefaultFileName(Session.IsUw2));
                    if (File.Exists(path)) { o.Names = ZoneSet.Load(path, Session.Profile.LevelSlots); o.NamesFrom = "the zone file in DATA (" + Path.GetFileName(path) + ")"; }
                }
                catch (Exception) { o.Names = null; }
            }
            return o;
        }

        /// <summary>Preview or apply the key item scatter: the solver once a key item, then a fresh solve of the result (seconds to minutes).</summary>
        Func<Func<string>> RzScatterJob(RzTool t, bool apply)
        {
            var s = Session;
            var edits = Edits;
            string levels = RandomizerSettings.LevelsHash(s, edits);
            string settings = RzSettingsText(t, true);
            string stamp = RandomizerSettings.LogLine(settings) + "\n\n";
            var last = t.Last as KeyShuffleResult;
            if (apply && last != null && last.Success && !last.IsApplied && t.LastSettings == settings && t.LastLevels == levels)
                return () => () => RzScatterApply(t, last, levels, true);
            var snap = KeyShuffle.Snapshot(s, edits);
            var o = RzScatterOptions(t);
            return () =>
            {
                var r = KeyShuffle.RunOn(s, snap, o);
                return () =>
                {
                    if (Session != s) throw new McpEditorException("Another game was opened while the scatter ran: nothing was applied.");
                    if (r.Options != null) r.Log.Insert(0, stamp);
                    t.Last = r; t.LastSettings = settings; t.LastLevels = levels;
                    if (!apply || !r.Success) return RzKeep(t, "The spoiler log", r.Log.ToString(), (apply ? "Nothing applied. " : "Preview, nothing changed. ") + RzScatterSummary(r));
                    return RzScatterApply(t, r, levels, false);
                };
            };
        }

        string RzScatterApply(RzTool t, KeyShuffleResult r, string levels, bool fromPreview)
        {
            if (RandomizerSettings.LevelsHash(Session, Edits) != levels) throw new McpEditorException("The levels changed while the scatter ran: nothing was applied. Apply again.");
            var opt = (KeyShuffleOptions)t.Options;
            r.Options.StartKit = opt.StartKit;
            r.Options.ExploreMaps = opt.ExploreMaps;
            r.Options.SpoilerNotes = opt.SpoilerNotes;
            KeyShuffle.Apply(r, Edits);
            if (r.AppliedAids.Count > 0)
            {
                r.Log.AppendLine();
                r.Log.AppendLine("AT APPLY");
                foreach (var l in r.AppliedAids) r.Log.AppendLine("  " + l);
            }
            t.Applied.Add(r);
            t.Last = null;
            RzApplied("Key items scattered (seed " + r.UsedSeed + ", by the assistant). Edit > Undo (per level) puts them back.");
            return RzKeep(t, "The spoiler log", r.Log.ToString(), "Applied" + (fromPreview ? " (the preview's result)" : "") + ", not saved yet; randomizer_undo puts it back. " + RzScatterSummary(r));
        }

        string RzScatterSummary(KeyShuffleResult r)
        {
            if (r.Refused != null) return r.Refused;
            if (!r.Success) return "No shuffle passed the check in " + r.Attempts + " attempts; see the log.";
            string start = "";
            if (r.Start != null && r.StartFrom != null)
                start = string.Format(" The game starts at level {0} ({1}, {2}): the first step at level {3} ({4}, {5}) teleports the player there.", r.Start.Level, (int)r.Start.X, (int)r.Start.Y, r.StartFrom.Level, (int)r.StartFrom.X, (int)r.StartFrom.Y)
                    + (KeyShuffle.LockedWorldNote(Session, r.Start.Level) is string locked ? " " + locked : "");
            return string.Format("Seed {0}{1}: {2} items move ({3} key items), {4} levels change; the shuffled game reaches {5} in {6} spheres (the original in {7}).{8}{9}",
                r.UsedSeed, r.UsedSeed != r.Options.Seed ? " (after " + (r.Attempts - 1) + " failed)" : "", r.Moves.Count(m => m.Item != m.Place), r.Moves.Count(m => m.ByFill && m.Item != m.Place), r.After.Count,
                Session.IsUw2 ? "the end" : "level 9", r.SpheresAfter, r.SpheresBefore, r.Unlocked.Count > 0 ? " " + r.Unlocked.Count + " door(s) unlocked." : "", start);
        }

        /// <summary>Whether a square can be the key scatter's given start (as its window's Preview says): the zones built once, a solve from the square's zone (seconds).</summary>
        public Func<Func<string>> RandomizerStartCheckJob(int level, int x, int y)
        {
            var t = RzGet("key-scatter");
            CheckLevelNumber(Session, level);
            if (x < 1 || x > 62 || y < 1 || y > 62) throw new ArgumentOutOfRangeException(null, "A start square is 1-62 each way.");
            var s = Session;
            var snap = KeyShuffle.Snapshot(s, Edits);
            var o = RzScatterOptions(t);
            return () =>
            {
                var c = StartPicker.Check(s, snap.Level, o, KeyShuffleOptions.GivenStart(level, x, y));
                return () => c.ToString() + (c.Made.Count > 0 ? "; the scatter would make: " + string.Join(", ", c.Made) : "") + (c.HostilesNear > 0 ? "; " + c.HostilesNear + " hostile creature(s) on or beside it" : "") + ".";
            };
        }

        public string RandomizerStartCheck(int level, int x, int y) { return RunJob(RandomizerStartCheckJob(level, x, y)); }

        // ---- the automatic zones

        Func<Func<string>> RzAutoZonesJob(RzTool t, bool apply, bool replace)
        {
            var s = Session;
            var o = (AutoZoneSettings)t.Options;
            var nav = o.Nav.Clone();
            var logic = o.Logic.Clone();
            var snap = KeyShuffle.Snapshot(s, Edits);
            string levels = RandomizerSettings.LevelsHash(s, Edits);
            if (apply && !replace && RzZoning().Dirty) throw new McpEditorException("The zoning has unsaved changes: save it (zones_save), or apply with replace to put the automatic zones in its place.");
            return () =>
            {
                var res = Timeline.Build(s, snap.Level, nav, logic);
                return () =>
                {
                    if (Session != s) throw new McpEditorException("Another game was opened while the zones were built.");
                    rzSolve = new RzSolveRun { Result = res, With = "automatic zones with the auto-zones options", Levels = levels };
                    string head = RzAutoSummary(res);
                    if (!apply) return RzKeep(t, "The report", Timeline.Report(res), "Built (the zoning is unchanged; apply puts these zones in it). " + head);
                    var z = RzZoning();
                    if (z.Dirty && !replace) throw new McpEditorException("The zoning got unsaved changes while the zones were built: nothing was replaced.");
                    z.Adopt(ZoneSet.FromJson(res.Abilities.Auto.Set.ToJson(), s.Profile.LevelSlots), Path.Combine(s.DataFolder, AutoZones.DefaultFileName(s.IsUw2)));
                    return RzKeep(t, "The report", Timeline.Report(res), "The automatic zones are the zoning now (not saved: zones_save writes " + AutoZones.DefaultFileName(s.IsUw2) + " in DATA). " + head);
                };
            };
        }

        static string RzAutoSummary(TimelineResult t)
        {
            var a = t.Abilities;
            var g = a.Auto.Graph;
            var zs = a.Auto.Set;
            var sv = t.Solve;
            return string.Format("{0} regions, {1} edges, {2} zones, {3} links, {4} placements, {5} mechanisms ({6} applied when set off, {7} doors closed behind), {8} levers ({9} out of reach), {10} ability sources, {11} hidden gates. Timeline: {12}, {13} of {14} zones reached, {15} changes applied, {16} one-way traps.",
                g.Regions.Count, g.Edges.Count, zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), zs.Links.Count, zs.Placements.Count, g.Mechanisms.Count, t.Changes.Count(c => c.Applied), t.Closers.Count, g.Levers.Count, g.Levers.Count(l => l.OutOfReach),
                a.Sources.Count, a.Hidden.Count,
                sv == null ? "failed" : sv.GoalReached ? "goal reached" : "goal not reached", Timeline.BaseZonesReached(sv), zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), sv?.Applied.Count ?? 0, sv?.Traps.Count ?? 0);
        }

        // ---- the solve over time: is the game beatable, and what is reached when

        /// <summary>One line on a solve, as the Zones window's Check if solvable.</summary>
        static string RzVerdict(TimelineResult r)
        {
            var sv = r.Solve;
            string main = sv == null ? "The solve failed." : sv.GoalReached ? "Solvable with these options (the goal reached by " + sv.GoalBy + ")." : "NOT solvable with these options: missing " + string.Join(", ", sv.GoalMissing.Distinct().Take(6)) + ".";
            if (r.Variants.Count == 0) return main;
            var fail = r.Variants.Where(v => v.Value == null || !v.Value.GoalReached).Select(v => v.Key).ToList();
            return main + string.Format(" Of the {0} main option sets, {1} reach the goal{2}.", r.Variants.Count, r.Variants.Count - fail.Count, fail.Count > 0 ? " (not: " + string.Join("; ", fail) + ")" : "");
        }

        /// <summary>
        /// The solve over time (Check if solvable): the zoning (its zones, else automatic zones) on the
        /// levels with their unsaved edits, with the movement and logic options of the key scatter (or
        /// of the automatic zones), and the main option sets. Kept for the reach tools.
        /// </summary>
        public Func<Func<string>> RandomizerSolveJob(string options, string zoning)
        {
            RzFresh();
            string from = string.IsNullOrWhiteSpace(options) ? "key-scatter" : options.Trim().ToLowerInvariant();
            NavOptions nav; AbilityOptions logic;
            if (from == "key-scatter" || from == "scatter") { var o = (KeyShuffleOptions)RzGet("key-scatter").Options; nav = o.Nav.Clone(); logic = o.Logic.Clone(); from = "the key scatter's options"; }
            else if (from == "auto-zones" || from == "autozones") { var o = (AutoZoneSettings)RzGet("auto-zones").Options; nav = o.Nav.Clone(); logic = o.Logic.Clone(); from = "the automatic zones' options"; }
            else if (from == "default" || from == "defaults") { nav = new NavOptions(); logic = new AbilityOptions(); from = "the default options"; }
            else throw new McpEditorException("options is key-scatter, auto-zones or default.");
            string zw = string.IsNullOrWhiteSpace(zoning) ? "zones" : zoning.Trim().ToLowerInvariant();
            if (zw != "zones" && zw != "automatic") throw new McpEditorException("zoning is zones (the zoning, automatic zones when it has none) or automatic.");
            var z = RzZoning();
            string split = "";
            ZoneSet given = zw == "zones" && z.HasZones ? z.ForSolver(out split) : null;
            var s = Session;
            var snap = KeyShuffle.Snapshot(s, Edits);
            string levels = RandomizerSettings.LevelsHash(s, Edits);
            string with = (given != null ? "the zoning (" + Path.GetFileName(z.Path) + (z.Dirty ? ", unsaved changes included" : "") + (z.SplitByLevel ? ", split by level: " + split : "") + ")" : "automatic zones") + ", " + from;
            return () =>
            {
                var res = Timeline.Build(s, snap.Level, nav, logic, null, null, true, given);
                return () =>
                {
                    if (Session != s) throw new McpEditorException("Another game was opened while the solve ran.");
                    rzSolve = new RzSolveRun { Result = res, With = with, Levels = levels };
                    var sv = res.Solve;
                    return RzVerdict(res) + "\nSolved on " + with + ". " + (sv == null ? "" : string.Format("{0} spheres, {1} of {2} zones reached, {3} one-way trap(s), {4} ability source(s).",
                        sv.Spheres.Count, sv.ZoneSphere.Count, res.Set.Zones.Count(x => x.Id > ZoneSet.Unassigned), sv.Traps.Count, res.Abilities.Sources.Count))
                        + " randomizer_solve_report has the full report; randomizer_reach the spheres, a square's or an object's zone, and the ability sources.";
                };
            };
        }

        public string RandomizerSolve(string options = null, string zoning = null) { return RunJob(RandomizerSolveJob(options, zoning)); }

        RzSolveRun RzLastSolve()
        {
            RzFresh();
            if (rzSolve == null) throw new McpEditorException("No solve yet: run randomizer_solve (or preview auto-zones) first.");
            return rzSolve;
        }

        string RzStale(RzSolveRun r) { return RandomizerSettings.LevelsHash(Session, Edits) != r.Levels ? " (The levels changed since this solve: solve again for the levels as they are.)" : ""; }

        /// <summary>A part of the last solve's full report.</summary>
        public string RandomizerSolveReport(int from, int length)
        {
            var r = RzLastSolve();
            string text = RzVerdict(r.Result) + "\n\n" + Timeline.Report(r.Result);
            if (from < 0 || from >= text.Length) throw new ArgumentOutOfRangeException(null, "from is 0-" + (text.Length - 1) + ".");
            if (length < 1) length = 8000;
            length = Math.Min(Math.Min(length, 50000), text.Length - from);
            return "The solve on " + r.With + RzStale(r) + ", characters " + from + "-" + (from + length) + " of " + text.Length + ":\n" + text.Substring(from, length);
        }

        string RzZoneName(ZoneSet set, int id)
        {
            var z = set.GetZone(id);
            return set.Label(id) + (z != null && z.Name.Length > 0 ? " " + z.Name : "");
        }

        /// <summary>
        /// What the last solve reaches: spheres (each sphere's zones and the items, keys and
        /// abilities gained), unreached (the zones never reached), square (the zone of a square
        /// and when it is reached), object (an object's), sources (where each ability comes from
        /// and when it is in reach), blocked (the ways the solve found shut, and the one-way traps).
        /// </summary>
        public string RandomizerReach(string what, int? level, int? x, int? y, int? index)
        {
            var run = RzLastSolve();
            var res = run.Result;
            var sv = res.Solve;
            if (sv == null) throw new McpEditorException("The last solve failed: there is nothing reached to show.");
            var set = res.Set;
            var b = new StringBuilder();
            string stale = RzStale(run);
            switch ((what ?? "spheres").Trim().ToLowerInvariant())
            {
                case "spheres":
                    b.Append(RzVerdict(res)).Append(stale).Append('\n');
                    foreach (var sp in sv.Spheres)
                    {
                        b.Append("Sphere ").Append(sp.Index).Append(": zones ").Append(string.Join(", ", sp.Zones.Select(z => RzZoneName(set, z))));
                        if (sp.Tokens.Count > 0) b.Append("; gains ").Append(string.Join(", ", sp.Tokens));
                        if (sp.SetOff.Count > 0) b.Append("; sets off ").Append(string.Join(", ", sp.SetOff));
                        b.Append('\n');
                        if (b.Length > 60000) { b.Append("... (more spheres: randomizer_solve_report)\n"); break; }
                    }
                    return b.ToString();
                case "unreached":
                    {
                        var not = set.Zones.Where(z => z.Id > ZoneSet.Unassigned && !sv.ZoneSphere.ContainsKey(z.Id)).ToList();
                        b.Append(not.Count).Append(" zone(s) never reached").Append(stale).Append(not.Count > 0 ? ":\n" : ".\n");
                        foreach (var z in not.Take(500)) b.Append(RzZoneName(set, z.Id)).Append(" (levels ").Append(string.Join(", ", set.LevelsOf(z.Id))).Append(")\n");
                        if (!sv.GoalReached) b.Append("The goal is missing: ").Append(string.Join(", ", sv.GoalMissing.Distinct())).Append('\n');
                        return b.ToString();
                    }
                case "square":
                    {
                        if (level == null || x == null || y == null) throw new McpEditorException("Give level, x and y.");
                        CheckLevelNumber(Session, level.Value);
                        if (x < 0 || x > 63 || y < 0 || y > 63) throw new ArgumentOutOfRangeException(null, "x and y are 0-63.");
                        int zone = set.ZoneAt(level.Value, x.Value, y.Value);
                        if (zone == ZoneSet.Solid) return "Level " + level + " (" + x + ", " + y + ") is rock in the zoning solved." + stale;
                        if (zone == ZoneSet.Unassigned) zone = set.NearestZone(level.Value, x.Value, y.Value);
                        if (zone <= ZoneSet.Unassigned) return "Level " + level + " (" + x + ", " + y + ") is in no zone of the zoning solved." + stale;
                        int sp;
                        return "Level " + level + " (" + x + ", " + y + ") is in zone " + RzZoneName(set, zone) + ": " + (sv.ZoneSphere.TryGetValue(zone, out sp) ? "reached in sphere " + sp + "." : "never reached.") + stale;
                    }
                case "object":
                    {
                        if (level == null || index == null) throw new McpEditorException("Give level and index.");
                        var m = ReadModel(level.Value);
                        if (index < 1 || index >= m.Objects.Length || m.Objects[index.Value].Free) throw new McpEditorException("Level " + level + " has no object #" + index + ".");
                        var o = m.Objects[index.Value];
                        int zone = set.ZoneOf(level.Value, m, o);
                        int sp;
                        string where = string.Format("#{0} (item 0x{1:X3}) on level {2} ({3}, {4})", index, o.Item, level, o.TileX, o.TileY);
                        if (zone <= ZoneSet.Unassigned) return where + " is in no zone of the zoning solved." + stale;
                        return where + " is in zone " + RzZoneName(set, zone) + ": " + (sv.ZoneSphere.TryGetValue(zone, out sp) ? "reached in sphere " + sp + "." : "never reached.") + stale;
                    }
                case "sources":
                    b.Append(res.Abilities.Sources.Count).Append(" ability source(s)").Append(stale).Append(":\n");
                    foreach (var a in res.Abilities.Sources.Take(400))
                    {
                        int sp; bool z = sv.ZoneSphere.TryGetValue(a.Zone, out sp);
                        int tk; bool t = sv.TokenSphere.TryGetValue(a.Token, out tk);
                        b.AppendFormat("{0}: {1} ({2}{3}) level {4} #{5} ({6}, {7}){8}, zone {9}: {10}{11}\n", a.Ability, a.Name, a.Kind, a.Permanent ? ", permanent" : a.Worn ? ", worn" : "",
                            a.Level, a.Index, a.TileX, a.TileY, string.IsNullOrEmpty(a.Holder) ? "" : " in " + a.Holder, RzZoneName(set, a.Zone),
                            z ? "zone reached in sphere " + sp : "zone never reached", t ? ", " + a.Token + " held from sphere " + tk : "");
                    }
                    return b.ToString();
                case "blocked":
                    b.Append(sv.Blocked.Count).Append(" way(s) found shut, ").Append(sv.Traps.Count).Append(" one-way trap(s)").Append(stale).Append(".\n");
                    foreach (var k in sv.Blocked.Take(300))
                        b.Append("shut: zone ").Append(RzZoneName(set, k.FromZone)).Append(" -> ").Append(RzZoneName(set, k.ToZone)).Append(k.Link != null ? " (" + k.Link.Kind + ")" : "")
                         .Append(k.Missing.Count > 0 ? ", missing " + string.Join(", ", k.Missing) : "").Append('\n');
                    foreach (var tr in sv.Traps.Take(100)) b.Append("one-way trap: ").Append(tr).Append(tr.Strands.Count > 0 ? ", strands " + string.Join(", ", tr.Strands) : "").Append('\n');
                    foreach (var w in sv.Warnings.Take(50)) b.Append("warning: ").Append(w).Append('\n');
                    return b.ToString();
            }
            throw new McpEditorException("what is spheres, unreached, square, object, sources or blocked.");
        }
    }
}
