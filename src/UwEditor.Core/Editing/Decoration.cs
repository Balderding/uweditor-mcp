using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;
using UwEditor.Core.Randomizer;

namespace UwEditor.Core.Editing
{
    /// <summary>What a decoration pass scatters, and how thickly.</summary>
    public sealed class DecorationOptions
    {
        /// <summary>The kinds to scatter (Decoration.Kinds); those the game has no item for are left out.</summary>
        public HashSet<string> Kinds = new HashSet<string>(Decoration.Kinds);
        /// <summary>Objects for every hundred tiles of the region (1-400).</summary>
        public int PerHundred = 15;
        /// <summary>The roll: the same seed, region and options give the same scatter.</summary>
        public int Seed = 1;
        /// <summary>Leave water and lava tiles bare (things there would lie under the surface).</summary>
        public bool AvoidLiquid = true;
        /// <summary>Leave tiles bare that already hold something.</summary>
        public bool AvoidOccupied;

        public DecorationOptions Clone() { var c = (DecorationOptions)MemberwiseClone(); c.Kinds = new HashSet<string>(Kinds); return c; }
    }

    /// <summary>What a decoration pass did.</summary>
    public sealed class DecorationResult
    {
        public readonly List<int> Made = new List<int>();
        public int Tiles, Wanted;
        /// <summary>How many of them hang from the ceiling.</summary>
        public int Hanging;
        public readonly Dictionary<string, int> ByKind = new Dictionary<string, int>();
        /// <summary>Why fewer were placed than wanted (the level's objects ran out), or null.</summary>
        public string Short;

        public string Describe()
        {
            if (Made.Count == 0) return Short ?? "Nothing placed: the region has no tile to put things on.";
            return string.Format("{0} object{1} on {2} tile{3} ({4}){5}", Made.Count, Made.Count == 1 ? "" : "s", Tiles, Tiles == 1 ? "" : "s",
                string.Join(", ", ByKind.OrderByDescending(k => k.Value).Select(k => k.Value + " " + k.Key)) + (Hanging > 0 ? "; " + Hanging + " of them hanging from the ceiling" : ""), Short != null ? "; " + Short : "");
        }
    }

    /// <summary>
    /// Scatters small scenery over a region to dress it: debris, plants, moss, bones and stalactites, chosen
    /// by kind, at a density, from a seed. The items of each kind are found by their names in
    /// the game's own item list (scenery only), so each game uses what it has; a kind with no
    /// item is not offered. Every object lands on an open tile of the region at a random fine
    /// place and heading: on the floor, or hanging from the ceiling where the game's own levels
    /// hang that item (Hanging). It is one undo step, so a roll can be undone and made again
    /// thicker, thinner or different.
    /// </summary>
    public static class Decoration
    {
        public static readonly string[] Kinds = { "debris", "plants", "moss", "bones", "stalactites" };

        /// <summary>
        /// The words in an item's name that put it in a kind, the first kind that fits winning.
        /// Neither game has an item called moss: the moss kind is the low ground cover they do
        /// have (grass, and UW2's vines), apart from the plants that stand up.
        /// </summary>
        static readonly KeyValuePair<string, string[]>[] Words =
        {
            new KeyValuePair<string, string[]>("moss", new[] { "moss", "lichen", "grass", "vine" }),
            new KeyValuePair<string, string[]>("plants", new[] { "plant", "bush", "fern", "weed", "flower" }),
            new KeyValuePair<string, string[]>("bones", new[] { "bone", "skull" }),
            new KeyValuePair<string, string[]>("debris", new[] { "debris", "rubble", "broken", "piece of wood", "wood chips", "stick" }),
            new KeyValuePair<string, string[]>("stalactites", new[] { "stalactite", "stalagmite" }),
        };

        /// <summary>At least this far above its tile's floor (in z units, 8 a floor step), an object hangs rather than lies.</summary>
        public const int HangsAbove = 24;

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameSession, Dictionary<int, int>> hangs = new System.Runtime.CompilerServices.ConditionalWeakTable<GameSession, Dictionary<int, int>>();

        /// <summary>
        /// The decoration items that hang from the ceiling, each with the height (z, 0-127) it
        /// hangs at: what the game's own levels do with them. An item whose copies in the levels
        /// as shipped mostly sit well above their tile's floor (moss and some plants, stalactites)
        /// hangs, at the height most of them have; one that mostly lies on the floor lies. An
        /// item the levels never use lies, except stalactites and vines (UW1 has vines and places
        /// none), which hang where the other hanging things of the game do.
        /// </summary>
        public static Dictionary<int, int> Hanging(GameSession s)
        {
            return hangs.GetValue(s, _ =>
            {
                var items = new HashSet<int>(Items(s).Values.SelectMany(v => v));
                var above = items.ToDictionary(i => i, i => new List<int>());
                var heights = items.ToDictionary(i => i, i => new List<int>());
                foreach (var slot in s.LevelSlots())
                {
                    if (!slot.Present) continue;
                    LevelModel m;
                    try { m = s.LoadLevel(slot.Level); } catch (Exception) { continue; }
                    foreach (var o in m.Objects)
                    {
                        if (o.Free || !o.OnTile || !items.Contains(o.Item)) continue;
                        int floor = (int)Math.Round(m.FloorAt(o.TileX, o.TileY, (o.FineX + 0.5) / 8, (o.FineY + 0.5) / 8) / LevelGeometry.Step * 8);
                        above[o.Item].Add(o.Z - floor);
                        heights[o.Item].Add(o.Z);
                    }
                }
                var map = new Dictionary<int, int>();
                var unused = new List<int>();
                foreach (int i in items)
                {
                    var a = above[i];
                    if (a.Count == 0)
                    {
                        string name = (s.Names.Get(i) ?? "").ToLowerInvariant();
                        if (name.Contains("stalactite") || name.Contains("vine")) unused.Add(i);
                        continue;
                    }
                    if (a.Count(x => x >= HangsAbove) * 2 <= a.Count) continue;
                    var h = heights[i].Where((z, k) => a[k] >= HangsAbove).OrderBy(z => z).ToList();
                    map[i] = h[h.Count / 2];
                }
                // those the levels never place hang as high as the ones they do (120, just under the ceiling, when there are none)
                int usual = map.Count > 0 ? map.Values.OrderBy(z => z).ElementAt(map.Count / 2) : 120;
                foreach (int i in unused) map[i] = usual;
                return map;
            });
        }

