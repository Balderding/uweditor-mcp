using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia.Interactivity;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Copying tiles with everything on them (Edit ▸ Copy tiles), turning and mirroring the copy,
    /// and pasting it with its south-west corner on the tile under the mouse. The copy is kept
    /// while the editor runs, across levels and games.
    /// </summary>
    public partial class MainWindow
    {
        TileClip tileClip;
        /// <summary>Whether the copy's footprint is drawn under the mouse (from a copy, turn or mirror until a paste or Esc).</summary>
        bool tileClipShown;

        public TileClip CopiedTiles { get { return tileClip; } }

        void OnCopyTiles(object sender, RoutedEventArgs e) { CopyTiles(); }
        void OnPasteTiles(object sender, RoutedEventArgs e) { PasteTiles(); }
        void OnTurnTiles(object sender, RoutedEventArgs e) { TurnTileClip(false); }
        void OnMirrorTiles(object sender, RoutedEventArgs e) { TurnTileClip(true); }

        /// <summary>Copies the chosen tiles with what is on them. True when something was copied.</summary>
        public bool CopyTiles()
        {
            var e = vm.Editor;
            var tiles = ChosenTiles();
            if (e == null || tiles.Count == 0) { vm.EditStatus = "Select tiles to copy first (Ctrl+drag a rectangle, or Ctrl+click tiles)."; return false; }
            try { tileClip = e.CopyTiles(tiles); }
            catch (Exception ex) { vm.EditStatus = ex.Message; return false; }
            tileClipShown = true;
            vm.EditStatus = "Copied " + tileClip.Describe() + ". Ctrl+Alt+V pastes with the south-west corner on the tile under the mouse; Ctrl+Alt+R turns the copy, Ctrl+Alt+M mirrors it.";
            UpdateMarkers();
            return true;
        }

        /// <summary>Turns the copied tiles a quarter clockwise, or mirrors them east to west.</summary>
        public bool TurnTileClip(bool mirror)
        {
            if (tileClip == null) { vm.EditStatus = "Nothing to turn: copy tiles first (Ctrl+Alt+C)."; return false; }
            if (mirror) tileClip.Mirror(); else tileClip.RotateClockwise();
            tileClipShown = true;
            vm.EditStatus = (mirror ? "Mirrored: " : "Turned a quarter clockwise: ") + tileClip.Describe() + ".";
            UpdateMarkers();
            return true;
        }

        /// <summary>The tile a paste of tiles puts the copy's south-west corner on: under the mouse, else the selected one.</summary>
        bool TilePasteCorner(out int x, out int y)
        {
            x = y = -1;
            if (hoverTileX >= 0 && View.IsPointerOver) { x = hoverTileX; y = hoverTileY; }
            else if (vm.SelectedTileX >= 0) { x = vm.SelectedTileX; y = vm.SelectedTileY; }
            return x >= 0;
        }

        /// <summary>Pastes the copied tiles with their south-west corner on (x, y) (−1: under the mouse, else the selected tile). Null when nothing was pasted.</summary>
        public TilePasteResult PasteTiles(int x = -1, int y = -1)
        {
            if (vm.Editor == null) return null;
            if (tileClip == null) { vm.EditStatus = "Nothing to paste: copy tiles first (Ctrl+Alt+C)."; return null; }
            if (x < 0 && !TilePasteCorner(out x, out y)) { vm.EditStatus = "Point at a tile, or select one, for the copy's south-west corner."; return null; }
            TilePasteResult res = null;
            bool pasted = false;
            // what lay on the tiles is removed: the texts only those objects read are freed; the copies' texts become their own
            var level = vm.Editor.Model;
            var under = new List<int>();
            for (int cy = 0; cy < tileClip.Height; cy++)
                for (int cx = 0; cx < tileClip.Width; cx++)
                    if (tileClip.Cells[cx, cy] != null && x + cx >= 0 && y + cy >= 0 && x + cx < 64 && y + cy < 64) under.AddRange(level.TileObjects(x + cx, y + cy).Select(o => o.Index));
            var texts = TextsUnder(under);
            string notes = "";
            WithTexts(() => pasted = vm.Edit(ed => res = ed.PasteTiles(tileClip, x, y)),
                () => { if (!pasted) return null; vm.EditStatus = ""; var m = OwnTextsFor(res.Made); notes += vm.EditStatus; return m; },
                () => { if (!pasted) return null; vm.EditStatus = ""; var f = FreeTexts(texts); notes += vm.EditStatus; return f; });
            if (!pasted) return null;
            tileClipShown = false;
            vm.FlushRefresh();
            selObjects.Clear(); selTiles.Clear();
            for (int cy = 0; cy < tileClip.Height; cy++)
                for (int cx = 0; cx < tileClip.Width; cx++)
                    if (tileClip.Cells[cx, cy] != null && x + cx >= 0 && y + cy >= 0 && x + cx < 64 && y + cy < 64) selTiles.Add(x + cx + 64 * (y + cy));
            vm.Select(-1, x, y, true);
            vm.EditStatus = string.Format("Pasted {0} at ({1}, {2}), one undo step.", res.Describe(), x, y) + notes;
            UpdateMarkers();
            return res;
        }

        /// <summary>The outline of where the copied tiles would land, while the copy is shown.</summary>
        void AddTileClipOutline(List<float> lines)
        {
            int x, y;
            if (!tileClipShown || tileClip == null || scene == null || vm.Level == null || !TilePasteCorner(out x, out y)) return;
            var c = new Vector4(1f, 0.6f, 0.15f, 1f);
            for (int cy = 0; cy < tileClip.Height; cy++)
                for (int cx = 0; cx < tileClip.Width; cx++)
                    if (tileClip.Cells[cx, cy] != null && x + cx >= 0 && y + cy >= 0 && x + cx < 64 && y + cy < 64) scene.TileOutline(lines, x + cx, y + cy, c);
        }

        /// <summary>The right-click menu's commands for the tile clipboard.</summary>
        IEnumerable<Avalonia.Controls.Control> TileClipItems(int tx, int ty)
        {
            bool editable = vm.Editor != null;
            int n = ChosenTiles().Count;
            yield return Item(n > 1 ? "Copy these " + n + " tiles" : "Copy this tile", () => { if (ChosenTiles().Count == 0) vm.Select(-1, tx, ty, true); CopyTiles(); }, editable, "Ctrl+Alt+C");
            if (tileClip != null)
            {
                yield return Item("Paste the copied tiles here (" + tileClip.Width + " × " + tileClip.Height + ")", () => PasteTiles(tx, ty), editable, "Ctrl+Alt+V");
                yield return Item("Turn the copied tiles a quarter", () => TurnTileClip(false), true, "Ctrl+Alt+R");
                yield return Item("Mirror the copied tiles", () => TurnTileClip(true), true, "Ctrl+Alt+M");
            }
        }
    }
}
