using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The text of the Go to box.</summary>
    public class GoToSpotTests
    {
        static string Parse(string text, out int l, out int x, out int y) { return GoToSpot.Parse(text, 5, n => n >= 1 && n <= 9, out l, out x, out y); }

        [Theory]
        [InlineData("3,10,20", 3, 10, 20)]
        [InlineData("10,20", 5, 10, 20)]
        [InlineData(" 7 , 0 , 63 ", 7, 0, 63)]
        [InlineData("12 34", 5, 12, 34)]
        [InlineData("L2 (33, 4)", 2, 33, 4)]
        [InlineData("9;1;2", 9, 1, 2)]
        public void Level_x_y_or_x_y_reads(string text, int level, int x, int y)
        {
            int l, tx, ty;
            Assert.Null(Parse(text, out l, out tx, out ty));
            Assert.Equal(level, l); Assert.Equal(x, tx); Assert.Equal(y, ty);
        }

        [Theory]
        [InlineData("")]
        [InlineData("7")]
        [InlineData("1,2,3,4")]
        [InlineData("a,b")]
        [InlineData("10,64")]
        [InlineData("64,1")]
        [InlineData("-1,5")]
        [InlineData("12,3,4")]
        [InlineData("0,3,4")]
        public void Anything_else_says_what_is_wrong(string text)
        {
            int l, tx, ty;
            Assert.False(string.IsNullOrEmpty(Parse(text, out l, out tx, out ty)));
        }

        [Fact]
        public void Two_numbers_need_a_level_shown()
        {
            int l, x, y;
            Assert.NotNull(GoToSpot.Parse("3,4", 0, n => true, out l, out x, out y));
            Assert.Null(GoToSpot.Parse("1,3,4", 0, n => true, out l, out x, out y));
        }
    }
}
