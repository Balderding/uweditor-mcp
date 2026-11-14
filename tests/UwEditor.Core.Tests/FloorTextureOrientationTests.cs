using System;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Floor textures lie with their top row along the tile's south edge, as the game draws them (LevelGeometry.FloorTextureRow).</summary>
    public class FloorTextureOrientationTests
    {
        readonly ITestOutputHelper output;
        public FloorTextureOrientationTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void A_floors_top_row_is_south_and_a_ceilings_north()
        {
            Assert.Equal(0f, LevelGeometry.FloorTextureRow(0f, false));
            Assert.Equal(1f, LevelGeometry.FloorTextureRow(1f, false));
            Assert.Equal(1f, LevelGeometry.FloorTextureRow(0f, true));
            Assert.Equal(0f, LevelGeometry.FloorTextureRow(1f, true));
        }

        [Fact]
        public void The_mesh_gives_floors_and_ceilings_those_rows()
        {
            string g = GameData.Game("UW2") ?? GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var level = s.LoadLevel(1);
            var mesh = LevelGeometry.Build(level, true);
            int floors = 0, ceilings = 0;
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                byte kind = mesh.TriangleKind[t];
                if (kind == 1) continue;
                int tile = mesh.TriangleTile[t], ty = tile / 64;
                for (int k = 0; k < 3; k++)
                {
                    var v = mesh.Vertices[3 * t + k];
                    float northing = v.Z - ty;                                  // 0 the south edge, 1 the north
                    // the row within the tile: v repeats every tile, so compare modulo 1 at the two edges
                    float frac = v.V - (float)Math.Floor(v.V);
                    float want = LevelGeometry.FloorTextureRow(northing, kind == 2);
                    Assert.True(Math.Abs(frac - (want - (float)Math.Floor(want))) < 1e-4 || Math.Abs(Math.Abs(frac - want) - 1f) < 1e-4, "tile " + tile + " kind " + kind);
                    Assert.Equal(v.X, v.U, 4);                                  // the left column to the west
                }
                // v grows northwards on a floor and southwards on a ceiling
                var a = Enumerable.Range(0, 3).Select(k => mesh.Vertices[3 * t + k]).OrderBy(v => v.Z).ToArray();
                if (a[2].Z > a[0].Z) Assert.Equal(kind == 0, a[2].V > a[0].V);
                if (kind == 0) floors++; else ceilings++;
            }
            Assert.True(floors > 100);
            Assert.True(ceilings > 100);
        }

        /// <summary>How unlike two rows of two textures are (the sum of the colour differences).</summary>
        static long Seam(RgbaImage north, int northRow, RgbaImage south, int southRow, int edge)
        {
            long d = 0;
            for (int i = 0; i < edge * 4; i++) d += Math.Abs(north.Pixels[northRow * edge * 4 + i] - south.Pixels[southRow * edge * 4 + i]);
            return d;
        }

        [Fact]
        public void The_djinns_pentagram_joins_up_across_its_tiles()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var level = s.LoadLevel(69);
            var gr = s.Graphics;
            const int edge = 64;
            Func<int, int, RgbaImage> tex = (x, y) => gr.TextureRgba(gr.FloorTexture(level.FloorTextureNumber(level.Tiles[x, y].FloorIndex)), gr.FloorEdge, edge);
            // four different floor textures: the picture's quarters
            var numbers = new[] { (21, 52), (22, 52), (21, 53), (22, 53) }.Select(t => level.FloorTextureNumber(level.Tiles[t.Item1, t.Item2].FloorIndex)).ToArray();
            output.WriteLine("floor textures SW, SE, NW, NE: " + string.Join(", ", numbers));
            Assert.Equal(4, numbers.Distinct().Count());
            foreach (int x in new[] { 21, 22 })
            {
                var south = tex(x, 52); var north = tex(x, 53);
                // the south tiles hold the picture's upper half and the north tiles its lower half (textures 236-239 are
                // its quarters, upper left first), so the picture only joins up with each texture's top row to the south,
                // as the game lays a floor: the north tile's top row then meets the south tile's bottom row. Those two rows
                // are the picture's middle and nearly alike; any other pair of rows of the two textures is not.
                long game = Seam(north, 0, south, edge - 1, edge);
                long wrong1 = Seam(north, 0, south, 0, edge), wrong2 = Seam(north, edge - 1, south, edge - 1, edge);
                output.WriteLine("x " + x + ": the picture's middle rows differ by " + game + "; other pairings by " + wrong1 + " and " + wrong2);
                Assert.True(game < wrong1 && game < wrong2, "x " + x + ": " + game + " against " + wrong1 + " and " + wrong2);
            }
            Assert.Equal(new[] { 236, 237, 238, 239 }, numbers);
            // and the map picture draws it so: the picture's row just north of the seam is the north tile's top row
            const int cell = 16;
            var map = MapPicture.Render(level, cell);
            var n = gr.TextureRgba(gr.FloorTexture(numbers[2]), gr.FloorEdge, cell);
            int X = 21 * cell, Y = (63 - 53) * cell + cell - 1;                 // the north-west tile's lowest picture row (its south edge)
            double shade = 0.55 + 0.45 * level.Tiles[21, 53].Height / 15.0;
            for (int px = 0; px < cell; px++)
                for (int c = 0; c < 3; c++)
                    Assert.Equal((byte)(n.Pixels[px * 4 + c] * shade), map.Pixels[(Y * map.Width + X + px) * 4 + c]);
        }
    }
}
