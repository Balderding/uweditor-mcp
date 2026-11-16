using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// A place a fact names: a tile of a level, or an object of a level (its tile), or a special
    /// word ("planted": wherever the player planted the silver sapling, see the warp kind).
    /// </summary>
    public sealed class FactSpot
    {
        public int Level;
        public int X = -1, Y = -1;
        public int Object = -1;
        /// <summary>A word in place of a place: "planted" (or "back").</summary>
        public string Special = "";

        public bool IsPlanted { get { return Special == "planted" || Special == "back"; } }
        public bool HasTile { get { return Level > 0 && X >= 0 && Y >= 0 && X < 64 && Y < 64; } }
        public bool HasObject { get { return Level > 0 && Object >= 0; } }
        public bool Valid { get { return IsPlanted || HasTile || HasObject; } }

        public override string ToString()
        {
            if (Special.Length > 0) return Special;
            if (HasObject) return string.Format("L{0} #{1}", Level, Object);
            return string.Format("L{0} ({1}, {2})", Level, X, Y);
        }
    }

    /// <summary>
    /// The object a conversation's gift is (a fact's "gift"): {"item": 0x10D, "owner": 28} for a
    /// key (a key keeps its lock number in its owner field), with quality, link, flags and
    /// is-quantity as an object record holds them; the defaults are what conv/BARTER.C
    /// npc_inv_create makes (quality 0x3F, a quantity of 1).
    /// </summary>
    public sealed class FactGift
    {
        public int Item = -1;
        public int Quality = 0x3F, Owner, Link = 1, Flags;
        public bool IsQuantity = true, Enchanted;
        public bool Valid { get { return Item >= 0 && Item < 0x140; } }

        /// <summary>The new object's fields for the editing API (LevelEditor.AddObject).</summary>
        public UwEditor.Core.Editing.ObjectSpec Spec()
        {
            return new UwEditor.Core.Editing.ObjectSpec { Item = Item, Quality = Quality, Owner = Owner, Link = Link, Flags = Flags, IsQuantity = IsQuantity, Enchanted = Enchanted };
        }

        public override string ToString() { return string.Format("item 0x{0:X}, quality {1}, owner {2}", Item, Quality, Owner); }
    }

    /// <summary>
    /// The kind of object a flag fact's object is (a fact's "match"): {"item": "0x119"}, with
    /// quality or link when the game tells such objects apart by them (a storage crystal by its
    /// quality, a potion by its spell in the link). The game checks the thing by its kind and not
    /// by where it lies (a blackrock key gem: obj/USEITEMS.C UseKeyGem), so the fact follows the
    /// object when the key item scatter moves it: AbilityZones.FactObjects finds it wherever it
    /// lies. Null when the fact names its object by place alone.
    /// </summary>
    public sealed class FactMatch
    {
        public int Item = -1;
        /// <summary>-1: any.</summary>
        public int Quality = -1, Link = -1;
        public bool Valid { get { return Item >= 0 && Item < 0x140; } }

        public bool Matches(ObjectInfo o)
        {
            return o != null && !o.Free && o.Item == Item && (Quality < 0 || o.Quality == Quality) && (Link < 0 || o.Link == Link);
        }

        public override string ToString() { return string.Format("item 0x{0:X}{1}{2}", Item, Quality >= 0 ? ", quality " + Quality : "", Link >= 0 ? ", link " + Link : ""); }
    }

    /// <summary>One hand-written fact of a facts file (docs/facts/uw1-*.json).</summary>
    public sealed class Fact
    {
        public string Id = "";
        /// <summary>craft, trade, flag, nomana, walls, hidden; Phase C: link, warp, goal, free, needs, door; Phase D: count; lost (a token or a place that may be lost: mayBeLost); spare (creatures the logic must never kill: nothing they carry or their deaths give counts, and their token may never be required); carried (what a creature carries is had only with Requires: UW2's Janar and his key ring).</summary>
        public string Kind = "";
        /// <summary>craft, flag: the token granted.</summary>
        public string Grants = "";
        /// <summary>trade: the token whose placements get the requirement.</summary>
        public string Token = "";
        public string Requires = "";
        /// <summary>craft, trade, flag: the tokens of Requires the step uses up (food handed over): each use needs a source of its own.</summary>
        public readonly List<string> UsesUp = new List<string>();
        /// <summary>nomana: the token that lifts it.</summary>
        public string Until = "";
        public int Level;
        /// <summary>flag: the object whose zone grants it; door: the door that Requires also opens; hidden: the look trigger.</summary>
        public int Object = -1, Trigger = -1;
        /// <summary>A tile given at the top level (goal, free, flag): x and y, or -1.</summary>
        public int X = -1, Y = -1;
        /// <summary>walls, nomana (optional: only there): the rectangle x0, y0, x1, y1 (inclusive).</summary>
        public int[] Rect;
        /// <summary>walls: which tiles of the rectangle (damage: move triggers whose chain hurts the player).</summary>
        public string Tiles = "";
        public string Notes = "";
        public string Source = "";
        /// <summary>certain, probable, todo (optional).</summary>
        public string Confidence = "";
        /// <summary>The file it was read from (uw1-facts.json, uw1-quests.json ...).</summary>
        public string File = "";

        // ---- Phase C

        /// <summary>link: where it starts and ends; warp: where it goes (To); goal: where the goal is (To, or the top-level level and tile or object).</summary>
        public FactSpot From, To;
        /// <summary>link: both ways.</summary>
        public bool TwoWay;
        /// <summary>link, warp: the option (without "option:") the fact needs; off: the link takes Otherwise, or does not exist.</summary>
        public string Option = "";
        /// <summary>link: the requirement when the option is off; empty: then there is no link.</summary>
        public string Otherwise = "";
        /// <summary>link: the links' kind (default "other").</summary>
        public string LinkKind = "";
        /// <summary>link: the other links between the two zones are removed (the fact is the only way between them).</summary>
        public bool Replace;
        /// <summary>
        /// link: "once": the way can be taken only once (the Historian's teleport to the island,
        /// quest 39). The solve takes what lies where it leads on arrival and walks out; that place
        /// counts again only by another way (Timeline's once links), and the walkthrough warns.
        /// </summary>
        public bool Once;
        /// <summary>warp: only from the zones of this level (0: from every zone).</summary>
        public int FromLevel;
        /// <summary>free: the objects (levers) and triggers whose chains count as done once their zone is reached, and the flags granted there.</summary>
        public readonly List<int> Objects = new List<int>();
        public readonly List<int> Triggers = new List<int>();
        public readonly List<string> Flags = new List<string>();
        /// <summary>
        /// carried: only these objects the creature carries (and what lies in them) take the
        /// requirement. A carried fact with none covers everything the creature carries that no
        /// other carried fact of the same creature names (Altara's runes apart from her dagger).
        /// </summary>
        public readonly List<int> Only = new List<int>();

        // ---- Phase D

        /// <summary>count: the item ids whose quantities add up (a stack counts its quantity, as conv/CONVERSE.C count_inv does).</summary>
        public readonly List<int> Items = new List<int>();
        /// <summary>count: the total that grants the token.</summary>
        public int AtLeast;

        // ---- things the game takes away for good (docs/UW2-RANDOMIZER-NEXT-STEPS.md 2.1)

        /// <summary>
        /// "mayBeLost": how what the fact gives (or, for kind "lost", the token or place it names)
        /// may be lost for good: "a" only by a choice the player can always avoid (a wrong answer,
        /// joining Mors Gotha), so the logic may rely on it and the walkthrough warns; "b" with time
        /// whatever the player does (a window of the castle plot); "c" a place that closes with what
        /// lies in it. "" when it lasts.
        /// </summary>
        public string Lost = "";
        /// <summary>mayBeLost: why, in a few words (the walkthrough's warning).</summary>
        public string LostWhy = "";
        /// <summary>mayBeLost: a requirement that holds once it is gone (kind b: "plot:14"); "" when no window is known (then never counted with the option on).</summary>
        public string LostWhen = "";

        // ---- creatures the logic must never kill, and gifts the randomizer can make anew

        /// <summary>spare: the whoami of the creatures (with Level: only on that level; with Object: that one), or -1.</summary>
        public int Whoami = -1;
        /// <summary>
        /// "gift": the object that stands for what a conversation hands over (a flag fact's token),
        /// so that the key item shuffle can make a new one when the start cannot get the gift in
        /// time (TimedGifts); null when the fact names none.
        /// </summary>
        public FactGift Gift;
        /// <summary>flag: the kind of its object (FactMatch), so the fact follows the object wherever it lies; null: the object is named by place alone.</summary>
        public FactMatch Match;

        /// <summary>The rank of a may-be-lost kind: "" 0, a 1, b 2, c 3 (a mark only ever moves up).</summary>
        public static int LostRank(string kind) { return kind == "c" ? 3 : kind == "b" ? 2 : kind == "a" ? 1 : 0; }

        public bool InRect(int x, int y) { return Rect != null && Rect.Length == 4 && x >= Rect[0] && x <= Rect[2] && y >= Rect[1] && y <= Rect[3]; }
        public override string ToString() { return Id + " (" + Kind + ")"; }

        /// <summary>The place the fact names at its top level (level with x and y, or with object), or null.</summary>
        public FactSpot TopSpot
        {
            get
            {
                if (Level <= 0) return null;
                if (Object >= 0) return new FactSpot { Level = Level, Object = Object };
                if (X >= 0 && Y >= 0) return new FactSpot { Level = Level, X = X, Y = Y };
                return null;
            }
        }
    }

    /// <summary>
    /// The facts files: ability sources that are not plain items (the dragon skin boots), flags set
    /// by using an item somewhere (the orb), areas with special rules (the damaging maze), notes on
    /// the hidden gates, and (Phase C) links, warps, the goal, and chains that count as done. A
    /// small JSON format meant to be edited by hand ("uw1-facts"). Every docs/facts/uw1-*.json is
    /// embedded under its own file name and read by <see cref="Shipped"/>, uw1-facts.json first.
    /// </summary>
    public sealed class FactSet
    {
        public string Game = "UW1";
        public string Notes = "";
        public readonly List<Fact> Facts = new List<Fact>();
        /// <summary>The files read (uw1-facts.json, uw1-quests.json ...).</summary>
        public readonly List<string> Files = new List<string>();

        /// <summary>The option names the facts may test (option:NAME), as the automatic zones' options set them.</summary>
        public static readonly string[] KnownOptions =
        {
            "magic", "single_use", "lockpick", "pick_retry", "search_retry", "damage_walls", "ring_needs_lava", "kill_npcs",
            "cut_levels", "gap_jumps", "diagonal_jumps", "glitches", "require_orb", "pits_gem_by_blog",
        };

        /// <summary>
        /// The "spare" fact naming a creature the logic must never kill, when the object is that
        /// creature or lies in one (however deep): by its whoami (and level, if the fact gives one)
        /// or by level and object. Null when none.
        /// </summary>
        public Fact Spared(int level, LevelModel m, ObjectInfo o)
        {
            if (m == null || o == null) return null;
            var spare = OfKind("spare").ToList();
            if (spare.Count == 0) return null;
            var at = o;
            for (int g = 0; at != null && g < 64; g++, at = at.Parent >= 0 && at.Parent < m.Objects.Length ? m.Objects[at.Parent] : null)
            {
                if (at.IsCritter && at.Record != null)
                {
                    int who = at.Record.Length > 0x1A ? at.Record[0x1A] : 0;
                    foreach (var f in spare)
                    {
                        if (f.Level > 0 && f.Level != level) continue;
                        if (f.Object >= 0 ? at.Index == f.Object : f.Whoami > 0 && who == f.Whoami) return f;
                    }
                }
            }
            return null;
        }

        /// <summary>The tokens that stand for the deaths of creatures the logic must spare (the spare facts' "token"): no requirement may ask for them.</summary>
        public HashSet<string> SpareTokens()
        {
            return new HashSet<string>(OfKind("spare").Where(f => f.Token.Length > 0).Select(f => Requirement.Normalize(f.Token)));
        }

        public IEnumerable<Fact> OfKind(string kind) { return Facts.Where(f => f.Kind == kind); }

        /// <summary>The nomana fact that covers a square of a level (one with a 'rect' only inside it; an unknown square, x or y below 0, counts as inside), or null.</summary>
        public Fact NoMana(int level, int x, int y)
        {
            return OfKind("nomana").FirstOrDefault(f => f.Level == level && (f.Rect == null || x < 0 || y < 0 || f.InRect(x, y)));
        }

        /// <summary>
        /// The extra conditions the facts give a mechanism (kind "needs" by its flag or its level
        /// and trigger, or a trade whose token is a mechanism flag): the mechanism goes off only
        /// when they hold too. "free" when none.
        /// </summary>
        public string MechanismNeeds(int level, int trigger, string flag)
        {
            string fl = Requirement.Normalize(flag);
            var terms = Facts.Where(f => (f.Kind == "needs" && ((f.Token.Length > 0 && Requirement.Normalize(f.Token) == fl) || (f.Token.Length == 0 && f.Level == level && f.Trigger == trigger)))
                || (f.Kind == "trade" && Requirement.Normalize(f.Token) == fl)).Select(f => f.Requires).Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
            return ZoneLink.JoinAnd(terms);
        }

        /// <summary>
        /// The "lost" fact (the strongest kind) whose place holds an object of a level: the object
        /// itself or a creature or container it lies in is the fact's object (what a creature
        /// carries goes when the schedule takes it off the map: event/SCDEVENT.C ev_remove), or
        /// its square (or its holder's) lies in the fact's rect, or the fact names the level
        /// alone. Null when none.
        /// </summary>
        public Fact LostPlace(int level, LevelModel m, ObjectInfo o)
        {
            Fact best = null;
            if (m == null || o == null) return null;
            foreach (var f in OfKind("lost"))
            {
                if (f.Level != level || f.Token.Length > 0 || Fact.LostRank(f.Lost) <= (best == null ? 0 : Fact.LostRank(best.Lost))) continue;
                bool inside;
                if (f.Object >= 0)
                {
                    inside = o.Index == f.Object;
                    for (int up = o.Parent, g = 0; !inside && up >= 0 && up < m.Objects.Length && g < 64; up = m.Objects[up].Parent, g++) inside = up == f.Object;
                }
                else if (f.Rect != null)
                {
                    var at = o;
                    for (int g = 0; at.TileX < 0 && at.Parent >= 0 && at.Parent < m.Objects.Length && g < 64; g++) at = m.Objects[at.Parent];
                    inside = f.InRect(at.TileX, at.TileY);
                }
                else inside = true;
                if (inside) best = f;
            }
            return best;
        }

        /// <summary>The "lost" fact (the strongest kind) that names a token (every source of it), or null.</summary>
        public Fact LostToken(string token)
        {
            string t = Requirement.Normalize(token);
            return OfKind("lost").Where(f => f.Token.Length > 0 && Requirement.Normalize(f.Token) == t).OrderByDescending(f => Fact.LostRank(f.Lost)).FirstOrDefault();
        }

        /// <summary>The tokens the facts grant (craft, flag, free flags).</summary>
        public HashSet<string> Granted()
        {
            var h = new HashSet<string>();
            foreach (var f in Facts)
            {
                if ((f.Kind == "craft" || f.Kind == "flag") && f.Grants.Length > 0) h.Add(Requirement.Normalize(f.Grants));
                if (f.Kind == "count" && f.Grants.Length > 0) h.Add(Requirement.Normalize(f.Grants));
                if (f.Kind == "free") foreach (var t in f.Flags) h.Add(Requirement.Normalize(t));
            }
            return h;
        }

        public static FactSet Load(string path)
        {
            var set = FromJson(File.ReadAllText(path), Path.GetFileName(path));
            return set;
        }

        /// <summary>The facts shipped with the editor for a game (every embedded uw1-*.json or uw2-*.json, the game's -facts.json first), or an empty set.</summary>
        public static FactSet Shipped(bool uw2)
        {
            var asm = typeof(FactSet).Assembly;
            string first = uw2 ? "uw2-facts.json" : "uw1-facts.json";
            var names = asm.GetManifestResourceNames().Where(n => IsFactsName(n, uw2)).OrderBy(n => n == first ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();
            var all = new FactSet();
            if (uw2) all.Game = "UW2";
            foreach (var name in names)
            {
                using (var st = asm.GetManifestResourceStream(name))
                {
                    if (st == null) continue;
                    FactSet one;
                    using (var r = new StreamReader(st))
                    {
                        try { one = FromJson(r.ReadToEnd(), name); }
                        catch (Exception ex) { all.LoadErrors.Add(name + ": " + ex.Message); continue; }
                    }
                    all.Add(one);
                }
            }
            return all;
        }

        /// <summary>A shipped facts file name: uw1-NAME.json (uw2-NAME.json for UW2).</summary>
        public static bool IsFactsName(string name, bool uw2 = false)
        {
            return name.StartsWith(uw2 ? "uw2-" : "uw1-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) && name.IndexOf('/') < 0 && !name.Contains("zones");
        }

        /// <summary>Files that could not be read (reported by <see cref="Check"/>).</summary>
        public readonly List<string> LoadErrors = new List<string>();

        /// <summary>Adds another file's facts (their notes are kept per fact; the first file's notes and game stay).</summary>
        public void Add(FactSet other)
        {
            if (Files.Count == 0) { Game = other.Game.Length > 0 ? other.Game : Game; Notes = other.Notes; }
            Files.AddRange(other.Files);
            Facts.AddRange(other.Facts);
            LoadErrors.AddRange(other.LoadErrors);
        }

        static string S(JsonNode n, string k)
        {
            var v = n?[k];
            if (v == null) return "";
            return v.GetValueKind() == JsonValueKind.String ? (string)v : v.ToJsonString();
        }

        static int Int(JsonNode v, int dflt)
        {
            if (v == null) return dflt;
            if (v.GetValueKind() == JsonValueKind.Number) return (int)v.AsValue().GetValue<double>();
            if (v.GetValueKind() != JsonValueKind.String) return dflt;
            int x;
            string t = ((string)v).Trim();
            if (t.StartsWith("#")) t = t.Substring(1);
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out x)) return x;
            return int.TryParse(t, out x) ? x : dflt;
        }

        static int I(JsonNode n, string k, int dflt) { return Int(n?[k], dflt); }

        static bool B(JsonNode n, string k)
        {
            var v = n?[k];
            if (v == null) return false;
            if (v.GetValueKind() == JsonValueKind.True) return true;
            if (v.GetValueKind() == JsonValueKind.String) { string t = ((string)v).Trim().ToLowerInvariant(); return t == "true" || t == "yes" || t == "1"; }
            return false;
        }

        /// <summary>A place: {"level": L, "x": X, "y": Y} or {"level": L, "object": N}, [L, X, Y], or a word ("planted").</summary>
        static FactSpot Spot(JsonNode v, int level)
        {
            if (v == null) return null;
            var kind = v.GetValueKind();
            if (kind == JsonValueKind.String) return new FactSpot { Level = level, Special = ((string)v).Trim().ToLowerInvariant() };
            if (kind == JsonValueKind.Number) return new FactSpot { Level = Int(v, 0) };
            if (v is JsonArray a)
            {
                var sp = new FactSpot();
                if (a.Count == 3) { sp.Level = Int(a[0], 0); sp.X = Int(a[1], -1); sp.Y = Int(a[2], -1); }
                else if (a.Count == 2) { sp.Level = Int(a[0], 0); sp.Object = Int(a[1], -1); }
                return sp;
            }
            if (v is JsonObject o)
            {
                var sp = new FactSpot { Level = I(o, "level", level), X = I(o, "x", -1), Y = I(o, "y", -1), Object = I(o, "object", -1) };
                var tile = o["tile"] as JsonArray;
                if (tile != null && tile.Count == 2) { sp.X = Int(tile[0], -1); sp.Y = Int(tile[1], -1); }
                return sp;
            }
            return null;
        }

        static void Ints(JsonNode n, string one, string many, List<int> into)
        {
            int v = I(n, one, -1);
            if (v >= 0) into.Add(v);
            if (n?[many] is JsonArray a) foreach (var x in a) { int k = Int(x, -1); if (k >= 0) into.Add(k); }
        }

        static string Opt(string o)
        {
            o = (o ?? "").Trim().ToLowerInvariant();
            return o.StartsWith("option:") ? o.Substring(7) : o;
        }

        public static FactSet FromJson(string json, string file = "")
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            string format = S(root, "format");
            if (format.Length > 0 && !format.EndsWith("-facts")) throw new FormatException("Not a facts file (format '" + format + "').");
            var set = new FactSet { Game = S(root, "game"), Notes = S(root, "notes") };
            if (file.Length > 0) set.Files.Add(file);
            var arr = root?["facts"] as JsonArray;
            if (arr == null) return set;
            foreach (var n in arr)
            {
                var f = new Fact
                {
                    Id = S(n, "id"), Kind = S(n, "kind").Trim().ToLowerInvariant(), Grants = S(n, "grants"), Token = S(n, "token"), Requires = S(n, "requires"), Until = S(n, "until"),
                    Level = I(n, "level", 0), Object = I(n, "object", -1), Trigger = I(n, "trigger", -1), X = I(n, "x", -1), Y = I(n, "y", -1),
                    Tiles = S(n, "tiles"), Notes = S(n, "notes"), Source = S(n, "source"), Confidence = S(n, "confidence"), File = file,
                    TwoWay = B(n, "twoWay") || B(n, "twoway") || B(n, "two_way"), Option = Opt(S(n, "option")), Otherwise = S(n, "otherwise"),
                    LinkKind = S(n, "linkKind"), Replace = B(n, "replace"), FromLevel = I(n, "fromLevel", 0), Once = B(n, "once"),
                };
                var rect = n?["rect"] as JsonArray;
                if (rect != null) f.Rect = rect.Select(x => Int(x, 0)).ToArray();
                var tile = n?["tile"] as JsonArray;
                if (tile != null && tile.Count == 2) { f.X = Int(tile[0], -1); f.Y = Int(tile[1], -1); }
                f.From = Spot(n?["from"], f.Level);
                f.To = Spot(n?["to"] ?? n?["at"], f.Level);
                if (f.Kind == "warp")
                {
                    // a warp's own level (with a destination given apart) says where it is made from
                    if (f.FromLevel == 0 && f.To != null && n?["level"] != null && n?["object"] == null && n?["x"] == null) f.FromLevel = f.Level;
                    if (f.From != null && f.From.Level > 0 && f.FromLevel == 0 && !f.From.HasTile && !f.From.HasObject) f.FromLevel = f.From.Level;
                }
                if (n?["usesUp"] is JsonArray uu) foreach (var x in uu) if (x != null && x.GetValueKind() == JsonValueKind.String) f.UsesUp.Add(Requirement.Normalize((string)x));
                if (f.Kind == "goal" && f.To == null) f.To = f.TopSpot;
                // mayBeLost: {"kind": "a" | "b" | "c", "why": "...", "when": "plot:14"}, or the kind alone
                var lost = n?["mayBeLost"];
                if (lost is JsonObject lo) { f.Lost = S(lo, "kind").Trim().ToLowerInvariant(); f.LostWhy = S(lo, "why"); f.LostWhen = S(lo, "when"); }
                else if (lost != null) f.Lost = S(n, "mayBeLost").Trim().ToLowerInvariant();
                f.Whoami = I(n, "whoami", -1);
                // gift: {"item": "0x10D", "owner": 28, "quality": 63, "link": 1, "flags": 0, "isQuantity": true, "enchanted": false}
                if (n?["gift"] is JsonObject go)
                    f.Gift = new FactGift
                    {
                        Item = I(go, "item", -1), Quality = I(go, "quality", 0x3F), Owner = I(go, "owner", 0), Link = I(go, "link", 1), Flags = I(go, "flags", 0),
                        IsQuantity = go["isQuantity"] == null || B(go, "isQuantity"), Enchanted = B(go, "enchanted"),
                    };
                // match: {"item": "0x119", "quality": 6, "link": 579}: the kind of the flag's object (FactMatch)
                if (n?["match"] is JsonObject mo) f.Match = new FactMatch { Item = I(mo, "item", -1), Quality = I(mo, "quality", -1), Link = I(mo, "link", -1) };
                if (f.Kind == "count")
                {
                    if (n?["items"] is JsonArray items) foreach (var x in items) { int k = Int(x, -1); if (k >= 0) f.Items.Add(k); }
                    f.AtLeast = I(n, "atLeast", 0);
                }
                if (f.Kind == "carried" && n?["only"] is JsonArray only) foreach (var x in only) { int k = Int(x, -1); if (k >= 0) f.Only.Add(k); }
                if (f.Kind == "free")
                {
                    Ints(n, "object", "objects", f.Objects);
                    Ints(n, "trigger", "triggers", f.Triggers);
                    if (S(n, "flag").Length > 0) f.Flags.Add(S(n, "flag"));
                    // "token" names the flag the chain's condition tested: with objects or triggers it only documents it, alone it is a flag to grant
                    if (f.Token.Length > 0 && f.Objects.Count + f.Triggers.Count == 0) f.Flags.Add(f.Token);
                    if (n?["flags"] is JsonArray fl) foreach (var x in fl) f.Flags.Add(x.GetValueKind() == JsonValueKind.String ? (string)x : x.ToJsonString());
                    for (int i = 0; i < f.Flags.Count; i++) { string t = f.Flags[i].Trim(); f.Flags[i] = t.Contains(":") ? t : "flag:" + t; }
                }
                set.Facts.Add(f);
            }
            return set;
        }

        /// <summary>The problems a hand edit may have made: unknown kinds, unreadable requirements, missing fields, ids used twice.</summary>
        public List<string> Check()
        {
            var list = new List<string>(LoadErrors);
            foreach (var g in Facts.GroupBy(f => f.Id).Where(g => g.Key.Length > 0 && g.Count() > 1))
                list.Add(g.Key + ": the id is used " + g.Count() + " times (" + string.Join(", ", g.Select(f => f.File).Distinct()) + ").");
            var spared = SpareTokens();
            foreach (var f in Facts)
            {
                Action<string> req = r =>
                {
                    string e;
                    var parsed = Requirement.TryParse(r, out e);
                    if (parsed == null) { list.Add(f.Id + ": " + e); return; }
                    // Karl: no solve may depend on the death of a creature the logic must spare (Killorn's brain creatures)
                    var toks = new List<string>();
                    parsed.Tokens(toks);
                    foreach (var t in toks.Where(spared.Contains)) list.Add(f.Id + ": requires " + t + ", which stands for killing a creature the logic must spare.");
                };
                if (f.Gift != null && !f.Gift.Valid) list.Add(f.Id + ": a gift needs 'item', an object item id below 0x140.");
                if (f.Gift != null && (f.Kind != "flag" || f.Grants.Length == 0)) list.Add(f.Id + ": only a flag fact with 'grants' names a gift.");
                if (f.Match != null && (f.Kind != "flag" || f.Object < 0 || !f.Match.Valid)) list.Add(f.Id + ": 'match' belongs to a flag fact with 'object', and needs 'item', an object item id below 0x140.");
                Action<string> option = o => { if (o.Length > 0 && !KnownOptions.Contains(o)) list.Add(f.Id + ": unknown option '" + o + "' (known: " + string.Join(", ", KnownOptions) + ")."); };
                if (f.Lost.Length > 0 && Fact.LostRank(f.Lost) == 0) list.Add(f.Id + ": mayBeLost's kind is a, b or c (not '" + f.Lost + "').");
                if (f.Lost.Length > 0 && f.LostWhy.Trim().Length == 0) list.Add(f.Id + ": mayBeLost needs 'why'.");
                // a window may name the fall (it says when a thing is gone, not what progress needs)
                if (f.LostWhen.Length > 0) { string e; if (Requirement.TryParse(f.LostWhen, out e) == null) list.Add(f.Id + ": " + e); }
                // a link carries only kind a (a choice: the walkthrough warns where it is taken); one the game lets be taken once is "once"
                if (f.Kind == "link" && f.Lost.Length > 0 && f.Lost != "a") list.Add(f.Id + ": a link's mayBeLost is of kind a (a choice the player can avoid); a way the game allows only once is marked \"once\".");
                if (f.Once && f.Kind != "link") list.Add(f.Id + ": 'once' belongs to a link.");
                if (f.Once && f.TwoWay) list.Add(f.Id + ": a link taken once is one way.");
                switch (f.Kind)
                {
                    case "spare":
                        if (f.Whoami <= 0 && (f.Level <= 0 || f.Object < 0)) list.Add(f.Id + ": a spare fact names the creatures by 'whoami' (and 'level') or by 'level' and 'object'.");
                        if (f.Notes.Trim().Length == 0) list.Add(f.Id + ": a spare fact needs 'notes' (why they must live).");
                        break;
                    case "lost":
                        if (f.Lost.Length == 0) list.Add(f.Id + ": a lost fact needs 'mayBeLost'.");
                        if (f.Token.Length == 0 && f.Level <= 0) list.Add(f.Id + ": a lost fact names a 'token' (every source of it) or a place ('level', with 'object' or 'rect', or the whole level).");
                        if (f.Rect != null && f.Rect.Length != 4) list.Add(f.Id + ": 'rect' is [x0, y0, x1, y1].");
                        break;
                    case "craft":
                    case "flag":
                        if (f.Grants.Length == 0) list.Add(f.Id + ": no 'grants'.");
                        req(f.Requires);
                        if (f.Kind == "flag" && (f.Level <= 0 || (f.Object < 0 && (f.X < 0 || f.Y < 0)))) list.Add(f.Id + ": a flag needs 'level' and 'object' (or 'x' and 'y').");
                        break;
                    case "trade":
                        if (f.Token.Length == 0) list.Add(f.Id + ": no 'token'.");
                        req(f.Requires);
                        break;
                    case "nomana":
                        if (f.Level <= 0) list.Add(f.Id + ": no 'level'.");
                        if (f.Rect != null && f.Rect.Length != 4) list.Add(f.Id + ": 'rect' is [x0, y0, x1, y1].");
                        break;
                    case "walls":
                        if (f.Level <= 0 || f.Rect == null || f.Rect.Length != 4) list.Add(f.Id + ": walls need 'level' and 'rect' [x0, y0, x1, y1].");
                        break;
                    case "hidden":
                        if (f.Level <= 0 || f.Trigger < 0) list.Add(f.Id + ": a hidden gate needs 'level' and 'trigger'.");
                        break;
                    case "link":
                        if (f.From == null || !(f.From.HasTile || f.From.HasObject)) list.Add(f.Id + ": a link needs 'from' {level, x, y} or {level, object}.");
                        if (f.To == null || !(f.To.HasTile || f.To.HasObject)) list.Add(f.Id + ": a link needs 'to' {level, x, y} or {level, object}.");
                        req(f.Requires);
                        if (f.Otherwise.Length > 0) req(f.Otherwise);
                        option(f.Option);
                        break;
                    case "warp":
                        if (f.To == null || !f.To.Valid) list.Add(f.Id + ": a warp needs 'to' {level, x, y}, {level, object} or \"planted\".");
                        req(f.Requires);
                        option(f.Option);
                        break;
                    case "goal":
                        // a goal fact with an option only adds its requirement when the option is on (Phase D: option:require_orb)
                        if (f.Option.Length == 0 && (f.To == null || !(f.To.HasTile || f.To.HasObject))) list.Add(f.Id + ": a goal needs where it is: 'level' with 'x' and 'y' or 'object' (or 'at').");
                        req(f.Requires);
                        option(f.Option);
                        break;
                    case "carried":
                        if (f.Level <= 0 || f.Object < 0) list.Add(f.Id + ": a carried fact names the creature by 'level' and 'object'.");
                        if (string.IsNullOrWhiteSpace(f.Requires)) list.Add(f.Id + ": a carried fact needs 'requires' (how what the creature carries is had).");
                        else req(f.Requires);
                        if (f.Only.Contains(f.Object)) list.Add(f.Id + ": 'only' names what the creature carries, not the creature.");
                        break;
                    case "count":
                        if (f.Grants.Length == 0) list.Add(f.Id + ": no 'grants'.");
                        if (f.Items.Count == 0 || f.AtLeast <= 0) list.Add(f.Id + ": a count needs 'items' [ids] and 'atLeast' N.");
                        break;
                    case "door":
                        if (f.Level <= 0 || f.Object < 0) list.Add(f.Id + ": a door needs 'level' and 'object' (the door's index).");
                        req(f.Requires);
                        break;
                    case "needs":
                        if (f.Token.Length == 0 && (f.Level <= 0 || f.Trigger < 0)) list.Add(f.Id + ": 'needs' names a mechanism by 'token' (its flag) or by 'level' and 'trigger'.");
                        req(f.Requires);
                        break;
                    case "free":
                        if (f.Objects.Count + f.Triggers.Count + f.Flags.Count == 0) list.Add(f.Id + ": 'free' names nothing (objects, triggers or flags).");
                        if ((f.Objects.Count + f.Triggers.Count > 0) && f.Level <= 0) list.Add(f.Id + ": objects and triggers need 'level'.");
                        foreach (var t in f.Flags) req(t);
                        break;
                    default:
                        list.Add(f.Id + ": unknown kind '" + f.Kind + "'.");
                        break;
                }
            }
            return list;
        }
    }
}
