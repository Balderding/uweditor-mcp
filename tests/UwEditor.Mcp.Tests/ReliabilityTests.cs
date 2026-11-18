using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ModelContextProtocol.Protocol;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>
    /// For clients: refusals marked as errors, reads that leave no editor behind, undo, a long object list
    /// cut short, the assistant's save in the editor; and the relay, which works in any order with the window.
    /// </summary>
    public class ReliabilityTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        [Fact]
        public void A_refusal_is_an_error_result()
        {
            var r = new EditorTools(new McpEditor(null)).LevelMap(1);
            Assert.True(r.IsError);
            Assert.Equal("Refused: Open a game folder first.", EditorTools.Text(r));
            var ed = Opened(); if (ed == null) return;
            Assert.NotEqual(true, new EditorTools(ed).LevelMap(1).IsError);
        }

        [Fact]
        public void An_unexpected_failure_is_an_error_with_its_reason()
        {
            var ed = McpEditor.Attached(() => throw new NotSupportedException("boom"), () => null, f => f());
            var r = new EditorTools(ed).LevelMap(1);
            Assert.True(r.IsError);
            Assert.Contains("boom", EditorTools.Text(r));
        }

        [Fact]
        public void Reading_a_level_leaves_no_editor_behind()
        {
            var ed = Opened(); if (ed == null) return;
            ed.LevelMap(2); ed.TileGet(2, 10, 10); ed.ObjectList(2, 10, 10); ed.Check(2); ed.PicturePng(2, null, null, null, null, 4);
            Assert.Null(ed.Edits.Existing(2));
            Assert.StartsWith("Level 2 passes", ed.Check(2));
        }

        [Fact]
        public void Undo_takes_back_the_last_change()
        {
            var ed = Opened(); if (ed == null) return;
            int before = ed.Edits.For(1).TileField(4, 4, "Floor height");
            ed.Fill(1, 2, 2, 6, 5, "open", (before + 3) % 16, null, null);
            Assert.Contains("Undid", ed.Undo(1, 1));
            Assert.Equal(before, ed.Edits.For(1).TileField(4, 4, "Floor height"));
            Assert.ThrowsAny<Exception>(() => ed.Undo(3, 1));         // nothing to undo on level 3
        }

        [Fact]
        public void A_whole_level_object_list_is_cut_short()
        {
            var ed = Opened(); if (ed == null) return;
            var lines = ed.ObjectList(1, null, null).Split('\n');
            Assert.True(lines.Length <= McpEditor.MaxListed + 3, lines.Length + " lines");
            Assert.Contains(lines, l => l.Contains("x and y"));
        }

        [Fact]
        public void The_editors_save_refuses_a_level_failing_the_checks_with_the_reason()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var s = GameSession.Open(g); var edits = new LevelEdits(s);
            int saves = 0;
            var ed = McpEditor.Attached(() => s, () => edits, f => f(), () => { saves++; throw new InvalidOperationException("disk full"); });
            var tools = new EditorTools(ed);
            Assert.Equal("Nothing to save.", EditorTools.Text(tools.Save()));
            Assert.Equal(0, saves);
            ed.Fill(1, 2, 2, 6, 5, "open", 3, null, null);
            var r = tools.Save();
            Assert.True(r.IsError);
            Assert.Contains("disk full", EditorTools.Text(r));
        }

        // ---- the relay (UwEditor.Mcp.Server --app, the editor's --mcp)

        static Process Relay(string pipe)
        {
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            var psi = new ProcessStartInfo(string.IsNullOrEmpty(dotnet) ? "dotnet" : dotnet)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "UwEditor.Mcp.Server.dll"));
            psi.ArgumentList.Add("--app"); psi.ArgumentList.Add("--pipe"); psi.ArgumentList.Add(pipe);
            var p = Process.Start(psi);
            p.ErrorDataReceived += (o, e) => { };
            p.BeginErrorReadLine();
            return p;
        }

        static int id;
        static string Ask(Process p, string method, string pars)
        {
            int n = ++id;
            p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":" + n + ",\"method\":\"" + method + "\"" + (pars == null ? "" : ",\"params\":" + pars) + "}");
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 30000)
            {
                var t = p.StandardOutput.ReadLineAsync();
                Assert.True(t.Wait(30000), "no answer to " + method);
                if (t.Result == null) throw new Exception("the relay ended");
                if (t.Result.Contains("\"id\":" + n + ",") || t.Result.Contains("\"id\":" + n + "}")) return t.Result;
            }
            throw new Exception("no answer to " + method);
        }

        const string Fill = "{\"name\":\"fill\",\"arguments\":{\"level\":1,\"x1\":2,\"y1\":2,\"x2\":6,\"y2\":5,\"type\":\"open\",\"height\":3}}";

        [Fact]
        public void The_relay_works_in_any_order_with_the_window()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var s = GameSession.Open(g); var edits = new LevelEdits(s);
            var ed = McpEditor.Attached(() => s, () => edits, f => f());
            string pipe = "uweditor-mcp-order-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            using var p = Relay(pipe);                                   // the client first, no window yet
            PipeHost h = null;
            try
            {
                Assert.Contains("\"result\"", Ask(p, "initialize", "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}"));
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                string list = Ask(p, "tools/list", null);
                Assert.Contains("\"name\":\"fill\"", list);
                Assert.DoesNotContain("\"name\":\"open\"", list);          // the editor opens games itself

                string none = Ask(p, "tools/call", Fill);
                Assert.Contains("\"isError\":true", none);
                Assert.Contains("Let an AI assistant work here", none);

                h = PipeHost.Start(ed, pipe);                              // the window opens: the next call reaches it
                Assert.Contains("Set 20 tiles", Ask(p, "tools/call", Fill));
                Assert.True(edits.HasChanges);

                h.Dispose(); h = null;                                     // closed...
                System.Threading.Thread.Sleep(300);
                Assert.Contains("\"isError\":true", Ask(p, "tools/call", Fill));
                h = PipeHost.Start(ed, pipe);                              // ...and open again: the relay finds it
                Assert.Contains("Set 20 tiles", Ask(p, "tools/call", Fill));
            }
            finally
            {
                h?.Dispose();
                try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { }
            }
        }
    }
}
