using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UwEditor.Core.Randomizer
{
    /// <summary>One step of a walkthrough: what to do, and why the logic says it can be done then.</summary>
    public sealed class WalkStep
    {
        public int Number;
        /// <summary>The solve's sphere the step belongs to.</summary>
        public int Sphere;
        /// <summary>start, go, take, count, use, set off, goal.</summary>
        public string Kind = "";
        public int Level;
        /// <summary>What to do.</summary>
        public string Text = "";
        /// <summary>Why it can be done: what it needs and which earlier step gave it.</summary>
        public string Why = "";
        /// <summary>The ways walked through on the way to this step that need nothing (zone names), shown before it.</summary>
        public readonly List<string> OnTheWay = new List<string>();
        /// <summary>The solve's events this step stands for, in order: the zones walked through on the way, then its own (Event).</summary>
        public readonly List<TimelineEvent> Events = new List<TimelineEvent>();
        public TimelineEvent Event;
        /// <summary>The earlier steps it relies on.</summary>
        public readonly SortedSet<int> After = new SortedSet<int>();
        /// <summary>The tokens it relies on (keys, items, NPCs, flags, spells), as the requirement names them once resolved.</summary>
        public readonly List<string> Uses = new List<string>();
        /// <summary>The logic options it relies on (bashing, lock picking, the Search skill, spells allowed, a named option), in words.</summary>
        public readonly List<string> Options = new List<string>();
        /// <summary>The way from where the step before left the player to where this one is done, when that is another zone: one line a stretch.</summary>
        public readonly List<string> Route = new List<string>();
        /// <summary>The zone the player stands in after the step (0: not known).</summary>
        public int EndsIn;
        /// <summary>The zones its requirement names as reached (a switch on the other side, where a trigger is set off).</summary>
        public readonly List<int> Zones = new List<int>();
        /// <summary>What not to do, for a source the game may take away (the facts' mayBeLost: Placement.Lost), or "".</summary>
        public string Warning = "";
        /// <summary>A line shown before the step, or "": UW2's "Leave the world now" on the step that leaves a shut start world (TimelineSolve.SealedLeft).</summary>
        public string Note = "";
    }

    /// <summary>
    /// A step-by-step solution of a timeline solve, for the spoiler log: from the start to the
    /// goal, only what the goal needs. It reads the solve's events (TimelineSolve.Events, in the
    /// order the solver found them) and works back from the goal: the zone the goal lies in and
    /// the goal's requirement; each zone needs the zone it was entered from and what its link
    /// asked for; each token needs the zone its placement lay in and the placement's own
    /// requirement (a count, Shak's coins, needs every part counted); each mechanism needs what
    /// sets it off (where its trigger is, its conditions). Of a requirement with alternatives the
    /// one shown is one that held at that point, chosen so that what it needs is as few things
    /// as possible: items and zones before logic options (bashing, lock picking, the Search
    /// skill), and of those what came earliest; the others that also held are not named. The
    /// steps keep the solve's spheres in order; inside a sphere every step comes after the steps
    /// it needs, and steps on the same level stay together where they can. Ways that need
    /// nothing are not steps of their own: they are listed as "on the way" before the next step.
    /// The order is walked before the steps are written (each step from where the one before left
    /// the player, with what he holds then); when it cannot be, because a one-way way or the
    /// leaving of a shut world came while a step behind it was still to do, the order is made
    /// again by walking it (Reordered).
    /// </summary>
    public sealed class Walkthrough
    {
        public readonly List<WalkStep> Steps = new List<WalkStep>();
        /// <summary>The solve reached the goal and the walkthrough ends there.</summary>
        public bool Reached;
        public string Goal = "";
        /// <summary>What the walkthrough could not justify: a requirement met by nothing it can name, an event with no zone before it.</summary>
        public readonly List<string> Doubts = new List<string>();
        /// <summary>
        /// Steps the player cannot get to from where the step before left him, with what he holds
        /// then and the world as it stands: the solver takes every zone once reached as reachable
        /// again, and here that does not hold (a one-way drop with no way round).
        /// </summary>
        public readonly List<string> NoWay = new List<string>();
        /// <summary>
        /// The order by spheres could not be walked (a one-way way, or the leaving of a shut world,
        /// came while a step behind it was still to do), and the steps are in the order of a walk
        /// that keeps such a way until nothing is left behind it (Builder.Reorder).
        /// </summary>
        public bool Reordered;
        /// <summary>The steps were walked one after the other, each from where the one before left the player, with what he held then (Routes; a step with no way there goes to NoWay).</summary>
        public bool Walked;
        /// <summary>The walkthrough reaches the goal and the walk found a way to every step.</summary>
        public bool Walkable { get { return Reached && Walked && NoWay.Count == 0; } }
        /// <summary>
        /// The reverse check: the steps gone through from the goal back to the start. What a
        /// step needs is owed from there on; a step that gives it pays it off (one piece of a
        /// thing that is used up pays one use). What is still owed at the start, or a step whose
        /// named needs do not meet its requirement on their own, is a problem.
        /// </summary>
        public readonly List<string> ReverseProblems = new List<string>();
        /// <summary>Steps that give nothing a later step needs (the walkthrough lists only what the goal needs, so there should be none).</summary>
        public readonly List<string> ReverseUnused = new List<string>();
        /// <summary>How many needs the reverse check saw paid.</summary>
        public int ReversePaid;
        /// <summary>The reverse check ran (the solve reached the goal).</summary>
        public bool ReverseRan;

        /// <summary>The reverse check as lines of text.</summary>
        public List<string> ReverseLines()
        {
            var l = new List<string>();
            if (!ReverseRan) return l;
            l.Add("REVERSE CHECK (the same steps from the goal back to the start)");
            l.Add("  Each step's needs are owed from the goal backwards until an earlier step gives them; a thing that is used up");
            l.Add("  (a scroll, food handed over) is owed once a use and paid one piece a source. Each step's named needs must also");
            l.Add("  meet its requirement with nothing else held.");
            if (ReverseProblems.Count == 0) l.Add(string.Format("  Passed: {0} needs, each paid by an earlier step; nothing is owed at the start.", ReversePaid));
            else { l.Add(string.Format("  FAILED: {0} problem{1} ({2} needs paid).", ReverseProblems.Count, ReverseProblems.Count == 1 ? "" : "s", ReversePaid)); foreach (var p in ReverseProblems) l.Add("    " + p); }
            foreach (var u in ReverseUnused) l.Add("  Not needed: " + u);
            return l;
        }

        /// <summary>The walkthrough of a solve of the timeline (the key shuffle's check: KeyShuffleResult.Check of CheckBuild). Places are named by the hand-made zones when given, else by the part of the level (from the level's tiles).</summary>
        public static Walkthrough Build(TimelineResult t, TimelineSolve sv, ZoneSet names = null, Func<int, LevelModel> level = null)
        {
            var w = new Walkthrough();
            if (t == null || sv == null) { w.Doubts.Add("There is no solve to follow."); return w; }
            new Builder(t, sv, names, level, w).Run();
            return w;
        }

        /// <summary>The walkthrough as lines of text: each step numbered, what to do, then why.</summary>
        public List<string> Lines()
        {
            var l = new List<string>();
            int sphere = -1;
            foreach (var st in Steps)
            {
                if (st.Sphere != sphere && st.Kind != "goal") { sphere = st.Sphere; l.Add("  -- sphere " + sphere + " --"); }
                if (st.OnTheWay.Count > 0)
                    l.Add(string.Format("       on the way (ways that need nothing): {0}", string.Join(", ", st.OnTheWay.Take(12)) + (st.OnTheWay.Count > 12 ? ", and " + (st.OnTheWay.Count - 12) + " more" : "")));
                if (st.Note.Length > 0) l.Add("  >>> " + st.Note);
                foreach (var r in st.Route) l.Add("       " + r);
                l.Add(string.Format("  {0,3}. {1}", st.Number, st.Text));
                if (st.Why.Length > 0) l.Add("       Why: " + st.Why);
                if (st.Warning.Length > 0) l.Add("       Beware: " + st.Warning);
            }
            return l;
        }

        /// <summary>
        /// What a bash through a link's door adds to "bash it open": a door of toughness class 1 or
        /// 2 takes half or a quarter of each blow (combat/DAMAGE.C damage_object shifts the damage
        /// right by COMOBJ byte 6 bits 2-3), so a weak character needs well charged blows. Class 0,
        /// or no breakable closed door on the link's square or beside it, adds nothing.
        /// </summary>
        public static string BashNote(LevelModel m, ZoneLink k)
        {
            if (m == null || k == null || k.X < 0 || k.Y < 0) return "";
            foreach (var d in new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = k.X + d.Item1, y = k.Y + d.Item2;
                if (x < 0 || y < 0 || x > 63 || y > 63) continue;
                foreach (var o in m.TileObjects(x, y))
                {
                    if (!Editing.DoorSpikes.IsClosedDoor(o.Item) || !ZoneDoors.Requirement(m.Session, m, o, out _).Split('|').Any(a => a.Trim() == "bash")) continue;
                    int qclass = (Editing.Races.ComObjByte(m.Session, o.Item, 6) >> 2) & 3;
                    return qclass == 2 ? "; charge each blow, the door takes only a quarter of it, so it may take a while" : qclass == 1 ? "; charge each blow, the door takes only half of it, so it may take a while" : "";
                }
            }
            return "";
        }

        // ---------------------------------------------------------------- building

        /// <summary>One alternative's leaf, resolved: what it is in words and the events it relies on.</summary>
        sealed class Part
        {
            public string Text = "";
            public readonly List<int> Deps = new List<int>();
            public string Option;
            public string Token;
            /// <summary>Free, or the zone the step starts or ends in: nothing to do.</summary>
            public bool Trivial;
            /// <summary>A zone:N leaf: the zone.</summary>
            public int Zone = -1;
        }

        sealed class Explained
        {
            public string Requires = "";
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<string> Problems = new List<string>();
            public bool Trivial { get { return Parts.All(p => p.Trivial); } }
        }

        /// <summary>
        /// The world as it stood just before an event (Cut): the tokens and zones the solve had gained.
        /// On the player's walk through the steps (NotYet given): less what the walkthrough's own
        /// steps still to come give, which the player does not hold yet whatever the solve's record says.
        /// </summary>
        sealed class At : IRequirementContext, INowContext
        {
            public Builder B;
            public int Cut;
            /// <summary>The walkthrough's events not walked yet (null: none left out).</summary>
            public HashSet<int> NotYet;
            public SolverOptions Options { get { return B.So; } }
            bool Gained(List<int> at) { return at != null && at.Any(i => i < Cut && !NotYet.Contains(i)); }
            public bool HasToken(string t)
            {
                t = Requirement.Normalize(t);
                int i;
                if (B.Start.Contains(t)) return true;
                if (NotYet == null) return B.FirstToken.TryGetValue(t, out i) && i < Cut;
                List<int> at;
                return B.TokenAt.TryGetValue(t, out at) && Gained(at);
            }
            public bool ZoneReachable(int z)
            {
                int i;
                if (NotYet == null) return B.FirstZone.TryGetValue(z, out i) && i < Cut;
                List<int> at;
                return B.ZoneAt.TryGetValue(z, out at) && Gained(at);
            }
            public bool ZoneNow(int z) { return ZoneReachable(z); }
            IEnumerable<string> Tokens() { return NotYet == null ? B.FirstToken.Where(kv => kv.Value < Cut).Select(kv => kv.Key) : B.TokenAt.Where(kv => Gained(kv.Value)).Select(kv => kv.Key); }
            public int TalismanCount() { return Tokens().Count(k => k.StartsWith("talisman:")) + B.Start.Count(x => x.StartsWith("talisman:")); }
            public int PlotClock() { return Requirement.PlotClock(Tokens().Concat(B.Start)); }
        }

        sealed class Builder
        {
            readonly TimelineResult t;
            readonly TimelineSolve sv;
            readonly ZoneSet zs, names;
            readonly Func<int, LevelModel> level;
            readonly Walkthrough w;
            readonly List<TimelineEvent> ev;
            public readonly SolverOptions So;
            public readonly HashSet<string> Start = new HashSet<string>();
            public readonly Dictionary<int, int> FirstZone = new Dictionary<int, int>();
            public readonly Dictionary<string, int> FirstToken = new Dictionary<string, int>();
            /// <summary>Every event that gives a token (or flag), and every one that enters a zone, in order.</summary>
            public readonly Dictionary<string, List<int>> TokenAt = new Dictionary<string, List<int>>();
            public readonly Dictionary<int, List<int>> ZoneAt = new Dictionary<int, List<int>>();
            readonly Dictionary<string, List<int>> counts = new Dictionary<string, List<int>>();
            readonly Dictionary<int, List<int>> spentFrom = new Dictionary<int, List<int>>();
            readonly Dictionary<int, Explained> explained = new Dictionary<int, Explained>();
            readonly Dictionary<int, List<int>> deps = new Dictionary<int, List<int>>();
            readonly Dictionary<int, List<int>> levelsOf = new Dictionary<int, List<int>>();
            readonly Dictionary<int, LevelModel> models = new Dictionary<int, LevelModel>();

            public Builder(TimelineResult t, TimelineSolve sv, ZoneSet names, Func<int, LevelModel> level, Walkthrough w)
            {
                this.t = t; this.sv = sv; this.names = names; this.level = level; this.w = w;
                zs = t.Set;
                ev = sv.Events;
                So = sv.Options ?? new SolverOptions();
                foreach (var s in So.StartTokens) Start.Add(Requirement.Normalize(s));
            }

            public void Run()
            {
                Index();
                if (!sv.GoalReached) { w.Doubts.Add("The solve does not reach the goal: there is no walkthrough."); return; }
                if (ev.Count == 0) { w.Doubts.Add("The solve recorded no steps."); return; }
                w.Reached = true;
                bool end = !sv.GoalBy.StartsWith("the goal zone");
                string goalReq = end ? t.EndFor(So) : t.GoalFor(So);
                w.Goal = sv.GoalBy;
                int goalIdx;
                var roots = new List<int>();
                if (FirstZone.TryGetValue(sv.GoalAt, out goalIdx)) roots.Add(goalIdx);
                else w.Doubts.Add("The goal's zone " + ZoneText(sv.GoalAt) + " was never entered in the solve's record.");
                var gx = Explain(goalReq, ev.Count, -1, -1, "goal");
                foreach (var p in gx.Parts) roots.AddRange(p.Deps);
                foreach (var pr in gx.Problems) w.Doubts.Add("The goal: " + pr);

                // every event the goal needs, back to the start
                var need = new HashSet<int>();
                var stack = new Stack<int>(roots);
                need.Add(0);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    if (!need.Add(i) && i != 0) continue;
                    foreach (int d in Deps(i)) if (!need.Contains(d)) stack.Push(d);
                }
                var order = Order(need);
                var folded = new HashSet<int>(order.Where(i => i != 0 && Folds(i)));
                // the order by spheres may not be one the player can follow: a one-way way taken (or a shut world left) while
                // a step behind it is still to do, which the solve, taking every zone once reached as reachable again, does not
                // see; then the order is made again by walking it (Reorder), and kept if that one can be walked
                if (!Walks(order, need, folded))
                {
                    var again = Reorder(order, need, folded);
                    if (again != null && Walks(again, need, folded)) { order = again; w.Reordered = true; }
                }

                // the steps: a way that needs nothing goes "on the way" of the first step that needs it
                var stepOf = new Dictionary<int, int>();
                var pos = new Dictionary<int, int>();
                for (int k = 0; k < order.Count; k++) pos[order[k]] = k;
                var attached = new HashSet<int>();
                Func<IEnumerable<int>, List<int>> onTheWay = from => OnTheWay(from, folded, attached, pos);
                var built = new List<KeyValuePair<WalkStep, int>>();
                foreach (int i in order)
                {
                    if (folded.Contains(i)) continue;
                    var st = new WalkStep { Number = built.Count + 1, Event = ev[i], Sphere = Postponed.ContainsKey(i) ? Postponed[i] : ev[i].Sphere, Level = LevelOf(i) };
                    foreach (int p in onTheWay(Deps(i))) { st.Events.Add(ev[p]); st.OnTheWay.Add(ZoneText(ev[p].Zone)); stepOf[p] = st.Number; }
                    st.Events.Add(ev[i]);
                    stepOf[i] = st.Number;
                    built.Add(new KeyValuePair<WalkStep, int>(st, i));
                }
                var goal = new WalkStep { Number = built.Count + 1, Kind = "goal", Sphere = sv.Spheres.Count > 0 ? sv.Spheres.Count - 1 : 0 };
                foreach (int p in onTheWay(folded.Where(f => !attached.Contains(f)).ToList())) { goal.Events.Add(ev[p]); goal.OnTheWay.Add(ZoneText(ev[p].Zone)); stepOf[p] = goal.Number; }
                // a step the walk put before steps of a later sphere is shown in that sphere (the world is at least that far by then)
                int upTo = 0;
                foreach (var kv in built) kv.Key.Sphere = upTo = Math.Max(upTo, kv.Key.Sphere);
                foreach (var kv in built) { Describe(kv.Key, kv.Value, stepOf); w.Steps.Add(kv.Key); }
                foreach (var kv in built)
                    if (Postponed.ContainsKey(kv.Value))
                        kv.Key.Warning = (kv.Key.Warning.Length > 0 ? kv.Key.Warning + " " : "") + "Not before this point: when the solve first got here (sphere " + ev[kv.Value].Sphere + ") this was a one-way pocket with no way back out yet.";
                Routes(built.Select(kv => kv.Key).ToList(), need);
                // the goal
                goal.Level = goalIdx >= 0 && FirstZone.ContainsKey(sv.GoalAt) ? LevelOf(FirstZone[sv.GoalAt]) : 0;
                if (FirstZone.ContainsKey(sv.GoalAt)) goal.Sphere = ev[FirstZone[sv.GoalAt]].Sphere;
                int gs;
                goal.Text = "GOAL: " + (end ? sv.GoalBy : "arriving in " + ZoneText(sv.GoalAt)) + (FirstZone.ContainsKey(sv.GoalAt) && stepOf.TryGetValue(FirstZone[sv.GoalAt], out gs) ? ", reached in step " + gs : "") + ".";
                goal.Why = WhyOf(gx, stepOf, goal, goal.Number, null);
                if (goal.Why == "Needs nothing.") goal.Why = "The goal needs nothing more than getting there.";
                if (FirstZone.ContainsKey(sv.GoalAt) && stepOf.TryGetValue(FirstZone[sv.GoalAt], out gs)) goal.After.Add(gs);
                w.Steps.Add(goal);
                Reverse(goalReq);
            }

            // ---- the record indexed

            void Index()
            {
                var sources = new Dictionary<string, List<int>>();
                var spends = new Dictionary<string, int>();
                // what is used up when used: single-use sources (once:X) and what a step hands over (food)
                var spendable = new HashSet<string>(ev.SelectMany(e => e.Spent));
                foreach (var e in ev) if (e.Token.StartsWith("once:")) spendable.Add(e.Token);
                for (int i = 0; i < ev.Count; i++)
                {
                    var e = ev[i];
                    if (e.Kind == "zone" && !FirstZone.ContainsKey(e.Zone)) FirstZone[e.Zone] = i;
                    if ((e.Kind == "token" || e.Kind == "flag") && e.Token.Length > 0 && !FirstToken.ContainsKey(e.Token)) FirstToken[e.Token] = i;
                    if (e.Kind == "zone") { List<int> l; if (!ZoneAt.TryGetValue(e.Zone, out l)) ZoneAt[e.Zone] = l = new List<int>(); l.Add(i); }
                    if ((e.Kind == "token" || e.Kind == "flag") && e.Token.Length > 0) { List<int> l; if (!TokenAt.TryGetValue(e.Token, out l)) TokenAt[e.Token] = l = new List<int>(); l.Add(i); }
                    if (e.Kind == "count") { List<int> l; if (!counts.TryGetValue(e.Token, out l)) counts[e.Token] = l = new List<int>(); l.Add(i); }
                    // a single-use source spent: the k-th use of a kind is the k-th source of it found
                    foreach (var tok in e.Spent)
                    {
                        int k; spends.TryGetValue(tok, out k); spends[tok] = ++k;
                        List<int> src;
                        List<int> d; if (!spentFrom.TryGetValue(i, out d)) spentFrom[i] = d = new List<int>();
                        if (sources.TryGetValue(tok, out src) && src.Count >= k) d.Add(src[k - 1]);
                    }
                    if ((e.Kind == "token" || e.Kind == "use") && spendable.Contains(e.Token))
                    {
                        List<int> l; if (!sources.TryGetValue(e.Token, out l)) sources[e.Token] = l = new List<int>(); l.Add(i);
                    }
                }
            }

            /// <summary>What an event needs: the events before it that made it possible.</summary>
            List<int> Deps(int i)
            {
                List<int> d;
                if (deps.TryGetValue(i, out d)) return d;
                d = new List<int>();
                var e = ev[i];
                var x = ExplainEvent(i);
                foreach (var p in x.Parts) d.AddRange(p.Deps);
                if (e.Kind == "zone")
                {
                    int j;
                    if (e.Link == null && i > 0)
                    {
                        // stood in when a change went off: the change before it
                        for (int k = i - 1; k >= 0; k--) if (ev[k].Kind == "flag") { d.Add(k); break; }
                    }
                    else if (e.Warp != null && (e.Warp.FromLevel > 0 || e.Warp.FromAbove > 0))
                    {
                        int k = FirstZone.Where(kv => kv.Value < i && WarpFrom(e.Warp, kv.Key)).Select(kv => kv.Value).DefaultIfEmpty(-1).Min();
                        string where = e.Warp.FromLevel > 0 ? "on level " + e.Warp.FromLevel : "on a level above " + e.Warp.FromAbove;
                        if (k >= 0) d.Add(k); else w.Doubts.Add("Event " + i + ": the warp " + e.Warp.Id + " needs a zone " + where + " reached before it; none is.");
                    }
                    else if (e.From >= 0)
                    {
                        if (FirstZone.TryGetValue(e.From, out j) && j < i) d.Add(j);
                        else w.Doubts.Add("Event " + i + ": " + ZoneText(e.Zone) + " is entered from " + ZoneText(e.From) + ", which the record does not reach before.");
                    }
                }
                else if (e.Kind == "token" || e.Kind == "count" || e.Kind == "use")
                {
                    int j;
                    if (FirstZone.TryGetValue(e.Zone, out j) && j < i) d.Add(j);
                    else w.Doubts.Add("Event " + i + " (" + e.Token + "): its zone " + ZoneText(e.Zone) + " is not reached before it in the record.");
                    if (e.Requires.StartsWith("count:")) { List<int> c; if (counts.TryGetValue(e.Requires.Substring(6), out c)) d.AddRange(c.Where(k => k < i)); }
                }
                List<int> sp;
                if (spentFrom.TryGetValue(i, out sp)) d.AddRange(sp);
                d = d.Where(k => k != i).Distinct().ToList();
                deps[i] = d;
                return d;
            }

            Explained ExplainEvent(int i)
            {
                Explained x;
                if (explained.TryGetValue(i, out x)) return x;
                var e = ev[i];
                if (e.Requires.StartsWith("count:")) x = new Explained { Requires = e.Requires };
                else if (e.Kind == "zone")
                {
                    x = Explain(e.Requires, i, e.From, e.Zone, e.Link != null && e.Link.Kind == "warp" ? "warp" : "link");
                    // a way that only a change of the level made (a lever that opened a wall, a bridge that came)
                    if (e.Link != null && e.Changes.Count > 0 && t.Engine != null)
                    {
                        List<string> made;
                        try { made = t.Engine.ChangesFor(e.Link, e.Changes.ToList()); }
                        catch (Exception ex) { made = new List<string>(); x.Problems.Add("cannot tell which change made this way (" + ex.Message + ")"); }
                        foreach (var f in made)
                        {
                            var p = new Part { Token = f, Text = "the way is there since the change " + f };
                            int j;
                            if (FirstToken.TryGetValue(f, out j) && j < i) p.Deps.Add(j);
                            else x.Problems.Add("the way needs the change " + f + ", which was not set off before");
                            x.Parts.Add(p);
                        }
                    }
                }
                else if (e.Kind == "flag") x = Explain(e.Requires, i, -1, -1, "flag");
                else x = Explain(e.Requires, i, e.Zone, -1, "placement");
                foreach (var p in x.Problems) w.Doubts.Add("Event " + i + " (" + Short(e) + "): " + p);
                explained[i] = x;
                return x;
            }

            // ---- requirements explained

            static readonly Regex TokenChars = new Regex(@"[A-Za-z0-9_:\-.]+");
            const string Off = "flag:__off__";

            /// <summary>
            /// The alternative of a requirement that held just before event `at`: the true leaves,
            /// then each dropped (logic options first, then what came latest) while the rest still
            /// satisfies it. from and to: the zones a link joins, whose zone:N terms (a door that
            /// opens from this side) are nothing to do.
            /// </summary>
            Explained Explain(string req, int at, int from, int to, string what)
            {
                var x = new Explained { Requires = string.IsNullOrWhiteSpace(req) ? "free" : req.Trim() };
                string err;
                var r = Requirement.TryParse(x.Requires, out err);
                if (r == null) { x.Problems.Add("cannot read the requirement '" + x.Requires + "': " + err); return x; }
                var ctx = new At { B = this, Cut = at };
                if (!r.Eval(ctx)) { x.Problems.Add("the requirement " + x.Requires + " does not hold with what the solve had gained by then"); return x; }
                var leaves = new List<string>();
                r.Tokens(leaves);
                var whole = r.ToString();
                var truth = leaves.Where(l => Requirement.Parse(l).Eval(ctx)).ToList();
                var keep = new HashSet<string>(truth);
                foreach (var leaf in truth.OrderByDescending(l => Cost(l, at)).ToList())
                {
                    if (IsFree(leaf)) continue;
                    keep.Remove(leaf);
                    if (!Restricted(whole, keep).Eval(ctx)) keep.Add(leaf);
                }
                foreach (var leaf in truth.Where(keep.Contains)) x.Parts.Add(Resolve(leaf, at, from, to, what, x.Problems));
                return x;
            }

            static bool IsFree(string leaf) { return leaf == "free" || leaf == "" || leaf == "none" || leaf == "true"; }

            static Requirement Restricted(string whole, HashSet<string> allowed)
            {
                string text = TokenChars.Replace(whole, m => { string tk = Requirement.Normalize(m.Value); return allowed.Contains(tk) || IsFree(tk) ? m.Value : Off; });
                return Requirement.TryParse(text, out _) ?? Requirement.Parse(Off);
            }

            /// <summary>Which leaves to drop first: logic options (dropped while an item or a zone does it), then what came latest.</summary>
            double Cost(string leaf, int at)
            {
                string head = Head(leaf);
                if (head == "bash" || head == "pick" || head == "search" || head == "option" || head == "nooption") return 1e7;
                if (head == "spell" && !So.AbilitySources) { int i; return FirstToken.TryGetValue(leaf, out i) && i < at ? i : 1e7; }
                var dummy = new List<string>();
                var p = Resolve(leaf, at, -1, -1, "", dummy);
                return p.Deps.Count == 0 ? (p.Option != null ? 1e7 : 0) : p.Deps.Max();
            }

            static string Head(string leaf) { int c = leaf.IndexOf(':'); return c < 0 ? leaf : leaf.Substring(0, c); }
            static string Tail(string leaf) { int c = leaf.IndexOf(':'); return c < 0 ? "" : leaf.Substring(c + 1); }

            bool Held(string token, int at, out int i)
            {
                i = -1;
                if (Start.Contains(token)) return true;
                return FirstToken.TryGetValue(token, out i) && i < at;
            }

            /// <summary>One true leaf in words, with the events it relies on.</summary>
            Part Resolve(string leaf, int at, int from, int to, string what, List<string> problems)
            {
                var p = new Part();
                string head = Head(leaf), tail = Tail(leaf);
                int n, i;
                if (IsFree(leaf)) { p.Trivial = true; p.Text = "nothing"; return p; }
                switch (head)
                {
                    case "bash":
                        p.Text = "bash it open (bashing is allowed" + BashNote(at) + ")";
                        p.Option = "bashing";
                        return p;
                    case "pick":
                        int.TryParse(tail, out n);
                        if (PickBySkill(n, at))
                        {
                            p.Text = string.Format("pick the lock (difficulty {0}; the lock picking option, skill {1}{2})", n, So.LockpickSkill, So.PickRetry ? ", tries again" : "");
                            p.Option = "lock picking";
                            if (So.PickNeedsLockpick && Held("item:lockpick", at, out i)) { p.Deps.Add(i); p.Token = "item:lockpick"; p.Text += " with the lockpick"; }
                            return p;
                        }
                        var open = Ability("open", at);
                        if (open != null)
                        {
                            p.Text = "the Open spell on the lock (" + Name(open) + ")";
                            p.Token = open;
                            if (Held(open, at, out i) && i >= 0) p.Deps.Add(i);
                            return p;
                        }
                        problems.Add(leaf + " holds, but neither the skill nor the Open spell does it");
                        p.Text = leaf;
                        return p;
                    case "search":
                        int.TryParse(tail, out n);
                        if (n <= 0) { p.Text = "look at it (a look of height 0 always works)"; return p; }
                        p.Text = string.Format("look with the Search skill (height {0}; skill {1}{2})", n, So.SearchSkill, So.SearchRetry ? ", tries again" : "");
                        p.Option = "the Search skill";
                        return p;
                    case "option":
                        p.Text = "the logic option " + tail + " is on";
                        p.Option = "option " + tail;
                        return p;
                    case "nooption":
                        p.Trivial = true;
                        p.Text = "the logic option " + tail + " is off";
                        return p;
                    case "zone":
                    case "now_zone":
                        int.TryParse(tail, out n);
                        p.Zone = n;
                        if (n == from || n == to)
                        {
                            p.Trivial = true;
                            p.Text = what == "placement" ? "it lies in " + ZoneText(n) : "it opens from " + ZoneText(n) + ", where you are";
                            if (what == "placement") p.Trivial = false;
                            if (FirstZone.TryGetValue(n, out i) && i < at) p.Deps.Add(i);
                            return p;
                        }
                        if (FirstZone.TryGetValue(n, out i) && i < at) p.Deps.Add(i);
                        else problems.Add(leaf + " holds, but the zone is not reached before");
                        p.Text = what == "flag" ? "stand in " + ZoneText(n) : what == "link" ? "a switch or the way round in " + ZoneText(n) : ZoneText(n) + " reached";
                        return p;
                    case "talismans":
                        int.TryParse(tail, out n);
                        var tal = FirstToken.Where(kv => kv.Key.StartsWith("talisman:") && kv.Value < at).OrderBy(kv => kv.Value).Take(n).ToList();
                        p.Deps.AddRange(tal.Select(kv => kv.Value));
                        p.Text = n + " talismans (" + string.Join(", ", tal.Select(kv => Tail(kv.Key))) + ")";
                        p.Token = leaf;
                        if (tal.Count < n) problems.Add(leaf + ": only " + tal.Count + " talismans found before");
                        return p;
                    case "plot":
                        var plot = FirstToken.Where(kv => (kv.Key.StartsWith("plotset:") || kv.Key.StartsWith("plotstep:")) && kv.Value < at).ToList();
                        p.Deps.AddRange(plot.Select(kv => kv.Value));
                        p.Text = "the castle plot at " + tail + " (" + string.Join(", ", plot.Select(kv => kv.Key)) + ")";
                        p.Token = leaf;
                        return p;
                    case "spell":
                    case "use":
                        string ab = head == "spell" ? Ability(tail, at) : Use(tail, at);
                        if (ab == null)
                        {
                            if (head == "spell" && !So.AbilitySources && So.AllowSpells) { p.Text = "the spell " + tail + " (spells are allowed)"; p.Option = "spells allowed"; return p; }
                            problems.Add(leaf + " holds, but no source of it is found before");
                            p.Text = leaf;
                            return p;
                        }
                        p.Token = ab;
                        p.Text = Name(ab);
                        if (Held(ab, at, out i) && i >= 0) p.Deps.Add(i);
                        return p;
                    default:
                        p.Token = leaf;
                        p.Text = Name(leaf);
                        if (Held(leaf, at, out i)) { if (i >= 0) p.Deps.Add(i); else p.Text += " (held from the start)"; }
                        else problems.Add(leaf + " holds, but nothing before gives it");
                        return p;
                }
            }

            /// <summary>Whether a link's square is solid rock in the level as shipped: a teleport's trigger set off by walking into the wall.</summary>
            bool InWall(ZoneLink k)
            {
                if (k == null || k.Level < 1 || k.X < 0 || k.Y < 0 || k.X > 63 || k.Y > 63 || !(k.Notes ?? "").StartsWith("trigger #")) return false;
                var m = Model(k.Level);
                return m != null && m.Tiles[k.X, k.Y] != null && m.Tiles[k.X, k.Y].IsSolid;
            }

            /// <summary>The bash note (Walkthrough.BashNote) for event at's link.</summary>
            string BashNote(int at)
            {
                var k = at >= 0 && at < ev.Count ? ev[at].Link : null;
                return k != null && k.Level > 0 ? Walkthrough.BashNote(Model(k.Level), k) : "";
            }

            bool PickBySkill(int z, int at)
            {
                if (z >= 15 || So.LockpickSkill < 0) return false;
                int i;
                if (So.PickNeedsLockpick && !Held("item:lockpick", at, out i)) return false;
                if (z == 14) return So.LockpickSkill >= 31;
                return So.PickRetry ? So.LockpickSkill - z * 3 + 30 > 15 : So.LockpickSkill >= z * 3;
            }

            /// <summary>The token that gives an ability just before `at` (as Requirement's spell:X with ability sources), the one gained first.</summary>
            string Ability(string x, int at)
            {
                var c = new List<string> { "spell:" + x, "worn:" + x, "perm:" + x };
                if (So.Magic) c.Add("cast:" + x);
                if (So.SingleUseItems) c.Add("once:" + x);
                return First(c, at);
            }

            string Use(string x, int at)
            {
                var c = new List<string> { "worn:" + x, "perm:" + x };
                if (So.SingleUseItems) c.Add("once:" + x);
                return First(c, at);
            }

            string First(List<string> c, int at)
            {
                string best = null;
                int bi = int.MaxValue, i;
                foreach (var k in c) if (Held(k, at, out i) && (i < 0 ? -1 : i) < bi) { best = k; bi = i < 0 ? -1 : i; }
                return best;
            }

            // ---- the order

            /// <summary>The needed events by sphere; inside a sphere each after what it needs, staying on the level of the step before where possible.</summary>
            /// <summary>The events Order puts off to a later sphere than the solve's (a one-way trap's entry), and that sphere.</summary>
            readonly Dictionary<int, int> Postponed = new Dictionary<int, int>();

            List<int> Order(HashSet<int> need)
            {
                var order = new List<int>();
                int lastLevel = ev.Count > 0 ? LevelOf(0) : 0;
                // a zone first entered while it was a one-way trap (the return check's traps: a pocket of the Void entered
                // from the Pits by a one-way teleport, left only with lava protection) is not walked into then: its entry, and
                // what follows inside, wait for the first later step outside that needs them, when the solve can come back out
                var wait = Postponed;
                // (not the zones of a shut start world, or of a shut world come out in, before the solve leaves the last of them: that part is done in its own order, and
                // put off it would come after the leaving, when the world is shut)
                int leftAt = sv.Leaves.Count > 0 ? sv.Leaves[sv.Leaves.Count - 1].Sphere : sv.SealedLevels != null ? int.MaxValue : 0;
                var traps = sv.Traps.Where(tr => tr.Sphere >= leftAt).ToList();
                if (traps.Count > 0)
                {
                    var users = new Dictionary<int, List<int>>();
                    foreach (int i in need) foreach (int d in Deps(i)) { List<int> l; if (!users.TryGetValue(d, out l)) users[d] = l = new List<int>(); l.Add(i); }
                    Func<int, bool> inTrap = i => ev[i].Kind == "zone" && traps.Any(tr => (tr.Zone == ev[i].Zone && tr.Sphere == ev[i].Sphere) || (tr.Sphere <= ev[i].Sphere && tr.Inside.Contains(ev[i].Zone)));
                    var trapped = new HashSet<int>(need.Where(i => i != 0 && inTrap(i)));
                    // inside the trap also what is done in a trapped zone after entering it (the zone's own events lean on its entry)
                    foreach (int i in need.OrderBy(i => i)) if (!trapped.Contains(i) && i != 0 && Deps(i).Any(trapped.Contains) && ev[i].Kind != "zone" && Deps(i).Where(d => ev[d].Kind == "zone").All(trapped.Contains)) trapped.Add(i);
                    foreach (int i in need.OrderByDescending(i => i))
                    {
                        if (!trapped.Contains(i)) continue;
                        int at = int.MaxValue;
                        List<int> us;
                        if (users.TryGetValue(i, out us)) foreach (int u in us) at = Math.Min(at, wait.ContainsKey(u) ? wait[u] : ev[u].Sphere);
                        if (at != int.MaxValue && at > ev[i].Sphere) wait[i] = at;
                    }
                }
                Func<int, int> sphereOf = i => wait.ContainsKey(i) ? wait[i] : ev[i].Sphere;
                foreach (var grp in need.GroupBy(sphereOf).OrderBy(g => g.Key))
                {
                    var left = new HashSet<int>(grp);
                    var done = new HashSet<int>(order);
                    while (left.Count > 0)
                    {
                        var ready = left.Where(i => Deps(i).All(d => done.Contains(d) || !need.Contains(d) || !left.Contains(d))).ToList();
                        if (ready.Count == 0) ready = new List<int> { left.Min() };     // cannot happen: deps come earlier in the record
                        int next = ready.OrderBy(i => LevelOf(i) == lastLevel ? 0 : 1).ThenBy(i => i).First();
                        order.Add(next);
                        done.Add(next);
                        left.Remove(next);
                        lastLevel = LevelOf(next);
                    }
                }
                return order;
            }

            /// <summary>A way that needs nothing: walked through on the way, not a step of its own.</summary>
            bool Folds(int i)
            {
                var e = ev[i];
                if (e.Kind != "zone" || e.Link == null || e.Spent.Count > 0) return false;
                string k = e.Link.Kind;
                if (k == "stairs" || k == "teleport" || k == "warp" || k == "drop" || k == "other" || k == "lever" || k == "doorman" || k == "reach") return false;
                return ExplainEvent(i).Trivial;
            }

            int LevelOf(int i)
            {
                var e = ev[i];
                if (e.Mechanism != null) return e.Mechanism.Level;
                if (e.Kind == "zone")
                {
                    var l = LevelsOf(e.Zone);
                    if (l.Count > 0) return l[0];
                    if (e.Link != null && e.Link.Level > 0) return e.Link.Level;
                    return 0;
                }
                var spot = Spot(e.Placement);
                if (spot != null) return spot[0];
                var lz = LevelsOf(e.Zone);
                return lz.Count > 0 ? lz[0] : 0;
            }

            List<int> LevelsOf(int z)
            {
                List<int> l;
                if (levelsOf.TryGetValue(z, out l)) return l;
                try { l = z > ZoneSet.Unassigned ? zs.LevelsOf(z) : new List<int>(); } catch (Exception) { l = new List<int>(); }
                if (l.Count == 0)
                {
                    // a part split off by a change: "part of L7 area at (..) at (x, y)" or "L7 new area at (x, y)"
                    string nm;
                    if (sv.NewZones.TryGetValue(z, out nm)) { var m = Regex.Match(nm, @"\bL(\d+)"); if (m.Success) l = new List<int> { int.Parse(m.Groups[1].Value) }; }
                }
                levelsOf[z] = l;
                return l;
            }

            // ---- words

            static readonly Regex Placed = new Regex(@"^#(\d+) (.*?), level (\d+) \((\d+), (\d+)\)(.*)$", RegexOptions.Singleline);

            /// <summary>Where a placement lies (level, x, y): from its notes, or its fact's spot.</summary>
            int[] Spot(Placement p)
            {
                if (p == null) return null;
                var m = Placed.Match(p.Notes ?? "");
                if (m.Success) return new[] { int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value) };
                var f = FactOf(p);
                FactSpot moved;
                // a fact that follows its object (match): the object it found in these levels
                var at = f != null && f.Match != null ? (t.Abilities.FactObjects.TryGetValue(f.Id, out moved) ? moved : null) : f?.TopSpot;
                if (at == null) return null;
                if (at.HasTile) return new[] { at.Level, at.X, at.Y };
                if (at.HasObject)
                {
                    var lm = Model(at.Level);
                    if (lm != null && at.Object < lm.Objects.Length && !lm.Objects[at.Object].Free && lm.Objects[at.Object].TileX >= 0) return new[] { at.Level, lm.Objects[at.Object].TileX, lm.Objects[at.Object].TileY };
                }
                return null;
            }

            Fact FactOf(Placement p)
            {
                var src = p?.Source ?? "";
                if (!src.StartsWith("facts: ")) return null;
                string id = src.Substring(7);
                return t.Facts.Facts.FirstOrDefault(x => x.Id == id);
            }

            LevelModel Model(int l)
            {
                if (level == null || l < 1) return null;
                LevelModel m;
                if (models.TryGetValue(l, out m)) return m;
                try { m = level(l); } catch (Exception) { m = null; }
                models[l] = m;
                return m;
            }

            /// <summary>A square named as the spoiler log names places: the level and square, and the hand-made zone it lies in or the part of the level.</summary>
            string Where(int l, int x, int y)
            {
                if (l <= 0) return "";
                if (x < 0 || y < 0) return "level " + l;
                string s = string.Format("L{0} ({1}, {2})", l, x, y);
                string n = KeyShuffle.PlaceName(names, l, x, y);
                if (n.Length > 0) return s + " in " + n;
                var m = Model(l);
                return m != null ? s + " in the " + SpoilerNotes.PartOfLevel(m, x, y) : s;
            }

            string ZoneText(int z)
            {
                string n;
                if (sv.NewZones.TryGetValue(z, out n)) return "the new " + n;
                var zone = zs.GetZone(z);
                string name = zone != null && !string.IsNullOrWhiteSpace(zone.Name) ? " (" + zone.Name.Trim() + ")" : "";
                return "zone " + zs.Label(z) + name;
            }

            /// <summary>A token in words.</summary>
            static string Name(string token)
            {
                string head = Head(token), tail = Tail(token).Replace('_', ' ');
                switch (head)
                {
                    case "key": return "key " + tail;
                    case "item": return "the " + tail;
                    case "npc": return "access to " + tail;
                    case "talisman": return "the talisman " + tail;
                    case "rune": return "the " + tail + " rune";
                    case "flag": return "flag " + tail;
                    case "cast": return "the runes to cast " + tail;
                    case "worn": return "an item worn for " + tail;
                    case "perm": return "an item used for " + tail;
                    case "once": return "a single-use " + tail + " (scroll, potion or wand)";
                    case "spell": return "the spell " + tail;
                    default: return token;
                }
            }

            static string Short(TimelineEvent e) { return e.Kind + (e.Token.Length > 0 ? " " + e.Token : " zone " + e.Zone); }

            static string FirstSentence(string s, int max = 220)
            {
                s = (s ?? "").Trim();
                int dot = s.IndexOf(". ", StringComparison.Ordinal);
                if (dot > 0 && dot < max) return s.Substring(0, dot + 1);
                return s.Length <= max ? s : s.Substring(0, max).TrimEnd() + "…";
            }

            string Ref(List<int> d, Dictionary<int, int> stepOf, int self)
            {
                var nums = d.Where(stepOf.ContainsKey).Select(k => stepOf[k]).Distinct().OrderBy(k => k).ToList();
                if (nums.Count == 0) return "";
                if (nums.All(k => k == self)) return " (on the way)";
                return " (step" + (nums.Count == 1 ? " " : "s ") + string.Join(", ", nums.Where(k => k != self)) + ")";
            }

            /// <summary>The why of a step: what it needs and how that was met, each with the step that gave it.</summary>
            string WhyOf(Explained x, Dictionary<int, int> stepOf, WalkStep st, int self, string lead)
            {
                var parts = new List<string>();
                foreach (var p in x.Parts.Where(p => !(p.Trivial && p.Deps.Count == 0)))
                {
                    parts.Add(p.Text + Ref(p.Deps, stepOf, self));
                    foreach (int d in p.Deps) { int k; if (stepOf.TryGetValue(d, out k) && k != self) st.After.Add(k); }
                    if (p.Zone > 0 && !st.Zones.Contains(p.Zone)) st.Zones.Add(p.Zone);
                    if (p.Token != null && !st.Uses.Contains(p.Token)) st.Uses.Add(p.Token);
                    if (p.Option != null && !st.Options.Contains(p.Option)) st.Options.Add(p.Option);
                }
                var sb = new StringBuilder();
                if (lead != null) sb.Append(lead);
                bool free = x.Requires == "free" || x.Requires.Length == 0;
                if (x.Requires.StartsWith("count:")) { }
                else if (free && parts.Count == 0) { if (sb.Length == 0) sb.Append("Needs nothing."); }
                else if (free) { if (sb.Length > 0) sb.Append(' '); sb.Append("Possible because of ").Append(string.Join(" and ", parts)).Append('.'); }
                else
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("Needs ").Append(x.Requires).Append(parts.Count > 0 ? "; met by " + string.Join(" and ", parts) + "." : "; met here.");
                }
                if (x.Problems.Count > 0) sb.Append(" UNEXPLAINED: ").Append(string.Join("; ", x.Problems)).Append('.');
                return sb.ToString();
            }

            void Describe(WalkStep st, int i, Dictionary<int, int> stepOf)
            {
                var e = ev[i];
                var x = ExplainEvent(i);
                int self = st.Number;
                Func<int, string> zoneRef = z => { int j, k; return FirstZone.TryGetValue(z, out j) && stepOf.TryGetValue(j, out k) ? (k == self ? " (on the way)" : " (step " + k + ")") : ""; };
                foreach (int d in Deps(i)) { int k; if (stepOf.TryGetValue(d, out k) && k != self) st.After.Add(k); }
                string spent = e.Spent.Count > 0 ? " Uses up " + string.Join(", ", e.Spent.Select(Name)) + Ref(spentFrom.ContainsKey(i) ? spentFrom[i] : new List<int>(), stepOf, self) + "." : "";
                if (e.Kind == "zone" && i == 0)
                {
                    st.Kind = "start";
                    var s0 = t.Abilities?.Auto?.Start;
                    st.Text = "Start" + (s0 != null ? " at " + Where(s0.Level, (int)s0.X, (int)s0.Y) : "") + ", in " + ZoneText(e.Zone) + ".";
                    return;
                }
                if (e.Kind == "zone")
                {
                    st.Kind = "go";
                    var k = e.Link;
                    if (k == null)
                    {
                        st.Text = "You now stand in " + ZoneText(e.Zone) + ": the change of the step before split it off.";
                        st.Why = "Where the player stood when the change went off.";
                        return;
                    }
                    string where = k.Level > 0 && k.X >= 0 ? Where(k.Level, k.X, k.Y) : k.Level > 0 ? "level " + k.Level : "";
                    string at = where.Length > 0 ? " at " + where : "";
                    string into = ZoneText(e.Zone);
                    var toLevels = LevelsOf(e.Zone);
                    string notes = (k.Notes ?? "").Trim();
                    switch (k.Kind)
                    {
                        case "stairs":
                            // a teleport whose move trigger lies in a wall is gone through by walking into the wall (UW2 level 1 (37, 39), into Nystul's room)
                            if (InWall(k)) st.Text = "Walk into the wall" + at + ": it teleports you to level " + (toLevels.Count > 0 ? toLevels[0].ToString() : "?") + ", into " + into + ".";
                            else st.Text = "Take the stairs" + at + " to level " + (toLevels.Count > 0 ? toLevels[0].ToString() : "?") + ", into " + into + ".";
                            break;
                        case "teleport": st.Text = "Step on the teleport" + at + ", to " + into + "."; break;
                        case "door": st.Text = "Go through the door" + at + " into " + into + "."; break;
                        case "portcullis": st.Text = "Go through the portcullis" + at + " into " + into + "."; break;
                        case "secret": st.Text = "Go through the secret door" + at + " into " + into + (notes.Length > 0 ? " (" + notes + ")" : "") + "."; break;
                        case "drop": st.Text = "Drop down" + at + " into " + into + (notes.Length > 0 ? " (" + notes + ")" : "") + "."; break;
                        case "climb": st.Text = "Climb up" + at + " to " + into + "."; break;
                        case "jump": st.Text = "Jump" + at + " to " + into + (notes.Length > 0 ? " (" + notes + ")" : "") + "."; break;
                        case "reach": st.Text = "Reach" + at + " into " + into + (notes.Length > 0 ? " (" + notes + ")" : "") + "."; break;
                        case "warp":
                            {
                                var f = e.Warp;
                                st.Text = "Warp to " + into + (f != null ? ": " + FirstSentence(f.Notes).TrimEnd('.') : "") + ".";
                                break;
                            }
                        default:
                            {
                                var f = notes.StartsWith("facts: ") ? t.Facts.Facts.FirstOrDefault(y => y.Id == notes.Substring(7)) : null;
                                if (f != null) st.Text = "Go into " + into + at + ": " + FirstSentence(f.Notes);
                                else st.Text = (k.Kind == "walk" ? "Walk" : "Go (" + k.Kind + ")") + " into " + into + at + (notes.Length > 0 ? " (" + notes + ")" : "") + ".";
                                break;
                            }
                    }
                    string lead = e.From >= 0 ? "From " + ZoneText(e.From) + zoneRef(e.From) + "." : null;
                    st.Why = WhyOf(x, stepOf, st, self, lead) + spent;
                    st.Warning = LinkWarning(k);
                    var leave = sv.Leaves.FirstOrDefault(x => x.Event == i);
                    if (leave != null)
                    {
                        string world = KeyShuffle.Uw2WorldName(leave.Levels.Min());
                        st.Note = "Leave " + world + " now: every step the logic needs from it is done. You cannot come back until " + leave.Opens + "; the steps there after this one wait for that.";
                    }
                    return;
                }
                if (e.Kind == "flag")
                {
                    st.Kind = "set off";
                    var m = e.Mechanism;
                    string trig = "";
                    var lm = Model(m.Level);
                    if (lm != null && m.Trigger > 0 && m.Trigger < lm.Objects.Length && lm.Objects[m.Trigger].TileX >= 0) trig = " at " + Where(m.Level, lm.Objects[m.Trigger].TileX, lm.Objects[m.Trigger].TileY);
                    else trig = " on level " + m.Level;
                    st.Text = "Set off the trigger #" + m.Trigger + trig + ": " + (m.What ?? "").Trim() + " (" + e.Token + ").";
                    st.Why = WhyOf(x, stepOf, st, self, null) + spent;
                    return;
                }
                // a placement: a token, a part of a count, another single-use source
                var p = e.Placement;
                var sp = Spot(p);
                string place = sp != null ? Where(sp[0], sp[1], sp[2]) : "";
                var f2 = FactOf(p);
                var pm = Placed.Match(p?.Notes ?? "");
                string zoneLead = "In " + ZoneText(e.Zone) + zoneRef(e.Zone) + ".";
                if (e.Requires.StartsWith("count:"))
                {
                    st.Kind = "take";
                    string id = e.Requires.Substring(6);
                    var cf = t.Facts.Facts.FirstOrDefault(y => y.Kind == "count" && y.Id == id);
                    var parts = counts.ContainsKey(id) ? counts[id].Where(k => k < i).ToList() : new List<int>();
                    st.Text = "You now have " + Name(e.Token) + ": the pieces counted for " + id + " reach " + (cf != null ? cf.AtLeast.ToString() : "the total") + ".";
                    st.Why = "Counted in" + Ref(parts, stepOf, self) + ".";
                    foreach (int d in parts) { int k; if (stepOf.TryGetValue(d, out k) && k != self) st.After.Add(k); }
                    return;
                }
                string what;
                bool fight = false;
                if (pm.Success)
                {
                    string name = Regex.Replace(pm.Groups[2].Value, @"#\d+ ", "").Trim();
                    string rest = pm.Groups[6].Value.Trim().TrimStart(';').Trim();
                    rest = rest.Length == 0 ? "" : rest.StartsWith("(") ? " " + FirstSentence(rest, 200) : ": " + FirstSentence(rest, 200);
                    string head = Head(e.Token);
                    // a thing taken always gets "the" (the Ylem stone, the Key of Courage); a creature dealt with only when not named (the golem, Tyball)
                    string thing = Article(name, true), who = Article(name, false);
                    if (e.Kind == "count") what = "Take " + thing + " at " + place + rest;
                    else if (e.Kind == "use") what = "Take " + thing + " at " + place + rest + ": another use of " + Name(e.Token);
                    else if (head == "npc") what = "Meet " + name + " at " + place + " (" + e.Token + ")";
                    else if (head == "flag") { what = (e.Token.EndsWith("_dead") ? "Kill " : "Deal with ") + who + " at " + place + rest + " (" + e.Token + ")"; fight = pm.Groups[2].Value.Length > 0 && Model(sp != null ? sp[0] : 0) is LevelModel fm && int.TryParse(pm.Groups[1].Value, out int fo) && fo < fm.Objects.Length && fm.Objects[fo].IsCritter; }
                    else what = "Take " + thing + " at " + place + rest + " (" + e.Token + ")";
                }
                // a fact that follows its object (match): the thing where it lies now, not the notes, which say where it lay in the shipped game
                else if (f2 != null && f2.Match != null && t.Abilities.FactObjects.TryGetValue(f2.Id, out FactSpot fo2) && Model(fo2.Level) is LevelModel om && fo2.Object >= 0 && fo2.Object < om.Objects.Length)
                    what = "Take " + Article(om.Objects[fo2.Object].Name, true) + " at " + place + " (" + e.Token + ")";
                else if (f2 != null)
                    what = (place.Length > 0 ? "At " + place + ": " : "") + FirstSentence(f2.Notes).TrimEnd('.') + " (" + e.Token + ")";
                else if (e.Token.StartsWith("cast:"))
                    what = "You can now cast " + (p?.Notes ?? Tail(e.Token)) + " (" + e.Token + ")";
                else
                    what = "Get " + Name(e.Token) + (place.Length > 0 ? " at " + place : "") + (string.IsNullOrWhiteSpace(p?.Notes) ? "" : ": " + FirstSentence(p.Notes)) + " (" + e.Token + ")";
                st.Kind = e.Kind == "token" ? "take" : e.Kind;
                st.Text = what.TrimEnd('.') + ".";
                // a spell's runes and what is done in the inventory lie in the start zone: no need to go there
                bool anywhere = e.Zone == zs.StartZone && (e.Token.StartsWith("cast:") || f2 != null && sp == null || p != null && (p.Source ?? "") == "facts");
                st.Why = WhyOf(x, stepOf, st, self, anywhere ? null : zoneLead) + spent + (fight ? " A fight: the logic takes it as won, whatever the creature." : "");
                st.Warning = LostWarning(p);
            }

            /// <summary>
            /// The warning for a way of the facts that may be lost: one taken only once (ZoneLink.Once,
            /// the Historian's teleport) or with a mayBeLost of kind a (Bishop and the gauntlets); "".
            /// </summary>
            string LinkWarning(ZoneLink k)
            {
                string notes = (k?.Notes ?? "").Trim();
                if (!notes.StartsWith("facts: ")) return "";
                string id = notes.Substring(7);
                int sp = id.IndexOf(' ');
                if (sp > 0) id = id.Substring(0, sp);
                var f = t.Facts.Facts.FirstOrDefault(y => y.Id == id && y.Kind == "link");
                if (f == null) return "";
                string why = (f.LostWhy ?? "").Trim().TrimEnd('.');
                if (f.Lost == "a" && why.Length > 0) return "this may be lost for good: " + why + ".";
                if (f.Once || k.Once) return "this way can be taken only once: take what lies where it leads before you leave.";
                return "";
            }

            /// <summary>
            /// The warning for a source the game may take away (the facts' mayBeLost): kind a, lost
            /// only by a choice, says what not to do; kinds b and c, which the logic counts only
            /// inside their window, say until when.
            /// </summary>
            static string LostWarning(Placement p)
            {
                if (p == null || string.IsNullOrEmpty(p.Lost)) return "";
                string why = (p.LostWhy ?? "").Trim().TrimEnd('.');
                if (p.Lost == "a") return "this may be lost for good: " + why + ".";
                return "this is only there for a time" + (string.IsNullOrEmpty(p.LostWhen) ? "" : " (gone at " + p.LostWhen + ")") + ": " + why + ".";
            }

            // ---- the reverse check

            /// <summary>What a step names, and nothing else: the tokens it uses, the zones it names, the logic's options.</summary>
            sealed class Named : IRequirementContext, INowContext
            {
                public SolverOptions So;
                public readonly HashSet<string> Tokens = new HashSet<string>();
                public readonly HashSet<int> Zones = new HashSet<int>();
                public SolverOptions Options { get { return So; } }
                public bool HasToken(string t) { return Tokens.Contains(Requirement.Normalize(t)); }
                public bool ZoneReachable(int z) { return Zones.Contains(z); }
                public bool ZoneNow(int z) { return Zones.Contains(z); }
                int Most(string head) { int best = 0, n; foreach (var t in Tokens) if (t.StartsWith(head) && int.TryParse(t.Substring(head.Length), out n) && n > best) best = n; return best; }
                public int TalismanCount() { return Most("talismans:"); }
                public int PlotClock() { return Most("plot:"); }
            }

            /// <summary>
            /// The reverse check (Walkthrough.ReverseProblems): from the goal back to the start.
            /// A step's needs (the tokens it uses, the zones it is done in or names, the pieces of
            /// a count) are owed; going back, a step that gives a thing pays what is owed of it: a
            /// lasting thing all at once, a thing that is used up one use a piece, a talisman one
            /// of the number owed. What is left owed at the start was needed before it was gained,
            /// or more often than it was found. Each step's requirement is also read again with
            /// only what the step names.
            /// </summary>
            void Reverse(string goalReq)
            {
                w.ReverseRan = true;
                var index = new Dictionary<TimelineEvent, int>();
                for (int i = 0; i < ev.Count; i++) index[ev[i]] = i;
                var spendable = new HashSet<string>(ev.SelectMany(e => e.Spent));
                var owed = new Dictionary<string, List<int>>();
                var units = new Dictionary<string, List<int>>();
                var talismans = new List<int>();
                var paidTalismans = new HashSet<string>();
                Action<Dictionary<string, List<int>>, string, int> owe = (d, key, step) => { List<int> l; if (!d.TryGetValue(key, out l)) d[key] = l = new List<int>(); l.Add(step); };
                Func<string, bool> pay = key =>
                {
                    bool some = false;
                    List<int> l;
                    if (owed.TryGetValue(key, out l)) { w.ReversePaid += l.Count; owed.Remove(key); some = true; }
                    if (units.TryGetValue(key, out l) && l.Count > 0) { l.RemoveAt(l.Count - 1); if (l.Count == 0) units.Remove(key); w.ReversePaid++; some = true; }
                    if (key.StartsWith("talisman:") && talismans.Count > 0 && paidTalismans.Add(key)) { talismans.RemoveAt(talismans.Count - 1); w.ReversePaid++; some = true; }
                    return some;
                };
                for (int n = w.Steps.Count - 1; n >= 0; n--)
                {
                    var st = w.Steps[n];
                    bool used = st.Kind == "goal";
                    // the step's own needs: for the goal, the goal's zone and requirement
                    Action<string, int, int, TimelineEvent> own = (req, place, gives, e) =>
                    {
                        foreach (var tok in st.Uses)
                        {
                            int k;
                            if (tok.StartsWith("talismans:") && int.TryParse(tok.Substring(10), out k)) { for (int j = 0; j < k; j++) talismans.Add(st.Number); paidTalismans.Clear(); }
                            else if (tok.StartsWith("plot:")) { }
                            else if (e != null && e.Spent.Contains(tok)) { }
                            else owe(owed, tok, st.Number);
                        }
                        if (e != null) foreach (var tok in e.Spent) owe(units, tok, st.Number);
                        foreach (int z in st.Zones) if (z != gives) owe(owed, "zone:" + z, st.Number);
                        if (req.StartsWith("count:"))
                        {
                            string id = req.Substring(6);
                            int have = counts.ContainsKey(id) && e != null ? counts[id].Count(k => k < index[e]) : 0;
                            for (int j = 0; j < have; j++) owe(units, "part:" + id, st.Number);
                            return;
                        }
                        // the requirement with only what the step names
                        var ctx = new Named { So = So };
                        foreach (var t0 in Start) ctx.Tokens.Add(t0);
                        foreach (var t0 in st.Uses) ctx.Tokens.Add(Requirement.Normalize(t0));
                        if (e != null) foreach (var t0 in e.Spent) ctx.Tokens.Add(t0);
                        foreach (int z in st.Zones) ctx.Zones.Add(z);
                        if (place > 0) ctx.Zones.Add(place);
                        var r = Requirement.TryParse(string.IsNullOrWhiteSpace(req) ? "free" : req, out _);
                        if (r != null && !r.Eval(ctx)) w.ReverseProblems.Add(string.Format("Step {0}: what it names ({1}) does not meet its requirement {2}.", st.Number, string.Join(", ", st.Uses.Concat(st.Zones.Select(z => "zone:" + z)).DefaultIfEmpty("nothing")), req));
                    };
                    if (st.Kind == "goal")
                    {
                        owe(owed, "zone:" + sv.GoalAt, st.Number);
                        own(goalReq ?? "", sv.GoalAt, -1, null);
                    }
                    for (int k = st.Events.Count - 1; k >= 0; k--)
                    {
                        var e = st.Events[k];
                        int i = index[e];
                        // what it gives
                        if (e.Kind == "zone") used |= pay("zone:" + e.Zone);
                        else if (e.Kind == "count") used |= pay("part:" + e.Token);
                        else if (e.Token.Length > 0) used |= pay(e.Token);
                        if (i == 0) { used = true; continue; }
                        // where it is done, and for the step's own event what it needs
                        int place = PlaceOf(i);
                        if (place > 0) owe(owed, "zone:" + place, st.Number);
                        if (e == st.Event) own(e.Requires ?? "", place, e.Kind == "zone" ? e.Zone : -1, e);
                    }
                    if (!used) w.ReverseUnused.Add("step " + st.Number + " (" + st.Text + ")");
                }
                foreach (var kv in owed.OrderBy(x => x.Value.Min()))
                {
                    if (Start.Contains(kv.Key)) { w.ReversePaid += kv.Value.Count; continue; }
                    w.ReverseProblems.Add(string.Format("{0} is needed by step{1} {2}, and no step before gives it.", kv.Key, kv.Value.Count == 1 ? "" : "s", string.Join(", ", kv.Value.Distinct().OrderBy(x => x))));
                }
                foreach (var kv in units.OrderBy(x => x.Key, StringComparer.Ordinal))
                    w.ReverseProblems.Add(string.Format("{0}: {1} more use{2} than pieces found before (step{3} {4}).", kv.Key, kv.Value.Count, kv.Value.Count == 1 ? "" : "s", kv.Value.Count == 1 ? "" : "s", string.Join(", ", kv.Value.Distinct().OrderBy(x => x))));
                if (talismans.Count > 0) w.ReverseProblems.Add(string.Format("{0} more talisman{1} needed than taken before (step {2}).", talismans.Count, talismans.Count == 1 ? "" : "s", string.Join(", ", talismans.Distinct())));
            }

            // ---- from one step to the next

            /// <summary>Where an event is done: the zone the player must stand in, or -1 when it is done anywhere (a spell's runes, a thing made in the pack, a state of the game).</summary>
            int PlaceOf(int i)
            {
                var e = ev[i];
                if (i == 0) return -1;
                if (e.Kind == "zone") return e.Link == null || e.Warp != null ? -1 : e.From;
                if (e.Kind == "flag")
                {
                    var z = ExplainEvent(i).Parts.Where(p => p.Zone > 0).Select(p => p.Zone).ToList();
                    return z.Count > 0 ? z[0] : -1;
                }
                if (e.Requires.StartsWith("count:")) return -1;
                var p0 = e.Placement;
                string src = p0 == null ? "" : p0.Source ?? "";
                if (src == "state") return -1;
                if (e.Zone == zs.StartZone && (e.Token.StartsWith("cast:") || src == "facts" || (FactOf(p0) != null && Spot(p0) == null))) return -1;
                return e.Zone;
            }

            /// <summary>
            /// The player's way through the steps: before each one done in another zone than the
            /// one he stands in, the way there over the links of the world as it stood (after the
            /// step's sphere, or the one before), through zones the solve reaches, with what he
            /// holds by then. Single-use items are not counted on the way back. No way found: the
            /// step goes to NoWay.
            /// </summary>
            void Routes(List<WalkStep> steps, HashSet<int> need)
            {
                var index = new Dictionary<TimelineEvent, int>();
                for (int i = 0; i < ev.Count; i++) index[ev[i]] = i;
                var wk = new Walk { Here = ev[0].Zone, NotYet = new HashSet<int>(need) };
                foreach (var st in steps)
                {
                    foreach (var e in st.Events)
                    {
                        string from, to;
                        if (!Move(wk, index[e], st.Route, out from, out to))
                            w.NoWay.Add("Step " + st.Number + ": no way from " + from + " to " + to + ".");
                    }
                    st.EndsIn = wk.Here;
                }
                w.Walked = true;
            }

            /// <summary>
            /// The player on his walk through the steps: the zone he stands in, the last of the
            /// solve's events walked (what he holds: what the solve had gained by then, less the
            /// walkthrough's events still to come), the sphere of the world as it stands.
            /// </summary>
            sealed class Walk
            {
                public int Here, Held, Sphere;
                public HashSet<int> NotYet;
                public Walk Copy() { return new Walk { Here = Here, Held = Held, Sphere = Sphere, NotYet = new HashSet<int>(NotYet) }; }
            }

            /// <summary>
            /// One event of the walk: the way from where the player stands to where the event is
            /// done (into route, when given), then where it leaves him. False when there is no way
            /// there (from and to name the zones).
            /// </summary>
            bool Move(Walk wk, int i, List<string> route, out string fromText, out string toText)
            {
                fromText = toText = "";
                var e = ev[i];
                int want = PlaceOf(i);
                bool ok = true;
                wk.Sphere = Math.Max(wk.Sphere, e.Sphere);
                if (want > 0 && want != wk.Here)
                {
                    int here = wk.Here, from0 = here, sphere = wk.Sphere;
                    // what is held on the way: all that the steps so far gave (a way listed "on the way" may come early in the solve's
                    // record, before a key a step before it in the walkthrough took)
                    int cut = Math.Max(i, wk.Held + 1);
                    var hops = Route(here, want, cut, sphere, wk.NotYet) ?? Route(here, want, cut, sphere - 1, wk.NotYet);
                    // a zone entered by a way from a zone he cannot get back to (a one-way teleport took him on): another
                    // way straight into the zone the step leads to does as well (the Academy's level 46: the teleport at
                    // (60, 39) to the room at (53, 31), whose own stairs lead to level 41 as the step's stairs do)
                    List<KeyValuePair<ZoneLink, int>> into = null;
                    if (hops == null && e.Kind == "zone" && e.Zone != here && (e.Link == null || e.Link.Kind != "reach"))
                        into = Route(here, e.Zone, cut, sphere, wk.NotYet) ?? Route(here, e.Zone, cut, sphere - 1, wk.NotYet);
                    // no way on foot: a warp that asks for nothing held may be part of the way (UW2: dying outside Britannia)
                    if (hops == null && into == null && sv.FreeWarps.Count > 0) hops = Route(here, want, cut, sphere, wk.NotYet, true) ?? Route(here, want, cut, sphere - 1, wk.NotYet, true);
                    if (into != null)
                    {
                        if (route != null)
                        {
                            var texts = into.Select(HopText).ToList();
                            if (texts.Count > 9) texts = texts.Take(4).Concat(new[] { "… " + (texts.Count - 8) + " more …" }).Concat(texts.Skip(texts.Count - 4)).ToList();
                            route.Add("by another way from " + ZoneText(here) + " (" + ZoneText(want) + ", where the step starts, is behind you): " + string.Join("; ", texts));
                        }
                        hops = into;
                        want = e.Zone;
                    }
                    else if (hops == null)
                    {
                        ok = false;
                        fromText = ZoneText(here);
                        toText = ZoneText(want);
                        if (route != null) route.Add("NO WAY FOUND from " + fromText + " to " + toText + " with what is held by then: check this.");
                    }
                    else if (route != null)
                    {
                        var texts = hops.Select(HopText).ToList();
                        if (texts.Count > 9) texts = texts.Take(4).Concat(new[] { "… " + (texts.Count - 8) + " more …" }).Concat(texts.Skip(texts.Count - 4)).ToList();
                        route.Add("the way there from " + ZoneText(here) + ": " + string.Join("; ", texts));
                    }
                    wk.Here = want;
                    // a zone the hand reaches from afar is not stood in: he stays where he reached from
                    if (hops != null && hops.Count > 0 && hops[hops.Count - 1].Key.Kind == "reach") wk.Here = hops.Count > 1 ? hops[hops.Count - 2].Value : from0;
                }
                if (e.Kind == "zone") wk.Here = e.Link != null && e.Link.Kind == "reach" && e.From > 0 ? e.From : e.Zone;
                wk.Held = Math.Max(wk.Held, i);
                wk.NotYet.Remove(i);
                return ok;
            }

            /// <summary>The folded ways (Folds) the events of `from` rest on that no step has taken on its way yet, now taken by this one, in the order of pos.</summary>
            List<int> OnTheWay(IEnumerable<int> from, HashSet<int> folded, HashSet<int> attached, Dictionary<int, int> pos)
            {
                var mine = new List<int>();
                var st2 = new Stack<int>(from.Where(folded.Contains));
                while (st2.Count > 0)
                {
                    int f = st2.Pop();
                    if (!attached.Add(f)) continue;
                    mine.Add(f);
                    foreach (int g in Deps(f)) if (folded.Contains(g)) st2.Push(g);
                }
                return pos == null ? mine.OrderBy(f => f).ToList() : mine.OrderBy(f => pos[f]).ToList();
            }

            /// <summary>The steps an order makes (as Run makes them): each step's events, the ways on the way first.</summary>
            List<List<int>> Units(List<int> order, HashSet<int> folded)
            {
                var pos = new Dictionary<int, int>();
                for (int k = 0; k < order.Count; k++) pos[order[k]] = k;
                var attached = new HashSet<int>();
                var units = new List<List<int>>();
                foreach (int i in order)
                {
                    if (folded.Contains(i)) continue;
                    var u = OnTheWay(Deps(i), folded, attached, pos);
                    u.Add(i);
                    units.Add(u);
                }
                return units;
            }

            /// <summary>Whether the player can walk an order's steps (as Routes walks them) without a step he has no way to.</summary>
            bool Walks(List<int> order, HashSet<int> need, HashSet<int> folded)
            {
                var wk = new Walk { Here = ev[0].Zone, NotYet = new HashSet<int>(need) };
                string a, b;
                foreach (var u in Units(order, folded))
                    foreach (int i in u)
                        if (!Move(wk, i, null, out a, out b)) return false;
                return true;
            }

            sealed class Frame
            {
                public int Choice;
                public Walk Before;
                public HashSet<int> Done, Attached, Tried;
                /// <summary>The steps still to do that the player could get to before this one and cannot after it.</summary>
                public HashSet<int> Lost;
            }

            /// <summary>
            /// The order made by walking it: the player's zone and what he holds are followed step by
            /// step. Of the steps whose needs are met and that he can get to, the one taken is the
            /// first in the order by spheres that leaves behind no other step still to do: one he
            /// can get to now and could not get back to afterwards with what he holds then (a
            /// one-way way, the leaving of a shut world). When every step leaves something behind,
            /// the one that leaves the least. When the walk gets stuck (steps left that he cannot
            /// get to), it goes back to the step that left behind what it is stuck on and takes
            /// another there. Null when no walk is found within the bound.
            /// </summary>
            List<int> Reorder(List<int> order, HashSet<int> need, HashSet<int> folded)
            {
                var steps = order.Where(i => i != 0 && !folded.Contains(i)).ToList();
                var wk = new Walk { Here = ev[0].Zone, NotYet = new HashSet<int>(need) };
                string a, b;
                if (!Move(wk, 0, null, out a, out b)) return null;
                var done = new HashSet<int> { 0 };
                var attached = new HashSet<int>();
                var tried = new HashSet<int>();
                var frames = new List<Frame>();
                var units = new List<List<int>>();
                int bound = 4 * steps.Count + 400;
                while (done.Count < steps.Count + 1)
                {
                    if (--bound < 0) return null;
                    var pending = steps.Where(i => !done.Contains(i)).ToList();
                    var reach = Reach(wk);
                    var open = new HashSet<int>(pending.Where(p => CanGetTo(p, reach)));
                    int best = -1, bestLoss = int.MaxValue;
                    Walk bestWalk = null;
                    List<int> bestUnit = null;
                    HashSet<int> bestLost = null;
                    foreach (int c in pending)
                    {
                        if (tried.Contains(c) || !Deps(c).All(d => done.Contains(d) || folded.Contains(d))) continue;
                        var unit = OnTheWay(Deps(c), folded, new HashSet<int>(attached), null);
                        unit.Add(c);
                        var inUnit = new HashSet<int>(unit);
                        if (!unit.All(e => Deps(e).All(d => done.Contains(d) || inUnit.Contains(d) || !need.Contains(d)))) continue;
                        var w2 = wk.Copy();
                        bool ok = true;
                        foreach (int e in unit) if (!Move(w2, e, null, out a, out b)) { ok = false; break; }
                        if (!ok) continue;
                        var after = Reach(w2);
                        var lost = new HashSet<int>(open.Where(p => p != c && !CanGetTo(p, after)));
                        if (lost.Count < bestLoss) { best = c; bestLoss = lost.Count; bestWalk = w2; bestUnit = unit; bestLost = lost; }
                        if (lost.Count == 0) break;
                    }
                    if (best < 0)
                    {
                        // stuck: back to the latest step that left behind a step it cannot get to now, and another step there
                        var stuck = new HashSet<int>(pending.Where(p => !open.Contains(p)));
                        int k = frames.Count - 1;
                        while (k >= 0 && !frames[k].Lost.Overlaps(stuck)) k--;
                        if (k < 0) k = frames.Count - 1;
                        if (k < 0) return null;
                        var f = frames[k];
                        frames.RemoveRange(k, frames.Count - k);
                        units.RemoveRange(k, units.Count - k);
                        wk = f.Before; done = f.Done; attached = f.Attached;
                        tried = f.Tried;
                        tried.Add(f.Choice);
                        continue;
                    }
                    frames.Add(new Frame { Choice = best, Before = wk, Done = new HashSet<int>(done), Attached = new HashSet<int>(attached), Tried = tried, Lost = bestLost });
                    units.Add(bestUnit);
                    wk = bestWalk;
                    foreach (int e in bestUnit) { done.Add(e); if (folded.Contains(e)) attached.Add(e); }
                    tried = new HashSet<int>();
                }
                var result = new List<int> { 0 };
                foreach (var u in units) result.AddRange(u);
                // the ways no step takes on its way (the goal's)
                result.AddRange(order.Where(i => !result.Contains(i)));
                return result;
            }

            /// <summary>Whether the player can get to where a step is done from the zones he can get to (or straight into the zone a way of it leads to).</summary>
            bool CanGetTo(int i, HashSet<int> reach)
            {
                int want = PlaceOf(i);
                if (want <= 0 || reach.Contains(want)) return true;
                var e = ev[i];
                return e.Kind == "zone" && (e.Link == null || e.Link.Kind != "reach") && reach.Contains(e.Zone);
            }

            /// <summary>The zones the player can get to from where he stands, with what he holds (over the world of his sphere or the one before).</summary>
            HashSet<int> Reach(Walk wk)
            {
                var r = Search(wk.Here, -1, wk.Held + 1, wk.Sphere, wk.NotYet, null);
                if (wk.Sphere > 0) r.UnionWith(Search(wk.Here, -1, wk.Held + 1, wk.Sphere - 1, wk.NotYet, null));
                return r;
            }

            string HopText(KeyValuePair<ZoneLink, int> hop)
            {
                var k = hop.Key;
                string req = string.IsNullOrWhiteSpace(k.Requires) || k.Requires.Trim() == "free" ? "" : " [" + k.Requires.Trim() + "]";
                string at = k.Level > 0 && k.X >= 0 ? string.Format(" L{0} ({1}, {2})", k.Level, k.X, k.Y) : "";
                if (k.Kind == "warp" && (k.Notes ?? "").StartsWith("facts: "))
                {
                    var f = t.Facts.Facts.FirstOrDefault(y => y.Id == k.Notes.Substring(7));
                    if (f != null) return FirstSentence(f.Notes) + " > " + zs.Label(hop.Value);
                }
                return (k.Kind ?? "way") + at + req + " > " + zs.Label(hop.Value);
            }

            /// <summary>Whether a warp can be made from a zone: one of its level (FromLevel), or of a level above FromAbove.</summary>
            bool WarpFrom(Fact f, int z)
            {
                var ls = LevelsOf(z);
                if (f.FromLevel > 0 && !ls.Contains(f.FromLevel)) return false;
                return f.FromAbove <= 0 || ls.Any(l => l > f.FromAbove);
            }

            readonly Dictionary<string, Requirement> parsed = new Dictionary<string, Requirement>();
            readonly Dictionary<List<ZoneLink>, Dictionary<int, List<KeyValuePair<ZoneLink, int>>>> ways = new Dictionary<List<ZoneLink>, Dictionary<int, List<KeyValuePair<ZoneLink, int>>>>();
            readonly Dictionary<List<ZoneLink>, Dictionary<int, List<KeyValuePair<ZoneLink, int>>>> waysWarp = new Dictionary<List<ZoneLink>, Dictionary<int, List<KeyValuePair<ZoneLink, int>>>>();

            /// <summary>
            /// The shortest way from one zone to another over a sphere's links, through zones the solve reaches, with what was held
            /// before event `cut`; null when there is none. With warps, the solve's free warps (TimelineSolve.FreeWarps) count as ways.
            /// </summary>
            List<KeyValuePair<ZoneLink, int>> Route(int from, int to, int cut, int sphere, HashSet<int> notYet, bool warps = false)
            {
                if (sphere < 0) return null;
                var prev = new Dictionary<int, KeyValuePair<ZoneLink, int>>();
                if (!Search(from, to, cut, sphere, notYet, prev, warps).Contains(to)) return null;
                var path = new List<KeyValuePair<ZoneLink, int>>();
                for (int z = to; z != from; z = prev[z].Value) path.Add(new KeyValuePair<ZoneLink, int>(prev[z].Key, z));
                path.Reverse();
                return path;
            }

            /// <summary>The zones reached from one over a sphere's links, through zones the solve reaches (and to), with what was held before event `cut`; stops once `to` is reached. prev: the link each was reached by.</summary>
            HashSet<int> Search(int from, int to, int cut, int sphere, HashSet<int> notYet, Dictionary<int, KeyValuePair<ZoneLink, int>> prev, bool warps = false)
            {
                var seen = new HashSet<int> { from };
                if (sphere < 0) return seen;
                var links = sphere < sv.Spheres.Count && sv.Spheres[sphere].Links != null ? sv.Spheres[sphere].Links : sv.Spheres.Count > 0 && sv.Spheres[sv.Spheres.Count - 1].Links != null ? sv.Spheres[sv.Spheres.Count - 1].Links : zs.Links;
                Dictionary<int, List<KeyValuePair<ZoneLink, int>>> outOf;
                var cache = warps ? waysWarp : ways;
                if (!cache.TryGetValue(links, out outOf))
                {
                    cache[links] = outOf = new Dictionary<int, List<KeyValuePair<ZoneLink, int>>>();
                    Action<int, int, ZoneLink> way = (x, z, k) =>
                    {
                        List<KeyValuePair<ZoneLink, int>> l;
                        if (!outOf.TryGetValue(x, out l)) outOf[x] = l = new List<KeyValuePair<ZoneLink, int>>();
                        l.Add(new KeyValuePair<ZoneLink, int>(k, z));
                    };
                    // a way the game allows only once is the step that takes it, never a stretch of a way there
                    foreach (var k in links) { if (k.Once) continue; way(k.From, k.To, k); if (k.TwoWay) way(k.To, k.From, k); }
                    if (warps)
                        foreach (var fw in sv.FreeWarps)
                        {
                            var wl = new ZoneLink { From = 0, To = fw.Value, Kind = "warp", Requires = "free", Notes = "facts: " + fw.Key.Id };
                            // from every zone of the zone set that the warp can be made from (the cache serves every start of a search)
                            foreach (int a in zs.Zones.Select(z => z.Id).Concat(FirstZone.Keys).Distinct().ToList())
                                if (a > 0 && a != fw.Value && WarpFrom(fw.Key, a)) way(a, fw.Value, wl);
                        }
                }
                var ctx = new At { B = this, Cut = cut, NotYet = notYet };
                var queue = new Queue<int>();
                queue.Enqueue(from);
                while (queue.Count > 0 && !seen.Contains(to))
                {
                    int a = queue.Dequeue();
                    List<KeyValuePair<ZoneLink, int>> l;
                    if (!outOf.TryGetValue(a, out l)) continue;
                    foreach (var h in l)
                    {
                        // through any zone the solve reaches at some time: one it lists later may well be walked through now, when the ways to it hold
                        if (seen.Contains(h.Value) || (h.Value != to && !FirstZone.ContainsKey(h.Value))) continue;
                        string rq = string.IsNullOrWhiteSpace(h.Key.Requires) ? "free" : h.Key.Requires;
                        Requirement r;
                        if (!parsed.TryGetValue(rq, out r)) parsed[rq] = r = Requirement.TryParse(rq, out _);
                        if (r == null || !r.Eval(ctx)) continue;
                        seen.Add(h.Value);
                        if (prev != null) prev[h.Value] = new KeyValuePair<ZoneLink, int>(h.Key, a);
                        queue.Enqueue(h.Value);
                    }
                }
                return seen;
            }

            /// <summary>A name with "the" before it, unless it has an article already, or it is a proper name and not a thing.</summary>
            static string Article(string name, bool thing)
            {
                if (name.Length == 0) return name;
                if ((!thing && char.IsUpper(name[0])) || name.StartsWith("the ") || name.StartsWith("a ") || name.StartsWith("an ")) return name;
                return "the " + name;
            }

            static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
        }
    }
}
