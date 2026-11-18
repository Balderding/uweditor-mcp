using System;
using System.Linq;
using System.Text;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.Mcp
{
    /// <summary>Every trap recipe of Edit ▸ Trap recipes with all its settings (the four short recipe_ tools take the usual ones).</summary>
    public sealed partial class McpEditor
    {
        /// <summary>The recipes the editor has, with their explanations.</summary>
        public string RecipeList()
        {
            var b = new StringBuilder();
            foreach (var r in TrapRecipes.Recipes) b.Append(r.Name).Append(": ").Append(r.Explanation).Append('\n');
            b.Append("End of the game (UW1): stepping on a square ends the game, as level 9's (a move trigger and hack trap 0x3F).\n");
            b.Append("Tools: recipe_lever (lever opens a door, every setting), recipe_teleport, recipe_plate_trap (pressure plate, every trap), recipe_object_trigger, recipe_spawner, recipe_end_of_game; recipe_lever_door and recipe_plate are the short forms.\n");
            b.Append("Traps a plate or object trigger can run (trap=): damage, poison, message, variable, create, door, delete.\n");
            return b.ToString();
        }

        static DoorAction EdDoorAction(string action)
        {
            switch ((action ?? "toggle").Trim().ToLowerInvariant())
            {
                case "open": return DoorAction.Open;
                case "close": return DoorAction.Close;
                case "toggle": case "": return DoorAction.Toggle;
                default: throw new ArgumentException("A door action is open, close or toggle.");
            }
        }

        /// <summary>A trap action from the tools' words. sx, sy: the square it acts on by default.</summary>
        TrapAction EdTrapAction(LevelEditor e, string trap, int? damage, string text, int? textIndex, int? variable, bool questBit, int? operation, int? value,
            string item, int? atX, int? atY, int? door, string doorAction, bool unlock, int? deleteIndex)
        {
            var a = new TrapAction();
            switch ((trap ?? "").Trim().ToLowerInvariant())
            {
                case "damage":
                    a.Kind = RecipeTrap.Damage;
                    if (damage.HasValue) a.Damage = Range(damage.Value, 1, 63, "damage");
                    break;
                case "poison":
                    a.Kind = RecipeTrap.Damage; a.Poison = true;
                    a.Damage = damage.HasValue ? Range(damage.Value, 1, 15, "poison strength") : 3;
                    break;
                case "message":
                    a.Kind = RecipeTrap.Message;
                    if (textIndex.HasValue) a.TextIndex = Range(textIndex.Value, 0, 2047, "string of block 9");
                    else if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A message trap needs text (a new string of block 9) or textIndex (one already there).");
                    else a.Text = text;
                    break;
                case "variable":
                    a.Kind = RecipeTrap.SetVariable;
                    if (!variable.HasValue) throw new ArgumentException("A variable trap needs the variable (UW1 1-63, or a quest bit 0-31 with questBit; UW2 0-0x19F).");
                    a.Variable = variable.Value; a.QuestBit = questBit;
                    if (operation.HasValue) a.Operation = operation.Value;
                    if (value.HasValue) a.Value = value.Value;
                    break;
                case "create":
                    a.Kind = RecipeTrap.CreateObject;
                    if (string.IsNullOrWhiteSpace(item)) throw new ArgumentException("A create trap needs the item it makes.");
                    a.Template = ObjectSpec.Default(ItemOf(item), Session.Kind);
                    if (atX.HasValue || atY.HasValue) { var p = Pair(atX, atY, "at"); a.X = p.Item1; a.Y = p.Item2; }
                    break;
                case "door":
                    a.Kind = RecipeTrap.Door;
                    if (!door.HasValue) throw new ArgumentException("A door trap needs the door's index.");
                    a.Door = Index(e, door.Value);
                    a.DoorAction = EdDoorAction(doorAction);
                    a.Unlock = unlock;
                    break;
                case "delete":
                    a.Kind = RecipeTrap.DeleteObject;
                    if (!deleteIndex.HasValue) throw new ArgumentException("A delete trap needs the index of the object it removes.");
                    a.Delete = Index(e, deleteIndex.Value);
                    break;
                default: throw new ArgumentException("A trap is damage, poison, message, variable, create, door or delete.");
            }
            return a;
        }

        StringsEditor EdRecipeStrings(TrapAction a)
        {
            if (a.Kind != RecipeTrap.Message || a.TextIndex >= 0) return null;
            var st = TblStrings();
            return st;
        }

        string EdRecipeSaid(RecipeResult r, TrapAction a)
        {
            string s = Said(r);
            if (a != null && a.Kind == RecipeTrap.Message && a.TextIndex < 0 && r.StringIndex >= 0)
            {
                HostTablesEdited?.Invoke();
                s += " The message is new string " + r.StringIndex + " of block 9 (STRINGS.PAK, written by save; undo takes the trap back, not the string).";
            }
            return s;
        }

        /// <summary>Stepping on a square (a move trigger, or a UW2 pressure plate) runs a trap of any kind.</summary>
        public string RecipePlateTrap(int level, int x, int y, string trap, bool pressure, bool reusable, bool creatures,
            int? damage, string text, int? textIndex, int? variable, bool questBit, int? operation, int? value,
            string item, int? atX, int? atY, int? door, string doorAction, bool unlock, int? deleteIndex)
        {
            var e = Editable(level);
            CheckTile(x, y);
            var a = EdTrapAction(e, trap, damage, text, textIndex, variable, questBit, operation, value, item, atX, atY, door, doorAction, unlock, deleteIndex);
            var r = TrapRecipes.Plate(e, x, y, pressure, a, new TriggerOptions { Reusable = reusable, Creatures = creatures }, EdRecipeStrings(a));
            return EdRecipeSaid(r, a);
        }

        /// <summary>Picking up, using or looking at an object runs a trap (the trigger goes inside the object).</summary>
        public string RecipeObjectTrigger(int level, int index, string when, string trap, bool reusable, bool creatures,
            int? damage, string text, int? textIndex, int? variable, bool questBit, int? operation, int? value,
            string item, int? atX, int? atY, int? door, string doorAction, bool unlock, int? deleteIndex)
        {
            var e = Editable(level);
            Index(e, index);
            ObjectEvent ev;
            switch ((when ?? "use").Trim().ToLowerInvariant().Replace(" ", ""))
            {
                case "pickup": case "pick": ev = ObjectEvent.PickUp; break;
                case "use": ev = ObjectEvent.Use; break;
                case "look": ev = ObjectEvent.Look; break;
                default: throw new ArgumentException("When is pickup, use or look.");
            }
            var a = EdTrapAction(e, trap, damage, text, textIndex, variable, questBit, operation, value, item, atX, atY, door, doorAction, unlock, deleteIndex);
            var r = TrapRecipes.ObjectTrigger(e, index, ev, a, new TriggerOptions { Reusable = reusable, Creatures = creatures }, EdRecipeStrings(a));
            return EdRecipeSaid(r, a);
        }

        /// <summary>A lever (one in the level, or a new one on a wall) that opens, closes or toggles a door, with every setting.</summary>
        public string RecipeLever(int level, int doorIndex, int? lever, int? wallX, int? wallY, string side, string leverItem, string action, bool unlock, bool reusable, bool creatures)
        {
            var e = Editable(level);
            Index(e, doorIndex);
            var o = new LeverDoorOptions { Door = doorIndex, Action = EdDoorAction(action), Unlock = unlock, Trigger = new TriggerOptions { Reusable = reusable, Creatures = creatures } };
            if (lever.HasValue)
            {
                if (wallX.HasValue || wallY.HasValue) throw new ArgumentException("Give a lever in the level or a tile for a new one, not both.");
                o.Lever = Index(e, lever.Value);
            }
            else
            {
                var p = Pair(wallX, wallY, "wall");
                o.WallX = p.Item1; o.WallY = p.Item2; o.Side = Side(side);
                if (!string.IsNullOrWhiteSpace(leverItem)) o.LeverItem = ItemOf(leverItem);
            }
            return Said(TrapRecipes.LeverDoor(e, o));
        }

        /// <summary>UW1: stepping on a square ends the game.</summary>
        public string RecipeEndOfGame(int level, int x, int y)
        {
            var e = Editable(level);
            CheckTile(x, y);
            return Said(TrapRecipes.EndOfGame(e, x, y));
        }

        /// <summary>The switches a new lever can be.</summary>
        public string LeverItems()
        {
            Require();
            return string.Join("\n", TrapRecipes.LeverItems().Select(i => Hex(i) + " " + Session.Names.Get(i))) + "\n";
        }
    }
}
