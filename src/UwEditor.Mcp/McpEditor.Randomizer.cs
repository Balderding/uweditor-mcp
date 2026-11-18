using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UwEditor.Core;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;
using UwEditor.Core.Randomizer;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The Randomizer menu without its windows: each randomizer's settings (the one JSON format
    /// the windows' Save/Load settings use, RandomizerSettings), its preview, its apply (one undo
    /// step a level, as the windows make it) and its undo, kept per game. The settings start from
    /// the game's defaults (the windows keep their own; randomizer_settings_from_window takes an
    /// open window's). The heavy runs (key item scatter, automatic zones, the solve) are in
    /// McpEditor.RandomizerScatter.cs, the zoning in McpEditor.RandomizerZones.cs.
    /// </summary>
    public sealed partial class McpEditor
    {
        /// <summary>Attached: an open randomizer window's settings text (by the tool's id: key-scatter, item-shuffle…), or null when that window is not open.</summary>
        public Func<string, string> HostRandomizerSettings { get; set; }
        /// <summary>Attached: after a randomizer changed or put back levels (the zones and the status line follow), with the status text.</summary>
        public Action<string> HostRandomizerApplied { get; set; }
        /// <summary>Attached: after a text randomizer changed STRINGS.PAK (the string views read it again), with the status text.</summary>
        public Action<string> HostStringsChanged { get; set; }
        /// <summary>One randomizer's state here: its options, the runs applied (for its undo), its last report.</summary>
        sealed class RzTool
        {
            public SettingsTool Tool;
            public object Options;
            public readonly List<object> Applied = new List<object>();
            public string Report = "";
            public string ReportWhat = "";
            // the last heavy run (key scatter, automatic zones), its settings and the levels it was made from
            public object Last;
            public string LastSettings, LastLevels;
        }

        GameSession rzFor;
        readonly Dictionary<string, RzTool> rzTools = new Dictionary<string, RzTool>();
        ConversationLibrary rzConversations;

        static readonly Dictionary<string, string> RzAbout = new Dictionary<string, string>
        {
            ["key-scatter"] = "Key item scatter > Apply: moves the key items (keys, talismans, runes, abilities, plot items...) so the game stays beatable with the logic chosen (the zoning's or automatic zones, the time-aware solver). Optional random or given start. Both games.",
            ["item-shuffle"] = "Item property shuffle: weapons, armour, food, lights, tools, runes, wands, scrolls, potions and rings may become other items by weights, quality and enchantments rolled again; with placeAnywhere floor items go to random squares.",
            ["item-swap"] = "Swap items: existing objects trade places whole (nothing made or removed); kinds, scope, ownership, enchantment swapping, uniform progression.",
            ["monster-swap"] = "Swap monsters: creatures trade identities between spots by rank (xp, hp, combined strength) or uniform progression; whitelist lets creatures with a conversation move, ignored ones stay.",
            ["challenges"] = "Named challenges: Ghost town, Monk, Naked, Starvation, Lockdown... each changes every level (and OBJECTS.DAT's creature loot, and in a saved game the player's inventory).",
            ["mantras"] = "Randomize mantras (UW1): the shrines' mantras trade words, every text naming one follows (STRINGS.PAK).",
            ["vowel-play"] = "Vowel play: the vowels of each name are drawn again, every string naming it follows (STRINGS.PAK); names conversations compare against are kept.",
            ["auto-zones"] = "Automatic zones (Randomizer > Scatter > Automatic zones): builds zones from the levels and solves over time with the movement and logic options; apply puts them in the zoning (as Build and Use).",
        };

        /// <summary>The randomizer state belongs to one game: another game open starts it afresh.</summary>
        void RzFresh()
        {
            Require();
            if (rzFor == Session) return;
            rzFor = Session;
            rzTools.Clear();
            rzConversations = null;
            rzOwnZoning = null;
            rzSolve = null;
            rzSpot = null;
        }

        Func<int, LevelModel> RzLevels { get { var e = Edits; return l => e.Model(l); } }

        static int RzNewSeed() { return new Random().Next(1, int.MaxValue); }

        string RzGame { get { return RandomizerSettings.GameOf(Session); } }

        /// <summary>A randomizer by its id or a part of its name (scatter, shuffle, monster...).</summary>
        RzTool RzGet(string name)
        {
            RzFresh();
            if (string.IsNullOrWhiteSpace(name)) throw new McpEditorException("Name the randomizer: " + string.Join(", ", RandomizerSettings.Tools.Select(t => t.Id)) + ".");
            string n = name.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
            var tool = RandomizerSettings.Tool(n);
            if (tool == null)
            {
                var alias = new Dictionary<string, string>
                {
                    ["scatter"] = "key-scatter", ["key"] = "key-scatter", ["keys"] = "key-scatter", ["apply"] = "key-scatter", ["key-shuffle"] = "key-scatter",
                    ["shuffle"] = "item-shuffle", ["property-shuffle"] = "item-shuffle", ["swap-items"] = "item-swap", ["items"] = "item-swap",
                    ["monsters"] = "monster-swap", ["swap-monsters"] = "monster-swap", ["challenge"] = "challenges", ["named-challenges"] = "challenges",
                    ["mantra"] = "mantras", ["vowels"] = "vowel-play", ["vowel"] = "vowel-play", ["autozones"] = "auto-zones", ["automatic-zones"] = "auto-zones", ["zones"] = "auto-zones",
                };
                string id;
                if (alias.TryGetValue(n, out id)) tool = RandomizerSettings.Tool(id);
            }
            if (tool == null) throw new McpEditorException("There is no randomizer \"" + name + "\"; they are " + string.Join(", ", RandomizerSettings.Tools.Select(t => t.Id)) + ".");
            if (tool == RandomizerSettings.Mantras && Session.IsUw2) throw new McpEditorException("Randomize mantras is for Ultima Underworld 1 (UW2's shrines have no mantras to swap).");
            RzTool t;
            if (!rzTools.TryGetValue(tool.Id, out t))
            {
                t = new RzTool { Tool = tool, Options = RzDefaults(tool) };
                rzTools[tool.Id] = t;
            }
            return t;
        }

        /// <summary>A randomizer's options as its window starts them from the game (a new random seed).</summary>
        object RzDefaults(SettingsTool tool)
        {
            var s = Session;
            var level = RzLevels;
            int seed = RzNewSeed();
            switch (tool.Id)
            {
                case "key-scatter": return new KeyShuffleOptions { Seed = seed };
                case "item-shuffle":
                    {
                        var o = ShuffleOptions.Defaults(s, level);
                        o.Seed = seed;
                        o.Game = RzGame;
                        o.Mode = "shuffle";
                        o.Ignored = ItemShuffle.DefaultIgnored(s, level);
                        return o;
                    }
                case "item-swap": return new SwapSettings { Seed = seed, Start = ItemShuffle.StartingEquipment(s, level), Ignored = ItemShuffle.DefaultIgnored(s, level) };
                case "monster-swap": return new MonsterSwapOptions { Seed = seed, Whitelist = MonsterSwap.DefaultWhitelist(s, level), Ignored = new List<IgnoredObject>() };
                case "challenges": return new ChallengeOptions { Seed = seed, Special = Challenges.DefaultSpecial(s, level) };
                case "mantras": return new MantraOptions { Seed = seed };
                case "vowel-play": return new VowelPlayOptions { Seed = seed };
                case "auto-zones": return new AutoZoneSettings();
            }
            throw new McpEditorException("No defaults for " + tool.Id + ".");
        }

        /// <summary>After options were read: what the windows fill in (lists never null, the item shuffle completed for the game).</summary>
        void RzFix(object o)
        {
            if (o is ShuffleOptions so) { so.Game = RzGame; so.Mode = "shuffle"; so.Complete(Session, RzLevels); if (so.Ignored == null) so.Ignored = new List<IgnoredObject>(); }
            else if (o is SwapSettings sw) { if (sw.Start == null) sw.Start = new List<IgnoredObject>(); if (sw.Ignored == null) sw.Ignored = new List<IgnoredObject>(); }
            else if (o is MonsterSwapOptions m) { if (m.Whitelist == null) m.Whitelist = new List<IgnoredObject>(); if (m.Ignored == null) m.Ignored = new List<IgnoredObject>(); }
            else if (o is ChallengeOptions c)
            {
                if (c.Special == null) c.Special = new List<IgnoredObject>();
                if (c.Selected == null) c.Selected = new List<string>();
                if (c.RangerWeights == null || c.RangerWeights.Length != 4) c.RangerWeights = new double[] { 10, 3, 2, 1 };
            }
        }

        /// <summary>The settings text of a randomizer's options (with the levels' hash; the key scatter's zoning as its input).</summary>
        string RzSettingsText(RzTool t, bool compact)
        {
            IDictionary<string, string> inputs = null;
            if (t.Tool == RandomizerSettings.KeyScatter)
            {
                var z = RzZoning();
                inputs = new Dictionary<string, string>();
                if (z.HasZones) { inputs["zoning"] = Path.GetFileName(z.Path) + (z.Dirty ? " with unsaved changes" : ""); inputs["zoningHash"] = RandomizerSettings.ShortHash(z.ForSolver().ToJson()); }
                else inputs["zoning"] = "automatic zones";
            }
            if (t.Options is ShuffleOptions so) so.Mode = "shuffle";
            string levels = t.Tool == RandomizerSettings.Mantras || t.Tool == RandomizerSettings.VowelPlay || t.Tool == RandomizerSettings.AutoZones ? null : RandomizerSettings.LevelsHash(Session, Edits);
            return RandomizerSettings.Write(RzGame, t.Tool, t.Options, levels, inputs, compact);
        }

        // ---- the tools' list and help

        /// <summary>Every randomizer: its id, what it does, its seed, runs applied here.</summary>
        public string RandomizerList()
        {
            RzFresh();
            var b = new StringBuilder();
            b.Append("The randomizers (give the id to the randomizer_ tools). Each starts from the game's defaults with a new seed; nothing is written to the files until save.\n");
            foreach (var tool in RandomizerSettings.Tools)
            {
                bool off = tool == RandomizerSettings.Mantras && Session.IsUw2;
                RzTool t;
                rzTools.TryGetValue(tool.Id, out t);
                int? seed = t != null ? RandomizerSettings.SeedOf(t.Options) : null;
                b.Append(tool.Id).Append(" (").Append(tool.Name).Append(")").Append(off ? " [UW1 only]" : "");
                if (seed != null) b.Append(", seed ").Append(seed.Value);
                if (t != null && t.Applied.Count > 0) b.Append(", ").Append(t.Applied.Count).Append(" run(s) applied here");
                b.Append(": ").Append(RzAbout[tool.Id]).Append('\n');
            }
            return b.ToString();
        }

        /// <summary>A randomizer's settings, one a line (name = value), with what the names may hold.</summary>
        public string RandomizerHelp(string name)
        {
            var t = RzGet(name);
            var b = new StringBuilder();
            b.Append(t.Tool.Name).Append(" (").Append(t.Tool.Id).Append("): ").Append(RzAbout[t.Tool.Id]).Append('\n');
            int? seed = RandomizerSettings.SeedOf(t.Options);
            if (seed != null) b.Append("seed = ").Append(seed.Value).Append('\n');
            b.Append("The settings (randomizer_setting sets one by its name, dots for nested ones: logic.lockPicking; randomizer_settings_set takes JSON):\n");
            RzFlatten(RandomizerSettings.OptionsNode(t.Options), "", b);
            switch (t.Tool.Id)
            {
                case "key-scatter":
                    b.Append("\nmove (the categories that move): ").Append(string.Join(", ", KeyShuffle.CategoriesOf(Session).Select(c => c + " = " + KeyShuffle.CategoryName(c, Session.IsUw2)))).Append('\n');
                    b.Append("start: own, random or given (with startAt {\"level\": 1, \"x\": 30, \"y\": 32}); startScope: firstLevels or anyLevel.\n");
                    try
                    {
                        var offered = ExtraItems.Offered(Session, RzLevels);
                        if (offered.Count > 0) b.Append("extra (more items to scatter, by item number): ").Append(string.Join("; ", offered.Take(80).Select(x => x.Item + " " + x.ToString()))).Append('\n');
                    }
                    catch (Exception) { }
                    b.Append("The zoning used: the zoning's (zones_info) when it has zones, else automatic zones.\n");
                    break;
                case "challenges":
                    b.Append("\nselected (the challenges, by these names):\n");
                    foreach (var i in Challenges.All) b.Append("  ").Append(i.Id).Append(" (").Append(i.Name).Append("): ").Append(i.Summary).Append('\n');
                    b.Append("Not together: ").Append(string.Join("; ", Challenges.Conflicting.Select(p => string.Join(" + ", p)))).Append(".\n");
                    b.Append("special: the creatures DMD mode and Hoarding leave alone (randomizer_list challenges special). ").Append(Challenges.SpecialDefaultsText(Session)).Append('\n');
                    break;
                case "monster-swap":
                    b.Append("\nmeasure: ").Append(string.Join(", ", new[] { "xp", "hp", "combined" }.Select(m => m + " = " + MonsterSwap.MeasureName(m)))).Append(". Lists: whitelist, ignored (randomizer_list).\n");
                    break;
                case "item-swap":
                    b.Append("\nLists: ignored, start (the starting equipment that stays), see randomizer_list.\n");
                    break;
                case "item-shuffle":
                    b.Append("\nList: ignored (randomizer_list). randomizer_settings_reset sets every tab from the levels as they are (All from the game).\n");
                    break;
                case "vowel-play":
                    b.Append("\nnames null means the default names: ").Append(string.Join(", ", VowelPlay.DefaultNames(RzStrings()).Take(200))).Append('\n');
                    break;
            }
            return b.ToString();
        }

        static void RzFlatten(JsonNode n, string path, StringBuilder b)
        {
            if (n is JsonObject o)
            {
                if (o.Count == 0 && path.Length > 0) { b.Append(path).Append(" = {}\n"); return; }
                foreach (var kv in o) RzFlatten(kv.Value, path.Length == 0 ? kv.Key : path + "." + kv.Key, b);
                return;
            }
            string v = n == null ? "null" : n.ToJsonString();
            if (n is JsonArray a && v.Length > 300) v = "[" + a.Count + " entries; randomizer_settings shows them]";
            b.Append(path).Append(" = ").Append(v).Append('\n');
        }

        // ---- settings

        /// <summary>A randomizer's settings as the settings file has them (Save settings in its window): what the next preview or apply uses.</summary>
        public string RandomizerSettingsGet(string name)
        {
            var t = RzGet(name);
            return RzSettingsText(t, false);
        }

        /// <summary>
        /// Sets a randomizer's settings from JSON: a whole settings text (as Copy settings gives,
        /// {"format": "uw-randomizer-settings", ...}: what it leaves out takes the game's defaults, as
        /// Load settings does), or an object of options laid over the ones now ({"seed": 5,
        /// "anywhere": false, "logic": {"lockPicking": true}}).
        /// </summary>
        public string RandomizerSettingsSet(string name, string json)
        {
            var t = RzGet(name);
            if (string.IsNullOrWhiteSpace(json)) throw new McpEditorException("No settings given.");
            JsonObject root;
            try { root = JsonNode.Parse(json.Trim(), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject; }
            catch (JsonException e) { throw new McpEditorException("That is not JSON: " + e.Message); }
            if (root == null) throw new McpEditorException("Give a JSON object: a settings text, or {\"name\": value, ...}.");
            bool whole = root.Any(kv => string.Equals(kv.Key, "format", StringComparison.OrdinalIgnoreCase));
            object defaults;
            string text;
            if (whole)
            {
                defaults = RzDefaults(t.Tool);
                int? now = RandomizerSettings.SeedOf(t.Options);
                if (now != null) RzSetSeed(defaults, now.Value);
                text = root.ToJsonString();
            }
            else
            {
                defaults = t.Options;
                text = RzWrap(t, root).ToJsonString();
            }
            var r = RandomizerSettings.Read(text, RzGame, t.Tool, defaults);
            if (!r.Ok) throw new McpEditorException(r.Error);
            RzFix(r.Options);
            t.Options = r.Options;
            var said = new StringBuilder(t.Tool.Name + " settings set" + (r.Seed != null ? " (seed " + r.Seed.Value + ")" : "") + ".");
            foreach (var w in r.Warnings) said.Append(' ').Append(w);
            string lw = whole ? RandomizerSettings.LevelsWarning(r, RandomizerSettings.LevelsHash(Session, Edits)) : "";
            if (lw.Length > 0) said.Append(' ').Append(lw);
            return said.ToString();
        }

        /// <summary>Options given alone, as a settings text for this game and tool (the seed lifted out of them, else the one now).</summary>
        JsonObject RzWrap(RzTool t, JsonObject options)
        {
            var opts = (JsonObject)options.DeepClone();
            JsonNode seed = null;
            foreach (var kv in opts.ToList())
                if (string.Equals(kv.Key, "seed", StringComparison.OrdinalIgnoreCase)) { seed = kv.Value?.DeepClone(); opts.Remove(kv.Key); }
            int? now = RandomizerSettings.SeedOf(t.Options);
            var root = new JsonObject { ["format"] = RandomizerSettings.FormatName, ["version"] = RandomizerSettings.FormatVersion, ["game"] = RzGame, ["tool"] = t.Tool.Id };
            if (seed != null) root["seed"] = seed;
            else if (now != null) root["seed"] = now.Value;
            root["options"] = opts;
            return root;
        }

        /// <summary>Sets one setting by its name (dots for nested ones: logic.lockPicking, nav.gapJumps); the value is JSON (true, 5, "level", [1, 2]) or plain text.</summary>
        public string RandomizerSettingSet(string name, string setting, string value)
        {
            var t = RzGet(name);
            if (string.IsNullOrWhiteSpace(setting)) throw new McpEditorException("Name the setting (randomizer_help lists them).");
            JsonNode v;
            try { v = JsonNode.Parse(value ?? "null"); }
            catch (JsonException) { v = JsonValue.Create(value); }
            var parts = setting.Trim().Split('.');
            JsonObject top = new JsonObject(), cur = top;
            for (int i = 0; i < parts.Length - 1; i++) { var next = new JsonObject(); cur[parts[i]] = next; cur = next; }
            cur[parts[parts.Length - 1]] = v;
            if (parts.Length == 1 && string.Equals(parts[0], "seed", StringComparison.OrdinalIgnoreCase))
            {
                if (RandomizerSettings.SeedOf(t.Options) == null) throw new McpEditorException(t.Tool.Name + " has no seed.");
            }
            else
            {
                // a name the options do not have is refused here (the settings reader would only warn)
                JsonNode at = RandomizerSettings.OptionsNode(t.Options);
                foreach (var p in parts)
                {
                    var o = at as JsonObject;
                    var hit = o?.FirstOrDefault(kv => string.Equals(kv.Key, p, StringComparison.OrdinalIgnoreCase));
                    if (o == null || hit == null || hit.Value.Key == null) throw new McpEditorException("\"" + setting + "\" is not a setting of " + t.Tool.Name + "; randomizer_help lists them.");
                    at = hit.Value.Value;
                }
            }
            var r = RandomizerSettings.Read(RzWrap(t, top).ToJsonString(), RzGame, t.Tool, t.Options);
            if (!r.Ok) throw new McpEditorException(r.Error);
            RzFix(r.Options);
            t.Options = r.Options;
            return t.Tool.Name + ": " + setting + " = " + (v == null ? "null" : v.ToJsonString()) + (r.Warnings.Count > 0 ? " (" + string.Join(" ", r.Warnings) + ")" : "") + ".";
        }

        static void RzSetSeed(object options, int seed)
        {
            var p = options.GetType().GetProperty("Seed");
            if (p != null && p.PropertyType == typeof(int)) { p.SetValue(options, seed); return; }
            var f = options.GetType().GetField("Seed");
            if (f != null && f.FieldType == typeof(int)) f.SetValue(options, seed);
        }

        /// <summary>Sets a randomizer's seed (null: a new random one); answers it.</summary>
        public string RandomizerSeed(string name, int? seed)
        {
            var t = RzGet(name);
            if (RandomizerSettings.SeedOf(t.Options) == null) throw new McpEditorException(t.Tool.Name + " has no seed (it is not random).");
            if (seed != null && seed.Value < 0) throw new ArgumentOutOfRangeException(null, "A seed is 0 or more.");
            int s = seed ?? RzNewSeed();
            RzSetSeed(t.Options, s);
            return t.Tool.Name + " seed " + s + ".";
        }

        /// <summary>A randomizer's settings back to the game's defaults (for the item shuffle: every tab from the levels as they are now), keeping its seed.</summary>
        public string RandomizerSettingsReset(string name)
        {
            var t = RzGet(name);
            int? seed = RandomizerSettings.SeedOf(t.Options);
            var o = RzDefaults(t.Tool);
            if (seed != null) RzSetSeed(o, seed.Value);
            t.Options = o;
            return t.Tool.Name + " settings back to the defaults" + (seed != null ? " (seed " + seed.Value + " kept)" : "") + ".";
        }

        /// <summary>Attached: takes the settings of the randomizer's window open in the editor.</summary>
        public string RandomizerSettingsFromWindow(string name)
        {
            var t = RzGet(name);
            string text = HostRandomizerSettings?.Invoke(t.Tool.Id);
            if (text == null) throw new McpEditorException(IsAttached ? "The " + t.Tool.Name + " window is not open in the editor." : "This works in the editor's window (Let an AI assistant work here): this server has no editor windows.");
            var defaults = RzDefaults(t.Tool);
            var r = RandomizerSettings.Read(text, RzGame, t.Tool, defaults);
            if (!r.Ok) throw new McpEditorException(r.Error);
            RzFix(r.Options);
            t.Options = r.Options;
            return "Took the " + t.Tool.Name + " window's settings" + (r.Seed != null ? " (seed " + r.Seed.Value + ")" : "") + ".";
        }

        // ---- the object lists (ignored, whitelist, special, start)

        List<IgnoredObject> RzList(RzTool t, string list, out Func<List<IgnoredObject>> candidates, out Func<List<IgnoredObject>> defaults)
        {
            var s = Session;
            var level = RzLevels;
            string l = (list ?? "").Trim().ToLowerInvariant();
            candidates = null; defaults = null;
            switch (t.Tool.Id)
            {
                case "item-shuffle":
                    if (l == "ignored") { candidates = () => ItemShuffle.Candidates(s, level); defaults = () => ItemShuffle.DefaultIgnored(s, level); return ((ShuffleOptions)t.Options).Ignored; }
                    break;
                case "item-swap":
                    {
                        var o = (SwapSettings)t.Options;
                        if (l == "ignored") { candidates = () => ItemShuffle.Candidates(s, level, true); defaults = () => ItemShuffle.DefaultIgnored(s, level); return o.Ignored; }
                        if (l == "start") { candidates = () => ItemShuffle.Candidates(s, level, true); defaults = () => ItemShuffle.StartingEquipment(s, level); return o.Start; }
                        break;
                    }
                case "monster-swap":
                    {
                        var o = (MonsterSwapOptions)t.Options;
                        Func<Func<MonsterSpot, bool>, List<IgnoredObject>> spots = keep => MonsterSwap.Survey(s, level, new MonsterSwapOptions { LeaveSpecialKinds = false }).Where(keep).Select(x => new IgnoredObject { Level = x.Level, Index = x.Index, Label = x.Label }).ToList();
                        if (l == "whitelist") { candidates = () => spots(x => x.Blocked == "has a conversation"); defaults = () => MonsterSwap.DefaultWhitelist(s, level); return o.Whitelist; }
                        if (l == "ignored") { candidates = () => spots(x => x.Blocked == null || x.Blocked == "has a conversation"); defaults = () => new List<IgnoredObject>(); return o.Ignored; }
                        break;
                    }
                case "challenges":
                    if (l == "special") { candidates = () => Challenges.SpecialCandidates(s, level); defaults = () => Challenges.DefaultSpecial(s, level); return ((ChallengeOptions)t.Options).Special; }
                    break;
            }
            throw new McpEditorException(t.Tool.Name + " has no list \"" + list + "\"; the lists are: item-shuffle ignored; item-swap ignored, start; monster-swap whitelist, ignored; challenges special.");
        }

        /// <summary>A randomizer's object list (or, with candidates, the objects that may go on it, filtered by a part of their label).</summary>
        public string RandomizerListShow(string name, string list, bool candidates, string filter)
        {
            var t = RzGet(name);
            Func<List<IgnoredObject>> cand, defs;
            var l = RzList(t, list, out cand, out defs);
            var shown = candidates ? cand() : l;
            if (!string.IsNullOrWhiteSpace(filter)) shown = shown.Where(o => (o.Label ?? "").IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var b = new StringBuilder();
            b.Append(candidates ? "May go on " : "On ").Append(t.Tool.Id).Append(' ').Append(list).Append(candidates ? "" : " (" + l.Count + ")").Append(": ").Append(shown.Count).Append(shown.Count > 300 ? ", the first 300" : "").Append('\n');
            foreach (var o in shown.Take(300))
            {
                b.Append("level ").Append(o.Level).Append(" #").Append(o.Index).Append(' ').Append(o.Label);
                if (candidates && l.Any(x => x.Level == o.Level && x.Index == o.Index)) b.Append("  [on the list]");
                b.Append('\n');
            }
            return b.ToString();
        }

        /// <summary>Changes a randomizer's object list: add or remove an object (level, index), clear it, or put back its defaults.</summary>
        public string RandomizerListEdit(string name, string list, string action, int? level, int? index)
        {
            var t = RzGet(name);
            Func<List<IgnoredObject>> cand, defs;
            var l = RzList(t, list, out cand, out defs);
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "add":
                    {
                        if (level == null || index == null) throw new McpEditorException("Give the object's level and index.");
                        if (l.Any(x => x.Level == level && x.Index == index)) return "Level " + level + " #" + index + " is on the list already.";
                        var c = cand().FirstOrDefault(x => x.Level == level && x.Index == index);
                        if (c == null) throw new McpEditorException("Level " + level + " #" + index + " cannot go on the " + list + " list (randomizer_list with candidates shows those that can).");
                        l.Add(new IgnoredObject { Level = c.Level, Index = c.Index, Label = c.Label });
                        return "Added " + c.Label + " (level " + c.Level + " #" + c.Index + ") to " + t.Tool.Id + " " + list + " (" + l.Count + ").";
                    }
                case "remove":
                    {
                        if (level == null || index == null) throw new McpEditorException("Give the object's level and index.");
                        int n = l.RemoveAll(x => x.Level == level && x.Index == index);
                        if (n == 0) throw new McpEditorException("Level " + level + " #" + index + " is not on the list.");
                        return "Removed level " + level + " #" + index + " from " + t.Tool.Id + " " + list + " (" + l.Count + ").";
                    }
                case "clear": l.Clear(); return "The " + t.Tool.Id + " " + list + " list is empty now.";
                case "defaults": l.Clear(); l.AddRange(defs()); return "The " + t.Tool.Id + " " + list + " list is back to its defaults (" + l.Count + ").";
            }
            throw new McpEditorException("The action is add, remove, clear or defaults.");
        }

        // ---- the formulas, the world as it is, the creatures' strengths

        /// <summary>The math of a run with the settings now (item shuffle, item swap, monster swap), as the window's formula box.</summary>
        public string RandomizerFormula(string name)
        {
            var t = RzGet(name);
            if (t.Options is ShuffleOptions so) { so.Mode = "shuffle"; return ItemShuffle.ShuffleFormula(Session, so); }
            if (t.Options is SwapSettings sw) return ItemShuffle.SwapFormula(Session, sw.ToShuffleOptions(Session));
            if (t.Options is MonsterSwapOptions m) return MonsterSwap.Formula(Session, m);
            throw new McpEditorException("Only item-shuffle, item-swap and monster-swap have a formula.");
        }

        /// <summary>What the levels hold now, as the windows' world report: items (item-shuffle, item-swap) or monsters (monster-swap).</summary>
        public string RandomizerWorld(string name)
        {
            var t = RzGet(name);
            if (t.Tool == RandomizerSettings.ItemShuffle || t.Tool == RandomizerSettings.ItemSwap) return ItemShuffle.WorldReport(Session, RzLevels);
            if (t.Tool == RandomizerSettings.MonsterSwap) return MonsterSwap.WorldReport(Session, RzLevels);
            throw new McpEditorException("Only item-shuffle, item-swap and monster-swap have a world report.");
        }

        /// <summary>The monster swap's strength table: every kind with xp, hp, armour, damage, the combined strength, moves, fire, height, spells.</summary>
        public string RandomizerStrengths()
        {
            RzFresh();
            var ci = CultureInfo.InvariantCulture;
            var b = new StringBuilder("kind | item | xp | hp | armour | damage | combined | moves | fire | height | spells\n");
            foreach (var r in MonsterSwap.StrengthRows(Session))
                b.Append(r.Name).Append(" | 0x").Append(r.Item.ToString("X2")).Append(" | ").Append(r.Xp.ToString(ci)).Append(" | ").Append(r.Hp.ToString(ci)).Append(" | ")
                 .Append(r.Armour.ToString("0.0", ci)).Append(" | ").Append(r.Damage.ToString("0.0", ci)).Append(" | ").Append(r.Combined.ToString("0.0", ci)).Append(" | ")
                 .Append(r.Moves).Append(" | ").Append(r.Fireproof ? "resists" : "").Append(" | ").Append(r.Height.ToString(ci)).Append(" | ").Append(r.Spells).Append('\n');
            b.Append(MonsterSwap.StrengthNote);
            return b.ToString();
        }

        // ---- reports

        /// <summary>Keeps a report as the randomizer's last; answers its head (the rest: randomizer_report).</summary>
        static string RzKeep(RzTool t, string what, string report, string head, int max = 6000)
        {
            t.Report = report ?? "";
            t.ReportWhat = what;
            string text = (head ?? "") + (head != null && head.Length > 0 ? "\n\n" : "");
            if (t.Report.Length <= max) return text + t.Report;
            return text + t.Report.Substring(0, max) + "\n\n... (" + (t.Report.Length - max) + " more characters: randomizer_report " + t.Tool.Id + " with from " + max + ")";
        }

        /// <summary>A part of a randomizer's last report (the preview's or apply's: the spoiler log of the key scatter).</summary>
        public string RandomizerReport(string name, int from, int length)
        {
            var t = RzGet(name);
            if (t.Report.Length == 0) throw new McpEditorException(t.Tool.Name + " has no report yet: preview or apply first.");
            if (from < 0 || from >= t.Report.Length) throw new ArgumentOutOfRangeException(null, "from is 0-" + (t.Report.Length - 1) + ".");
            if (length < 1) length = 8000;
            length = Math.Min(Math.Min(length, 50000), t.Report.Length - from);
            return t.ReportWhat + ", characters " + from + "-" + (from + length) + " of " + t.Report.Length + ":\n" + t.Report.Substring(from, length);
        }

        /// <summary>Writes a randomizer's last report (the key scatter's spoiler log) to a text file; an existing file is not written over unless overwrite.</summary>
        public string RandomizerReportSave(string name, string path, bool overwrite)
        {
            var t = RzGet(name);
            if (t.Report.Length == 0) throw new McpEditorException(t.Tool.Name + " has no report yet: preview or apply first.");
            if (string.IsNullOrWhiteSpace(path)) throw new McpEditorException("Give the file to write.");
            path = Path.GetFullPath(path.Trim().Trim('"'));
            if (File.Exists(path) && !overwrite) throw new McpEditorException(path + " exists: give another name, or overwrite.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, t.Report);
            return "Wrote " + t.ReportWhat.ToLowerInvariant() + " to " + path + " (" + t.Report.Length + " characters).";
        }

        // ---- preview, apply, undo

        string RzLogLine(RzTool t) { return RandomizerSettings.LogLine(RzSettingsText(t, true)) + "\n\n"; }

        StringsEditor RzStrings() { return TblStrings(); }

        ConversationLibrary RzConversationLibrary()
        {
            var h = HostConversations?.Invoke();
            if (h != null && h.Session == Session) return h;
            if (rzConversations == null) rzConversations = ConversationLibrary.Open(Session);
            return rzConversations;
        }

        /// <summary>The objects table for the challenges: as edited (the Object tables window's, or this server's copy).</summary>
        ObjectTables RzObjects() { return TblObjects(); }

        /// <summary>The open saved game's player sheet (the one the player tools and save use), or null.</summary>
        PlayerSheet RzPlayer() { return Session.IsSave ? SvSheetOrNull() : null; }

        ChallengeResult RzChallengeRun(ChallengeOptions o)
        {
            byte[] objects = null;
            try { objects = RzObjects().ObjectsBytes; } catch (McpEditorException) { }
            var w = ChallengeWorld.Copy(Session, Edits, objects, RzPlayer(), Challenges.TraderSlots(Session));
            return Challenges.Run(w, o);
        }

        /// <summary>What a randomizer would change with its settings now; nothing changes. The heavy ones run as a job (stage on the host, work here, answer on the host).</summary>
        public Func<Func<string>> RandomizerPreviewJob(string name)
        {
            var t = RzGet(name);
            if (t.Tool == RandomizerSettings.KeyScatter) return RzScatterJob(t, false);
            if (t.Tool == RandomizerSettings.AutoZones) return RzAutoZonesJob(t, false, false);
            string text = RzPreviewLight(t);
            return () => () => text;
        }

        string RzPreviewLight(RzTool t)
        {
            var s = Session;
            switch (t.Tool.Id)
            {
                case "item-shuffle":
                    {
                        string line = RzLogLine(t);
                        var r = ItemShuffle.Run(s, RzLevels, (ShuffleOptions)t.Options);
                        return RzKeep(t, "The preview", line + r.Report, string.Format("Preview, nothing changed: {0} object(s) would change, {1} would move.", r.Changed, r.Moved));
                    }
                case "item-swap":
                    {
                        string line = RzLogLine(t);
                        var r = ItemShuffle.Run(s, RzLevels, ((SwapSettings)t.Options).ToShuffleOptions(s));
                        return RzKeep(t, "The preview", line + r.Report, string.Format("Preview, nothing changed: {0} object(s) would change.", r.Changed));
                    }
                case "monster-swap":
                    {
                        string line = RzLogLine(t);
                        var r = MonsterSwap.Run(s, RzLevels, (MonsterSwapOptions)t.Options);
                        return RzKeep(t, "The preview", line + r.Report, string.Format("Preview, nothing changed: {0} creature record(s) would change.", r.Changed));
                    }
                case "challenges":
                    {
                        string line = RzLogLine(t);
                        var r = RzChallengeRun((ChallengeOptions)t.Options);
                        if (r.Refused != null) throw new McpEditorException(r.Refused);
                        return RzKeep(t, "The preview", line + r.Report, string.Format("Preview, nothing changed: {0} change(s) on level(s) {1}{2}.", r.Total, string.Join(", ", r.ChangedLevels()), r.TableChanges.Count > 0 ? ", " + r.TableChanges.Count + " OBJECTS.DAT field(s)" : ""));
                    }
                case "mantras":
                    {
                        var r = MantraShuffle.Preview(RzStrings(), ((MantraOptions)t.Options).Seed);
                        return RzKeep(t, "The preview", r.Report(false), "Preview, nothing changed: " + r.Edits.Count + " string(s) would change.");
                    }
                case "vowel-play":
                    {
                        var st = RzStrings();
                        var r = VowelPlay.Preview(st, VowelPlay.ProtectedWords(st, RzConversationLibrary()), (VowelPlayOptions)t.Options);
                        return RzKeep(t, "The preview", r.Report(false), "Preview, nothing changed: " + r.Names.Count + " name(s), " + r.Edits.Count + " string(s) would change.");
                    }
            }
            throw new McpEditorException("No preview for " + t.Tool.Id + ".");
        }

        /// <summary>Applies a randomizer with its settings now: one undoable step a level (Edit > Undo, or randomizer_undo); nothing is written until save.</summary>
        public Func<Func<string>> RandomizerApplyJob(string name, bool replace = false)
        {
            var t = RzGet(name);
            if (t.Tool == RandomizerSettings.KeyScatter) return RzScatterJob(t, true);
            if (t.Tool == RandomizerSettings.AutoZones) return RzAutoZonesJob(t, true, replace);
            string text = RzApplyLight(t);
            return () => () => text;
        }

        void RzApplied(string status) { HostRandomizerApplied?.Invoke(status); }

        string RzApplyLight(RzTool t)
        {
            var s = Session;
            switch (t.Tool.Id)
            {
                case "item-shuffle":
                    {
                        var o = (ShuffleOptions)t.Options;
                        string line = RzLogLine(t);
                        var r = ItemShuffle.Run(s, RzLevels, o);
                        if (r.Changed == 0 && r.Moved == 0) return RzKeep(t, "The report", r.Report.ToString(), "Nothing to change.");
                        ItemShuffle.Apply(r, Edits, o.Seed);
                        t.Applied.Add(r);
                        RzApplied("Item properties shuffled (by the assistant). Edit > Undo (per level) puts them back.");
                        return RzKeep(t, "The report", line + r.Report, string.Format("Shuffled (seed {0}): {1} object(s) changed, {2} moved, on level(s) {3}. Not saved yet; randomizer_undo puts back every shuffle made here.", o.Seed, r.Changed, r.Moved, string.Join(", ", r.Levels)));
                    }
                case "item-swap":
                    {
                        var o = (SwapSettings)t.Options;
                        string line = RzLogLine(t);
                        var r = ItemShuffle.Run(s, RzLevels, o.ToShuffleOptions(s));
                        if (r.Changed == 0) return RzKeep(t, "The report", r.Report.ToString(), "Nothing to change.");
                        ItemShuffle.Apply(r, Edits, o.Seed);
                        t.Applied.Add(r);
                        RzApplied("Items swapped (by the assistant). Edit > Undo (per level) puts them back.");
                        return RzKeep(t, "The report", line + r.Report, string.Format("Swapped (seed {0}): {1} object change(s) on level(s) {2}. Not saved yet; randomizer_undo puts back every swap made here.", o.Seed, r.Changed, string.Join(", ", r.Levels)));
                    }
                case "monster-swap":
                    {
                        var o = (MonsterSwapOptions)t.Options;
                        string line = RzLogLine(t);
                        var r = MonsterSwap.Run(s, RzLevels, o);
                        if (r.Changed == 0) return RzKeep(t, "The report", r.Report.ToString(), "Nothing to change.");
                        MonsterSwap.Apply(r, Edits, o.Seed);
                        t.Applied.Add(r);
                        RzApplied("Monsters swapped (by the assistant). Edit > Undo (per level) puts them back.");
                        return RzKeep(t, "The report", line + r.Report, string.Format("Swapped (seed {0}): {1} creature record(s) on level(s) {2}. Not saved yet; randomizer_undo puts back every swap made here.", o.Seed, r.Changed, string.Join(", ", r.ByLevel.Keys.OrderBy(k => k))));
                    }
                case "challenges":
                    {
                        var o = (ChallengeOptions)t.Options;
                        string line = RzLogLine(t);
                        var r = RzChallengeRun(o);
                        if (r.Refused != null) throw new McpEditorException(r.Refused);
                        if (r.Total == 0) return RzKeep(t, "The report", r.Report.ToString(), "Nothing to change.");
                        var tables = r.TableChanges.Count > 0 ? RzObjects() : null;
                        var player = s.IsSave && r.World.PlayerChanged ? RzPlayer() : null;
                        var a = Challenges.Apply(r, Edits, RzSetter(tables), player, "Named challenges (seed " + o.Seed + ")");
                        if (player != null) SvPlayerEdited(player);
                        t.Applied.Add(a);
                        if (tables != null) HostTablesEdited?.Invoke();
                        var notes = new List<string>();
                        if (r.TableChanges.Count > 0) notes.Add(a.Table.Count + " field(s) of OBJECTS.DAT's creature table changed (written by save).");
                        if (player != null) notes.Add("The player's inventory changed (unsaved).");
                        else if (s.IsSave && r.World.PlayerChanged) notes.Add("The player's inventory was left as it is (PLAYER.DAT could not be read).");
                        RzApplied("Named challenges applied (by the assistant). Edit > Undo (per level) puts them back.");
                        return RzKeep(t, "The report", line + r.Report, string.Format("Applied (seed {0}): {1} change(s) on level(s) {2}.{3} Not saved yet; randomizer_undo puts back every run made here.", o.Seed, r.Total, string.Join(", ", r.ChangedLevels()), notes.Count > 0 ? " " + string.Join(" ", notes) : ""));
                    }
                case "mantras":
                    {
                        var r = MantraShuffle.Apply(RzStrings(), ((MantraOptions)t.Options).Seed);
                        if (r.Edits.Count == 0) return RzKeep(t, "The report", r.Report(false), "Nothing to change.");
                        t.Applied.Add(r);
                        HostStringsChanged?.Invoke("Mantras swapped (seed " + r.Seed + ", by the assistant); File > Save writes STRINGS.PAK.");
                        return RzKeep(t, "The report", r.Report(true), "Mantras swapped (seed " + r.Seed + "): " + r.Edits.Count + " string(s) changed in STRINGS.PAK, written by save. randomizer_undo puts the last run back.");
                    }
                case "vowel-play":
                    {
                        var st = RzStrings();
                        var r = VowelPlay.Apply(st, VowelPlay.ProtectedWords(st, RzConversationLibrary()), (VowelPlayOptions)t.Options);
                        if (r.Edits.Count == 0) return RzKeep(t, "The report", r.Report(false), "Nothing to change.");
                        t.Applied.Add(r);
                        HostStringsChanged?.Invoke("Vowel play applied (seed " + r.Seed + ", by the assistant); File > Save writes STRINGS.PAK.");
                        return RzKeep(t, "The report", r.Report(true), "Vowel play applied (seed " + r.Seed + "): " + r.Names.Count + " name(s), " + r.Edits.Count + " string(s) changed in STRINGS.PAK, written by save. randomizer_undo puts the last run back.");
                    }
            }
            throw new McpEditorException("No apply for " + t.Tool.Id + ".");
        }

        static Func<int, string, int, string> RzSetter(ObjectTables t)
        {
            if (t == null) return null;
            return (row, field, value) => t.Set("Creatures", row, field, value);
        }

        /// <summary>
        /// Puts back what a randomizer applied here: the item shuffle, swaps and challenges every run
        /// (as their windows' Undo), the key scatter, mantras and vowel play the last run. A level
        /// edited since is left as it is (Edit > Undo, or undo, steps back there).
        /// </summary>
        public string RandomizerUndo(string name)
        {
            var t = RzGet(name);
            if (t.Applied.Count == 0) throw new McpEditorException(t.Tool.Name + " has applied nothing here to undo" + (IsAttached ? " (what its window applied, its own Undo puts back)." : "."));
            var skipped = new SortedSet<int>();
            string said;
            switch (t.Tool.Id)
            {
                case "item-shuffle":
                case "item-swap":
                    {
                        int n = 0;
                        for (int i = t.Applied.Count - 1; i >= 0; i--) { var r = (ShuffleResult)t.Applied[i]; skipped.UnionWith(ItemShuffle.Revert(r, Edits)); n += r.Changed; }
                        said = string.Format("Undone: {0} run(s), {1} object change(s) put back.", t.Applied.Count, n);
                        t.Applied.Clear();
                        break;
                    }
                case "monster-swap":
                    {
                        int n = 0;
                        for (int i = t.Applied.Count - 1; i >= 0; i--) { var r = (MonsterSwapResult)t.Applied[i]; skipped.UnionWith(MonsterSwap.Revert(r, Edits)); n += r.Changed; }
                        said = string.Format("Undone: {0} swap(s), {1} creature record(s) put back.", t.Applied.Count, n);
                        t.Applied.Clear();
                        break;
                    }
                case "challenges":
                    {
                        bool table = t.Applied.Cast<ChallengeApplied>().Any(a => a.Table.Count > 0), player = t.Applied.Cast<ChallengeApplied>().Any(a => a.ChangedPlayer);
                        var tables = table ? RzObjects() : null;
                        var sheet = player ? RzPlayer() : null;
                        for (int i = t.Applied.Count - 1; i >= 0; i--) skipped.UnionWith(Challenges.Undo((ChallengeApplied)t.Applied[i], Edits, RzSetter(tables), sheet));
                        if (sheet != null) SvPlayerEdited(sheet);
                        if (tables != null) HostTablesEdited?.Invoke();
                        said = string.Format("Undone: {0} run(s) of the challenges put back.", t.Applied.Count);
                        t.Applied.Clear();
                        break;
                    }
                case "key-scatter":
                    {
                        var r = (KeyShuffleResult)t.Applied[t.Applied.Count - 1];
                        t.Applied.RemoveAt(t.Applied.Count - 1);
                        skipped.UnionWith(KeyShuffle.Undo(r, Edits));
                        said = "The scatter of seed " + r.UsedSeed + " is undone" + (t.Applied.Count > 0 ? " (" + t.Applied.Count + " earlier one(s) still applied)." : ".");
                        t.Last = null;
                        break;
                    }
                case "mantras":
                case "vowel-play":
                    {
                        var r = t.Applied[t.Applied.Count - 1];
                        t.Applied.RemoveAt(t.Applied.Count - 1);
                        int left = r is MantraResult m ? MantraShuffle.Undo(RzStrings(), m) : VowelPlay.Undo(RzStrings(), (VowelPlayResult)r);
                        said = t.Tool.Name + " put back" + (left > 0 ? "; " + left + " string(s) changed since stay as they are." : ".");
                        HostStringsChanged?.Invoke(t.Tool.Name + " put back (by the assistant).");
                        return said;
                    }
                default: throw new McpEditorException(t.Tool.Name + " has nothing to undo (automatic zones change the zoning: zones_undo or zones_open).");
            }
            RzApplied(t.Tool.Name + " undone (by the assistant).");
            if (skipped.Count > 0) said += " Left as they are because they were edited since: level" + (skipped.Count == 1 ? " " : "s ") + string.Join(", ", skipped) + " (undo steps back there).";
            return said;
        }

        /// <summary>Runs a job's three stages here, one after another (the server alone, the tests).</summary>
        public static string RunJob(Func<Func<string>> job) { return job()(); }

        /// <summary>Preview, run to its end here.</summary>
        public string RandomizerPreview(string name) { return RunJob(RandomizerPreviewJob(name)); }

        /// <summary>Apply, run to its end here.</summary>
        public string RandomizerApply(string name, bool replace = false) { return RunJob(RandomizerApplyJob(name, replace)); }
    }
}
