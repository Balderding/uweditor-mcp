using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Rows of options lined up in columns: a label column (110 wide), then cells that take the
    /// width of the widest one in their column, so the checkboxes of every row start at the same
    /// places. A row with fewer cells than columns lets its last cell span the rest.
    /// </summary>
    public sealed class OptionGrid : Grid
    {
        readonly int columns;

        public OptionGrid(int columns)
        {
            this.columns = columns;
            ColumnDefinitions.Add(new ColumnDefinition(110, GridUnitType.Pixel));
            for (int i = 0; i < columns; i++) ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        public void AddRow(string label, params Control[] cells)
        {
            int row = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (!string.IsNullOrEmpty(label))
            {
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 1) };
                SetRow(l, row);
                Children.Add(l);
            }
            for (int i = 0; i < cells.Length && i < columns; i++)
            {
                var c = cells[i];
                if (c == null) continue;
                c.VerticalAlignment = VerticalAlignment.Center;
                c.HorizontalAlignment = HorizontalAlignment.Left;
                c.Margin = new Thickness(0, 1, 20, 1);
                SetRow(c, row);
                SetColumn(c, 1 + i);
                if (i == cells.Length - 1 && cells.Length < columns) SetColumnSpan(c, columns - i + 1);
                Children.Add(c);
            }
        }
    }

    /// <summary>
    /// The automatic zones' options as rows of controls, shared by the Automatic zones window and
    /// the Apply window: the autozone movement options (climbs, gap jumps, the level cut, dead
    /// ends, the start square) when asked, and the player logic (what the solver may expect of
    /// the player), each group lined up in columns. The controls write straight into the options
    /// objects given.
    /// </summary>
    public sealed class LogicOptionsPanel : StackPanel
    {
        public NavOptions Nav { get; private set; }
        public AbilityOptions Logic { get; private set; }
        /// <summary>Raised when any option changes.</summary>
        public event Action Changed;

        public LogicOptionsPanel(NavOptions nav, AbilityOptions logic, bool movement)
        {
            Nav = nav;
            Logic = logic;
            Spacing = 2;
            if (movement)
            {
                Children.Add(Heading("AUTOZONE MOVEMENT", "how the levels' tiles become regions joined by drops, climbs and jumps, and how regions become zones"));
                var g = new OptionGrid(3);
                var tick = Num(() => nav.TickLength, 4, 64, v => nav.TickLength = v);
                ToolTip.SetTip(tick, "The time of one frame in 1/256 s, at most 64. The game works a jump out frame by frame: longer frames give lower, shorter jumps, so a larger number is more cautious. 16 is about 16 frames a second.");
                var margin = Num(() => nav.JumpMargin, 0, 8, v => nav.JumpMargin = v);
                ToolTip.SetTip(margin, "What a gap jump is asked to spare, in eighths of a tile, for a take-off that is not on the last cell of the ledge.");
                g.AddRow("Jumps", Row(Label("Tick length", 90), tick), Row(Label("Margin (1/8 tile)", 130), margin), null);
                g.AddRow("Gap jumps", Check("On", () => nav.GapJumps, v => nav.GapJumps = v), Check("Diagonals too", () => nav.DiagonalJumps, v => nav.DiagonalJumps = v), null);
                g.AddRow("Zones", Check("Cut at level changes", () => nav.CutAtLevelChanges, v => nav.CutAtLevelChanges = v),
                    Check("Fold empty dead ends (pits and ledges with nothing in them)", () => nav.FoldEmptyDeadEnds, v => nav.FoldEmptyDeadEnds = v));
                var start = Check("Consider the current starting square in the editor", () => !nav.ShippedStart, v => nav.ShippedStart = !v);
                ToolTip.SetTip(start, "When ticked, the player starts on the new-game square the executable now holds (moved with the editor's start tool). When unticked, the player starts on the game's own square, level 1 (32, 2).");
                g.AddRow("Start", start);
                Children.Add(g);
            }
            Children.Add(Heading("PLAYER LOGIC (THE SOLVER)", "what the solver may expect of the player"));
            var lg = new OptionGrid(2);
            var pickSkill = Num(() => logic.PickSkill, 0, 30, v => logic.PickSkill = v);
            var search = Num(() => logic.SearchSkill, 0, 30, v => logic.SearchSkill = v);
            lg.AddRow("Abilities", Check("Magic (casting from runes)", () => logic.Magic, v => logic.Magic = v),
                Check("Single-use items count (scrolls, potions, charged wands)", () => logic.SingleUseItems, v => logic.SingleUseItems = v));
            var circle = Num(() => Math.Min(logic.NewCharacterCircle, AbilityOptions.MaxCircle), 0, AbilityOptions.MaxCircle, v => logic.NewCharacterCircle = v);
            ToolTip.SetTip(circle, "The highest circle a spell cast from runes may have while the player has not yet left the start's free area (the zones he walks to from the start with nothing). A new character is level 1, and casting from runes asks for level 2 x circle - 1, so 1 (Light, Leap); 8 counts every circle from the start. Once he is out (by a key, a lever, an item, a spell he can cast …) every circle counts. When the way out of the start needs more, the key shuffle makes a wand (or, with single-use items counting, a scroll) of that spell within reach.");
            lg.AddRow("Runes at start", Row(Label("New character casts up to circle", 210), circle));
            lg.AddRow("Locks, NPCs", Row(Check("Lock picking in logic, skill", () => logic.LockPicking, v => logic.LockPicking = v), pickSkill),
                Check("Killing NPCs in logic (to take what they carry)", () => logic.KillNpcs, v => logic.KillNpcs = v));
            // the damage-trap tiles of the level 7 maze (the facts' area) are always walls; the option for every other one is no longer offered
            lg.AddRow("Search", Row(Label("Search skill", 90), search), Check("Retry until found", () => logic.SearchRetry, v => logic.SearchRetry = v));
            lg.AddRow("Goal, ring", Check("Ring of Levitation needs lava protection", () => logic.RingNeedsLava, v => logic.RingNeedsLava = v),
                Check("Require breaking the orb (level 7)", () => logic.RequireOrb, v => logic.RequireOrb = v));
            var blog = Check("The Pits' key gem only by Blog's way (UW2)", () => logic.PitsGemByBlog, v => logic.PitsGemByBlog = v);
            ToolTip.SetTip(blog, "Ultima Underworld 2: the key gem of the Pits of Carnage counts only when Blog the troll gets it for you. Befriend Blog on level 59 (give him a grey rock, or lose three games of White Rock Black Rock with the black rocks in his room), then speak to Dorstag, who hands the gem to Blog, and Blog gives it to you. Killing Dorstag, who carries the gem, no longer counts. Unticked, both ways count.");
            lg.AddRow("Pits gem", blog);
            var death = Check("Dying outside Britannia returns to the blackrock gem (UW2)", () => logic.DeathReturns, v => logic.DeathReturns = v);
            ToolTip.SetTip(death, "Ultima Underworld 2: on any level outside Britannia, a player with no health left wakes in the blackrock gem's cavern on castle level 5 with all he holds, a ninth of his experience lost. With this on the logic counts it as a way back to the castle from every other world (running into walls at speed is enough to die), so a place with no way out on foot is no trap and a start in a shut world can always leave. Off: no solve counts on dying.");
            lg.AddRow("Death", death);
            Children.Add(lg);
        }

        // ---- small builders, as the other randomizer windows

        public static StackPanel Row(params Control[] c)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var x in c) if (x != null) p.Children.Add(x);
            return p;
        }

        public static TextBlock Label(string t, double w = 110) { return new TextBlock { Text = t, Width = w, VerticalAlignment = VerticalAlignment.Center }; }
        public static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }; }

        public static Control Heading(string title, string what)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 2) };
            p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = what, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            return p;
        }

        /// <summary>Sets every control from the options objects again (after settings were loaded into them).</summary>
        public void Refresh() { foreach (var r in refreshers) r(); }

        readonly System.Collections.Generic.List<Action> refreshers = new System.Collections.Generic.List<Action>();

        CheckBox Check(string t, Func<bool> get, Action<bool> set)
        {
            var c = new CheckBox { Content = t, IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
            c.IsCheckedChanged += (o, e) => { set(c.IsChecked == true); Changed?.Invoke(); };
            refreshers.Add(() => c.IsChecked = get());
            return c;
        }

        NumericUpDown Num(Func<int> get, int min, int max, Action<int> set)
        {
            int v = get();
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Value = v, Width = 110 };
            n.ValueChanged += (o, e) => { set((int)(n.Value ?? v)); Changed?.Invoke(); };
            refreshers.Add(() => n.Value = get());
            return n;
        }
    }
}
