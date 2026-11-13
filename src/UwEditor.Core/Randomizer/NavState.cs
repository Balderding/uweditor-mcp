using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// A test a UW2 check variable trap makes of the numbered variables (event/TRIGGER.C UseTrap,
    /// TRAP_CHECK_VARIABLE): the variables First..First + Count, added up (Sum) or joined three
    /// bits each, equal to Value; Negated for the trap's other chain, which runs when they differ.
    /// The numbers: 0-0xFF the variables, 0x100-0x17F the quest flags, 0x180-0x18F the quest
    /// bytes, 0x190-0x19F the X clocks (set_numbered_variable).
    /// </summary>
    public sealed class StateTest
    {
        public int First, Count;
        public bool Sum;
        public int Value;
        public bool Negated;

        /// <summary>The name of a numbered variable: var52, quest109, qbyte3, xclock1.</summary>
        public static string Name(int i)
        {
            return i < 0x100 ? "var" + i : i < 0x180 ? "quest" + (i - 0x100) : i < 0x190 ? "qbyte" + (i - 0x180) : "xclock" + (i - 0x190);
        }

        /// <summary>The test of a check variable trap (item 0x18E).</summary>
        public static StateTest Of(ObjectInfo trap, bool negated)
        {
            return new StateTest
            {
                First = trap.Z | (trap.FineX & 3) << 7, Count = trap.Heading, Sum = (trap.FineX >> 2) != 0,
                Value = (trap.Quality << 5 | trap.Owner) << 3 | trap.FineY, Negated = negated,
            };
        }

        /// <summary>flag:state_var2_is_4, flag:state_var200_to_var201_sum_not_0, flag:state_var90_low3_is_1.</summary>
        public string Token
        {
            get
            {
                string name = Name(First) + (Count > 0 ? "_to_" + Name(First + Count) + (Sum ? "_sum" : "_digits") : Sum ? "" : "_low3");
                return "flag:state_" + name + (Negated ? "_not_" : "_is_") + Value;
            }
        }

        /// <summary>Whether the variables pass the trap's comparison (not counting Negated).</summary>
        public bool Equal(Func<int, int> get)
        {
            int v = 0;
            for (int i = First; i <= First + Count; i++)
            {
                if (Sum) v += get(i);
                else v = v << 3 | (get(i) & 7);
            }
            return v == Value;
        }

        /// <summary>Whether the chain this test guards runs in a state.</summary>
        public bool Holds(Func<int, int> get) { return Equal(get) != Negated; }

        public bool Covers(int variable) { return variable >= First && variable <= First + Count; }
    }

    /// <summary>A UW2 set variable trap (item 0x18D) the player can set off, and the flags of the mechanisms that run it.</summary>
    public sealed class StateSet
    {
        public int Level, Trap;
        public int Variable, Op, Value;
        public readonly List<string> Flags = new List<string>();

        public static readonly string[] OpText = { "+=", "-=", "=", "&=", "|=", "^=", "<<=", "counts up from" };

        /// <summary>event/TRIGGER.C do_math_op.</summary>
        public static int Math(int value, int op, int right)
        {
            switch (op)
            {
                case 0: return value + right;
                case 1: return value - right;
                case 2: return right;
                case 3: return value & right;
                case 4: return value | right;
                case 5: return value ^ right;
                case 6: return value << right;
                case 7: return value == right ? value + 1 : 0;
            }
            return value;
        }

        /// <summary>The trap's effect on a state (event/TRIGGER.C set_numbered_variable).</summary>
        public void Apply(Dictionary<int, int> state)
        {
            int v;
            state.TryGetValue(Variable, out v);
            if (Variable < 0x100) v = Math(v, Op, Value) & 0xFF;
            else if (Variable < 0x180) v = Op == 5 ? v ^ 1 : Op == 1 ? v : Value > 0 ? 1 : 0;
            else v = Math(v, Op, Value);
            state[Variable] = v;
        }

        public string Requires { get { return Flags.Count == 1 ? Flags[0] : "(" + string.Join(" | ", Flags) + ")"; } }
        public override string ToString() { return string.Format("L{0} #{1}: {2} {3} {4}", Level, Trap, StateTest.Name(Variable), OpText[Op & 7], Value); }
    }

    /// <summary>A token of the game's state and what grants it: the flags of the mechanisms that bring the state about (null: nothing known does).</summary>
    public sealed class NavState
    {
        public string Token;
        public string Requires;
        public string Notes = "";
    }

    /// <summary>
    /// UW2's state as tokens. The variables all start at 0, so a chain that needs the state the
    /// game starts in is free (the walk gives it no condition); any other state is a token,
    /// granted by the set variable traps that bring it about:
    /// one trap alone, or all of a level's traps on those variables together (a counter that every one
    /// of four pick-ups adds to, a lever puzzle that toggles a bit each).
    /// The castle plot (X clock 1) is a count: plotset:V from a trap that sets it to V,
    /// plotstep:NAME from one that adds 1, and plot:N holds when the highest set plus the steps
    /// reach N (Requirement.PlotClock).
    /// </summary>
    public static class StateRules
    {
        /// <summary>The castle plot's number among the variables (X clock 1).</summary>
        public const int PlotVariable = 0x191;

        public static List<NavState> Derive(IEnumerable<StateTest> tests, List<StateSet> sets)
        {
            var list = new List<NavState>();
            foreach (var t in tests.OrderBy(x => x.Token, StringComparer.Ordinal))
            {
                var mine = sets.Where(x => t.Covers(x.Variable) && x.Flags.Count > 0).ToList();
                var ways = new List<string>();
                var notes = new List<string>();
                foreach (var one in mine)
                {
                    var st = new Dictionary<int, int>();
                    one.Apply(st);
                    if (!Holds(t, st)) continue;
                    ways.Add(one.Requires);
                    notes.Add(one.ToString());
                }
                // all of one level's together, once each (a puzzle is one level's; the Tomb's lies on two): the counting ones in the order they count
                foreach (var steps in mine.Where(x => x.Op != 2).GroupBy(x => x.Level).Select(grp => grp.OrderBy(x => x.Op == 7 ? x.Value : 0).ToList()))
                {
                    if (steps.Count < 2) continue;
                    var st = new Dictionary<int, int>();
                    foreach (var x in steps) x.Apply(st);
                    if (Holds(t, st))
                    {
                        ways.Add(string.Join(" & ", steps.Select(x => x.Requires)));
                        notes.Add("all of: " + string.Join("; ", steps.Select(x => x.ToString())));
                    }
                }
                ways = ways.Distinct().ToList();
                list.Add(new NavState
                {
                    Token = t.Token,
                    Requires = ways.Count == 0 ? null : ways.Count == 1 ? ways[0] : string.Join(" | ", ways.Select(w => w.Contains(" & ") ? "(" + w + ")" : w)),
                    Notes = ways.Count == 0 ? "no set variable trap the player sets off brings this about (a conversation, a schedule or the game's code may)" : string.Join(" / ", notes),
                });
            }
            foreach (var x in sets.Where(x => x.Variable == PlotVariable && x.Flags.Count > 0))
            {
                if (x.Op == 2) list.Add(new NavState { Token = "plotset:" + x.Value, Requires = x.Requires, Notes = "the castle plot: " + x });
                else if (x.Op == 0 && x.Value > 0) for (int i = 0; i < x.Value; i++) list.Add(new NavState { Token = string.Format("plotstep:l{0}_{1}{2}", x.Level, x.Trap, i > 0 ? "_" + i : ""), Requires = x.Requires, Notes = "the castle plot: " + x });
            }
            return list;
        }

        static bool Holds(StateTest t, Dictionary<int, int> state)
        {
            return t.Holds(i => { int v; return state.TryGetValue(i, out v) ? v : 0; });
        }

        /// <summary>
        /// The blackrock gem's facets (event/WORLDEV.C black_gem_trip's worlds[]): level, square,
        /// and the castle plot from which the gem may show the facet (black_gem_rotate picks among
        /// the first 1, 3, 6 or 8 facets with the plot under 4, under 8, under 13, or more).
        /// </summary>
        public static readonly int[][] GemWorlds =
        {
            new[] { 9, 33, 32, 0 }, new[] { 17, 27, 34, 4 }, new[] { 25, 18, 39, 4 }, new[] { 33, 31, 31, 8 },
            new[] { 41, 4, 38, 8 }, new[] { 57, 59, 20, 8 }, new[] { 49, 32, 32, 13 }, new[] { 69, 32, 22, 13 },
        };
    }
}
