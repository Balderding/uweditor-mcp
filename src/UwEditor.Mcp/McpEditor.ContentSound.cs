using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Sound;
using UwEditor.Core;
using UwEditor.Core.Music;
using UwEditor.Core.Sound;

namespace UwEditor.Mcp
{
    /// <summary>
    /// Sound and music (Tools ▸ Sound and music) and the built-in 3D models (Tools ▸ 3D models):
    /// SOUND's .VOC files and SOUNDS.DAT, the music themes (.XMI) and UW.EXE's models. Attached,
    /// the sound window's libraries are the ones changed while it is open. Nothing is ever played.
    /// </summary>
    public sealed partial class McpEditor
    {
        SoundLibrary ctSound;
        MusicLibrary ctMusic;

        SoundLibrary CtSoundIfAny()
        {
            if (Session == null) return null;
            var h = HostSound?.Invoke();
            if (h != null && string.Equals(h.GameFolder, Session.GameFolder, StringComparison.OrdinalIgnoreCase)) return h;
            return ctFor == Session ? ctSound : null;
        }

        MusicLibrary CtMusicIfAny()
        {
            if (Session == null) return null;
            var h = HostMusic?.Invoke();
            if (h != null) return h;
            return ctFor == Session ? ctMusic : null;
        }

        SoundLibrary CtSound()
        {
            CtFresh();
            var h = HostSound?.Invoke();
            if (h != null && string.Equals(h.GameFolder, Session.GameFolder, StringComparison.OrdinalIgnoreCase))
            {
                if (ctSound != null && ctSound.HasChanges) throw new McpEditorException("The editor's sound window is open now, without your unsaved sound changes: close it first, or revert them.");
                ctSound = null;
                return h;
            }
            if (ctSound == null) ctSound = SoundLibrary.For(Session);
            return ctSound;
        }

        MusicLibrary CtMusic()
        {
            CtFresh();
            var h = HostMusic?.Invoke();
            if (h != null)
            {
                if (ctMusic != null && ctMusic.HasChanges) throw new McpEditorException("The editor's sound window is open now, without your unsaved music changes: close it first, or revert them.");
                ctMusic = null;
                return h;
            }
            if (ctMusic == null) ctMusic = MusicLibrary.Open(Session.GameFolder, Session.Kind);
            if (ctMusic.SoundFolder == null) throw new McpEditorException("The game has no SOUND folder.");
            return ctMusic;
        }

        // ---- sound files

        /// <summary>What a .VOC name is for: speech (UW1 nn, UW2 BSPnn), a UW2 effect (SPnn) or a UW2 trap sound (UWnn).</summary>
        SoundUse CtUse(SoundLibrary lib, string name, out string normal)
        {
            string n = Path.GetFileName((name ?? "").Trim()).ToUpperInvariant();
            if (!n.EndsWith(".VOC")) n += ".VOC";
            normal = n;
            string stem = n.Substring(0, n.Length - 4);
            if (lib.IsUw2)
            {
                if (stem.StartsWith("BSP") && stem.Length > 3 && stem.Substring(3).All(char.IsDigit)) return SoundUse.Uw2Speech;
                if (stem.StartsWith("SP") && stem.Length > 2 && stem.Substring(2).All(char.IsDigit)) return SoundUse.Uw2Effect;
                if (stem.StartsWith("UW") && stem.Length > 2 && stem.Substring(2).All(char.IsDigit)) return SoundUse.Uw2Trap;
            }
            else if (stem.Length > 0 && stem.All(char.IsDigit)) return SoundUse.Uw1Speech;
            throw new ArgumentException(lib.IsUw2 ? "Ultima Underworld II's sound files are BSPnn.VOC (speech), SPnn.VOC (effects) and UWnn.VOC (trap sounds)." : "Ultima Underworld's sound files are nn.VOC (the introduction's speech).");
        }

        static string CtInfo(VocInfo i)
        {
            if (!i.Exists) return i.Name + ": not there";
            return string.Format(Ci, "{0}: {1:0.00} s at {2} Hz, {3}{4}", i.Name, i.Seconds, i.Rate, i.SizeText, i.Problem != null ? " (" + i.Problem + ")" : "");
        }

