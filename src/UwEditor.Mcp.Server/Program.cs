using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UwEditor.Mcp;

namespace UwEditor.Mcp.Server
{
    /// <summary>
    /// The editor as an MCP server over stdio. Start options: --only FOLDER (open refuses any other
    /// game folder), or --app [--pipe NAME] (pass stdio through to the editor's Level > Let an AI
    /// assistant work here, which serves the tools on the game open there; the editor itself does the
    /// same as UwEditorAvalonia --mcp). stdout carries the protocol, so the
    /// log goes to stderr.
    /// </summary>
    public static class Program
    {
        public sealed class Options
        {
            public string Only;
            public bool App;
            public string Pipe = PipeHost.DefaultPipe;
        }

        /// <summary>
        /// The start options. A missing or blank value, an unknown option, or --only with --app throws:
        /// the server must not start other than as it was asked.
        /// </summary>
        public static Options ParseArgs(string[] args)
        {
            var o = new Options();
            bool pipe = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--app") { o.App = true; continue; }
                if (a != "--only" && a != "--pipe") throw new ArgumentException("Unknown start option " + a + " (the options are --only FOLDER, --app, --pipe NAME).");
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1])) throw new ArgumentException(a + " needs a value.");
                if (a == "--only") o.Only = args[++i];
                else { o.Pipe = args[++i]; pipe = true; }
            }
            if (o.App && o.Only != null) throw new ArgumentException("--app works on the game open in the app; --only is for the server on its own.");
            if (pipe && !o.App) throw new ArgumentException("--pipe goes with --app.");
            return o;
        }

        /// <summary>The --only folder (null without one); see ParseArgs.</summary>
        public static string ParseOnly(string[] args)
        {
            var o = ParseArgs(args);
            if (o.App) throw new ArgumentException("--app is not --only.");
            return o.Only;
        }

        public static async Task<int> Main(string[] args)
        {
            Options o;
            try { o = ParseArgs(args); }
            catch (ArgumentException e) { Console.Error.WriteLine("UwEditor.Mcp: " + e.Message); return 2; }
            if (o.App) return await PipeRelay.RunAsync(o.Pipe);

            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(c => c.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(new McpEditor(o.Only));
            builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly(typeof(EditorTools).Assembly);
            await builder.Build().RunAsync();
            return 0;
        }
    }
}
