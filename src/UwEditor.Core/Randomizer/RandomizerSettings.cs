using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>Randomize mantras' settings: the seed alone.</summary>
    public sealed class MantraOptions
    {
        public int Seed { get; set; } = 1;
    }

    /// <summary>The Automatic zones window's settings: the movement options and the player logic (a build has no seed).</summary>
    public sealed class AutoZoneSettings
    {
        public NavOptions Nav = new NavOptions();
        public AbilityOptions Logic = new AbilityOptions();
    }

    /// <summary>A randomizer tool whose settings can be saved: its name in the file, its name in the menu, its options class.</summary>
    public sealed class SettingsTool
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public Type Options { get; private set; }
        public SettingsTool(string id, string name, Type options) { Id = id; Name = name; Options = options; }
        public override string ToString() { return Name; }
    }

    /// <summary>What reading a settings text gave: the options (or why not), the warnings, and what the text says of where it came from.</summary>
    public sealed class SettingsRead
    {
        /// <summary>The options read, the defaults given standing in for every missing key; null when the text was refused.</summary>
        public object Options;
        /// <summary>Why the text was refused (not settings, another game or tool, a newer format, a value that cannot be read), or null.</summary>
        public string Error;
        /// <summary>Keys this editor does not know (ignored), a missing seed or options.</summary>
        public readonly List<string> Warnings = new List<string>();
        /// <summary>The editor version that wrote the text, the seed (null for a tool without one), the short hash of the levels it was saved from ("" when not recorded), and the outside inputs it recorded.</summary>
        public string Editor = "", Levels = "";
        public int? Seed;
        public readonly Dictionary<string, string> Inputs = new Dictionary<string, string>();
        public bool Ok { get { return Error == null; } }
        public T As<T>() where T : class { return Options as T; }
    }

    /// <summary>
    /// The randomizer settings file: one JSON format for every randomizer window's settings, so
    /// that a run can be shared and made again. It holds the format and its version, the editor
    /// that wrote it, the game, the tool, the seed, a short hash of the levels the settings were
    /// saved from, the outside inputs (a painted zoning), and every option of the tool under
    /// "options", named as the options class names them (camelCase), nested ones included.
    /// Reading is tolerant: a missing key takes the default given, an unknown key is a warning; a
    /// text for another game or tool, or of a newer format, is refused. <see cref="Write"/> gives
    /// the pretty form (LF line ends) or the one-line form for a chat; both read alike.
    /// </summary>
    public static class RandomizerSettings
    {
        public const string FormatName = "uw-randomizer-settings";
        public const int FormatVersion = 1;

        public static readonly SettingsTool KeyScatter = new SettingsTool("key-scatter", "Key item scatter ▸ Apply", typeof(KeyShuffleOptions));
        public static readonly SettingsTool ItemShuffle = new SettingsTool("item-shuffle", "Item property shuffle", typeof(ShuffleOptions));
        public static readonly SettingsTool ItemSwap = new SettingsTool("item-swap", "Swap items", typeof(SwapSettings));
        public static readonly SettingsTool MonsterSwap = new SettingsTool("monster-swap", "Swap monsters", typeof(MonsterSwapOptions));
        public static readonly SettingsTool Challenges = new SettingsTool("challenges", "Named challenges", typeof(ChallengeOptions));
        public static readonly SettingsTool Mantras = new SettingsTool("mantras", "Randomize mantras", typeof(MantraOptions));
        public static readonly SettingsTool VowelPlay = new SettingsTool("vowel-play", "Vowel play", typeof(VowelPlayOptions));
        public static readonly SettingsTool AutoZones = new SettingsTool("auto-zones", "Automatic zones", typeof(AutoZoneSettings));
        public static readonly IReadOnlyList<SettingsTool> Tools = new[] { KeyScatter, ItemShuffle, ItemSwap, MonsterSwap, Challenges, Mantras, VowelPlay, AutoZones };

        public static SettingsTool Tool(string id) { return Tools.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)); }

        /// <summary>
        /// Public fields and properties of the options classes that the file leaves out on purpose,
        /// and why: runtime things a run fills in itself, or objects recorded as inputs instead.
        /// Every other public field or property is carried (a test checks that it round-trips).
        /// The seed of a tool's options is carried in the file's "seed", not under "options".
        /// </summary>
        public static readonly IReadOnlyDictionary<Type, IReadOnlyDictionary<string, string>> LeftOut = new Dictionary<Type, IReadOnlyDictionary<string, string>>
        {
            [typeof(KeyShuffleOptions)] = new Dictionary<string, string>
            {
                ["Names"] = "a zone set read from the zone file in DATA or Karl's painted map; it only names the places in the spoiler log, and the window loads it itself",
                ["NamesFrom"] = "says in the log where Names came from",
                ["Zoning"] = "the Zones window's zoning (a zone set object), copied in at each run; recorded under \"inputs\" as its file name and a hash",
                ["ZoningFrom"] = "says in the log where Zoning came from",
            },
            [typeof(NavOptions)] = new Dictionary<string, string>
            {
                ["StartOverride"] = "set by the key shuffle for each attempt's start (the Start settings say where)",
                ["FreeChains"] = "filled from the facts at each build",
                ["DoorAlternatives"] = "filled from the facts at each build",
                ["ClosersOneWay"] = "set on by every timeline build",
            },
        };

        /// <summary>Options classes nested in another tool's options, copied into the object already there (the windows' shared logic panel holds them).</summary>
        static readonly HashSet<Type> Nested = new HashSet<Type> { typeof(AbilityOptions), typeof(NavOptions) };

        static bool IsLeftOut(Type t, string member)
        {
            IReadOnlyDictionary<string, string> d;
            if (member == "Seed" && Tools.Any(x => x.Options == t)) return true;
            return LeftOut.TryGetValue(t, out d) && d.ContainsKey(member);
        }

        static readonly JsonSerializerOptions Json = MakeJson();

        static JsonSerializerOptions MakeJson()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(ti =>
            {
                if (ti.Kind != JsonTypeInfoKind.Object) return;
                for (int i = ti.Properties.Count - 1; i >= 0; i--)
                {
                    var m = ti.Properties[i].AttributeProvider as MemberInfo;
                    if (m != null && (IsLeftOut(ti.Type, m.Name) || m.GetCustomAttribute<JsonIgnoreAttribute>() != null)) ti.Properties.RemoveAt(i);
                }
            });
            var o = new JsonSerializerOptions
            {
                IncludeFields = true, WriteIndented = true, NewLine = "\n",
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                TypeInfoResolver = resolver,
                ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            };
            o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            o.Converters.Add(new StartSquareConverter());
            o.MakeReadOnly();
            return o;
        }

        /// <summary>A given start is written as its square, {"level": 1, "x": 30, "y": 32}, and read back as the window makes it (KeyShuffleOptions.GivenStart).</summary>
        sealed class StartSquareConverter : JsonConverter<StartPoint>
        {
            public override StartPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                var n = JsonNode.Parse(ref reader) as JsonObject;
                if (n == null) throw new JsonException("a start square is written {\"level\": 1, \"x\": 30, \"y\": 32}");
                Func<string, int> get = k =>
                {
                    var v = n.FirstOrDefault(kv => string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase)).Value;
                    if (v == null) throw new JsonException("the start square has no \"" + k + "\"");
                    return (int)Math.Floor((double)v);
                };
                return KeyShuffleOptions.GivenStart(get("level"), get("x"), get("y"));
            }

            public override void Write(Utf8JsonWriter writer, StartPoint value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber("level", value.Level);
                writer.WriteNumber("x", (int)Math.Floor(value.X));
                writer.WriteNumber("y", (int)Math.Floor(value.Y));
                writer.WriteEndObject();
            }
        }

        /// <summary>The members of an options class the file carries, by their JSON names.</summary>
        public static IReadOnlyList<string> CarriedMembers(Type t)
        {
            return Json.GetTypeInfo(t).Properties.Select(p => ((MemberInfo)p.AttributeProvider).Name).ToList();
        }

        /// <summary>The editor's version (Directory.Build.props), as the file records it.</summary>
        public static string EditorVersion
        {
            get
            {
                var a = typeof(RandomizerSettings).Assembly;
                string v = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? a.GetName().Version?.ToString() ?? "";
                int plus = v.IndexOf('+');
                return plus >= 0 ? v.Substring(0, plus) : v;
            }
        }

        /// <summary>"UW1" or "UW2".</summary>
        public static string GameOf(GameSession s) { return s.IsUw2 ? "UW2" : "UW1"; }

        static string GameName(string game) { return string.Equals(game, "UW2", StringComparison.OrdinalIgnoreCase) ? "Ultima Underworld 2" : string.Equals(game, "UW1", StringComparison.OrdinalIgnoreCase) ? "Ultima Underworld 1" : "\"" + game + "\""; }

        // ---- the seed, lifted out of the options

        static PropertyInfo SeedProperty(Type t) { var p = t.GetProperty("Seed"); return p != null && p.PropertyType == typeof(int) && p.CanWrite ? p : null; }
        static FieldInfo SeedField(Type t) { var f = t.GetField("Seed"); return f != null && f.FieldType == typeof(int) ? f : null; }

        /// <summary>The options' seed, or null for a tool without one.</summary>
        public static int? SeedOf(object options)
        {
            if (options == null) return null;
            var p = SeedProperty(options.GetType());
            if (p != null) return (int)p.GetValue(options);
            var f = SeedField(options.GetType());
            return f != null ? (int)f.GetValue(options) : (int?)null;
        }

        static void SetSeed(object options, int seed)
        {
            var p = SeedProperty(options.GetType());
            if (p != null) { p.SetValue(options, seed); return; }
            SeedField(options.GetType())?.SetValue(options, seed);
        }

        // ---- writing

        /// <summary>The options alone, as the file's "options" object.</summary>
        public static JsonObject OptionsNode(object options) { return (JsonObject)JsonSerializer.SerializeToNode(options, options.GetType(), Json); }

        /// <summary>
        /// The settings text of a tool's options: pretty (two-space indents, LF) or on one line
        /// (compact, for a chat or a log). levels is the short hash of the levels the settings go
        /// with (<see cref="LevelsHash"/>), or null; inputs the outside things the run used (a
        /// painted zoning), by name, or null.
        /// </summary>
        public static string Write(string game, SettingsTool tool, object options, string levels = null, IDictionary<string, string> inputs = null, bool compact = false)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.GetType() != tool.Options) throw new ArgumentException("the " + tool.Id + " settings are " + tool.Options.Name + ", not " + options.GetType().Name);
            var root = new JsonObject
            {
                ["format"] = FormatName,
                ["version"] = FormatVersion,
                ["editor"] = EditorVersion,
                ["game"] = game,
                ["tool"] = tool.Id,
            };
            int? seed = SeedOf(options);
            if (seed != null) root["seed"] = seed.Value;
            if (!string.IsNullOrEmpty(levels)) root["levels"] = levels;
            if (inputs != null && inputs.Count > 0)
            {
                var i = new JsonObject();
                foreach (var kv in inputs) i[kv.Key] = kv.Value;
                root["inputs"] = i;
            }
            root["options"] = OptionsNode(options);
            return Text(root, compact);
        }

        static string Text(JsonNode n, bool compact)
        {
            string s = n.ToJsonString(compact ? new JsonSerializerOptions(Json) { WriteIndented = false } : Json);
            return s.Replace("\r\n", "\n");
        }

        // ---- reading

        static readonly string[] HeaderKeys = { "format", "version", "editor", "game", "tool", "seed", "levels", "inputs", "options" };

        static JsonNode Find(JsonObject o, string key, out string name)
        {
            foreach (var kv in o) if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) { name = kv.Key; return kv.Value; }
            name = null;
            return null;
        }

        static JsonNode Get(JsonObject o, string key) { string n; return Find(o, key, out n); }

        static string Str(JsonNode n) { try { return n == null ? null : n.GetValueKind() == JsonValueKind.String ? (string)n : n.ToJsonString(); } catch (Exception) { return null; } }

        /// <summary>
        /// Reads a settings text for this game ("UW1", "UW2") and tool. defaults gives every option
        /// the text leaves out (it is not changed); a new object comes back. Refused, with a
        /// message: not settings text, another game or tool, a newer format version, a value that
        /// cannot be read. Warned: keys this editor does not know, no seed, no options.
        /// </summary>
        public static SettingsRead Read(string text, string game, SettingsTool tool, object defaults)
        {
            var r = new SettingsRead();
            JsonObject root;
            try { root = JsonNode.Parse((text ?? "").Trim(), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject; }
            catch (Exception ex) { r.Error = "This is not randomizer settings text: " + ex.Message; return r; }
            if (root == null || Str(Get(root, "format")) != FormatName) { r.Error = "This is not randomizer settings text: it should start {\"format\": \"" + FormatName + "\", …}."; return r; }
            int version;
            if (!int.TryParse(Str(Get(root, "version")), out version)) { r.Error = "The settings text has no format version (\"version\": " + FormatVersion + ")."; return r; }
            r.Editor = Str(Get(root, "editor")) ?? "";
            if (version > FormatVersion) { r.Error = string.Format("These settings were written by a newer editor ({0}) in format version {1}; this editor ({2}) reads format version {3}. Update the editor to load them.", r.Editor.Length > 0 ? r.Editor : "unknown", version, EditorVersion, FormatVersion); return r; }
            string g = Str(Get(root, "game")) ?? "";
            if (!string.Equals(g, game, StringComparison.OrdinalIgnoreCase)) { r.Error = string.Format("These settings are for {0}, and the game open is {1}.", GameName(g), GameName(game)); return r; }
            string t = Str(Get(root, "tool")) ?? "";
            if (!string.Equals(t, tool.Id, StringComparison.OrdinalIgnoreCase))
            {
                var other = Tool(t);
                r.Error = string.Format("These settings are for {0}, not {1}; load them in that window.", other != null ? other.Name : "\"" + t + "\"", tool.Name);
                return r;
            }
            foreach (var kv in root) if (!HeaderKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) r.Warnings.Add("Unknown key \"" + kv.Key + "\" ignored.");
            r.Levels = Str(Get(root, "levels")) ?? "";
            if (Get(root, "inputs") is JsonObject ins) foreach (var kv in ins) r.Inputs[kv.Key] = Str(kv.Value) ?? "";

            // the defaults, with what the text gives laid over them
            var merged = OptionsNode(defaults);
            var given = Get(root, "options");
            if (given is JsonObject go) Merge(merged, go);
            else if (given == null) r.Warnings.Add("The text has no \"options\": every option takes its default.");
            else { r.Error = "\"options\" should be an object: {…}."; return r; }
            object o;
            try { o = merged.Deserialize(tool.Options, Json); }
            catch (Exception ex) { r.Error = "A setting could not be read: " + ex.Message; return r; }
            if (o == null) { r.Error = "The options could not be read."; return r; }

            int? dseed = SeedOf(defaults);
            if (dseed != null)
            {
                int seed;
                var sn = Get(root, "seed");
                if (sn == null) { r.Warnings.Add("The text has no \"seed\": the seed stays " + dseed.Value + "."); seed = dseed.Value; }
                else if (!int.TryParse(Str(sn), out seed)) { r.Error = "The seed should be a whole number: " + Str(sn) + "."; return r; }
                SetSeed(o, seed);
                r.Seed = seed;
            }
            if (given is JsonObject g2) Unknown(g2, OptionsNode(o), "options", r.Warnings);
            r.Options = o;
            return r;
        }

        /// <summary>Lays given over into (objects key by key, everything else replaced), the keys matched without regard to case.</summary>
        static void Merge(JsonObject into, JsonObject given)
        {
            foreach (var kv in given.ToList())
            {
                string name;
                var there = Find(into, kv.Key, out name);
                if (there is JsonObject a && kv.Value is JsonObject b) { Merge(a, b); continue; }
                into[name ?? kv.Key] = kv.Value?.DeepClone();
            }
        }

        /// <summary>The keys of given that the options read back do not have: names this editor does not know.</summary>
        static void Unknown(JsonObject given, JsonObject back, string path, List<string> warnings)
        {
            foreach (var kv in given)
            {
                string name;
                var b = Find(back, kv.Key, out name);
                if (name == null) { warnings.Add("Unknown setting \"" + path + "." + kv.Key + "\" ignored."); continue; }
                if (kv.Value is JsonObject a && b is JsonObject bo) Unknown(a, bo, path + "." + kv.Key, warnings);
            }
        }

        // ---- loading into a window's options

        /// <summary>
        /// Copies every option the file carries from one options object into another of its class:
        /// nested options (the logic, the movement) into the objects already there, lists, sets and
        /// dictionaries into the collections already there (others may hold them), the rest set.
        /// What the file leaves out is not touched.
        /// </summary>
        public static void CopyInto(object from, object to)
        {
            if (from == null || to == null || from.GetType() != to.GetType()) throw new ArgumentException("both options must be of one class");
            var t = from.GetType();
            int? seed = SeedOf(from);
            if (seed != null) SetSeed(to, seed.Value);
            foreach (var p in Json.GetTypeInfo(t).Properties)
            {
                object v = p.Get(from), cur = p.Get(to);
                if (v != null && cur != null && Nested.Contains(p.PropertyType)) { CopyInto(v, cur); continue; }
                if (v != null && cur != null && v.GetType() == cur.GetType() && !(cur is Array) && !ReferenceEquals(v, cur))
                {
                    if (cur is IDictionary d) { d.Clear(); foreach (DictionaryEntry e in (IDictionary)v) d[e.Key] = e.Value; continue; }
                    if (cur is IList l) { l.Clear(); foreach (var e in (IList)v) l.Add(e); continue; }
                    var clear = cur.GetType().GetMethod("Clear", Type.EmptyTypes);
                    var union = cur.GetType().GetMethod("UnionWith");
                    if (clear != null && union != null) { clear.Invoke(cur, null); union.Invoke(cur, new[] { v }); continue; }
                }
                p.Set(to, v);
            }
        }

        /// <summary>Whether two options objects carry the same settings (their texts are equal).</summary>
        public static bool SameSettings(object a, object b)
        {
            if (a == null || b == null) return a == b;
            return SeedOf(a) == SeedOf(b) && OptionsNode(a).ToJsonString() == OptionsNode(b).ToJsonString();
        }

        // ---- what the settings go with

        /// <summary>A short hash (8 hex digits) of a text, to tell whether an input is still the same.</summary>
        public static string ShortHash(string text) { return ShortHash(Encoding.UTF8.GetBytes(text ?? "")); }

        static string ShortHash(byte[] bytes)
        {
            var h = SHA256.HashData(bytes);
            return Convert.ToHexString(h, 0, 4).ToLowerInvariant();
        }

        /// <summary>
        /// A short hash of the levels as they are now (each present level's block, with the edits
        /// not yet saved): the runs depend on them, so a settings file records it and loading warns
        /// when the levels differ. edits may be null (the levels on disk).
        /// </summary>
        public static string LevelsHash(GameSession s, LevelEdits edits)
        {
            using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var p = s.Profile;
                foreach (var slot in s.LevelSlots())
                {
                    if (!slot.Present) continue;
                    var e = edits?.Existing(slot.Level);
                    byte[] data = e != null ? e.Data : s.LevArk.Has(p.LevelBlock(slot.Level)) ? s.LevArk.Read(p.LevelBlock(slot.Level)) : null;
                    if (data == null) continue;
                    h.AppendData(BitConverter.GetBytes(slot.Level));
                    h.AppendData(data);
                }
                return Convert.ToHexString(h.GetHashAndReset(), 0, 4).ToLowerInvariant();
            }
        }

        /// <summary>The warning when the levels now differ from those the settings were saved with ("" when they match or nothing was recorded).</summary>
        public static string LevelsWarning(SettingsRead r, string now)
        {
            if (r == null || string.IsNullOrEmpty(r.Levels) || string.Equals(r.Levels, now, StringComparison.OrdinalIgnoreCase)) return "";
            return "The levels differ from those the settings were saved with (" + r.Levels + ", now " + now + "): the same settings may give another result.";
        }

        /// <summary>The usual name of a settings file: the game, the tool, the seed (uw1-key-scatter-seed-1.json).</summary>
        public static string FileName(string game, SettingsTool tool, int? seed)
        {
            return game.ToLowerInvariant() + "-" + tool.Id + (seed != null ? "-seed-" + seed.Value : "") + ".json";
        }

        /// <summary>The line a log or report starts with, so that the log alone makes the run again.</summary>
        public static string LogLine(string compact) { return "Settings (Paste settings in the window makes this run again): " + compact; }

        /// <summary>The settings text in a log or report written with <see cref="LogLine"/>, or null.</summary>
        public static string FromLog(string log)
        {
            if (log == null) return null;
            int i = log.IndexOf("{\"format\":\"" + FormatName + "\"", StringComparison.Ordinal);
            if (i < 0) return null;
            int end = log.IndexOf('\n', i);
            return (end < 0 ? log.Substring(i) : log.Substring(i, end - i)).Trim();
        }
    }
}
