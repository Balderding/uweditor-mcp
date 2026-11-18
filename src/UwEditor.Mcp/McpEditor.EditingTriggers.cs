using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateEditor.Uw1;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;

namespace UwEditor.Mcp
{
    /// <summary>Tools ▸ Triggers and traps, links between objects, trap targets, Preview changed terrain, Keys and locks.</summary>
    public sealed partial class McpEditor
    {
        static string EdFlat(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        static string EdWhere(ObjectInfo o) { return o.TileX >= 0 ? "(" + o.TileX + ", " + o.TileY + ")" : "(nowhere)"; }

        /// <summary>A level's trigger chains, wandering-monster spawners, traps no trigger reaches and UW2 rune traps.</summary>
        public string TriggerChains(int level)
        {
            var m = ReadModel(level);
            if (!m.Present) return "Level " + level + " is an empty slot.";
            var s = Session;
            var b = new StringBuilder();
            var chains = Triggers.Chains(m);
            b.Append("Level ").Append(level).Append(": ").Append(chains.Count).Append(" triggers\n");
            foreach (var c in chains)
            {
                var t = c.Trigger;
                b.Append("#").Append(t.Index).Append(' ').Append(t.Name).Append(" at ").Append(EdWhere(t)).Append(", target (").Append(c.TargetX).Append(", ").Append(c.TargetY).Append(")");
                b.Append(": ").Append(EdFlat(Triggers.Short(s, t))).Append('\n');
                foreach (var o in c.Steps)
                    b.Append("  -> #").Append(o.Index).Append(' ').Append(o.Name).Append(' ').Append(EdWhere(o)).Append(": ").Append(EdFlat(Triggers.Short(s, o)))
                        .Append(Triggers.IsTrap(o.Item) ? "" : Triggers.IsTrigger(o.Item) ? " (a trigger: runs its own chain)" : " (not a trap)").Append('\n');
            }
            var spawners = Triggers.Spawners(m);
            if (spawners.Count > 0)
            {
                b.Append(spawners.Count).Append(" wandering-monster spawners\n");
                foreach (var o in spawners) b.Append("  #").Append(o.Index).Append(' ').Append(o.Name).Append(' ').Append(EdWhere(o)).Append(": ").Append(EdFlat(Triggers.SpawnerText(s, m, o))).Append('\n');
            }
            var set = new HashSet<int>(spawners.Select(o => o.Index));
            var lone = Triggers.Unreached(m, chains).Where(o => !set.Contains(o.Index)).ToList();
            if (lone.Count > 0)
            {
                b.Append(lone.Count).Append(" traps no trigger runs (a conversation, a schedule or the game's code may)\n");
                foreach (var o in lone)
                {
                    var from = Triggers.LinkedFrom(m, o);
                    b.Append("  #").Append(o.Index).Append(' ').Append(o.Name).Append(' ').Append(EdWhere(o)).Append(": ").Append(EdFlat(Triggers.Short(s, o)))
                        .Append(from.Count > 0 ? " (linked from " + string.Join(", ", from.Select(x => "#" + x.Index)) + ")" : "").Append('\n');
                }
            }
            var runes = Triggers.RuneTraps(m);
            if (runes.Count > 0)
            {
                b.Append(runes.Count).Append(" rune traps\n");
                foreach (var o in runes) b.Append("  #").Append(o.Index).Append(' ').Append(o.Name).Append(' ').Append(EdWhere(o)).Append('\n');
            }
            return b.ToString();
        }

        /// <summary>An object's links as the properties panel shows them: what it is, its chain, what leads to it and where it acts.</summary>
        public string ObjectLinks(int level, int index)
        {
            var m = ReadModel(level);
            if (index < 2 || index >= m.Objects.Length || m.Objects[index].Free) throw new ArgumentException("#" + index + " is not an object of level " + level + ".");
            var o = m.Objects[index];
            var b = new StringBuilder();
            b.Append(o).Append(' ').Append(EdWhere(o)).Append('\n');
            string ex = Triggers.Explain(Session, o);
            if (!string.IsNullOrWhiteSpace(ex)) b.Append(ex.Trim()).Append('\n');
            if (o.IsTrapOrTrigger)
            {
                string chain = Triggers.ChainText(m, o);
                if (!string.IsNullOrWhiteSpace(chain)) b.Append("Chain: ").Append(EdFlat(chain)).Append('\n');
                var next = Triggers.ChainNext(m, o);
                if (next.Count > 0) b.Append("Next: ").Append(string.Join(", ", next.Select(x => x.ToString()))).Append('\n');
                var prev = Triggers.ChainPrevious(m, o);
                if (prev.Count > 0) b.Append("Previous: ").Append(string.Join(", ", prev.Select(x => x.ToString()))).Append('\n');
            }
            foreach (var l in TriggerLinks.Of(m, o)) b.Append(l.Text).Append('\n');
            return b.ToString();
        }

        /// <summary>Links a trigger, trap or lever to an object that stays where it lies (the properties panel's link).</summary>
        public string ObjectLink(int level, int from, int to)
        {
            var e = Editable(level);
            Index(e, from); Index(e, to);
            int src = e.LinkObjects(from, to);
            var m = e.Model;
            return "#" + src + " " + m.Objects[src].Name + (src != from ? " (inside #" + from + ")" : "") + " now links to #" + to + " " + m.Objects[to].Name + ". One undo step.";
        }

        /// <summary>Takes a trigger's or trap's link off.</summary>
        public string ObjectUnlink(int level, int from)
        {
            var e = Editable(level);
            Index(e, from);
            int was = e.Model.Objects[from].Link;
            e.Unlink(from);
            return was == 0 ? "#" + from + " linked to nothing." : "#" + from + " no longer links to #" + was + ".";
        }

        /// <summary>Edit ▸ Choose the selected trap's target: the square a trigger, teleport or delete trap acts on (a teleport also its level).</summary>
        public string TrapTargetSet(int level, int index, int x, int y, int? toLevel)
        {
            var e = Editable(level);
            Index(e, index);
            CheckTile(x, y);
            var o = e.Model.Objects[index];
            if (!Triggers.HasTargetSquare(o.Item)) throw new ArgumentException("#" + index + " " + o.Name + " has no target square: triggers, teleport and delete traps do.");
            int dest = toLevel ?? level;
            if (dest != level)
            {
                if (!Triggers.IsTeleport(o.Item)) throw new ArgumentException("Only a teleport trap reaches another level; " + o.Name + " acts on its own level.");
                CheckLevelNumber(Session, dest);
            }
            var S = o.IsMobile ? Uw1Schemas.MobileObject : Uw1Schemas.StaticObject;
            e.Batch("Target of #" + index, () =>
            {
                e.SetObjectField(index, S["Quality"], x);
                e.SetObjectField(index, S["Owner"], y);
                if (Triggers.IsTeleport(o.Item)) e.SetObjectField(index, S["Z"], dest == level ? 0 : dest);
            });
            return "#" + index + " " + o.Name + " now acts on (" + x + ", " + y + ")" + (dest != level ? " of level " + dest : "") + ".";
        }

        /// <summary>Edit ▸ Go to the teleport's target: where a teleport trap (or a trigger running one) sends the player.</summary>
        public string TeleportTarget(int level, int index)
        {
            var m = ReadModel(level);
            if (index < 2 || index >= m.Objects.Length || m.Objects[index].Free) throw new ArgumentException("#" + index + " is not an object of level " + level + ".");
            var o = m.Objects[index];
            var t = Triggers.Targets(m, o).Where(x => x.What.StartsWith("teleports")).ToList();
            if (t.Count == 0) throw new ArgumentException("#" + index + " " + o.Name + " teleports nowhere (it is not a teleport trap, nor a trigger running one).");
            return string.Join("\n", t.Select(x => "Level " + x.Level + " (" + x.X + ", " + x.Y + "): " + x.What)) + "\n";
        }

        // ---- Preview changed terrain

        /// <summary>The level's change-terrain traps (UW2 also height oscillators), numbered for terrain_preview.</summary>
        public string TerrainChanges(int level)
        {
            var m = ReadModel(level);
            var list = TerrainPreview.List(Session, m);
            if (list.Count == 0) return "Level " + level + " has no change-terrain traps.";
            var b = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
                b.Append(i + 1).Append(". ").Append(EdFlat(list[i].Text)).Append(" (squares (").Append(list[i].X).Append(", ").Append(list[i].Y).Append(") ").Append(list[i].W).Append('x').Append(list[i].H).Append(")\n");
            return b.ToString();
        }

        /// <summary>The 2D map of the level as it would be after the chosen terrain changes ran ("all", or numbers from terrain_changes); the level is not changed.</summary>
        public byte[] TerrainPreviewPng(int level, string which, int cell, out string said)
        {
            var m = ReadModel(level);
            if (cell < 4 || cell > 32) throw new ArgumentOutOfRangeException(null, "A tile is 4-32 pixels.");
            var list = TerrainPreview.List(Session, m);
            if (list.Count == 0) throw new McpEditorException("Level " + level + " has no change-terrain traps.");
            List<TerrainChange> chosen;
            if (string.IsNullOrWhiteSpace(which) || which.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) chosen = list;
            else chosen = EdIndexes(which).Select(n => { if (n < 1 || n > list.Count) throw new ArgumentOutOfRangeException(null, "The changes are 1-" + list.Count + "."); return list[n - 1]; }).ToList();
            var copy = TerrainPreview.Apply(m, chosen);
            var diff = TerrainPreview.Differences(m, copy);
            said = chosen.Count + " change" + (chosen.Count == 1 ? "" : "s") + " applied to a copy: " + diff.Count + " square" + (diff.Count == 1 ? "" : "s") + " differ"
                + (diff.Count > 0 ? " (" + string.Join(", ", diff.Take(30).Select(d => "(" + d % 64 + ", " + d / 64 + ")")) + (diff.Count > 30 ? ", ..." : "") + ")" : "") + ". The level is not changed.";
            var img = MapPicture.Render(copy, cell);
            return Png.EncodeRgba(img.Width, img.Height, img.Pixels);
        }

        // ---- Keys and locks

        /// <summary>The keys and locks across the levels by lock number, with the keys' text.</summary>
        public string KeysList(int? number)
        {
            Require();
            var st = EdStrings();
            var groups = KeyIndex.Build(Session, l => Edits.Model(l));
            if (number.HasValue) groups = groups.Where(g => g.Id == number.Value).ToList();
            if (groups.Count == 0) return number.HasValue ? "No key or lock has number " + number + "." : "No keys or locks in the levels.";
            var b = new StringBuilder();
            foreach (var g in groups)
            {
                string text = st != null ? st.Get(5, g.TextIndex) : g.Text;
                b.Append(g.Summary).Append(string.IsNullOrWhiteSpace(text) ? "" : ": \"" + EdFlat(text) + "\"").Append('\n');
                foreach (var k in g.Keys) b.Append("  key ").Append(k).Append('\n');
                foreach (var l in g.Locks) b.Append("  lock ").Append(l).Append(l.Locked ? ", locked" : ", unlocked").Append(l.Relockable ? ", relockable" : "").Append('\n');
            }
            return b.ToString();
        }

        /// <summary>The description of the keys of a lock number (STRINGS.PAK block 5, string 0x64 + the number).</summary>
        public string KeyTextSet(int number, string text)
        {
            Require();
            if (number < 0 || number > 0x1FF - 0x64) throw new ArgumentOutOfRangeException(null, "Lock numbers are 0-" + (0x1FF - 0x64) + ".");
            var st = TblStrings();
            int index = 0x64 + number;
            if (index > st.Get(5).Count) throw new ArgumentException("Block 5 has strings 0-" + (st.Get(5).Count - 1) + ": string " + index + " cannot be added.");
            string before = index < st.Get(5).Count ? st.Get(5, index) : null;
            try { st.Set(5, index, (text ?? "").Trim()); }
            catch (FormatException e) { throw new ArgumentException(e.Message); }
            HostTablesEdited?.Invoke();
            return "Keys of lock " + number + " (block 5 string " + index + "): " + (before == null ? "" : "\"" + before + "\" -> ") + "\"" + st.Get(5, index) + "\". Not written until save.";
        }
    }
}
