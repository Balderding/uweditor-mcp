using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>The editing group's tools: find and replace, the checks and fixes, the item cleaner, item slots, texts, jumps and hitboxes (McpEditor.EditingChecks.cs).</summary>
    public sealed partial class EditorTools
    {
        const string EdCategoryHelp = "Categories, comma separated: MeleeWeapon, Ammunition, Launcher, Armour, Shield, Ring, Crown, Creature, Container, Light, Wand, Treasure, Food, Potion, Scenery, QuestKey, Runestone, Key, Lock, Quest, MagicItem, SpellObject, Book, Scroll, Door, Furniture, Decal, Switch, Trap, Trigger, Animated, or the groups Weapon, Wearable, Keys, Model3D, Event.";

        [McpServerTool(Name = "objects_find"), Description("Level > Find in all levels: objects (on tiles and inside anything) whose name, NPC name, proper name, enchantment or text holds the text; \"0x1A2\" an item, \"#12\" an index. Narrow by categories, a level, flags and quality. The levels as edited. " + EdCategoryHelp)]
        public CallToolResult ObjectsFind([Description("A part of a name or text, 0x item or #index (empty: any).")] string text = "",
            [Description("Categories (see the description), or leave out for all.")] string categories = null,
            [Description("Only this level.")] int? level = null,
            [Description("True only enchanted, false only not.")] bool? enchanted = null, [Description("True only invisible, false only visible.")] bool? invisible = null,
            [Description("True only inside something, false only on tiles.")] bool? inContainer = null, [Description("Items: true only owned, false only not.")] bool? owned = null,
            [Description("Lowest quality 0-63.")] int qualityMin = 0, [Description("Highest quality 0-63.")] int qualityMax = 63,
            [Description("At most this many lines, 1-2000 (default 200).")] int max = 200)
        {
            return Answer(() => ed.ObjectsFind(text, categories, level, enchanted, invisible, inContainer, owned, qualityMin, qualityMax, max));
        }

        [McpServerTool(Name = "objects_replace"), Description("Find's Replace: every object objects_find finds with the same filters becomes another item (newItem), or gets one field set (field, value: a record field such as Quality, Owner, Z, Heading, Is quantity, Link, Invisible, Enchanted). Each goes through the panel's checks; refused ones stay. One undo step over all the levels touched.")]
        public CallToolResult ObjectsReplace([Description("A part of a name or text, 0x item or #index.")] string text = "",
            [Description("Categories, as objects_find.")] string categories = null, [Description("Only this level.")] int? level = null,
            [Description("True only enchanted, false only not.")] bool? enchanted = null, [Description("True only invisible, false only visible.")] bool? invisible = null,
            [Description("True only inside something, false only on tiles.")] bool? inContainer = null, [Description("Items: true only owned, false only not.")] bool? owned = null,
            [Description("Lowest quality 0-63.")] int qualityMin = 0, [Description("Highest quality 0-63.")] int qualityMax = 63,
            [Description("The item they become, by number or name.")] string newItem = null,
            [Description("Or a field to set.")] string field = null, [Description("The field's value.")] int? value = null)
        {
            return Answer(() => ed.ObjectsReplace(text, categories, level, enchanted, invisible, inContainer, owned, qualityMin, qualityMax, newItem, field, value));
        }

        [McpServerTool(Name = "world_check"), Description("Level > World check, on the levels as edited: locked locks no key in the levels opens, keys whose lock is nowhere, levels near the object limits (255 mobile, 768 static), damaged levels.")]
        public CallToolResult WorldCheck() { return Answer(() => ed.WorldCheckRun()); }

        [McpServerTool(Name = "fixes_checks"), Description("The checks of Level > Fixes: each one's id (for fixes_find and fixes_apply), title, kind (ListError: fails the game's check; Problem: the game misbehaves; Warning: maybe meant) and what it looks for.")]
        public CallToolResult FixesChecks() { return Answer(() => ed.FixesChecks()); }

        [McpServerTool(Name = "fixes_find"), Description("Level > Fixes: what the checks find in the levels as edited, numbered a level, with what each automatic fix does.")]
        public CallToolResult FixesFind([Description("\"all\" or level numbers, e.g. \"1, 3\".")] string levels = "all",
            [Description("Only this check's id (from fixes_checks).")] string check = null)
        {
            return Answer(() => ed.FixesFind(levels, check));
        }

        [McpServerTool(Name = "fixes_apply"), Description("Level > Fixes on one level, one undo step: \"all\" (Fix all, warnings too when asked), a check's id (all its findings), or finding numbers from fixes_find.")]
        public CallToolResult FixesApply([Description("Level number, from 1.")] int level, [Description("\"all\", a check's id, or finding numbers.")] string what = "all",
            [Description("With all: fix the warnings too.")] bool warnings = false)
        {
            return Answer(() => ed.FixesApply(level, what, warnings));
        }

        [McpServerTool(Name = "mod_checks"), Description("The checks run before a mod is made: the game's object list check on every level, the Fixes' problems and the world check, on the game as saved (a mod holds only what is saved), or as edited.")]
        public CallToolResult ModChecks([Description("Check the levels as edited instead of as saved.")] bool edited = false) { return Answer(() => ed.ModChecksRun(edited)); }

        [McpServerTool(Name = "item_cleaner"), Description("Level > Item cleaner: lists (or with apply removes, one undo step a level) loose items of the kinds asked in a rectangle of the levels; named, quest, guarded, conversation and trap-linked items are kept. Kinds: Debris, Bones, Junk, Plants, Food, Gear, Treasure, Potions, Lights, AllLoose.")]
        public CallToolResult ItemCleaner([Description("\"all\" or level numbers.")] string levels = "all",
            [Description("Kinds, comma separated (default Debris, Bones).")] string kinds = null,
            [Description("Also what lies in containers on the map.")] bool insideContainers = false,
            [Description("Also what creatures without a conversation carry.")] bool creatureInventories = false,
            [Description("A corner.")] int x1 = 0, [Description("A corner.")] int y1 = 0, [Description("The other corner.")] int x2 = 63, [Description("The other corner.")] int y2 = 63,
            [Description("Only items whose name holds this.")] string name = null,
            [Description("True removes them; false (default) only lists them.")] bool apply = false)
        {
            return Answer(() => ed.ItemClean(levels, kinds, insideContainers, creatureInventories, x1, y1, x2, y2, name, apply));
        }

        [McpServerTool(Name = "item_uses"), Description("Tools > Item slots for one item id: everything that uses it (objects in the levels, conversations, the game's own code, creature loot, combinations, quest items) and its class, to know whether its slot can become a new item.")]
        public CallToolResult ItemUses([Description("The item, by number or name.")] string item) { return Answer(() => ed.ItemUsesOf(item)); }

        [McpServerTool(Name = "item_slots"), Description("Tools > Item slots: every item id (weapons, armour, misc, scenery; not creatures, doors or traps) with what uses it in short, or only the free ones nothing uses. Reads every conversation: slow.")]
        public CallToolResult ItemSlots([Description("Only the free slots (default true).")] bool freeOnly = true) { return Answer(() => ed.ItemSlots(freeOnly)); }

        [McpServerTool(Name = "object_text"), Description("The text a book, scroll, gravestone, writing or text trap reads (its block and string) and how many objects share that string.")]
        public CallToolResult ObjectText([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index)
        {
            return Answer(() => ed.ObjectText(level, index));
        }

        [McpServerTool(Name = "object_text_set"), Description("Sets what a book, scroll, gravestone, writing or text trap reads: its own string is changed, or (shared or none) it gets a free string of its own. Written by save.")]
        public CallToolResult ObjectTextSet([Description("Level number, from 1.")] int level, [Description("The object's index (#).")] int index, [Description("The text.")] string text)
        {
            return Answer(() => ed.ObjectTextSet(level, index, text));
        }

        [McpServerTool(Name = "try_jump"), Description("The Try Jump tool: whether the player can jump from one square to another (the game's jump physics, plain and with the Jump spell), where he lands or what stops him.")]
        public CallToolResult TryJump([Description("Level number, from 1.")] int level, [Description("Jump from, 0-63.")] int fromX, [Description("Jump from.")] int fromY,
            [Description("Land on, 0-63.")] int toX, [Description("Land on.")] int toY)
        {
            return Answer(() => ed.TryJump(level, fromX, fromY, toX, toY));
        }

        [McpServerTool(Name = "hitbox_overlaps"), Description("View > Collision edges as text: the objects' collision boxes that overlap on a level (a creature that starts inside another box may never move).")]
        public CallToolResult HitboxOverlaps([Description("Level number, from 1.")] int level) { return Answer(() => ed.HitboxOverlaps(level)); }
    }
}