        /// <summary>Names that hold a kind's word but are no decoration (things the game gives a use or a quest).</summary>
        static readonly string[] Not = { "runestone", "rune stone", "moonstone", "rock hammer", "gem", "key", "talisman", "orb", "boulder", "bones of", "sling", "resilient" };

        /// <summary>The scenery items of each kind in this game, by name (an empty list: the game has none).</summary>
        public static Dictionary<string, List<int>> Items(GameSession s)
        {
            var map = Kinds.ToDictionary(k => k, k => new List<int>());
            for (int item = 0; item < 0x180; item++)
            {
                if ((ItemCategories.Of(item, s.Kind) & ItemCategory.Scenery) == 0) continue;
                string name = (s.Names.Get(item) ?? "").ToLowerInvariant();
                if (name.Length == 0 || Not.Any(n => name.Contains(n))) continue;
                foreach (var k in Words)
                    if (k.Value.Any(w => name.Contains(w))) { map[k.Key].Add(item); break; }
            }
            return map;
        }

        /// <summary>The kinds this game has items for, in Kinds' order.</summary>
        public static List<string> KindsOf(GameSession s) { var m = Items(s); return Kinds.Where(k => m[k].Count > 0).ToList(); }

        /// <summary>Scatters the decoration over the tiles, as one undo step. Throws when no kind chosen has an item in this game.</summary>
        public static DecorationResult Apply(LevelEditor e, IEnumerable<(int X, int Y)> tiles, DecorationOptions o)
        {
            var s = e.Session;
            var items = Items(s);
            var kinds = Kinds.Where(k => o.Kinds.Contains(k) && items[k].Count > 0).ToList();
            if (kinds.Count == 0) throw new InvalidOperationException("Choose at least one kind this game has items for: " + string.Join(", ", KindsOf(s)) + ".");
            var m = e.Model;
            var terrain = o.AvoidLiquid ? CreatureTerrain.TryLoad(s) : null;
            var region = tiles.Where(t => t.X >= 0 && t.Y >= 0 && t.X < 64 && t.Y < 64).Distinct().Where(t =>
            {
                if (m.Tiles[t.X, t.Y].IsSolid) return false;
                if (o.AvoidOccupied && m.TileObjects(t.X, t.Y).Count > 0) return false;
                if (terrain != null) { int c = CreatureTerrain.FloorClass(s, terrain, m, t.X, t.Y); if (c == CreatureTerrain.Water || c == CreatureTerrain.Lava) return false; }
                return true;
            }).OrderBy(t => t.Y * 64 + t.X).ToList();
            var res = new DecorationResult();
            if (region.Count == 0) return res;
            int per = Math.Max(1, Math.Min(400, o.PerHundred));
            res.Wanted = Math.Max(1, (int)Math.Round(region.Count * per / 100.0));
            var rng = new Random(o.Seed);
            var used = new HashSet<int>();
            var hanging = Hanging(s);
            e.Batch(string.Format("Decorate {0} tiles ({1})", region.Count, string.Join(", ", kinds)), () =>
            {
                for (int n = 0; n < res.Wanted; n++)
                {
                    var t = region[rng.Next(region.Count)];
                    string kind = kinds[rng.Next(kinds.Count)];
                    int item = items[kind][rng.Next(items[kind].Count)];
                    var spec = ObjectSpec.Default(item, s.Kind);
                    spec.FineX = 1 + rng.Next(6); spec.FineY = 1 + rng.Next(6); spec.Heading = rng.Next(8);
                    // what the game hangs from the ceiling hangs here too, at the height it has in the shipped levels (never below this floor)
                    int hang;
                    if (hanging.TryGetValue(item, out hang))
                    {
                        spec.Z = Math.Max(hang, Math.Min(127, e.FloorZ(t.X, t.Y, spec.FineX, spec.FineY) + HangsAbove));
                        res.Hanging++;
                    }
                    int made;
                    try { made = e.AddObject(spec, t.X, t.Y); }
                    catch (InvalidOperationException ex)
                    {
                        res.Short = string.Format("stopped at {0} of {1}: {2}", res.Made.Count, res.Wanted, ex.Message);
                        break;
                    }
                    res.Made.Add(made);
                    used.Add(t.X + 64 * t.Y);
                    int c; res.ByKind.TryGetValue(kind, out c); res.ByKind[kind] = c + 1;
                }
            });
            res.Tiles = used.Count;
            return res;
        }
    }
}
