using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The app's side (PipeHost on a game it holds) and the server program passing stdio through to it (--app).</summary>
    public class PipeTests
    {
        static Process Relay(string pipe)
        {
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrEmpty(dotnet)) dotnet = "dotnet";
            var psi = new ProcessStartInfo(dotnet)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "UwEditor.Mcp.Server.dll"));
            psi.ArgumentList.Add("--app");
            psi.ArgumentList.Add("--pipe");
            psi.ArgumentList.Add(pipe);
            return Process.Start(psi);
        }

        static string Line(Process p)
        {
            var t = p.StandardOutput.ReadLineAsync();
            Assert.True(t.Wait(TimeSpan.FromSeconds(20)), "no answer in 20 s");
            Assert.NotNull(t.Result);
            return t.Result;
        }

        [Fact]
        public void The_relay_reaches_the_hosted_game()
        {
            string g = GameCopy.Make(); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var ed = McpEditor.Attached(() => s, () => edits, f => f());
            string pipe = "uweditor-mcp-test-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var calls = new List<string>();
            using var host = PipeHost.Start(ed, pipe);
            host.Called += c => { lock (calls) calls.Add(c); };
            using var p = Relay(pipe);
            try
            {
                p.ErrorDataReceived += (o, e) => { };
                p.BeginErrorReadLine();
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}");
                Assert.Contains("\"id\":1", Line(p));
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
                string list = Line(p);
                foreach (var t in new[] { "fill", "object_add", "recipe_teleport", "picture" }) Assert.Contains("\"name\":\"" + t + "\"", list);
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"fill\",\"arguments\":{\"level\":1,\"x1\":2,\"y1\":2,\"x2\":6,\"y2\":5,\"type\":\"open\",\"height\":3}}}");
                Assert.Contains("Set 20 tiles", Line(p));
                Assert.True(edits.HasChanges);
                Assert.True(host.Connected);
                lock (calls) Assert.Contains(calls, c => c.StartsWith("fill: Set 20 tiles"));
            }
            finally { try { p.Kill(true); } catch (InvalidOperationException) { } }
        }

        [Fact]
        public void Without_the_window_a_call_is_an_error_saying_what_to_open()
        {
            using var p = Relay("uweditor-mcp-none-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                p.ErrorDataReceived += (o, e) => { };
                p.BeginErrorReadLine();
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}");
                Assert.Contains("\"id\":1", Line(p));
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"level_map\",\"arguments\":{\"level\":1}}}");
                string r = Line(p);
                Assert.Contains("\"isError\":true", r);
                Assert.Contains("Let an AI assistant work here", r);
                Assert.False(p.HasExited);
            }
            finally { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } }
        }

        [Fact]
        public void Start_options()
        {
            var o = UwEditor.Mcp.Server.Program.ParseArgs(new[] { "--app" });
            Assert.True(o.App);
            Assert.Equal(PipeHost.DefaultPipe, o.Pipe);
            Assert.Throws<ArgumentException>(() => UwEditor.Mcp.Server.Program.ParseArgs(new[] { "--app", "--only", "O:\\x" }));
            Assert.Throws<ArgumentException>(() => UwEditor.Mcp.Server.Program.ParseArgs(new[] { "--pipe" }));
        }
    }
}
