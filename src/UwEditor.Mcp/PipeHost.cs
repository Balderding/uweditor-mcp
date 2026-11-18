using System;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The MCP tools served on a named pipe of this PC (this user only), for a host that holds the
    /// game (the app), all but open (the host opens games). One client at a time. The client reaches
    /// it through PipeRelay (the editor's --mcp, or the server program's --app).
    /// </summary>
    public sealed class PipeHost : IDisposable
    {
        public const string DefaultPipe = "uweditor-mcp";

        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly EditorTools tools;
        Task loop;
        volatile bool connected;

        /// <summary>A client is connected now.</summary>
        public bool Connected { get { return connected; } }
        /// <summary>After each tool call: "tool: the answer's first line". Raised on the call's thread.</summary>
        public event Action<string> Called;
        /// <summary>A client came or went (true: came).</summary>
        public event Action<bool> ConnectionChanged;
        /// <summary>The pipe could not be served (another program holds it), or (null) it is served again; on a worker thread, retried every 2 s.</summary>
        public event Action<string> Failed;
        /// <summary>Why the pipe is not served now, or null.</summary>
        public string Problem { get; private set; }
        /// <summary>Stopped: calls still waiting are refused.</summary>
        public bool Stopped { get { return stopped; } }
        volatile bool stopped;

        PipeHost(McpEditor ed)
        {
            tools = new EditorTools(ed) { Gate = () => stopped ? "Refused: the editor stopped serving." : null };
            tools.Called += (name, answer) =>
            {
                string first = (answer ?? "").Split('\n')[0];
                Called?.Invoke(name + ": " + first);
            };
        }

        /// <summary>A host not serving yet: attach to its events, then Begin.</summary>
        public static PipeHost Create(McpEditor ed)
        {
            if (ed == null) throw new ArgumentNullException(nameof(ed));
            return new PipeHost(ed);
        }

        /// <summary>Starts serving on the pipe.</summary>
        public PipeHost Begin(string pipeName = DefaultPipe)
        {
            if (loop == null) loop = Task.Run(() => Serve(pipeName));
            return this;
        }

        public static PipeHost Start(McpEditor ed, string pipeName = DefaultPipe) { return Create(ed).Begin(pipeName); }

        /// <summary>The tool methods served for a host (the window): every one but open.</summary>
        public static MethodInfo[] HostedTools()
        {
            return typeof(EditorTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null && m.GetCustomAttribute<McpServerToolAttribute>().Name != "open").ToArray();
        }

        McpServerOptions Options()
        {
            var o = new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "uweditor", Version = typeof(PipeHost).Assembly.GetName().Version?.ToString() ?? "0" },
                ToolCollection = new McpServerPrimitiveCollection<McpServerTool>(),
            };
            foreach (var m in HostedTools()) o.ToolCollection.Add(McpServerTool.Create(m, tools));
            return o;
        }

        async Task Serve(string pipeName)
        {
            while (!stop.IsCancellationRequested)
            {
                NamedPipeServerStream made;
                try
                {
                    made = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    if (Problem != null) { Problem = null; Failed?.Invoke(null); }
                }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
                {
                    Problem = "Another program already serves the pipe " + pipeName + " (another editor's Let an AI assistant work here?): " + e.Message;
                    Failed?.Invoke(Problem);
                    try { await Task.Delay(2000, stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                    continue;
                }
                try
                {
                    using (var pipe = made)
                    {
                        await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                        connected = true;
                        ConnectionChanged?.Invoke(true);
                        try
                        {
                            await using (var server = McpServer.Create(new StreamServerTransport(pipe, pipe, "uweditor"), Options()))
                                await server.RunAsync(stop.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            connected = false;
                            ConnectionChanged?.Invoke(false);
                        }
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (System.IO.IOException)         // the client left mid-message: wait for the next
                {
                    try { await Task.Delay(200, stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                }
                catch (ObjectDisposedException) { return; }
            }
        }

        /// <summary>
        /// Stops serving: a call still waiting is refused, the client's connection ends. Does not wait
        /// for the serving loop (it may be waiting on the caller's thread, the app's UI thread).
        /// </summary>
        public void Dispose()
        {
            stopped = true;
            if (!stop.IsCancellationRequested) stop.Cancel();
        }
    }
}
