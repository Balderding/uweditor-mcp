using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The MCP client's side of "Let an AI assistant work here": passes the client's stdin to the
    /// editor's pipe (PipeHost) and the pipe to stdout, until either side ends. Started by the client
    /// as `UwEditorAvalonia --mcp` (or `UwEditor.Mcp.Server --app`).
    /// </summary>
    public static class PipeRelay
    {
        /// <summary>The editor's --mcp [--pipe NAME]: runs the relay and answers its exit code (2: bad options).</summary>
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

        /// <summary>Runs the relay; the exit code: 0 the client ended, 1 no editor serving or it stopped.</summary>
        public static async Task<int> RunAsync(string pipeName = PipeHost.DefaultPipe, int connectMs = 3000)
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(connectMs).ConfigureAwait(false); }
            catch (Exception e) when (e is TimeoutException || e is UnauthorizedAccessException || e is IOException)
            {
                Console.Error.WriteLine("UwEditor MCP: the editor is not running Level > Let an AI assistant work here (no pipe " + pipeName + "), or another client is connected.");
                return 1;
            }
            using Stream stdin = Console.OpenStandardInput(), stdout = Console.OpenStandardOutput();
            var up = Copy(stdin, pipe);
            var down = Copy(pipe, stdout);
            if (await Task.WhenAny(up, down).ConfigureAwait(false) == up) return 0;      // the client closed stdin: done
            Console.Error.WriteLine("UwEditor MCP: the editor stopped serving (its Let an AI assistant work here window was closed).");
            return 1;
        }

        static async Task Copy(Stream from, Stream to)
        {
            var buf = new byte[16384];
            try
            {
                int n;
                while ((n = await from.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                {
                    await to.WriteAsync(buf, 0, n).ConfigureAwait(false);
                    await to.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
