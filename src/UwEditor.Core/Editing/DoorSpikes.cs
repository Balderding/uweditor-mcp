using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// A closed door's owner field, as both games read it: bit 0 a spike, bits 1-5 the spiked
    /// door's strength. A spike (0x127, UW1 only) used on a closed door (0x140-0x147) writes
    /// owner bit 0 and strength 31 (uw1 obj/USEITEMS.C SpikeDoor_seg040_662: owner &amp; 1 | 0x3E);
    /// other items "You can only spike closed doors". A hit on a spiked door with strength left
    /// takes the damage off the strength and never breaks it (combat/DAMAGE.C damage_object, both
    /// games: OBJ_OWNER &amp; 1 &amp;&amp; OBJ_OWNER &gt;&gt; 1 &gt; 0); with strength 0 the door loses quality and
    /// breaks like any other. A creature cannot open a spiked door; the player can, and opening it
    /// clears bit 0 (UseRect, OpenDoor), after which the moving door's owner is its state and the
    /// door comes to rest with owner 0 (obj/EFFECT.C). UW2 has no spike item (0x127 is a force field)
    /// but keeps every one of these rules. No shipped door of either game has an owner other than 0.
    /// </summary>
    public static class DoorSpikes
    {
        /// <summary>The owner a spike writes: spiked, strength 31.</summary>
        public const int SpikedOwner = 0x3F;

        /// <summary>Owner bit 0 of a closed door.</summary>
        public static readonly FieldDef Spiked = new FieldDef("Spiked", 6, 0, 1,
            "A spike holds the door. Creatures cannot open it; you can, and opening it pulls the spike out. "
            + "Hits wear its Strength down instead of breaking it: while strength is left it never breaks. "
            + "With Strength 0 it breaks like any door. Ticking it writes Strength 31, as the spike does (UW2 has no spike item, but keeps the rule).",
            "obj/USEITEMS.C SpikeDoor_seg040_662, UseRect, OpenDoor; combat/DAMAGE.C damage_object");

        /// <summary>Owner bits 1-5 of a closed door.</summary>
        public static readonly FieldDef Strength = new FieldDef("Strength", 6, 1, 5,
            "A spiked door's strength, 0-31. Each hit takes its damage (after the door's toughness) off the strength, never below 0; "
            + "at 0 the spike no longer holds and the door breaks like any other. A spike sets 31. Means nothing while the door is not spiked.",
            "combat/DAMAGE.C damage_object; obj/USEITEMS.C SpikeDoor_seg040_662");

        /// <summary>The doors a spike goes into and whose owner the damage code reads: the closed ones.</summary>
        public static bool IsClosedDoor(int item) { return item >= 0x140 && item <= 0x147; }

        /// <summary>A spiked closed door with strength left: hits wear it down, it never breaks.</summary>
        public static bool Holds(ObjectInfo door) { return door != null && IsClosedDoor(door.Item) && (door.Owner & 1) != 0 && door.Owner >> 1 > 0; }

        /// <summary>One of the two fields (the properties panel's rows).</summary>
        public static bool IsField(FieldDef f) { return f == Spiked || f == Strength; }

        /// <summary>
        /// Sets Spiked or Strength of a closed door. Spiked set writes the whole owner as the spike
        /// does (spiked, strength 31); cleared it clears bit 0 only, as opening the door does.
        /// </summary>
        public static void Set(LevelEditor e, int index, FieldDef f, int value)
        {
            var o = e.Model.Objects[index];
            if (!IsClosedDoor(o.Item)) throw new System.InvalidOperationException("Only a closed door (0x140-0x147) is spiked.");
            if (f == Spiked && value == 1 && (o.Owner & 1) == 0) e.SetObjectField(index, Uw1Schemas.StaticObject["Owner"], SpikedOwner);
            else e.SetObjectField(index, f, value);
        }
    }
}
