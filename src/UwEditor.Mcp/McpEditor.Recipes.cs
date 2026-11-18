using System;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>The editor's trap recipes (TrapRecipes): teleports, a lever and its door, plates, spawners.</summary>
    public sealed partial class McpEditor
    {
        static string Said(RecipeResult r)
        {
            string s = r.Summary ?? "Done.";
            if (r.Made.Count > 0) s += " Made " + string.Join(", ", r.Made.Select(i => "#" + i)) + " on level " + r.Level + ".";
            if (r.Back != null) s += " Back: " + Said(r.Back);
            return s;
        }

        /// <summary>Stepping on (fromX, fromY) sends the player to (toX, toY) of toLevel; with back, a pair leading back.</summary>
        public string RecipeTeleport(int level, int fromX, int fromY, int toLevel, int toX, int toY, bool back,
            int? backFromX = null, int? backFromY = null, int? backToX = null, int? backToY = null)
        {
            var from = Editable(level);
            var to = Editable(toLevel);
            CheckTile(fromX, fromY); CheckTile(toX, toY);
            var o = new TeleportOptions { FromX = fromX, FromY = fromY, ToX = toX, ToY = toY, Back = back };
            if (back)
            {
                // the way back starts beside the arrival and arrives beside the start, unless given:
                // on the squares themselves the player would be sent straight back
                var bf = backFromX.HasValue || backFromY.HasValue ? Pair(backFromX, backFromY, "backFrom") : Beside(to.Model, toX, toY, "the arrival");
                var bt = backToX.HasValue || backToY.HasValue ? Pair(backToX, backToY, "backTo") : Beside(from.Model, fromX, fromY, "the start");
                o.BackFromX = bf.Item1; o.BackFromY = bf.Item2; o.BackToX = bt.Item1; o.BackToY = bt.Item2;
            }
            else if (backFromX.HasValue || backFromY.HasValue || backToX.HasValue || backToY.HasValue)
                throw new ArgumentException("The way back's squares go with back.");
            return Said(TrapRecipes.Teleport(from, to, o));
        }

        static Tuple<int, int> Pair(int? x, int? y, string what)
        {
            if (!x.HasValue || !y.HasValue) throw new ArgumentException("Give both " + what + "X and " + what + "Y.");
            CheckTile(x.Value, y.Value);
            return Tuple.Create(x.Value, y.Value);
        }

        /// <summary>An open square next to (x, y) (north, east, south, west), one without objects first.</summary>
        static Tuple<int, int> Beside(UwEditor.Core.LevelModel m, int x, int y, string what)
        {
            var near = new[] { Tuple.Create(x, y + 1), Tuple.Create(x + 1, y), Tuple.Create(x, y - 1), Tuple.Create(x - 1, y) }
                .Where(t => t.Item1 >= 0 && t.Item1 < 64 && t.Item2 >= 0 && t.Item2 < 64 && m.Tiles[t.Item1, t.Item2].Type == UwEditor.Core.TileType.Open)
                .OrderBy(t => m.TileObjects(t.Item1, t.Item2).Count).ToList();
            if (near.Count == 0) throw new ArgumentException("No open square beside " + what + " for the way back: give backFromX/Y and backToX/Y.");
            return near[0];
        }

        static WallSide Side(string side)
        {
            if (string.IsNullOrWhiteSpace(side)) return WallSide.Auto;
            WallSide w;
            if (!int.TryParse(side.Trim(), out _) && Enum.TryParse(side.Trim(), true, out w) && Enum.IsDefined(w)) return w;
            throw new ArgumentException("A side is auto, north, east, south or west.");
        }

        /// <summary>A new lever on a wall of (wallX, wallY) that toggles the door.</summary>
        public string RecipeLeverDoor(int level, int doorIndex, int wallX, int wallY, string side)
        {
            var e = Editable(level);
            Index(e, doorIndex);
            CheckTile(wallX, wallY);
            return Said(TrapRecipes.LeverDoor(e, new LeverDoorOptions { Door = doorIndex, WallX = wallX, WallY = wallY, Side = Side(side) }));
        }

        /// <summary>Stepping on (x, y) runs a trap: damage (an amount) or a door (toggled).</summary>
        public string RecipePlate(int level, int x, int y, string kind, int? damage, int? doorIndex)
        {
            var e = Editable(level);
            CheckTile(x, y);
            var a = new TrapAction();
            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "damage":
                    a.Kind = RecipeTrap.Damage;
                    if (damage.HasValue) a.Damage = Range(damage.Value, 1, 63, "damage");
                    break;
                case "door":
                    a.Kind = RecipeTrap.Door;
                    if (!doorIndex.HasValue) throw new ArgumentException("A door plate needs the door's index.");
                    a.Door = Index(e, doorIndex.Value);
                    break;
                default: throw new ArgumentException("A plate's trap is damage or door.");
            }
            return Said(TrapRecipes.Plate(e, x, y, false, a));
        }

        /// <summary>Wandering monsters: a spawner on (x, y) making the creature with the chance (percent) a run.</summary>
        public string RecipeSpawner(int level, int x, int y, string creature, double chance)
        {
            var e = Editable(level);
            CheckTile(x, y);
            if (chance <= 0 || chance > 100) throw new ArgumentOutOfRangeException(null, "The chance is above 0, up to 100 (percent).");
            return Said(TrapRecipes.Spawner(e, new SpawnerOptions { X = x, Y = y, Item = ItemOf(creature), Chance = chance }));
        }
    }
}
