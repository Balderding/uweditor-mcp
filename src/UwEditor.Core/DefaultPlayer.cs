using System;
using System.IO;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>
    /// The player a play test uses when the game folder has no saved game to take one from: a
    /// PLAYER.DAT kept in the editor (Resources/default-player-uw1.dat and -uw2.dat, a character
    /// of each game), and a saved game made round it from the game folder's own files. Only the
    /// player comes from the editor; the levels, the conversations' globals and UW2's schedules
    /// are the game's.
    /// </summary>
    public static class DefaultPlayer
    {
        public static string ResourceName(GameKind kind) { return kind == GameKind.UW2 ? "default-player-uw2.dat" : "default-player-uw1.dat"; }

        /// <summary>The default PLAYER.DAT of a game (a fresh copy each time), or null when the editor has none.</summary>
        public static byte[] Bytes(GameKind kind)
        {
            using (var st = typeof(DefaultPlayer).Assembly.GetManifestResourceStream(ResourceName(kind)))
            {
                if (st == null) return null;
                using (var ms = new MemoryStream()) { st.CopyTo(ms); return ms.ToArray(); }
            }
        }

        /// <summary>
        /// Writes a saved game in slotDir as the game makes one for a new character: PLAYER.DAT
        /// (the default player), LEV.ARK (levels, as given), BGLOBALS.DAT (DATA's BABGLOBS.DAT
        /// with every word 0, as a new game copies it: conv/BABL.C), UW2's SCD.ARK (DATA's) and
        /// DESC. The player's position is left for the caller to set.
        /// </summary>
        public static void WriteSave(GameSession s, string slotDir, byte[] levels, string description = "Play test")
        {
            byte[] player = Bytes(s.Kind);
            if (player == null) throw new InvalidOperationException("The editor has no default player for this game.");
            if (Directory.Exists(slotDir)) Directory.Delete(slotDir, true);
            Directory.CreateDirectory(slotDir);
            File.WriteAllBytes(Path.Combine(slotDir, "PLAYER.DAT"), player);
            File.WriteAllBytes(Path.Combine(slotDir, Path.GetFileName(s.LevArkPath)), levels);
            string bab = GameProfile.Find(s.DataFolder, "BABGLOBS.DAT");
            File.WriteAllBytes(Path.Combine(slotDir, "BGLOBALS.DAT"), bab != null ? BGlobals.Parse(File.ReadAllBytes(bab), false).ToBytes(true) : new byte[0]);
            if (s.IsUw2)
            {
                string scd = GameProfile.Find(s.DataFolder, "SCD.ARK");
                if (scd != null) File.Copy(scd, Path.Combine(slotDir, "SCD.ARK"), true);
            }
            File.WriteAllBytes(Path.Combine(slotDir, "DESC"), Encoding.Latin1.GetBytes(description.Length > 0x1E ? description.Substring(0, 0x1E) : description));
        }
    }
}
