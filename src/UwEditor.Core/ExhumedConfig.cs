using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>
    /// The settings Underworld Exhumed runs with when the editor starts it. The port has one
    /// settings file, uw1port.cfg (uw2port.cfg for UW2), in its home folder: lines of key=value,
    /// read by port_config_get and written by port_config_set (the port's sys/gamedir.c; a line
    /// counts when it starts with the key and an equals sign, so a line starting with # does
    /// not). The home is --home, else $UW1PORT_HOME / $UW2PORT_HOME, else %APPDATA%\uw1port on
    /// Windows and ~/.uw1port elsewhere (sys/main.c), and the port keeps its saved games there
    /// too. No option names the file by itself, so the editor keeps its own copy of the settings
    /// and writes their keys into the home's file before each start; --home is the folder the
    /// game runs in, which is where the saved games then land.
    /// </summary>
    public static class ExhumedConfig
    {
        /// <summary>The port's settings file in its home folder.</summary>
        public static string FileName(GameKind kind) { return kind == GameKind.UW2 ? "uw2port.cfg" : "uw1port.cfg"; }

        /// <summary>
        /// The settings the editor starts with: every key the port's file has (its
        /// docs/SETTINGS.md), each commented out, so that nothing changes until a line is
        /// uncommented or added.
        /// </summary>
        public static string Defaults(GameKind kind)
        {
            string port = kind == GameKind.UW2 ? "uw2port" : "uw1port";
            return
"# Underworld Exhumed's settings for runs the Underworld Editor starts (" + port + ".cfg).\n" +
"# One key=value a line, with no spaces round the equals sign. A line that starts with # does\n" +
"# nothing: take the # away to use it. Before the port starts, the editor writes the keys set\n" +
"# here into " + port + ".cfg of the folder the game runs in (the game folder, or the play\n" +
"# test's copy); keys not set here stay as that file has them.\n" +
"#\n" +
"# Enhancements and fixes that are on, by name with commas between (" + port + " --enhance list):\n" +
"#enhance=skip-intro\n" +
"# Do not open the settings screen before the game (F11 still opens it):\n" +
"#settings-at-start=0\n" +
"# The window: fullscreen 0 or 1, scale 1 to 8, 4:3 aspect 0 or 1, whole-number scaling 0 or 1:\n" +
"#fullscreen=0\n" +
"#scale=3\n" +
"#aspect=1\n" +
"#integer=1\n" +
"# The mouse (follow or lock) and the mouse-look speed (10 to 400):\n" +
"#mouse=follow\n" +
"#look-speed=100\n" +
"# Sound: the volume (0 to 100) and the folder of your MT-32 ROM images:\n" +
"#volume=100\n" +
"#mt32-roms=\n" +
"# Record each session's inputs to recordings in that folder (0 or 1):\n" +
"#recording=1\n";
        }

        /// <summary>
        /// The keys a settings text sets, in order (a key set twice keeps its last value, as the
        /// port reads it). Comments, blank lines and lines with no equals sign set nothing, and
        /// neither does "data": the editor names the game folder itself, with --data.
        /// </summary>
        public static List<KeyValuePair<string, string>> Keys(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line[0] == '#' || eq <= 0) continue;
                string key = line.Substring(0, eq).TrimEnd();
                if (key.Any(char.IsWhiteSpace) || key == "data") continue;
                list.RemoveAll(k => k.Key == key);
                list.Add(new KeyValuePair<string, string>(key, line.Substring(eq + 1).TrimStart()));
            }
            return list;
        }

        /// <summary>The key a line of the port's file sets, or null (the port's own test: the key, then an equals sign, from the line's start).</summary>
        static string KeyOfLine(string line)
        {
            int eq = line.IndexOf('=');
            return eq > 0 && line[0] != '#' ? line.Substring(0, eq) : null;
        }

        /// <summary>
        /// A home's settings file with the editor's keys in it: the file's own lines but those
        /// that set one of the editor's keys, then the editor's keys. A home with no file yet
        /// gets the title line the port itself starts one with.
        /// </summary>
        public static string Merge(string homeText, string editorText, GameKind kind)
        {
            var keys = Keys(editorText);
            var set = new HashSet<string>(keys.Select(k => k.Key));
            var sb = new StringBuilder();
            if (homeText == null) sb.Append("# " + (kind == GameKind.UW2 ? "uw2port" : "uw1port") + " settings\n");
            else
                foreach (var raw in homeText.Replace("\r\n", "\n").Split('\n'))
                {
                    if (raw.Length == 0) continue;
                    string key = KeyOfLine(raw);
                    if (key != null && set.Contains(key)) continue;
                    sb.Append(raw).Append('\n');
                }
            foreach (var k in keys) sb.Append(k.Key).Append('=').Append(k.Value).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Writes the editor's keys into the settings file of home (made when it is not there).
        /// Settings with no key set leave the home alone. Returns the file written, or null.
        /// </summary>
        public static string Apply(string home, GameKind kind, string editorText)
        {
            if (Keys(editorText).Count == 0) return null;
            Directory.CreateDirectory(home);
            string path = GameProfile.Find(home, FileName(kind)) ?? Path.Combine(home, FileName(kind));
            string before = File.Exists(path) ? File.ReadAllText(path) : null;
            string after = Merge(before, editorText, kind);
            if (before == null || after != before.Replace("\r\n", "\n")) File.WriteAllText(path, after);
            return path;
        }

        /// <summary>A port's own settings file as settings for the editor: its lines but the game folder it remembers ("data").</summary>
        public static string FromPortFile(string text)
        {
            var sb = new StringBuilder();
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Length == 0 || KeyOfLine(raw) == "data") continue;
                sb.Append(raw).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>A file that is only a placeholder for one kept in the cloud (OneDrive's Files On-Demand and the like): Windows' offline and recall attributes.</summary>
        public static bool CloudOnly(FileAttributes a)
        {
            const int recallOnOpen = 0x40000, recallOnDataAccess = 0x400000;
            return (a & FileAttributes.Offline) != 0 || ((int)a & (recallOnOpen | recallOnDataAccess)) != 0;
        }

        /// <summary>The files of a folder (and the folders in it) that are not on this disk, by CloudOnly; the attributes alone are read, so nothing is fetched.</summary>
        public static List<string> NotOnDisk(string folder, Func<string, FileAttributes> attributes = null)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;
            attributes = attributes ?? File.GetAttributes;
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { if (CloudOnly(attributes(f))) list.Add(f); } catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }

        /// <summary>
        /// Why the port cannot run on these folders, in words, or null. The port ends without a
        /// word when it cannot read its files, which is what happens with a folder of a cloud
        /// drive (OneDrive) whose files are not on this disk: the game folder, the home, and the
        /// folder the port itself is in are looked at.
        /// </summary>
        public static string Problem(string port, string data, string home, Func<string, FileAttributes> attributes = null)
        {
            if (!string.IsNullOrEmpty(data) && !Directory.Exists(data)) return "The game folder is not there: " + data + ".";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in new[] { new[] { "The game folder", data }, new[] { "The folder the saved games go to", home }, new[] { "The port's folder", string.IsNullOrEmpty(port) ? null : Path.GetDirectoryName(port) } })
            {
                if (string.IsNullOrEmpty(pair[1]) || !seen.Add(Path.GetFullPath(pair[1]))) continue;
                var off = NotOnDisk(pair[1], attributes);
                if (off.Count == 0) continue;
                return string.Format("{0} ({1}) has {2} file{3} that {4} not on this disk, only in the cloud ({5}{6}). Underworld Exhumed would end without a word. Make the folder available on this device (in OneDrive: right-click it, Always keep on this device) and wait for it to finish, or copy the game to a folder outside the cloud drive.",
                    pair[0], pair[1], off.Count, off.Count == 1 ? "" : "s", off.Count == 1 ? "is" : "are", string.Join(", ", off.Take(3).Select(Path.GetFileName)), off.Count > 3 ? ", …" : "");
            }
            return null;
        }

        /// <summary>
        /// The port's arguments: --data the game's files and --home the folder its settings file
        /// and its saved games go to (sys/main.c), then the extra arguments set.
        /// </summary>
        public static List<string> Arguments(string data, string home, string extra)
        {
            var a = new List<string> { "--data", data, "--home", home };
            if (!string.IsNullOrWhiteSpace(extra)) a.AddRange(PlayTest.SplitArgs(extra));
            return a;
        }
    }
}
