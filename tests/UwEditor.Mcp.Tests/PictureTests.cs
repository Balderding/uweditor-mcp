using System;
using System.Buffers.Binary;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>picture: the 2D map as a PNG, whole or cropped.</summary>
    public class PictureTests
    {
        static (int w, int h) Size(byte[] png)
        {
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.AsSpan(0, 8).ToArray());
            return (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        }

        [Fact]
        public void Whole_level_and_a_crop()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            Assert.Equal((512, 512), Size(ed.PicturePng(1, null, null, null, null, 8)));
            Assert.Equal((80, 80), Size(ed.PicturePng(1, 20, 30, 29, 39, 8)));
            Assert.Equal((80, 80), Size(ed.PicturePng(1, 29, 39, 20, 30, 8)));
        }

        [Fact]
        public void Bad_crop_refused()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var ed = new McpEditor(g); ed.Open(g);
            Assert.ThrowsAny<ArgumentException>(() => ed.PicturePng(1, 20, 30, 64, 39, 8));
            Assert.ThrowsAny<ArgumentException>(() => ed.PicturePng(1, 20, null, 29, 39, 8));
            Assert.ThrowsAny<ArgumentException>(() => ed.PicturePng(1, null, null, null, null, 100));
        }
    }
}
