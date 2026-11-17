using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using UwEditor.Core.Randomizer;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// One cache for the whole test assembly: the opened game per game, the unshuffled timeline
    /// builds and the key item scatters, each computed once (even with xUnit's classes running in
    /// parallel) and shared by every class that asks for the same thing.
    ///
    /// The rules for the callers:
    /// - What this hands out is shared: never change it. A test that edits levels, a result, its
    ///   facts, zones or placements builds its own.
    /// - The session is for reading (levels, tables, LevelEdits kept in memory); a test that writes
    ///   to its session (SetNewGameStart, saving) opens its own.
    /// - A shared timeline's engine keeps caches that are not thread-safe: anything that solves
    ///   again on a shared build (Timeline.Solve, Walkthrough.Build, the engine, KeyShuffle helpers
    ///   that take the build) goes through <see cref="Solve"/>, <see cref="Walk"/> or
    ///   <see cref="Locked{T}"/>, which hold the build's lock. Reading its fields needs no lock.
    /// - A scatter that a test applies (KeyShuffle.Apply / Undo write into the result) is taken with
    ///   <see cref="ScatterCopy"/>: the result object and its top-level collections are the test's
    ///   own, what they hold (moves, objects, the check's build) is shared and only read.
    /// - Determinism tests that run one seed twice keep calling KeyShuffle.Run themselves.
    /// The keys are the full options as serialised (System.Text.Json with fields), so a new option
    /// with a non-default value can never be served a build made without it.
    /// </summary>
    internal static class TestWorld
    {
        static readonly ConcurrentDictionary<string, Lazy<GameSession>> sessions = new ConcurrentDictionary<string, Lazy<GameSession>>();
        static readonly ConcurrentDictionary<string, Lazy<TimelineResult>> builds = new ConcurrentDictionary<string, Lazy<TimelineResult>>();
        static readonly ConcurrentDictionary<string, Lazy<KeyShuffleResult>> scatters = new ConcurrentDictionary<string, Lazy<KeyShuffleResult>>();

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            IncludeFields = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>The game ("UW" or "UW2") opened once, or null when its files are missing (as GameData.Game).</summary>
        public static GameSession Session(string game)
        {
            string folder = GameData.Game(game);
            if (folder == null) return null;
            return sessions.GetOrAdd(game, _ => new Lazy<GameSession>(() => GameSession.Open(folder), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>
        /// Timeline.Build of the game as shipped (the shared session's levels, the shipped facts, the
        /// automatic zones): null nav or logic are the defaults, as Timeline.Build takes them.
        /// Null when the game is missing. Shared: read it, solve again only through the lock.
        /// </summary>
        public static TimelineResult Build(string game, NavOptions nav = null, AbilityOptions logic = null, bool variants = false)
        {
            var s = Session(game);
            if (s == null) return null;
            nav = (nav ?? new NavOptions()).Clone();
            logic = Copy(logic ?? new AbilityOptions());
            string key = game + "|" + Key(nav) + "|" + Key(logic) + "|variants=" + variants;
            return builds.GetOrAdd(key, _ => new Lazy<TimelineResult>(() => Timeline.Build(s, l => s.LoadLevel(l), nav, logic, null, null, variants), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>KeyShuffle.Run of the game as shipped (no edits, the shipped facts), once per game and options (seed included). Null when the game is missing. Shared: read only.</summary>
        public static KeyShuffleResult Scatter(string game, KeyShuffleOptions opt)
        {
            if (opt.Names != null || opt.Zoning != null) throw new ArgumentException("TestWorld does not key on zone sets: a scatter with Names or Zoning runs on its own.");
            var s = Session(game);
            if (s == null) return null;
            opt = opt.Clone();
            opt.Logic = Copy(opt.Logic);
            string key = game + "|" + Key(opt) + "|fail=" + opt.FailAttempts;
            return scatters.GetOrAdd(key, _ => new Lazy<KeyShuffleResult>(() => KeyShuffle.Run(s, null, opt), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        /// <summary>The shared scatter as a copy of the test's own to apply and undo (see the class notes).</summary>
        public static KeyShuffleResult ScatterCopy(string game, KeyShuffleOptions opt)
        {
            var r = Scatter(game, opt);
            return r == null ? null : TopLevelCopy(r);
        }

        /// <summary>Timeline.Solve on a build that may be shared, under its lock.</summary>
        public static TimelineSolve Solve(TimelineResult t, AbilityOptions opt, string label, IEnumerable<string> startTokens = null)
        {
            lock (t) return Timeline.Solve(t, opt, label, startTokens);
        }

        /// <summary>Walkthrough.Build on a build that may be shared, under its lock.</summary>
        public static Walkthrough Walk(TimelineResult t, TimelineSolve sv, ZoneSet names = null, Func<int, LevelModel> level = null)
        {
            lock (t) return Walkthrough.Build(t, sv, names, level);
        }

        /// <summary>Anything else that works the engine of a build that may be shared, under its lock.</summary>
        public static T Locked<T>(TimelineResult t, Func<T> work)
        {
            lock (t) return work();
        }

        // ---------------------------------------------------------------- keys and copies

        static AbilityOptions Copy(AbilityOptions o)
        {
            var c = o.Clone();
            if (c.SingleUseOnly != null) c.SingleUseOnly = new HashSet<string>(c.SingleUseOnly);
            return c;
        }

        static string Key(object options)
        {
            Covered(options.GetType());
            return JsonSerializer.Serialize(options, options.GetType(), Json);
        }

        /// <summary>The non-public members the keys add by hand (the serialiser sees public ones only).</summary>
        static readonly Dictionary<Type, string[]> ByHand = new Dictionary<Type, string[]>
        {
            [typeof(KeyShuffleOptions)] = new[] { "FailAttempts" },
        };

        static readonly ConcurrentDictionary<Type, bool> checkedTypes = new ConcurrentDictionary<Type, bool>();

        /// <summary>
        /// Refuses an options class (or one nested in it) with a non-public field the key would not
        /// see: such a field could change a build while two calls share one key.
        /// </summary>
        static void Covered(Type t)
        {
            if (t == null || t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || !checkedTypes.TryAdd(t, true)) return;
            if (typeof(IEnumerable).IsAssignableFrom(t))
            {
                foreach (var a in t.IsArray ? new[] { t.GetElementType() } : t.GetGenericArguments()) Covered(a);
                return;
            }
            if (t.Namespace == null || !t.Namespace.StartsWith("UwEditor")) return;
            string[] hand;
            ByHand.TryGetValue(t, out hand);
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                bool seen = f.IsPublic || (hand != null && hand.Contains(f.Name));
                if (!seen && f.Name.StartsWith("<") && f.Name.EndsWith(">k__BackingField"))
                {
                    var p = t.GetProperty(f.Name.Substring(1, f.Name.IndexOf('>') - 1), BindingFlags.Instance | BindingFlags.Public);
                    seen = p != null && p.GetMethod != null && p.GetMethod.IsPublic;
                }
                if (!seen) throw new InvalidOperationException("TestWorld: " + t.Name + "." + f.Name + " is not in the cache key; add it to TestWorld.ByHand.");
                if (f.FieldType != typeof(ZoneSet)) Covered(f.FieldType);
            }
        }

        static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>A copy of the object whose collections (lists, sets, dictionaries, the log) are new ones with the same items.</summary>
        static T TopLevelCopy<T>(T o) where T : class
        {
            var c = (T)MemberwiseCloneMethod.Invoke(o, null);
            foreach (var f in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v = f.GetValue(c);
                if (v == null) continue;
                var vt = v.GetType();
                if (v is StringBuilder sb) f.SetValue(c, new StringBuilder(sb.ToString()));
                else if (vt.IsGenericType && vt.GetGenericTypeDefinition() == typeof(List<>))
                    f.SetValue(c, vt.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(vt.GetGenericArguments()) }).Invoke(new[] { v }));
                else if (vt.IsGenericType && vt.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    var a = vt.GetGenericArguments();
                    var ctor = vt.GetConstructor(new[] { typeof(IDictionary<,>).MakeGenericType(a), typeof(IEqualityComparer<>).MakeGenericType(a[0]) });
                    f.SetValue(c, ctor.Invoke(new[] { v, vt.GetProperty("Comparer").GetValue(v) }));
                }
                else if (vt.IsGenericType && vt.GetGenericTypeDefinition() == typeof(HashSet<>))
                {
                    var a = vt.GetGenericArguments();
                    var ctor = vt.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(a), typeof(IEqualityComparer<>).MakeGenericType(a[0]) });
                    f.SetValue(c, ctor.Invoke(new[] { v, vt.GetProperty("Comparer").GetValue(v) }));
                }
                else if (v is ICollection && !vt.IsArray) throw new InvalidOperationException("TestWorld: cannot copy " + typeof(T).Name + "." + f.Name + " (" + vt.Name + ").");
            }
            return c;
        }
    }
}
