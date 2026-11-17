using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>STRINGS.PAK as a translation table in TSV, CSV and PO, out and in again (StringsTable).</summary>
    public class StringsTableTests
    {
        static Dictionary<int, List<string>> Snapshot(StringsEditor st) { return st.Blocks().ToDictionary(b => b.Number, b => st.Get(b.Number)); }

        [Theory]
        [InlineData("UW", "tsv")]
        [InlineData("UW", "csv")]
        [InlineData("UW", "po")]
        [InlineData("UW2", "tsv")]
        [InlineData("UW2", "csv")]
        [InlineData("UW2", "po")]
        public void Every_string_goes_out_and_comes_back_the_same_and_a_translation_is_applied(string game, string format)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var st = StringsEditor.Open(s);
            var before = Snapshot(st);
            int total = before.Values.Sum(l => l.Count), filled = before.Values.Sum(l => l.Count(x => !string.IsNullOrEmpty(x)));
            string table = StringsTable.Export(st, format);

            // read back: every string is there, each with the text it has
            var problems = new List<string>();
            var rows = StringsTable.Read(table, format, problems);
            Assert.Empty(problems);
            Assert.Equal(format == "po" ? filled : total, rows.Count);
            if (format != "po" && format != "csv")
                foreach (var r in rows) Assert.Equal(before[r.Block][r.Index].Replace("\r\n", "\n"), r.Text);

            // importing the export as it is changes nothing
            var same = StringsTable.Import(st, table, format);
            Assert.Equal(0, same.Changed);
            Assert.Empty(same.Problems);
            Assert.False(st.HasChanges);

            // a translated table: three strings changed, one of them with a line break, a comma and quotes
            var picks = before.Where(b => b.Value.Count > 3).Take(3).Select(b => (Block: b.Key, Index: Enumerable.Range(0, b.Value.Count).First(i => !string.IsNullOrEmpty(b.Value[i])))).ToList();
            var texts = new[] { "FIRST CHANGED", "SECOND, \"QUOTED\"\nON TWO LINES", "THIRD\tWITH A TAB" };
            string made;
            string bs = ((char)92).ToString(), nl = ((char)10).ToString(), tab = ((char)9).ToString(), q = ((char)34).ToString();
            if (format == "tsv") made = string.Join(nl, picks.Select((p, k) => p.Block + tab + p.Index + tab + texts[k].Replace(nl, bs + "n").Replace(tab, bs + "t"))) + nl + "# a comment" + nl;
            else if (format == "csv") made = "block,index,source,translation\r\n" + string.Join("\r\n", picks.Select((p, k) => p.Block + "," + p.Index + ",ignored,\"" + texts[k].Replace("\"", "\"\"") + "\"")) + "\r\n";
            else made = string.Join(nl, picks.Select((p, k) => "msgctxt " + q + p.Block + ":" + p.Index + q + nl + "msgid " + q + "x" + q + nl + "msgstr " + q + q + nl
                + q + texts[k].Replace(bs, bs + bs).Replace(q, bs + q).Replace(nl, bs + "n").Replace(tab, bs + "t") + q + nl));
            var dry = StringsTable.Import(st, made, format, dryRun: true);
            Assert.Equal(3, dry.Changed);
            Assert.False(st.HasChanges);
            var res = StringsTable.Import(st, made, format);
            Assert.Equal(3, res.Changed);
            Assert.Empty(res.Problems);
            for (int k = 0; k < 3; k++) Assert.Equal(texts[k], st.Get(picks[k].Block, picks[k].Index));
            // everything else is as it was, and the file still writes
            var after = Snapshot(st);
            foreach (var b in before)
                for (int i = 0; i < b.Value.Count; i++)
                    if (!picks.Contains((b.Key, i))) Assert.Equal(b.Value[i], after[b.Key][i]);
            Assert.True(st.Pak.ToBytes().Length > 1000);
        }

        [Fact]
        public void Rows_that_do_not_fit_are_reported_and_the_rest_applied()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var st = StringsEditor.Open(s);
            int count = st.Get(1).Count;
            string tsv = "1\t0\tGOOD\n9999\t0\tno such block\n1\t" + (count + 5) + "\tpast the end\nnot a row\n1\t1\t\n1\t0\tTWICE\n1\t2\t\u4e2d\u6587\n";
            var res = StringsTable.Import(st, tsv, "tsv");
            Assert.Equal(1, res.Changed);
            Assert.Equal(1, res.Empty);
            Assert.Equal(5, res.Problems.Count);
            Assert.Equal("GOOD", st.Get(1, 0));
            Assert.Equal(count, st.Get(1).Count);
            Assert.Contains("not applied", res.Describe());
            Assert.Equal("tsv", StringsTable.FormatOf(@"C:\x\de.strings.tsv"));
            Assert.Equal("po", StringsTable.FormatOf("uw.pot"));
            Assert.Null(StringsTable.FormatOf("strings.json"));
        }
    }
}
