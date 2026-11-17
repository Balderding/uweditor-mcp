using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace UwEditor.App
{
    /// <summary>View ▸ Save the view as a picture: the 3D view as it is drawn, as a PNG (for a mod's page, or to show a room).</summary>
    public partial class MainWindow
    {
        async void OnSaveViewPicture(object sender, RoutedEventArgs e)
        {
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the view as a picture", SuggestedFileName = "view.png", DefaultExtension = "png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG picture") { Patterns = new[] { "*.png" } } },
            });
            string path = f?.TryGetLocalPath();
            if (path != null) await SaveViewPicture(path);
        }

        /// <summary>
        /// Saves the 3D view's next frame as a PNG: the level as drawn, with the outlines and
        /// markers on it (not the labels, which are drawn over the view). True when written.
        /// </summary>
        public Task<bool> SaveViewPicture(string path)
        {
            var done = new TaskCompletionSource<bool>();
            View.TakePicture((w, h, px) =>
            {
                try
                {
                    if (px == null) throw new InvalidOperationException("the view could not be read (" + (View.GlError ?? "no frame") + ")");
                    using (var bmp = new WriteableBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque))
                    {
                        using (var fb = bmp.Lock())
                        {
                            // the frame comes bottom row first, and its alpha is whatever the scene left: rows turned over, alpha made solid
                            var row = new byte[w * 4];
                            for (int y = 0; y < h; y++)
                            {
                                Buffer.BlockCopy(px, (h - 1 - y) * w * 4, row, 0, w * 4);
                                for (int x = 3; x < row.Length; x += 4) row[x] = 255;
                                System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w * 4);
                            }
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                        bmp.Save(path);
                    }
                    vm.EditStatus = "The view is saved as " + path + " (" + w + " × " + h + ").";
                    done.TrySetResult(true);
                }
                catch (Exception ex) { vm.EditStatus = "The picture was not saved: " + ex.Message; done.TrySetResult(false); }
            });
            return done.Task;
        }

        /// <summary>Points the view at a place: the middle looked at (tile x, height, tile y), from how far, turned (yaw) and tilted (pitch) in degrees.</summary>
        public void LookAt(float x, float height, float y, float distance, float yaw, float pitch)
        {
            if (View.FirstPerson) View.SetFirstPerson(false);
            View.Camera.Target = new Vector3(x, height, y);
            View.Camera.Distance = distance;
            View.Camera.Yaw = yaw;
            View.Camera.Pitch = pitch;
            View.Redraw();
        }
    }
}
