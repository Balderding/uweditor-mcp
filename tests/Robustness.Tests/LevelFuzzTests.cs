using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Damaged object lists (ROADMAP.md §2.1, test 3): free-stack tops and entries that lie, chains
    /// that loop, an object in two lists, flipped bytes. Reading the level must not fail; adding,
    /// deleting, pasting and moving objects must refuse while the lists fail the game's check, and
    /// on a level that passes it they must leave it passing. Saving never writes a level the
    /// editor broke.
    /// </summary>
    public class LevelFuzzTests
    {
        readonly ITestOutputHelper output;
        public LevelFuzzTests(ITestOutputHelper output) { this.output = output; }

        static void Put10(byte[] d, int at, int v) { int w = d[at] | d[at + 1] << 8; w = (w & 0x3F) | (v & 0x3FF) << 6; d[at] = (byte)w; d[at + 1] = (byte)(w >> 8); }
        static void Put16(byte[] d, int at, int v) { d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); }

        /// <summary>A level block with its object lists damaged, reproducible from the seed.</summary>
        static byte[] Damage_(byte[] block, int seed, out string what)
        {
            var r = new Random(seed);
            var d = (byte[])block.Clone();
            var b = new LevelBlock(d, GameKind.UW1);
            var parts = new List<string>();
            int edits = 1 + r.Next(3);
            Func<int> anyObject = () => r.Next(4) == 0 ? r.Next(2) : r.Next(LevelBlock.Objects);
            for (int e = 0; e < edits; e++)
            {
                switch (r.Next(8))
                {
                    case 0:
                        {
                            int[] lies = { -2, -1, 0, 253, 254, 255, 767, 768, 800, 0x7FFF, r.Next(1024) };
                            bool mobile = r.Next(2) == 0;
                            int v = lies[r.Next(lies.Length)];
                            Put16(d, mobile ? LevelBlock.MobTopAt : LevelBlock.StaticTopAt, v);
                            parts.Add((mobile ? "mobile" : "static") + " top = " + v);
                            break;
                        }
                    case 1:
                        {
                            bool mobile = r.Next(2) == 0;
                            int k = r.Next(mobile ? LevelBlock.MobileFreeSlots : LevelBlock.StaticFreeSlots);
                            int v = r.Next(5) == 0 ? 0xFFFF : anyObject();
                            Put16(d, (mobile ? LevelBlock.MobFreeAt : LevelBlock.StaticFreeAt) + 2 * k, v);
                            parts.Add((mobile ? "mobile" : "static") + " free " + k + " = " + v);
                            break;
                        }
                    case 2:
                        {
                            int x = r.Next(64), y = r.Next(64), v = anyObject();
                            Put10(d, LevelBlock.TilesAt + (y * 64 + x) * 4 + 2, v);
                            parts.Add("tile (" + x + ", " + y + ") first = " + v);
                            break;
                        }
                    case 3:
                        {
                            int i = 2 + r.Next(LevelBlock.Objects - 2), v = r.Next(3) == 0 ? i : anyObject();
                            Put10(d, LevelBlock.ObjectOffset(i) + 4, v);
                            parts.Add("#" + i + " next = " + v);
                            break;
                        }
                    case 4:
                        {
                            int i = 2 + r.Next(LevelBlock.Objects - 2), v = r.Next(3) == 0 ? i : anyObject();
                            int at = LevelBlock.ObjectOffset(i);
                            d[at + 1] &= 0x7F;      // a list, not a quantity
                            Put10(d, at + 6, v);
                            parts.Add("#" + i + " link = " + v + " (a list)");
                            break;
                        }
                    case 5:
                        {
                            // an object already on one tile's list put at the head of another's too
                            int x = r.Next(64), y = r.Next(64), first = -1;
                            for (int n = 0; n < 4096 && first <= 0; n++) { int t = (y * 64 + x + n) % 4096; first = b.TileFirstObject(t % 64, t / 64); }
                            int x2 = r.Next(64), y2 = r.Next(64);
                            Put10(d, LevelBlock.TilesAt + (y2 * 64 + x2) * 4 + 2, Math.Max(0, first));
                            parts.Add("#" + first + " also first on (" + x2 + ", " + y2 + ")");
                            break;
                        }
                    case 6:
                        {
                            int n = 1 + r.Next(8);
                            for (int k = 0; k < n; k++) d[LevelBlock.MobilesAt + r.Next(LevelBlock.MagicAt - LevelBlock.MobilesAt)] ^= (byte)(1 + r.Next(255));
                            parts.Add(n + " object bytes flipped");
                            break;
                        }
                    default:
                        {
                            int n = 1 + r.Next(8);
                            for (int k = 0; k < n; k++) d[r.Next(LevelBlock.MagicAt)] ^= (byte)(1 + r.Next(255));
                            parts.Add(n + " bytes flipped");
                            break;
                        }
                }
            }
            what = "seed " + seed + ": " + string.Join(", ", parts);
            return d;
        }

        static void Refusable(Action a)
        {
            try { a(); }
            catch (InvalidOperationException) { }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Damaged_object_lists_are_refused_not_made_worse(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            int[] levels = game == "UW" ? new[] { 1, 2, 3, 5, 7, 9 } : new[] { 1, 2, 9, 17, 25 };
            var originals = levels.Select(l => s.LoadLevel(l)).Where(m => m.Present).ToList();
            int damaged = 0, n = Damage.Seeds;
            // cases that once broke the editor, run every time (ROADMAP.md §2.7): UW k 1841, a loop that
            // made Copy's walk grow by the power of its depth
            var known = game == "UW" ? new[] { 1841 } : new int[0];
            foreach (int k in Enumerable.Range(0, n).Concat(known))
            {
                var m0 = originals[k % originals.Count];
                string what;
                var bad = Damage_(m0.Block.Data, 1000 + k, out what);
                var spec = ObjectSpec.Default(0xA0, s.Kind);
                Damage.Run(game + " level " + m0.Level + " " + what, () =>
                {
                    var m = LevelModel.FromBytes(s, m0.Level, bad, m0.TextureBytes);
                    var e = new LevelEditor(s, m0.Level, (byte[])bad.Clone(), m0.TextureBytes == null ? null : (byte[])m0.TextureBytes.Clone());
                    bool before;
                    e.Check(out before);
                    if (!before) damaged++;
                    var copy = (byte[])e.Data.Clone();
                    var open = Enumerable.Range(0, 4096).Select(t => (x: t % 64, y: t / 64)).Where(t => !m.Tiles[t.x, t.y].IsSolid).ToList();
                    var onTile = m.Objects.Where(o => !o.Free && o.OnTile && o.Index > 1).Select(o => o.Index).ToList();
                    var inside = m.Objects.Where(o => !o.Free && !o.OnTile && o.Parent > 0).Select(o => o.Index).ToList();
                    var rnd = new Random(k);
                    Action<string> after = op =>
                    {
                        bool ok;
                        string report = e.Check(out ok);
                        if (before) Assert.True(ok, op + " broke a level that passed the check: " + report);
                        else Assert.True(e.Data.AsSpan().SequenceEqual(copy), op + " changed a level whose lists fail the check instead of refusing");
                    };
                    if (open.Count > 0)
                    {
                        var t = open[rnd.Next(open.Count)];
                        Refusable(() => e.AddObject(spec, t.x, t.y)); after("add");
                        if (onTile.Count > 0)
                        {
                            int i = onTile[rnd.Next(onTile.Count)];
                            Refusable(() => e.Paste(e.Copy(new[] { i }), t.x, t.y)); after("paste");
                            Refusable(() => e.MoveObject(i, t.x, t.y)); after("move");
                            Refusable(() => e.DeleteObject(i)); after("delete");
                        }
                        if (inside.Count > 0)
                        {
                            int i = inside[rnd.Next(inside.Count)];
                            Refusable(() => e.MoveObjectAnywhere(i, t.x, t.y)); after("take out");
                        }
                    }
                    // the model of what is left reads, whatever happened
                    var again = e.Model;
                }, 20);
            }
            output.WriteLine("{0}: {1} damaged levels, {2} failed the check", game, n, damaged);
            Assert.True(damaged > n / 4, "too few damaged levels failed the check: the damage is too mild to test the guards");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Saving_refuses_a_level_the_editor_broke(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var copy = new Damage.GameCopy(g))
            {
                var s = GameSession.Open(copy.Folder);
                var edits = new LevelEdits(s);
                var e = edits.For(1);
                Assert.True(e.ReadOk);
                int x = -1, y = -1;
                for (int t = 0; t < 4096 && x < 0; t++) if (!e.Model.Tiles[t % 64, t / 64].IsSolid) { x = t % 64; y = t / 64; }
                int coins = e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);
                // as a bug would: the new object also put at the head of another tile's list
                int x2 = x == 63 ? 0 : x + 1;
                Put10(e.Data, LevelBlock.TilesAt + (y * 64 + x2) * 4 + 2, coins);
                var before = File.ReadAllBytes(s.LevArkPath);
                var ex = Assert.Throws<InvalidOperationException>(() => edits.Save());
                Assert.Contains("Level 1 was not saved", ex.Message);
                Assert.Equal(before, File.ReadAllBytes(s.LevArkPath));
                // and the editor itself refuses to go on adding there
                Assert.Throws<InvalidOperationException>(() => e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y));
            }
        }
    }
}
