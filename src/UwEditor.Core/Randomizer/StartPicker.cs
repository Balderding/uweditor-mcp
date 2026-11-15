using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>Where a random start may be drawn: UW1 level 1 only or any level; UW2 the castle (slots 1 to 5) or any of the nine worlds.</summary>
    public enum StartScope { FirstLevels, AnyLevel }

    /// <summary>What a check of a starting square found: viable, or why not, in words.</summary>
    public sealed class StartCheck
    {
        public int Level, X, Y;
        /// <summary>The zone the square lies in (-1: none).</summary>
        public int Zone = -1;
        public bool Viable;
        /// <summary>Why it is not viable ("lava", "a move trigger …", "the solve from here misses …", "cut off …"), or what was found when it is.</summary>
        public string Reason = "";
        /// <summary>The zone filter used the unshuffled game's solve (UW2, where the key shuffle does not run yet), not the key shuffle's assumed one.</summary>
        public bool StandIn;
        /// <summary>Hostile creatures on the square or the eight around it (reported, not a reason).</summary>
        public int HostilesNear;
        /// <summary>The abilities whose object the key item shuffle makes for this start, as a new character cannot cast them from runes (StartAbilities); empty for none.</summary>
        public List<string> Made = new List<string>();
        public StartPoint Point(string description = null)
        {
            return new StartPoint { Level = Level, X = X + 0.5, Y = Y + 0.5, Z = double.NaN, Description = description ?? string.Format("random start, level {0} ({1}, {2})", Level, X, Y) };
        }
        public override string ToString() { return string.Format("L{0} ({1}, {2}) zone {3}: {4}{5}", Level, X, Y, Zone, Viable ? "viable" : "not viable", Reason.Length > 0 ? " (" + Reason + ")" : ""); }
    }

    /// <summary>
    /// Random starting positions: the squares a player may begin on, and whether the game is
    /// still finishable from each.
    /// <para>A square is a candidate when it is open (not rock), on a zoned level (UW2: not a slot
    /// past the nine worlds), 1 to 62 across and up (a teleport takes x 1-62: uw1 do_teleport
    /// ignores 0, 0x3F skips the search for a free square), its floor neither water nor lava
    /// (TERRAIN.DAT's class, as Anywhere: lava burns a point one time in five, motion/PLAYMOVE.C;
    /// deep water drowns, game/PLAYTIME.C) and in UW2 not an ice slope (the player slides down at
    /// once: motion/PHYSICS.C set_player_phys_params), nothing on it a door, a trap or a trigger
    /// (a move trigger there goes off at the first step and its chain may teleport, hurt or shut
    /// the player in; UW2's rune traps go off at a touch; the rest are kept off for safety, as Anywhere
    /// does), not UW1's level 5 maze, not on or beside a barrier, and in a region of its zone that is
    /// not a door, a lava patch or an empty dead end folded into its neighbour's zone (a pit there
    /// may have no way out). UW1's level 9 is left out: reaching it is the goal; so are the game's own
    /// start and the squares round it, where the start teleport's triggers go (StartTeleport).</para>
    /// <para>The zone filter: a start is viable when a timeline solve from its zone, with the given
    /// tokens held (the key item shuffle's "every moving item held", the same assumed solve as its
    /// check), reaches the goal with no cut-off (a zone cut off that the solve gets to again later,
    /// TimelineSolve.CutOffRegained, does not count). Every square of an automatic zone outside its
    /// folded dead ends and doors reaches every other with nothing (a zone is a strongly connected
    /// group of regions over free ways: AutoZones; <see cref="ZonesNotStronglyConnected"/> checks it),
    /// so the solve is made once a zone and cached. The solve moves the start of the one build
    /// (the zone set's start zone, and the placements the build puts there to mean "anywhere": the
    /// spells, the crafts, the UW2 state); a full build from the square differs only in that the
    /// game's own start region may then fold, so the key shuffle's final check builds afresh.</para>
    /// <para>A start whose free area a new character cannot leave with his runes (a spell above
    /// AbilityOptions.NewCharacterCircle, say Gate Travel out of a sealed room) is viable when the
    /// solve reaches the goal with the scroll or wand the key shuffle then makes (StartAbilities;
    /// StartCheck.Made names them). Not for UW2's stand-in, as nothing makes them there.</para>
    /// <para>The draw: a zone with weight the square root of its candidate squares (uniform over
    /// squares would put most starts in the few great halls, uniform over zones as many in one-square
    /// ledges as in towns), then a square of it at random; a zone the filter rejects is struck off and
    /// the draw made again (lazy: only the zones drawn are solved).</para>
    /// </summary>
    public sealed class StartPicker
    {
        public readonly GameSession Session;
        public readonly TimelineResult T;
        readonly Func<int, LevelModel> level;
        /// <summary>The tokens held in the zone solves.</summary>
        public readonly List<string> Assumed;
        public readonly AbilityOptions Logic;
        /// <summary>The solves are the unshuffled game's (no tokens assumed): UW2's stand-in.</summary>
        public readonly bool StandIn;
        readonly TerrainTable terrain;
        readonly Dictionary<int, StartCheck> verdicts = new Dictionary<int, StartCheck>();
        readonly Dictionary<int, LevelModel> models = new Dictionary<int, LevelModel>();
        readonly Dictionary<StartScope, List<StartCheck>> candidates = new Dictionary<StartScope, List<StartCheck>>();
        /// <summary>Time spent solving zones so far.</summary>
        public TimeSpan FilterTime { get; private set; }
        public int ZonesSolved { get { return verdicts.Count; } }

        public StartPicker(GameSession s, Func<int, LevelModel> level, TimelineResult t, AbilityOptions logic, IEnumerable<string> assumed, bool standIn)
        {
            Session = s; this.level = level; T = t; Logic = logic ?? new AbilityOptions(); StandIn = standIn;
            Assumed = (assumed ?? Enumerable.Empty<string>()).ToList();
            terrain = CreatureTerrain.TryLoad(s);
        }

        /// <summary>
        /// A picker for a game as it is: the zones built with these options (the game's own start),
        /// and for UW1 the key item shuffle's assumed tokens (every item that moves held); for UW2,
        /// where the key shuffle does not run yet, the unshuffled game's solve stands in.
        /// </summary>
        public static StartPicker For(GameSession s, Func<int, LevelModel> level, KeyShuffleOptions opt = null, FactSet facts = null)
        {
            opt = (opt ?? new KeyShuffleOptions()).Clone();
            opt.Nav.StartOverride = null;
            var t = Timeline.Build(s, level, opt.Nav, opt.Logic, null, facts, false, opt.Zoning);
            var assumed = s.IsUw2 ? new List<string>() : KeyShuffle.MovingTokens(s, level, t, opt);
            return new StartPicker(s, level, t, opt.Logic, assumed, s.IsUw2);
        }

        /// <summary>Checks one square of a game as it is (builds the zones once: seconds).</summary>
        public static StartCheck Check(GameSession s, Func<int, LevelModel> level, KeyShuffleOptions opt, StartPoint start)
        {
            return For(s, level, opt).Check(start.Level, (int)start.X, (int)start.Y);
        }

        /// <summary>The levels of a scope: UW1 level 1 or all nine; UW2 the castle's slots 1 to 5 or the nine worlds' levels; only levels present.</summary>
        public static List<int> LevelsIn(GameSession s, StartScope scope)
        {
            return s.LevelSlots().Where(x => x.Present && Progression.InWalk(s, x.Level) && (scope == StartScope.AnyLevel || (s.IsUw2 ? x.Level <= 5 : x.Level == 1))).Select(x => x.Level).ToList();
        }

        LevelModel Model(int l)
        {
            LevelModel m;
            if (!models.TryGetValue(l, out m)) { try { m = level(l); } catch (Exception) { m = null; } models[l] = m; }
            return m;
        }

        NavGraph Graph { get { return T.Abilities.Auto.Graph; } }

        /// <summary>The goal's level, left out for UW1 (reaching level 9 is the goal); none for UW2 (the goal is a ceremony on the castle's level).</summary>
        int GoalLevel { get { return Session.IsUw2 ? -1 : T.Set.LevelsOf(T.GoalZone).DefaultIfEmpty(-1).First(); } }

        /// <summary>Why a square cannot be a start, by the square rules alone (null: it can).</summary>
        public string SquareProblem(int l, int x, int y)
        {
            var s = Session;
            if (l < 1 || l > T.Set.Levels) return "no such level";
            if (!Progression.InWalk(s, l)) return "a slot past the nine worlds";
            var m = Model(l);
            if (m == null || !m.Present) return "the level is not there";
            if (x < 1 || y < 1 || x > 62 || y > 62) return "on the map's edge (a teleport takes x 1-62)";
            var t = m.Tiles[x, y];
            if (t == null || t.IsSolid) return "solid rock";
            int cls = CreatureTerrain.FloorClass(s, terrain, m, x, y);
            if (cls == CreatureTerrain.Water) return "water";
            if (cls == CreatureTerrain.Lava) return "lava";
            if (s.IsUw2 && cls == CreatureTerrain.Ice && t.Type >= TileType.SlopeUpN) return "an ice slope (the player slides down at once)";
            foreach (var o in m.TileObjects(x, y))
            {
                if (o.Item >= 0x140 && o.Item < 0x150) return "a door's square";
                if (Triggers.IsTrigger(o.Item)) return (o.Item == 0x1A0 || (s.IsUw2 && o.Item == 0x1B0) ? "a move trigger" : "a trigger") + " (#" + o.Index + " " + o.Name + ": it may go off at the first step)";
                if (o.IsTrapOrTrigger) return (Triggers.IsRuneTrap(s.Kind, o.Item) ? "a rune trap" : "a trap") + " (#" + o.Index + " " + o.Name + ")";
            }
            if (MazeRocks.InMaze(s, l, x, y)) return "the level 5 maze";
            if (Graph.NearBarrier(l, x, y)) return "on or beside a force field or a grating";
            if (l == GoalLevel) return "the goal's level";
            var own = (T.Abilities.Nav ?? new NavOptions()).BaseStartOf(s);
            if (own != null && own.Level == l && Math.Abs((int)own.X - x) <= 1 && Math.Abs((int)own.Y - y) <= 1) return "the game's own start or beside it (where the start teleport's triggers go)";
            int z = T.Set.ZoneAt(l, x, y);
            if (z <= ZoneSet.Unassigned) return "in no zone";
            int r = Graph.RegionOf(l, x, y);
            if (r < 0) return "in no region";
            var reg = Graph.Regions[r];
            if (reg.IsDoor || reg.KeepApart) return "a door's square";
            if (reg.Lava) return "lava";
            if (reg.Folded) return "an empty pit or ledge folded into its neighbour's zone (it may have no way out its zone has)";
            if (reg.IsBarrier) return "a barrier";
            return null;
        }

        /// <summary>Hostile creatures (attitude 0) on the square or the eight around it.</summary>
        public int HostilesNear(int l, int x, int y)
        {
            var m = Model(l);
            if (m == null) return 0;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int a = x + dx, b = y + dy;
                    if (a < 0 || b < 0 || a > 63 || b > 63) continue;
                    foreach (var o in m.TileObjects(a, b))
                        if (o.IsCritter && o.Record.Length > 0x0E && (UltimateEditor.Uw1.Uw1Schemas.MobileObject["Attitude"].Get(o.Record) & 3) == 0) n++;
                }
            return n;
        }

        /// <summary>Every candidate square of the scope (the square rules hold), with its zone, level by level, row by row.</summary>
        public List<StartCheck> Candidates(StartScope scope)
        {
            List<StartCheck> list;
            if (candidates.TryGetValue(scope, out list)) return list;
            candidates[scope] = list = new List<StartCheck>();
            foreach (int l in LevelsIn(Session, scope))
                for (int y = 1; y < 63; y++)
                    for (int x = 1; x < 63; x++)
                        if (SquareProblem(l, x, y) == null) list.Add(new StartCheck { Level = l, X = x, Y = y, Zone = T.Set.ZoneAt(l, x, y), Viable = true });
            return list;
        }

        /// <summary>The placements the build puts in the start zone to mean "anywhere" (the spells, the crafts, the facts' free flags with no place, UW2's state): they follow the start.</summary>
        List<Placement> StartPlacements()
        {
            int start = T.Set.StartZone;
            return T.Set.Placements.Where(p => p.Zone == start && Timeline.Everywhere(p, T.Facts)).ToList();
        }

        /// <summary>The solve from a zone (cached): viable, or why not.</summary>
        public StartCheck CheckZone(int zone)
        {
            StartCheck v;
            if (verdicts.TryGetValue(zone, out v)) return v;
            var sw = Stopwatch.StartNew();
            int keep = T.Set.StartZone;
            var moved = StartPlacements();
            try
            {
                foreach (var p in moved) p.Zone = zone;
                T.Set.StartZone = zone;
                var sv = Timeline.Solve(T, Logic, "start in zone " + zone, Assumed);
                v = new StartCheck { Zone = zone, StandIn = StandIn };
                // a way out a new character cannot cast from runes: the key shuffle makes its scroll or wand within reach (UW1's shuffle only)
                var made = sv.GoalReached || StandIn ? new List<string>() : StartAbilities.Needed(Session, T, Logic, Assumed, sv);
                if (made.Count > 0)
                {
                    v.Made = made;
                    sv = Timeline.Solve(T, Logic, "start in zone " + zone + ", objects made for " + string.Join(", ", made), Assumed.Concat(made.Select(a => StartAbilities.TokenFor(Logic, a))));
                }
                // a zone cut off that the solve gets to again later is not lost (the way back ran through zones not reached yet when the change went off)
                var cut = sv.CutOff.Select(c => new KeyValuePair<int, List<int>>(c.Key, c.Value.Where(z => !sv.CutOffRegained.Contains(z)).ToList())).Where(c => c.Value.Count > 0).ToList();
                if (!sv.GoalReached) v.Reason = "the solve from here misses the goal (missing " + string.Join(", ", sv.GoalMissing.Distinct().Take(6)) + "; " + Timeline.BaseZonesReached(sv) + " zones reached)";
                else if (cut.Count > 0) v.Reason = "cut off: " + string.Join("; ", cut.Select(c => "sphere " + c.Key + ", " + c.Value.Count + " zones (" + string.Join(", ", c.Value.Take(4).Select(z => SolveResult.ZoneName(T.Set, z))) + (c.Value.Count > 4 ? " …" : "") + ")"));
                else { v.Viable = true; v.Reason = "reaches the goal (" + Timeline.BaseZonesReached(sv) + " zones)" + (StandIn ? ", by the unshuffled game's solve (the key shuffle does not run on UW2 yet)" : ", with every moving item held") + (made.Count > 0 ? " and an object made for " + string.Join(", ", made.Select(a => a.Replace('_', ' '))) + " (a new character cannot cast it from runes)" : ""); }
            }
            finally
            {
                foreach (var p in moved) p.Zone = keep;
                T.Set.StartZone = keep;
                FilterTime += sw.Elapsed;
            }
            verdicts[zone] = v;
            return v;
        }

        /// <summary>Checks a square: the square rules, then its zone's solve.</summary>
        public StartCheck Check(int l, int x, int y)
        {
            var c = new StartCheck { Level = l, X = x, Y = y, StandIn = StandIn };
            string why = SquareProblem(l, x, y);
            if (l >= 1 && l <= T.Set.Levels && x >= 0 && y >= 0 && x < 64 && y < 64) c.Zone = T.Set.ZoneAt(l, x, y);
            if (why != null) { c.Reason = why; return c; }
            c.HostilesNear = HostilesNear(l, x, y);
            var v = CheckZone(c.Zone);
            c.Viable = v.Viable;
            c.Reason = v.Reason;
            c.Made = v.Made;
            return c;
        }

        /// <summary>
        /// A viable start drawn from the seed (the same seed, levels and options give the same
        /// square): a zone of the scope by the square root of its candidate squares, then one of its
        /// squares; a zone whose solve fails is struck off and the draw made again. Not viable, with
        /// the reason, when no zone of the scope is.
        /// </summary>
        public StartCheck Pick(int seed, StartScope scope)
        {
            var byZone = Candidates(scope).GroupBy(c => c.Zone).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.ToList());
            var rng = new Random(unchecked(seed * 7919 + 0x51A7));
            var left = byZone.Keys.ToList();
            int rejected = 0;
            while (left.Count > 0)
            {
                double total = left.Sum(z => Math.Sqrt(byZone[z].Count)), at = rng.NextDouble() * total;
                int pick = left[left.Count - 1];
                foreach (int z in left) { at -= Math.Sqrt(byZone[z].Count); if (at < 0) { pick = z; break; } }
                var v = CheckZone(pick);
                if (!v.Viable) { left.Remove(pick); rejected++; continue; }
                var sq = byZone[pick][rng.Next(byZone[pick].Count)];
                var c = new StartCheck { Level = sq.Level, X = sq.X, Y = sq.Y, Zone = pick, Viable = true, StandIn = StandIn, HostilesNear = HostilesNear(sq.Level, sq.X, sq.Y), Made = v.Made };
                c.Reason = v.Reason + (rejected > 0 ? "; " + rejected + " zone" + (rejected == 1 ? "" : "s") + " drawn before were rejected" : "");
                return c;
            }
            return new StartCheck { Level = -1, X = -1, Y = -1, StandIn = StandIn, Reason = byZone.Count == 0 ? "no candidate square in the scope" : "no zone of the scope reaches the goal (" + byZone.Count + " tried)" };
        }

        /// <summary>Every zone of the scope solved (for a report or a test): the verdict of each zone with candidates.</summary>
        public Dictionary<int, StartCheck> Sweep(StartScope scope)
        {
            var d = new Dictionary<int, StartCheck>();
            foreach (int z in Candidates(scope).Select(c => c.Zone).Distinct().OrderBy(z => z)) d[z] = CheckZone(z);
            return d;
        }

        /// <summary>
        /// The zones whose candidate squares do not all reach one another over free ways inside the
        /// zone (the regions of each zone that hold candidates, joined by the free edges between
        /// them that are not a level change; a region is walked through freely by definition). Empty
        /// for automatic zones; a painted zoning may hold parts with no free way between them.
        /// </summary>
        public List<int> ZonesNotStronglyConnected(StartScope scope)
        {
            var g = Graph;
            var bad = new List<int>();
            var regionsOf = new Dictionary<int, HashSet<int>>();
            foreach (var c in Candidates(scope))
            {
                HashSet<int> h;
                if (!regionsOf.TryGetValue(c.Zone, out h)) regionsOf[c.Zone] = h = new HashSet<int>();
                h.Add(g.RegionOf(c.Level, c.X, c.Y));
            }
            // the free ways between regions of one zone (through any region of the zone, candidates or not)
            var adj = new Dictionary<int, List<int>>();
            foreach (var e in g.Edges)
            {
                if (e.Requires != "free" || e.Kind == "stairs" || e.From == e.To) continue;
                if (g.Regions[e.From].Zone != g.Regions[e.To].Zone) continue;
                List<int> l;
                if (!adj.TryGetValue(e.From, out l)) adj[e.From] = l = new List<int>();
                l.Add(e.To);
            }
            Func<int, HashSet<int>> reach = r0 =>
            {
                var seen = new HashSet<int> { r0 };
                var q = new Queue<int>(seen);
                while (q.Count > 0) { List<int> l; if (adj.TryGetValue(q.Dequeue(), out l)) foreach (int n in l) if (seen.Add(n)) q.Enqueue(n); }
                return seen;
            };
            foreach (var kv in regionsOf)
            {
                if (kv.Value.Count < 2) continue;
                var list = kv.Value.ToList();
                if (list.Any(a => !reach(a).IsSupersetOf(kv.Value))) bad.Add(kv.Key);
            }
            return bad;
        }
    }
}
