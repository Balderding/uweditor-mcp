using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using UwEditor.Mcp;

namespace UwEditor.App
{
    /// <summary>
    /// Level ▸ Let an AI assistant work here (MCP): serves the editor's MCP tools (src/UwEditor.Mcp) on a pipe of
    /// this PC, working on the game open in this window, so the changes show as they are made and go
    /// in Undo. Closing the window stops it. An MCP client reaches it by starting UwEditorAvalonia --mcp (or UwEditor.Mcp.Server --app).
    /// </summary>
    public sealed partial class McpWindow : Window
    {
        readonly MainWindow main;
        readonly PipeHost host;
        readonly TextBlock state = new TextBlock { FontWeight = FontWeight.SemiBold };
        readonly TextBlock folder = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        readonly ObservableCollection<string> calls = new ObservableCollection<string>();
        readonly ListBox list = new ListBox();
        const int MaxCalls = 500;

        /// <summary>The calls made so far, newest last (for the tests).</summary>
        public System.Collections.Generic.IReadOnlyList<string> Calls { get { return calls; } }

        public McpWindow(MainWindow main, string pipeName = null)
        {
            this.main = main;
            Title = "Let an AI assistant work here (MCP)";
            Width = 640; Height = 520; MinWidth = 420; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var vm = main.ViewModel;
            // each call runs on this window's thread, on whatever game is open at the time; the views redraw by themselves (LevelEdits.Changed)
            var ed = McpEditor.Attached(() => vm.Session, () => vm.Edits, f => Dispatcher.UIThread.Invoke(() =>
            {
                string r = f();
                vm.RaiseEditState();
                return r;
            }), Save);
            // the data tables: the ones the editor's windows hold, so the change shows there and either side's save writes it
            Func<GameTablesWindow> gameTables = () => System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<GameTablesWindow>(main.OwnedWindows));
            ed.HostObjectTables = () => main.ObjectTablesOpen?.Tables;
            ed.HostGameTables = () => { var w = gameTables(); return w == null ? null : new UwEditor.Core.Editing.DataTable[] { w.Terrain, w.Skills, w.Combinations, w.Shades, w.MinimumLight }; };
            ed.HostStrings = () => vm.Session == null ? null : main.Strings();
            ed.HostTablesEdited = () => gameTables()?.RefreshTextures();
            ed.HostTablesSaved = files =>
            {
                if (vm.Level != null && vm.Edits != null) vm.RefreshLevel();     // COMOBJ.DAT says how objects are drawn
                if (files.Contains("SHADES.DAT") || files.Contains("DL.DAT")) main.GameLightChanged();
                gameTables()?.RefreshTextures();
            };
            AttachHooks(ed, vm);
            host = PipeHost.Create(ed);
            host.Called += c => Dispatcher.UIThread.Post(() => Add(c));
            host.ConnectionChanged += on => Dispatcher.UIThread.Post(() => { ShowState(); Add(on ? "(the assistant connected)" : "(the assistant left)"); });
            host.Failed += why => Dispatcher.UIThread.Post(ShowState);
            // the folder line follows the game open in the editor
            System.ComponentModel.PropertyChangedEventHandler follow = (o, a) => { if (a.PropertyName == nameof(MainViewModel.Session) || string.IsNullOrEmpty(a.PropertyName)) ShowState(); };
            vm.PropertyChanged += follow;
            Closed += (o, a) => vm.PropertyChanged -= follow;
            host.Begin(pipeName ?? PipeHost.DefaultPipe);

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(state);
            head.Children.Add(folder);
            head.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                Text = "An AI assistant (Claude, or any other MCP client) edits the game open in the editor: each change shows in the views as it is made and goes in Undo, so you can undo it. "
                     + "The view stays where you put it. Nothing is written to the game's files until a save (the assistant's save tool, or yours). "
                     + "The assistant is set up once to start the editor as UwEditorAvalonia --mcp (Help > Manual, An AI assistant at work); it may be started before or after this window, and reaches it through the pipe " + (pipeName ?? PipeHost.DefaultPipe) + ", on this PC only.",
            });
            DockPanel.SetDock(head, Dock.Top);

            var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var stop = new Button { Content = "Stop", Padding = new Thickness(14, 3) };
            ToolTip.SetTip(stop, "Stops serving the tools and closes this window. The changes made stay (undo them as any other).");
            stop.Click += (o, e) => Close();
            DockPanel.SetDock(stop, Dock.Right);
            foot.Children.Add(stop);
            DockPanel.SetDock(foot, Dock.Bottom);

            list.ItemsSource = calls;
            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(head); dock.Children.Add(foot);
            dock.Children.Add(list);
            Content = dock;

            ShowState();
            Closed += (o, e) => host.Dispose();
        }

        /// <summary>The assistant's save: the editor's own (Ctrl+S), so its checks, status line and markers follow.</summary>
        string Save()
        {
            // McpEditor has checked the levels first (the refusal the assistant should hear); what is left
            // to fail is the writing itself, which the editor reports to the user
            var levels = System.Linq.Enumerable.ToList(main.ViewModel.Edits.DirtyLevels);
            if (!main.SaveAll()) throw new InvalidOperationException("The editor did not save: " + (string.IsNullOrEmpty(main.ViewModel.EditStatus) ? "it showed the reason to the user." : main.ViewModel.EditStatus));
            return (levels.Count == 1 ? "Saved level " : "Saved levels ") + string.Join(", ", levels) + ", as Ctrl+S does (with the editor's other unsaved work).";
        }

        void ShowState()
        {
            state.Text = host.Problem != null ? host.Problem : host.Connected ? "The assistant is connected." : "Waiting for the assistant…";
            var s = main.ViewModel.Session;
            folder.Text = s == null ? "No game is open." : "Working on " + s.GameFolder;
        }

        void Add(string call)
        {
            calls.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + call);
            while (calls.Count > MaxCalls) calls.RemoveAt(0);
            list.ScrollIntoView(calls.Count - 1);
            ShowState();
        }
    }
}