        public string SoundList()
        {
            var lib = CtSound();
            var b = new StringBuilder("SOUND folder " + lib.SoundFolder + ". Nothing is played here; sound_export writes WAV files to listen to.\n");
            b.Append("\nSPEECH (").Append(lib.IsUw2 ? "BSPnn.VOC, cutscene speech" : "nn.VOC, the introduction's speech").Append("):\n");
            foreach (var s in lib.Speech())
            {
                var i = lib.Info(s.Name, lib.SpeechUse);
                b.Append(CtInfo(i)).Append(lib.IsChanged(s.Name) ? " [changed, not saved]" : "");
                if (s.Lines.Count > 0) b.Append("; said in cutscene ").Append(s.CutsceneText).Append(": \"").Append((Session.Strings.Get(s.Lines[0].StringBlock, s.Lines[0].String) ?? "").Trim()).Append('"');
                b.Append('\n');
            }
            if (lib.IsUw2)
            {
                b.Append("\nDIGITAL EFFECTS (SPnn.VOC, played for SOUNDS.DAT effects marked digital):\n");
                foreach (int n in lib.NumberedFiles("SP")) { string nm = SoundLibrary.EffectName(n); b.Append(CtInfo(lib.Info(nm, SoundUse.Uw2Effect))).Append(lib.IsChanged(nm) ? " [changed, not saved]" : "").Append(" — ").Append(SoundEffectNames.Get(true, n).What).Append('\n'); }
                b.Append("\nTRAP SOUNDS (UWnn.VOC, played by special-effects traps 0x184 of quality 2 and owner nn):\n");
                foreach (int n in lib.TrapFiles()) { string nm = SoundLibrary.TrapName(n); b.Append(CtInfo(lib.Info(nm, SoundUse.Uw2Trap))).Append(lib.IsChanged(nm) ? " [changed, not saved]" : "").Append('\n'); }
            }
            b.Append("\nSOUNDS.DAT: ").Append(lib.Effects == null ? lib.EffectsProblem : lib.Effects.Effects.Count + " effects (sound_effects lists them)" + (lib.EffectsChanged ? ", changed, not saved" : "")).Append('\n');
            return b.ToString();
        }

