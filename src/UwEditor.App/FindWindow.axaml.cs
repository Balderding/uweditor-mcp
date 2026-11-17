using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>One result line: the object's picture, where it is, and what matched.</summary>
    public sealed class HitRow : Observable
    {
        public SearchHit Hit { get; set; }
        Bitmap icon;
        public Bitmap Icon { get { return icon; } set { Set(ref icon, value); } }
        public string Title { get; set; }
        public string Detail { get; set; }
        public bool HasDetail { get { return !string.IsNullOrEmpty(Detail); } }
    }

    public sealed class FindViewModel : Observable
    {
        string text = "", status = "Search every level of the open game.";
        int kindIndex;
        bool busy;
        HitRow selected;

        public string Text { get { return text; } set { Set(ref text, value ?? ""); } }
        public List<string> Kinds { get; } = ItemCategories.Choices.Select(c => c.Value).ToList();
        public int KindIndex { get { return kindIndex; } set { Set(ref kindIndex, value); } }
        /// <summary>The old two-way box: ticked is Enchanted = true, clear is either.</summary>
        public bool EnchantedOnly { get { return Enchanted == true; } set { Enchanted = value ? true : (bool?)null; } }
        bool? fEnchanted, fInvisible, fQuantity, fOwned, fInside;
        decimal? qMin = 0, qMax = 63;
        bool thisLevel;
        int replaceMode, replaceField;
        string replaceItem = "", replaceValue = "";
        public bool? Enchanted { get { return fEnchanted; } set { if (Set(ref fEnchanted, value)) Raise(nameof(EnchantedOnly)); } }
        public bool? Invisible { get { return fInvisible; } set { Set(ref fInvisible, value); } }
        public bool? IsQuantity { get { return fQuantity; } set { Set(ref fQuantity, value); } }
        public bool? Owned { get { return fOwned; } set { Set(ref fOwned, value); } }
        public bool? InContainer { get { return fInside; } set { Set(ref fInside, value); } }
        public decimal? QualityMin { get { return qMin; } set { Set(ref qMin, value); } }
        public decimal? QualityMax { get { return qMax; } set { Set(ref qMax, value); } }
        public bool ThisLevelOnly { get { return thisLevel; } set { Set(ref thisLevel, value); } }
        /// <summary>The level "This level only" means: the one shown in the main window (set by the window before a search).</summary>
        public int ShownLevel;

        // ---- replace
        /// <summary>0: the results become an item; 1: a field of theirs is set.</summary>
        public int ReplaceMode { get { return replaceMode; } set { if (Set(ref replaceMode, value)) Raise(nameof(ReplacesItem)); } }
        public bool ReplacesItem { get { return replaceMode == 0; } }
        public string ReplaceItem { get { return replaceItem; } set { Set(ref replaceItem, value ?? ""); } }
        public List<string> ReplaceFields { get; } = UwEditor.Core.Editing.LevelReplace.Fields;
        public int ReplaceFieldIndex { get { return replaceField; } set { Set(ref replaceField, value); } }
        public string ReplaceValue { get { return replaceValue; } set { Set(ref replaceValue, value ?? ""); } }
        public bool CanReplace { get { return Hits.Count > 0 && !busy; } }
        public void ResultsChanged() { Raise(nameof(CanReplace)); }
        public bool Busy { get { return busy; } set { if (Set(ref busy, value)) Raise(nameof(CanReplace)); } }
        public string Status { get { return status; } set { Set(ref status, value); } }
        public ObservableCollection<HitRow> Hits { get; } = new ObservableCollection<HitRow>();
        public HitRow Selected { get { return selected; } set { if (Set(ref selected, value)) Raise(nameof(HasSelection)); } }
        public bool HasSelection { get { return selected != null; } }

        public ObjectQuery Query()
        {
            return new ObjectQuery
            {
                Text = text,
                Categories = ItemCategories.Choices[Math.Clamp(kindIndex, 0, ItemCategories.Choices.Length - 1)].Key,
                Enchanted = fEnchanted, Invisible = fInvisible, IsQuantity = fQuantity, Owned = fOwned, InContainer = fInside,
                QualityMin = (int)(qMin ?? 0), QualityMax = (int)(qMax ?? 63),
                OnlyLevel = thisLevel ? ShownLevel : 0,
            };
        }

        /// <summary>What Replace would do, from the boxes; throws (with the reason) when they do not say.</summary>
        public UwEditor.Core.Editing.ReplaceAction Action(GameSession s)
        {
            if (replaceMode == 0)
            {
                string t = replaceItem.Trim();
                int v;
                if (t.Length == 0) throw new InvalidOperationException("Type the item the results should become: its id (0x.. or a number) or its name.");
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? int.TryParse(t.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v) : int.TryParse(t, out v))
                    return new UwEditor.Core.Editing.ReplaceAction { NewItem = v };
                var named = Enumerable.Range(0, 0x200).Where(i => string.Equals((s.Names.Get(i) ?? "").Trim(), t, StringComparison.OrdinalIgnoreCase)).ToList();
                if (named.Count == 0) named = Enumerable.Range(0, 0x200).Where(i => (s.Names.Get(i) ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (named.Count == 1) return new UwEditor.Core.Editing.ReplaceAction { NewItem = named[0] };
                throw new InvalidOperationException(named.Count == 0 ? "No item is called \"" + t + "\"." : named.Count + " items have \"" + t + "\" in their names (" + string.Join(", ", named.Take(5).Select(i => "0x" + i.ToString("X") + " " + s.Names.Get(i))) + (named.Count > 5 ? ", …" : "") + "): type the id.");
            }
            int value;
            if (!MainViewModel.TryParse(replaceValue, out value)) throw new InvalidOperationException("Type the value the field should get.");
            return new UwEditor.Core.Editing.ReplaceAction { Field = ReplaceFields[Math.Clamp(replaceField, 0, ReplaceFields.Count - 1)], Value = value };
        }
    }

    /// <summary>Tools ▸ Find in all levels: searches every level with the object list's rules
    /// and kinds; a result opens its level and selects the object.</summary>
    public partial class FindWindow : Window, IShowsItemIcons
    {
        public readonly FindViewModel Model = new FindViewModel();
        readonly MainViewModel main;
        readonly Action<SearchHit> goTo;
        public const int MaxHits = 2000;

        public FindWindow() : this(null, null) { }

        public FindWindow(MainViewModel main, Action<SearchHit> goTo)
        {
            this.main = main;
            this.goTo = goTo;
            InitializeComponent();
            ManualWindow.Attach(this, "Finding and selecting");
            DataContext = Model;
            Opened += (s, e) => QueryBox.Focus();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Results.KeyDown += (s, e) => { if (e.Key == Key.Enter) { GoToSelected(); e.Handled = true; } };
        }

        void OnSearch(object sender, RoutedEventArgs e) { _ = SearchAsync(); }

        /// <summary>A picture file was saved: the results' icons are taken again.</summary>
        public void RefreshIcons()
        {
            if (main == null) return;
            foreach (var h in Model.Hits) h.Icon = main.IconOf(h.Hit.Item);
        }

        /// <summary>Runs the search off the window's thread and fills the results.</summary>
        public async Task SearchAsync()
        {
            var session = main?.Session;
            if (session == null) { Model.Status = "Open a game first."; return; }
            Model.ShownLevel = main.Level != null ? main.Level.Level : 0;
            var q = Model.Query();
            Model.Busy = true;
            Model.Status = "Searching…";
            Model.Hits.Clear();
            List<SearchHit> hits;
            // the levels with unsaved edits are searched as edited (taken here, on the window's thread)
            var edited = new Dictionary<int, LevelModel>();
            if (main.Edits != null) foreach (var e in main.Edits.Editors) if (e.Dirty) edited[e.Level] = e.Model;
            try { hits = await Task.Run(() => LevelSearch.Run(session, q, MaxHits, l => { LevelModel m; return edited.TryGetValue(l, out m) ? m : null; })); }
            finally { Model.Busy = false; }
            foreach (var h in hits)
            {
                string detail = string.Join("  ·  ", new[] { h.Inside.Length > 0 ? "inside " + h.Inside : null, h.Matched.Length > 0 ? "“" + h.Matched + "”" : null }.Where(x => x != null));
                Model.Hits.Add(new HitRow
                {
                    Hit = h, Icon = main.IconOf(h.Item), Detail = detail,
                    Title = string.Format("{0}  ·  #{1}  {2}  ({3}, {4})", h.LevelName, h.Index, h.Name, h.TileX, h.TileY),
                });
            }
            int levels = hits.Select(h => h.Level).Distinct().Count();
            Model.Status = hits.Count == 0 ? "Nothing found." :
                string.Format("{0}{1} found on {2} level{3}. Double-click or press Enter to go there.", hits.Count >= MaxHits ? "The first " : "", hits.Count, levels, levels == 1 ? "" : "s");
            if (Model.Hits.Count > 0) Model.Selected = Model.Hits[0];
            Model.ResultsChanged();
        }

        async void OnReplace(object sender, RoutedEventArgs e)
        {
            UwEditor.Core.Editing.ReplaceAction a;
            try { a = Model.Action(main.Session); } catch (Exception ex) { Model.Status = ex.Message; return; }
            int n = Model.Hits.Count, levels = Model.Hits.Select(h => h.Hit.Level).Distinct().Count();
            string ask = await MessageWindow.Ask(this, "Replace in " + n + " object" + (n == 1 ? "" : "s") + "?",
                string.Format("The {0} object{1} in the list, on {2} level{3}, {4}. It is one undo step (Edit ▸ Undo on any of those levels takes it all back), and nothing is written until you save.",
                    n, n == 1 ? "" : "s", levels, levels == 1 ? "" : "s", a.Describe(main.Session.Names)), "Replace", "Cancel");
            if (ask == "Replace") await ReplaceAsync();
        }

        /// <summary>Replaces in the results shown, then searches again. Returns what was done, or null (the reason is in the status line).</summary>
        public async Task<UwEditor.Core.Editing.ReplaceResult> ReplaceAsync()
        {
            if (main?.Session == null || main.Edits == null || Model.Hits.Count == 0) { Model.Status = "Search first: Replace changes the objects in the list."; return null; }
            UwEditor.Core.Editing.ReplaceResult res;
            try { res = UwEditor.Core.Editing.LevelReplace.Apply(main.Edits, Model.Hits.Select(h => h.Hit).ToList(), Model.Action(main.Session)); }
            catch (Exception ex) { Model.Status = "Not replaced: " + ex.Message; return null; }
            main.FlushRefresh();
            await SearchAsync();
            Model.Status = "Replaced: " + res.Describe() + " One undo step; the list shows what the search finds now.";
            main.EditStatus = "Replaced: " + res.Describe();
            return res;
        }

        void OnGoTo(object sender, RoutedEventArgs e) { GoToSelected(); }
        void OnResultDoubleTapped(object sender, TappedEventArgs e) { GoToSelected(); }

        public void GoToSelected()
        {
            if (Model.Selected != null) goTo?.Invoke(Model.Selected.Hit);
        }
    }
}
