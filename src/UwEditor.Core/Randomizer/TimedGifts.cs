using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// A gift only there for a time (a fact of kind b) that a solve did not get, and why; or (Ability
    /// set) an ability the start needs as an object, as a new character cannot cast it from runes
    /// (StartAbilities). Either is made anew by the key item shuffle (ShuffleObject.Made).
    /// </summary>
    public sealed class MissedGift
    {
        public Fact Fact;
        /// <summary>The fact's placement (the zone of the one who gives it).</summary>
        public Placement Placement;
        /// <summary>What the gift grants (key:28).</summary>
        public string Token = "";
        /// <summary>Whether the solve reached the giver's zone at all (then the window had closed by the time it did).</summary>
        public bool GiverReached;
        /// <summary>For an object made for the start's way out: the ability (gate_travel), its object and why; null for a gift.</summary>
        public string Ability;
        public FactGift Object;
        public string Reason;
        /// <summary>The object that stands for the gift (the fact's "gift", or the object made for an ability), or null: then no new one can be made.</summary>
        public FactGift Gift { get { return Object ?? (Fact == null ? null : Fact.Gift); } }
        /// <summary>What it is made for: the fact's id, or "start: ability".</summary>
        public string Id { get { return Ability != null ? "start: " + Ability : Fact == null ? "?" : Fact.Id; } }
        public string Why
        {
            get
            {
                if (Reason != null) return Reason;
                string when = Fact == null || Fact.LostWhen.Length == 0 ? "" : " (gone at " + Fact.LostWhen + ")";
                return GiverReached ? "the giver is reached only after the window has closed" + when : "the giver is never reached" + when;
            }
        }
        public override string ToString() { return Id + " [" + Token + "]: " + Why; }
    }

    /// <summary>
    /// Gifts a conversation hands over only while a window is open (the facts' mayBeLost of kind b:
    /// Dupre's key 28 at castle plot 0, Charles's key 2 at plot 9 to 11, Altara's dagger and rod
    /// before plot 14). From some starts the solve cannot get such a gift in time; with a fact's
    /// "gift" (the object it is) the key item shuffle can then make a new one and place it like a
    /// key item (KeyShuffle: "made" items). The gift's own fact stays: a player who does get there
    /// in time is given a second one.
    /// </summary>
    public static class TimedGifts
    {
        /// <summary>
        /// The timed gifts a solve did not get: a flag fact of kind b (or c) whose placement the
        /// solve did not count (SolverOptions.Gone kept it out once its window had closed, or its
        /// zone was never reached), and whose token the solve did not get from anywhere else either.
        /// Gifts whose token nothing asks for are listed too; the caller decides what matters.
        /// </summary>
        public static List<MissedGift> Missed(TimelineResult t, TimelineSolve sv)
        {
            var list = new List<MissedGift>();
            if (t == null || sv == null || t.Facts == null) return list;
            var used = new HashSet<Placement>(sv.Events.Where(e => e.Placement != null).Select(e => e.Placement));
            foreach (var f in t.Facts.OfKind("flag"))
            {
                if (Fact.LostRank(f.Lost) < 2 || f.Grants.Length == 0) continue;
                var ps = t.Set.Placements.Where(p => p.Source == "facts: " + f.Id).ToList();
                if (ps.Count == 0 || ps.Any(used.Contains)) continue;
                string token = Requirement.Normalize(f.Grants);
                if (sv.TokenSphere.ContainsKey(token)) continue;
                list.Add(new MissedGift { Fact = f, Placement = ps[0], Token = token, GiverReached = ps.Any(p => sv.ZoneSphere.ContainsKey(p.Zone)) });
            }
            return list;
        }
    }
}
