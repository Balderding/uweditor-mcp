using Avalonia.Interactivity;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>Tools ▸ Preview changed terrain: the view shows a changed copy of the level; the level itself stays as it is.</summary>
    public partial class MainWindow
    {
        TerrainPreviewWindow terrainPreviewWindow;
        /// <summary>The changed copy the view shows instead of the level (null: the level itself).</summary>
        LevelModel previewLevel;

        /// <summary>The level the view draws: the terrain preview's copy while one is shown, else the level being edited.</summary>
        public LevelModel ShownLevel { get { return previewLevel ?? vm.Level; } }

        void OnTerrainPreview(object sender, RoutedEventArgs e) { ShowTerrainPreview(); }

        public TerrainPreviewWindow ShowTerrainPreview()
        {
            if (vm.Session == null || vm.Level == null) return null;
            if (terrainPreviewWindow != null) { terrainPreviewWindow.Activate(); return terrainPreviewWindow; }
            vm.FlushRefresh();
            var w = new TerrainPreviewWindow(this);
            terrainPreviewWindow = w;
            w.Closed += (s, e) => { if (terrainPreviewWindow == w) terrainPreviewWindow = null; };
            w.Show(this);
            return w;
        }

        /// <summary>Shows a changed copy of the level in the view (null: the level itself again). Nothing is edited.</summary>
        public void SetTerrainPreview(LevelModel copy, int changes)
        {
            if (vm.Level == null || (copy == null && previewLevel == null)) return;
            previewLevel = copy;
            vm.PreviewStatus = copy == null ? "" : string.Format("Preview: {0} terrain change{1} applied; the level itself is unchanged. The first edit ends the preview.", changes, changes == 1 ? "" : "s");
            if (copy == null) vm.EditStatus = "Preview ended: the view shows the level itself.";
            scene = LevelScene.Update(scene, ShownLevel, vm.Options);
            View.UpdateScene(scene);
            UpdateGrid();
        }

        /// <summary>Another level is shown, or the level was edited: the preview is over (the caller draws the level itself).</summary>
        void DropTerrainPreview(string say)
        {
            if (previewLevel == null) return;
            previewLevel = null;
            vm.PreviewStatus = "";
            if (say != null) vm.EditStatus = say;
        }
    }
}