        public string SoundExport(string name, string folder)
        {
            var lib = CtSound();
            var dir = CtFolder(folder);
            var names = new List<string>();
            if (string.Equals((name ?? "").Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                names.AddRange(lib.Speech().Select(s => s.Name));
                if (lib.IsUw2) { names.AddRange(lib.NumberedFiles("SP").Select(SoundLibrary.EffectName)); names.AddRange(lib.TrapFiles().Select(SoundLibrary.TrapName)); }
            }
            else { string n; CtUse(lib, name, out n); names.Add(n); }
            var done = new List<string>();
            foreach (var n in names)
            {
                var b = lib.Bytes(n);
                if (b == null) { if (names.Count == 1) throw new McpEditorException(n + " is not there."); continue; }
                string p = Path.Combine(dir, Path.ChangeExtension(n, ".WAV"));
                File.WriteAllBytes(p, SoundConversion.VocToWav(b));
                done.Add(Path.GetFileName(p));
            }
            return "Written to " + dir + ": " + string.Join(", ", done) + " (8-bit mono WAV).";
        }

        public string SoundImport(string name, string wav, bool? normalise, bool? trimSilence, bool? dither, string overLimit)
        {
            var lib = CtSound();
            string n;
            var use = CtUse(lib, name, out n);
            var opt = new SoundConvertOptions();
            if (normalise.HasValue) opt.Normalise = normalise.Value;
            if (trimSilence.HasValue) opt.TrimSilence = trimSilence.Value;
            if (dither.HasValue) opt.Dither = dither.Value;
            switch ((overLimit ?? "refuse").Trim().ToLowerInvariant())
            {
                case "refuse": opt.OverLimit = OverLimit.Refuse; break;
                case "shorten": opt.OverLimit = OverLimit.Shorten; break;
                case "lowerrate": case "lower rate": case "rate": opt.OverLimit = OverLimit.LowerRate; break;
                default: throw new ArgumentException("overLimit is refuse, shorten or lowerRate.");
            }
            WavFile w;
            try { w = WavFile.Parse(CtInput(wav, "WAV")); }
            catch (Exception e) when (e is InvalidDataException || e is FormatException || e is NotSupportedException) { throw new McpEditorException("Not imported: " + e.Message); }
            var r = SoundConversion.Convert(w, use, opt);
            if (!r.Ok) throw new McpEditorException(n + " not imported: " + r.Problem + (r.TooLong ? " (overLimit shorten or lowerRate makes it fit)" : ""));
            lib.Put(n, r.Voc);
            HostSoundChanged?.Invoke();
            return n + " replaced (" + SoundLimits.Describe(use) + "): " + r.Summary + " Not written until save.";
        }

        public string SoundRevert(string name, bool toOriginal)
        {
            var lib = CtSound();
            string n;
            CtUse(lib, name, out n);
            if (toOriginal)
            {
                lib.Revert(n);
                HostSoundChanged?.Invoke();
                return n + " goes back to the original (the .ORIG the editor kept, or no file for one the editor added) on save.";
            }
            if (!lib.IsChanged(n)) return n + " has no unsaved change.";
            lib.Discard(n);
            HostSoundChanged?.Invoke();
            return n + "'s unsaved change dropped.";
        }

        public string SoundEffects()
        {
            var lib = CtSound();
            if (lib.Effects == null) throw new McpEditorException(lib.EffectsProblem);
            var t = TblObjectsOrNull();
            var b = new StringBuilder("SOUNDS.DAT (MIDI effects: patch is a timbre of bank 1)" + (lib.EffectsChanged ? ", changed, not saved" : "") + ":\n");
            for (int i = 0; i < lib.Effects.Effects.Count; i++)
            {
                var e = lib.Effects.Effects[i];
                b.AppendFormat(Ci, "{0}: patch {1}, note {2}, volume {3}, length {4}{5} — {6}", i, e.Patch, e.Note, e.Volume, e.Length, e.IsUw2 ? ", digital " + (e.Digital ? 1 : 0) + ", priority " + e.Priority : "", SoundEffectNames.Get(lib.IsUw2, i).What);
                var who = SoundEffectNames.ForCreatures(Session, t, i);
                if (who.Count > 0) b.Append("; creatures: ").Append(string.Join(", ", who.Take(8))).Append(who.Count > 8 ? "..." : "");
                b.Append('\n');
            }
            return b.ToString();
        }

        UwEditor.Core.Editing.ObjectTables TblObjectsOrNull()
        {
            try { return TblObjects(); } catch (Exception e) when (e is McpEditorException || e is IOException) { return null; }
        }

        public string SoundEffectSet(int effect, string field, int value)
        {
            var lib = CtSound();
            if (lib.Effects == null) throw new McpEditorException(lib.EffectsProblem);
            if (effect < 0 || effect >= lib.Effects.Effects.Count) throw new ArgumentOutOfRangeException(null, "SOUNDS.DAT has effects 0-" + (lib.Effects.Effects.Count - 1) + ".");
            var e = lib.Effects.Effects[effect];
            string f = (field ?? "").Trim().ToLowerInvariant();
            try
            {
                switch (f)
                {
                    case "patch": e.Patch = value; break;
                    case "note": e.Note = value; break;
                    case "volume": e.Volume = value; break;
                    case "length": e.Length = value; break;
                    case "digital": e.Digital = value != 0; break;
                    case "priority": e.Priority = value; break;
                    default: throw new ArgumentException("The fields are patch, note, volume (0-255), length (0-65535)" + (lib.IsUw2 ? ", digital (0/1) and priority (0-51255)." : "."));
                }
            }
            catch (ArgumentOutOfRangeException) { throw new ArgumentOutOfRangeException(null, "That value is out of the field's range: patch, note and volume are 0-255, length 0-65535, priority 0-51255."); }
            HostSoundChanged?.Invoke();
            return string.Format(Ci, "Effect {0}: patch {1}, note {2}, volume {3}, length {4}{5}. Not written until save.", effect, e.Patch, e.Note, e.Volume, e.Length, e.IsUw2 ? ", digital " + (e.Digital ? 1 : 0) + ", priority " + e.Priority : "");
        }

        // ---- music

        MusicTheme CtTheme(MusicLibrary lib, int number)
        {
            var t = lib.Theme(number);
            if (t == null) throw new McpEditorException("The themes are " + string.Join(", ", lib.Themes.Select(x => x.Number)) + ".");
            return t;
        }

        static List<MusicVersion> CtVersions(string version)
        {
            switch ((version ?? "both").Trim().ToLowerInvariant())
            {
                case "fm": case "adlib": return new List<MusicVersion> { MusicVersion.Fm };
                case "mt32": case "mt-32": case "roland": return new List<MusicVersion> { MusicVersion.Mt32 };
                case "both": case "": return new List<MusicVersion> { MusicVersion.Fm, MusicVersion.Mt32 };
                default: throw new ArgumentException("The version is fm, mt32 or both.");
            }
        }

        public string MusicList()
        {
            var lib = CtMusic();
            var b = new StringBuilder("The music themes of " + lib.SoundFolder + " (FM file / MT-32 file). Timbres: UW.AD " + (lib.Fm != null ? "read" : "missing") + ", UW.MT " + (lib.Mt != null ? "read" : "missing") + (lib.Kind == UltimateEditor.Games.GameKind.UW2 ? ", UW.OPL " + (lib.Opl != null ? "read" : "missing") : "") + ".\n");
            foreach (var t in lib.Themes)
            {
                b.AppendFormat(Ci, "{0}: {1}", t.Number, t.When);
                foreach (var v in new[] { MusicVersion.Fm, MusicVersion.Mt32 })
                {
                    var bytes = lib.Read(t, v);
                    var issues = bytes == null ? null : lib.Validate(t, v);
                    b.AppendFormat(Ci, "; {0} {1}", t.File(v), bytes == null ? "missing" : bytes.Length + " bytes" + (lib.IsChanged(t, v) ? " (changed, not saved)" : "") +
                        (issues.Count(i => i.Severity == MusicSeverity.Error) > 0 ? ", " + issues.Count(i => i.Severity == MusicSeverity.Error) + " error(s)" : "") +
                        (issues.Count(i => i.Severity == MusicSeverity.Warning) > 0 ? ", " + issues.Count(i => i.Severity == MusicSeverity.Warning) + " warning(s)" : ""));
                }
                if (!string.IsNullOrEmpty(t.AtEnd)) b.Append("; at its end: ").Append(t.AtEnd);
                b.Append('\n');
            }
            return b.ToString();
        }

        public string MusicCheck(int theme, string version)
        {
            var lib = CtMusic();
            var t = CtTheme(lib, theme);
            var b = new StringBuilder();
            foreach (var v in CtVersions(version))
            {
                b.Append(t.File(v)).Append(':');
                if (lib.Read(t, v) == null) { b.Append(" missing\n"); continue; }
                var issues = lib.Validate(t, v);
                if (issues.Count == 0) b.Append(" no problem found");
                foreach (var i in issues) b.Append("\n  ").Append(i);
                b.Append('\n');
            }
            return b.ToString();
        }

        public string MusicExport(string theme, string version, string folder)
        {
            var lib = CtMusic();
            var dir = CtFolder(folder);
            var themes = string.Equals((theme ?? "").Trim(), "all", StringComparison.OrdinalIgnoreCase) ? lib.Themes : new List<MusicTheme> { CtTheme(lib, CtInt(theme, "The theme")) };
            var done = new List<string>();
            foreach (var t in themes)
                foreach (var v in CtVersions(version))
                {
                    var midi = lib.ExportMidi(t, v);
                    if (midi == null) continue;
                    string p = Path.Combine(dir, MusicLibrary.MidiName(t, v));
                    File.WriteAllBytes(p, midi);
                    done.Add(Path.GetFileName(p));
                }
            if (done.Count == 0) throw new McpEditorException("Nothing written: the theme's files are missing or cannot be read.");
            return "Written to " + dir + ": " + string.Join(", ", done) + " (standard MIDI files).";
        }

        public string MusicImport(int theme, string midi, string mt32Midi, string version, bool? loopWholeTheme, bool? mapGmToMt32, bool? moveChannels, bool? replaceMissingDrums)
        {
            var lib = CtMusic();
            var t = CtTheme(lib, theme);
            var vs = CtVersions(version);
            var opt = new MusicImportOptions { FmTimbres = lib.Fm };
            if (loopWholeTheme.HasValue) opt.LoopWholeTheme = loopWholeTheme.Value;
            if (moveChannels.HasValue) opt.MoveChannels = moveChannels.Value;
            if (replaceMissingDrums.HasValue) opt.ReplaceMissingDrums = replaceMissingDrums.Value;
            var main = CtInput(midi, "MIDI");
            var second = string.IsNullOrWhiteSpace(mt32Midi) ? null : CtInput(mt32Midi, "MT-32 MIDI");
            var results = lib.Import(t, main, second, vs.Contains(MusicVersion.Fm), vs.Contains(MusicVersion.Mt32), opt, mapGmToMt32 ?? true);
            var b = new StringBuilder();
            bool ok = results.All(kv => !kv.Value.HasErrors && kv.Value.Bytes != null);
            b.Append(ok ? "Theme " + theme + " imported (not written until save):\n" : "Not imported (nothing changed): errors below.\n");
            foreach (var kv in results)
            {
                b.Append(t.File(kv.Key)).Append(kv.Value.Bytes != null ? ": " + kv.Value.Bytes.Length + " bytes" : "").Append(ok && !lib.IsChanged(t, kv.Key) ? " (the same as the file on disk: nothing to save)" : "").Append('\n');
                foreach (var i in kv.Value.Notes.Concat(kv.Value.Validation)) b.Append("  ").Append(i).Append('\n');
            }
            if (!ok) throw new McpEditorException(b.ToString());
            HostSoundChanged?.Invoke();
            return b.ToString();
        }

        public string MusicRevert(int theme, string version)
        {
            var lib = CtMusic();
            var t = CtTheme(lib, theme);
            var said = new List<string>();
            foreach (var v in CtVersions(version))
                said.Add(t.File(v) + (lib.Revert(t, v) ? " goes back to the original on save" : ": nothing to go back to"));
            HostSoundChanged?.Invoke();
            return string.Join("; ", said) + ".";
        }

        // ---- the built-in 3D models (UW.EXE)

        List<ModelEntry> CtModels()
        {
            CtFresh();
            var l = ModelLibrary.Of(Session);
            if (l.Count == 0) throw new McpEditorException("No models: the game's " + (Session.IsUw2 ? "UW2.EXE" : "UW.EXE") + " is missing or cannot be read.");
            return l;
        }

        ModelEntry CtModel(int index)
        {
            var l = CtModels();
            var e = l.FirstOrDefault(m => m.Index == index);
            if (e == null) throw new ArgumentOutOfRangeException(null, "The models are 0-" + (l.Count - 1) + ".");
            return e;
        }

        public string ModelList()
        {
            var b = new StringBuilder("The built-in 3D models of " + (Session?.IsUw2 == true ? "UW2.EXE" : "UW.EXE") + " (read only: they are code in the executable; model_export writes OBJ):\n");
            foreach (var e in CtModels())
                b.AppendFormat(Ci, "{0}: {1}; {2} vertices, {3} faces{4}{5}; items: {6}\n", e.Index, e.Model.Name, e.VertexCount, e.FaceCount,
                    e.Budget >= 0 ? ", " + e.Budget + " bytes" : "", e.SharedWith.Count > 0 ? ", the same bytes as " + string.Join(", ", e.SharedWith) : "",
                    e.Items.Count == 0 ? "none" : string.Join(", ", e.Items.Take(8).Select(i => string.Format(Ci, "0x{0:X3} {1}", i, Session.Names.Get(i)))) + (e.Items.Count > 8 ? "..." : ""));
            return b.ToString();
        }

        public string ModelGet(int index)
        {
            var e = CtModel(index);
            var m = e.Model;
            var b = new StringBuilder();
            b.AppendFormat(Ci, "Model {0}: {1} at 0x{2:X} in the executable{3}. Extent {4:0.###} × {5:0.###} × {6:0.###} tiles (x, y, z up).\n", m.Index, m.Name, m.FileOffset, m.Error != null ? " (cannot be read: " + m.Error + ")" : "", m.ExtentX, m.ExtentY, m.ExtentZ);
            b.Append("Items drawn with it: ").Append(e.Items.Count == 0 ? "none" : string.Join(", ", e.Items.Select(i => string.Format(Ci, "0x{0:X3} {1}", i, Session.Names.Get(i))))).Append('\n');
            b.Append("Vertices (index: x y z):\n");
            for (int i = 0; i < m.Vertices.Count; i++) if (m.Vertices[i] != null) b.AppendFormat(Ci, "  {0}: {1:0.####} {2:0.####} {3:0.####}\n", i, m.Vertices[i][0], m.Vertices[i][1], m.Vertices[i][2]);
            b.Append("Faces (vertices; colour = palette index, or textured):\n");
            foreach (var f in m.Faces) b.Append("  ").Append(string.Join(" ", f.Vertices)).Append(f.Textured ? "  textured" : "  colour " + f.Color).Append('\n');
            return b.ToString();
        }

        public string ModelExport(int? index, string folder)
        {
            var dir = CtFolder(folder);
            var list = index.HasValue ? new List<ModelEntry> { CtModel(index.Value) } : CtModels().Where(x => x.FaceCount > 0).ToList();
            var done = new List<string>();
            foreach (var e in list)
            {
                string name = string.Format(Ci, "model{0:D2}-{1}", e.Index, new string(e.Model.Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-'));
                while (name.Contains("--")) name = name.Replace("--", "-");
                string mtl;
                string obj = ModelLibrary.ToObj(e.Model, Session.Graphics.Palette, name + ".mtl", out mtl);
                File.WriteAllText(Path.Combine(dir, name + ".obj"), obj);
                File.WriteAllText(Path.Combine(dir, name + ".mtl"), mtl);
                done.Add(name + ".obj");
            }
            return "Written to " + dir + " (each with its .mtl of the palette colours; 1.0 = a tile, y up): " + string.Join(", ", done) + ".";
        }
    }
}
