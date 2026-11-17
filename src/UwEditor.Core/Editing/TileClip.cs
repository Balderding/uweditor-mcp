using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>One object of a tile clip: its record, what it holds, and what its link and target square name inside the clip.</summary>
    public sealed class TileClipObject
    {
        public byte[] Record;
        public bool Mobile;
        public readonly List<TileClipObject> Children = new List<TileClipObject>();
        /// <summary>The object this one's link names when that is another object of the clip and not its contents (a trigger's trap lying elsewhere), or null.</summary>
        public TileClipObject LinkTo;
        /// <summary>The link named an object outside the clip (its index on the level copied from).</summary>
        public int LinkOutside;
        /// <summary>The square its quality and owner name (a trigger, teleport or delete trap), relative to the clip's corner, when that square is in the clip.</summary>
        public int TargetDx = -1, TargetDy = -1;
        public int Item { get { return (Record[0] | Record[1] << 8) & 0x1FF; } }
    }

    /// <summary>One tile of a tile clip (null in the grid where the tile was not copied).</summary>
    public sealed class TileClipCell
    {
        /// <summary>The 4 bytes of the tile, its object list cleared.</summary>
        public byte[] Record;
        /// <summary>The numbers of its floor and wall textures on the level it came from.</summary>
        public int FloorTexture, WallTexture;
        public readonly List<TileClipObject> Objects = new List<TileClipObject>();
    }

    /// <summary>What a paste of tiles did.</summary>
    public sealed class TilePasteResult
    {
        public int Tiles, Objects, Removed, OffMap, TexturesKept, ObjectsLeftOut;
        /// <summary>The new objects lying on the tiles.</summary>
        public readonly List<int> Made = new List<int>();

        public string Describe()
        {
            var s = string.Format("{0} tile{1}", Tiles, Tiles == 1 ? "" : "s");
            if (Objects > 0) s += string.Format(" with {0} object{1}", Objects, Objects == 1 ? "" : "s");
            var notes = new List<string>();
            if (Removed > 0) notes.Add(Removed + " object" + (Removed == 1 ? "" : "s") + " that lay there removed");
            if (OffMap > 0) notes.Add(OffMap + " tile" + (OffMap == 1 ? "" : "s") + " off the map left out");
            if (TexturesKept > 0) notes.Add(TexturesKept + " texture" + (TexturesKept == 1 ? "" : "s") + " not on this level's list kept by slot number");
            if (ObjectsLeftOut > 0) notes.Add(ObjectsLeftOut + " object" + (ObjectsLeftOut == 1 ? "" : "s") + " of the other game left out");
            return s + (notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "");
        }
    }

    /// <summary>
    /// A rectangle of a level copied with everything on it: each tile's type, height, textures and
    /// flags, the objects lying there with what they hold, the links between them and the squares
    /// their triggers name. It can be turned a quarter clockwise and mirrored before it is pasted
    /// on any level. Cells[x, y] has x east and y north, as the map; a null cell was not copied.
    /// </summary>
    public sealed class TileClip
    {
        public GameKind Kind;
        public int FromLevel, FromX, FromY;
        public int Width, Height;
        public TileClipCell[,] Cells;

        public int TileCount { get { int n = 0; foreach (var c in Cells) if (c != null) n++; return n; } }

        public IEnumerable<TileClipObject> AllObjects()
        {
            var todo = new Stack<TileClipObject>();
            foreach (var c in Cells) if (c != null) foreach (var o in c.Objects) todo.Push(o);
            while (todo.Count > 0)
            {
                var o = todo.Pop();
                yield return o;
                foreach (var k in o.Children) todo.Push(k);
            }
        }

        public int Count(bool mobile) { return AllObjects().Count(o => o.Mobile == mobile); }

        public string Describe()
        {
            int n = TileCount, o = AllObjects().Count();
            return string.Format("{0} × {1} tiles ({2} copied{3})", Width, Height, n, o > 0 ? ", " + o + " object" + (o == 1 ? "" : "s") : "");
        }

        static readonly int[] TurnType = { 0, 1, 3, 5, 2, 4, 8, 9, 7, 6 };       // clockwise: SE→SW→NW→NE→SE, N→E→S→W→N
        static readonly int[] MirrorType = { 0, 1, 3, 2, 5, 4, 6, 7, 9, 8 };     // east and west change places

        /// <summary>Turns the clip a quarter clockwise (seen from above, north up): north becomes east.</summary>
        public void RotateClockwise()
        {
            var n = new TileClipCell[Height, Width];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var c = Cells[x, y];
                    n[y, Width - 1 - x] = c;
                    if (c == null) continue;
                    Retype(c, TurnType);
                    foreach (var o in c.Objects) Reface(o, fx => fx.Item2, fx => 7 - fx.Item1, h => (h + 2) & 7);
                }
            int w = Width;
            foreach (var o in AllObjects().Where(o => o.TargetDx >= 0)) { int tx = o.TargetDx; o.TargetDx = o.TargetDy; o.TargetDy = w - 1 - tx; }
            Cells = n;
            Width = Height; Height = w;
        }

        /// <summary>Mirrors the clip: east and west change places.</summary>
        public void Mirror()
        {
            var n = new TileClipCell[Width, Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var c = Cells[x, y];
                    n[Width - 1 - x, y] = c;
                    if (c == null) continue;
                    Retype(c, MirrorType);
                    foreach (var o in c.Objects) Reface(o, fx => 7 - fx.Item1, fx => fx.Item2, h => (8 - h) & 7);
                }
            foreach (var o in AllObjects().Where(o => o.TargetDx >= 0)) o.TargetDx = Width - 1 - o.TargetDx;
            Cells = n;
        }

        static void Retype(TileClipCell c, int[] map)
        {
            var f = Uw1Schemas.Tile["Type"];
            int t = f.Get(c.Record);
            if (t >= 0 && t < map.Length) f.Set(c.Record, map[t]);
        }

        /// <summary>An object lying on a turned tile: its fine place and its heading turn with it. A trap's or trigger's are settings and stay.</summary>
        static void Reface(TileClipObject o, Func<Tuple<int, int>, int> newX, Func<Tuple<int, int>, int> newY, Func<int, int> newHeading)
        {
            if (o.Item >= 0x180 && o.Item < 0x1C0) return;
            var S = Uw1Schemas.StaticObject;
            var at = Tuple.Create(S["Fine X"].Get(o.Record), S["Fine Y"].Get(o.Record));
            S["Fine X"].Set(o.Record, newX(at));
            S["Fine Y"].Set(o.Record, newY(at));
            S["Heading"].Set(o.Record, newHeading(S["Heading"].Get(o.Record)));
        }
    }

    public sealed partial class LevelEditor
    {
        /// <summary>
        /// Copies tiles with all that is on them (TileClip): the clip is the rectangle around the
        /// tiles given, holding those tiles only.
        /// </summary>
        public TileClip CopyTiles(IEnumerable<(int X, int Y)> tiles)
        {
            var set = new HashSet<int>(tiles.Where(t => t.X >= 0 && t.Y >= 0 && t.X < 64 && t.Y < 64).Select(t => t.X + 64 * t.Y));
            if (set.Count == 0) throw new InvalidOperationException("Select tiles to copy first.");
            var m = Model;
            int x0 = set.Min(t => t % 64), x1 = set.Max(t => t % 64), y0 = set.Min(t => t / 64), y1 = set.Max(t => t / 64);
            var clip = new TileClip { Kind = Session.Kind, FromLevel = Level, FromX = x0, FromY = y0, Width = x1 - x0 + 1, Height = y1 - y0 + 1 };
            clip.Cells = new TileClipCell[clip.Width, clip.Height];
            var nodes = new Dictionary<int, TileClipObject>();
            foreach (int t in set.OrderBy(t => t))
            {
                int x = t % 64, y = t / 64;
                var rec = new byte[4];
                Array.Copy(data, TileAt(x, y), rec, 0, 4);
                Uw1Schemas.Tile["First object"].Set(rec, 0);
                var cell = new TileClipCell { Record = rec, FloorTexture = m.FloorTextureNumber(m.Tiles[x, y].FloorIndex), WallTexture = m.WallTextureNumber(m.Tiles[x, y].WallIndex) };
                foreach (var o in m.TileObjects(x, y)) if (!o.Free) cell.Objects.Add(ClipObject(m, o, nodes, 0));
                clip.Cells[x - x0, y - y0] = cell;
            }
            // links that name an object which is not the object's own contents, and target squares
            foreach (var kv in nodes)
            {
                var o = m.Objects[kv.Key];
                bool list = !o.IsQuantity, follows = o.IsTrapOrTrigger || list;
                if (follows && o.Link > 0 && o.Link < m.Objects.Length && !(list && m.Objects[o.Link].Parent == o.Index))
                {
                    TileClipObject to;
                    if (nodes.TryGetValue(o.Link, out to)) kv.Value.LinkTo = to; else kv.Value.LinkOutside = o.Link;
                }
                if (Triggers.HasTargetSquare(o.Item) && !(Triggers.IsTeleport(o.Item) && o.Z != 0 && o.Z != Level))
                {
                    int tx = o.Quality, ty = o.Owner;
                    if (set.Contains(tx + 64 * ty) && tx < 64 && ty < 64) { kv.Value.TargetDx = tx - x0; kv.Value.TargetDy = ty - y0; }
                }
            }
            return clip;
        }

        TileClipObject ClipObject(LevelModel m, ObjectInfo o, Dictionary<int, TileClipObject> nodes, int depth)
        {
            var n = new TileClipObject { Mobile = o.IsMobile, Record = (byte[])o.Record.Clone() };
            nodes[o.Index] = n;
            if (depth < 64)
                foreach (int c in o.Children)
                    if (c > 0 && c < m.Objects.Length && !m.Objects[c].Free && !nodes.ContainsKey(c)) n.Children.Add(ClipObject(m, m.Objects[c], nodes, depth + 1));
            return n;
        }

        /// <summary>
        /// Pastes a tile clip with its south-west corner on (x, y), as one undoable change. Each
        /// tile copied replaces the tile there: what lay on it is removed, the tile takes the
        /// copied type, height, flags and textures (by texture number where this level's list has
        /// it, else by slot), and the copied objects are put on it at their heights, their links
        /// and target squares inside the clip pointing at the new copies. Tiles off the map are
        /// left out; objects of the other game are left out.
        /// </summary>
        public TilePasteResult PasteTiles(TileClip clip, int x, int y)
        {
            if (clip == null || clip.TileCount == 0) throw new InvalidOperationException("Nothing to paste: copy tiles first.");
            ListsMustPass();
            var res = new TilePasteResult();
            bool sameGame = clip.Kind == Session.Kind;
            Change("Paste " + clip.Describe(), () =>
            {
                var m = Model;
                int[] floorMap = m.FloorMap, wallMap = m.WallMap;
                var T = Uw1Schemas.Tile;
                // what lies on the tiles goes first: its records are then free for the copies
                var cells = new List<(TileClipCell C, int X, int Y)>();
                for (int cy = 0; cy < clip.Height; cy++)
                    for (int cx = 0; cx < clip.Width; cx++)
                    {
                        var c = clip.Cells[cx, cy];
                        if (c == null) continue;
                        int tx = x + cx, ty = y + cy;
                        if (tx < 0 || ty < 0 || tx > 63 || ty > 63) { res.OffMap++; continue; }
                        cells.Add((c, tx, ty));
                    }
                if (cells.Count == 0) throw new InvalidOperationException("The whole clip would lie off the map there.");
                // by the model, which holds each object once (a raw walk of the lists could meet one twice,
                // through its tile and through a trigger's link): everything on those tiles, at any depth
                var on = new HashSet<int>(cells.Select(c => c.X + 64 * c.Y));
                foreach (var o in m.Objects)
                    if (!o.Free && o.Index > 1 && o.TileX >= 0 && on.Contains(o.TileX + 64 * o.TileY)) { FreeIndex(o.Index); res.Removed++; }
                foreach (var c in cells) SetHead(c.X, c.Y, 0);
                var made = new Dictionary<TileClipObject, int>();
                foreach (var c in cells)
                {
                    var rec = (byte[])c.C.Record.Clone();
                    if (sameGame)
                    {
                        // the slot it had when this level's list holds the same texture there (a list may name one texture twice), else the first slot that does
                        int f0 = T["Floor texture"].Get(rec), w0 = T["Wall texture"].Get(rec);
                        int f = f0 < floorMap.Length && floorMap[f0] == c.C.FloorTexture ? f0 : Array.IndexOf(floorMap, c.C.FloorTexture);
                        int w = w0 < wallMap.Length && wallMap[w0] == c.C.WallTexture ? w0 : Array.IndexOf(wallMap, c.C.WallTexture);
                        if (f >= 0) T["Floor texture"].Set(rec, f); else res.TexturesKept++;
                        if (w >= 0) T["Wall texture"].Set(rec, w); else res.TexturesKept++;
                    }
                    if (T["Floor texture"].Get(rec) >= floorMap.Length) T["Floor texture"].Set(rec, 0);
                    if (T["Wall texture"].Get(rec) >= wallMap.Length) T["Wall texture"].Set(rec, 0);
                    Array.Copy(rec, 0, data, TileAt(c.X, c.Y), 4);
                    res.Tiles++;
                    if (!sameGame) { res.ObjectsLeftOut += c.C.Objects.Sum(o => 1 + Descendants(o)); continue; }
                    // in their order: each put at the head, last first
                    for (int k = c.C.Objects.Count - 1; k >= 0; k--) res.Made.Add(PasteTileObject(c.C.Objects[k], c.X, c.Y, -1, made));
                }
                res.Made.Reverse();
                res.Objects = made.Count;
                // links and target squares between the copies
                var S = Uw1Schemas.StaticObject;
                foreach (var kv in made)
                {
                    int at = LevelBlock.ObjectOffset(kv.Value);
                    var o = kv.Key;
                    int to;
                    if (o.LinkTo != null) SetLink(kv.Value, made.TryGetValue(o.LinkTo, out to) ? to : 0);
                    else if (o.LinkOutside > 0) SetLink(kv.Value, clip.FromLevel == Level && sameGame ? o.LinkOutside : 0);
                    if (o.TargetDx >= 0)
                    {
                        int tx = x + o.TargetDx, ty = y + o.TargetDy;
                        if (tx >= 0 && ty >= 0 && tx < 64 && ty < 64)
                        {
                            S["Quality"].Set(data, tx, at);
                            S["Owner"].Set(data, ty, at);
                            // a teleport that named its own level by number goes to the level it is pasted on
                            if (Triggers.IsTeleport(o.Item) && clip.FromLevel != Level) S["Z"].Set(data, 0, at);
                        }
                    }
                }
            });
            return res;
        }

        static int Descendants(TileClipObject o) { return o.Children.Sum(c => 1 + Descendants(c)); }

        int PasteTileObject(TileClipObject n, int x, int y, int container, Dictionary<TileClipObject, int> made)
        {
            int i = Alloc(n.Mobile);
            made[n] = i;
            int at = LevelBlock.ObjectOffset(i);
            Array.Copy(n.Record, 0, data, at, Math.Min(n.Record.Length, n.Mobile ? LevelBlock.MobileSize : LevelBlock.StaticSize));
            SetNext(i, 0);
            // the link of a list is filled by the contents below; one that named another object is set after all are made
            bool list = !IsQuant(i);
            if (list || n.LinkTo != null || n.LinkOutside > 0) SetLink(i, 0);
            if (n.Mobile) { Uw1Schemas.MobileObject["Home X"].Set(data, x, at); Uw1Schemas.MobileObject["Home Y"].Set(data, y, at); }
            if (n.Mobile && container >= 0) RemoveFromActive(i);
            if (list) for (int k = n.Children.Count - 1; k >= 0; k--) PasteTileObject(n.Children[k], x, y, i, made);
            if (container >= 0) { SetNext(i, LinkOf(container)); SetLink(container, i); }
            else { SetNext(i, HeadOf(x, y)); SetHead(x, y, i); }
            return i;
        }
    }
}
