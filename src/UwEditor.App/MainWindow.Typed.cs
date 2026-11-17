using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>The right-click menu's commands by kind of object: locks on doors and containers, and snapping wall objects to their wall.</summary>
    public partial class MainWindow
    {
        IEnumerable<Control> TypedItems(ObjectInfo o)
        {
            var e = vm.Editor;
            if (e == null) yield break;
            var cat = ItemCategories.Of(o.Item, vm.Session.Kind);
            int idx = o.Index;
            if ((cat & (ItemCategory.Door | ItemCategory.Container)) != 0)
            {
                bool has = e.LockOf(idx) != null;
                if (!has) yield return Item("Add a _lock…", () => { _ = AskAddLock(idx); });
                yield return Item(has ? "Replace its lock with another's: click that door or container…" : "Copy another's lock: click that door or container…", () => StartCopyLockPick(idx));
                if (has) yield return Item("Remove its lock", () => RemoveLock(idx));
            }
            if (o.OnTile && o.Item >= 0x160 && o.Item < 0x180) yield return Item("Snap to the nearest _wall", () => SnapToWall(idx));
        }

        async Task AskAddLock(int holder)
        {
            string t = await TextDialog.Ask(this, "A lock for #" + holder + " " + vm.Level.Objects[holder].Name,
                "Type the lock's number (0-511): the keys whose \"Opens lock number\" is the same open it. The lock is added locked; its difficulty and flags are in the panel once it is there.", "1");
            if (t == null) return;
            int n;
            if (!MainViewModel.TryParse(t.Trim().Split('\n')[0].Trim(), out n)) { vm.EditStatus = "'" + t.Trim() + "' is not a number."; return; }
            AddLock(holder, n);
        }

        /// <summary>Puts a locked lock of this number on a door or container and selects it. True when added.</summary>
        public bool AddLock(int holder, int number)
        {
            int made = -1;
            if (!vm.Edit(ed => made = ed.AddLock(holder, number), "#" + holder + " " + vm.Level.Objects[holder].Name + " has lock " + number + " now, locked. A key with \"Opens lock number\" " + number + " opens it.")) return false;
            vm.FlushRefresh();
            vm.Select(made, -1, -1, true);
            return true;
        }

        public bool RemoveLock(int holder)
        {
            bool had = false;
            if (!vm.Edit(ed => had = ed.RemoveLock(holder))) return false;
            vm.FlushRefresh();
            vm.Select(holder, -1, -1, true);
            vm.EditStatus = had ? "The lock of #" + holder + " is removed." : "#" + holder + " has no lock.";
            return had;
        }

        /// <summary>Gives a door or container a copy of another's lock. True when copied.</summary>
        public bool CopyLock(int from, int to)
        {
            int made = -1;
            if (!vm.Edit(ed => made = ed.CopyLock(from, to), "#" + to + " " + vm.Level.Objects[to].Name + " has a copy of the lock of #" + from + " now.")) return false;
            vm.FlushRefresh();
            vm.Select(to, -1, -1, true);
            return true;
        }

        void StartCopyLockPick(int to)
        {
            objectPick = new ObjectPick { Kind = "copylock", Index = to };
            vm.EditStatus = "Click the door or container whose lock #" + to + " should get a copy of (Esc cancels).";
        }

        /// <summary>Puts a wall object against the nearest wall of its tile, facing into it. True when its tile has a wall.</summary>
        public bool SnapToWall(int index)
        {
            bool ok = false;
            if (!vm.Edit(ed => ok = ed.SnapToWall(index))) return false;
            vm.FlushRefresh();
            vm.Select(index, -1, -1, true);
            vm.EditStatus = ok ? "#" + index + " " + vm.Level.Objects[index].Name + " is against the nearest wall of its tile, facing into it." : "Its tile has no wall beside it: nothing to snap to.";
            UpdateMarkers();
            return ok;
        }
    }
}
