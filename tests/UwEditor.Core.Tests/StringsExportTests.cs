using System.Linq;
using System.Text.Json.Nodes;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The Strings window's "Export all as JSON".</summary>
    public class StringsExportTests
    {
        [Fact]
        public void Every_block_is_exported_with_its_strings_as_shown()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            s.Accents.Set('$', 'é');
            var e = StringsEditor.Open(s);
            e.Set(1, 0, "Café");                // an unsaved edit, with a mapped letter
            var root = JsonNode.Parse(e.ExportJson()) as JsonObject;
            Assert.Equal("STRINGS.PAK", (string)root["file"]);
            var blocks = (JsonArray)root["blocks"];
            var list = e.Blocks();
            Assert.Equal(list.Count, blocks.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Assert.Equal(list[i].Number, (int)blocks[i]["block"]);
                Assert.Equal("0x" + list[i].Number.ToString("X4"), (string)blocks[i]["hex"]);
                Assert.Equal(e.Get(list[i].Number).Count, ((JsonArray)blocks[i]["strings"]).Count);
            }
            var one = blocks.First(b => (int)b["block"] == 1);
            Assert.Equal("Café", (string)one["strings"][0]);
            // the letter is written as itself, not as an escape
            Assert.Contains("Café", e.ExportJson());
        }
    }
}
