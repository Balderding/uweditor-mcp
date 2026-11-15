using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>What the logic may expect of the player (Phase B of the automatic zones).</summary>
    public sealed class AbilityOptions
    {
        /// <summary>Casting from runes counts (cast:X).</summary>
        public bool Magic = true;
        /// <summary>
        /// The highest circle of a spell cast from runes that counts before the player has left the
        /// start's free area (the zones the start reaches by ways that need nothing; Timeline's
        /// solve). A new character is level 1, and casting from runes asks for level 2 * circle - 1
        /// (combat/RUNES.C player_cast, UW1 and UW2), so 1 by default; 8 for no limit. Once the
        /// area is left (by a key, a lever, an item, a spell up to this circle …) every circle
        /// counts again: the logic has no experience model and takes fights as won.
        /// </summary>
        public int NewCharacterCircle = 1;
        /// <summary>The circles a spell from runes can have (UW1 and UW2: 8); NewCharacterCircle at this or above sets no limit.</summary>
        public const int MaxCircle = 8;
        /// <summary>Scrolls, potions and charged wands count for an ability, besides worn and unlimited items.</summary>
        public bool SingleUseItems;
        /// <summary>With SingleUseItems: the only abilities whose scrolls, potions and charges count (a stricter solve, say "iron_flesh" alone); null for all.</summary>
        public HashSet<string> SingleUseOnly;
        /// <summary>Lock picking is in logic, with PickSkill assumed.</summary>
        public bool LockPicking;
        /// <summary>The Pick Lock skill assumed, 0-30.</summary>
        public int PickSkill = 30;
        /// <summary>A failed pick can be tried again (UseLockpickOn keeps the pick): any chance counts.</summary>
        public bool PickRetry = true;
        /// <summary>The Search skill assumed for look triggers, 0-30.</summary>
        public int SearchSkill;
        /// <summary>A failed look can be tried again: any chance counts. Off: even odds (skill at least the height).</summary>
        public bool SearchRetry = true;
        /// <summary>Every tile of every level holding a move trigger that hurts the player is a wall (not only the damaging maze).</summary>
        public bool DamageTrapWalls;
        /// <summary>
        /// Karl: the Ring of Levitation's area on L7 counts as reached only with lava protection
        /// (the jumps from (59, 43) that reach it without any are an ordeal); off, it is merged with
        /// the zone of (59, 43). The facts' link "ring_of_levitation" tests option:ring_needs_lava.
        /// </summary>
        public bool RingNeedsLava = true;
        /// <summary>An item a non-hostile NPC carries can be had by killing it once its zone is reached (option:kill_npcs).</summary>
        public bool KillNpcs;
        /// <summary>Karl (Phase D): the goal also needs Tyball's orb on L7 broken (option:require_orb; the goal fact "goal_orb").</summary>
        public bool RequireOrb;
        /// <summary>
        /// The logic relies on nothing the game may take away whatever the player does (the facts'
        /// mayBeLost of kind b or c: Dupre's key at castle plot 0, Altara before plot 14): such a
        /// source counts only while its window is still open in the solve's own order. Kind a (lost
        /// only by an avoidable choice) still counts; the walkthrough warns.
        /// </summary>
        public bool AvoidLost = true;
        /// <summary>
        /// UW2: the Pits of Carnage's key gem (gem 7) comes only by Blog's way (befriend Blog on
        /// level 59, then Dorstag hands the gem to him and Blog gives it: the facts key_gem_7_blog
        /// and blog_friend); killing Dorstag, who carries it, no longer counts (option:pits_gem_by_blog;
        /// key_gem_7 needs nooption:pits_gem_by_blog). Off: both ways count.
        /// </summary>
        public bool PitsGemByBlog;

        // Bashing is always in the logic (Karl: fists bash any breakable door), so it is no option.

        public AbilityOptions Clone() { return (AbilityOptions)MemberwiseClone(); }

        /// <summary>The option tokens these set (option:NAME; FactSet.KnownOptions).</summary>
        public HashSet<string> Tokens()
        {
            var h = new HashSet<string>();
            if (Magic) h.Add("magic");
            if (SingleUseItems) h.Add("single_use");
            if (LockPicking) h.Add("lockpick");
            if (PickRetry) h.Add("pick_retry");
            if (SearchRetry) h.Add("search_retry");
            if (DamageTrapWalls) h.Add("damage_walls");
            if (RingNeedsLava) h.Add("ring_needs_lava");
            if (KillNpcs) h.Add("kill_npcs");
            if (RequireOrb) h.Add("require_orb");
            if (PitsGemByBlog) h.Add("pits_gem_by_blog");
            return h;
        }

        /// <summary>The solver's options for these (bashing always allowed).</summary>
        public SolverOptions ToSolver()
        {
            return new SolverOptions
            {
                AbilitySources = true, AllowSpells = false, Magic = Magic, SingleUseItems = SingleUseItems, SingleUseOnly = SingleUseOnly,
                NewCharacterCircle = NewCharacterCircle >= MaxCircle ? int.MaxValue : Math.Max(0, NewCharacterCircle),
                AllowBash = true, LockpickSkill = LockPicking ? PickSkill : -1, PickRetry = PickRetry, PickNeedsLockpick = true,
                SearchSkill = SearchSkill, SearchRetry = SearchRetry, OptionsOn = Tokens(), AvoidLost = AvoidLost,
            };
        }

        public override string ToString()
        {
            return string.Format("Magic {0}, single-use items {1}, lock picking {2}, bashing always, Search {3}{4}, damage-trap tiles are walls {5}, Ring of Levitation {6}, killing NPCs {7}{8}{9}{10}",
                Magic ? (NewCharacterCircle >= MaxCircle ? "on" : "on (from runes up to circle " + NewCharacterCircle + " until the start's free area is left)") : "off", SingleUseItems ? "count" : "do not count", LockPicking ? "on (skill " + PickSkill + (PickRetry ? ", retry" : "") + ")" : "off",
                SearchSkill, SearchRetry ? " with retry" : " without retry", DamageTrapWalls ? "everywhere" : "only in the facts' areas",
                RingNeedsLava ? "needs lava protection" : "free", KillNpcs ? "in logic" : "not in logic", RequireOrb ? ", the goal needs the orb broken" : "",
                AvoidLost ? "" : ", things the game takes away count", PitsGemByBlog ? ", the Pits' gem only by Blog's way" : "");
        }
    }

    /// <summary>A wall that a look trigger opens (a secret door standing in rock, or a plain passage): a gate Reveal or the Search skill passes.</summary>
    public sealed class HiddenGate
    {
        public int Level, Trigger, Holder, HolderItem, Z;
        public string HolderName;
        /// <summary>The square that opens.</summary>
        public int X, Y;
        public int FromRegion = -1, FromZone;
        public readonly List<int> ToZones = new List<int>();
        /// <summary>The regions around the opened square.</summary>
        public readonly List<int> Regions = new List<int>();
        public string Requires = "";
        /// <summary>The secret door that stands on the square, or -1.</summary>
        public int Door = -1;
        public string Notes = "", FactNotes = "";
        /// <summary>Painted links it stands for.</summary>
        public readonly List<int> PaintedLinks = new List<int>();
        public bool RevealWorks { get { return Z < 30; } }
        public override string ToString() { return string.Format("L{0} look trigger #{1} (height {2}) on #{3} {4}: opens ({5}, {6})", Level, Trigger, Z, Holder, HolderName, X, Y); }
    }

    /// <summary>A door the logic cannot pass when lock picking or bashing is off, and what lies behind it.</summary>
    public sealed class DoorToOpen
    {
        public ZoneLink Link;
        public int FromZone, ToZone;
        /// <summary>Zones reached once it is open, besides what the logic reaches already.</summary>
        public readonly List<int> Gains = new List<int>();
        public readonly List<string> GainedTokens = new List<string>();
        public bool GoalBehind;
        /// <summary>Something behind it matters: tokens, zones with links onwards, the goal.</summary>
        public bool Matters { get { return GoalBehind || GainedTokens.Count > 0 || Gains.Count > 1; } }
    }

    /// <summary>The Phase B result: the zones with abilities and options, their sources, the hidden gates and the walls.</summary>
    public sealed class AbilityZoneResult
    {
        public AutoZoneResult Auto;
        public ZoneSet Set { get { return Auto.Set; } }
        public FactSet Facts;
        public AbilityOptions Options;
        public List<AbilitySource> Sources = new List<AbilitySource>();
        public List<HiddenGate> Hidden = new List<HiddenGate>();
        /// <summary>Walled tiles by fact (or "global"), as "Lx (x, y)".</summary>
        public Dictionary<string, List<string>> Walls = new Dictionary<string, List<string>>();
        /// <summary>Damage-trap tiles outside the facts' areas (walled only with the global option), per level.</summary>
        public Dictionary<int, int> DamageTilesElsewhere = new Dictionary<int, int>();
        /// <summary>Region edges whose spell: terms were narrowed where magic does not work (BritanniaEdges: UW2's levels 1 to 8, AbilityZones.CastAt).</summary>
        public int NoMagicEdges, NoManaEdges, BritanniaEdges;
        public SolveResult Solve;
        /// <summary>The main option sets, by a short label: the solve of each.</summary>
        public List<KeyValuePair<string, SolveResult>> Variants = new List<KeyValuePair<string, SolveResult>>();
        public List<DoorToOpen> DoorsToOpen = new List<DoorToOpen>();
        public List<string> Warnings = new List<string>();
        /// <summary>The tiles made walls, per level (y * 64 + x).</summary>
        public Dictionary<int, HashSet<int>> WallTiles = new Dictionary<int, HashSet<int>>();
        /// <summary>The level of each level as the zones see it (the walls in), read once.</summary>
        public Func<int, LevelModel> WalledLevel;
        /// <summary>The movement options the zones were built with (the facts' free chains in).</summary>
        public NavOptions Nav;
        /// <summary>Placements of items non-hostile NPCs carry (option:kill_npcs), and of items a create trap makes (its flag).</summary>
        public List<Placement> NpcHeld = new List<Placement>(), Created = new List<Placement>();
        /// <summary>The placements left out because they come from a creature the logic must spare (the facts' kind "spare": what it carries, what its death leaves, a fact granted on it), as "token: why".</summary>
        public List<string> Spared = new List<string>();
        /// <summary>The spells' names (block 6 0x100 + spell), for the report.</summary>
        public Dictionary<int, string> SpellNames = new Dictionary<int, string>();
    }

    /// <summary>
    /// Phase B of the automatic zones: the player's abilities and where they come from (spells
    /// from runes, items, the dragon skin boots), where magic does not work, the hidden gates
    /// that Reveal or the Search skill pass, the options for lock picking and bashing, the
    /// damaging maze's trap tiles as walls, and the solver run with all of it.
    /// </summary>
    public static class AbilityZones
    {
        /// <param name="zoning">A zoning whose zones are used instead of making them (AutoZones.AssignGiven), or null.</param>
        public static AbilityZoneResult Build(GameSession s, Func<int, LevelModel> level, NavOptions nav = null, AbilityOptions opt = null, ZoneSet painted = null, FactSet facts = null, ZoneSet zoning = null)
        {
            opt = opt ?? new AbilityOptions();
            facts = facts ?? FactSet.Shipped(s.IsUw2);
            nav = FreeChains(nav ?? new NavOptions(), facts);
            var res = new AbilityZoneResult { Facts = facts, Options = opt, Nav = nav };
            for (int i = 0; i < Abilities.SpellCount(s.IsUw2); i++) res.SpellNames[i] = SpellTables.Name(s, i);
            foreach (var w in facts.Check()) res.Warnings.Add("Facts: " + w);

            // ---- walls: the facts' areas, and with the option every damage-trap tile
            var walls = new Dictionary<int, HashSet<int>>();
            var cache = new Dictionary<int, LevelModel>();
            Func<int, LevelModel> orig = l => { LevelModel m; if (!cache.TryGetValue(l, out m)) cache[l] = m = level(l); return m; };
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = orig(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                var dmg = DamageTiles(s, m);
                int elsewhere = 0;
                foreach (int t in dmg)
                {
                    int x = t % 64, y = t / 64;
                    var f = facts.OfKind("walls").FirstOrDefault(w => w.Level == slot.Level && w.Tiles == "damage" && w.InRect(x, y));
                    if (f == null) elsewhere++;
                    if (f == null && !opt.DamageTrapWalls) continue;
                    HashSet<int> set;
                    if (!walls.TryGetValue(slot.Level, out set)) walls[slot.Level] = set = new HashSet<int>();
                    set.Add(t);
                    string key = f == null ? "global" : f.Id;
                    List<string> l;
                    if (!res.Walls.TryGetValue(key, out l)) res.Walls[key] = l = new List<string>();
                    l.Add(string.Format("L{0} ({1}, {2})", slot.Level, x, y));
                }
                if (elsewhere > 0) res.DamageTilesElsewhere[slot.Level] = elsewhere;
            }
            var walled = new Dictionary<int, LevelModel>();
            Func<int, LevelModel> wl = l =>
            {
                HashSet<int> set;
                if (!walls.TryGetValue(l, out set)) return orig(l);
                LevelModel c;
                if (walled.TryGetValue(l, out c)) return c;
                var m = orig(l);
                c = LevelModel.FromBytes(s, l, m.Block.Data, m.TextureBytes);
                foreach (int t in set) c.Tiles[t % 64, t / 64].Type = TileType.Solid;
                walled[l] = c;
                return c;
            };

            res.WallTiles = walls;
            res.WalledLevel = wl;

            // ---- the regions, the hidden gates, and where magic does not work
            var g = Graph(s, wl, nav, facts, res, painted, null);

            // ---- zones, then the Phase B placements
            var auto = AutoZones.FromGraph(s, wl, g, painted, zoning);
            res.Auto = auto;
            var zs = auto.Set;
            if (zoning == null) zs.Source = "automatic zones (Underworld Editor), Phase B";
            zs.OpenSources.Remove("hazard:lava");
            // one placement per object, not per zone and token (Phase D: the key item shuffle moves objects one by one)
            zs.Placements.RemoveAll(p => p.Source == "levels");
            zs.Placements.AddRange(KeyItems.SuggestPlacements(zs, KeyItems.Find(s, wl, zs), true));
            foreach (var h in res.Hidden)
            {
                h.FromZone = h.FromRegion >= 0 ? g.Regions[h.FromRegion].Zone : 0;
                foreach (int r in h.Regions) { int z = g.Regions[r].Zone; if (z > ZoneSet.Unassigned && z != h.FromZone && !h.ToZones.Contains(z)) h.ToZones.Add(z); }
            }
            AddPlacements(s, wl, zs, facts, res);
            if (!s.IsUw2) ChainPlacements(s, wl, g, zs, facts, res);
            Explain(zs);

            // ---- the solves
            Resolve(res);
            return res;
        }

        /// <summary>The movement options with the facts' free chains (kind "free": objects and triggers) and the doors conversations open (kind "door") added.</summary>
        public static NavOptions FreeChains(NavOptions nav, FactSet facts)
        {
            nav = nav.Clone();
            foreach (var f in facts.OfKind("free"))
                foreach (int i in f.Objects.Concat(f.Triggers)) nav.FreeChains.Add(f.Level + ":" + i);
            foreach (var f in facts.OfKind("door"))
            {
                if (f.Level <= 0 || f.Object < 0 || string.IsNullOrWhiteSpace(f.Requires)) continue;
                string key = f.Level + ":" + f.Object, before;
                string r = f.Requires.Trim();
                nav.DoorAlternatives[key] = nav.DoorAlternatives.TryGetValue(key, out before) ? "(" + before + ") | (" + r + ")" : r;
            }
            return nav;
        }

        /// <summary>
        /// The region graph of the levels given (all when only is null) with Phase B's additions:
        /// the hidden gates and the narrowing of spell terms where magic does not work. The
        /// timeline (Phase C) rebuilds one level at a time with it.
        /// </summary>
        public static NavGraph Graph(GameSession s, Func<int, LevelModel> wl, NavOptions nav, FactSet facts, AbilityZoneResult res, ZoneSet painted, IEnumerable<int> only)
        {
            var g = NavGraph.Build(s, wl, nav, only);
            AddHiddenGates(s, wl, g, res, painted);
            Narrow(s, wl, g, facts, res);
            return g;
        }

        /// <summary>Runs the solver again with res.Options (and the variants and the doors to open).</summary>
        public static void Resolve(AbilityZoneResult res)
        {
            var zs = res.Set;
            res.Solve = new ZoneSolver(zs, res.Options.ToSolver()).Solve(zs.StartZone, zs.GoalZone);
            res.Variants.Clear();
            foreach (bool magic in new[] { true, false })
                foreach (bool once in new[] { false, true })
                    foreach (bool doors in new[] { true, false })
                    {
                        var o = res.Options.Clone();
                        o.Magic = magic; o.SingleUseItems = once; o.LockPicking = doors;
                        if (doors) { o.PickSkill = 30; o.PickRetry = true; }
                        string label = string.Format("Magic {0}, single-use {1}, picking {2}", magic ? "on" : "off", once ? "on" : "off", doors ? "on" : "off");
                        res.Variants.Add(new KeyValuePair<string, SolveResult>(label, new ZoneSolver(zs, o.ToSolver()).Solve(zs.StartZone, zs.GoalZone)));
                    }
            res.DoorsToOpen = DoorsToOpen(zs, res.Options);
            // Phase A's own solve (spells free, lava as a placeholder) means nothing here any more: its report then leaves it out
            res.Auto.Solve = null;
            res.Auto.SolveWithLava = null;
        }

        // ---------------------------------------------------------------- damage tiles

        /// <summary>
        /// The tiles holding a move trigger the player sets off (id bit 11) whose chain hurts him:
        /// a damage trap that does not heal (owner 0), an arrow trap, or a spell trap casting a
        /// missile, a backfire, Sheet Lightning or Flame Wind, Poison or Paralyze (event/TRIGGER.C
        /// UseTrap; combat/SPELLS.C do_spell). Move triggers go off when the player touches them
        /// (motion/PHYSICS.C). UW1 only.
        /// </summary>
        public static HashSet<int> DamageTiles(GameSession s, LevelModel m)
        {
            var set = new HashSet<int>();
            if (s.IsUw2) return set;
            foreach (var o in m.Objects)
            {
                if (o.Free || !o.OnTile || o.Item != 0x1A0 || ((o.Record[1] >> 3) & 1) == 0) continue;
                if (m.Tiles[o.TileX, o.TileY].IsSolid) continue;
                if (ChainHurts(m, o)) set.Add(o.TileY * 64 + o.TileX);
            }
            return set;
        }

        static bool ChainHurts(LevelModel m, ObjectInfo trigger)
        {
            var seen = new HashSet<int> { trigger.Index };
            var next = Triggers.LinksTo(m, trigger);
            while (next != null && seen.Add(next.Index) && seen.Count < 256)
            {
                if (Triggers.IsTrigger(next.Item)) { next = Triggers.LinksTo(m, next); continue; }
                if (!Triggers.IsTrap(next.Item)) break;
                int it = next.Item;
                if (it == 0x180 && next.Owner == 0 && next.Quality > 0) return true;
                if (it == 0x182) return true;
                if (it == 0x186)
                {
                    int cls = next.Quality, minor = next.Owner & 0x3F;
                    if (cls == 5 || cls == 9 || (cls == 6 && (minor == 2 || minor == 4)) || (cls == 7 && (minor == 4 || minor == 5))) return true;
                }
                // create, delete, the wards and UW1's door trap end the chain (event/TRIGGER.C UseTrap)
                if (it == 0x187 || it == 0x18B || it == 0x189 || it == 0x18A || it == 0x188) break;
                next = Triggers.LinksTo(m, next);
            }
            return false;
        }

        // ---------------------------------------------------------------- hidden gates

        /// <summary>
        /// Every change-terrain trap set off by a look trigger that opens a square now in rock is
        /// a gate: looking at the trigger's object opens the square (often a secret door stands in
        /// it), which then joins the regions around it. The look passes at once for a trigger of
        /// height 0, else with skill_check(Search, height) (event/TRIGGER.C UseTrigger), and Reveal
        /// sets the trigger off with Search 45 (combat/SPELLS.C sp_true_sight). The square's new
        /// height decides the climbs. Region edges between every pair of its neighbours, needing
        /// the side the look is made from (region:A) and "spell:reveal | search:height".
        /// </summary>
        static void AddHiddenGates(GameSession s, Func<int, LevelModel> level, NavGraph g, AbilityZoneResult res, ZoneSet painted)
        {
            if (s.IsUw2) return;
            var o = g.Options;
            foreach (var mech in g.Mechanisms)
            {
                if (mech.Kind != "terrain" || mech.TriggerItem != 0x1A3) continue;
                var m = level(mech.Level);
                var trig = m.Objects[mech.Trigger];
                var trap = m.Objects[mech.Trap];
                int type = trap.Heading * 2 + (trap.Quality & 1);
                if (type == 15) type = 10;
                if (type == 0) continue;        // it makes rock
                // a trigger the player cannot set off opens nothing
                if (string.IsNullOrEmpty(mech.SetOff)) continue;
                int A = -1;
                var mm = Regex.Match(mech.SetOff ?? "", @"region:(\d+)");
                if (mm.Success) A = int.Parse(mm.Groups[1].Value);
                // what the chain needs besides the look: where it is set off from (all of it, not only the first region),
                // the conditions met along it, and what the facts add (as the timeline's mechanism does)
                bool plain = mech.SetOff == "region:" + A;
                string also = ZoneLink.JoinAnd(new[] { mech.Conditions, res.Facts.MechanismNeeds(mech.Level, mech.Trigger, mech.Flag) });
                for (int y = mech.Y; y < mech.Y + mech.H; y++)
                    for (int x = mech.X; x < mech.X + mech.W; x++)
                    {
                        if (x < 0 || y < 0 || x > 63 || y > 63 || !m.Tiles[x, y].IsSolid) continue;
                        if (type == 10) continue;   // a rock square that keeps its type stays rock
                        int h = trap.Z / 8 < 14 ? trap.Z / 8 : m.Tiles[x, y].Height;
                        var holder = trig.Parent >= 0 ? m.Objects[trig.Parent] : null;
                        var gate = new HiddenGate
                        {
                            Level = mech.Level, Trigger = trig.Index, Z = trig.Z, X = x, Y = y, FromRegion = A,
                            Holder = holder == null ? -1 : holder.Index, HolderItem = holder == null ? -1 : holder.Item, HolderName = holder == null ? "" : holder.Name,
                        };
                        // the door that stands there
                        string doorReq = "free";
                        foreach (var d in m.TileObjects(x, y))
                            if (d.Item >= 0x140 && d.Item <= 0x14F) { string dn; doorReq = ZoneDoors.Requirement(s, m, d, out dn); gate.Door = d.Index; gate.Notes = "secret door #" + d.Index + ": " + dn; break; }
                        string look = "spell:reveal | search:" + gate.Z;
                        if (!gate.RevealWorks) look = "search:" + gate.Z;
                        // the neighbours and their climbs onto and off the opened square
                        var sides = new List<KeyValuePair<int, int[]>>();      // region, (up onto, up off)
                        for (int dir = 0; dir < 4; dir++)
                        {
                            int nx = x + (dir == 1 ? 1 : dir == 3 ? -1 : 0), ny = y + (dir == 0 ? 1 : dir == 2 ? -1 : 0);
                            int r = g.RegionOf(mech.Level, nx, ny);
                            if (r < 0) continue;
                            var nt = level(mech.Level).Tiles[nx, ny];
                            if (!ZoneFlood.IsSideOpen(nt.Type, (dir + 2) & 3)) continue;
                            int lo, hi;
                            ZoneFlood.EdgeHeights(nt, (dir + 2) & 3, out lo, out hi);
                            sides.Add(new KeyValuePair<int, int[]>(r, new[] { h - hi, lo - h }));
                        }
                        foreach (var p in sides) if (!gate.Regions.Contains(p.Key)) gate.Regions.Add(p.Key);
                        foreach (var p in sides)
                            foreach (var q in sides)
                            {
                                if (p.Key == q.Key) continue;
                                var terms = new List<string>();
                                if (!plain || p.Key != A) terms.Add(mech.SetOff);
                                terms.Add(also);
                                terms.Add(look);
                                terms.Add(doorReq);
                                // up onto the opened tile from p's floor (h less the climb), then up off it from h
                                terms.Add(Climb(o, p.Value[0], h - p.Value[0], q.Value[1], h));
                                var rq = g.Regions[q.Key];
                                if (rq.IsDoor) terms.Add(rq.DoorRequires);
                                g.Edges.Add(new NavEdge
                                {
                                    From = p.Key, To = q.Key, Kind = "secret", Requires = ZoneLink.JoinAnd(terms), Level = mech.Level, X = x, Y = y,
                                    Notes = string.Format("hidden: look trigger #{0} (height {1}) on #{2} opens ({3}, {4})", trig.Index, trig.Z, gate.Holder, x, y),
                                });
                            }
                        gate.Requires = ZoneLink.JoinAnd(new[] { look, doorReq });
                        var fact = res.Facts.OfKind("hidden").FirstOrDefault(f => f.Level == mech.Level && f.Trigger == trig.Index);
                        if (fact != null) { gate.FactNotes = fact.Notes; if (!string.IsNullOrEmpty(fact.Requires)) gate.Requires = fact.Requires; }
                        if (painted != null) MatchPainted(painted, gate);
                        res.Hidden.Add(gate);
                    }
            }
            foreach (var f in res.Facts.OfKind("hidden"))
                if (!res.Hidden.Any(h => h.Level == f.Level && h.Trigger == f.Trigger))
                    res.Warnings.Add(string.Format("Facts: {0}: no look trigger #{1} opening a wall on L{2}.", f.Id, f.Trigger, f.Level));
        }

        /// <summary>What two climbs in a row need, each from its own floor (NavGraph's climb rule).</summary>
        static string Climb(NavOptions o, int rise1, int floor1, int rise2, int floor2)
        {
            if (rise1 <= o.Climb(floor1, false) && rise2 <= o.Climb(floor2, false)) return "free";
            return rise1 <= o.Climb(floor1, true) && rise2 <= o.Climb(floor2, true) ? "spell:jump" : "spell:levitate";
        }

        static void MatchPainted(ZoneSet painted, HiddenGate gate)
        {
            var near = new HashSet<int>();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int z = painted.ZoneAt(gate.Level, gate.X + dx, gate.Y + dy);
                    if (z > ZoneSet.Unassigned) near.Add(z);
                }
            string at = string.Format("({0},{1})", gate.X, gate.Y);
            foreach (var k in painted.Links)
            {
                if (k.Kind != "secret") continue;
                bool notes = (k.Notes ?? "").Replace(" ", "").Contains(at) && (k.Notes ?? "").Contains("L" + gate.Level);
                if (notes || (near.Contains(k.From) && near.Contains(k.To))) gate.PaintedLinks.Add(k.Id);
            }
        }

        // ---------------------------------------------------------------- where magic does not work

        static readonly Regex SpellTerm = new Regex(@"spell:([a-z_]+)");

        /// <summary>
        /// The highest circle UW2 lets the player cast from runes in Britannia (levels 1 to 8):
        /// combat/RUNES.C player_cast, <c>if ((PlayerLevel - 1) / 8 == 0 &amp;&amp; level &gt; 3) return
        /// fail_spell(3)</c> ("Casting was not successful", no mana spent), tested before the
        /// level, mana and skill checks, with no plot state, quest or item that lifts it. Objects
        /// do not go through player_cast (obj/OBJUSE.C checkSpell, event/WORLDEV.C
        /// inanimate_spell, combat/SPELLS.C do_spell), so scrolls, wands and enchanted things work
        /// there; UW1's player_cast has no such test.
        /// </summary>
        public const int BritanniaCircle = 3;

        /// <summary>UW2's first world, Britannia: levels 1 to 8 (PlayerLevel, 1-based as the editor's levels; (PlayerLevel - 1) / 8 == 0).</summary>
        public static bool InBritannia(GameSession s, int level) { return s != null && s.IsUw2 && level >= 1 && level <= 8; }

        /// <summary>
        /// Whether no spell works at a square, cast or from a thing the player uses: the tile's
        /// no-magic bit (bit 14 of the tile word, combat/SPELLS.C anti_magic_p, both games) or
        /// UW1's level 9 (do_spell: <c>anti_magic_p(OBJ_HOMEX(who), OBJ_HOMEY(who)) || PlayerLevel ==
        /// 9</c>; the player's casts and the objects he uses both pass who = ThePlayer, whose home
        /// square motion/PHYSICS.C keeps at the square he stands on). A square not known (x or y
        /// below 0) is not one.
        /// </summary>
        public static bool NoMagicAt(GameSession s, Func<int, LevelModel> level, int lv, int x, int y)
        {
            if (s != null && !s.IsUw2 && lv == 9) return true;
            if (level == null || x < 0 || y < 0 || x > 63 || y > 63) return false;
            LevelModel m;
            try { m = level(lv); } catch (Exception) { return false; }
            return m != null && m.Present && m.Tiles[x, y] != null && m.Tiles[x, y].NoMagic;
        }

        /// <summary>
        /// What casting an ability from runes needs on top of cast:X in Britannia: "" when every
        /// spell giving it is at or below BritanniaCircle (Leaping, Water Walk), null when none is
        /// (Levitate, Flameproof, Open, Telekinesis, Gate Travel, Reveal, Portal, Iron Flesh: the
        /// runes count for nothing there), else the runes of one of the low spells (Light: Light or
        /// Night Vision, not Daylight alone).
        /// </summary>
        public static string BritanniaCast(GameSession s, string ability)
        {
            var all = Abilities.SpellsGiving(true, ability);
            var low = all.Where(sp => SpellTables.Circle(true, sp) <= BritanniaCircle).ToList();
            if (low.Count == 0) return null;
            if (low.Count == all.Count) return "";
            return string.Join(" | ", low.Select(sp => "(" + Abilities.CastRequirement(s, sp) + ")"));
        }

        /// <summary>
        /// A requirement as it holds for a way, a placement or a warp that starts at the square
        /// (lv, x, y): where the spell is cast decides. Each spell:X becomes
        /// <list type="bullet">
        /// <item>worn:X on a square where no magic works (NoMagicAt): a worn enchantment never goes
        /// through do_spell (game/PLAYDATA.C FixPlayerEquips);</item>
        /// <item>(cast:X &amp; the fact's token) | use:X where a nomana fact holds (UW1's level 7 while
        /// the orb stands: no mana, so no rune casting; UW2's Academy levels 42 to 47 and level 48 west
        /// of x 25), as before;</item>
        /// <item>in UW2's Britannia (levels 1 to 8): use:X when every spell giving X is above circle 3
        /// (BritanniaCast), (cast:X &amp; the runes of a low one) | use:X when only some are.</item>
        /// </list>
        /// A lasting spell cast elsewhere and carried in counts for nothing (the strict reading, as
        /// for the nomana levels): the solve does not follow how long a spell lasts. noMagic, noMana
        /// and britannia say which rule changed something.
        /// </summary>
        public static string CastAt(GameSession s, Func<int, LevelModel> level, FactSet facts, string req, int lv, int x, int y, out bool noMagic, out bool noMana, out bool britannia)
        {
            noMagic = noMana = britannia = false;
            if (string.IsNullOrEmpty(req) || !req.Contains("spell:")) return req;
            req = SpellTerm.Replace(req, mm => Requirement.Normalize(mm.Value));
            if (NoMagicAt(s, level, lv, x, y)) { noMagic = true; return SpellTerm.Replace(req, mm => "worn:" + mm.Groups[1].Value); }
            var nomana = facts == null ? null : facts.NoMana(lv, x, y);
            bool brit = InBritannia(s, lv);
            if (nomana == null && !brit) return req;
            bool mana = false, b = false;
            string res = SpellTerm.Replace(req, mm =>
            {
                string ab = mm.Groups[1].Value;
                var conds = new List<string>();
                if (nomana != null) { conds.Add(string.IsNullOrEmpty(nomana.Until) ? "flag:__never__" : nomana.Until); mana = true; }
                if (brit)
                {
                    string low = BritanniaCast(s, ab);
                    if (low == null) { b = true; return "use:" + ab; }
                    if (low.Length > 0) { conds.Add(low); b = true; }
                }
                if (conds.Count == 0) return mm.Value;
                return string.Format("((cast:{0} & {1}) | use:{0})", ab, string.Join(" & ", conds.Select(c => "(" + c + ")")));
            });
            noMana = mana; britannia = b;
            return res;
        }

        /// <summary>CastAt without the flags.</summary>
        public static string CastAt(GameSession s, Func<int, LevelModel> level, FactSet facts, string req, int lv, int x, int y)
        {
            bool a, b, c;
            return CastAt(s, level, facts, req, lv, x, y, out a, out b, out c);
        }

        /// <summary>
        /// hazard:lava becomes spell:lava; then the spell terms of each edge are narrowed by where it
        /// starts (CastAt): on a no-magic square (both games; the shipped UW2 has none, UW1 sixteen on
        /// level 6) and anywhere on UW1's level 9 to worn sources, on a nomana level to casting with
        /// the fact's token, in UW2's Britannia to the spells of circle 3 and below.
        /// </summary>
        static void Narrow(GameSession s, Func<int, LevelModel> level, NavGraph g, FactSet facts, AbilityZoneResult res)
        {
            foreach (var e in g.Edges)
            {
                string req = e.Requires.Replace("hazard:lava", "spell:lava");
                if (!req.Contains("spell:")) { e.Requires = req; continue; }
                bool noMagic, noMana, brit;
                req = CastAt(s, level, facts, req, e.Level, e.X, e.Y, out noMagic, out noMana, out brit);
                if (noMagic) res.NoMagicEdges++;
                if (noMana) res.NoManaEdges++;
                if (brit) res.BritanniaEdges++;
                string err;
                var r = Requirement.TryParse(req, out err);
                e.Requires = r == null ? req : r.ToString();
            }
        }

        // ---------------------------------------------------------------- placements

        static string Slug(string t) { return new string((t ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_'); }

        /// <summary>Adds a placement; with perObject (an object of the levels) always, else only when the zone has none like it. Returns the placement added, or null.</summary>
        static Placement Add(ZoneSet zs, int zone, string grants, string requires, string notes, string source, bool perObject = false, List<string> usesUp = null)
        {
            if (zone <= ZoneSet.Unassigned) return null;
            if (!perObject && zs.Placements.Any(p => p.Zone == zone && Requirement.Normalize(p.Grants) == Requirement.Normalize(grants) && (p.Requires ?? "") == (requires ?? ""))) return null;
            var added = new Placement { Zone = zone, Grants = grants, Requires = requires ?? "", Notes = notes, Source = source, UsesUp = usesUp != null && usesUp.Count > 0 ? usesUp : null };
            zs.Placements.Add(added);
            return added;
        }

        /// <summary>
        /// The facts' mayBeLost on the placements (docs/UW2-RANDOMIZER-NEXT-STEPS.md 2.1): a
        /// "lost" fact naming a token marks every source of it; one naming a place marks the
        /// placements of the objects of the levels that lie there or are carried by its creature
        /// (KeyItems and the item scan note them as "#N name ..., level L (x, y)").
        /// </summary>
        static void MarkLost(ZoneSet zs, Func<int, LevelModel> level, FactSet facts)
        {
            var lost = facts.OfKind("lost").ToList();
            if (lost.Count == 0) return;
            var models = new Dictionary<int, LevelModel>();
            var byId = facts.Facts.Where(f => f.Id.Length > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var p in zs.Placements)
            {
                if (p.Grants.Length > 0) p.MarkLost(facts.LostToken(p.Grants));
                int l, i;
                Fact own;
                var mm = PlacedObject.Match(p.Notes ?? "");
                if (p.Source == "levels" && mm.Success) { l = int.Parse(mm.Groups[2].Value); i = int.Parse(mm.Groups[1].Value); }
                // a flag fact on an object of a place that may be lost (the key gem of Killorn's lower level)
                else if ((p.Source ?? "").StartsWith("facts: ") && byId.TryGetValue(p.Source.Substring(7), out own) && own.Kind == "flag" && own.Level > 0 && own.Object >= 0) { l = own.Level; i = own.Object; }
                else continue;
                if (!lost.Any(f => f.Level == l)) continue;
                LevelModel m;
                if (!models.TryGetValue(l, out m)) { try { m = level(l); } catch (Exception) { m = null; } models[l] = m; }
                if (m == null || i < 0 || i >= m.Objects.Length) continue;
                p.MarkLost(facts.LostPlace(l, m, m.Objects[i]));
            }
        }

        /// <summary>
        /// Karl's rule: no solve may depend on the death of a creature the facts' kind "spare" names
        /// (UW2's brain creatures, whose deaths make Killorn Keep fall). Every placement of an object
        /// of the levels that is such a creature or lies in one (what it carries, the item its corpse
        /// leaves, an ability item it holds), and every flag fact granted on one, is left out; this is
        /// where creatures' deaths become sources (fights are taken as won, so what a creature carries
        /// is otherwise there to take), so nothing can ask for them later. Each is listed in res.Spared.
        /// </summary>
        static void Spare(ZoneSet zs, Func<int, LevelModel> level, FactSet facts, AbilityZoneResult res)
        {
            if (!facts.OfKind("spare").Any()) return;
            var models = new Dictionary<int, LevelModel>();
            var byId = facts.Facts.Where(f => f.Id.Length > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            Func<int, LevelModel> model = l => { LevelModel m; if (!models.TryGetValue(l, out m)) { try { m = level(l); } catch (Exception) { m = null; } models[l] = m; } return m; };
            zs.Placements.RemoveAll(p =>
            {
                int l, i;
                Fact own;
                var mm = PlacedObject.Match(p.Notes ?? "");
                if (p.Source == "levels" && mm.Success) { l = int.Parse(mm.Groups[2].Value); i = int.Parse(mm.Groups[1].Value); }
                else if ((p.Source ?? "").StartsWith("facts: ") && byId.TryGetValue(p.Source.Substring(7), out own) && own.Kind == "flag" && own.Level > 0 && own.Object >= 0) { l = own.Level; i = own.Object; }
                else return false;
                var m = model(l);
                if (m == null || i < 0 || i >= m.Objects.Length || m.Objects[i].Free) return false;
                var f = facts.Spared(l, m, m.Objects[i]);
                if (f == null) return false;
                res.Spared.Add(string.Format("{0}: {1} ({2}): the creature must live (fact {3})", p.Grants.Length > 0 ? p.Grants : p.Counts, p.Notes, p.Source, f.Id));
                return true;
            });
        }

        static void AddPlacements(GameSession s, Func<int, LevelModel> level, ZoneSet zs, FactSet facts, AbilityZoneResult res)
        {
            // the items that give abilities
            res.Sources = Abilities.Find(s, level, zs);
            foreach (var src in res.Sources)
                Add(zs, src.Zone, src.Token, "", string.Format("#{0} {1} ({2}, {3}{4}), level {5} ({6}, {7})", src.Index, src.Name, src.Spell, src.Kind, src.Charges >= 0 ? " " + src.Charges + " charges" : "", src.Level, src.TileX, src.TileY), "levels", true);
            // casting: the runes in the rune bag (combat/RUNES.C try_cast; ignoring mana and the casting roll); the spell's circle goes with
            // it, as player_cast asks for level 2 * circle - 1 and a new character is level 1 (AbilityOptions.NewCharacterCircle, Timeline's solve)
            foreach (var ab in Abilities.Names)
                foreach (int sp in Abilities.SpellsGiving(s.IsUw2, ab))
                {
                    var cast = Add(zs, zs.StartZone, "cast:" + ab, Abilities.CastRequirement(s, sp), SpellTables.Name(s, sp) + " (" + string.Join(" ", Abilities.Runes(s.IsUw2, sp).Select(r => Abilities.RuneNames[r])) + ")", "facts");
                    if (cast != null) cast.Circle = SpellTables.Circle(s.IsUw2, sp);
                }
            // the rune bag, lock picks, and the items the facts name
            var wanted = new HashSet<string> { "item:rune_bag", "item:lockpick" };
            var mustExist = new HashSet<string>();
            // every item a fact's requirement names (craft, trade, flag, and Phase C's link, warp, goal, needs)
            foreach (var f in facts.Facts)
            {
                var toks = new List<string>();
                foreach (var text in new[] { f.Requires, f.Otherwise })
                {
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var r = Requirement.TryParse(text, out _);
                    if (r != null) r.Tokens(toks);
                }
                foreach (var t in toks) if (t.StartsWith("item:")) { wanted.Add(t); if (f.Kind == "craft" || f.Kind == "trade" || f.Kind == "flag") mustExist.Add(t); }
            }
            // tokens the key items already give (one placement per object: KeyItems) are not scanned again
            foreach (var t in zs.Placements.Select(p => Requirement.Normalize(p.Grants)).ToList()) wanted.Remove(t);
            foreach (var f in facts.OfKind("craft")) wanted.Remove(Requirement.Normalize(f.Grants));
            var corpse = CorpseItems(s);
            var counts = facts.OfKind("count").Where(f => f.Grants.Length > 0 && f.AtLeast > 0).ToList();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present || (wanted.Count == 0 && counts.Count == 0)) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0) continue;
                    int z = zs.ZoneOf(slot.Level, m, o);
                    string where = string.Format("#{0} {1}{2}, level {3} ({4}, {5})", o.Index, o.Name, o.Parent >= 0 ? " in " + m.Objects[o.Parent] : "", slot.Level, o.TileX, o.TileY);
                    string tok = NavGraph.ItemToken(s, o.Item);
                    if (wanted.Contains(tok)) Add(zs, z, tok, "", where, "levels", true);
                    // any food pays Marrowsuck: items 0xB0-0xBF (major 2 minor 3, conv/CONVERSE.C find_barter 1011)
                    if (wanted.Contains("item:food") && o.Item >= 0xB0 && o.Item < 0xC0 && o.Item != 0xBF) Add(zs, z, "item:food", "", where + " (food)", "levels", true);
                    // UW2's dream plant (item 0x114: eaten, the next sleep is a dream of the Ethereal Void; obj/USEITEMS.C UseFood, game/SKILLS.C dream)
                    if (s.IsUw2 && o.Item == 0x114 && wanted.Contains("item:dream_plant")) Add(zs, z, "item:dream_plant", "", where + " (dream plant)", "levels", true);
                    // the stew recipe: a book or scroll whose text number is 0x100 or more (obj/USEITEMS.C UseBook, obj/COMBINE.C make_stew)
                    if (wanted.Contains("item:stew_recipe") && !s.IsUw2 && o.Item >= 0x130 && o.Item < 0x140 && o.Item != 0x13B && !o.Enchanted && o.IsQuantity && (o.Link & 0x1FF) >= 0x100)
                        Add(zs, z, "item:stew_recipe", "", where + " (make_stew)", "levels", true);
                    // a creature that leaves the item when killed (critter/PATHFIND.C build_corpse: item 0xD8 + its corpse field, where it lay)
                    int ci;
                    if (o.IsCritter && corpse.TryGetValue(o.Item, out ci) && wanted.Contains(NavGraph.ItemToken(s, ci)))
                        Add(zs, z, NavGraph.ItemToken(s, ci), "", where + " (killed, it leaves " + s.Names.Get(ci) + ")", "levels", true);
                    // the count facts (Phase D: Shak's 20 gold): each stack adds its quantity (conv/CONVERSE.C count_inv)
                    foreach (var cf in counts)
                        if (cf.Items.Contains(o.Item))
                            zs.Placements.Add(new Placement { Zone = z, Grants = "", Counts = cf.Id, Quantity = CountOf(o), Notes = where + " (" + CountOf(o) + " towards " + cf.Grants + ")", Source = "levels" });
                }
            }
            var factGrants = facts.Granted();
            foreach (var t in wanted)
                if (mustExist.Contains(t) && !factGrants.Contains(t) && !zs.Placements.Any(p => Requirement.Normalize(p.Grants) == t)) res.Warnings.Add("Nothing in the levels grants " + t + ".");
            // the facts
            foreach (var f in facts.Facts)
            {
                switch (f.Kind)
                {
                    case "craft":
                        Add(zs, zs.StartZone, f.Grants, f.Requires, f.Notes, "facts: " + f.Id, false, f.UsesUp)?.MarkLost(f);
                        break;
                    case "trade":
                        foreach (var p in zs.Placements.Where(p => Requirement.Normalize(p.Grants) == Requirement.Normalize(f.Token)))
                        {
                            p.Requires = string.IsNullOrEmpty(p.Requires) ? f.Requires : ZoneLink.JoinAnd(new[] { p.Requires, f.Requires });
                            p.Notes = (p.Notes ?? "") + "; " + f.Notes;
                            if (f.UsesUp.Count > 0) p.UsesUp = (p.UsesUp ?? new List<string>()).Union(f.UsesUp).ToList();
                        }
                        break;
                    case "flag":
                        {
                            LevelModel m = null;
                            try { m = level(f.Level); } catch (Exception) { }
                            if (m != null && f.Object < 0 && f.X >= 0 && f.Y >= 0)
                            {
                                // a square instead of an object
                                int at = zs.NearestZone(f.Level, f.X, f.Y);
                                if (at > ZoneSet.Unassigned) Add(zs, at, f.Grants, f.Requires, f.Notes + string.Format(" (level {0} ({1}, {2}))", f.Level, f.X, f.Y), "facts: " + f.Id, false, f.UsesUp)?.MarkLost(f);
                                else res.Warnings.Add("Facts: " + f.Id + ": no zone at L" + f.Level + " (" + f.X + ", " + f.Y + ").");
                                break;
                            }
                            var o = m != null && f.Object >= 0 && f.Object < m.Objects.Length ? m.Objects[f.Object] : null;
                            if (o == null || o.Free || o.TileX < 0) { res.Warnings.Add("Facts: " + f.Id + ": no object #" + f.Object + " on L" + f.Level + "."); break; }
                            Add(zs, zs.ZoneOf(f.Level, m, o), f.Grants, f.Requires, f.Notes + " (#" + o.Index + " " + o.Name + ")", "facts: " + f.Id, false, f.UsesUp)?.MarkLost(f);
                            break;
                        }
                }
            }
            MarkLost(zs, level, facts);
            Spare(zs, level, facts, res);
            // a thing that works on its own level only: the ways and grants of that level take its flag in place of the ability
            foreach (var src in res.Sources.Where(x => x.OnlyLevel > 0))
            {
                string spell = "spell:" + src.Ability, either = "(" + spell + " | " + src.Token + ")";
                foreach (var k in zs.Links)
                    if (k.Level == src.OnlyLevel && k.Requires != null && k.Requires.Contains(spell) && !k.Requires.Contains(src.Token)) k.Requires = k.Requires.Replace(spell, either);
                foreach (var p in zs.Placements)
                    if (p.Requires != null && p.Requires.Contains(spell) && !p.Requires.Contains(src.Token) && zs.LevelsOf(p.Zone).All(l => l == src.OnlyLevel)) p.Requires = p.Requires.Replace(spell, either);
            }
            // NPCs the facts name must stand somewhere
            foreach (var f in facts.Facts)
            {
                var toks = new List<string>();
                var r = Requirement.TryParse(f.Requires, out _);
                if (r != null) r.Tokens(toks);
                foreach (var t in toks)
                    if (t.StartsWith("npc:") && !zs.Placements.Any(p => Requirement.Normalize(p.Grants) == t)) res.Warnings.Add("Facts: " + f.Id + ": no creature talks as " + t + ".");
            }
        }

        /// <summary>
        /// What opens the locked container an object lies in (however deep), or null: the lock
        /// (0x10F) in a container's list with its locked flag (flag 9) set opens with its key (the
        /// lock's link &amp; 0x1FF; 0: no key fits) or by picking (its Z, below 15), as checkLock reads
        /// it (obj/USEITEMS.C UseCont, checkLock); a locked chest is not broken open. Only UW1's
        /// fixed chests (0x15D) carry locks in the shipped levels.
        /// </summary>
        public static string ContainerLock(LevelModel m, ObjectInfo o)
        {
            var terms = new List<string>();
            for (int up = o.Parent, g = 0; up >= 0 && g < 64; up = m.Objects[up].Parent, g++)
            {
                var h = m.Objects[up];
                if (h.IsCritter || h.IsTrapOrTrigger) break;
                foreach (int c in h.Children)
                {
                    var lk = m.Objects[c];
                    if (lk.Free || lk.Item != 0x10F || lk.Record == null || ((lk.Record[1] >> 1) & 1) == 0) continue;
                    int key = lk.Link & 0x1FF, z = lk.Z;
                    var alts = new List<string>();
                    if (key > 0) alts.Add("key:" + key);
                    if (z < 15) alts.Add("pick:" + z);
                    terms.Add(alts.Count == 0 ? "flag:never_opened" : alts.Count == 1 ? alts[0] : "(" + string.Join(" | ", alts) + ")");
                }
            }
            return terms.Count == 0 ? null : string.Join(" & ", terms);
        }

        /// <summary>How many an object counts as: its quantity, or 1 (conv/CONVERSE.C count_inv: a quantity below 0x200, else 1).</summary>
        public static int CountOf(ObjectInfo o) { return o.IsQuantity && o.Link > 0 && o.Link < 0x200 ? o.Link : 1; }

        static readonly Regex PlacedObject = new Regex(@"^#(\d+) .*?level (\d+) \(");

        /// <summary>
        /// Phase C's corrections to the placements read from the levels:
        /// <list type="bullet">
        /// <item>an item that a non-hostile creature with a name (an NPC: whoami, block 7) carries is
        /// not simply there to take: it needs option:kill_npcs (killing it once its zone is
        /// reached), or what a trade fact for its token says (the quest that gets it), or either;
        /// a hostile creature is fought anyway, so what it carries stays free;</item>
        /// <item>an item kept in a create trap (its template) is only there once a chain the player
        /// sets off creates it: it needs that mechanism's flag (event/TRIGGER.C TRAP_CREATE);</item>
        /// <item>the facts' kind "free" with flags grants each flag in the zone of its place (or at
        /// the start when it names none).</item>
        /// </list>
        /// </summary>
        static void ChainPlacements(GameSession s, Func<int, LevelModel> level, NavGraph g, ZoneSet zs, FactSet facts, AbilityZoneResult res)
        {
            var npcHeld = new Dictionary<string, string>();      // "level:index" -> the NPC's name
            var created = new Dictionary<string, List<string>>(); // "level:index" -> the flags that create it
            var lockedIn = new Dictionary<string, string>();      // "level:index" -> what opens the locked container it lies in
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.Parent < 0) continue;
                    string lk = ContainerLock(m, o);
                    if (lk != null) lockedIn[slot.Level + ":" + o.Index] = lk;
                    // the outermost holder
                    var h = m.Objects[o.Parent];
                    int guard = 0;
                    while (!h.IsCritter && h.Parent >= 0 && guard++ < 16) h = m.Objects[h.Parent];
                    if (!h.IsCritter || h.Record == null || h.Record.Length < 0x1B) continue;
                    string npc = ObjectTexts.NpcName(s, h);
                    int attitude = UltimateEditor.Uw1.Uw1Schemas.MobileObject["Attitude"].Get(h.Record) & 3;   // 0 hostile (include/object.h OBJ_ATTITUDE)
                    if (npc != null && attitude != 0) npcHeld[slot.Level + ":" + o.Index] = npc;
                }
                foreach (var trap in m.Objects)
                {
                    if (trap.Free || trap.Item != 0x187 || trap.Link <= 0 || trap.Link >= m.Objects.Length || Triggers.IsSpawner(m, trap)) continue;
                    var flags = g.Mechanisms.Where(x => x.Level == slot.Level && x.Kind == "create" && x.Trap == trap.Index).Select(x => x.Flag).Distinct().ToList();
                    if (flags.Count == 0) flags.Add(string.Format("flag:never_created_L{0}_{1}", slot.Level, trap.Index));
                    // the template and what it holds
                    var stack = new Stack<int>();
                    stack.Push(trap.Link);
                    var seen = new HashSet<int>();
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        if (i <= 0 || i >= m.Objects.Length || !seen.Add(i)) continue;
                        List<string> l;
                        if (!created.TryGetValue(slot.Level + ":" + i, out l)) created[slot.Level + ":" + i] = l = new List<string>();
                        l.AddRange(flags.Where(f => !l.Contains(f)));
                        foreach (int c in m.Objects[i].Children) stack.Push(c);
                    }
                }
            }
            var traded = new HashSet<string>(facts.OfKind("trade").Select(f => Requirement.Normalize(f.Token)));
            foreach (var p in zs.Placements)
            {
                var mm = PlacedObject.Match(p.Notes ?? "");
                if (!mm.Success) continue;
                string key = mm.Groups[2].Value + ":" + mm.Groups[1].Value;
                List<string> by;
                if (created.TryGetValue(key, out by))
                {
                    p.Requires = ZoneLink.JoinAnd(new[] { p.Requires, string.Join(" | ", by) });
                    p.Notes += "; made by a create trap (" + string.Join(" | ", by) + ")";
                    res.Created.Add(p);
                }
                string lockReq;
                if (lockedIn.TryGetValue(key, out lockReq))
                {
                    p.Requires = ZoneLink.JoinAnd(new[] { p.Requires, lockReq });
                    p.Notes += "; in a locked container (" + lockReq + ")";
                }
                string npc;
                if (npcHeld.TryGetValue(key, out npc) && !p.Grants.StartsWith("npc:"))
                {
                    bool trade = traded.Contains(Requirement.Normalize(p.Grants));
                    string r = string.IsNullOrEmpty(p.Requires) ? "free" : p.Requires;
                    if (trade) p.Requires = r == "free" ? "" : (r.Contains("|") || r.Contains("&") ? "(" + r + ")" : r) + " | option:kill_npcs";
                    else p.Requires = ZoneLink.JoinAnd(new[] { r, "option:kill_npcs" });
                    p.Notes += "; carried by " + npc + (trade ? " (a trade fact, or killing it)" : " (killing it)");
                    res.NpcHeld.Add(p);
                }
            }
            foreach (var f in facts.OfKind("free"))
            {
                if (f.Flags.Count == 0) continue;
                int zone = zs.StartZone;
                var at = f.TopSpot;
                if (at != null)
                {
                    LevelModel m = null;
                    try { m = level(at.Level); } catch (Exception) { }
                    int x = at.X, y = at.Y;
                    if (at.HasObject && m != null && at.Object < m.Objects.Length && !m.Objects[at.Object].Free) { x = m.Objects[at.Object].TileX; y = m.Objects[at.Object].TileY; }
                    if (x >= 0 && y >= 0) zone = zs.NearestZone(at.Level, x, y);
                    else res.Warnings.Add("Facts: " + f.Id + ": no object #" + at.Object + " on L" + at.Level + ".");
                }
                foreach (var t in f.Flags) Add(zs, zone, Requirement.Normalize(t), "", f.Notes, "facts: " + f.Id);
            }
        }

        /// <summary>The item each creature type leaves where it dies (OBJECTS.DAT's creature table, byte 8 bits 5-7: item 0xD8 + it; include/critter.h struct Creature corpse).</summary>
        static Dictionary<int, int> CorpseItems(GameSession s)
        {
            var d = new Dictionary<int, int>();
            try
            {
                var t = ObjectTables.Load(s);
                var bytes = t.ObjectsBytes;
                for (int row = 0; row < 64; row++)
                {
                    int at = 0x132 + row * 48 + 8;
                    if (bytes == null || at >= bytes.Length) break;
                    int c = (bytes[at] >> 5) & 7;
                    if (c > 0) d[0x40 + row] = 0xD8 + c;
                }
            }
            catch (Exception) { }
            return d;
        }

        static void Explain(ZoneSet zs)
        {
            zs.OpenSources["spell:lava"] = "Walking on lava unharmed: Flameproof (cast: Sanct Flam; or a potion), or worn dragon skin boots (the facts: Marrowsuck makes them).";
            zs.OpenSources["worn:lava"] = "Dragon skin boots, worn (crafted: see the facts file), or a worn enchantment of fire resistance.";
            zs.OpenSources["flag:orb_destroyed"] = "Tyball's orb on L7 broken with an orb rock (L8): mana comes back on L7.";
            zs.OpenSources["item:food"] = "Any food (items 0xB0-0xBF): Marrowsuck's payment.";
            zs.OpenSources["item:stew_recipe"] = "Lanugo's stew recipe (L1): a scroll whose text is the stew (obj/COMBINE.C make_stew).";
            foreach (var ab in Abilities.Names)
            {
                zs.OpenSources["cast:" + ab] = "The runes of a spell giving " + ab + ", in the rune bag (Magic option).";
                zs.OpenSources["spell:" + ab] = "Ability " + ab + ": cast (runes, Magic option) or an item (worn, unlimited, or single-use with the option).";
            }
        }

        // ---------------------------------------------------------------- doors to open

        static readonly HashSet<string> DoorKinds = new HashSet<string> { "door", "portcullis", "secret" };

        static bool OnlyPickOrBash(ZoneSet zs, string req, HashSet<string> granted)
        {
            var r = Requirement.TryParse(req, out _);
            if (r == null) return false;
            var toks = new List<string>();
            r.Tokens(toks);
            if (!toks.Any(t => t == "bash" || t.StartsWith("pick:"))) return false;
            // with picking and bashing impossible and every token that some placement grants held, does it still hold?
            // (the Open spell is a pick: it is left out, as it was when it needed lock picking on)
            granted = new HashSet<string>(granted.Where(t => !t.EndsWith(":open")));
            var ctx =new FixedContext(new SolverOptions { AllowBash = true, LockpickSkill = -1, AbilitySources = true, Magic = true, SingleUseItems = true, SearchSkill = 30, OptionsOn = new HashSet<string>(FactSet.KnownOptions) }, granted);
            return !r.Eval(ctx);
        }

        internal sealed class FixedContext : IRequirementContext
        {
            readonly HashSet<string> tokens;
            public SolverOptions Options { get; private set; }
            public FixedContext(SolverOptions o, HashSet<string> t) { Options = o; tokens = t; }
            public bool HasToken(string token) { return tokens.Contains(Requirement.Normalize(token)); }
            public bool ZoneReachable(int zone) { return true; }
            public int TalismanCount() { return 8; }
        }

        /// <summary>
        /// With lock picking off: the door links that nothing but picking opens (no key in the
        /// levels fits, no lever, not breakable: bashing is always in the logic), blocked at the end
        /// of the solve, and what opening each one alone would add.
        /// </summary>
        public static List<DoorToOpen> DoorsToOpen(ZoneSet zs, AbilityOptions opt)
        {
            var list = new List<DoorToOpen>();
            if (opt.LockPicking) return list;
            var so = opt.ToSolver();
            var baseSolve = new ZoneSolver(zs, so).Solve(zs.StartZone, zs.GoalZone);
            var granted = new HashSet<string>(zs.Placements.Select(p => Requirement.Normalize(p.Grants)));
            var have = new HashSet<string>(baseSolve.TokenSphere.Keys);
            foreach (var b in baseSolve.Blocked)
            {
                var k = b.Link;
                if (!DoorKinds.Contains(k.Kind) || !OnlyPickOrBash(zs, k.Requires, granted)) continue;
                if (list.Any(d => d.Link == k)) continue;
                var d0 = new DoorToOpen { Link = k, FromZone = b.FromZone, ToZone = b.ToZone };
                string keep = k.Requires;
                k.Requires = "free";
                try
                {
                    var r = new ZoneSolver(zs, so).Solve(zs.StartZone, zs.GoalZone);
                    foreach (var z in r.ZoneSphere.Keys) if (!baseSolve.ZoneSphere.ContainsKey(z)) d0.Gains.Add(z);
                    foreach (var t in r.TokenSphere.Keys) if (!have.Contains(t)) d0.GainedTokens.Add(t);
                    d0.GoalBehind = r.GoalReached && !baseSolve.GoalReached;
                }
                finally { k.Requires = keep; }
                d0.Gains.Sort();
                d0.GainedTokens.Sort(StringComparer.Ordinal);
                list.Add(d0);
            }
            return list.OrderByDescending(d => d.Matters).ThenByDescending(d => d.Gains.Count).ToList();
        }

        // ---------------------------------------------------------------- the report

        static string Z(ZoneSet zs, int id) { return SolveResult.ZoneName(zs, id); }

        static int FirstSphere(SolveResult sv, SolverOptions so, string ability)
        {
            int best = int.MaxValue, k;
            foreach (var t in new[] { "spell:" + ability, "worn:" + ability, "perm:" + ability })
                if (sv.TokenSphere.TryGetValue(t, out k)) best = Math.Min(best, k);
            if (so.SingleUseItems && sv.TokenSphere.TryGetValue("once:" + ability, out k)) best = Math.Min(best, k);
            if (so.Magic && sv.TokenSphere.TryGetValue("cast:" + ability, out k)) best = Math.Min(best, k);
            return best;
        }

        public static string Report(AbilityZoneResult res)
        {
            var zs = res.Set;
            var sv = res.Solve;
            var so = res.Options.ToSolver();
            var sb = new StringBuilder();
            sb.AppendLine("AUTOMATIC ZONES, PHASE B: ABILITIES AND OPTIONS");
            sb.AppendLine();
            foreach (var w in res.Warnings) sb.AppendLine("Warning: " + w);
            sb.AppendLine("Options: " + res.Options + ".");
            if (res.Spared.Count > 0) sb.AppendLine("Left out, from creatures the logic must spare (the facts' kind spare): " + string.Join("; ", res.Spared) + ".");
            sb.AppendLine("Casting ignores character level, mana and the casting roll (combat/RUNES.C player_cast): holding the runes and the rune bag is enough.");
            sb.AppendLine();
            if (sv != null)
            {
                sb.AppendLine(string.Format("SOLVE: {0} {1} spheres, {2} of {3} zones reached.", sv.GoalReached ? "Goal reached." : "Goal NOT reached.", sv.Spheres.Count, sv.ZoneSphere.Count, zs.Zones.Count(z => z.Id > ZoneSet.Unassigned)));
                var missing = sv.Blocked.SelectMany(b => b.Missing).GroupBy(t => t).OrderByDescending(x => x.Count()).Take(15).ToList();
                if (missing.Count > 0) sb.AppendLine("  What blocks the links out of the reached part (token: links): " + string.Join(", ", missing.Select(x => x.Key + " " + x.Count())));
                if (sv.NoSource.Count > 0) sb.AppendLine("  Needed but granted nowhere: " + string.Join(", ", sv.NoSource.Take(30)) + (sv.NoSource.Count > 30 ? " …" : ""));
                sb.AppendLine();
                if (res.Options.SingleUseItems)
                {
                    // what only scrolls, potions and charged wands open: the zones lost without them, and the links that first reached them
                    var o = res.Options.Clone();
                    o.SingleUseItems = false;
                    var without = new ZoneSolver(zs, o.ToSolver()).Solve(zs.StartZone, zs.GoalZone);
                    var lost = sv.ZoneSphere.Keys.Where(z => !without.ZoneSphere.ContainsKey(z)).OrderBy(z => z).ToList();
                    sb.AppendLine(string.Format("  Relying on single-use items: {0} zones are reached only with them.", lost.Count));
                    foreach (int z in lost)
                    {
                        PathStep st;
                        if (!sv.FirstReach.TryGetValue(z, out st) || st.Link == null) continue;
                        var k = st.Link;
                        if (!Regex.IsMatch(k.Requires, @"\b(spell|use):")) continue;
                        sb.AppendLine(string.Format("    [{0}] {1} -> {2} ({3}): {4}", k.Id, Z(zs, st.Reverse ? k.To : k.From), Z(zs, z), k.Kind, k.Requires));
                    }
                }
                sb.AppendLine("  The main option sets (zones reached, spheres, goal):");
                foreach (var kv in res.Variants)
                    sb.AppendLine(string.Format("    {0}: {1} zones, {2} spheres{3}", kv.Key, kv.Value.ZoneSphere.Count, kv.Value.Spheres.Count, kv.Value.GoalReached ? ", goal reached" : ""));
                sb.AppendLine();
            }

            sb.AppendLine("ABILITIES (sources; the sphere where each first holds with these options)");
            foreach (var ab in Abilities.Names)
            {
                int first = sv == null ? int.MaxValue : FirstSphere(sv, so, ab);
                var items = res.Sources.Where(x => x.Ability == ab).ToList();
                var spells = Abilities.SpellsGiving(zs.Game == "UW2", ab);
                var crafted = zs.Placements.Where(p => Requirement.Normalize(p.Grants) == "worn:" + ab && (p.Source ?? "").StartsWith("facts")).ToList();
                sb.AppendLine(string.Format("  {0}: {1} items ({2} permanent), {3} spells{4}; {5}", ab, items.Count, items.Count(x => x.Permanent), spells.Count, crafted.Count > 0 ? ", crafted" : "", first == int.MaxValue ? "never available" : "first in sphere " + first));
                foreach (int sp in spells) { string nm; res.SpellNames.TryGetValue(sp, out nm); sb.AppendLine(string.Format("    cast {0}: {1}", nm ?? "spell " + sp, string.Join(" ", Abilities.Runes(zs.Game == "UW2", sp).Select(r => Abilities.RuneNames[r])))); }
                foreach (var x in items) sb.AppendLine(string.Format("    {0}{1}: #{2} {3} ({4}){5}, L{6} ({7}, {8}), {9}", x.Permanent ? (x.Worn ? "worn" : "unlimited") : "single-use", x.Kind == "worn" ? "" : " " + x.Kind, x.Index, x.Name, x.Spell, x.Holder != null ? " in " + x.Holder : "", x.Level, x.TileX, x.TileY, Z(zs, x.Zone)));
                foreach (var p in crafted) sb.AppendLine("    crafted: " + p.Requires);
                var gates = zs.Links.Where(k => UsesAbility(k.Requires, ab)).ToList();
                if (gates.Count > 0)
                {
                    sb.AppendLine(string.Format("    gates asking for it: {0} links ({1})", gates.Count, string.Join(", ", gates.GroupBy(k => k.Kind).Select(x => x.Key + " " + x.Count()))));
                    foreach (var k in gates.Take(12)) sb.AppendLine(string.Format("      [{0}] {1} -> {2} ({3}): {4}", k.Id, Z(zs, k.From), Z(zs, k.To), k.Kind, k.Requires));
                    if (gates.Count > 12) sb.AppendLine("      … " + (gates.Count - 12) + " more.");
                }
            }
            sb.AppendLine("  Light gates nothing: no link of the automatic zones asks for it.");
            sb.AppendLine();

            sb.AppendLine("WHERE MAGIC DOES NOT WORK");
            sb.AppendLine(string.Format("  No-magic squares (the tile's bit, combat/SPELLS.C anti_magic_p) and UW1's level 9 refuse every spell, cast or from an item; worn enchantments still work. Edges narrowed to worn sources: {0}.", res.NoMagicEdges));
            foreach (var f in res.Facts.OfKind("nomana")) sb.AppendLine(string.Format("  L{0}{4}: {1} Edges where casting needs {2}: {3}.", f.Level, f.Notes, f.Until, res.NoManaEdges,
                f.Rect != null && f.Rect.Length == 4 ? string.Format(" ({0}, {1}) to ({2}, {3})", f.Rect[0], f.Rect[1], f.Rect[2], f.Rect[3]) : ""));
            if (res.Facts != null && res.Facts.Game == "UW2")
                sb.AppendLine(string.Format("  Britannia (levels 1 to 8): no spell above circle {0} is cast from runes (combat/RUNES.C player_cast); scrolls, wands and worn things still work. Edges narrowed: {1}.", BritanniaCircle, res.BritanniaEdges));
            sb.AppendLine();

            sb.AppendLine(string.Format("HIDDEN GATES (look triggers that open a wall; with Search {0}{1})", res.Options.SearchSkill, res.Options.SearchRetry ? " and retries" : ", one look"));
            foreach (var h in res.Hidden)
            {
                int chance = Requirement.PassChanceIn31(res.Options.SearchSkill, h.Z);
                string pass = h.Z == 0 ? "any look" : string.Format("{0} in 31 a look; Search {1} always", chance, h.Z + 16);
                bool reached = sv != null && h.FromZone > 0 && sv.ZoneSphere.ContainsKey(h.FromZone) && h.ToZones.All(z => sv.ZoneSphere.ContainsKey(z));
                sb.AppendLine(string.Format("  {0}: {1}; Reveal {2}. From {3} to {4}. {5}{6}{7}", h, pass, h.RevealWorks ? "opens it" : "cannot", h.FromZone > 0 ? Z(zs, h.FromZone) : "?", h.ToZones.Count == 0 ? "the same zone" : string.Join(", ", h.ToZones.Select(z => Z(zs, z))), h.Notes.Length > 0 ? h.Notes + ". " : "",
                    h.PaintedLinks.Count > 0 ? "Painted link " + string.Join(", ", h.PaintedLinks) + ". " : "", reached ? "Passed in the solve." : "Not passed in the solve."));
                if (h.FactNotes.Length > 0) sb.AppendLine("    " + h.FactNotes);
            }
            sb.AppendLine();

            sb.AppendLine("WALLS (damage-trap tiles)");
            foreach (var kv in res.Walls) sb.AppendLine(string.Format("  {0}: {1} tiles: {2}", kv.Key, kv.Value.Count, string.Join(", ", kv.Value.Take(40)) + (kv.Value.Count > 40 ? " …" : "")));
            if (res.DamageTilesElsewhere.Count > 0)
                sb.AppendLine(string.Format("  Elsewhere (walled only with the global option): {0} tiles ({1}).", res.DamageTilesElsewhere.Values.Sum(), string.Join(", ", res.DamageTilesElsewhere.Select(kv => "L" + kv.Key + " " + kv.Value))));
            sb.AppendLine();

            sb.AppendLine("THE DRAGON SKIN BOOTS (as the data shows them)");
            var boots = zs.Placements.FirstOrDefault(p => (p.Source ?? "") == "facts: dragonskin_boots");
            if (boots != null)
            {
                int k;
                sb.AppendLine("  worn:lava <= " + boots.Requires + (sv != null && sv.TokenSphere.TryGetValue("worn:lava", out k) ? "  (sphere " + k + ")" : "  (not obtained)"));
                var stew = zs.Placements.FirstOrDefault(p => (p.Source ?? "") == "facts: rotworm_stew");
                if (stew != null) sb.AppendLine("  item:rotworm_stew <= " + stew.Requires);
                var toks = new List<string>();
                Requirement.Parse(boots.Requires).Tokens(toks);
                if (stew != null) Requirement.Parse(stew.Requires).Tokens(toks);
                foreach (var p in zs.Placements.Where(p => p.Grants == "item:dragon_scales")) Requirement.Parse(string.IsNullOrEmpty(p.Requires) ? "free" : p.Requires).Tokens(toks);
                foreach (var t in toks.Distinct().Where(t => t != "item:rotworm_stew" && t != "free"))
                {
                    var ps = zs.Placements.Where(p => Requirement.Normalize(p.Grants) == t).ToList();
                    sb.AppendLine(string.Format("    {0}: {1}{2}", t, ps.Count == 0 ? "nowhere" : string.Join("; ", ps.Take(6).Select(p => Z(zs, p.Zone) + " (" + p.Notes + ")" + (string.IsNullOrEmpty(p.Requires) ? "" : " needs " + p.Requires))) + (ps.Count > 6 ? "; … " + (ps.Count - 6) + " more" : ""),
                        sv != null && sv.TokenSphere.TryGetValue(t, out k) ? "  [sphere " + k + "]" : ""));
                }
                foreach (var p in zs.Placements.Where(p => p.Grants == "item:dragon_scales"))
                    sb.AppendLine(string.Format("    item:dragon_scales: {0} ({1}) needs {2}{3}", Z(zs, p.Zone), p.Notes, p.Requires, sv != null && sv.TokenSphere.TryGetValue("item:dragon_scales", out k) ? "  [sphere " + k + "]" : ""));
            }
            sb.AppendLine();

            if (!res.Options.LockPicking)
            {
                sb.AppendLine("DOORS THE SHUFFLE MUST OPEN (only picking opens them, lock picking off; bashing is always in the logic)");
                foreach (var d in res.DoorsToOpen)
                    sb.AppendLine(string.Format("  {0}[{1}] L{2} ({3}, {4}) {5}: {6} -> {7}: {8}. Opening it alone adds {9} zones{10}{11}.", d.Matters ? "" : "(nothing behind) ", d.Link.Id, d.Link.Level, d.Link.X, d.Link.Y, d.Link.Kind, Z(zs, d.FromZone), Z(zs, d.ToZone), d.Link.Requires,
                        d.Gains.Count, d.GainedTokens.Count > 0 ? ", " + string.Join(", ", d.GainedTokens.Take(10)) : "", d.GoalBehind ? ", and the goal" : ""));
                if (res.DoorsToOpen.Count == 0) sb.AppendLine("  None blocks the logic.");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static bool UsesAbility(string req, string ab)
        {
            return Regex.IsMatch(req ?? "", @"\b(spell|cast|use|worn|perm|once):" + ab + @"\b");
        }
    }
}
