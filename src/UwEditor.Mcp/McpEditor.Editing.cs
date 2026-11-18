using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>
    /// The Edit menu's object and history commands: redo, the undo history, raising floors, objects to
    /// the floor, copy / cut / paste / duplicate of objects, deleting several, taking one out of a
    /// container, locks and snapping to a wall. Helpers of the editing group start with Ed.
    /// </summary>
    public sealed partial class McpEditor
    {
        // ---- helpers

        /// <summary>Object indexes from text: "12, 34 56" or "#12 #34".</summary>
        static List<int> EdIndexes(string text)
        {
            var list = new List<int>();
            foreach (var p in (text ?? "").Split(new[] { ',', ';', ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (!int.TryParse(p.Trim().TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out n)) throw new ArgumentException("\"" + p + "\" is not an object index.");
                if (!list.Contains(n)) list.Add(n);
            }
            if (list.Count == 0) throw new ArgumentException("Give the objects' indexes (#), e.g. \"12, 34\".");
            return list;
        }

        /// <summary>The tiles of a rectangle (corners in any order); all four left out is one tile when x1/y1 are given only.</summary>
        static List<(int X, int Y)> EdRect(int x1, int y1, int? x2, int? y2)
        {
            CheckTile(x1, y1);
            int bx = x2 ?? x1, by = y2 ?? y1;
            CheckTile(bx, by);
            var list = new List<(int, int)>();
            for (int y = Math.Min(y1, by); y <= Math.Max(y1, by); y++)
                for (int x = Math.Min(x1, bx); x <= Math.Max(x1, bx); x++) list.Add((x, y));
            return list;
        }

        static string EdRectText(int x1, int y1, int? x2, int? y2)
        {
            int bx = x2 ?? x1, by = y2 ?? y1;
            if (bx == x1 && by == y1) return "(" + x1 + ", " + y1 + ")";
            return "(" + Math.Min(x1, bx) + ", " + Math.Min(y1, by) + ")-(" + Math.Max(x1, bx) + ", " + Math.Max(y1, by) + ")";
        }

        static readonly List<TextSlot> EdNoTexts = new List<TextSlot>();

        /// <summary>The strings everything shares (the app's when attached), or null when they cannot be had now.</summary>
        StringsEditor EdStrings()
        {
            try { return TblStrings(); }
            catch (Exception e) when (e is McpEditorException || e is System.IO.IOException || e is InvalidOperationException || e is FormatException) { return null; }
        }

        /// <summary>
        /// A change with what it does to the objects' texts, as the editor makes it (MainWindow.Texts.cs
        /// WithTexts): the strings it gave copies or freed are set back on undo and again on redo.
        /// </summary>
        void EdWithTexts(Action change, Func<StringsEditor, List<TextSlot>> made, Func<StringsEditor, List<TextSlot>> freed)
        {
            var st = EdStrings();
            List<TextSlot> m = EdNoTexts, f = EdNoTexts;
            Action<List<TextSlot>, bool> put = (list, text) =>
            {
                if (st == null) return;
                foreach (var t in list) { try { st.Set(t.Block, t.Index, text ? t.Text : ""); } catch (Exception) { } }
            };
            Edits.Together(() =>
            {
                change();
                if (st != null)
                {
                    try { m = made?.Invoke(st) ?? EdNoTexts; } catch (InvalidOperationException) { m = EdNoTexts; }
                    try { f = freed?.Invoke(st) ?? EdNoTexts; } catch (InvalidOperationException) { f = EdNoTexts; }
                }
            },
            undone: () => { put(m, false); put(f, true); },
            redone: () => { put(m, true); put(f, false); });
            if (m.Count > 0 || f.Count > 0) HostTablesEdited?.Invoke();
            edTextNote = (m.Count > 0 ? " " + m.Count + " cop" + (m.Count == 1 ? "y" : "ies") + " got a text of " + (m.Count == 1 ? "its" : "their") + " own (" + string.Join(", ", m.Take(4)) + ")." : "")
                + (f.Count > 0 ? " " + f.Count + " string" + (f.Count == 1 ? "" : "s") + " no object reads any more emptied (" + string.Join(", ", f.Take(4)) + ")." : "");
        }

        string edTextNote = "";

        /// <summary>The objects of the list that are not inside another one of the list.</summary>
        static List<int> EdTops(LevelModel m, List<int> objs)
        {
            var set = new HashSet<int>(objs);
            return objs.Where(i => { for (int up = m.Objects[i].Parent, g = 0; up >= 0 && g < 1024; up = m.Objects[up].Parent, g++) if (set.Contains(up)) return false; return true; }).ToList();
        }

        // ---- history

        /// <summary>Redoes the next steps of a level's changes (a step made on several levels together is redone on all of them).</summary>
        public string Redo(int level, int steps)
        {
            Require();
            CheckLevelNumber(Session, level);
            if (steps < 1 || steps > 50) throw new ArgumentOutOfRangeException(null, "Redo 1-50 steps.");
            var e = Edits.Existing(level);
            if (e == null || !e.CanRedo) throw new McpEditorException("Level " + level + " has nothing to redo.");
            var labels = new List<string>();
            for (int i = 0; i < steps && e.CanRedo; i++)
            {
                string label = e.RedoLabel, refused;
                var done = Edits.Redo(level, out refused);
                if (refused != null) { if (labels.Count == 0) throw new McpEditorException(refused); labels.Add("(stopped: " + refused + ")"); break; }
                labels.Add(label + (done.Count > 1 ? " (levels " + string.Join(", ", done) + ")" : ""));
            }
            return "Redid on level " + level + ": " + string.Join("; ", labels) + ".";
        }

        /// <summary>Edit ▸ Undo history: a level's steps, the next undo first, and the steps to redo.</summary>
        public string UndoHistory(int level)
        {
            Require();
            CheckLevelNumber(Session, level);
            var e = Edits.Existing(level);
            if (e == null || (!e.CanUndo && !e.CanRedo)) return "Level " + level + " has no changes in its history.";
            var b = new StringBuilder();
            var u = e.UndoLabels;
            b.Append("Level ").Append(level).Append(e.Dirty ? " (unsaved changes)" : "").Append(": ").Append(u.Count).Append(" step").Append(u.Count == 1 ? "" : "s").Append(" to undo, the next first:\n");
            for (int i = u.Count - 1, n = 1; i >= 0; i--, n++) b.Append("  ").Append(n).Append(". ").Append(u[i]).Append('\n');
            var r = e.RedoLabels;
            if (r.Count > 0)
            {
                b.Append(r.Count).Append(" step").Append(r.Count == 1 ? "" : "s").Append(" to redo, the next first:\n");
                for (int i = r.Count - 1, n = 1; i >= 0; i--, n++) b.Append("  ").Append(n).Append(". ").Append(r[i]).Append('\n');
            }
            return b.ToString();
        }

        // ---- floors

        /// <summary>Raises (steps &gt; 0) or lowers the floor of a tile or a rectangle, the objects lying there moving with it (Ctrl+wheel). One undo step.</summary>
        public string FloorRaise(int level, int x1, int y1, int? x2, int? y2, int steps)
        {
            var e = Editable(level);
            var tiles = EdRect(x1, y1, x2, y2);
            if (steps == 0 || steps < -15 || steps > 15) throw new ArgumentOutOfRangeException(null, "Steps are -15 to 15, not 0 (8 z units a step).");
            int changed = 0, solid = 0;
            e.Batch((steps > 0 ? "Raise" : "Lower") + " the floor of " + EdRectText(x1, y1, x2, y2), () =>
            {
                foreach (var t in tiles)
                {
                    if (e.Model.Tiles[t.X, t.Y].IsSolid) { solid++; continue; }
                    if (e.RaiseFloor(t.X, t.Y, steps)) changed++;
                }
            });
            if (changed == 0) return "Nothing changed: " + (solid == tiles.Count ? "only solid rock there." : "the floor there is already at " + (steps > 0 ? "15" : "0") + ".");
            return (steps > 0 ? "Raised" : "Lowered") + " the floor of " + changed + " tile" + (changed == 1 ? "" : "s") + " in " + EdRectText(x1, y1, x2, y2) + " by up to " + Math.Abs(steps) + " (kept to 0-15)" + (solid > 0 ? "; " + solid + " solid left alone" : "") + ".";
        }

        /// <summary>Puts the objects lying on a tile or rectangle on its floor (things placed at a height on purpose, 0x160 on, stay). One undo step.</summary>
        public string DropToFloor(int level, int x1, int y1, int? x2, int? y2)
        {
            var e = Editable(level);
            var tiles = EdRect(x1, y1, x2, y2);
            int moved = 0;
            e.Batch("Objects of " + EdRectText(x1, y1, x2, y2) + " to the floor", () => { foreach (var t in tiles) moved += e.DropToFloor(t.X, t.Y); });
            return moved == 0 ? "Nothing to move: the objects there lie on the floor already (or are placed high on purpose)." : "Put " + moved + " object" + (moved == 1 ? "" : "s") + " on the floor in " + EdRectText(x1, y1, x2, y2) + ".";
        }

        // ---- objects: copy, cut, paste, duplicate

        ObjectClip edObjectClip;

        /// <summary>Edit ▸ Copy (and Cut): the objects with everything inside them, kept for objects_paste.</summary>
        public string ObjectsCopy(int level, string indexes, bool cut)
        {
            var e = cut ? Editable(level) : Level(level);
            if (!e.Model.Present) throw new McpEditorException("Level " + level + " is an empty slot.");
            var list = EdIndexes(indexes);
            foreach (int i in list) Index(e, i);
            var clip = e.Copy(list);
            if (clip.Roots.Count == 0) throw new ArgumentException("Nothing to copy: those objects are not in the level (on a tile or inside something).");
            edObjectClip = clip;
            string what = clip.Describe(Session.Names);
            if (!cut) return "Copied " + what + " (" + clip.Total + " object" + (clip.Total == 1 ? "" : "s") + " with what they hold) from level " + level + ". objects_paste puts them on a tile.";
            int n = EdDeleteMany(e, list, "Cut " + what);
            return "Cut " + what + ": " + n + " object" + (n == 1 ? "" : "s") + " deleted (one undo step), kept for objects_paste." + edTextNote;
        }

        /// <summary>Deletes several objects (an object inside another one deleted goes with it) as one undo step; frees the texts no object reads any more.</summary>
        int EdDeleteMany(LevelEditor e, List<int> objs, string label)
        {
            var tops = EdTops(e.Model, objs);
            List<TextSlot> texts = null;
            var st = EdStrings();
            if (st != null) { try { texts = ObjectTextEdits.TextsUnder(e.Model, tops, st); } catch (Exception) { texts = null; } }
            int done = 0;
            EdWithTexts(() => e.Batch(label, () =>
            {
                foreach (int i in tops)
                {
                    if (e.Model.Objects[i].Free) continue;
                    e.DeleteObject(i);
                    done++;
                }
            }), null, s => texts == null ? EdNoTexts : ObjectTextEdits.FreeUnused(Edits, s, texts));
            return done;
        }

        /// <summary>Deletes several objects with everything inside them, one undo step.</summary>
        public string ObjectsDelete(int level, string indexes)
        {
            var e = Editable(level);
            var list = EdIndexes(indexes);
            foreach (int i in list) Index(e, i);
            int n = EdDeleteMany(e, list, "Delete " + list.Count + " object" + (list.Count == 1 ? "" : "s"));
            return "Deleted " + n + " object" + (n == 1 ? "" : "s") + " (with what they held) on level " + level + ", one undo step." + edTextNote;
        }

        /// <summary>Edit ▸ Paste: the copied objects on a tile (the others where they lay relative to the first), or inside a container.</summary>
        public string ObjectsPaste(int level, int? x, int? y, int? container)
        {
            var e = Editable(level);
            if (edObjectClip == null) throw new McpEditorException("Nothing to paste: copy objects first (objects_copy).");
            int px, py, into = -1;
            if (container.HasValue)
            {
                if (x.HasValue || y.HasValue) throw new ArgumentException("Give a tile (x and y) or a container, not both.");
                into = Index(e, container.Value);
                var c = e.Model.Objects[into];
                px = Math.Max(0, c.TileX); py = Math.Max(0, c.TileY);
            }
            else
            {
                if (!x.HasValue || !y.HasValue) throw new ArgumentException("Give the tile (x and y) to paste on, or a container.");
                CheckTile(x.Value, y.Value);
                px = x.Value; py = y.Value;
            }
            var clip = edObjectClip;
            List<int> made = null;
            EdWithTexts(() => made = e.Paste(clip, px, py, into), s => ObjectTextEdits.GiveCopiesOwnTexts(Edits, level, made, s, out _), null);
            string said = "Pasted " + clip.Describe(Session.Names) + (into >= 0 ? " inside #" + into : " on (" + px + ", " + py + ")") + " of level " + level + ": " + string.Join(", ", made.Select(i => "#" + i)) + ".";
            if (clip.HasTalker) said += " A creature with a conversation was copied: the game finds talkers by their conversation number, so two creatures now share it.";
            return said + edTextNote;
        }

        /// <summary>Edit ▸ Duplicate: copies of the objects on the same tiles (or in the same container), one undo step.</summary>
        public string ObjectsDuplicate(int level, string indexes)
        {
            var e = Editable(level);
            var list = EdIndexes(indexes);
            foreach (int i in list) Index(e, i);
            var first = e.Model.Objects[list[0]];
            var clip = e.Copy(list);
            if (clip.Roots.Count == 0) throw new ArgumentException("Nothing to duplicate: those objects are not in the level.");
            int into = first.OnTile ? -1 : first.Parent;
            if (!first.OnTile && into < 0) throw new ArgumentException("#" + list[0] + " is not in the level.");
            int px = Math.Max(0, first.TileX), py = Math.Max(0, first.TileY);
            List<int> made = null;
            EdWithTexts(() => made = e.Paste(clip, px, py, into), s => ObjectTextEdits.GiveCopiesOwnTexts(Edits, level, made, s, out _), null);
            string said = "Duplicated " + clip.Describe(Session.Names) + (into >= 0 ? " inside #" + into : " on (" + px + ", " + py + ")") + ": " + string.Join(", ", made.Select(i => "#" + i)) + ".";
            if (clip.HasTalker) said += " A creature with a conversation was copied: two creatures now share its conversation.";
            return said + edTextNote;
        }

        /// <summary>What objects_copy holds now.</summary>
        public string ObjectsClip()
        {
            Require();
            if (edObjectClip == null) return "Nothing copied.";
            var b = new StringBuilder("Copied from level " + edObjectClip.FromLevel + ": " + edObjectClip.Describe(Session.Names) + ", " + edObjectClip.Total + " objects in all (" + edObjectClip.Count(true) + " mobile, " + edObjectClip.Count(false) + " static).\n");
            foreach (var r in edObjectClip.Roots) b.Append("  ").Append(Hex(r.Item)).Append(' ').Append(Session.Names.Get(r.Item)).Append(" at +").Append(r.Dx).Append(", +").Append(r.Dy).Append(r.Children.Count > 0 ? ", holding " + r.Children.Count : "").Append('\n');
            return b.ToString();
        }

        /// <summary>Takes an object out of a container, a creature or a trigger's chain onto a tile (or moves one lying on a tile).</summary>
        public string ObjectTakeOut(int level, int index, int x, int y, int? fineX, int? fineY)
        {
            var e = Editable(level);
            Index(e, index);
            CheckTile(x, y);
            if (fineX.HasValue) Range(fineX.Value, 0, 7, "fine x");
            if (fineY.HasValue) Range(fineY.Value, 0, 7, "fine y");
            var o = e.Model.Objects[index];
            string from = o.OnTile ? "(" + o.TileX + ", " + o.TileY + ")" : o.Parent >= 0 ? "#" + o.Parent + " " + e.Model.Objects[o.Parent].Name : "nowhere";
            e.MoveObjectAnywhere(index, x, y, fineX, fineY);
            return "Moved #" + index + " " + o.Name + " from " + from + " to (" + x + ", " + y + ") of level " + level + ".";
        }

        // ---- locks and walls (the right-click menu's typed commands)

        /// <summary>Takes a door's or a container's lock away.</summary>
        public string LockRemove(int level, int holder)
        {
            var e = Editable(level);
            Index(e, holder);
            string name = e.Model.Objects[holder].Name;
            if (!e.RemoveLock(holder)) throw new ArgumentException("#" + holder + " " + name + " has no lock.");
            return "#" + holder + " " + name + " has no lock now.";
        }

        /// <summary>Gives a door or a container a copy of another's lock (the one it had goes).</summary>
        public string LockCopy(int level, int from, int to)
        {
            var e = Editable(level);
            Index(e, from); Index(e, to);
            int made = e.CopyLock(from, to);
            var l = e.Model.Objects[made];
            return "#" + to + " " + e.Model.Objects[to].Name + " has #" + from + "'s lock now: #" + made + ", lock number " + (l.Link & 0x1FF) + ".";
        }

        /// <summary>Puts a lever, button, writing or picture (0x160-0x17F) against the nearest wall of its tile.</summary>
        public string SnapToWall(int level, int index)
        {
            var e = Editable(level);
            Index(e, index);
            var o = e.Model.Objects[index];
            if (o.Item < 0x160 || o.Item >= 0x180) throw new ArgumentException("#" + index + " " + o.Name + " does not hang on a wall: snapping is for 0x160-0x17F (levers, buttons, writings, pictures).");
            if (!e.SnapToWall(index)) throw new ArgumentException("The tile of #" + index + " has no wall beside it.");
            var n = e.Model.Objects[index];
            return "#" + index + " " + n.Name + " is against its wall: fine " + n.FineX + "," + n.FineY + ", heading " + n.Heading + ".";
        }
    }
}
