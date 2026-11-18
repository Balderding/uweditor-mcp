using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;
using UwEditor.Core.Music;
using UwEditor.Core.Sound;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The content tools' shared part (conversations, balance, graphics, models, sound and music,
    /// mods): what the app's windows hand over (the host hooks), the own copies kept here
    /// otherwise, and their saving. Each copy is held as the editor's own windows hold it: in
    /// memory until save, written with SafeFile (NAME.EXT.ORIG once, NAME.EXT.BKP each time).
    /// An own copy with changes is not written over a file changed on disk since it was read.
    /// </summary>
    public sealed partial class McpEditor
    {
        // ---- the host's hooks (set by the app's McpWindow.Content.cs; null standing alone)

        /// <summary>Attached: the app's conversations (CNV.ARK and the STRINGS.PAK everything shares), or null.</summary>
        public Func<ConversationLibrary> HostConversations { get; set; }
        /// <summary>Attached: why a slot may not be changed now (the conversation window has text typed there, not applied), or null.</summary>
        public Func<int, string> HostConversationBusy { get; set; }
        /// <summary>Attached: after a slot's conversation or strings changed here (so the window shows it).</summary>
        public Action<int> HostConversationChanged { get; set; }
        /// <summary>Attached: the sound window's sound library while it is open, else null.</summary>
        public Func<SoundLibrary> HostSound { get; set; }
        /// <summary>Attached: the sound window's music library while it is open, else null.</summary>
        public Func<MusicLibrary> HostMusic { get; set; }
        /// <summary>Attached: after sound or music changed here (so the sound window redraws).</summary>
        public Action HostSoundChanged { get; set; }
        /// <summary>Attached: whether the app holds unsaved work of any kind (a mod is applied only without).</summary>
        public Func<bool> HostUnsaved { get; set; }
        /// <summary>Attached: opens the game again after a mod was applied (the app's windows close and read it anew).</summary>
        public Action HostReopen { get; set; }
        /// <summary>Attached: after content files were written here, with their names (so the views read them again).</summary>
        public Action<IList<string>> HostContentSaved { get; set; }

        object ctFor;

        /// <summary>The own copies belong to one game: another game open drops them.</summary>
        void CtFresh()
        {
            Require();
            if (ctFor == Session) return;
            ctFor = Session;
            ctLib = null; ctLibCnvDisk = null; ctLibPakDisk = null; ctUndo.Clear(); ctUndoLib = null;
            ctSound = null; ctMusic = null; ctCritters = null; ctCritterDisk.Clear(); ctLpf.Clear(); ctLpfDisk.Clear();
            ctFonts.Clear(); ctFontDisk.Clear(); ctFontChanged.Clear(); ctCutscenes = null;
        }

        static bool CtDiffers(string path, byte[] read)
        {
            if (path == null || read == null) return false;
            try { return !File.ReadAllBytes(path).SequenceEqual(read); }
            catch (IOException) { return false; }
        }

        static byte[] CtRead(string path)
        {
            try { return path != null && File.Exists(path) ? File.ReadAllBytes(path) : null; }
            catch (IOException) { return null; }
        }

        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <summary>A number in decimal or 0x hexadecimal.</summary>
        static int CtInt(string s, string what)
        {
            s = (s ?? "").Trim();
            int v;
            bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(s.Substring(2), NumberStyles.HexNumber, Ci, out v)
                : int.TryParse(s, NumberStyles.Integer, Ci, out v);
            if (!ok) throw new ArgumentException(what + " \"" + s + "\" is not a number (decimal, or hexadecimal with 0x).");
            return v;
        }

        /// <summary>A folder to write into, made when missing.</summary>
        static string CtFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("No folder given.");
            string f = Path.GetFullPath(folder.Trim().Trim('"'));
            Directory.CreateDirectory(f);
            return f;
        }

        /// <summary>A file to read, which must be there.</summary>
        static byte[] CtInput(string path, string what)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("No " + what + " file given.");
            string p = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(p)) throw new McpEditorException("There is no file " + p + ".");
            return File.ReadAllBytes(p);
        }

        /// <summary>A picture as PNG, scaled up by whole pixels (nearest), for looking at.</summary>
        static byte[] CtPng(IndexedImage img, int scale)
        {
            if (scale <= 1) return Png.EncodeRgba(img.Width, img.Height, img.ToRgba());
            return CtScaled(img.Width, img.Height, img.ToRgba(), scale);
        }

        static byte[] CtScaled(int w, int h, byte[] rgba, int scale)
        {
            if (scale < 1 || scale > 16) throw new ArgumentOutOfRangeException(null, "The scale is 1-16.");
            if ((long)w * h * scale * scale > Png.MaxPixels) throw new ArgumentOutOfRangeException(null, "That scale makes a picture of more than " + Png.MaxPixels + " pixels: use a smaller one.");
            int W = w * scale, H = h * scale;
            var o = new byte[W * H * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Array.Copy(rgba, ((y / scale) * w + x / scale) * 4, o, (y * W + x) * 4, 4);
            return Png.EncodeRgba(W, H, o);
        }

        /// <summary>The scale a picture is shown at by default: about 256 pixels wide, at most 8 times.</summary>
        static int CtAutoScale(int w, int h, int? scale)
        {
            if (scale.HasValue) return scale.Value;
            int m = Math.Max(w, h);
            return Math.Max(1, Math.Min(8, 256 / Math.Max(1, m)));
        }

        // ---- saving (save calls these)

        /// <summary>The content with unsaved changes made here or in the app's windows this server shares, by name.</summary>
        List<string> CtChangedParts()
        {
            var l = new List<string>();
            if (Session == null) return l;
            var lib = CtLibIfAny();
            if (lib != null && lib.Archive.HasChanges) l.Add("conversations (CNV.ARK)");
            if (Session.Pictures.HasChanges) l.Add("pictures (" + string.Join(", ", Session.Pictures.Open.Where(f => f.HasChanges).Select(f => f.Name)) + ")");
            if (ctFor == Session)
            {
                if (ctCritters != null && ctCritters.Any(c => c.Pages.Any(p => p.HasChanges))) l.Add("critter art");
                if (ctLpf.Values.Any(a => a.Changed)) l.Add("cutscenes");
                if (ctFontChanged.Count > 0) l.Add("fonts");
            }
            var snd = CtSoundIfAny();
            if (snd != null && snd.HasChanges) l.Add("sound (" + string.Join(", ", snd.ChangedNames) + ")");
            var mus = CtMusicIfAny();
            if (mus != null && mus.HasChanges) l.Add("music");
            return l;
        }

        /// <summary>Refuses (writing nothing) when an own changed copy's file was changed on disk since it was read.</summary>
        void CheckContentOnDisk()
        {
            if (Session == null || ctFor != Session) return;
            var stale = new List<string>();
            if (ctLib != null && ctLib.Archive.HasChanges && CtDiffers(ctLib.Archive.Path, ctLibCnvDisk)) stale.Add("CNV.ARK");
            if (ctCritters != null)
                foreach (var c in ctCritters) foreach (var p in c.Pages.Where(p => p.HasChanges)) { byte[] d; if (ctCritterDisk.TryGetValue(p.Path, out d) && CtDiffers(p.Path, d)) stale.Add(p.Name); }
            foreach (var kv in ctLpf) { byte[] d; if (kv.Value.Changed && ctLpfDisk.TryGetValue(kv.Key, out d) && CtDiffers(kv.Key, d)) stale.Add(Path.GetFileName(kv.Key)); }
            foreach (var n in ctFontChanged) { byte[] d; var p = CtFontPath(n); if (ctFontDisk.TryGetValue(n, out d) && CtDiffers(p, d)) stale.Add(n); }
            if (stale.Count > 0)
                throw new McpEditorException(string.Join(", ", stale) + " changed on disk since your changes began (saved from the editor?); writing would lose that. Nothing was written: revert your changes there, then make them again.");
        }

        /// <summary>Writes every changed content file (own copies and the shared ones); answers what was written, or null.</summary>
        string SaveContent()
        {
            if (Session == null) return null;
            var written = new List<string>();
            var notes = new List<string>();
            var lib = CtLibIfAny();
            if (lib != null && lib.HasChanges)
            {
                written.AddRange(lib.Save());
                Session.ReloadStrings();
                if (lib == ctLib) { ctLibCnvDisk = CtRead(lib.Archive.Path); ctLibPakDisk = CtRead(lib.Strings.Path); if (ownStrings != null && ownStrings.Pak == lib.Strings) ownStringsDisk = ctLibPakDisk; }
            }
            if (Session.Pictures.HasChanges)
            {
                foreach (var f in Session.Pictures.Open.Where(x => x.HasChanges).ToList())
                {
                    notes.AddRange(Session.Pictures.Save(f));
                    written.Add(f.Name);
                }
                if (!IsAttached) Session.ReloadGraphics();
            }
            if (ctFor == Session)
            {
                if (ctCritters != null)
                    foreach (var c in ctCritters)
                        foreach (var p in c.Pages.Where(p => p.HasChanges))
                        {
                            p.Save();
                            ctCritterDisk[p.Path] = CtRead(p.Path);
                            written.Add("CRIT\\" + p.Name);
                        }
                foreach (var kv in ctLpf.Where(x => x.Value.Changed).ToList())
                {
                    kv.Value.Save();
                    ctLpfDisk[kv.Key] = CtRead(kv.Key);
                    written.Add("CUTS\\" + Path.GetFileName(kv.Key));
                }
                foreach (var n in ctFontChanged.ToList())
                {
                    string p = CtFontPath(n);
                    SafeFile.Write(p, ctFonts[n].ToBytes());
                    ctFontDisk[n] = CtRead(p);
                    written.Add(n);
                }
                ctFontChanged.Clear();
            }
            var snd = CtSoundIfAny();
            if (snd != null && snd.HasChanges) written.AddRange(snd.SaveAll().Select(x => "SOUND\\" + Path.GetFileName(x)));
            var mus = CtMusicIfAny();
            if (mus != null && mus.HasChanges) written.AddRange(mus.Save().Select(x => "SOUND\\" + Path.GetFileName(x)));
            if (written.Count == 0) return null;
            HostContentSaved?.Invoke(written);
            if (snd != null && snd == HostSound?.Invoke()) HostSoundChanged?.Invoke();
            return "Saved " + string.Join(", ", written.Distinct()) + " (each old file kept as .BKP, the first as .ORIG)." + (notes.Count > 0 ? " " + string.Join(" ", notes) : "");
        }

        /// <summary>One part of save's answer after another; null parts left out.</summary>
        static string CtJoin(params string[] parts)
        {
            var l = parts.Where(p => !string.IsNullOrEmpty(p)).ToList();
            return l.Count == 0 ? null : string.Join(" ", l);
        }
    }
}
