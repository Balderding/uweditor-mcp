using System;
using System.Globalization;
using System.Linq;

namespace UwEditor.Core.Editing
{
    /// <summary>The text of the Go to box: "level, x, y", or "x, y" for a square of the level shown.</summary>
    public static class GoToSpot
    {
        /// <summary>
        /// Reads "level, x, y" or "x, y" (commas, semicolons or blanks between the numbers; brackets and
        /// a leading L are ignored, so "L26 (33, 33)" reads too). With two numbers the level is
        /// <paramref name="currentLevel"/>. Returns null when read, else what is wrong.
        /// </summary>
        public static string Parse(string text, int currentLevel, Func<int, bool> hasLevel, out int level, out int x, out int y)
        {
            level = currentLevel; x = y = -1;
            var parts = (text ?? "").Split(new[] { ',', ';', ' ', '\t', '(', ')', '[', ']', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.TrimStart('L', 'l')).Where(p => p.Length > 0).ToArray();
            if (parts.Length != 2 && parts.Length != 3) return "Type level, x, y or x, y.";
            var n = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return "\"" + parts[i] + "\" is not a number.";
            if (parts.Length == 3) level = n[0];
            x = n[parts.Length - 2]; y = n[parts.Length - 1];
            if (x > 63 || y > 63) return "x and y run from 0 to 63.";
            if (level <= 0 || (hasLevel != null && !hasLevel(level))) return parts.Length == 3 ? "There is no level " + level + "." : "No level is shown.";
            return null;
        }
    }
}
