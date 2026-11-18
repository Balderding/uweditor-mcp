using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Level ▸ Let an AI assistant work here (MCP): a client on the pipe changes the game open in the window, on its UI thread.</summary>
    public class McpWindowTests
    {
        [AvaloniaFact]
        public void A_fill_through_the_pipe_changes_the_open_level()
        {
            string game = GameData.Game("UW");
            if (game == null) return;
            string tmp = Environment.GetEnvironmentVariable("UW_MCP_TMP");
            string folder = Path.Combine(string.IsNullOrEmpty(tmp) ? Path.GetTempPath() : tmp, "uwedit-mcp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Path.Combine(folder, "DATA"));
            foreach (var f in Directory.GetFiles(Path.Combine(game, "DATA"))) File.Copy(f, Path.Combine(folder, "DATA", Path.GetFileName(f)));
            try
            {
                var w = Ui.Open(folder);
                w.AskBeforeDiscarding = false;
                string pipe = "uweditor-mcp-apptest-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                var mw = w.ShowMcp(pipe);
                Ui.Pump();
                Assert.Same(mw, w.ShowMcp(pipe));

                var client = Task.Run(() =>
                {
                    using var p = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    p.Connect(5000);
                    var r = new StreamReader(p); var wr = new StreamWriter(p) { AutoFlush = true };
                    wr.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}");
                    r.ReadLine();
                    wr.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                    wr.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"fill\",\"arguments\":{\"level\":1,\"x1\":2,\"y1\":2,\"x2\":6,\"y2\":5,\"type\":\"open\",\"height\":3}}}");
                    return r.ReadLine();
                });
                var clock = Stopwatch.StartNew();
                while (!client.IsCompleted && clock.ElapsedMilliseconds < 20000) { Ui.Pump(); System.Threading.Thread.Sleep(10); }   // the calls run on this (the UI) thread
                Assert.True(client.IsCompleted, "no answer through the pipe in 20 s");
                Assert.Contains("Set 20 tiles", client.Result);
                Assert.True(w.ViewModel.HasLevelChanges);
                Assert.Equal(3, w.ViewModel.Edits.For(1).TileField(4, 4, "Floor height"));
                Ui.Pump();
                Assert.Contains(mw.Calls, c => c.Contains("fill: Set 20 tiles"));
                // the editor itself as the client's relay (UwEditorAvalonia --mcp), as an MCP client starts it;
                // the first client has left, so the window serves the next
                string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                var psi = new ProcessStartInfo(string.IsNullOrEmpty(dotnet) ? "dotnet" : dotnet)
                {
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                };
                psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "UwEditorAvalonia.dll"));
                psi.ArgumentList.Add("--mcp"); psi.ArgumentList.Add("--pipe"); psi.ArgumentList.Add(pipe);
                using (var relay = Process.Start(psi))
                {
                    relay.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}");
                    var line = relay.StandardOutput.ReadLineAsync();
                    clock.Restart();
                    while (!line.IsCompleted && clock.ElapsedMilliseconds < 20000) { Ui.Pump(); System.Threading.Thread.Sleep(10); }
                    Assert.True(line.IsCompleted && line.Result != null && line.Result.Contains("\"id\":1"), "the editor's --mcp did not answer");
                    // a call: --mcp passes it to this window, which runs it on the UI thread
                    relay.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                    relay.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"tile_get\",\"arguments\":{\"level\":1,\"x\":4,\"y\":4}}}");
                    line = relay.StandardOutput.ReadLineAsync();
                    clock.Restart();
                    while (!line.IsCompleted && clock.ElapsedMilliseconds < 20000) { Ui.Pump(); System.Threading.Thread.Sleep(10); }
                    Assert.True(line.IsCompleted && line.Result != null, "the editor's --mcp did not reach the window");
                    Assert.Contains("Floor height: 3", line.Result);       // the fill made through the pipe above
                    Ui.Pump();
                    Assert.Contains(mw.Calls, c => c.Contains("tile_get: Tile (4, 4)"));
                    relay.StandardInput.Close();
                    Assert.True(relay.WaitForExit(10000), "the relay kept running after its client closed stdin");
                    Assert.Equal(0, relay.ExitCode);
                }

                mw.Close();
                Ui.Pump();
            }
            finally { try { Directory.Delete(folder, true); } catch (Exception) { } }
        }
    }
}
