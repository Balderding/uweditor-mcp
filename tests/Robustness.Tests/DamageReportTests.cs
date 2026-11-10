using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Damage is reported, not hidden (ROADMAP.md §2.1): a level block that is no level is a
    /// damaged slot (not an empty one), the World check names it, and the work that reads every
    /// level refuses; a string block or PLAYER.DAT that cannot be read is listed when the game opens.
    /// </summary>
    public class DamageReportTests
    {
        [Theory]
        [InlineData("UW", null)]
        [InlineData("UW2", null)]
        [InlineData("UW2", "SAVE1")]
        public void The_shipped_games_have_nothing_damaged(string game, string save)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(save == null ? g : Path.Combine(g, save));
            Assert.Empty(s.DamagedLevels());
            Assert.Empty(s.ReadProblems);
        }

        /// <summary>A copy of the game whose level 1 block is replaced by these bytes.</summary>
        static void PutLevelBlock(Damage.GameCopy copy, GameKind kind, Func<byte[], byte[]> change)
        {
            string path = GameProfile.Find(Path.Combine(copy.Folder, "DATA"), "LEV.ARK");
            var ark = Archive.Load(path, kind);
            int b = GameProfile.For(kind).LevelBlock(1);
            ark.Write(b, change(ark.Read(b)));
            File.WriteAllBytes(path, ark.ToBytes());
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_level_block_that_is_no_level_is_damaged_not_empty(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var kind = game == "UW2" ? GameKind.UW2 : GameKind.UW1;
            using (var copy = new Damage.GameCopy(g))
            {
                // the level's mark wiped
                PutLevelBlock(copy, kind, d => { d[LevelBlock.MagicAt] = 0; d[LevelBlock.MagicAt + 1] = 0; return d; });
                var s = GameSession.Open(copy.Folder);
                var bad = s.DamagedLevels();
                Assert.Single(bad);
                Assert.Equal(1, bad[0].Level);
                Assert.Contains("mark", bad[0].Damage);
                Assert.EndsWith("(damaged)", bad[0].Name);

                // the World check says so first
                var found = WorldCheck.Run(s, s.LoadLevel);
                Assert.Equal("damaged", found[0].Kind);
                Assert.Equal(2, found[0].Severity);
                Assert.Contains("nothing in it was checked", found[0].Text);

                // the randomizer and the item cleaner refuse, naming the level
                var edits = new LevelEdits(s);
                var ex = Assert.Throws<InvalidOperationException>(() => MonsterSwap.Apply(new MonsterSwapResult(), edits, 1));
                Assert.Contains("needs every level", ex.Message);
                Assert.Contains(bad[0].Name, ex.Message);
                Assert.Throws<InvalidOperationException>(() => ItemCleaner.Apply(edits, new CleanPlan()));
            }
            using (var copy = new Damage.GameCopy(g))
            {
                // the block cut short
                PutLevelBlock(copy, kind, d => d.Take(100).ToArray());
                var s = GameSession.Open(copy.Folder);
                Assert.Contains("100 bytes", s.DamagedLevels().Single().Damage);
            }
        }

        [Fact]
        public void A_string_block_that_cannot_be_read_is_listed()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            using (var copy = new Damage.GameCopy(g))
            {
                string path = GameProfile.Find(Path.Combine(copy.Folder, "DATA"), "STRINGS.PAK");
                var d = File.ReadAllBytes(path);
                // the block stored last: its first string made to start past the end of the file
                int nodes = d[0] | d[1] << 8, table = 2 + 4 * nodes, count = d[table] | d[table + 1] << 8;
                int last = -1, lastNumber = -1;
                for (int k = 0; k < count; k++)
                {
                    int at = table + 2 + 6 * k;
                    int off = d[at + 2] | d[at + 3] << 8 | d[at + 4] << 16 | d[at + 5] << 24;
                    if (off > last) { last = off; lastNumber = d[at] | d[at + 1] << 8; }
                }
                d[last + 2] = 0xFF; d[last + 3] = 0xFF;
                File.WriteAllBytes(path, d);
                var s = GameSession.Open(copy.Folder);
                Assert.Contains(s.ReadProblems, p => p.Contains("STRINGS.PAK block " + lastNumber));
                Assert.Empty(s.Strings.Block(lastNumber));
            }
        }

        [Fact]
        public void A_saved_player_that_cannot_be_read_is_listed()
        {
            string g = GameData.Game("UW2"); if (g == null || !Directory.Exists(Path.Combine(g, "SAVE1"))) return;
            using (var copy = new Damage.GameCopy(g))
            {
                File.WriteAllBytes(Path.Combine(copy.Folder, "SAVE1", "PLAYER.DAT"), new byte[10]);
                var s = GameSession.Open(Path.Combine(copy.Folder, "SAVE1"));
                Assert.Null(s.Player);
                Assert.Contains(s.ReadProblems, p => p.StartsWith("PLAYER.DAT could not be read"));
            }
        }
    }
}
