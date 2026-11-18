using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The MCP client's side of "Let an AI assistant work here": an MCP server on stdio (the editor's
    /// --mcp, or UwEditor.Mcp.Server --app) that lists the tools itself and passes each call to the
    /// editor's window over its pipe (PipeHost). The client may start it before the window is open, and
    /// the window may be closed and opened again: a call finds the window when it is there, and is
    /// answered with an error saying what to open when it is not.
    /// </summary>
    public static class PipeRelay
    {
        /// <summary>The editor's --mcp [--pipe NAME]: runs the relay until the client ends it; 2: bad options.</summary>
        public static int Main(string[] args)
        {
            string pipe = PipeHost.DefaultPipe;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--pipe" && i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1])) pipe = args[++i];
                else { Console.Error.WriteLine("UwEditor MCP: --mcp takes only --pipe NAME (not " + args[i] + ")."); return 2; }
            }
            return RunAsync(pipe).GetAwaiter().GetResult();
        }

        public const string NotServing = "The Underworld Editor is not serving: open a game in the editor, then Level > Let an AI assistant work here (MCP)..., and try again.";
        public const string Busy = "Another assistant is connected to the editor's Let an AI assistant work here window (one at a time). Try again when it has finished.";

        /// <summary>Runs the relay on stdio until the client closes it.</summary>
        public static async Task<int> RunAsync(string pipeName = PipeHost.DefaultPipe)
        {
            var tools = PipeHost.HostedTools().Select(m => McpServerTool.Create(m, new EditorTools(new McpEditor(null))).ProtocolTool).ToList();
            using var link = new Link(pipeName);
            var options = new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "uweditor", Version = typeof(PipeRelay).Assembly.GetName().Version?.ToString() ?? "0" },
                Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
                Handlers = new McpServerHandlers
                {
                    ListToolsHandler = (r, ct) => new ValueTask<ListToolsResult>(new ListToolsResult { Tools = tools }),
                    CallToolHandler = async (r, ct) => await link.CallAsync(r.Params, ct).ConfigureAwait(false),
                },
            };
            await using (var server = McpServer.Create(new StdioServerTransport("uweditor"), options))
                await server.RunAsync().ConfigureAwait(false);
            return 0;
        }

        static CallToolResult Error(string text)
        {
            return new CallToolResult { Content = new List<ContentBlock> { new TextContentBlock { Text = text } }, IsError = true };
        }

        /// <summary>Whether a pipe of that name is served on this computer (it may be busy with another client).</summary>
        static bool PipeExists(string name)
        {
            try
            {
                if (OperatingSystem.IsWindows()) return Directory.GetFiles(@"\\.\pipe\").Any(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
                return File.Exists(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + name));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>The connection to the window: made when a call comes, made again after it is lost.</summary>
        sealed class Link : IDisposable
        {
            readonly string pipeName;
            readonly SemaphoreSlim one = new SemaphoreSlim(1, 1);
            NamedPipeClientStream pipe;
            StreamReader reader;
            StreamWriter writer;
            int nextId;

            public Link(string pipeName) { this.pipeName = pipeName; }

            public async Task<CallToolResult> CallAsync(CallToolRequestParams p, CancellationToken ct)
            {
                await one.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // a window closed since the last call: its connection fails once, then a new one is made
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        if (pipe == null && !await ConnectAsync(ct).ConfigureAwait(false))
                            return Error(PipeExists(pipeName) ? Busy : NotServing);
                        try
                        {
                            var answer = await RequestAsync("tools/call", JsonSerializer.SerializeToNode(p, McpJsonUtilities.DefaultOptions), ct).ConfigureAwait(false);
                            if (answer["error"] != null) return Error("The editor answered with an error: " + answer["error"]?["message"]);
                            return answer["result"].Deserialize<CallToolResult>(McpJsonUtilities.DefaultOptions) ?? Error("The editor gave no answer.");
                        }
                        catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
                        {
                            Drop();
                        }
                    }
                    return Error("The editor's window stopped serving (it was closed, or Stop was pressed). " + NotServing);
                }
                finally { one.Release(); }
            }

            async Task<bool> ConnectAsync(CancellationToken ct)
            {
                var p = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await p.ConnectAsync(1500, ct).ConfigureAwait(false); }
                catch (Exception e) when (e is TimeoutException || e is IOException || e is UnauthorizedAccessException)
                {
                    p.Dispose();
                    return false;
                }
                pipe = p;
                reader = new StreamReader(p);
                writer = new StreamWriter(p) { AutoFlush = true, NewLine = "\n" };
                try
                {
                    var init = new JsonObject
                    {
                        ["protocolVersion"] = "2025-06-18",
                        ["capabilities"] = new JsonObject(),
                        ["clientInfo"] = new JsonObject { ["name"] = "uweditor-relay", ["version"] = "1" },
                    };
                    var answer = await RequestAsync("initialize", init, ct).ConfigureAwait(false);
                    if (answer["result"] == null) { Drop(); return false; }
                    await writer.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" }.ToJsonString()).ConfigureAwait(false);
                    return true;
                }
                catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
                {
                    Drop();
                    return false;
                }
            }

            /// <summary>One request on the pipe and its answer (lines with other ids, and notifications, are passed over).</summary>
            async Task<JsonNode> RequestAsync(string method, JsonNode pars, CancellationToken ct)
            {
                int id = ++nextId;
                var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = pars };
                await writer.WriteLineAsync(msg.ToJsonString()).ConfigureAwait(false);
                for (;;)
                {
                    string line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line == null) throw new IOException("The editor closed the connection.");
                    if (line.Length == 0) continue;
                    JsonNode n;
                    try { n = JsonNode.Parse(line); } catch (JsonException) { continue; }
                    var nid = n?["id"];
                    if (nid != null && nid.GetValueKind() == JsonValueKind.Number && nid.GetValue<int>() == id) return n;
                }
            }

            void Drop()
            {
                try { pipe?.Dispose(); } catch (IOException) { }
                pipe = null; reader = null; writer = null;
            }

            public void Dispose() { Drop(); one.Dispose(); }
        }
    }
}
