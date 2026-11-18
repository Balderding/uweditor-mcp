using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace UwEditor.Mcp.Tests
{
    /// <summary>The server itself over stdio: the handshake, its tool list, and one call.</summary>
    public class StdioTests
    {
        [Fact]
        public void Server_lists_its_tools()
        {
            string g = GameCopy.Make(); if (g == null) return;
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrEmpty(dotnet)) dotnet = "dotnet";
            var psi = new ProcessStartInfo(dotnet)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "UwEditor.Mcp.Server.dll"));
            psi.ArgumentList.Add("--only");
            psi.ArgumentList.Add(g);
            using var p = Process.Start(psi);
            try
            {
                p.ErrorDataReceived += (s, e) => { };
                p.BeginErrorReadLine();
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}");
                Assert.Contains("\"id\":1", Line(p));
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
                string list = Line(p);
                foreach (var t in new[] { "open", "level_map", "tile_get", "tile_set", "fill", "check", "save" })
                    Assert.Contains("\"name\":\"" + t + "\"", list);
                string arg = g.Replace("\\", "\\\\");
                p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"open\",\"arguments\":{\"folder\":\"" + arg + "\"}}}");
                Assert.Contains("level 1: in use", Line(p));
            }
            finally { if (!p.HasExited) p.Kill(true); }
        }

        static string Line(Process p)
        {
            var t = p.StandardOutput.ReadLineAsync();
            Assert.True(t.Wait(TimeSpan.FromSeconds(20)), "the server did not answer in 20 s");
            Assert.NotNull(t.Result);
            return t.Result;
        }
    }
}
