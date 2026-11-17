using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A world change (a mechanism of the levels), what it does to the ways, and how the timeline treats it.</summary>
    public sealed class WorldChange
    {
        public NavMechanism Mechanism;
        public string Flag { get { return Requirement.Normalize(Mechanism.Flag); } }
        public int Level { get { return Mechanism.Level; } }
        /// <summary>What the change does to the ways of its level, found by applying it alone: joins, new ways, ways gone, zones split, squares turned to rock.</summary>
        public readonly List<string> Opens = new List<string>(), Closes = new List<string>();
        /// <summary>opens, closes, opens and closes, no way changed, not applied (variables, hack traps, creatures).</summary>
        public string Effect = "";
        /// <summary>How the timeline uses it.</summary>
        public string Treatment = "";
        /// <summary>Applied to its level when it goes off (else only its flag is granted).</summary>
        public bool Applied;
        public override string ToString() { return Mechanism.Flag + ": " + Effect; }
    }

    /// <summary>One step of the timeline: the zones reached, the tokens gained, the changes set off and applied.</summary>
    public sealed class TimelineSphere
    {
        public int Index;
        public readonly List<int> Zones = new List<int>();
        public readonly List<string> Tokens = new List<string>();
        public readonly List<string> TokenSources = new List<string>();
        /// <summary>Mechanism flags gained (set off), and those applied to their level.</summary>
        public readonly List<string> SetOff = new List<string>(), Applied = new List<string>();
        /// <summary>The uses left of each single-use source (once:X) after this sphere.</summary>
        public readonly Dictionary<string, int> OnceLeft = new Dictionary<string, int>();
        /// <summary>The state of each changed level after this sphere (level -> state key).</summary>
        public Dictionary<int, string> State = new Dictionary<int, string>();
        /// <summary>The links of the world as it stood after this sphere (the walkthrough finds the way from one step to the next over them).</summary>
        public List<ZoneLink> Links;
    }

    /// <summary>A zone the player can enter but not leave (back to the start), with what it holds.</summary>
    public sealed class OneWayTrap
    {
        public int Zone, Sphere;
        /// <summary>The zones reachable from it without getting back (the trap's inside).</summary>
        public readonly List<int> Inside = new List<int>();
        /// <summary>Tokens gained only inside it.</summary>
        public readonly List<string> Holds = new List<string>();
        /// <summary>Tokens gained only inside it that a later step uses: they strand the player.</summary>
        public readonly List<string> Strands = new List<string>();
        /// <summary>The link that led in (one way).</summary>
        public ZoneLink Entry;
        public override string ToString() { return "zone " + Zone + " (sphere " + Sphere + ")"; }
    }

    /// <summary>A timeline solve: spheres, when each zone and token came, the changes applied, the goal, the return check.</summary>
    public sealed class TimelineSolve
    {
        public string Label = "";
        public readonly List<TimelineSphere> Spheres = new List<TimelineSphere>();
        public readonly Dictionary<int, int> ZoneSphere = new Dictionary<int, int>();
        public readonly Dictionary<string, int> TokenSphere = new Dictionary<string, int>();
        public readonly Dictionary<int, ZoneLink> FirstLink = new Dictionary<int, ZoneLink>();
        public readonly List<string> Fired = new List<string>(), Applied = new List<string>();
        public bool GoalZoneReached, GoalReached;
        /// <summary>What the goal was reached by: "the goal zone", or an end-of-game trigger ("the end of the game at L9 (52, 51)").</summary>
        public string GoalBy = "";
        public readonly List<string> GoalMissing = new List<string>();
        public readonly List<BlockedLink> Blocked = new List<BlockedLink>();
        public readonly List<OneWayTrap> Traps = new List<OneWayTrap>();
        /// <summary>Zones the return check found safe thanks to the planted silver sapling.</summary>
        public int SavedBySapling;
        /// <summary>The warps that ask for nothing held and whose option is on (UW2: dying outside Britannia), each with the zone it leads to: ways the walkthrough may take between steps.</summary>
        public readonly List<KeyValuePair<Fact, int>> FreeWarps = new List<KeyValuePair<Fact, int>>();
        /// <summary>From a start other than the game's own: the zones the player comes out into from it, which the return check counted as back as the start (Engine.ReRoot); empty when it went back to the start alone.</summary>
        public readonly List<int> ReturnTo = new List<int>();
        /// <summary>The solve again with nothing taken from inside a trap (or -1 when not run).</summary>
        public int SafeZones = -1;
        public bool SafeGoal;
        /// <summary>That solve itself (null when not run): a walkthrough over it never enters a one-way trap (KeyShuffle's walkthrough falls back on it).</summary>
        public TimelineSolve Safe;
        public readonly List<string> Warnings = new List<string>();
        /// <summary>The single-use sources spent (once:X), one entry per use, in order.</summary>
        public readonly List<string> OnceSpent = new List<string>();
        /// <summary>
        /// A new character's rune casting (SolverOptions.NewCharacterCircle): the start's free area
        /// (the start zone and the zones it reaches by ways that need nothing, in the world as it
        /// starts), or null when every circle counts from the start.
        /// </summary>
        public HashSet<int> FreeArea;
        /// <summary>The sphere in which the solve first reached a zone outside FreeArea (from then on every circle counts), or -1: never (or no limit).</summary>
        public int FreeAreaLeft = -1;
        /// <summary>Zones reached before that a change cut the player off from: the sphere of the change and the zones (some may be reached again later).</summary>
        public readonly List<KeyValuePair<int, List<int>>> CutOff = new List<KeyValuePair<int, List<int>>>();
        /// <summary>
        /// The zones of CutOff that the solve got to again afterwards (the reach at its end holds
        /// them): the player lost them only for a while, as when the way back runs through zones the
        /// solve had not reached yet when the change went off (the search after a change looks only
        /// among the zones reached so far).
        /// </summary>
        public readonly HashSet<int> CutOffRegained = new HashSet<int>();
        /// <summary>Names of the zones made by changes (parts split off), by id.</summary>
        public readonly Dictionary<int, string> NewZones = new Dictionary<int, string>();
        /// <summary>What the solve found, in order (Walkthrough reads it).</summary>
        public readonly List<TimelineEvent> Events = new List<TimelineEvent>();
        /// <summary>The solver options the solve ran with.</summary>
        public SolverOptions Options;
        /// <summary>The zone the goal was reached in (the goal zone, or the zone of the end of the game), or 0.</summary>
        public int GoalAt;
        /// <summary>
        /// UW2, a start in a world the blackrock gem keeps shut (Engine.Run): each shut world the
        /// solve stood in and left, in order (the start's first). Empty when the start is not in one.
        /// The solve takes first all that can be had without leaving the world, then leaves it (for
        /// the castle when it can, else for the world a way out leads to, which is then taken the same
        /// way when it is shut too) and comes back only by a way that holds (the gem's facet once the
        /// plot or the world's fused key gem opens it).
        /// </summary>
        public readonly List<WorldLeave> Leaves = new List<WorldLeave>();
        /// <summary>The levels of the start's shut world, or null when the start is not in one.</summary>
        public HashSet<int> SealedLevels { get { return Leaves.Count > 0 ? Leaves[0].Levels : sealedStart; } }
        internal HashSet<int> sealedStart;
        /// <summary>The sphere in which the solve left the start's shut world (-1: it did not).</summary>
        public int SealedLeft { get { return Leaves.Count > 0 ? Leaves[0].Sphere : -1; } }
    }

    /// <summary>A shut world of UW2 the timeline solve left (TimelineSolve.Leaves).</summary>
    public sealed class WorldLeave
    {
        /// <summary>The world's levels.</summary>
        public HashSet<int> Levels;
        /// <summary>The sphere of the leaving and its zone event (an index into TimelineSolve.Events).</summary>
        public int Sphere, Event;
        /// <summary>The zone the player came out in.</summary>
        public int To;
        /// <summary>What opens the world again, in words ("the castle plot at 8 or this world's key gem (7) fused").</summary>
        public string Opens = "";
    }

    /// <summary>
    /// One thing a timeline solve found, in the order it found it: a zone first entered (by a
    /// link, by a warp, or stood in when a change went off), a token gained from a placement, a
    /// mechanism set off, a part of a count (Shak's coins), or another use of a single-use
    /// source. Whatever an event needed came before it in the list.
    /// </summary>
    public sealed class TimelineEvent
    {
        public int Sphere;
        /// <summary>zone, token, flag, count, use.</summary>
        public string Kind = "";
        /// <summary>The zone entered, or the zone the placement lay in then (0 for a mechanism: its trigger is on the mechanism's level).</summary>
        public int Zone;
        /// <summary>A zone event: the zone it was entered from (-1: by a warp, or where the player stood).</summary>
        public int From = -1;
        public ZoneLink Link;
        public Fact Warp;
        public string Token = "";
        public Placement Placement;
        public NavMechanism Mechanism;
        /// <summary>The requirement that held: the link's, the placement's, the warp's, or the mechanism's set-off in zone terms.</summary>
        public string Requires = "";
        /// <summary>The single-use sources (once:X) this step used up, one entry a use.</summary>
        public readonly List<string> Spent = new List<string>();
        /// <summary>A zone event: the changes applied to the link's level by then, in order (the link may exist only because of some of them: Timeline.ChangesFor).</summary>
        public readonly List<string> Changes = new List<string>();
        public override string ToString() { return Sphere + " " + Kind + " " + (Token.Length > 0 ? Token : Zone.ToString()); }
    }

    /// <summary>Phase C of the automatic zones: the zones of Phase B, the world changes and the facts' links, warps and goal, and the timeline solves.</summary>
    public sealed class TimelineResult
    {
        public AbilityZoneResult Abilities;
        public ZoneSet Set { get { return Abilities.Set; } }
        public FactSet Facts { get { return Abilities.Facts; } }
        public AbilityOptions Options { get { return Abilities.Options; } }
        public readonly List<WorldChange> Changes = new List<WorldChange>();
        public List<NavCloser> Closers { get { return Abilities.Auto.Graph.Closers; } }
        /// <summary>Links made by the facts (kind link), as they stand with the options.</summary>
        public readonly List<ZoneLink> FactLinks = new List<ZoneLink>();
        public readonly List<Fact> Warps = new List<Fact>();
        public int GoalZone;
        /// <summary>The squares that end the game (EndGame.Find) and their zones: reaching one is the goal too, as reaching the goal zone (level 9) is.</summary>
        public readonly List<KeyValuePair<EndTrigger, int>> Ends = new List<KeyValuePair<EndTrigger, int>>();
        public string GoalRequires = "free";
        public Fact GoalFact;
        /// <summary>Goal facts with an option (Phase D: goal_orb, option:require_orb): their requirement joins the goal's when the option is on.</summary>
        public readonly List<Fact> GoalExtras = new List<Fact>();

        /// <summary>What the goal needs with these solver options: the goal fact's requirement and those of the goal facts whose option is on.</summary>
        public string GoalFor(SolverOptions so)
        {
            var terms = new List<string> { GoalRequires };
            foreach (var f in GoalExtras) if (so.OptionsOn.Contains(f.Option)) terms.Add(f.Requires);
            return ZoneLink.JoinAnd(terms);
        }

        /// <summary>What a square that ends the game needs: the goal options' extras only (option:require_orb), not the goal fact's own requirement, which is level 9's.</summary>
        public string EndFor(SolverOptions so)
        {
            var terms = new List<string> { "free" };
            foreach (var f in GoalExtras) if (so.OptionsOn.Contains(f.Option)) terms.Add(f.Requires);
            return ZoneLink.JoinAnd(terms);
        }
        public TimelineSolve Solve;
        public readonly List<KeyValuePair<string, TimelineSolve>> Variants = new List<KeyValuePair<string, TimelineSolve>>();
        public readonly List<string> Warnings = new List<string>();
        internal Timeline.Engine Engine;
    }

    /// <summary>
    /// Phase C (C1) of the automatic zones: a solver that follows the world as it changes.
    /// When the player can set a mechanism off (its trigger's zone reached, its conditions met:
    /// an item held, a variable's value, a Search check), its flag is granted and, unless it only
    /// takes ways away, its change is applied to that level (terrain rewritten, objects deleted,
    /// doors opened); that level alone is then rebuilt and the search goes on. Doors that a trigger
    /// the player cannot avoid closes behind him are one way (NavGraph.Closers). At the end a
    /// return check looks for zones the player can enter but not leave.
    /// </summary>
    public static class Timeline
    {
        /// <summary>The first id of the zones a change splits off (above every zone of the zone set: a level's are level * 256 + number).</summary>
        public const int FreshZoneIds = 1000000;

        /// <summary>How many zones of the zone set itself a solve reached (the parts changes split off not counted).</summary>
        public static int BaseZonesReached(TimelineSolve sv) { return sv == null ? 0 : sv.ZoneSphere.Keys.Count(z => z < FreshZoneIds); }

        /// <summary>
        /// Builds Phase B's zones with the closers made one way, adds the facts' links, warps and goal, classifies the changes and solves.
        /// With a zoning, its zones are used instead of automatic ones (a region takes the zone that holds most of its tiles,
        /// also after a change), and its own links are added to those the levels give.
        /// </summary>
        public static TimelineResult Build(GameSession s, Func<int, LevelModel> level, NavOptions nav = null, AbilityOptions opt = null, ZoneSet painted = null, FactSet facts = null, bool variants = true, ZoneSet zoning = null)
        {
            nav = (nav ?? new NavOptions()).Clone();
            nav.ClosersOneWay = true;
            opt = opt ?? new AbilityOptions();
            var b = AbilityZones.Build(s, level, nav, opt, painted, facts, zoning);
            b.Set.Source = zoning != null ? "the zoning given, solved over time (Underworld Editor)" : "automatic zones (Underworld Editor), Phase C";
            var res = new TimelineResult { Abilities = b };
            res.Engine = new Engine(s, level, b, res);
            res.Engine.Prepare();
            Classify(res);
            res.Solve = Solve(res, opt, "with these options");
            if (variants) RunVariants(res);
            return res;
        }

        /// <summary>
        /// The automatic zones the solve over time makes with these options (doors closed behind
        /// the player kept apart), as a zoning of its own for the Zones window; the solve is not run.
        /// </summary>
        public static ZoneSet AutoZoning(GameSession s, Func<int, LevelModel> level, NavOptions nav = null, AbilityOptions opt = null, FactSet facts = null)
        {
            nav = (nav ?? new NavOptions()).Clone();
            nav.ClosersOneWay = true;
            var b = AbilityZones.Build(s, level, nav, opt ?? new AbilityOptions(), null, facts);
            b.Set.Source = "automatic zones (Underworld Editor), Phase C";
            return ZoneSet.FromJson(b.Set.ToJson(), s.Profile.LevelSlots);
        }

        /// <summary>The option tokens of the movement options (option:cut_levels ...).</summary>
        static void NavTokens(NavOptions n, HashSet<string> into)
        {
            if (n.CutAtLevelChanges) into.Add("cut_levels");
            if (n.GapJumps) into.Add("gap_jumps");
            if (n.DiagonalJumps) into.Add("diagonal_jumps");
        }

        /// <summary>The solver's options for these logic options and these zones (the movement options as tokens too), with tokens held from the start.</summary>
        public static SolverOptions SolverFor(TimelineResult res, AbilityOptions opt, IEnumerable<string> startTokens)
        {
            var so = opt.ToSolver();
            if (startTokens != null) so.StartTokens.AddRange(startTokens);
            NavTokens(res.Abilities.Nav, so.OptionsOn);
            return so;
        }

        /// <summary>
        /// A placement the build puts in the start zone to stand for something done anywhere, not
        /// a thing lying there: a spell cast (its runes held), a craft or a free flag of the facts
        /// with no place (AbilityZones, Source "facts" or "facts: id"), a state token of UW2
        /// (AutoZones, Source "state"). Only meaningful for placements of the start zone.
        /// </summary>
        public static bool Everywhere(Placement p, FactSet facts)
        {
            return Everywhere(p, id => facts == null ? null : facts.Facts.FirstOrDefault(x => x.Id == id));
        }

        static bool Everywhere(Placement p, Func<string, Fact> byId)
        {
            string src = p.Source ?? "";
            if (src == "state" || src == "facts") return true;
            if (!src.StartsWith("facts: ")) return false;
            var f = byId(src.Substring(7));
            return f != null && (f.Kind == "craft" || (f.Kind == "free" && f.TopSpot == null));
        }

        static readonly System.Text.RegularExpressions.Regex PlotSetToken = new System.Text.RegularExpressions.Regex(@"^plotset:(\d+)$");

        /// <summary>
        /// UW2: whether a solve can set the castle plot out of the game's order. Each of the plot's set
        /// traps (levels 3, 9 and 14: 1, 2, 3) writes its value whatever the plot stands at
        /// (event/TRIGGER.C set_numbered_variable) and its triggers are used once (they lack flag 10),
        /// so the plot ends at the value of the last to go off. The logic counts the highest set only
        /// once every lower one is held (StateRules), but a trigger goes off when the player walks onto
        /// it: when the solve first reaches the highest set's trigger (its flag) in a sphere before the
        /// one in which a lower set is held, the player may set the plot to 3 and later see a lower trap
        /// set it back, with level 14's trigger gone (from the gem room, the Prison Tower before the
        /// castle's sewers). Then a phrase saying so; null when the order holds (always from the game's
        /// own start, where the way to the gem runs through the sewers), and for UW1. Reached in the
        /// same sphere counts as in order: the walkthrough takes the lower one first.
        /// </summary>
        public static string PlotOrderProblem(TimelineResult res, TimelineSolve sv)
        {
            if (res == null || sv == null || res.Set == null) return null;
            var sets = new List<KeyValuePair<int, Placement>>();
            foreach (var p in res.Set.Placements)
            {
                if (p.Source != "state") continue;
                var mm = PlotSetToken.Match(p.Grants ?? "");
                if (mm.Success) sets.Add(new KeyValuePair<int, Placement>(int.Parse(mm.Groups[1].Value), p));
            }
            if (sets.Count < 2) return null;
            var top = sets.OrderBy(x => x.Key).Last();
            var r = Requirement.TryParse(top.Value.Requires ?? "", out _);
            if (r == null) return null;
            var toks = new List<string>();
            r.Tokens(toks);
            int first = int.MaxValue;
            string which = null;
            foreach (var tok in toks.Select(Requirement.Normalize).Where(x => x.StartsWith("flag:")))
            {
                int sp;
                if (sv.TokenSphere.TryGetValue(tok, out sp) && sp < first) { first = sp; which = tok; }
            }
            if (first == int.MaxValue) return null;
            foreach (var low in sets.Where(x => x.Key < top.Key).OrderBy(x => x.Key))
            {
                int below;
                if (!sv.TokenSphere.TryGetValue("plotset:" + low.Key, out below)) below = int.MaxValue;
                if (first < below)
                    return string.Format("the castle plot's trap that sets it to {0} ({1}) can be set off in sphere {2}, {3}: the plot would end at {4} (a set trap writes its value whatever the plot stands at, and its triggers are used once)",
                        top.Key, which, first, below == int.MaxValue ? "and the one that sets it to " + low.Key + " never is" : "before the one that sets it to " + low.Key + " (sphere " + below + ")", low.Key);
            }
            return null;
        }

        /// <summary>One timeline solve with the given logic options (the zones and the facts as built).</summary>
        public static TimelineSolve Solve(TimelineResult res, AbilityOptions opt, string label, IEnumerable<string> startTokens = null)
        {
            var so = SolverFor(res, opt, startTokens);
            var sv = res.Engine.Run(so, opt, label, null);
            res.Engine.ReturnCheck(sv, so);
            if (sv.Traps.Count > 0)
            {
                // again, taking nothing from inside a trap and working no lever there. A trap that leads on
                // to the goal is a point of no return, not a trap to stay out of: what it holds is used on the way.
                var goals = new HashSet<int> { res.GoalZone };
                foreach (var e in res.Ends) goals.Add(e.Value);
                Func<OneWayTrap, HashSet<int>> zonesOf = t => { var h = new HashSet<int>(t.Inside) { t.Zone }; h.ExceptWith(goals); return h; };
                var avoid = sv.Traps.Where(t => !goals.Contains(t.Zone) && !t.Inside.Any(goals.Contains)).ToList();
                var banned = new HashSet<int>(avoid.SelectMany(zonesOf));
                var safe = res.Engine.Run(so, opt, label + " (safe)", banned);
                sv.SafeZones = BaseZonesReached(safe);
                sv.SafeGoal = safe.GoalReached;
                sv.Safe = safe;
                safe.Traps.AddRange(sv.Traps);   // where the walkthrough of it must not linger (Walkthrough's order)
                if (sv.GoalReached && !safe.GoalReached)
                {
                    // the goal needs something from inside a trap: which traps, each tried alone
                    bool found = false;
                    foreach (var t in avoid)
                    {
                        if (res.Engine.Run(so, opt, label + " (without " + t + ")", zonesOf(t)).GoalReached) continue;
                        t.Strands.AddRange(t.Holds.Count > 0 ? t.Holds : new List<string> { "a way or a lever inside" });
                        found = true;
                    }
                    // no single one, but several together
                    if (!found) foreach (var t in avoid) t.Strands.AddRange(t.Holds.Count > 0 ? t.Holds : new List<string> { "a way or a lever inside (with the other traps)" });
                }
            }
            return sv;
        }

        static void RunVariants(TimelineResult res)
        {
            res.Variants.Clear();
            foreach (bool magic in new[] { true, false })
                foreach (bool once in new[] { false, true })
                    foreach (bool pick in new[] { false, true })
                    {
                        var o = res.Options.Clone();
                        o.Magic = magic; o.SingleUseItems = once; o.LockPicking = pick;
                        if (pick) { o.PickSkill = 30; o.PickRetry = true; }
                        string label = string.Format("Magic {0}, single-use {1}, picking {2}", magic ? "on" : "off", once ? "on" : "off", pick ? "on" : "off");
                        res.Variants.Add(new KeyValuePair<string, TimelineSolve>(label, Solve(res, o, label)));
                    }
            var k = res.Options.Clone();
            k.KillNpcs = true;
            res.Variants.Add(new KeyValuePair<string, TimelineSolve>("these options, killing NPCs on", Solve(res, k, "killing NPCs on")));
            var r = res.Options.Clone();
            r.RingNeedsLava = !r.RingNeedsLava;
            res.Variants.Add(new KeyValuePair<string, TimelineSolve>("these options, Ring of Levitation " + (r.RingNeedsLava ? "needs lava protection" : "free"), Solve(res, r, "ring")));
        }

        // ---------------------------------------------------------------- classifying the changes

        static readonly HashSet<string> Appliable = new HashSet<string> { "terrain", "delete", "door" };

        /// <summary>Applies each mechanism alone to its level and compares the rebuilt level with the level rebuilt unchanged.</summary>
        static void Classify(TimelineResult res)
        {
            var e = res.Engine;
            var onlyCloses = new List<WorldChange>();
            foreach (var m in res.Abilities.Auto.Graph.Mechanisms)
            {
                var c = new WorldChange { Mechanism = m };
                res.Changes.Add(c);
                if (!Appliable.Contains(m.Kind))
                {
                    c.Effect = "not applied";
                    c.Treatment = m.Kind == "create" ? "its flag is granted when set off (what it creates needs the flag)" :
                        m.Kind == "hack" ? "its flag is granted when set off; the hack itself is not modelled" : "its flag is granted when set off (facts may test it)";
                    continue;
                }
                if (m.Toggle)
                {
                    c.Effect = "comes and goes";
                    c.Treatment = "not applied: one state of a control the player sets either way; the squares its other state opens are ways that ask for that state's flag";
                    continue;
                }
                if (m.Airlock)
                {
                    c.Effect = "comes and goes";
                    c.Treatment = "not applied: an airlock, the lock it puts on the other door comes off again when this door is closed";
                    continue;
                }
                if (m.Kind == "door" && (m.DoorAction == 2 || (m.DoorAction == 3 && m.Unavoidable)))
                {
                    var cl = res.Closers.FirstOrDefault(x => x.Mechanism == m);
                    c.Effect = "closes";
                    c.Closes.Add(string.Format("closes the door at ({0}, {1})", m.X, m.Y));
                    c.Treatment = cl == null ? "not applied (set off by a lever: the player leaves it)" : "never applied: the door is one way instead (" + cl.Treatment + ")";
                    continue;
                }
                // the mechanisms of one trigger go off together: try them together
                var together = res.Abilities.Auto.Graph.Mechanisms.Where(x => x.Level == m.Level && x.Trigger == m.Trigger && Appliable.Contains(x.Kind) && !x.Airlock && !x.Toggle && !(x.Kind == "door" && (x.DoorAction == 2 || (x.DoorAction == 3 && x.Unavoidable))))
                    .Select(x => Requirement.Normalize(x.Flag)).ToList();
                var basis = e.Rebuild(m.Level, new List<string>());
                var trial = e.Rebuild(m.Level, together);
                Compare(res, basis, trial, c);
                bool opens = c.Opens.Count > 0, closes = c.Closes.Count > 0;
                c.Effect = opens && closes ? "opens and closes" : opens ? "opens" : closes ? "closes" : "no way changed";
                if (closes && !opens && !m.Unavoidable)
                {
                    c.Treatment = "not applied: it only takes ways away and the player chooses to set it off";
                    onlyCloses.Add(c);
                    continue;
                }
                c.Applied = true;
                c.Treatment = closes ? "applied when set off; the ways it takes away are gone from then on (the zones reached before stay reached)" : "applied when set off";
            }
            // A change the player chooses that alone only takes ways away may open one together with
            // other changes of its level (UW1 level 4's levers: each raises one step, which alone
            // stands apart, and the three make a stair up to the alcove a fourth lever opens). It
            // is applied when the level with every change has a way that the level with all but
            // this one lacks.
            foreach (var lv in onlyCloses.GroupBy(c => c.Level))
            {
                var all = res.Changes.Where(c => c.Level == lv.Key && (c.Applied || onlyCloses.Contains(c))).ToList();
                if (all.Count < 2) continue;
                var joint = e.Rebuild(lv.Key, all.Select(c => c.Flag).Distinct().ToList());
                foreach (var grp in lv.GroupBy(c => c.Mechanism.Trigger))
                {
                    var mine = new HashSet<string>(grp.Select(c => c.Flag));
                    var without = e.Rebuild(lv.Key, all.Select(c => c.Flag).Where(f => !mine.Contains(f)).Distinct().ToList());
                    var d = new WorldChange { Mechanism = grp.First().Mechanism };
                    Compare(res, without, joint, d);
                    if (d.Opens.Count == 0) continue;
                    foreach (var c in grp)
                    {
                        c.Applied = true;
                        c.Effect = "opens with others";
                        c.Opens.AddRange(d.Opens);
                        c.Treatment = "applied when set off: alone it only takes ways away, but with the other changes of this level it opens one";
                    }
                }
            }
            // Changes the player chooses that alone change no way may take ways away together (the
            // Academy's falling platforms, UW2 level 47: each platform stepped on drops another, and
            // with all of them down the ledge of the way on stands alone). They are taken one
            // trigger after another on top of those before: one that then only takes ways away is
            // left to the player, who steps where it does no harm.
            foreach (var lv in res.Changes.Where(c => c.Applied && !c.Mechanism.Unavoidable && c.Opens.Count == 0 && c.Closes.Count == 0).GroupBy(c => c.Level))
            {
                var groups = lv.GroupBy(c => c.Mechanism.Trigger).OrderBy(x => x.Key).ToList();
                if (groups.Count < 2) continue;
                var acc = new List<string>();
                var prev = e.Rebuild(lv.Key, acc);
                foreach (var grp in groups)
                {
                    var with = acc.Concat(grp.Select(c => c.Flag)).ToList();
                    var next = e.Rebuild(lv.Key, with);
                    var d = new WorldChange { Mechanism = grp.First().Mechanism };
                    Compare(res, prev, next, d);
                    if (d.Closes.Count > 0 && d.Opens.Count == 0)
                    {
                        foreach (var c in grp)
                        {
                            c.Applied = false;
                            c.Effect = "closes with others";
                            c.Closes.AddRange(d.Closes);
                            c.Treatment = "not applied: alone it changes no way, but after the others like it on this level it only takes ways away, and the player chooses to set it off";
                        }
                        continue;
                    }
                    acc = with;
                    prev = next;
                }
            }
            e.Applicable = new HashSet<string>(res.Changes.Where(c => c.Applied).Select(c => c.Flag));
            e.Closing = new HashSet<string>(res.Changes.Where(c => c.Applied && c.Closes.Count > 0).Select(c => c.Flag));
        }

        /// <summary>
        /// A timeline over a zone set alone, with no levels behind it (tests, and zonings drawn by
        /// hand): its links and placements as they are, no world changes, the goal its goal zone.
        /// </summary>
        internal static TimelineResult OnZones(ZoneSet zs, AbilityOptions opt = null)
        {
            opt = opt ?? new AbilityOptions();
            var b = new AbilityZoneResult { Auto = new AutoZoneResult { Graph = new NavGraph(), Set = zs }, Facts = new FactSet(), Options = opt, Nav = new NavOptions() };
            var res = new TimelineResult { Abilities = b };
            res.Engine = new Engine(null, null, b, res);
            res.Engine.Prepare();
            Classify(res);
            res.Solve = Solve(res, opt, "with these options");
            return res;
        }

        static void Compare(TimelineResult res, LevelState a, LevelState b, WorldChange c)
        {
            var zs = res.Set;
            Func<ZoneLink, string> pair = k => k.From + ">" + k.To;
            var pa = new Dictionary<string, List<ZoneLink>>();
            foreach (var k in a.Links) { List<ZoneLink> l; if (!pa.TryGetValue(pair(k), out l)) pa[pair(k)] = l = new List<ZoneLink>(); l.Add(k); }
            var pb = new Dictionary<string, List<ZoneLink>>();
            foreach (var k in b.Links) { List<ZoneLink> l; if (!pb.TryGetValue(pair(k), out l)) pb[pair(k)] = l = new List<ZoneLink>(); l.Add(k); }
            Func<int, string> zn = z => b.NewNames.ContainsKey(z) ? z + " " + b.NewNames[z] : SolveResult.ZoneName(zs, z);
            foreach (var kv in pb)
            {
                var k = kv.Value[0];
                List<ZoneLink> old;
                bool freeNow = kv.Value.Any(x => x.Requires == "free");
                // a way of a part split off a zone is no new way when the zone it was part of had it:
                // inside that zone before (the part and the rest were one), or from that zone to the same other zone
                int pf, pt;
                bool partF = b.PartOf.TryGetValue(k.From, out pf), partT = b.PartOf.TryGetValue(k.To, out pt);
                if (partF || partT)
                {
                    if (!partF) pf = k.From;
                    if (!partT) pt = k.To;
                    if (pf == pt) continue;
                    List<ZoneLink> was;
                    if (pa.TryGetValue(pf + ">" + pt, out was) && (!freeNow || was.Any(x => x.Requires == "free"))) continue;
                }
                if (!pa.TryGetValue(kv.Key, out old)) c.Opens.Add(string.Format("{0} -> {1} ({2}, {3})", zn(k.From), zn(k.To), k.Kind, k.Requires));
                else if (freeNow && !old.Any(x => x.Requires == "free")) c.Opens.Add(string.Format("{0} -> {1} now free", zn(k.From), zn(k.To)));
            }
            // a way that went still counts as there when another route of the changed level does the same, needing nothing more
            Func<string, HashSet<string>> toks = r => { var l = new List<string>(); Requirement.TryParse(r, out _)?.Tokens(l); return new HashSet<string>(l.Where(t => t != "free")); };
            Func<int, int, string, bool> still = (from, to, req) =>
            {
                var allowed = toks(req);
                var seen = new HashSet<int> { b.ZoneOfBase(from) };
                var q = new Queue<int>(seen);
                int goal = b.ZoneOfBase(to);
                while (q.Count > 0)
                {
                    int z = q.Dequeue();
                    if (z == goal) return true;
                    foreach (var k in b.Links)
                    {
                        if (k.Requires != "free" && !toks(k.Requires).IsSubsetOf(allowed)) continue;
                        int nx = k.From == z ? k.To : k.TwoWay && k.To == z ? k.From : -1;
                        if (nx >= 0 && seen.Add(nx)) q.Enqueue(nx);
                    }
                }
                return false;
            };
            foreach (var kv in pa)
            {
                var k = kv.Value[0];
                List<ZoneLink> now;
                if (!pb.TryGetValue(kv.Key, out now))
                {
                    // a way gone; unless both ends are one zone now, or another way does as much
                    if (b.ZoneOfBase(k.From) == b.ZoneOfBase(k.To)) continue;
                    if (kv.Value.Any(x => still(k.From, k.To, x.Requires))) continue;
                    c.Closes.Add(string.Format("{0} -> {1} ({2}) gone", zn(k.From), zn(k.To), k.Kind));
                }
                else if (kv.Value.Any(x => x.Requires == "free") && !now.Any(x => x.Requires == "free") && !still(k.From, k.To, "free")) c.Closes.Add(string.Format("{0} -> {1} no longer free", zn(k.From), zn(k.To)));
            }
            foreach (var z in b.NewNames.Keys.Where(z => !a.NewNames.ContainsKey(z) || a.NewNames[z] != b.NewNames[z]))
                if (b.PartOf.ContainsKey(z) && !a.NewNames.Values.Contains(b.NewNames[z])) c.Closes.Add("splits off " + z + " " + b.NewNames[z]);     // a part of a zone; squares that were no zone's are a new area, nothing taken away
            int rock = 0;
            for (int i = 0; i < 64 * 64; i++) if (!a.Model.Tiles[i % 64, i / 64].IsSolid && b.Model.Tiles[i % 64, i / 64].IsSolid) rock++;
            if (rock > 0) c.Closes.Add(rock + " square" + (rock == 1 ? "" : "s") + " turned to rock");
            int opened = 0;
            for (int i = 0; i < 64 * 64; i++) if (a.Model.Tiles[i % 64, i / 64].IsSolid && !b.Model.Tiles[i % 64, i / 64].IsSolid) opened++;
            if (opened > 0 && c.Opens.Count == 0) c.Opens.Add(opened + " square" + (opened == 1 ? "" : "s") + " opened (no new way between zones)");
            foreach (var kv in b.Absorbed.Where(kv => { int t; return !a.Absorbed.TryGetValue(kv.Key, out t) || t != kv.Value; })) c.Opens.Add(string.Format("joins {0} with {1}", zn(kv.Key), zn(kv.Value)));
        }

        // ---------------------------------------------------------------- the level states

        /// <summary>A level as the timeline sees it after some changes: its rebuilt regions grouped into zones, the links, the mechanisms.</summary>
        internal sealed class LevelState
        {
            public int Level;
            public string Key;
            public LevelModel Model;
            public NavGraph Graph;
            /// <summary>Region id -> zone id.</summary>
            public int[] Zone;
            public readonly List<ZoneLink> Links = new List<ZoneLink>();
            public readonly List<KeyValuePair<NavMechanism, Requirement>> Mechs = new List<KeyValuePair<NavMechanism, Requirement>>();
            /// <summary>Zones split off by the changes (fresh ids) and their names.</summary>
            public readonly Dictionary<int, string> NewNames = new Dictionary<int, string>();
            /// <summary>A zone split off a base zone (a fresh id) -> that base zone.</summary>
            public readonly Dictionary<int, int> PartOf = new Dictionary<int, int>();
            /// <summary>Base zones with no group of their own any more -> the zone that took their tiles.</summary>
            public readonly Dictionary<int, int> Absorbed = new Dictionary<int, int>();
            /// <summary>Placements on this level that lie in another zone now.</summary>
            public readonly Dictionary<Placement, int> PlacementZone = new Dictionary<Placement, int>();
            public int ZoneOfBase(int z) { int t; return Absorbed.TryGetValue(z, out t) ? t : z; }
            /// <summary>Tile (y * 64 + x) -> zone, for placements found later (Phase D).</summary>
            public readonly Dictionary<int, int> TileZone = new Dictionary<int, int>();
        }

        /// <summary>The changing world: the base zones, the level states (cached by the changes applied) and the solver.</summary>
        internal sealed class Engine
        {
            readonly GameSession s;
            readonly Func<int, LevelModel> orig;
            readonly AbilityZoneResult b;
            readonly TimelineResult res;
            readonly ZoneSet zs;
            readonly Dictionary<string, LevelState> cache = new Dictionary<string, LevelState>();
            readonly Dictionary<string, NavMechanism> byFlag = new Dictionary<string, NavMechanism>();
            readonly Dictionary<string, Requirement> parsed = new Dictionary<string, Requirement>();
            /// <summary>Where each placement lies (level, x, y), when known.</summary>
            readonly Dictionary<Placement, int[]> spot = new Dictionary<Placement, int[]>();
            /// <summary>The base mechanisms per level, with their set-off requirement in zone terms.</summary>
            readonly Dictionary<int, List<KeyValuePair<NavMechanism, Requirement>>> baseMechs = new Dictionary<int, List<KeyValuePair<NavMechanism, Requirement>>>();
            readonly Dictionary<int, List<ZoneLink>> baseLinks = new Dictionary<int, List<ZoneLink>>();
            readonly List<ZoneLink> globalLinks = new List<ZoneLink>();
            readonly HashSet<string> cuts = new HashSet<string>();
            /// <summary>Zones that are only a door kept apart (a closer's doorway): the player passes them, he never stays in them.</summary>
            readonly HashSet<int> doorways = new HashSet<int>();
            int nextFresh = FreshZoneIds;
            /// <summary>Phase D (the key item shuffle): the placements a run uses instead of the zone set's, and where the new ones lie (level, x, y).</summary>
            List<Placement> placements;
            readonly Dictionary<Placement, int[]> extraSpot = new Dictionary<Placement, int[]>();
            /// <summary>The count facts (kind "count"), by id.</summary>
            Dictionary<string, Fact> countFacts = new Dictionary<string, Fact>();
            readonly Dictionary<string, int> freshIds = new Dictionary<string, int>();
            /// <summary>The flags of the changes applied when set off (Classify).</summary>
            public HashSet<string> Applicable = new HashSet<string>();
            /// <summary>The flags of the applied changes that also take ways away (Classify): after one, what is still reached is worked out again.</summary>
            public HashSet<string> Closing = new HashSet<string>();
            string plantedRequires;

            public Engine(GameSession s, Func<int, LevelModel> level, AbilityZoneResult b, TimelineResult res)
            {
                this.s = s; orig = level; this.b = b; this.res = res; zs = b.Set;
            }

            Requirement Req(string text)
            {
                text = string.IsNullOrWhiteSpace(text) ? "free" : text;
                Requirement r;
                if (parsed.TryGetValue(text, out r)) return r;
                string err;
                r = Requirement.TryParse(text, out err) ?? Requirement.Parse("flag:__unparsable__");
                if (err != null) res.Warnings.Add("Cannot read '" + text + "': " + err);
                parsed[text] = r;
                return r;
            }

            static readonly Regex RegionTerm = new Regex(@"region:(\d+)");

            /// <summary>The base world and the facts' links, warps and goal.</summary>
            public void Prepare()
            {
                var g = b.Auto.Graph;
                foreach (var m in g.Mechanisms) { string f = Requirement.Normalize(m.Flag); if (!byFlag.ContainsKey(f)) byFlag[f] = m; }
                foreach (var grp in g.Regions.Where(r => r.Zone > 0).GroupBy(r => r.Zone)) if (grp.All(r => r.KeepApart)) doorways.Add(grp.Key);
                foreach (var grp in g.Mechanisms.GroupBy(m => m.Level))
                    baseMechs[grp.Key] = grp.Select(m => new KeyValuePair<NavMechanism, Requirement>(m, Req(When(m, r => g.Regions[r].Zone)))).ToList();
                foreach (var k in zs.Links)
                {
                    if (k.Kind == "stairs" || k.Hand || k.Level < 1 || k.Level > zs.Levels) { globalLinks.Add(k); continue; }
                    List<ZoneLink> l;
                    if (!baseLinks.TryGetValue(k.Level, out l)) baseLinks[k.Level] = l = new List<ZoneLink>();
                    l.Add(k);
                }
                // where the placements lie
                var objs = new Dictionary<int, LevelModel>();
                Func<int, LevelModel> lm = l => { LevelModel m; if (!objs.TryGetValue(l, out m)) { try { m = b.WalledLevel(l); } catch (Exception) { m = null; } objs[l] = m; } return m; };
                var place = new Regex(@"^#(\d+) .*?level (\d+) \((\d+), (\d+)\)");
                foreach (var p in zs.Placements)
                {
                    var mm = place.Match(p.Notes ?? "");
                    if (mm.Success) { spot[p] = new[] { int.Parse(mm.Groups[2].Value), int.Parse(mm.Groups[3].Value), int.Parse(mm.Groups[4].Value) }; continue; }
                    var src = p.Source ?? "";
                    if (src.StartsWith("facts: "))
                    {
                        var f = res.Facts.Facts.FirstOrDefault(x => x.Id == src.Substring(7));
                        FactSpot moved;
                        // a fact that follows its object (match): the object it found in these levels
                        var at = f != null && f.Match != null ? (b.FactObjects.TryGetValue(f.Id, out moved) ? moved : null) : f?.TopSpot;
                        if (at == null) continue;
                        int[] t = Tile(at, lm);
                        if (t != null) spot[p] = t;
                    }
                }
                Facts(lm);
                countFacts = res.Facts.OfKind("count").Where(f => f.Grants.Length > 0 && f.AtLeast > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            }

            /// <summary>The placements the runs use (null: the zone set's again), with the tiles of those the zone set does not hold.</summary>
            public void UsePlacements(List<Placement> list, Dictionary<Placement, int[]> spots)
            {
                placements = list;
                extraSpot.Clear();
                if (spots != null) foreach (var kv in spots) extraSpot[kv.Key] = kv.Value;
            }

            List<Placement> Placements { get { return placements ?? zs.Placements; } }

            /// <summary>The zone of a placement's tile in a changed level (placements the zone set does not hold).</summary>
            int ZoneOfTile(LevelState st, int[] t, int dflt)
            {
                int key = t[2] * 64 + t[1], z;
                if (st.TileZone.TryGetValue(key, out z)) return z > 0 ? z : dflt;
                int r = st.Graph.NearestRegion(st.Level, t[1], t[2], 2, true);
                z = r >= 0 && r < st.Zone.Length ? st.Zone[r] : 0;
                st.TileZone[key] = z;
                return z > 0 ? z : dflt;
            }

            int[] Tile(FactSpot at, Func<int, LevelModel> lm)
            {
                if (at == null || at.Level <= 0 || at.Level > zs.Levels) return null;
                if (at.HasTile) return new[] { at.Level, at.X, at.Y };
                if (!at.HasObject) return null;
                var m = lm(at.Level);
                if (m == null || at.Object >= m.Objects.Length || m.Objects[at.Object].Free || m.Objects[at.Object].TileX < 0) return null;
                return new[] { at.Level, m.Objects[at.Object].TileX, m.Objects[at.Object].TileY };
            }

            int ZoneAt(int[] t) { return t == null ? 0 : zs.NearestZone(t[0], t[1], t[2], 3); }

            // ---- where a spell is cast (AbilityZones.CastAt): the ways are narrowed when the levels are built; the facts' links,
            // the placements, the mechanisms and the warps here

            readonly Dictionary<string, string> castAt = new Dictionary<string, string>();

            /// <summary>A requirement as it holds when the spell is cast at the square t (level, x, y); unchanged with no square.</summary>
            string CastAt(string req, int[] t)
            {
                if (t == null || string.IsNullOrEmpty(req) || !req.Contains("spell:")) return req;
                string key = req + "|" + t[0] + "|" + t[1] + "|" + t[2], r;
                if (!castAt.TryGetValue(key, out r))
                {
                    r = AbilityZones.CastAt(s, b.WalledLevel, res.Facts, req, t[0], t[1], t[2]);
                    var p = Requirement.TryParse(r, out _);
                    castAt[key] = r = p == null ? r : p.ToString();
                }
                return r;
            }

            /// <summary>A placement's requirement where it lies (its tile, when known).</summary>
            string PlaceRequires(Placement p)
            {
                if (string.IsNullOrEmpty(p.Requires) || !p.Requires.Contains("spell:")) return p.Requires;
                int[] t;
                if (!spot.TryGetValue(p, out t)) extraSpot.TryGetValue(p, out t);
                return CastAt(p.Requires, t);
            }

            /// <summary>Base zone -> one square of each kind of place a spell can be cast from in it (CastKind).</summary>
            Dictionary<int, List<int[]>> castSquares;
            Dictionary<int, int[]> freshSquare;

            /// <summary>What decides a spell at a square: no magic, the nomana fact, Britannia (the same kind narrows a requirement the same way).</summary>
            string CastKind(int l, int x, int y)
            {
                if (AbilityZones.NoMagicAt(s, b.WalledLevel, l, x, y)) return "nomagic";
                var f = res.Facts.NoMana(l, x, y);
                return (f != null ? "nomana:" + f.Id : "") + (AbilityZones.InBritannia(s, l) ? "|britannia" : "");
            }

            /// <summary>
            /// The squares a spell may be cast from in a zone: one of each kind (CastKind) for a base
            /// zone, read off the zone set's tiles; for a zone a change split off (a fresh id), its
            /// first square.
            /// </summary>
            List<int[]> CastSquares(int z)
            {
                if (castSquares == null)
                {
                    castSquares = new Dictionary<int, List<int[]>>();
                    var kinds = new Dictionary<int, HashSet<string>>();
                    for (int l = 1; l <= zs.Levels; l++)
                        for (int y = 0; y < 64; y++)
                            for (int x = 0; x < 64; x++)
                            {
                                int k = zs.ZoneAt(l, x, y);
                                if (k <= ZoneSet.Unassigned) continue;
                                HashSet<string> seen;
                                if (!kinds.TryGetValue(k, out seen)) { kinds[k] = seen = new HashSet<string>(); castSquares[k] = new List<int[]>(); }
                                if (seen.Add(CastKind(l, x, y))) castSquares[k].Add(new[] { l, x, y });
                            }
                }
                List<int[]> list;
                if (castSquares.TryGetValue(z, out list)) return list;
                if (freshSquare == null || freshSquare.Count < freshIds.Count)
                {
                    freshSquare = new Dictionary<int, int[]>();
                    foreach (var kv in freshIds)
                    {
                        var parts = kv.Key.Split('|');
                        int l = int.Parse(parts[0]), first = int.Parse(parts[2]);
                        freshSquare[kv.Value] = first >= 0 ? new[] { l, first % 64, first / 64 } : new[] { l, -1, -1 };
                    }
                }
                int[] t;
                return freshSquare.TryGetValue(z, out t) ? new List<int[]> { t } : new List<int[]>();
            }

            /// <summary>
            /// A warp's requirement as it holds from the zones reached (ctx.Reach; only those on its
            /// level when it starts from one): a spell is cast where the player stands, so the warp
            /// holds when it holds from one of them (CastAt). Gate Travel from UW1's level 7 before the
            /// orb is broken takes a scroll or a wand, and from a no-magic square nothing.
            /// </summary>
            string WarpRequires(Fact f, Ctx ctx)
            {
                if (string.IsNullOrEmpty(f.Requires) || !f.Requires.Contains("spell:")) return f.Requires;
                var alts = new HashSet<string>();
                foreach (int z in ctx.Reach)
                    foreach (var t in CastSquares(z))
                        if (f.FromLevel <= 0 || t[0] == f.FromLevel) alts.Add(CastAt(f.Requires, t));
                if (alts.Count == 0) return "flag:__never__";
                if (alts.Count == 1) return alts.First();
                return string.Join(" | ", alts.OrderBy(a => a, StringComparer.Ordinal).Select(a => "(" + a + ")"));
            }

            void Facts(Func<int, LevelModel> lm)
            {
                foreach (var f in res.Facts.OfKind("link"))
                {
                    int[] ta = Tile(f.From, lm), tz = Tile(f.To, lm);
                    int a = ZoneAt(ta), z = ZoneAt(tz);
                    if (a <= ZoneSet.Unassigned || z <= ZoneSet.Unassigned) { res.Warnings.Add("Facts: " + f.Id + ": no zone at " + f.From + " or " + f.To + "."); continue; }
                    // a link that starts at a creature (Altara's door) lies in one zone only while the creature stands there: no fault in the facts
                    if (a == z && f.From.HasObject && lm(f.From.Level) is LevelModel fm && f.From.Object < fm.Objects.Length && fm.Objects[f.From.Object].IsCritter) continue;
                    if (a == z) { res.Warnings.Add("Facts: " + f.Id + ": " + f.From + " and " + f.To + " lie in the same zone (" + SolveResult.ZoneName(zs, a) + "); the link changes nothing."); continue; }
                    // a spell is cast where the way starts (AbilityZones.CastAt): a two-way link whose ends differ becomes a link each way
                    string fwd = CastAt(f.Requires, ta), back = f.TwoWay ? CastAt(f.Requires, tz) : fwd;
                    string kind = f.LinkKind.Length > 0 ? f.LinkKind : "other", conf = f.Confidence == "todo" ? "todo" : "certain";
                    res.FactLinks.Add(new ZoneLink { Id = 9000 + res.FactLinks.Count, From = a, To = z, Kind = kind, Requires = fwd, TwoWay = f.TwoWay && fwd == back, Notes = "facts: " + f.Id, Level = f.From.Level, X = f.From.X, Y = f.From.Y, Confidence = conf, Once = f.Once });
                    if (f.TwoWay && fwd != back)
                        res.FactLinks.Add(new ZoneLink { Id = 9000 + res.FactLinks.Count, From = z, To = a, Kind = kind, Requires = back, Notes = "facts: " + f.Id + " (back)", Level = f.To.Level, X = f.To.X, Y = f.To.Y, Confidence = conf });
                    if (f.Replace) { cuts.Add(a + ">" + z); cuts.Add(z + ">" + a); }
                }
                foreach (var f in res.Facts.OfKind("warp"))
                {
                    res.Warps.Add(f);
                    if (f.To != null && f.To.IsPlanted) plantedRequires = plantedRequires == null ? f.Requires : plantedRequires + " | " + f.Requires;
                }
                var goals = res.Facts.OfKind("goal").Where(f => f.Option.Length == 0).ToList();
                res.GoalExtras.AddRange(res.Facts.OfKind("goal").Where(f => f.Option.Length > 0));
                res.GoalZone = zs.GoalZone;
                if (goals.Count > 0)
                {
                    var f = goals[0];
                    int z = ZoneAt(Tile(f.To, lm));
                    if (z > ZoneSet.Unassigned) { res.GoalZone = z; res.GoalRequires = string.IsNullOrWhiteSpace(f.Requires) ? "free" : f.Requires; res.GoalFact = f; }
                    else res.Warnings.Add("Facts: " + f.Id + ": no zone at " + f.To + ".");
                    if (goals.Count > 1) res.Warnings.Add("Facts: " + goals.Count + " goal facts; the first (" + f.Id + ") is used.");
                }
                // the squares that end the game (level 9's, and any put elsewhere): reaching one wins too
                if (s != null) foreach (var et in EndGame.Find(s, orig))
                {
                    int z = ZoneAt(new[] { et.Level, et.X, et.Y });
                    if (z > ZoneSet.Unassigned) res.Ends.Add(new KeyValuePair<EndTrigger, int>(et, z));
                    else res.Warnings.Add("The end of the game at " + et + " lies in no zone.");
                }
            }

            /// <summary>The zone an end of the game lies in, in the world as it stands.</summary>
            int EndZoneNow(KeyValuePair<EndTrigger, int> e, Dictionary<int, LevelState> cur)
            {
                LevelState st;
                return cur.TryGetValue(e.Key.Level, out st) ? ZoneOfTile(st, new[] { e.Key.Level, e.Key.X, e.Key.Y }, e.Value) : e.Value;
            }

            /// <summary>What sets a mechanism off, in zone terms: where (its trigger's regions), its conditions, a look's Search check.</summary>
            string When(NavMechanism m, Func<int, int> zoneOf)
            {
                if (m.SetOff.Length == 0) return "flag:__never__";
                string needs = res.Facts.MechanismNeeds(m.Level, m.Trigger, m.Flag);
                string where = RegionTerm.Replace(m.SetOff, mm => { int r = int.Parse(mm.Groups[1].Value); int z = zoneOf(r); return z > 0 ? "zone:" + z : "flag:__no_zone__"; });
                var terms = new List<string> { where, m.Conditions, needs };
                // a look trigger of height z > 0 passes skill_check(Search, z); Reveal sets it off with Search 45 (event/TRIGGER.C UseTrigger, combat/SPELLS.C sp_true_sight)
                if (m.Look > 0) terms.Add(m.Look < 30 ? "spell:reveal | search:" + m.Look : "search:" + m.Look);
                // a spell it asks for (Reveal for a look, what the facts add) where magic does not work, as the ways are narrowed (AbilityZones.CastAt):
                // cast at the trigger's square when it lies on the map, else at the square the trap acts on
                int tx = m.X, ty = m.Y;
                LevelModel lm = null;
                try { lm = b.WalledLevel != null ? b.WalledLevel(m.Level) : null; } catch (Exception) { }
                if (lm != null && m.Trigger > 0 && m.Trigger < lm.Objects.Length && lm.Objects[m.Trigger].TileX >= 0) { tx = lm.Objects[m.Trigger].TileX; ty = lm.Objects[m.Trigger].TileY; }
                return CastAt(ZoneLink.JoinAnd(terms), new[] { m.Level, tx, ty });
            }

            // ---- applying changes to a level

            /// <summary>The level with the changes applied, in the given order (event/TRIGGER.C, event/WORLDEV.C change_terrain).</summary>
            LevelModel Changed(int level, List<string> flags)
            {
                var o = orig(level);
                var m = LevelModel.FromBytes(s, level, o.Block.Data, o.TextureBytes);
                foreach (var f in flags)
                {
                    NavMechanism me;
                    if (!byFlag.TryGetValue(f, out me) || me.Level != level) continue;
                    Apply(m, me);
                }
                HashSet<int> walls;
                if (b.WallTiles.TryGetValue(level, out walls)) foreach (int t in walls) m.Tiles[t % 64, t / 64].Type = TileType.Solid;
                return m;
            }

            void Apply(LevelModel m, NavMechanism me)
            {
                var trap = m.Objects[me.Trap];
                switch (me.Kind)
                {
                    case "terrain":
                        {
                            // change_terrain: type (10 or more unchanged), height (UW1 at most 13), floor (11 or more unchanged); the wall texture does not matter here
                            // the chain may hold several change-terrain traps: apply each one met (NavMechanism joins them)
                            foreach (var t in TerrainTraps(m, me))
                            {
                                int type = t.Item2.Heading * 2 + (t.Item2.Quality & 1);
                                if (type == 15) type = 10;
                                int height = t.Item2.Z >> 3, floor = t.Item2.Quality >> 1;
                                for (int y = t.Item1[1]; y <= t.Item1[1] + t.Item2.FineY; y++)
                                    for (int x = t.Item1[0]; x <= t.Item1[0] + t.Item2.FineX; x++)
                                    {
                                        if (x < 0 || y < 0 || x > 63 || y > 63) continue;
                                        var tile = m.Tiles[x, y];
                                        if (height <= (s.IsUw2 ? 14 : 13)) tile.Height = height;     // UW2: 15 leaves the height (event/WORLDEV.C change_terrain sets 0 to 14; 15 only for an oscillator)
                                        if (floor < (s.IsUw2 ? 15 : 11)) tile.FloorIndex = floor;
                                        if (type < 10) tile.Type = (TileType)type;
                                    }
                            }
                            break;
                        }
                    case "delete":
                        {
                            // TRAP_DELETE_OBJECT: the object the trap links to, from the list of tile (quality, owner)
                            int x = trap.Quality, y = trap.Owner, i = trap.Link;
                            if (i <= 0 || i >= m.Objects.Length || x < 0 || y < 0 || x > 63 || y > 63) break;
                            var t = m.Tiles[x, y];
                            if (t.FirstObject == i) t.FirstObject = m.Objects[i].Next;
                            else
                            {
                                int j = t.FirstObject, guard = 0;
                                while (j > 0 && j < m.Objects.Length && guard++ < 1100) { if (m.Objects[j].Next == i) { m.Objects[j].Next = m.Objects[i].Next; break; } j = m.Objects[j].Next; }
                            }
                            var stack = new Stack<int>();
                            stack.Push(i);
                            var seen = new HashSet<int>();
                            while (stack.Count > 0) { int k = stack.Pop(); if (k <= 0 || !seen.Add(k)) continue; m.Objects[k].Free = true; foreach (int c in m.Objects[k].Children) stack.Push(c); }
                            break;
                        }
                    case "door":
                        {
                            // TRAP_DOOR (UW1): the door's lock goes, a copy of the trap's lock comes, then open, close or toggle
                            ObjectInfo door = null;
                            foreach (var o in m.TileObjects(me.X, me.Y)) if (o.Item >= 0x140 && o.Item <= 0x14F) { door = o; break; }
                            if (door == null) break;
                            door.Children.RemoveAll(c => m.Objects[c].Item == 0x10F);
                            if (!trap.IsQuantity && trap.Link > 0 && trap.Link < m.Objects.Length && m.Objects[trap.Link].Item == 0x10F) door.Children.Add(trap.Link);
                            bool open = door.Item >= 0x148;
                            bool want = trap.Quality == 1 ? true : trap.Quality == 2 ? false : !open;
                            if (want && !open) door.Item += 8;
                            if (!want && open) door.Item -= 8;
                            break;
                        }
                }
            }

            /// <summary>The change-terrain traps a mechanism's chain runs, with the square each runs at.</summary>
            static List<Tuple<int[], ObjectInfo>> TerrainTraps(LevelModel m, NavMechanism me)
            {
                var list = new List<Tuple<int[], ObjectInfo>>();
                var trig = m.Objects[me.Trigger];
                int rx = trig.Quality, ry = trig.Owner;
                var seen = new HashSet<int> { trig.Index };
                var next = Editing.Triggers.LinksTo(m, trig);
                while (next != null && seen.Add(next.Index) && seen.Count < 256)
                {
                    if (Editing.Triggers.IsTrigger(next.Item)) { rx = next.Quality; ry = next.Owner; next = Editing.Triggers.LinksTo(m, next); continue; }
                    if (!Editing.Triggers.IsTrap(next.Item)) break;
                    if (next.Item == 0x185) list.Add(Tuple.Create(new[] { rx, ry }, next));
                    int it = next.Item;
                    // an inventory or variable check: the mechanism goes off only when it passes (its conditions), so go on
                    if (it == 0x187 || it == 0x18B || it == 0x189 || it == 0x18A || it == 0x188) break;
                    next = Editing.Triggers.LinksTo(m, next);
                }
                return list;
            }

            /// <summary>
            /// The changes (of those applied to its level, in order) without which a link of a
            /// changed level would not be there: none when the unchanged level has it; else each
            /// change is left out in turn, from the last, and kept only when the link goes without it.
            /// </summary>
            public List<string> ChangesFor(ZoneLink k, List<string> applied)
            {
                var keep = new List<string>();
                if (k == null || k.Level < 1 || applied == null || applied.Count == 0 || globalLinks.Contains(k)) return keep;
                // the same way: the same kind and requirement into the same zone, at the same square when the link has one
                // (a change that joins two zones gives the joined zone one of their ids, so the zone a way starts in may be named anew
                // though the way was always there: UW1 level 7's door at (55, 2) once the wall at (42, 10) is open)
                Func<IEnumerable<ZoneLink>, bool> has = ls => ls.Any(x => x.Kind == k.Kind && x.Requires == k.Requires &&
                    (k.X >= 0 ? x.X == k.X && x.Y == k.Y && (x.To == k.To || (x.TwoWay && x.From == k.To)) : x.From == k.From && x.To == k.To));
                List<ZoneLink> bl;
                if (baseLinks.TryGetValue(k.Level, out bl) && has(bl)) return keep;
                // not a way of the level's own (a fact's link anchored there): no change makes it
                if (!has(Rebuild(k.Level, applied).Links)) return keep;
                keep.AddRange(applied.Distinct());
                for (int i = keep.Count - 1; i >= 0; i--)
                {
                    var trial = keep.Where((f, j) => j != i).ToList();
                    if (has(Rebuild(k.Level, trial).Links)) keep = trial;
                }
                return keep;
            }

            /// <summary>The level rebuilt with the changes (flags, in order), its regions grouped and mapped onto the base zones (cached).</summary>
            public LevelState Rebuild(int level, List<string> flags)
            {
                // in the order they went off: a later change to the same squares wins, as in the game
                var applied = flags.Where(f => byFlag.ContainsKey(f) && byFlag[f].Level == level).Distinct().ToList();
                string key = level + "|" + string.Join(",", applied);
                LevelState st;
                if (cache.TryGetValue(key, out st)) return st;
                var model = Changed(level, applied);
                var scratch = new AbilityZoneResult { Facts = b.Facts, Options = b.Options };
                Func<int, LevelModel> lv = l => l == level ? model : b.WalledLevel(l);
                var g = AbilityZones.Graph(s, lv, b.Nav, b.Facts, scratch, null, new[] { level });
                st = new LevelState { Level = level, Key = key, Model = model, Graph = g };
                Group(st, lv);
                cache[key] = st;
                return st;
            }

            /// <summary>
            /// Groups the rebuilt regions as the automatic zones do (the strongly connected parts
            /// of the free edges, door tiles with a neighbour, empty dead ends folded) and gives each
            /// group a zone: the base zone with the most tiles in it, each base zone to one group
            /// only (the one holding most of its tiles). A group left without one gets a new zone
            /// (a part split off by the change). A base zone whose tiles all went into groups that
            /// took other zones is joined to the zone that took most of them by a free link both
            /// ways (merged by the change).
            /// </summary>
            void Group(LevelState st, Func<int, LevelModel> lv)
            {
                var g = st.Graph;
                int n = g.Regions.Count;
                var adj = new List<int>[n];
                for (int i = 0; i < n; i++) adj[i] = new List<int>();
                foreach (var e in g.Edges) if (e.Requires == "free" && e.From != e.To && e.Kind != "stairs") adj[e.From].Add(e.To);
                var comp = AutoZones.Scc(adj);
                var size = new Dictionary<int, int>();
                foreach (int c in comp) { int k; size.TryGetValue(c, out k); size[c] = k + 1; }
                var nb = new Dictionary<int, SortedSet<int>>();
                foreach (var e in g.Edges)
                {
                    if (e.Kind == "stairs" || e.Kind == "teleport") continue;
                    foreach (var p in new[] { (e.From, e.To), (e.To, e.From) })
                    {
                        if (!g.Regions[p.Item1].IsDoor || g.Regions[p.Item2].IsDoor) continue;
                        SortedSet<int> set;
                        if (!nb.TryGetValue(p.Item1, out set)) nb[p.Item1] = set = new SortedSet<int>();
                        set.Add(p.Item2);
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    if (!g.Regions[i].IsDoor || g.Regions[i].KeepApart || size[comp[i]] > 1) continue;
                    SortedSet<int> set;
                    if (nb.TryGetValue(i, out set) && set.Count > 0) comp[i] = comp[set.Min];
                }
                // a painted zoning keeps its zones after a change; an automatic one is regrouped as the automatic zones are (its zones are those groups)
                if (b.Auto.Given != null && !AutoZones.IsAutomatic(b.Auto.Given)) { GroupGiven(st, comp); return; }
                if (g.Options.FoldEmptyDeadEnds)
                {
                    // the stairs of this level (not in a one-level graph) keep their regions from folding, as in the full graph
                    var busy = new HashSet<int>();
                    var g0 = b.Auto.Graph;
                    foreach (var e in g0.Edges)
                    {
                        if (e.Kind != "stairs") continue;
                        foreach (int r0 in new[] { e.From, e.To })
                        {
                            var reg = g0.Regions[r0];
                            if (reg.Level != st.Level) continue;
                            foreach (int t in reg.Tiles) { int r = g.RegionOf(st.Level, t % 64, t / 64); if (r >= 0) busy.Add(r); }
                        }
                    }
                    AutoZones.FoldDeadEnds(s, lv, g, comp, busy);
                }
                // the base zones in each group
                var counts = new Dictionary<int, Dictionary<int, int>>();
                var tiles = new Dictionary<int, int>();
                for (int i = 0; i < n; i++)
                {
                    Dictionary<int, int> c;
                    if (!counts.TryGetValue(comp[i], out c)) counts[comp[i]] = c = new Dictionary<int, int>();
                    foreach (int z in BaseZones(st.Level, g.Regions[i]))
                    {
                        int k; tiles.TryGetValue(comp[i], out k); tiles[comp[i]] = k + 1;
                        if (z <= ZoneSet.Unassigned) continue;
                        int v; c.TryGetValue(z, out v); c[z] = v + 1;
                    }
                }
                var claims = counts.SelectMany(kv => kv.Value.Select(z => new { Group = kv.Key, Zone = z.Key, Count = z.Value })).OrderByDescending(x => x.Count).ThenBy(x => x.Zone).ThenBy(x => x.Group).ToList();
                var idOf = new Dictionary<int, int>();
                var taken = new HashSet<int>();
                foreach (var c in claims)
                {
                    if (idOf.ContainsKey(c.Group) || taken.Contains(c.Zone)) continue;
                    idOf[c.Group] = c.Zone;
                    taken.Add(c.Zone);
                }
                foreach (var grp in counts.Keys.OrderBy(x => x))
                {
                    if (idOf.ContainsKey(grp)) continue;
                    var top = counts[grp].OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).FirstOrDefault();
                    var mine = Enumerable.Range(0, n).Where(i => comp[i] == grp).ToList();
                    // the same part gets the same id in every state: keyed by the level, its main base zone and its first tile of that zone
                    int first = mine.SelectMany(i => Spots(st.Level, g.Regions[i])).Where(t => top <= ZoneSet.Unassigned || t.Value == top).Select(t => t.Key).DefaultIfEmpty(-1).Min();
                    string fk = st.Level + "|" + top + "|" + first;
                    int id;
                    if (!freshIds.TryGetValue(fk, out id)) freshIds[fk] = id = nextFresh++;
                    idOf[grp] = id;
                    if (top > ZoneSet.Unassigned) st.PartOf[id] = top;
                    st.NewNames[id] = top > ZoneSet.Unassigned ? string.Format("part of {0} at ({1}, {2})", ZoneName(top), first % 64, first / 64) : string.Format("L{0} new area at ({1}, {2})", st.Level, first % 64, first / 64);
                }
                st.Zone = new int[n];
                for (int i = 0; i < n; i++) st.Zone[i] = idOf[comp[i]];
                // base zones of this level with no group: merged into the group holding most of their tiles
                var levelZones = zs.CountsOnLevel(st.Level).Keys.Where(z => z > ZoneSet.Unassigned).ToList();
                foreach (int z in levelZones)
                {
                    if (taken.Contains(z)) continue;
                    var best = counts.Where(kv => kv.Value.ContainsKey(z)).OrderByDescending(kv => kv.Value[z]).Select(kv => (int?)kv.Key).FirstOrDefault();
                    if (best == null) continue;
                    st.Absorbed[z] = idOf[best.Value];
                    st.Links.Add(new ZoneLink { From = z, To = idOf[best.Value], Kind = "walk", Requires = "free", TwoWay = true, Level = st.Level, Notes = "merged by the change" });
                }
                Finish(st);
            }

            /// <summary>
            /// With a given zoning: each rebuilt region takes the base zone that holds most of its
            /// tiles (as the zoning was laid on the regions at first), so the zones stay those of the
            /// zoning. A region on tiles no zone holds (opened by the change) takes its group's zone,
            /// else a new zone. A base zone of this level that no region takes any more is joined by a
            /// free link to the zone that took most of its tiles.
            /// Open question (code review, 2026-11-04, left for Karl): a change that cuts a painted zone
            /// in two (a portcullis closing inside it) leaves both parts in that zone, so the solver is
            /// too kind there; the automatic regroup gives the cut-off part a fresh id ("part of Z").
            /// A fix would split only the parts that had a free way between them before the change and
            /// have none after it, so that zones painted apart on purpose stay as painted.
            /// </summary>
            /// <summary>The squares of a region with the base zone each lies in: its floor tiles by the zones' map, its decks by the zones' deck list.</summary>
            IEnumerable<KeyValuePair<int, int>> Spots(int level, NavRegion r)
            {
                foreach (int t in r.Tiles) yield return new KeyValuePair<int, int>(t, zs.ZoneAt(level, t % 64, t / 64));
                foreach (var d in r.Decks) yield return new KeyValuePair<int, int>(d.Y * 64 + d.X, zs.DeckZone(level, d.X, d.Y, d.Top));
            }

            IEnumerable<int> BaseZones(int level, NavRegion r) { return Spots(level, r).Select(t => t.Value); }

            void GroupGiven(LevelState st, int[] comp)
            {
                var g = st.Graph;
                int n = g.Regions.Count;
                Func<int, IEnumerable<int>> baseOf = i => BaseZones(st.Level, g.Regions[i]);
                st.Zone = new int[n];
                for (int i = 0; i < n; i++) st.Zone[i] = AutoZones.Majority(baseOf(i));
                var members = Enumerable.Range(0, n).GroupBy(i => comp[i]).ToDictionary(x => x.Key, x => x.ToList());
                foreach (var grp in members)
                {
                    var open = grp.Value.Where(i => st.Zone[i] <= ZoneSet.Unassigned).ToList();
                    if (open.Count == 0) continue;
                    int z = AutoZones.Majority(grp.Value.SelectMany(baseOf));
                    if (z <= ZoneSet.Unassigned)
                    {
                        // the same new area gets the same id in every state: keyed by the level and its first tile
                        int first = open.SelectMany(i => Spots(st.Level, g.Regions[i])).Select(t => t.Key).DefaultIfEmpty(-1).Min();
                        string fk = st.Level + "|0|" + first;
                        if (!freshIds.TryGetValue(fk, out z)) freshIds[fk] = z = nextFresh++;
                        st.NewNames[z] = string.Format("L{0} new area at ({1}, {2})", st.Level, first % 64, first / 64);
                    }
                    foreach (int i in open) st.Zone[i] = z;
                }
                var taken = new HashSet<int>(st.Zone);
                var into = new Dictionary<int, Dictionary<int, int>>();
                for (int i = 0; i < n; i++)
                    foreach (int v in baseOf(i))
                    {
                        if (v <= ZoneSet.Unassigned || taken.Contains(v)) continue;
                        Dictionary<int, int> c;
                        if (!into.TryGetValue(v, out c)) into[v] = c = new Dictionary<int, int>();
                        int k; c.TryGetValue(st.Zone[i], out k); c[st.Zone[i]] = k + 1;
                    }
                foreach (var kv in into)
                {
                    int to = kv.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key).First().Key;
                    st.Absorbed[kv.Key] = to;
                    st.Links.Add(new ZoneLink { From = kv.Key, To = to, Kind = "walk", Requires = "free", TwoWay = true, Level = st.Level, Notes = "merged by the change" });
                }
                Finish(st);
            }

            /// <summary>A rebuilt level's links, mechanisms, doorways and placements, from its regions' zones.</summary>
            void Finish(LevelState st)
            {
                var g = st.Graph;
                int n = g.Regions.Count;
                // the links
                var seenLinks = new HashSet<string>();
                Func<int, int> zoneOf = r => r >= 0 && r < n ? st.Zone[r] : 0;
                foreach (var e in g.Edges)
                {
                    if (e.Kind == "stairs") continue;
                    int a = zoneOf(e.From), z = zoneOf(e.To);
                    if (a == 0 || z == 0 || a == z) continue;
                    string req = AutoZones.Normalize(AutoZones.Simplify(RegionTerm.Replace(e.Requires, mm => "zone:" + zoneOf(int.Parse(mm.Groups[1].Value))), a));
                    string k = a + ">" + z + "|" + e.Kind + "|" + req;
                    if (!seenLinks.Add(k)) continue;
                    st.Links.Add(new ZoneLink { From = a, To = z, Kind = e.Kind, Requires = req, Level = st.Level, X = e.X, Y = e.Y, Notes = e.Notes, Confidence = e.Kind == "jump" || req.Contains("flag:var") ? "todo" : "certain" });
                }
                foreach (var m in g.Mechanisms) st.Mechs.Add(new KeyValuePair<NavMechanism, Requirement>(m, Req(When(m, zoneOf))));
                foreach (var grp in Enumerable.Range(0, n).GroupBy(i => st.Zone[i])) if (grp.All(i => g.Regions[i].KeepApart)) doorways.Add(grp.Key);
                foreach (var kv in spot)
                {
                    if (kv.Value[0] != st.Level) continue;
                    int r = g.NearestRegion(st.Level, kv.Value[1], kv.Value[2], 2, true);
                    if (r < 0) continue;
                    if (st.Zone[r] != kv.Key.Zone) st.PlacementZone[kv.Key] = st.Zone[r];
                }
            }

            string ZoneName(int z) { var zz = zs.GetZone(z); return zz == null ? "zone " + z : zz.Name; }

            // ---- the solve

            sealed class Ctx : IRequirementContext, INowContext
            {
                public SolverOptions Options { get; set; }
                public HashSet<string> Tokens = new HashSet<string>();
                public HashSet<int> Reach = new HashSet<int>();
                /// <summary>Zones reached before (the return check): their levers were pulled and stay so.</summary>
                public HashSet<int> Before;
                /// <summary>Zones whose levers and buttons do not count (the safe solve: the inside of a one-way trap).</summary>
                public HashSet<int> Hidden;
                /// <summary>The single-use sources (once:X) that count just now; null: all of them.</summary>
                public HashSet<string> OnceAllowed;
                /// <summary>Tokens taken as not held just now (to see whether a requirement can do without them); null: none.</summary>
                public HashSet<string> Without;
                public bool HasToken(string t)
                {
                    t = Requirement.Normalize(t);
                    if (Without != null && Without.Contains(t)) return false;
                    if (OnceAllowed != null && t.StartsWith("once:") && !OnceAllowed.Contains(t)) return false;
                    return Tokens.Contains(t);
                }
                public bool ZoneReachable(int z) { return (Hidden == null || !Hidden.Contains(z)) && (Reach.Contains(z) || (Before != null && Before.Contains(z))); }
                public bool ZoneNow(int z) { return Reach.Contains(z) && (Hidden == null || !Hidden.Contains(z)); }
                public int TalismanCount() { return Tokens.Count(t => t.StartsWith("talisman:")); }
                public int PlotClock() { return Requirement.PlotClock(Tokens); }
            }

            /// <summary>The links of the world in the given level states.</summary>
            List<ZoneLink> LinksOf(Dictionary<int, LevelState> cur, SolverOptions so)
            {
                var list = new List<ZoneLink>(globalLinks);
                for (int l = 1; l <= zs.Levels; l++)
                {
                    LevelState st;
                    if (cur.TryGetValue(l, out st)) list.AddRange(st.Links);
                    else { List<ZoneLink> bl; if (baseLinks.TryGetValue(l, out bl)) list.AddRange(bl); }
                }
                if (cuts.Count > 0) list.RemoveAll(k => cuts.Contains(k.From + ">" + k.To));
                foreach (var k in res.FactLinks)
                {
                    var f = res.Facts.Facts.FirstOrDefault(x => "facts: " + x.Id == k.Notes);
                    string req = k.Requires;
                    if (f != null && f.Option.Length > 0 && !so.OptionsOn.Contains(f.Option))
                    {
                        if (string.IsNullOrWhiteSpace(f.Otherwise)) continue;
                        req = f.Otherwise;
                    }
                    list.Add(new ZoneLink { Id = k.Id, From = k.From, To = k.To, Kind = k.Kind, Requires = req, TwoWay = k.TwoWay, Notes = k.Notes, Level = k.Level, X = k.X, Y = k.Y, Confidence = k.Confidence, Once = k.Once });
                }
                return list;
            }

            /// <summary>The links of a run: the world's (LinksOf) and, once the player has left a shut start world, the gem's facets into it with its fused key gem.</summary>
            List<ZoneLink> RunLinks(Dictionary<int, LevelState> cur, SolverOptions so, List<ZoneLink> extra)
            {
                var list = LinksOf(cur, so);
                if (extra != null) list.AddRange(extra);
                return list;
            }

            readonly Dictionary<int, int> zoneLevel = new Dictionary<int, int>();

            /// <summary>The level a zone lies on (its first, for a zone over several), a zone split off by a change too; 0 when not known.</summary>
            int ZoneLevel(int z)
            {
                int l;
                if (zoneLevel.TryGetValue(z, out l)) return l;
                l = 0;
                if (z >= FreshZoneIds) { foreach (var kv in freshIds) if (kv.Value == z) { int.TryParse(kv.Key.Split('|')[0], out l); break; } }
                else if (z > ZoneSet.Unassigned) { var ls = zs.LevelsOf(z); l = ls.Count > 0 ? ls[0] : 0; }
                if (l > 0 || z < FreshZoneIds) zoneLevel[z] = l;
                return l;
            }

            /// <summary>
            /// UW2: the levels of the world a start zone lies in when the blackrock gem keeps that world
            /// shut at the start, else null (UW1, the castle, the Prison Tower: the gem shows its facet
            /// below plot 4, every other turn). The facet of each world and the plot that lets the gem
            /// show it are StateRules.GemWorlds (event/WORLDEV.C black_gem_trip, black_gem_rotate); a
            /// fused key gem opens its facet for good, gem n lighting facet n - 1 with gems 6 and 7
            /// swapped (obj/USEITEMS.C UseKeyGem: the Pits are facet 5 and gem 7, the Tomb facet 6 and
            /// gem 6).
            /// </summary>
            HashSet<int> SealedWorld(int start, out int gem, out string opens)
            {
                gem = 0; opens = "";
                if (s == null || !s.IsUw2 || b.Nav == null || b.Auto == null) return null;
                int level = ZoneLevel(start);
                if (level <= 8 || level > 72) return null;
                int world = (level - 1) / 8;
                for (int f = 0; f < StateRules.GemWorlds.Length; f++)
                {
                    var w = StateRules.GemWorlds[f];
                    if ((w[0] - 1) / 8 != world) continue;
                    if (w[3] <= 0) return null;
                    gem = f == 5 ? 7 : f == 6 ? 6 : f + 1;
                    opens = string.Format("the castle plot at {0} or this world's key gem ({1}) fused", w[3], gem);
                    var set = new HashSet<int>();
                    for (int l = world * 8 + 1; l <= world * 8 + 8; l++) set.Add(l);
                    return set;
                }
                return null;
            }

            /// <summary>
            /// The blackrock gem's facets into a shut start world (links whose notes name the gem and
            /// that lead onto its levels) again, with the world's own key gem treated by Nystul and fused
            /// in place of the castle plot (the facts' gems_fused: item:key_gem_N &amp; npc:nystul).
            /// </summary>
            List<ZoneLink> FusedGemLinks(List<ZoneLink> links, HashSet<int> levels, int gem)
            {
                var list = new List<ZoneLink>();
                if (gem <= 0) return list;
                var plot = new Regex(@"\bplot:\d+\b");
                foreach (var k in links)
                {
                    if (!(k.Notes ?? "").StartsWith("the blackrock gem") || !levels.Contains(ZoneLevel(k.To)) || !plot.IsMatch(k.Requires ?? "")) continue;
                    string req = plot.Replace(k.Requires, "(item:key_gem_" + gem + " & npc:nystul)");
                    list.Add(new ZoneLink { Id = k.Id, From = k.From, To = k.To, Kind = k.Kind, Requires = req, TwoWay = false, Notes = k.Notes + ", lit by its fused key gem " + gem, Level = k.Level, X = k.X, Y = k.Y, Confidence = k.Confidence });
                }
                return list;
            }

            int PlacementZone(Placement p, Dictionary<int, LevelState> cur)
            {
                int[] t;
                LevelState st;
                if (spot.TryGetValue(p, out t) && cur.TryGetValue(t[0], out st))
                {
                    int z;
                    if (st.PlacementZone.TryGetValue(p, out z)) return z;
                }
                else if (extraSpot.TryGetValue(p, out t) && cur.TryGetValue(t[0], out st)) return ZoneOfTile(st, t, p.Zone);
                return p.Zone;
            }

            bool WarpHolds(Fact f, Ctx ctx, Dictionary<int, LevelState> cur)
            {
                if (f.Option.Length > 0 && !ctx.Options.OptionsOn.Contains(f.Option)) return false;
                if (f.FromLevel > 0 && !ctx.Reach.Any(z => zs.LevelsOf(z).Contains(f.FromLevel))) return false;
                if (f.FromAbove > 0 && !ctx.Reach.Any(z => WarpFrom(f, z))) return false;
                return Req(WarpRequires(f, ctx)).Eval(ctx);
            }

            /// <summary>Whether a warp can be made from a zone: one of its level (FromLevel), or of a level above FromAbove.</summary>
            bool WarpFrom(Fact f, int z)
            {
                if (f.FromLevel <= 0 && f.FromAbove <= 0) return true;
                int l = ZoneLevel(z);
                if (f.FromLevel > 0 && l != f.FromLevel && !zs.LevelsOf(z).Contains(f.FromLevel)) return false;
                return f.FromAbove <= 0 || l > f.FromAbove;
            }

            int WarpTarget(Fact f)
            {
                if (f.To == null || f.To.IsPlanted) return 0;
                LevelModel m = null;
                var t = Tile(f.To, l => { if (m == null || m.Level != l) { try { m = b.WalledLevel(l); } catch (Exception) { m = null; } } return m; });
                return ZoneAt(t);
            }

            /// <summary>The forward search with the world changing; banned zones grant nothing (the safe solve).</summary>
            public TimelineSolve Run(SolverOptions so, AbilityOptions opt, string label, HashSet<int> banned)
            {
                var sv = new TimelineSolve { Label = label, Options = so };
                var ctx = new Ctx { Options = so, Hidden = banned };
                foreach (var t in so.StartTokens) ctx.Tokens.Add(Requirement.Normalize(t));
                // single-use sources (scrolls, potions): each placement is one use, and each way, item or change that
                // only such a source opens takes one (a way crossed again is not counted: the logic's players come back by another way or keep one in hand)
                var onceSeen = new HashSet<Placement>();
                var onceHave = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var onceUsed = new Dictionary<string, int>();
                Func<string, bool> left = tok => { int h, u; onceHave.TryGetValue(tok, out h); onceUsed.TryGetValue(tok, out u); return h > u; };
                Action<string> spend = tok => { int u; onceUsed.TryGetValue(tok, out u); onceUsed[tok] = u + 1; sv.OnceSpent.Add(tok); };
                var none = new HashSet<string>();
                Func<Requirement, bool> holds = r =>
                {
                    if (!so.SingleUseItems || !onceHave.Keys.Any(k => k.StartsWith("once:"))) return r.Eval(ctx);
                    try
                    {
                        ctx.OnceAllowed = none;
                        if (r.Eval(ctx)) return true;       // something lasting does it
                        var avail = onceHave.Keys.Where(k => k.StartsWith("once:") && left(k)).ToList();
                        foreach (var tok in avail)
                        {
                            ctx.OnceAllowed = new HashSet<string> { tok };
                            if (r.Eval(ctx)) { spend(tok); return true; }
                        }
                        // several kinds together: those it does not hold without
                        ctx.OnceAllowed = new HashSet<string>(avail);
                        if (avail.Count < 2 || !r.Eval(ctx)) return false;
                        var need = new List<string>();
                        foreach (var tok in avail)
                        {
                            ctx.OnceAllowed = new HashSet<string>(avail.Where(x => x != tok));
                            if (!r.Eval(ctx)) need.Add(tok);
                        }
                        foreach (var tok in need.Count > 0 ? need : avail) spend(tok);
                        return true;
                    }
                    finally { ctx.OnceAllowed = null; }
                };
                // what a step hands over (a fact's usesUp: food for Zak, food for Marrowsuck): each source of such a token is one piece,
                // counted as the single-use sources are, and a step that cannot do without the token takes one; with none left it waits for another
                var handed = new HashSet<string>(Placements.Where(p => p.UsesUp != null).SelectMany(p => p.UsesUp).Select(Requirement.Normalize));
                Func<Placement, Requirement, List<string>> handsOver = (p, r) =>
                {
                    var l = new List<string>();
                    if (p.UsesUp == null) return l;
                    foreach (var g in p.UsesUp)
                    {
                        try { ctx.Without = new HashSet<string> { g }; if (!r.Eval(ctx)) l.Add(g); }
                        finally { ctx.Without = null; }
                    }
                    return l;
                };
                var cur = new Dictionary<int, LevelState>();
                var applied = new Dictionary<int, List<string>>();
                var fired = new HashSet<string>();
                int start = zs.StartZone;
                // what the build put in the start zone to mean "anywhere" (spells, crafts, UW2's state): granted wherever the player is,
                // also once the start zone is out of reach (a start left by a one-way way: the random start)
                var factById = res.Facts.Facts.Where(f => f.Id.Length > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
                var everywhere = new HashSet<Placement>(Placements.Where(p => p.Zone == start && Timeline.Everywhere(p, id => { Fact f; return factById.TryGetValue(id, out f) ? f : null; })));
                ctx.Reach.Add(start);
                sv.ZoneSphere[start] = 0;
                sv.Events.Add(new TimelineEvent { Kind = "zone", Zone = start });
                // the single-use sources a step used up: those spent since the count n
                Action<TimelineEvent, int> spentSince = (ev, n) => { for (int i = n; i < sv.OnceSpent.Count; i++) ev.Spent.Add(sv.OnceSpent[i]); };
                var warpTo = res.Warps.ToDictionary(f => f, f => WarpTarget(f));
                foreach (var f in res.Warps)
                {
                    if (f.To == null || f.To.IsPlanted || (f.Option.Length > 0 && !so.OptionsOn.Contains(f.Option))) continue;
                    if (warpTo[f] <= ZoneSet.Unassigned) { sv.Warnings.Add("Facts: the warp " + f.Id + " leads to " + f.To + ", where no zone lies; it is not counted."); continue; }
                    // a warp that asks for nothing held (dying, UW2) is a way the walkthrough may take between steps
                    string wr = (f.Requires ?? "").Trim();
                    if (wr.Length == 0 || wr == "free") sv.FreeWarps.Add(new KeyValuePair<Fact, int>(f, warpTo[f]));
                }
                var counted = new HashSet<Placement>();
                var tally = new Dictionary<string, int>();
                var sphere = new TimelineSphere { Index = 0 };
                sphere.Zones.Add(start);
                List<ZoneLink> links = LinksOf(cur, so);
                // a new character (level 1) casts from runes only up to so.NewCharacterCircle (combat/RUNES.C player_cast: level at least
                // 2 * circle - 1): a spell above it does not count until the player has reached a zone outside the start's free area
                foreach (var p in Placements) Abilities.CircleOf(s, p);
                bool early = so.AbilitySources && so.Magic && so.NewCharacterCircle < int.MaxValue && Placements.Any(p => p.Circle > so.NewCharacterCircle);
                if (early) sv.FreeArea = FreeArea(start, links, so);
                // UW2: a start in a world the blackrock gem keeps shut. First all that can be had without leaving that world (no way
                // out of its levels is taken), then the player leaves for the castle, and the world's zones count again only once a way
                // back into them holds (the gem's facet at the castle plot that shows it, or with the world's key gem fused)
                int sealedGem;
                string sealedOpens;
                var sealedLevels = SealedWorld(start, out sealedGem, out sealedOpens);
                bool sealedPhase = sealedLevels != null, leaveNow = false;
                List<ZoneLink> fusedLinks = null;
                sv.sealedStart = sealedLevels;
                // ways the game allows only once (ZoneLink.Once): crossed, they are gone; the zone they led to is taken in on arrival,
                // walked out of in the next round, and then counts only by another way (zone -> the sphere it was come to by such a way)
                var onceUsedLinks = new HashSet<string>();
                var onceVisit = new Dictionary<int, int>();
                Func<ZoneLink, string> onceKey = k => k.Notes + "|" + k.From + ">" + k.To;
                const int MaxRounds = 400;
                for (int round = 0; round <= MaxRounds; round++)
                {
                    if (round == MaxRounds) { sv.Warnings.Add("Stopped after " + MaxRounds + " rounds: the solve is not complete."); break; }
                    bool any = false, more = false;
                    if (round > 0 && leaveNow)
                    {
                        // the shut world is done: leave it by a way out that holds (to the castle when one does, else to the zone of
                        // another world the first such way leads to), and stand there alone
                        leaveNow = false;
                        sphere = new TimelineSphere { Index = sv.Spheres.Count };
                        ZoneLink exit = null;
                        int exitFrom = -1, exitTo = -1, x0 = sv.OnceSpent.Count;
                        bool exitCastle = false;
                        // a warp that holds from the world (dying outside Britannia) is a way out as well, taken only when no
                        // way on foot leads as far (to the castle, or else out of the world at all)
                        var ways = links.OrderBy(k => k.To).ThenBy(k => k.From).ToList();
                        var wayWarp = new Dictionary<ZoneLink, Fact>();
                        foreach (var f in res.Warps)
                        {
                            int z = warpTo[f];
                            if (z <= ZoneSet.Unassigned || !WarpHolds(f, ctx, cur)) continue;
                            int a = ctx.Reach.Where(x => WarpFrom(f, x)).DefaultIfEmpty(-1).Min();
                            if (a < 0) continue;
                            var wk = new ZoneLink { From = a, To = z, Kind = "warp", Requires = WarpRequires(f, ctx), Notes = "facts: " + f.Id };
                            wayWarp[wk] = f;
                            ways.Add(wk);
                        }
                        foreach (var k in ways)
                        {
                            if (k.Confidence == "todo" && !so.TrustTodoLinks) continue;
                            if (k.Once && onceUsedLinks.Contains(onceKey(k))) continue;
                            foreach (bool rev in new[] { false, true })
                            {
                                if (rev && !k.TwoWay) continue;
                                int a = rev ? k.To : k.From, z = rev ? k.From : k.To;
                                int zl = ZoneLevel(z);
                                if (!ctx.Reach.Contains(a) || zl < 1 || sealedLevels.Contains(zl)) continue;
                                bool castle = zl <= 8;
                                if (exit != null && ((exitCastle && !castle) || (exitCastle == castle && (z >= exitTo || wayWarp.ContainsKey(k))))) continue;
                                if (!Req(k.Requires).Eval(ctx)) continue;
                                exit = k; exitFrom = a; exitTo = z; exitCastle = castle;
                            }
                        }
                        if (exit == null) { sv.Warnings.Add("The solve stands in " + KeyShuffle.Uw2WorldName(sealedLevels.Min()) + ", which the blackrock gem keeps shut, and no way out of it holds with what that world gives."); break; }
                        holds(Req(exit.Requires));
                        if (exit.Once) onceUsedLinks.Add(onceKey(exit));
                        ctx.Reach = new HashSet<int> { exitTo };
                        onceVisit.Clear();
                        if (!sv.ZoneSphere.ContainsKey(exitTo)) { sv.ZoneSphere[exitTo] = sphere.Index; sphere.Zones.Add(exitTo); }
                        if (!sv.FirstLink.ContainsKey(exitTo)) sv.FirstLink[exitTo] = exit;
                        var lev = new TimelineEvent { Sphere = sphere.Index, Kind = "zone", Zone = exitTo, From = exitFrom, Link = exit, Requires = exit.Requires ?? "" };
                        Fact exitWarp;
                        if (wayWarp.TryGetValue(exit, out exitWarp)) lev.Warp = exitWarp;
                        spentSince(lev, x0);
                        List<string> was0;
                        if (exit.Level > 0 && applied.TryGetValue(exit.Level, out was0)) lev.Changes.AddRange(was0);
                        sv.Leaves.Add(new WorldLeave { Levels = sealedLevels, Sphere = sphere.Index, Event = sv.Events.Count, To = exitTo, Opens = sealedOpens });
                        sv.Events.Add(lev);
                        if (early && !sv.FreeArea.Contains(exitTo)) { early = false; sv.FreeAreaLeft = sphere.Index; }
                        // the world's own key gem, treated by Nystul and fused, opens its facet for good (event/WORLDEV.C black_gem_trip, obj/USEITEMS.C UseKeyGem)
                        fusedLinks = (fusedLinks ?? new List<ZoneLink>()).Concat(FusedGemLinks(links, sealedLevels, sealedGem)).ToList();
                        // come out in another shut world (the Void from the Pits, the Academy from the Void): that one is taken the same way
                        sealedLevels = SealedWorld(exitTo, out sealedGem, out sealedOpens);
                        sealedPhase = sealedLevels != null;
                        links = RunLinks(cur, so, fusedLinks);
                        any = true;
                    }
                    else if (round > 0)
                    {
                        sphere = new TimelineSphere { Index = sv.Spheres.Count };
                        var newly = new List<int>();
                        foreach (var k in links)
                        {
                            if (k.Confidence == "todo" && !so.TrustTodoLinks) continue;
                            bool fwd = ctx.Reach.Contains(k.From) && !ctx.Reach.Contains(k.To) && !newly.Contains(k.To);
                            bool back = k.TwoWay && ctx.Reach.Contains(k.To) && !ctx.Reach.Contains(k.From) && !newly.Contains(k.From);
                            if (!fwd && !back) continue;
                            // the shut world's first part: no way out of its levels
                            if (sealedPhase && !sealedLevels.Contains(ZoneLevel(fwd ? k.To : k.From))) continue;
                            if (k.Once && onceUsedLinks.Contains(onceKey(k))) continue;
                            int n0 = sv.OnceSpent.Count;
                            if (!holds(Req(k.Requires))) continue;
                            int z = fwd ? k.To : k.From;
                            if (k.Once) { onceUsedLinks.Add(onceKey(k)); onceVisit[z] = sphere.Index; }
                            else onceVisit.Remove(z);
                            newly.Add(z);
                            if (!sv.FirstLink.ContainsKey(z))
                            {
                                sv.FirstLink[z] = k;
                                var ev = new TimelineEvent { Sphere = sphere.Index, Kind = "zone", Zone = z, From = fwd ? k.From : k.To, Link = k, Requires = k.Requires ?? "" };
                                spentSince(ev, n0);
                                List<string> was;
                                if (k.Level > 0 && applied.TryGetValue(k.Level, out was)) ev.Changes.AddRange(was);
                                sv.Events.Add(ev);
                            }
                        }
                        foreach (var f in res.Warps)
                        {
                            int z = warpTo[f];
                            if (z <= ZoneSet.Unassigned || ctx.Reach.Contains(z) || newly.Contains(z) || !WarpHolds(f, ctx, cur)) continue;
                            if (sealedPhase && !sealedLevels.Contains(ZoneLevel(z))) continue;
                            // a warp made with a scroll or a potion uses it up, as a way does
                            int w0 = sv.OnceSpent.Count;
                            string wreq = WarpRequires(f, ctx);
                            if (!holds(Req(wreq))) continue;
                            newly.Add(z);
                            if (!sv.FirstLink.ContainsKey(z))
                            {
                                sv.FirstLink[z] = new ZoneLink { From = 0, To = z, Kind = "warp", Requires = wreq, Notes = "facts: " + f.Id };
                                var wev = new TimelineEvent { Sphere = sphere.Index, Kind = "zone", Zone = z, Link = sv.FirstLink[z], Warp = f, Requires = wreq ?? "" };
                                spentSince(wev, w0);
                                sv.Events.Add(wev);
                            }
                        }
                        // a zone reached again after a change cut it off keeps the sphere it first came in
                        foreach (int z in newly) { ctx.Reach.Add(z); if (!sv.ZoneSphere.ContainsKey(z)) { sv.ZoneSphere[z] = sphere.Index; sphere.Zones.Add(z); } }
                        // a zone come to by a way taken once, in the round before: walked out of now; with no other way into it that holds, it
                        // leaves the reach (what it holds was taken on arrival) until another way leads in
                        foreach (var kv in onceVisit.Where(v => v.Value < sphere.Index).ToList())
                        {
                            onceVisit.Remove(kv.Key);
                            int t = kv.Key;
                            bool other = links.Any(k => !k.Once && (k.Confidence != "todo" || so.TrustTodoLinks) &&
                                ((k.To == t && k.From != t && ctx.Reach.Contains(k.From)) || (k.TwoWay && k.From == t && k.To != t && ctx.Reach.Contains(k.To))) && Req(k.Requires).Eval(ctx));
                            if (!other) ctx.Reach.Remove(t);
                        }
                        if (early && newly.Any(z => !sv.FreeArea.Contains(z))) { early = false; sv.FreeAreaLeft = sphere.Index; }
                        any = newly.Count > 0;
                    }
                    // grant what the reached zones hold, then set off what can go off
                    bool gained = true;
                    var toApply = new Dictionary<int, List<string>>();
                    // where the player stands when a change that takes ways away goes off (level, x, y), and the zones of those squares before it
                    var stood = new List<List<int[]>>();
                    var stoodZones = new HashSet<int>();
                    Func<Func<NavMechanism, string, bool>, bool> fire = which =>
                    {
                        bool some = false;
                        for (int l = 1; l <= zs.Levels; l++)
                        {
                            LevelState st;
                            List<KeyValuePair<NavMechanism, Requirement>> ms;
                            if (cur.TryGetValue(l, out st)) ms = st.Mechs;
                            else if (!baseMechs.TryGetValue(l, out ms)) continue;
                            foreach (var kv in ms)
                            {
                                string f = Requirement.Normalize(kv.Key.Flag);
                                int n0 = sv.OnceSpent.Count;
                                if (fired.Contains(f) || !which(kv.Key, f) || !holds(kv.Value)) continue;
                                var fev = new TimelineEvent { Sphere = sphere.Index, Kind = "flag", Token = f, Mechanism = kv.Key, Requires = kv.Value.ToString() };
                                spentSince(fev, n0);
                                sv.Events.Add(fev);
                                if (Closing.Contains(f)) stood.AddRange(StandingTiles(kv.Key, cur, ctx, stoodZones));
                                fired.Add(f);
                                sv.Fired.Add(f);
                                sphere.SetOff.Add(f);
                                if (!ctx.Tokens.Contains(f)) { ctx.Tokens.Add(f); sv.TokenSphere[f] = sphere.Index; }
                                some = true;
                                if (Applicable.Contains(f))
                                {
                                    List<string> a;
                                    if (!toApply.TryGetValue(l, out a)) toApply[l] = a = new List<string>();
                                    a.Add(f);
                                }
                            }
                        }
                        return some;
                    };
                    Action apply = () =>
                    {
                        // the world before the changes, for the cut-off check: what the player could still get to from where he stood
                        var linksBefore = links;
                        var curBefore = stood.Count > 0 ? new Dictionary<int, LevelState>(cur) : null;
                        foreach (var kv in toApply)
                        {
                            List<string> a;
                            if (!applied.TryGetValue(kv.Key, out a)) applied[kv.Key] = a = new List<string>();
                            a.AddRange(kv.Value);
                            var st = Rebuild(kv.Key, a);
                            cur[kv.Key] = st;
                            foreach (var z in st.NewNames) sv.NewZones[z.Key] = z.Value;
                            sphere.Applied.AddRange(kv.Value);
                            sv.Applied.AddRange(kv.Value);
                        }
                        if (toApply.Count > 0) links = RunLinks(cur, so, fusedLinks);
                        if (stood.Count > 0) StillReached(sv, ctx, cur, links, stood, sphere, stoodZones, linksBefore, curBefore);
                        toApply.Clear();
                        stood.Clear();
                        stoodZones.Clear();
                    };
                    // what the player cannot avoid goes off before he takes anything: a square he walks onto that
                    // seals part of the zone behind it leaves the things there out of his reach
                    if (fire((m, f) => m.Unavoidable && Closing.Contains(f))) apply();
                    while (gained)
                    {
                        gained = false;
                        foreach (var p in Placements)
                        {
                            string t = Requirement.Normalize(p.Grants);
                            if (so.SingleUseOnly != null && t.StartsWith("once:") && !so.SingleUseOnly.Contains(t.Substring(5))) continue;     // a single-use thing that is not to count
                            bool counts = t.Length == 0 && !string.IsNullOrEmpty(p.Counts);
                            bool once = (so.SingleUseItems && t.StartsWith("once:")) || handed.Contains(t);
                            if (counts ? counted.Contains(p) : once ? onceSeen.Contains(p) : (t.Length == 0 || ctx.Tokens.Contains(t))) continue;
                            if (early && p.Circle > so.NewCharacterCircle) continue;       // a spell a new character cannot cast yet
                            int z = PlacementZone(p, cur);
                            if (!(ctx.Reach.Contains(z) || (z == start && everywhere.Contains(p))) || (banned != null && banned.Contains(z))) continue;
                            // a source the game takes away whatever the player does: only inside its window, as the solve stands now
                            if (SolverOptions.Gone(p, ctx, Req)) continue;
                            int n0 = sv.OnceSpent.Count;
                            List<string> over = null;
                            string preq = PlaceRequires(p);
                            if (p.UsesUp != null && !string.IsNullOrEmpty(preq))
                            {
                                over = handsOver(p, Req(preq));
                                if (over.Any(g => !left(g))) continue;      // nothing left to hand over: another piece must be found first
                            }
                            if (!string.IsNullOrEmpty(preq) && !holds(Req(preq))) continue;
                            if (over != null) foreach (var g in over) spend(g);
                            var pev = new TimelineEvent { Sphere = sphere.Index, Kind = "token", Zone = z, Token = t, Placement = p, Requires = preq ?? "" };
                            spentSince(pev, n0);
                            if (once)
                            {
                                // one more use of this kind; the token itself comes with the first
                                onceSeen.Add(p);
                                int h; onceHave.TryGetValue(t, out h); onceHave[t] = h + 1;
                                more = true;
                                if (ctx.Tokens.Contains(t)) { pev.Kind = "use"; sv.Events.Add(pev); continue; }
                            }
                            if (counts)
                            {
                                // a count fact (Shak's 20 gold): the quantities add up; the token comes once the total is reached
                                counted.Add(p);
                                int n; tally.TryGetValue(p.Counts, out n); tally[p.Counts] = n += p.Quantity;
                                Fact cf;
                                pev.Kind = "count";
                                pev.Token = p.Counts;
                                sv.Events.Add(pev);
                                if (!countFacts.TryGetValue(p.Counts, out cf) || n < cf.AtLeast) continue;
                                t = Requirement.Normalize(cf.Grants);
                                if (ctx.Tokens.Contains(t)) continue;
                                pev = new TimelineEvent { Sphere = sphere.Index, Kind = "token", Zone = z, Token = t, Placement = p, Requires = "count:" + p.Counts };
                            }
                            sv.Events.Add(pev);
                            ctx.Tokens.Add(t);
                            sphere.Tokens.Add(t);
                            sphere.TokenSources.Add(t + " (zone " + z + (string.IsNullOrEmpty(p.Requires) ? "" : ", with " + p.Requires) + ")");
                            sv.TokenSphere[t] = sphere.Index;
                            gained = true;
                        }
                        if (fire((m, f) => true)) gained = true;
                    }
                    apply();
                    foreach (var kv in cur) sphere.State[kv.Key] = kv.Value.Key;
                    if (round > 0 && !any && sphere.Tokens.Count == 0 && sphere.SetOff.Count == 0)
                    {
                        if (more) continue;     // only another use of a single-use source came: try the ways again
                        // all of the shut world that can be had from inside it is had: leave it in the next round
                        if (sealedPhase) { sealedPhase = false; leaveNow = true; continue; }
                        break;
                    }
                    foreach (var kv in onceHave) { int u; onceUsed.TryGetValue(kv.Key, out u); sphere.OnceLeft[kv.Key] = kv.Value - u; }
                    sphere.Links = links;
                    sv.Spheres.Add(sphere);
                }
                // the goal: the goal zone (level 9), or a square that ends the game
                // the goal zone (level 9's arrival) with the goal fact's requirement, or a square that ends the game:
                // stepping there wins at once (game/SKILLS.C check_victory), so it needs only the goal options' extras (option:require_orb)
                // (reached at some time: a change that cut the way back afterwards does not undo the end of the game)
                bool atGoal = sv.ZoneSphere.ContainsKey(res.GoalZone);
                var end = res.Ends.FirstOrDefault(e => sv.ZoneSphere.ContainsKey(EndZoneNow(e, cur)));
                var greq = Req(res.GoalFor(so));
                var ereq = Req(res.EndFor(so));
                sv.GoalZoneReached = atGoal || end.Key != null;
                if (atGoal && greq.Eval(ctx)) { sv.GoalReached = true; sv.GoalBy = "the goal zone"; sv.GoalAt = res.GoalZone; }
                else if (end.Key != null && ereq.Eval(ctx)) { sv.GoalReached = true; sv.GoalBy = "the end of the game at " + end.Key; sv.GoalAt = EndZoneNow(end, cur); }
                else sv.GoalReached = false;
                if (!sv.GoalZoneReached) sv.GoalMissing.Add("the goal zone " + ZoneName(res.GoalZone) + (res.Ends.Count > 0 ? " or an end of the game (" + string.Join(", ", res.Ends.Select(e => e.Key.ToString())) + ")" : ""));
                if (!sv.GoalReached) (atGoal || end.Key == null ? greq : ereq).Missing(ctx, sv.GoalMissing);
                // blocked links out of the reached part
                foreach (var k in links)
                {
                    foreach (bool rev in new[] { false, true })
                    {
                        if (rev && !k.TwoWay) continue;
                        int a = rev ? k.To : k.From, z = rev ? k.From : k.To;
                        if (!ctx.Reach.Contains(a) || ctx.Reach.Contains(z)) continue;
                        var bl = new BlockedLink { Link = k, Reverse = rev, FromZone = a, ToZone = z };
                        Req(k.Requires).Missing(ctx, bl.Missing);
                        if (bl.Missing.Count == 0) bl.Missing.Add(k.Confidence == "todo" ? "(todo link, not trusted)" : "(?)");
                        sv.Blocked.Add(bl);
                    }
                }
                // the zones cut off that the solve got to again afterwards
                foreach (var c in sv.CutOff) foreach (int z in c.Value) if (ctx.Reach.Contains(z)) sv.CutOffRegained.Add(z);
                lastWorld = cur;
                lastLinks = links;
                return sv;
            }

            Dictionary<int, LevelState> lastWorld;
            List<ZoneLink> lastLinks;

            /// <summary>
            /// The start's free area: the start zone and every zone it reaches over ways whose
            /// requirement holds with nothing held (walking, a free jump or drop, a door that opens or
            /// is bashed, a look that any Search finds), in the world as it starts. While the player
            /// has reached nothing outside it, he is taken to be the new character the game made
            /// (SolverOptions.NewCharacterCircle).
            /// </summary>
            HashSet<int> FreeArea(int start, List<ZoneLink> links, SolverOptions so)
            {
                var ctx = new Ctx { Options = so };
                ctx.Reach.Add(start);
                for (bool grew = true; grew;)
                {
                    grew = false;
                    foreach (var k in links)
                    {
                        if (k.Confidence == "todo" && !so.TrustTodoLinks) continue;
                        bool fwd = ctx.Reach.Contains(k.From) && !ctx.Reach.Contains(k.To);
                        bool back = k.TwoWay && ctx.Reach.Contains(k.To) && !ctx.Reach.Contains(k.From);
                        if ((fwd || back) && Req(k.Requires).Eval(ctx)) { ctx.Reach.Add(fwd ? k.To : k.From); grew = true; }
                    }
                }
                return ctx.Reach;
            }

            /// <summary>
            /// Where the player can stand when he sets a mechanism off: one square of each region its
            /// set-off names whose zone is reached, in the world as it is before the change; the
            /// zones of those regions (before the change) go into zones.
            /// </summary>
            List<List<int[]>> StandingTiles(NavMechanism m, Dictionary<int, LevelState> cur, Ctx ctx, HashSet<int> zones)
            {
                var list = new List<List<int[]>>();
                LevelState st;
                cur.TryGetValue(m.Level, out st);
                var g = st != null ? st.Graph : b.Auto.Graph;
                // the squares of each region, those nearest the trigger first (the change may turn some to rock)
                int tx = -1, ty = -1;
                var model = st != null ? st.Model : b.WalledLevel != null ? b.WalledLevel(m.Level) : null;
                if (model != null && m.Trigger > 0 && m.Trigger < model.Objects.Length) { tx = model.Objects[m.Trigger].TileX; ty = model.Objects[m.Trigger].TileY; }
                foreach (Match mm in RegionTerm.Matches(m.SetOff ?? ""))
                {
                    int r = int.Parse(mm.Groups[1].Value);
                    if (r < 0 || r >= g.Regions.Count || g.Regions[r].Tiles.Count == 0) continue;
                    int z = st != null ? st.Zone[r] : g.Regions[r].Zone;
                    if (!ctx.Reach.Contains(z)) continue;
                    zones.Add(z);
                    var tiles = g.Regions[r].Tiles.Select(t => new[] { m.Level, t % 64, t / 64 });
                    if (tx >= 0) tiles = tiles.OrderBy(t => Math.Max(Math.Abs(t[1] - tx), Math.Abs(t[2] - ty))).ThenBy(t => t[2] * 64 + t[1]);
                    list.Add(tiles.ToList());
                }
                return list;
            }

            /// <summary>
            /// After a change that takes ways away: the zones reached so far that the player can
            /// still get to, from where he stood when it went off (and from the start, once the
            /// sapling is planted), in the world as it is now. What he holds stays his; the zones
            /// he can no longer get to are no longer a place to go on from. Zones are only taken
            /// off here: new ones come in the next round, as always.
            /// The cut-off reported is narrower: only the zones the player could still get to from
            /// where he stood just before the change (the same search over the ways of the world
            /// before it, from the zones of the squares he stood on: stoodZones, linksBefore,
            /// curBefore) and cannot after it. A zone already out of his reach before (the start
            /// zone left by a one-way way, and what lies only behind it) was not cut off by this
            /// change, though it leaves the reach all the same.
            /// </summary>
            void StillReached(TimelineSolve sv, Ctx ctx, Dictionary<int, LevelState> cur, List<ZoneLink> links, List<List<int[]>> stood, TimelineSphere sphere,
                HashSet<int> stoodZones = null, List<ZoneLink> linksBefore = null, Dictionary<int, LevelState> curBefore = null)
            {
                var seeds = new HashSet<int>();
                foreach (var region in stood)
                    foreach (var t in region)
                    {
                        // the nearest square of the region that is still open
                        LevelState st;
                        if (!cur.TryGetValue(t[0], out st)) break;
                        int r = st.Graph.RegionOf(t[0], t[1], t[2]);
                        if (r < 0 || st.Zone[r] <= 0) continue;
                        seeds.Add(st.Zone[r]);
                        break;
                    }
                if (seeds.Count == 0)
                {
                    sv.Warnings.Add("Sphere " + sphere.Index + ": a change took ways away, but the square it was set off from is gone; the zones reached before are taken as still reached.");
                    return;
                }
                bool planted = plantedRequires != null && Req(plantedRequires).Eval(ctx);
                if (planted) seeds.Add(zs.StartZone);
                var old = ctx.Reach;
                var warps = res.Warps.Where(f => f.To != null && !f.To.IsPlanted).Select(f => new KeyValuePair<Fact, int>(f, WarpTarget(f))).Where(w => old.Contains(w.Value)).ToList();
                var now = ReachAmong(seeds, links, old, ctx, cur, warps);
                // what he could get to just before the change, from where he stood, in the world as it was then
                HashSet<int> before = null;
                if (stoodZones != null && stoodZones.Count > 0 && linksBefore != null)
                {
                    var from = new HashSet<int>(stoodZones.Where(old.Contains));
                    if (planted) from.Add(zs.StartZone);
                    before = ReachAmong(from, linksBefore, old, ctx, curBefore ?? cur, warps);
                }
                ctx.Reach = now;
                foreach (int z in seeds) if (!sv.ZoneSphere.ContainsKey(z)) { sv.ZoneSphere[z] = sphere.Index; sphere.Zones.Add(z); sv.Events.Add(new TimelineEvent { Sphere = sphere.Index, Kind = "zone", Zone = z }); }
                var lost = old.Where(z => !now.Contains(z) && (before == null || before.Contains(z))).OrderBy(z => z).ToList();
                if (lost.Count > 0) sv.CutOff.Add(new KeyValuePair<int, List<int>>(sphere.Index, lost));
            }

            /// <summary>
            /// The zones the player gets to from seeds over the given ways, among the zones of within
            /// (and the seeds): ways that do not hold yet are tried again whenever the reach grows
            /// (zone:N terms, warps from a level). ctx.Reach is the growing set while it runs, and is
            /// left as it was.
            /// </summary>
            HashSet<int> ReachAmong(HashSet<int> seeds, List<ZoneLink> links, HashSet<int> within, Ctx ctx, Dictionary<int, LevelState> cur, List<KeyValuePair<Fact, int>> warps)
            {
                var keep = ctx.Reach;
                var now = new HashSet<int>(seeds);
                ctx.Reach = now;
                try
                {
                    // the ways out of each zone, among the zones reached before
                    var outOf = new Dictionary<int, List<KeyValuePair<int, ZoneLink>>>();
                    Action<int, int, ZoneLink> way = (a, z, k) =>
                    {
                        if (!within.Contains(z) && !seeds.Contains(z)) return;
                        List<KeyValuePair<int, ZoneLink>> l;
                        if (!outOf.TryGetValue(a, out l)) outOf[a] = l = new List<KeyValuePair<int, ZoneLink>>();
                        l.Add(new KeyValuePair<int, ZoneLink>(z, k));
                    };
                    // a way taken only once is not a way back (ZoneLink.Once)
                    foreach (var k in links) { if (k.Once) continue; way(k.From, k.To, k); if (k.TwoWay) way(k.To, k.From, k); }
                    var waiting = new List<KeyValuePair<int, ZoneLink>>();
                    var queue = new Queue<int>(now);
                    while (true)
                    {
                        while (queue.Count > 0)
                        {
                            List<KeyValuePair<int, ZoneLink>> l;
                            if (!outOf.TryGetValue(queue.Dequeue(), out l)) continue;
                            foreach (var w in l)
                            {
                                if (now.Contains(w.Key)) continue;
                                if (Req(w.Value.Requires).Eval(ctx)) { now.Add(w.Key); queue.Enqueue(w.Key); }
                                else waiting.Add(w);
                            }
                        }
                        foreach (var w in waiting)
                            if (!now.Contains(w.Key) && Req(w.Value.Requires).Eval(ctx)) { now.Add(w.Key); queue.Enqueue(w.Key); }
                        foreach (var w in warps)
                            if (!now.Contains(w.Value) && WarpHolds(w.Key, ctx, cur)) { now.Add(w.Value); queue.Enqueue(w.Value); }
                        waiting.RemoveAll(w => now.Contains(w.Key));
                        if (queue.Count == 0) break;
                    }
                    return now;
                }
                finally { ctx.Reach = keep; }
            }

            /// <summary>
            /// The return check: from each zone reached, with what the player held when he got there
            /// (and what he finds on the way), in the world as it stood at the end of that sphere,
            /// can he get back to the start? If not, the zone is a one-way trap. Once the silver
            /// sapling's requirement holds (a warp fact to "planted"), the player plants it before
            /// going one way, so every zone reached later can return (death takes him back).
            /// When the walk back stalls, the changes the solve applied in the following spheres join
            /// in, one sphere at a time, while each of them only opens ways and its set-off holds for
            /// the walk (he can set it off himself from where he got to); the first that does not ends
            /// the walk. From a start other than the game's own, back may also mean a zone the start
            /// leads out into (ReRoot).
            /// </summary>
            public void ReturnCheck(TimelineSolve sv, SolverOptions so)
            {
                int start = zs.StartZone;
                var planted = plantedRequires == null ? null : Req(plantedRequires);
                var states = new Dictionary<string, List<ZoneLink>>();
                var warpTo = res.Warps.Where(f => f.To != null && !f.To.IsPlanted).ToDictionary(f => f, f => WarpTarget(f));
                var trapZones = new Dictionary<int, OneWayTrap>();
                // from zone z, reached in sphere sp: the zones he gets to, stopping once one of home is among them (then back is true)
                Func<int, int, ICollection<int>, Tuple<bool, HashSet<int>>> walk = (z, sp, home) =>
                {
                    var ctx = new Ctx { Options = so, Before = new HashSet<int>(sv.ZoneSphere.Where(x => x.Value < sp).Select(x => x.Key)) };
                    foreach (var t in sv.TokenSphere) if (t.Value < sp) ctx.Tokens.Add(t.Key);
                    if (so.SingleUseItems)
                    {
                        // of the single-use sources held, only those with a use left when he got here (and those he finds inside) get him back
                        ctx.OnceAllowed = new HashSet<string>();
                        if (sp > 0 && sp - 1 < sv.Spheres.Count) foreach (var kv2 in sv.Spheres[sp - 1].OnceLeft) if (kv2.Value > 0) ctx.OnceAllowed.Add(kv2.Key);
                    }
                    if (planted != null && planted.Eval(ctx)) return null;
                    // the world at the end of sphere sp
                    var st = sp < sv.Spheres.Count ? sv.Spheres[sp].State : new Dictionary<int, string>();
                    string key = string.Join(";", st.OrderBy(x => x.Key).Select(x => x.Value));
                    var cur = st.ToDictionary(x => x.Key, x => cache[x.Value]);
                    List<ZoneLink> links;
                    if (!states.TryGetValue(key, out links)) states[key] = links = LinksOf(cur, so);
                    ctx.Reach.Add(z);
                    bool back = false;
                    // stuck in the world at the end of sphere sp: the changes the solve applied in the next spheres count too, as long as
                    // each only opens ways (not Closing) and he can set it off from where the walk got to (a walk-on square in a zone
                    // reached, as the floor of the moonstone room opening after Gate Travel); the first one that does not ends it
                    for (int next = sp + 1; ; next++)
                    {
                        if (next > sp + 1)
                        {
                            if (back || next - 1 >= sv.Spheres.Count) break;
                            var sph = sv.Spheres[next - 1];
                            if (sph.Applied.Count == 0) continue;
                            bool opens = sph.Applied.All(f => !Closing.Contains(f) && sv.Events.Any(e => e.Kind == "flag" && e.Sphere == next - 1 && e.Token == f && Req(e.Requires).Eval(ctx)));
                            if (!opens) break;
                            string key2 = string.Join(";", sph.State.OrderBy(x => x.Key).Select(x => x.Value));
                            if (!states.TryGetValue(key2, out links)) states[key2] = links = LinksOf(sph.State.ToDictionary(x => x.Key, x => cache[x.Value]), so);
                            cur = sph.State.ToDictionary(x => x.Key, x => cache[x.Value]);
                        }
                        for (int round = 0; round <= 300 && !back; round++)
                        {
                            if (round == 300) { sv.Warnings.Add("The return check of zone " + z + " stopped after 300 rounds."); break; }
                            bool any = false;
                            foreach (var p in Placements)
                            {
                                string t = Requirement.Normalize(p.Grants);
                                bool onceHere = ctx.OnceAllowed != null && t.StartsWith("once:") && !ctx.OnceAllowed.Contains(t);
                                // a spell is cast wherever the player is, once he holds its runes (those placements lie in the start zone)
                                bool anywhere = t.StartsWith("cast:");
                                if (t.Length == 0 || (ctx.Tokens.Contains(t) && !onceHere) || (!anywhere && !ctx.Reach.Contains(PlacementZone(p, cur)))) continue;
                                // from a zone reached before the solve left the start's free area, a new character's casting
                                if (anywhere && sv.FreeArea != null && (sv.FreeAreaLeft < 0 || sp < sv.FreeAreaLeft) && p.Circle > so.NewCharacterCircle) continue;
                                if (!string.IsNullOrEmpty(p.Requires) && !Req(PlaceRequires(p)).Eval(ctx)) continue;
                                if (onceHere) ctx.OnceAllowed.Add(t);
                                ctx.Tokens.Add(t); any = true;
                            }
                            foreach (var k in links)
                            {
                                if (k.Once) continue;       // a way taken only once does not take him back
                                bool fwd = ctx.Reach.Contains(k.From) && !ctx.Reach.Contains(k.To);
                                bool rev = k.TwoWay && ctx.Reach.Contains(k.To) && !ctx.Reach.Contains(k.From);
                                if ((fwd || rev) && Req(k.Requires).Eval(ctx)) { ctx.Reach.Add(fwd ? k.To : k.From); any = true; }
                            }
                            foreach (var w in warpTo)
                                if (w.Value > ZoneSet.Unassigned && !ctx.Reach.Contains(w.Value) && WarpHolds(w.Key, ctx, null)) { ctx.Reach.Add(w.Value); any = true; }
                            if (home.Any(ctx.Reach.Contains)) back = true;
                            if (!any) break;
                        }
                    }
                    return Tuple.Create(back, ctx.Reach);
                };
                // every zone reached but the start (and the doorways), and what the walk back found
                var walks = new List<Tuple<int, int, Tuple<bool, HashSet<int>>>>();
                var home0 = new HashSet<int> { start };
                // a shut start world left for the castle (Run): a zone reached after the leaving is back at the castle zone it came out
                // in; the zones of the world's first part are back at the start, as for any start
                // (after several shut worlds, the zone he came out in from the last one left before it was reached)
                Func<int, bool> later = sp => sv.Leaves.Count > 0 && sp >= sv.Leaves[0].Sphere;
                Func<int, HashSet<int>> homeOf = sp => { var lv = sv.Leaves.LastOrDefault(x => x.Sphere <= sp); return lv == null ? home0 : new HashSet<int> { lv.To }; };
                var outs = new HashSet<int>(sv.Leaves.Select(x => x.To));
                foreach (var kv in sv.ZoneSphere.OrderBy(x => x.Value).ThenBy(x => x.Key))
                {
                    if (kv.Key == start || doorways.Contains(kv.Key) || outs.Contains(kv.Key)) continue;
                    walks.Add(Tuple.Create(kv.Key, kv.Value, walk(kv.Key, kv.Value, homeOf(kv.Value))));
                }
                // a start other than the game's own that the player leaves for good: back to where he came out counts as back (ReRoot);
                // of a shut start world, only over the world's first part
                var first = walks.Where(w => !later(w.Item2)).ToList();
                var exits = first.Any(w => w.Item3 != null && !w.Item3.Item1) ? ReRoot(sv, start, first.ToDictionary(w => w.Item1, w => w.Item2), new HashSet<int>(first.Where(w => w.Item3 != null && w.Item3.Item1).Select(w => w.Item1)), walk) : null;
                if (exits != null)
                {
                    sv.ReturnTo.AddRange(exits.OrderBy(x => x));
                    var home = new HashSet<int>(exits) { start };
                    walks = walks.Select(w => later(w.Item2) ? w : exits.Contains(w.Item1) ? Tuple.Create(w.Item1, w.Item2, Tuple.Create(true, new HashSet<int>())) : Tuple.Create(w.Item1, w.Item2, walk(w.Item1, w.Item2, home))).ToList();
                }
                foreach (var w in walks)
                {
                    int z = w.Item1, sp = w.Item2;
                    if (w.Item3 == null) { sv.SavedBySapling++; continue; }
                    if (w.Item3.Item1) continue;
                    var trap = new OneWayTrap { Zone = z, Sphere = sp };
                    ZoneLink entry;
                    if (sv.FirstLink.TryGetValue(z, out entry)) trap.Entry = entry;
                    trap.Inside.AddRange(w.Item3.Item2.Where(x => x != z).OrderBy(x => x));
                    trapZones[z] = trap;
                    sv.Traps.Add(trap);
                }
                // what the traps hold that nothing outside a trap gives (where each thing lies now, after the changes).
                // Whether it strands the player is settled by Timeline.Solve: the solve again without the trap.
                var world = lastWorld ?? new Dictionary<int, LevelState>();
                var inTrap = new HashSet<int>(trapZones.Keys);
                foreach (var trap in sv.Traps)
                {
                    var here = new HashSet<int>(trap.Inside) { trap.Zone };
                    foreach (var p in Placements)
                    {
                        string t = Requirement.Normalize(p.Grants);
                        if (t.Length == 0 || !here.Contains(PlacementZone(p, world)) || !sv.TokenSphere.ContainsKey(t)) continue;
                        bool elsewhere = Placements.Any(q => { if (Requirement.Normalize(q.Grants) != t) return false; int qz = PlacementZone(q, world); return sv.ZoneSphere.ContainsKey(qz) && !inTrap.Contains(qz) && !here.Contains(qz); });
                        if (elsewhere || trap.Holds.Contains(t)) continue;
                        trap.Holds.Add(t);
                    }
                }
            }

            /// <summary>
            /// The return check from a start other than the game's own (StartOverride, or a start
            /// teleport): the zones the player comes out into from the start, to count as back as the
            /// start does, or null to keep the start alone. The game's own start can be gone back to
            /// (the levels were made so); a random one may be a sealed room or a ledge left one way, from
            /// which every zone reached would be a trap, although nothing needed lies behind. They count
            /// when all of this holds, so that being back in one of them is as good as being at the start:
            /// the start's pocket is the start zone, the doorways out of it and the zones the walk got back
            /// to it from (pocket); nothing in the pocket is taken after the sphere the solve first leaves
            /// it in (no placement there but what stands for "anywhere" gives a token first gained later,
            /// no mechanism set off from it later), so going back there is never needed for what lies in
            /// it; the exits are every zone entered from the pocket, and those entered by a warp in the
            /// sphere that first leaves it or by a warp cast on its levels, so every way the solve leaves
            /// the pocket begins in one of them; and each exit, with what was held when it was reached, gets to every other (walk:
            /// the return check's own search), so from any of them the player can take any other way the
            /// solve takes. A zone that gets back to an exit then gets on to all that the solve reaches.
            /// What is not covered, as for a one-way way into the goal's area: a player who leaves the
            /// pocket before taking what lies in it.
            /// </summary>
            HashSet<int> ReRoot(TimelineSolve sv, int start, Dictionary<int, int> sphereOf, HashSet<int> pocket, Func<int, int, ICollection<int>, Tuple<bool, HashSet<int>>> walk)
            {
                // a zone set solved on its own (Timeline.OnZones) has no game and no start square: nothing to compare
                if (s == null || b.Nav == null || b.Auto == null) return null;
                var own = b.Nav.BaseStartOf(s);
                var at = b.Auto.Start;
                if (at == null || own == null || (at.Level == own.Level && (int)at.X == (int)own.X && (int)at.Y == (int)own.Y)) return null;
                // the start's pocket: the start, the doorways out of it and the zones the walk got back to it from
                var front = new HashSet<int>(pocket) { start };
                for (bool grew = true; grew;)
                {
                    grew = false;
                    foreach (var e in sv.Events) if (e.Kind == "zone" && front.Contains(e.From) && doorways.Contains(e.Zone) && front.Add(e.Zone)) grew = true;
                }
                var entered = sv.Events.Where(e => e.Kind == "zone" && !front.Contains(e.Zone) && sphereOf.ContainsKey(e.Zone)).ToList();
                if (entered.Count == 0) return null;
                int first = entered.Where(e => front.Contains(e.From) || e.Warp != null).Select(e => e.Sphere).DefaultIfEmpty(-1).Min();
                if (first < 0) return null;
                // nothing of the pocket is taken after the sphere he first leaves it in
                var none = new Dictionary<int, LevelState>();
                foreach (var p in Placements)
                {
                    if (!front.Contains(PlacementZone(p, none)) || Everywhere(p, res.Facts)) continue;
                    string t = Requirement.Normalize(p.Grants);
                    int tsp;
                    if (t.Length > 0 && sv.TokenSphere.TryGetValue(t, out tsp) && tsp > first) return null;
                }
                var inPocket = new Regex(@"\bzone:(" + string.Join("|", front) + @")\b");
                if (sv.Events.Any(e => e.Kind == "flag" && e.Sphere > first && inPocket.IsMatch(e.Requires ?? ""))) return null;
                var levels = new HashSet<int>(front.SelectMany(z => zs.LevelsOf(z)));
                var exits = new HashSet<int>(entered.Where(e => front.Contains(e.From) || (e.Warp != null && (e.Sphere == first || levels.Contains(e.Warp.FromLevel)))).Select(e => e.Zone));
                // an exit reached once the silver sapling is planted needs no way back (death takes him to it)
                var walks = exits.ToDictionary(d => d, d => walk(d, sphereOf[d], new int[0]));
                exits.RemoveWhere(d => walks[d] == null);
                if (exits.Count == 0) return null;
                foreach (int d in exits)
                    if (!exits.All(walks[d].Item2.Contains)) return null;
                return exits;
            }
        }

        // ---------------------------------------------------------------- the report

        static string Z(TimelineResult res, TimelineSolve sv, int id)
        {
            string n;
            if (sv != null && sv.NewZones.TryGetValue(id, out n)) return id + " " + n;
            return SolveResult.ZoneName(res.Set, id);
        }

        public static string Report(TimelineResult res)
        {
            var zs = res.Set;
            var sv = res.Solve;
            var sb = new StringBuilder();
            sb.AppendLine("AUTOMATIC ZONES, PHASE C: THE WORLD OVER TIME");
            sb.AppendLine();
            foreach (var w in res.Warnings.Distinct()) sb.AppendLine("Warning: " + w);
            sb.AppendLine("Options: " + res.Options + ".");
            sb.AppendLine("Facts read: " + string.Join(", ", res.Facts.Files) + " (" + res.Facts.Facts.Count + " facts; " + string.Join(", ", res.Facts.Facts.GroupBy(f => f.Kind).Select(g => g.Key + " " + g.Count())) + ").");
            sb.AppendLine("Rules: glitchless. Bashing is always allowed (fists). item:pole means the pole or Telekinesis.");
            var start = res.Abilities.Auto.Start;
            if (start != null) sb.AppendLine(string.Format("Start: {0}, level {1} ({2}, {3}), zone {4}.", start.Description, start.Level, (int)start.X, (int)start.Y, SolveResult.ZoneName(zs, zs.StartZone)));
            var given = res.Abilities.Auto.Given;
            if (given != null && !AutoZones.IsAutomatic(given))
            {
                sb.AppendLine(string.Format("Zones: the zoning given ({0} zones), laid on the levels' regions: each region takes the zone that holds most of its tiles, before and after a change; the links come from the levels, plus the zoning's own links that the levels do not give.", given.Zones.Count(z => z.Id > ZoneSet.Unassigned)));
                foreach (var w in res.Abilities.Auto.Warnings.Where(w => w.Contains("zoning") || w.Contains("by hand"))) sb.AppendLine("Zoning: " + w);
            }
            else
                sb.AppendLine("Zones are those of the base state" + (given != null ? " (the automatic zoning given)" : "") + ". After a change, its level alone is rebuilt; each group of regions takes the base zone that has the most tiles in it; a part split off gets a new zone (ids from 1000000); a base zone left without a group is joined by a free link to the one that took its tiles.");
            sb.AppendLine();
            if (sv != null)
            {
                sb.AppendLine(string.Format("SOLVE ({0}): {1}; {2} spheres, {3} of {4} zones reached, {5} changes set off, {6} applied.", sv.Label, sv.GoalReached ? "goal reached" : "goal NOT reached",
                    sv.Spheres.Count, BaseZonesReached(sv), zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), sv.Fired.Count, sv.Applied.Count));
                sb.AppendLine("  Goal: " + Z(res, sv, res.GoalZone) + (res.GoalFact != null ? " (facts: " + res.GoalFact.Id + "), needs " + res.GoalRequires : " (no goal fact: the largest zone of the last level)") + (sv.GoalMissing.Count > 0 ? "; missing: " + string.Join(", ", sv.GoalMissing.Distinct().Take(20)) : ""));
                if (res.Ends.Count > 0)
                    sb.AppendLine("  Or the end of the game: " + string.Join("; ", res.Ends.Select(e => e.Key + " (trigger #" + e.Key.Trigger + "), " + Z(res, sv, e.Value))) + (sv.GoalReached && sv.GoalBy.Length > 0 ? ". Reached by " + sv.GoalBy + "." : "."));
                var missing = sv.Blocked.SelectMany(x => x.Missing).GroupBy(t => t).OrderByDescending(x => x.Count()).Take(15).ToList();
                if (missing.Count > 0) sb.AppendLine("  What blocks the links out of the reached part (token: links): " + string.Join(", ", missing.Select(x => x.Key + " " + x.Count())));
                if (sv.SafeZones >= 0) sb.AppendLine(string.Format("  Taking nothing from inside the one-way traps: {0} zones{1}.", sv.SafeZones, sv.SafeGoal ? ", goal reached" : ""));
                foreach (var w in sv.Warnings.Distinct()) sb.AppendLine("  Warning: " + w);
                if (sv.FreeArea != null)
                    sb.AppendLine(string.Format("  A new character casts from runes up to circle {0}: higher circles count {1} (the start's free area: {2} zone{3}).", sv.Options.NewCharacterCircle,
                        sv.FreeAreaLeft < 0 ? "never, as the solve does not leave the start's free area" : "from sphere " + sv.FreeAreaLeft + ", when the solve first leaves the start's free area", sv.FreeArea.Count, sv.FreeArea.Count == 1 ? "" : "s"));
                if (sv.OnceSpent.Count > 0) sb.AppendLine("  Single-use items spent (one per way, item or change that only they open): " + string.Join(", ", sv.OnceSpent.GroupBy(t => t).Select(g => g.Key + " " + g.Count())) + ".");
                foreach (var c in sv.CutOff)
                    sb.AppendLine(string.Format("  Sphere {0}: a change cut the player off from {1} zone(s) reached before: {2}.", c.Key, c.Value.Count, string.Join(", ", c.Value.Take(8).Select(z => Z(res, sv, z))) + (c.Value.Count > 8 ? ", …" : "")));
                sb.AppendLine();
                sb.AppendLine("  The main option sets (zones reached, spheres, changes applied, traps, goal):");
                foreach (var kv in res.Variants)
                    sb.AppendLine(string.Format("    {0}: {1} zones, {2} spheres, {3} applied, {4} one-way traps{5}{6}", kv.Key, BaseZonesReached(kv.Value), kv.Value.Spheres.Count, kv.Value.Applied.Count, kv.Value.Traps.Count,
                        kv.Value.SafeZones >= 0 ? " (" + kv.Value.SafeZones + " safe)" : "", kv.Value.GoalReached ? ", goal reached" : ""));
                sb.AppendLine();
                sb.AppendLine("TIMELINE (each sphere: zones reached, tokens gained, changes set off and applied)");
                foreach (var sp in sv.Spheres)
                {
                    sb.AppendLine(string.Format("  {0}: {1} zones: {2}", sp.Index, sp.Zones.Count, string.Join(", ", sp.Zones.Take(14).Select(z => Z(res, sv, z))) + (sp.Zones.Count > 14 ? ", …" : "")));
                    if (sp.Tokens.Count > 0) sb.AppendLine("     gains " + string.Join(", ", sp.Tokens.Where(t => !sp.SetOff.Contains(t)).Take(30)) + (sp.Tokens.Count > 30 ? " …" : ""));
                    if (sp.SetOff.Count > 0) sb.AppendLine("     sets off " + string.Join(", ", sp.SetOff.Select(f => sp.Applied.Contains(f) ? f + " (applied)" : f)));
                }
                sb.AppendLine();
            }
            sb.AppendLine("CHANGES THAT CLOSE THINGS (and how each is treated)");
            foreach (var c in res.Changes.Where(c => c.Closes.Count > 0))
                sb.AppendLine(string.Format("  {0} [{1}{2}]: {3}. {4}.{5}", c.Mechanism.Flag, c.Effect, c.Mechanism.Unavoidable ? ", unavoidable" : ", chosen", string.Join("; ", c.Closes.Take(6)) + (c.Closes.Count > 6 ? " …" : ""), Capital(c.Treatment),
                    c.Opens.Count > 0 ? " Opens: " + string.Join("; ", c.Opens.Take(4)) + (c.Opens.Count > 4 ? " …" : "") + "." : ""));
            foreach (var cl in res.Closers)
                sb.AppendLine(string.Format("  door closer {0}: trigger #{1}; sides {2}; {3} edges changed", cl, cl.Mechanism.Trigger, string.Join(", ", cl.Sides.Select(p => "region " + p.Key + (p.Value ? " (trigger side)" : ""))), cl.EdgesChanged));
            sb.AppendLine();
            sb.AppendLine("CHANGES THAT OPEN THINGS");
            foreach (var c in res.Changes.Where(c => c.Opens.Count > 0 && c.Closes.Count == 0))
                sb.AppendLine(string.Format("  {0}{1}: {2}{3}", c.Mechanism.Flag, c.Mechanism.Free ? " (free by the facts)" : "", string.Join("; ", c.Opens.Take(5)) + (c.Opens.Count > 5 ? " …" : ""), sv != null && sv.Applied.Contains(c.Flag) ? "  [applied]" : sv != null && sv.Fired.Contains(c.Flag) ? "" : "  [not set off]"));
            sb.AppendLine(string.Format("  Changes with no effect on the ways: {0}; not applied (variables, creatures, hacks): {1}.", res.Changes.Count(c => c.Effect == "no way changed"), res.Changes.Count(c => c.Effect == "not applied")));
            sb.AppendLine();
            if (sv != null)
            {
                sb.AppendLine(string.Format("RETURN CHECK (from each zone reached, back to the start with what was held then; {0} one-way traps{1})", sv.Traps.Count, sv.SavedBySapling > 0 ? ", " + sv.SavedBySapling + " zones saved by the planted sapling" : ""));
                if (sv.ReturnTo.Count > 0) sb.AppendLine("  The start is not the game's own and nothing in it is needed after the first sphere: back in a zone it leads out into counts as back (" + string.Join(", ", sv.ReturnTo.Select(z => Z(res, sv, z))) + ").");
                foreach (var t in sv.Traps)
                    sb.AppendLine(string.Format("  {0} (sphere {1}){2}{3}{4}{5}", Z(res, sv, t.Zone), t.Sphere, t.Entry != null ? " entered by [" + t.Entry.Kind + "] " + t.Entry.Requires + (t.Entry.Level > 0 ? " at L" + t.Entry.Level + " (" + t.Entry.X + ", " + t.Entry.Y + ")" : "") : "",
                        t.Inside.Count > 0 ? "; leads on to " + string.Join(", ", t.Inside.Take(5).Select(z => Z(res, sv, z))) + (t.Inside.Count > 5 ? " …" : "") : "",
                        t.Holds.Count > 0 ? "; holds " + string.Join(", ", t.Holds.Take(8)) : "; nothing needed inside",
                        t.Strands.Count > 0 ? "; STRANDS: " + string.Join(", ", t.Strands) : ""));
                sb.AppendLine();
            }
            sb.AppendLine("FACTS: LINKS, WARPS, GOAL");
            foreach (var k in res.FactLinks)
            {
                var f = res.Facts.Facts.FirstOrDefault(x => "facts: " + x.Id == k.Notes);
                sb.AppendLine(string.Format("  {0}: {1} {2} {3} needs {4}{5}", k.Notes, Z(res, sv, k.From), k.TwoWay ? "<->" : "->", Z(res, sv, k.To), k.Requires,
                    f != null && f.Option.Length > 0 ? " with option:" + f.Option + (f.Otherwise.Length > 0 ? ", else " + f.Otherwise : ", else no link") : ""));
            }
            foreach (var f in res.Warps) sb.AppendLine(string.Format("  warp {0}: from {1} to {2} when {3}", f.Id, f.FromLevel > 0 ? "level " + f.FromLevel : f.FromAbove > 0 ? "every level above " + f.FromAbove : "every zone", f.To,
                string.IsNullOrWhiteSpace(f.Requires) ? "free" : f.Requires) + (f.Option.Length > 0 ? " with option:" + f.Option + (res.Options.Tokens().Contains(f.Option) ? " (on)" : " (off)") : ""));
            if (res.GoalFact != null) sb.AppendLine(string.Format("  goal {0}: {1} at {2}, needs {3}", res.GoalFact.Id, Z(res, sv, res.GoalZone), res.GoalFact.To, res.GoalRequires));
            foreach (var f in res.Facts.OfKind("free")) sb.AppendLine(string.Format("  free {0}: L{1} {2}", f.Id, f.Level, string.Join(", ", f.Objects.Select(o => "#" + o).Concat(f.Triggers.Select(t => "trigger #" + t)).Concat(f.Flags))));
            var npc = res.Abilities.NpcHeld;
            if (npc.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format("ITEMS NPCs CARRY ({0}; option:kill_npcs {1})", npc.Count, res.Options.KillNpcs ? "on" : "off"));
                foreach (var p in npc.Where(p => !p.Grants.StartsWith("item:food")).Take(40)) sb.AppendLine(string.Format("  {0} in {1}: needs {2} ({3})", p.Grants, Z(res, sv, p.Zone), p.Requires, p.Notes));
            }
            if (res.Abilities.Created.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ITEMS A CREATE TRAP MAKES");
                foreach (var p in res.Abilities.Created) sb.AppendLine(string.Format("  {0} in {1}: needs {2}{3}", p.Grants, Z(res, sv, p.Zone), p.Requires, sv != null && sv.TokenSphere.ContainsKey(Requirement.Normalize(p.Grants)) ? "  [obtained]" : ""));
            }
            return sb.ToString();
        }

        static string Capital(string t) { return string.IsNullOrEmpty(t) ? t : char.ToUpperInvariant(t[0]) + t.Substring(1); }
    }
}
