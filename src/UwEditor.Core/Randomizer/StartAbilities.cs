using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// Abilities a start needs as an object: a new character (level 1) casts from runes only up to
    /// AbilityOptions.NewCharacterCircle (combat/RUNES.C player_cast: level at least 2 * circle - 1,
    /// mana at least 3 * circle and a Casting roll, UW1 and UW2), so a start whose free area is left
    /// only by, say, Gate Travel (circle 6) cannot be left with the runes. An object that holds the
    /// spell casts it without any of those checks (obj/USEITEMS.C checkSpell: only a pause between
    /// uses, and no spell on a no-magic square or on level 9), so the key item shuffle makes one and
    /// puts it within reach ("made" items, KeyShuffle). Which object, after the game's own:
    /// <list type="bullet">
    /// <item>a scroll of the spell when the logic counts single-use things for the ability (a scroll
    /// is read once and is gone, obj/USEITEMS.C UseBook, using_punt): UW1's scrolls of Gate Travel
    /// (item 0x138, quality 40, link 0x200 + 32, ID_FLAG11, enchanted, a quantity: level 5 #899,
    /// level 7 #760), UW2's item 0x137 (level 57 #477, Portal);</item>
    /// <item>otherwise a wand enchanted with the spell itself (a quantity object, enchanted, link 0x200 +
    /// the spell, ID_FLAG11): UseWand casts it (checkSpell) and leaves it whole, since only a wand
    /// holding a spell object loses charges or breaks (useNSpellCharges returns at once for a
    /// quantity object, and UseWand looks for the spell object only when it is not one). UW2 has one
    /// in its own data, the wand of Light at level 67 #899 (item 0x9B, link 0x200 + 5, flags 4);
    /// UW1 has none, so its wand (item 0x98) follows the scroll's fields. A lasting thing is the
    /// sound choice when single-use things do not count, and Gate Travel may be needed again.</item>
    /// </list>
    /// The runes themselves stay ordinary key items. Only UW1's key item shuffle makes the objects.
    /// </summary>
    public static class StartAbilities
    {
        /// <summary>
        /// The abilities rune casting gives that a new character cannot cast with these options:
        /// every spell of the game that gives it (and has runes) is above NewCharacterCircle. None
        /// with Magic off or no circle limit.
        /// </summary>
        public static List<string> Withheld(GameSession s, AbilityOptions logic)
        {
            var list = new List<string>();
            if (s == null || logic == null || !logic.Magic || logic.NewCharacterCircle >= AbilityOptions.MaxCircle) return list;
            foreach (var ab in Abilities.Names)
            {
                var spells = Abilities.SpellsGiving(s.IsUw2, ab);
                if (spells.Count > 0 && spells.All(sp => SpellTables.Circle(s.IsUw2, sp) > logic.NewCharacterCircle)) list.Add(ab);
            }
            return list;
        }

        /// <summary>The spell an object for the ability holds: the lowest circle's of those giving it (Levitate rather than Fly), or -1.</summary>
        public static int SpellFor(GameSession s, string ability)
        {
            return Abilities.SpellsGiving(s.IsUw2, ability).OrderBy(sp => SpellTables.Circle(s.IsUw2, sp)).ThenBy(sp => sp).DefaultIfEmpty(-1).First();
        }

        /// <summary>Whether the logic counts a single-use thing (once:X) for the ability.</summary>
        public static bool SingleUseCounts(AbilityOptions logic, string ability)
        {
            return logic.SingleUseItems && (logic.SingleUseOnly == null || logic.SingleUseOnly.Contains(ability));
        }

        /// <summary>
        /// The object made for an ability (see the class comment), its token (once:X for a scroll,
        /// perm:X for the wand) and the spell; null when the ability has no spell.
        /// </summary>
        public static FactGift ObjectFor(GameSession s, AbilityOptions logic, string ability, out string token, out int spell)
        {
            token = null;
            spell = SpellFor(s, ability);
            if (spell < 0 || spell >= 64) return null;
            bool scroll = SingleUseCounts(logic, ability);
            token = (scroll ? "once:" : "perm:") + ability;
            int item = scroll ? (s.IsUw2 ? 0x137 : 0x138) : (s.IsUw2 ? 0x9B : 0x98);
            return new FactGift { Item = item, Quality = 40, Link = 0x200 + spell, Flags = 4, IsQuantity = true, Enchanted = true };
        }

        /// <summary>The token the object made for an ability gives with these options (once:X or perm:X).</summary>
        public static string TokenFor(AbilityOptions logic, string ability) { return (SingleUseCounts(logic, ability) ? "once:" : "perm:") + ability; }

        /// <summary>
        /// The abilities a start needs as made objects, or an empty list: the solve from t's start
        /// zone with the tokens assumed (sv, already made, or null to make it) misses the goal and
        /// never leaves the start's free area, and with the objects made for some withheld abilities
        /// it reaches the goal. The abilities tried are those the ways out of the reached part and
        /// the warps ask for; one alone if one does it (in the order of Abilities.Names), else all of
        /// them, then each left out in turn when the goal is reached without it.
        /// </summary>
        public static List<string> Needed(GameSession s, TimelineResult t, AbilityOptions logic, IEnumerable<string> assumed, TimelineSolve sv = null)
        {
            var none = new List<string>();
            var withheld = Withheld(s, logic);
            if (withheld.Count == 0) return none;
            var held = (assumed ?? Enumerable.Empty<string>()).ToList();
            if (sv == null) sv = t.Engine.Run(Timeline.SolverFor(t, logic, held), logic, "made abilities: none", null);
            if (sv.GoalReached || sv.FreeArea == null || sv.FreeAreaLeft >= 0) return none;
            // the abilities the ways out ask for
            var asked = new HashSet<string>();
            Action<string> note = tok =>
            {
                tok = Requirement.Normalize(tok);
                int c = tok.IndexOf(':');
                if (c < 0) return;
                string head = tok.Substring(0, c), tail = tok.Substring(c + 1);
                if (head == "spell" || head == "cast" || head == "use" || head == "perm" || head == "once" || head == "worn") asked.Add(tail);
            };
            foreach (var b in sv.Blocked) foreach (var m in b.Missing) note(m);
            foreach (var w in t.Warps)
            {
                var r = Requirement.TryParse(w.Requires ?? "", out _);
                if (r == null) continue;
                var toks = new List<string>();
                r.Tokens(toks);
                foreach (var x in toks) note(x);
            }
            var cands = withheld.Where(asked.Contains).Where(a => !held.Contains(TokenFor(logic, a))).ToList();
            if (cands.Count == 0) return none;
            Func<IEnumerable<string>, bool> reaches = abs => t.Engine.Run(Timeline.SolverFor(t, logic, held.Concat(abs.Select(a => TokenFor(logic, a)))), logic, "made abilities: " + string.Join(", ", abs), null).GoalReached;
            foreach (var a in cands) if (reaches(new[] { a })) return new List<string> { a };
            if (cands.Count < 2 || !reaches(cands)) return none;
            var keep = cands.ToList();
            foreach (var a in cands) if (keep.Count > 1 && reaches(keep.Where(x => x != a))) keep.Remove(a);
            return keep;
        }

        /// <summary>Why a start gets the object made for an ability, in words (the log).</summary>
        public static string Why(GameSession s, AbilityOptions logic, string ability)
        {
            int sp = SpellFor(s, ability);
            int c = sp < 0 ? 0 : SpellTables.Circle(s.IsUw2, sp);
            return string.Format("the start's free area is left only with {0}, and a new character cannot cast {1} from runes (circle {2}, which asks for level {3}; the logic counts rune casting up to circle {4} until the start's free area is left)",
                ability.Replace('_', ' '), sp < 0 ? ability : SpellTables.Name(s, sp), c, 2 * c - 1, logic.NewCharacterCircle);
        }
    }
}
