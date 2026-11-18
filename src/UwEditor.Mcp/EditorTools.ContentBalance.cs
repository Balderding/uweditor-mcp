using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UwEditor.Mcp
{
    /// <summary>Balance (Tools ▸ Balance: Weapons, Armour and rings, Barter simulator).</summary>
    public sealed partial class EditorTools
    {
        const string StatsHelp = "Player stats to change from the open game's player, as name=value, comma-separated: strength, dexterity, a skill (attack, defense, unarmed, sword, axe, mace, missile, charm, appraise...), or easy.";

        [McpServerTool(Name = "balance_weapons"), Description("The weapons overview (Tools ▸ Balance ▸ Weapons): every melee weapon and launcher with its numbers, the damage a player of the given stats expects per swing and per second against a target, and how many the levels hold (plain, enchanted, special, other). From the tables as edited (table_set changes them) and the levels as edited.")]
        public CallToolResult BalanceWeapons([Description(StatsHelp)] string stats = null,
            [Description("A creature to fight (its number 0x40-0x7F or name): its defence and armour.")] string creature = null,
            [Description("The target's defence, -10-40 (default 5, or the creature's).")] int? defence = null,
            [Description("The target's armour, 0-30 (default 0, or the creature's).")] int? armour = null,
            [Description("A powerful target (armour counts 5/3).")] bool powerful = false,
            [Description("Seconds spent aiming a launcher, 0-3 (default 0.3).")] double aimSeconds = 0.3,
            [Description("Only the weapons the levels hold.")] bool inWorldOnly = false)
        {
            return Answer(() => ed.BalanceWeapons(stats, creature, defence, armour, powerful, aimSeconds, inWorldOnly));
        }

        [McpServerTool(Name = "balance_armour"), Description("The armour and rings overview (Tools ▸ Balance ▸ Armour and rings): every piece's slot, protection, armour at a quality, to-hit penalty, wear, the damage a blow does with it worn and without, and how many the levels hold; where the game's code slips, what it means to give. From the tables as edited.")]
        public CallToolResult BalanceArmour([Description(StatsHelp)] string stats = null,
            [Description("The skill of the weapon in hand, 0-30: half of it adds to Defence (default the player's Sword).")] int? weaponSkill = null,
            [Description("The items' quality, 0-63 (default 63).")] int quality = 63,
            [Description("The blow's to-hit, 0-40 (default 10).")] int toHit = 10,
            [Description("The blow's damage, 1-60 (default 12).")] int damage = 12,
            [Description("Only the pieces the levels hold.")] bool inWorldOnly = false)
        {
            return Answer(() => ed.BalanceArmour(stats, weaponSkill, quality, toHit, damage, inWorldOnly));
        }

        [McpServerTool(Name = "barter_traders"), Description("The creatures of the levels that have a conversation of their own, as barter_simulate's trader (level:index), with how many items their conversation likes and dislikes.")]
        public CallToolResult BarterTraders() { return Answer(() => ed.BarterTraders()); }

        [McpServerTool(Name = "barter_simulate"), Description("The barter simulator (Tools ▸ Balance ▸ Barter simulator): a trade worked out as the game does it (conv/BARTER.C): each item's worth to the trader (its likes and dislikes from its conversation), its greed, patience and misjudging drawn as the game draws them for that creature, whether it accepts, and the player's own remark. settle adds the coins that make it accept (greed) or make it even.")]
        public CallToolResult BarterSimulate([Description("The player's items: \"item x quantity @ quality\", comma-separated; an item by number or name (\"0xA0 x 20, sword @ 40\").")] string playerItems,
            [Description("The trader's items, the same way.")] string traderItems,
            [Description("The trader: \"level:index\" of a creature (barter_traders), or a creature kind by number or name (its typical figures).")] string trader,
            [Description(StatsHelp + " Charm and appraise count here.")] string stats = null,
            [Description("The coins' quality for settle, 0-63 (default the game's).")] int? coinQuality = null,
            [Description("none, greed (the fewest coins it accepts) or even (an even trade).")] string settle = "none")
        {
            return Answer(() => ed.BarterSimulate(playerItems, traderItems, trader, stats, coinQuality, settle));
        }
    }
}
