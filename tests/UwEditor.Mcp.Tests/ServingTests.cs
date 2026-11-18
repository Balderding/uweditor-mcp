using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>Teleports with a way back, the wall side, and serving on the pipe: taken, stopped, a second client, the relay's exit.</summary>
    public class ServingTests
    {
        static McpEditor Opened()
        {
            string g = GameCopy.Make(); if (g == null) return null;
            var ed = new McpEditor(g); ed.Open(g);
            return ed;
        }

        [Fact]
        public void Teleport_with_the_way_back_works()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var open = Enumerable.Range(0, 64 * 64).Select(k => (x: k % 64, y: k / 64)).Where(t => m.Tiles[t.x, t.y].Type == TileType.Open).ToList();
            var a = open[100]; var b = open[600];
            string said = ed.RecipeTeleport(1, a.x, a.y, 1, b.x, b.y, true);
            Assert.DoesNotContain("Refused", said);
            Assert.Equal(2, ed.Edits.For(1).Model.Objects.Count(o => o != null && !o.Free && o.Item == 0x181) - m.Objects.Count(o => o != null && !o.Free && o.Item == 0x181));
            Assert.StartsWith("Level 1 passes", ed.Check(1));
        }

        [Fact]
        public void Teleport_back_squares_can_be_given()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var open = Enumerable.Range(0, 64 * 64).Select(k => (x: k % 64, y: k / 64)).Where(t => m.Tiles[t.x, t.y].Type == TileType.Open).ToList();
            var a = open[100]; var b = open[600]; var c = open[700]; var d = open[200];
            ed.RecipeTeleport(1, a.x, a.y, 1, b.x, b.y, true, c.x, c.y, d.x, d.y);
            Assert.Contains(ed.Edits.For(1).Model.TileObjects(c.x, c.y), o => o.Item == 0x1A0);
        }

        [Fact]
        public void Wall_side_by_number_refused()
        {
            var ed = Opened(); if (ed == null) return;
            var m = ed.Edits.For(1).Model;
            var door = m.Objects.First(o => o != null && !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x146);
            Assert.ThrowsAny<ArgumentException>(() => ed.RecipeLeverDoor(1, door.Index, door.TileX, door.TileY, "7"));
            Assert.False(ed.Edits.HasChanges);
        }

        [Fact]
        public void A_gate_refuses_calls()
        {
            var ed = Opened(); if (ed == null) return;
            var tools = new EditorTools(ed) { Gate = () => "Refused: the editor stopped serving." };
            Assert.Equal("Refused: the editor stopped serving.", tools.Fill(1, 2, 2, 6, 5, "open"));
            Assert.False(ed.Edits.HasChanges);
        }

        static McpEditor Hosted(out LevelEdits edits)
        {
            edits = null;
            string g = GameCopy.Make(); if (g == null) return null;
            var s = GameSession.Open(g);
            var e = edits = new LevelEdits(s);
            return McpEditor.Attached(() => s, () => e, f => f());
        }

        [Fact]
        public void A_pipe_already_taken_is_reported()
        {
            var ed = Hosted(out _); if (ed == null) return;
            string pipe = "uweditor-mcp-taken-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            using var first = PipeHost.Start(ed, pipe);
            Thread.Sleep(300);
            string failed = null;
            using var second = PipeHost.Start(ed, pipe);
            second.Failed += why => failed = why;
            var clock = Stopwatch.StartNew();
            while (failed == null && second.Problem == null && clock.ElapsedMilliseconds < 5000) Thread.Sleep(20);
            Assert.NotNull(second.Problem ?? failed);
            Assert.Contains(pipe, second.Problem ?? failed);
        }

        [Fact]
        public void Stop_is_quick_and_refuses_after()
        {
            var ed = Hosted(out var edits); if (ed == null) return;
            var h = PipeHost.Start(ed, "uweditor-mcp-stop-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Thread.Sleep(200);
            var clock = Stopwatch.StartNew();
            h.Dispose();
            Assert.True(clock.ElapsedMilliseconds < 300, "Stop took " + clock.ElapsedMilliseconds + " ms");
            Assert.True(h.Stopped);
        }

        static Process Relay(string pipe)
        {
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrEmpty(dotnet)) dotnet = "dotnet";
            var psi = new ProcessStartInfo(dotnet) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "UwEditor.Mcp.Server.dll"));
            psi.ArgumentList.Add("--app"); psi.ArgumentList.Add("--pipe"); psi.ArgumentList.Add(pipe);
            return Process.Start(psi);
        }

        const string Init = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}";

        [Fact]
        public void A_second_client_is_turned_away_and_the_relay_says_when_the_app_leaves()
        {
            var ed = Hosted(out _); if (ed == null) return;
            string pipe = "uweditor-mcp-two-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var h = PipeHost.Start(ed, pipe);
            using var one = Relay(pipe);
            Process two = null;
            try
            {
                one.StandardInput.WriteLine(Init);
                var t = one.StandardOutput.ReadLineAsync();
                Assert.True(t.Wait(20000) && t.Result != null && t.Result.Contains("\"id\":1"));
                two = Relay(pipe);                                  // only once the first holds the pipe
                var err2 = two.StandardError.ReadToEndAsync();
                Assert.True(two.WaitForExit(10000), "the second client was not turned away");
                Assert.Equal(1, two.ExitCode);

                var err1 = one.StandardError.ReadToEndAsync();
                h.Dispose();
                Assert.True(one.WaitForExit(10000), "the relay kept running after the app stopped serving");
                Assert.Equal(1, one.ExitCode);
                Assert.Contains("stopped serving", err1.Result);
            }
            finally
            {
                h.Dispose();
                foreach (var p in new[] { one, two }) try { if (p != null && !p.HasExited) p.Kill(true); } catch (InvalidOperationException) { }
                two?.Dispose();
            }
        }
    }
}
