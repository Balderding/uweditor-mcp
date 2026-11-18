using System;
using System.Collections.Generic;
using System.IO;

namespace UwEditor.Mcp.Tests
{
    /// <summary>
    /// A fresh copy of UW1 for a test to change: UW_GAMEDATA\UW or tests\gamedata\UW (as the core tests' GameData), copied
    /// under UW_MCP_TMP (or a gamecopies folder beside the tests). Null when there is no game data: the
    /// test then passes without checking, as the core tests do. The copies of a run are removed when
    /// the run ends.
    /// </summary>
    public static class GameCopy
    {
        static readonly List<string> made = new List<string>();

        static GameCopy()
        {
            AppDomain.CurrentDomain.ProcessExit += (o, e) =>
            {
                lock (made)
                    foreach (var d in made)
                        try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            };
        }

        public static string Make()
        {
            string root = Environment.GetEnvironmentVariable("UW_GAMEDATA");
            if (string.IsNullOrEmpty(root))
            {
                // as the core tests' GameData: the git-ignored tests/gamedata folder of the repository
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimaUnderworldEditorAvalonia.sln"))) dir = dir.Parent;
                if (dir != null) root = Path.Combine(dir.FullName, "tests", "gamedata");
            }
            string game = string.IsNullOrEmpty(root) ? null : Path.Combine(root, "UW");
            if (game == null || !Directory.Exists(Path.Combine(game, "DATA"))) return null;
            string tmp = Environment.GetEnvironmentVariable("UW_MCP_TMP");
            if (string.IsNullOrEmpty(tmp)) tmp = Path.Combine(AppContext.BaseDirectory, "gamecopies");
            string to = Path.Combine(tmp, "uwmcp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            lock (made)
            {
                // the test host may end before ProcessExit runs: a run also clears earlier runs' copies
                if (made.Count == 0 && Directory.Exists(tmp))
                    foreach (var d in Directory.GetDirectories(tmp, "uwmcp-*"))
                        if (Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddMinutes(-10))
                            try { Directory.Delete(d, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                made.Add(to);
            }
            CopyFolder(Path.Combine(game, "DATA"), Path.Combine(to, "DATA"));
            return to;
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(from)) CopyFolder(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
