using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The randomizer settings file (RandomizerSettings): every public option of every tool is
    /// carried or left out on purpose, each one survives a round trip, a text read back gives the
    /// same run on the game's data, and reading is tolerant (unknown keys warned, missing keys
    /// defaulted) but refuses another game, another tool or a newer format. With
    /// UW_SETTINGS_EXAMPLES set to a folder, the examples of the manual are written there.
    /// </summary>
    public class RandomizerSettingsTests
    {
        readonly ITestOutputHelper output;
        public RandomizerSettingsTests(ITestOutputHelper output) { this.output = output; }

        static GameSession Uw1() { string g = GameData.Game("UW"); return g == null ? null : GameSession.Open(g); }

        /// <summary>The options classes the file holds, the tools' and those nested or listed in them.</summary>
        static readonly Type[] Classes =
        {
            typeof(KeyShuffleOptions), typeof(ShuffleOptions), typeof(SwapSettings), typeof(MonsterSwapOptions), typeof(ChallengeOptions), typeof(MantraOptions), typeof(VowelPlayOptions), typeof(AutoZoneSettings),
            typeof(AbilityOptions), typeof(NavOptions), typeof(EnchantGroup), typeof(SpecialChoice), typeof(IgnoredObject), typeof(CurvePoint),
        };

        /// <summary>The public instance fields and read-write properties of a class that a serializer of plain values would see.</summary>
        static IEnumerable<string> PublicOptions(Type t)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) yield return f.Name;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() == null && (p.SetMethod?.IsPublic ?? false)) yield return p.Name;
        }

        [Fact]
        public void Every_public_option_is_carried_or_left_out_on_purpose()
        {
            foreach (var t in Classes)
            {
                var carried = RandomizerSettings.CarriedMembers(t);
                IReadOnlyDictionary<string, string> left;
                RandomizerSettings.LeftOut.TryGetValue(t, out left);
                bool tool = RandomizerSettings.Tools.Any(x => x.Options == t);
                foreach (var m in PublicOptions(t))
                {
                    bool leftOut = (left != null && left.ContainsKey(m)) || (tool && m == "Seed");
                    // a public option added later must be carried (or listed in RandomizerSettings.LeftOut with a reason)
                    Assert.True(leftOut != carried.Contains(m), t.Name + "." + m + (leftOut ? " is left out but carried" : " is not carried by the settings file"));
                }
                // every option left out names a member that is there, with a reason
                if (left != null) foreach (var kv in left) { Assert.Contains(kv.Key, PublicOptions(t)); Assert.False(string.IsNullOrWhiteSpace(kv.Value)); }
                // a public property the file cannot set back is not an option (it would be written and lost)
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (p.GetCustomAttribute<JsonIgnoreAttribute>() == null && !(p.SetMethod?.IsPublic ?? false))
                        Assert.Fail(t.Name + "." + p.Name + " is a public property without a setter: mark it [JsonIgnore] or give it one");
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (f.IsInitOnly && carried.Contains(f.Name)) Assert.Fail(t.Name + "." + f.Name + " is a read-only field the file would write and lose");
            }
        }

        /// <summary>Another value of a member's type, so that a change shows in the file.</summary>
        static object Other(Type t, object v)
        {
            if (t == typeof(bool)) return !(bool)v;
            if (t == typeof(bool?)) return v == null ? (object)true : null;
            if (t == typeof(int)) return (int)v + 3;
            if (t == typeof(double)) return (double)v + 1.5;
            if (t == typeof(string)) return (v as string ?? "") + "x";
            if (t.IsEnum) { var vals = Enum.GetValues(t); return vals.GetValue((Array.IndexOf(vals, v) + 1) % vals.Length); }
            if (t == typeof(StartPoint)) return KeyShuffleOptions.GivenStart(2, 10, 11);
            if (t.IsArray) { var a = Array.CreateInstance(t.GetElementType(), 2); a.SetValue(Other(t.GetElementType(), Activator.CreateInstance(t.GetElementType())), 1); return a; }
            if (t.IsGenericType)
            {
                var args = t.GetGenericArguments();
                var c = Activator.CreateInstance(t);
                if (c is IDictionary d) { d[Other(args[0], Activator.CreateInstance(args[0]))] = Other(args[1], args[1] == typeof(string) ? "" : Activator.CreateInstance(args[1])); return c; }
                object e = args[0] == typeof(string) ? "zz" : args[0].IsValueType ? Other(args[0], Activator.CreateInstance(args[0])) : Activator.CreateInstance(args[0]);
                if (args[0] == typeof(IgnoredObject)) e = new IgnoredObject { Level = 2, Index = 33, Label = "test" };
                t.GetMethod("Add").Invoke(c, new[] { e });
                return c;
            }
            throw new InvalidOperationException("no other value for " + t.Name);
        }

        [Fact]
        public void Every_carried_option_survives_a_round_trip()
        {
            foreach (var tool in RandomizerSettings.Tools)
            {
                var paths = new List<string>();
                Visit(tool.Options, Activator.CreateInstance(tool.Options), "", paths);
                foreach (var path in paths)
                {
                    var o = Activator.CreateInstance(tool.Options);
                    try { Change(o, path); }
                    catch (Exception ex) { Assert.Fail(tool.Id + ": " + path + " cannot be changed: " + ex.Message); }
                    var fresh = Activator.CreateInstance(tool.Options);
                    Assert.False(RandomizerSettings.SameSettings(o, fresh), tool.Id + ": the change of " + path + " does not show in the file");
                    string text = RandomizerSettings.Write("UW1", tool, o);
                    var r = RandomizerSettings.Read(text, "UW1", tool, fresh);
                    Assert.True(r.Ok, r.Error);
                    Assert.Empty(r.Warnings);
                    Assert.True(RandomizerSettings.SameSettings(o, r.Options), tool.Id + ": " + path + " did not survive:\n" + text);
                    // the one-line form reads alike
                    var c = RandomizerSettings.Read(RandomizerSettings.Write("UW1", tool, o, compact: true), "UW1", tool, fresh);
                    Assert.True(RandomizerSettings.SameSettings(o, c.Options), tool.Id + ": " + path + " (one line)");
                }
                Assert.True(paths.Count > 0 || tool == RandomizerSettings.Mantras, tool.Id + " carries nothing");
                // the seed goes in the header
                var s = Activator.CreateInstance(tool.Options);
                if (RandomizerSettings.SeedOf(s) != null)
                {
                    tool.Options.GetProperty("Seed")?.SetValue(s, 4242);
                    tool.Options.GetField("Seed")?.SetValue(s, 4242);
                    var r = RandomizerSettings.Read(RandomizerSettings.Write("UW2", tool, s), "UW2", tool, Activator.CreateInstance(tool.Options));
                    Assert.Equal(4242, r.Seed);
                    Assert.Equal(4242, RandomizerSettings.SeedOf(r.Options));
                }
            }
        }

        static readonly Type[] NestedClasses = { typeof(AbilityOptions), typeof(NavOptions) };

        static void Visit(Type t, object o, string prefix, List<string> paths)
        {
            foreach (var m in RandomizerSettings.CarriedMembers(t))
            {
                var mt = MemberType(t, m);
                if (NestedClasses.Contains(mt)) Visit(mt, Get(o, m), prefix + m + ".", paths);
                else paths.Add(prefix + m);
            }
        }

        static Type MemberType(Type t, string m) { var f = t.GetField(m); return f != null ? f.FieldType : t.GetProperty(m).PropertyType; }
        static object Get(object o, string m) { var f = o.GetType().GetField(m); return f != null ? f.GetValue(o) : o.GetType().GetProperty(m).GetValue(o); }
        static void Set(object o, string m, object v) { var f = o.GetType().GetField(m); if (f != null) f.SetValue(o, v); else o.GetType().GetProperty(m).SetValue(o, v); }

        static void Change(object o, string path)
        {
            var parts = path.Split('.');
            for (int i = 0; i < parts.Length - 1; i++) o = Get(o, parts[i]);
            string m = parts[parts.Length - 1];
            Set(o, m, Other(MemberType(o.GetType(), m), Get(o, m)));
        }

        // ---- the same run from the text, on the game's data

        /// <summary>Karl's two random-start checks: (a) a random start on any level, (b) the sealed room of level 1 given; seed 1, the default logic.</summary>
        public static KeyShuffleOptions RandomStart() { return new KeyShuffleOptions { Seed = 1, Start = StartChoice.Random, StartScope = StartScope.AnyLevel }; }
        public static KeyShuffleOptions SealedRoom() { return new KeyShuffleOptions { Seed = 1, Start = StartChoice.Given, StartAt = KeyShuffleOptions.GivenStart(1, 30, 32) }; }

        [Theory]
        [InlineData("random")]
        [InlineData("sealed")]
        public void A_key_scatter_read_back_gives_the_same_run(string which)
        {
            var s = Uw1(); if (s == null) return;
            var o = which == "random" ? RandomStart() : SealedRoom();
            string levels = RandomizerSettings.LevelsHash(s, null);
            string text = RandomizerSettings.Write("UW1", RandomizerSettings.KeyScatter, o, levels, new Dictionary<string, string> { ["zoning"] = "automatic zones" }, compact: true);
            Assert.DoesNotContain("\n", text);
            var read = RandomizerSettings.Read(text, "UW1", RandomizerSettings.KeyScatter, new KeyShuffleOptions());
            Assert.True(read.Ok, read.Error);
            Assert.Empty(read.Warnings);
            Assert.Equal(levels, read.Levels);
            Assert.Equal("automatic zones", read.Inputs["zoning"]);
            var o2 = read.As<KeyShuffleOptions>();
            Assert.True(RandomizerSettings.SameSettings(o, o2));
            var a = KeyShuffle.Run(s, null, o);
            var b = KeyShuffle.Run(s, null, o2);
            Assert.True(a.Success, a.Refused ?? string.Join("; ", a.Failures));
            Assert.Equal(a.UsedSeed, b.UsedSeed);
            Assert.Equal((a.Start.Level, a.Start.X, a.Start.Y), (b.Start.Level, b.Start.X, b.Start.Y));
            Assert.Equal(a.After.Keys.OrderBy(k => k), b.After.Keys.OrderBy(k => k));
            foreach (var kv in a.After) Assert.True(kv.Value.AsSpan().SequenceEqual(b.After[kv.Key]), "level " + kv.Key);
            // what Karl is told to expect
            output.WriteLine(text);
            output.WriteLine(string.Format("seed {0} (attempts {1}): start level {2} ({3}, {4}), from level {5} ({6}, {7}); {8} items move ({9} key items), {10} levels change; spheres {11} (original {12}); made: {13}; moved into reach: {14}",
                a.UsedSeed, a.Attempts, a.Start.Level, (int)a.Start.X, (int)a.Start.Y, a.StartFrom.Level, (int)a.StartFrom.X, (int)a.StartFrom.Y,
                a.Moves.Count(m => m.Item != m.Place), a.Moves.Count(m => m.ByFill && m.Item != m.Place), a.After.Count, a.SpheresAfter, a.SpheresBefore,
                string.Join(", ", a.Objects.Where(x => x.Made != null).Select(x => x.Name + " at " + x.Where)), a.MovedIntoReach.Count == 0 ? "none" : string.Join("; ", a.MovedIntoReach)));
            if (which == "sealed")
            {
                Assert.Equal((1, 30, 32), (a.Start.Level, (int)a.Start.X, (int)a.Start.Y));
                Assert.Contains(a.Objects, x => x.Made != null && x.Name == "wand of Gate Travel");
            }
            // a log that starts with the settings line is enough to make the run again
            string log = RandomizerSettings.LogLine(text) + "\n\n" + a.Log;
            Assert.Equal(text, RandomizerSettings.FromLog(log));
        }

        [Fact]
        public void The_item_tools_read_back_give_the_same_run()
        {
            var s = Uw1(); if (s == null) return;
            var edits = new LevelEdits(s);

            // the item property shuffle: the missing keys take the game's defaults
            var shuffle = ShuffleOptions.Defaults(s, edits.Model);
            shuffle.Seed = 77; shuffle.Axes = 90; shuffle.PlaceAnywhere = false; shuffle.WeaponEnchants[0].Probability = 40; shuffle.RuneMinimum[ItemTables.Runes[0]] = 3;
            var rs = RoundTrip(RandomizerSettings.ItemShuffle, shuffle, ShuffleOptions.Defaults(s, edits.Model));
            Assert.Equal(ItemShuffle.Run(s, edits.Model, shuffle).Report.ToString(), ItemShuffle.Run(s, edits.Model, (ShuffleOptions)rs).Report.ToString());

            // the item swap
            var swap = new SwapSettings { Seed = 12, AnyKind = true, Scope = "near", Range = 2, Bones = true, Start = ItemShuffle.StartingEquipment(s, edits.Model), Ignored = ItemShuffle.DefaultIgnored(s, edits.Model) };
            var rw = (SwapSettings)RoundTrip(RandomizerSettings.ItemSwap, swap, new SwapSettings());
            Assert.Equal(ItemShuffle.Run(s, edits.Model, swap.ToShuffleOptions(s)).Report.ToString(), ItemShuffle.Run(s, edits.Model, rw.ToShuffleOptions(s)).Report.ToString());

            // the monster swap
            var mon = new MonsterSwapOptions { Seed = 5, Scope = "all", Measure = "hp", Spread = 35, RerollPowerful = true, Whitelist = MonsterSwap.DefaultWhitelist(s, edits.Model), Ignored = new List<IgnoredObject>() };
            var rm = (MonsterSwapOptions)RoundTrip(RandomizerSettings.MonsterSwap, mon, new MonsterSwapOptions());
            Assert.Equal(MonsterSwap.Run(s, edits.Model, mon).Report.ToString(), MonsterSwap.Run(s, edits.Model, rm).Report.ToString());

            // the named challenges
            var ch = new ChallengeOptions { Seed = 8, FastingKeep = 30, Special = Challenges.DefaultSpecial(s, edits.Model) };
            ch.Set(Challenge.Fasting, true); ch.Set(Challenge.Ranger, true);
            var rc = (ChallengeOptions)RoundTrip(RandomizerSettings.Challenges, ch, new ChallengeOptions());
            Assert.Equal(Challenges.Run(ChallengeWorld.Copy(s, edits, null, null), ch).Report.ToString(), Challenges.Run(ChallengeWorld.Copy(s, edits, null, null), rc).Report.ToString());
        }

        [Fact]
        public void The_text_tools_read_back_give_the_same_run()
        {
            var s = Uw1(); if (s == null) return;
            var st = StringsEditor.Open(s);
            var m = (MantraOptions)RoundTrip(RandomizerSettings.Mantras, new MantraOptions { Seed = 9 }, new MantraOptions());
            Assert.Equal(MantraShuffle.Preview(st, 9).Report(false), MantraShuffle.Preview(st, m.Seed).Report(false));
            var v = new VowelPlayOptions { Seed = 3, KeepFirstLetter = true, Names = new List<string> { "Garamon", "Tyball" } };
            var rv = (VowelPlayOptions)RoundTrip(RandomizerSettings.VowelPlay, v, new VowelPlayOptions());
            var none = new HashSet<string>();
            Assert.Equal(VowelPlay.Preview(st, none, v).Report(false), VowelPlay.Preview(st, none, rv).Report(false));
            // the automatic zones: no seed, the logic and the movement
            var az = new AutoZoneSettings();
            az.Nav.TickLength = 24; az.Nav.GapJumps = false; az.Logic.LockPicking = true; az.Logic.SingleUseOnly = new HashSet<string> { "iron_flesh" };
            RoundTrip(RandomizerSettings.AutoZones, az, new AutoZoneSettings());
        }

        object RoundTrip(SettingsTool tool, object o, object defaults)
        {
            string text = RandomizerSettings.Write("UW1", tool, o);
            Assert.DoesNotContain("\r", text);
            var r = RandomizerSettings.Read(text, "UW1", tool, defaults);
            Assert.True(r.Ok, r.Error);
            Assert.Empty(r.Warnings);
            Assert.True(RandomizerSettings.SameSettings(o, r.Options), tool.Id + " did not come back the same");
            return r.Options;
        }

        // ---- tolerant reading

        const string Head = "{\"format\": \"uw-randomizer-settings\", \"version\": 1, \"game\": \"UW1\", ";

        [Fact]
        public void A_hand_written_text_takes_the_defaults_for_what_it_leaves_out_and_warns_of_unknown_keys()
        {
            string text = Head + "\"tool\": \"key-scatter\", \"seed\": 7, \"colour\": \"red\",\n" +
                          "  \"options\": { \"start\": \"Given\", \"startAt\": { \"level\": 1, \"x\": 30, \"y\": 32 }, \"StartScope\": \"AnyLevel\", \"logic\": { \"magic\": false, \"wings\": true }, \"move\": [\"keys\", \"runes\"], \"extra\": [200], \"bogus\": 1 } }";
            var r = RandomizerSettings.Read(text, "uw1", RandomizerSettings.KeyScatter, new KeyShuffleOptions());
            Assert.True(r.Ok, r.Error);
            var o = r.As<KeyShuffleOptions>();
            Assert.Equal(7, o.Seed);
            Assert.Equal(StartChoice.Given, o.Start);
            Assert.Equal(StartScope.AnyLevel, o.StartScope);
            Assert.Equal((1, 30.5, 32.5), (o.StartAt.Level, o.StartAt.X, o.StartAt.Y));
            Assert.True(double.IsNaN(o.StartAt.Z));
            Assert.False(o.Logic.Magic);
            Assert.True(o.Logic.RingNeedsLava);                         // left out: the default
            Assert.Equal(20, o.Retries);
            Assert.True(o.Anywhere);
            Assert.Equal(new[] { "keys", "runes" }, o.Move.OrderBy(x => x));
            Assert.Equal(new[] { 200 }, o.Extra);
            Assert.Equal(3, r.Warnings.Count);
            Assert.Contains(r.Warnings, w => w.Contains("\"colour\""));
            Assert.Contains(r.Warnings, w => w.Contains("options.logic.wings"));
            Assert.Contains(r.Warnings, w => w.Contains("options.bogus"));
            // no seed, no options: the defaults, with warnings
            var d = RandomizerSettings.Read(Head + "\"tool\": \"mantras\"}", "UW1", RandomizerSettings.Mantras, new MantraOptions { Seed = 31 });
            Assert.True(d.Ok, d.Error);
            Assert.Equal(31, d.As<MantraOptions>().Seed);
            Assert.Equal(2, d.Warnings.Count);
        }

        [Fact]
        public void Another_game_another_tool_a_newer_format_or_no_settings_are_refused()
        {
            string text = RandomizerSettings.Write("UW2", RandomizerSettings.MonsterSwap, new MonsterSwapOptions());
            var g = RandomizerSettings.Read(text, "UW1", RandomizerSettings.MonsterSwap, new MonsterSwapOptions());
            Assert.Null(g.Options);
            Assert.Equal("These settings are for Ultima Underworld 2, and the game open is Ultima Underworld 1.", g.Error);
            var t = RandomizerSettings.Read(text, "UW2", RandomizerSettings.ItemSwap, new SwapSettings());
            Assert.Equal("These settings are for Swap monsters, not Swap items; load them in that window.", t.Error);
            var n = RandomizerSettings.Read(text.Replace("\"version\": 1", "\"version\": 2"), "UW2", RandomizerSettings.MonsterSwap, new MonsterSwapOptions());
            Assert.StartsWith("These settings were written by a newer editor", n.Error);
            Assert.Contains("format version 2", n.Error);
            Assert.StartsWith("This is not randomizer settings text", RandomizerSettings.Read("seed 4", "UW1", RandomizerSettings.Mantras, new MantraOptions()).Error);
            Assert.StartsWith("This is not randomizer settings text", RandomizerSettings.Read("{\"seed\": 4}", "UW1", RandomizerSettings.Mantras, new MantraOptions()).Error);
            Assert.StartsWith("A setting could not be read", RandomizerSettings.Read(Head + "\"tool\": \"mantras\", \"options\": {\"names\": 3}}".Replace("mantras", "vowel-play"), "UW1", RandomizerSettings.VowelPlay, new VowelPlayOptions()).Error);
        }

        [Fact]
        public void The_file_records_the_editor_and_the_levels_and_copying_keeps_the_shared_objects()
        {
            string text = RandomizerSettings.Write("UW1", RandomizerSettings.KeyScatter, RandomStart(), "0123abcd");
            Assert.Contains("\"editor\": \"" + RandomizerSettings.EditorVersion + "\"", text);
            Assert.Matches(@"^\d+\.\d+\.\d+", RandomizerSettings.EditorVersion);
            Assert.Contains("\"levels\": \"0123abcd\"", text);
            Assert.Equal("uw1-key-scatter-seed-1.json", RandomizerSettings.FileName("UW1", RandomizerSettings.KeyScatter, 1));
            var r = RandomizerSettings.Read(text, "UW1", RandomizerSettings.KeyScatter, new KeyShuffleOptions());
            Assert.Equal("", RandomizerSettings.LevelsWarning(r, "0123ABCD"));
            Assert.Contains("0123abcd, now 99999999", RandomizerSettings.LevelsWarning(r, "99999999"));
            // copied into a window's options: the nested logic and the sets stay the objects the window holds
            var win = new KeyShuffleOptions();
            var logic = win.Logic; var nav = win.Nav; var move = win.Move;
            win.Logic.Magic = false;
            RandomizerSettings.CopyInto(r.Options, win);
            Assert.Same(logic, win.Logic); Assert.Same(nav, win.Nav); Assert.Same(move, win.Move);
            Assert.True(win.Logic.Magic);
            Assert.True(RandomizerSettings.SameSettings(r.Options, win));
        }

        [Fact]
        public void The_levels_hash_follows_the_edits()
        {
            var s = Uw1(); if (s == null) return;
            var edits = new LevelEdits(s);
            string disk = RandomizerSettings.LevelsHash(s, null);
            Assert.Matches("^[0-9a-f]{8}$", disk);
            edits.For(1);
            Assert.Equal(disk, RandomizerSettings.LevelsHash(s, edits));        // opened, not changed
            edits.For(1).EditBytes("test", (d, t) => d[0x10] ^= 0x01);
            Assert.NotEqual(disk, RandomizerSettings.LevelsHash(s, edits));
        }

        /// <summary>The examples in the manual and docs/examples/settings: each tool's settings as the window starts them on UW1 (the item tools with the game's defaults).</summary>
        [Fact]
        public void Examples()
        {
            string dir = Environment.GetEnvironmentVariable("UW_SETTINGS_EXAMPLES");
            var s = Uw1(); if (s == null || string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var edits = new LevelEdits(s);
            string levels = RandomizerSettings.LevelsHash(s, null);
            var shuffle = ShuffleOptions.Defaults(s, edits.Model); shuffle.Seed = 1;
            var examples = new Dictionary<SettingsTool, object>
            {
                [RandomizerSettings.KeyScatter] = new KeyShuffleOptions(),
                [RandomizerSettings.ItemShuffle] = shuffle,
                [RandomizerSettings.ItemSwap] = new SwapSettings { Start = ItemShuffle.StartingEquipment(s, edits.Model), Ignored = ItemShuffle.DefaultIgnored(s, edits.Model) },
                [RandomizerSettings.MonsterSwap] = new MonsterSwapOptions { Whitelist = MonsterSwap.DefaultWhitelist(s, edits.Model), Ignored = new List<IgnoredObject>() },
                [RandomizerSettings.Challenges] = new ChallengeOptions { Special = Challenges.DefaultSpecial(s, edits.Model) },
                [RandomizerSettings.Mantras] = new MantraOptions(),
                [RandomizerSettings.VowelPlay] = new VowelPlayOptions(),
                [RandomizerSettings.AutoZones] = new AutoZoneSettings(),
            };
            foreach (var kv in examples)
            {
                var inputs = kv.Key == RandomizerSettings.KeyScatter ? new Dictionary<string, string> { ["zoning"] = "automatic zones" } : null;
                bool levelsMatter = kv.Key != RandomizerSettings.Mantras && kv.Key != RandomizerSettings.VowelPlay && kv.Key != RandomizerSettings.AutoZones;
                File.WriteAllText(Path.Combine(dir, RandomizerSettings.FileName("UW1", kv.Key, RandomizerSettings.SeedOf(kv.Value))), RandomizerSettings.Write("UW1", kv.Key, kv.Value, levelsMatter ? levels : null, inputs) + "\n");
            }
            File.WriteAllText(Path.Combine(dir, "karl-a.txt"), RandomizerSettings.Write("UW1", RandomizerSettings.KeyScatter, RandomStart(), levels, new Dictionary<string, string> { ["zoning"] = "automatic zones" }, compact: true));
            File.WriteAllText(Path.Combine(dir, "karl-b.txt"), RandomizerSettings.Write("UW1", RandomizerSettings.KeyScatter, SealedRoom(), levels, new Dictionary<string, string> { ["zoning"] = "automatic zones" }, compact: true));
        }
    }
}
