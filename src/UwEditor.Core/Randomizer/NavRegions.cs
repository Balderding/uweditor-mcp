using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The movement assumptions of the automatic zones: the game's jump (<see cref="JumpModel"/>); ceilings are ignored.</summary>
    public sealed class NavOptions
    {
        /// <summary>
        /// The tick length the jumps are worked out at, in 1/256 s: the time of one frame, at most
        /// 64 (motion/PLAYMOVE.C check_physics). Longer ticks give lower, shorter jumps, so a
        /// larger number is the more cautious. 16 is about 16 frames a second.
        /// </summary>
        public int TickLength = 16;
        /// <summary>What a gap jump is asked to spare, in eighths of a tile (2: a quarter tile, one side's footprint), for a take-off that is not on the last cell.</summary>
        public int JumpMargin = 2;
        /// <summary>Gap jumps over lower ground are made at all (kind "jump").</summary>
        public bool GapJumps = true;
        /// <summary>Gap jumps along the diagonals too (a diagonal tile is 1.41 long, and the tiles beside the line must be clear as well).</summary>
        public bool DiagonalJumps = false;

        /// <summary>The highest step in floor levels a jump from a floor of this level gets onto, with nothing or with the jump spell.</summary>
        public int Climb(int floor, bool spell) { return JumpModel.Climb(spell, floor, Math.Max(1, Math.Min(64, TickLength))); }
        /// <summary>Teleports between levels never merge zones (each level's zones stay apart).</summary>
        public bool CutAtLevelChanges = true;
        /// <summary>Groups of regions with one neighbour and no objects (empty pits and ledges) go into that neighbour's zone.</summary>
        public bool FoldEmptyDeadEnds = true;
        /// <summary>The start is the game's own new-game square (UW1 (32, 2)), not the one the executable now holds (moved in the editor).</summary>
        public bool ShippedStart;

        /// <summary>Where the player starts with these options: the new-game square in the executable (or, with ShippedStart, the game's own), else the start of the game opened.</summary>
        public StartPoint StartOf(GameSession s) { return ShippedStart ? s.ShippedNewGameStart() : s.NewGameStart ?? s.Start; }

        // ---- Phase C

        /// <summary>Switches and triggers ("level:index") whose chains count as done once their zone is reached: no pole or levitation for the reach, no conditions along the chain (the facts' kind "free").</summary>
        public HashSet<string> FreeChains = new HashSet<string>();
        /// <summary>A door that a move or pick-up trigger closes behind the player (with a lock) is passed freely only towards the trigger; away from it the closed door's requirement holds (<see cref="NavGraph.Closers"/>).</summary>
        public bool ClosersOneWay;

        public bool IsFree(int level, int index) { return FreeChains != null && FreeChains.Contains(level + ":" + index); }

        public NavOptions Clone()
        {
            var o = (NavOptions)MemberwiseClone();
            o.FreeChains = new HashSet<string>(FreeChains ?? new HashSet<string>());
            return o;
        }
    }

    /// <summary>A door that a trigger the player cannot avoid closes (or toggles), and how the ways through it are treated.</summary>
    public sealed class NavCloser
    {
        public NavMechanism Mechanism;
        public int Level, Door, DoorRegion, X, Y;
        /// <summary>The door's requirement once closed: the trap's lock, or the levers that open it (now_region:N: worked again after the closing).</summary>
        public string After = "free";
        /// <summary>The regions beside the door, and whether the trigger lies on that side (reachable from it without the door).</summary>
        public readonly List<KeyValuePair<int, bool>> Sides = new List<KeyValuePair<int, bool>>();
        /// <summary>one way, both ways closed, no effect.</summary>
        public string Treatment = "";
        public int EdgesChanged;
        public override string ToString() { return string.Format("L{0} door #{1} at ({2}, {3}): {4}; after closing {5}", Level, Door, X, Y, Treatment, After); }
    }

    /// <summary>A set of tiles of one level where the player walks anywhere and back, or a door tile, or a lava patch.</summary>
    public sealed class NavRegion
    {
        public int Id;
        public int Level;
        /// <summary>The tiles whose floor is in it, as y * 64 + x.</summary>
        public readonly List<int> Tiles = new List<int>();
        /// <summary>The decks in it (bridges over other tiles' floors): a region may have only these.</summary>
        public readonly List<NavDeck> Decks = new List<NavDeck>();
        /// <summary>A square an object closes for good (DoorKind "barrier"): a force field, a grating.</summary>
        public bool IsBarrier;
        /// <summary>A door tile: its own node; its requirement is on every edge that touches it.</summary>
        public bool IsDoor;
        public int DoorIndex = -1;
        public string DoorKind;
        public string DoorRequires = "free";
        public string DoorNotes = "";
        public bool Lava;
        /// <summary>A door that every way out of needs more than it did (a closer, NavGraph.Closers): it stays a zone of its own, not given to a neighbour.</summary>
        public bool KeepApart;
        /// <summary>An empty dead end folded into its only neighbour's zone (AutoZones.FoldDeadEnds): a pit or a ledge that may need more to enter or leave than its zone does, so nothing is put there.</summary>
        public bool Folded;
        public int WaterTiles, LavaTiles, NoMagicTiles;
        public int MinHeight = 99, MaxHeight = -1;
        /// <summary>The automatic zone it ended in (0 until zones are made).</summary>
        public int Zone;
        public int X { get { return Tiles.Count == 0 ? (Decks.Count == 0 ? -1 : Decks[0].X) : Tiles[0] % 64; } }
        public int Y { get { return Tiles.Count == 0 ? (Decks.Count == 0 ? -1 : Decks[0].Y) : Tiles[0] / 64; } }
        public override string ToString()
        {
            return string.Format("R{0} L{1} ({2}, {3}) {4} tiles{5}{6}{7}{8}", Id, Level, X, Y, Tiles.Count, Decks.Count > 0 ? " " + Decks.Count + " decks" : "", IsDoor ? " door #" + DoorIndex : "", Lava ? " lava" : WaterTiles > 0 ? " water " + WaterTiles : "", NoMagicTiles > 0 ? " no-magic " + NoMagicTiles : "");
        }
    }

    /// <summary>
    /// The top of a solid object the player walks on above a tile's floor: a bridge (COMOBJ's
    /// Solid flag and radius 4, the whole tile; motion/COLLIDE.C obj_coll_check, MOTION.C
    /// get_pcoll). A tile may have several, at different heights, and its floor under them.
    /// </summary>
    public sealed class NavDeck
    {
        public int Level, X, Y;
        /// <summary>The top and the underside in height units (8 a floor step).</summary>
        public int Top, Bottom;
        /// <summary>The object.</summary>
        public int Object;
        /// <summary>The region it belongs to: the ledges it joins, or one of its own.</summary>
        public int Region = -1;
        /// <summary>The top in floor steps, to the nearest.</summary>
        public int Step { get { return (Top + 4) / 8; } }
        internal int Node;
        public override string ToString() { return string.Format("L{0} ({1}, {2}) deck at {3} (#{4}), R{5}", Level, X, Y, Top, Object, Region); }
    }

    /// <summary>A one-way way from one region to another, with what it needs (the requirement language; region:N stands for a region that must be reached).</summary>
    public sealed class NavEdge
    {
        public int From, To;
        /// <summary>walk, drop, climb, jump, door, portcullis, secret, teleport, stairs (a teleport to another level).</summary>
        public string Kind = "walk";
        public string Requires = "free";
        /// <summary>Where it starts (level, tile).</summary>
        public int Level, X, Y;
        /// <summary>A drop's fall, in floor steps (the largest difference at the edge); the highest of the passages joined.</summary>
        public int Fall;
        /// <summary>How many tile passages this edge stands for.</summary>
        public int Count = 1;
        /// <summary>The trigger of a teleport, or -1.</summary>
        public int Trigger = -1;
        public string Notes = "";
        public override string ToString() { return string.Format("R{0} -> R{1} [{2}] {3}{4}", From, To, Kind, Requires, Fall > 0 ? " fall " + Fall : ""); }
    }

    /// <summary>A switch (lever, button, pull chain) whose use trigger runs a chain, and whether it can be reached from the floor.</summary>
    public sealed class NavLever
    {
        public int Level, Index, Item, X, Y, Z;
        public string Name;
        /// <summary>Regions from which the hand reaches it.</summary>
        public readonly List<int> Reach = new List<int>();
        /// <summary>Regions from which only the pole reaches it.</summary>
        public readonly List<int> PoleReach = new List<int>();
        /// <summary>The nearest region (for a lever nothing reaches).</summary>
        public int Nearest = -1;
        /// <summary>The floor height (steps) it is reached from, and how far above that floor it is (object z units, 8 a step).</summary>
        public int Floor, Above;
        public string What = "";
        public bool OutOfReach { get { return Reach.Count == 0; } }
        /// <summary>The facts make it free (kind "free"): its zone is enough.</summary>
        public bool Free;
        /// <summary>"free", or for a switch in a rock square what opens the square (the mechanisms' flags).</summary>
        public string InRock = "free";
        /// <summary>The requirement for using it: one of its regions reached, with the pole or levitation when the hand does not reach.</summary>
        public string Requires;
        public override string ToString()
        {
            return string.Format("L{0} #{1} {2} ({3}, {4}) z {5}: {6}", Level, Index, Name, X, Y, Z, OutOfReach ? (PoleReach.Count > 0 ? "out of reach, the pole reaches it" : "out of reach, even with the pole") : "in reach");
        }
    }

    /// <summary>A trigger chain that changes the world (not yet applied to the regions).</summary>
    public sealed class NavMechanism
    {
        public int Level;
        public int Trigger, TriggerItem;
        public int Trap, TrapItem;
        /// <summary>terrain, delete, create, hack, variable, door, quest.</summary>
        public string Kind;
        /// <summary>Where the player sets it off, as a requirement (region:N terms), or "" when the player cannot set it off.</summary>
        public string SetOff = "";
        /// <summary>Conditions met on the way along the chain (an item held, a variable's value).</summary>
        public string Conditions = "";
        public string What = "";
        /// <summary>The flag a later phase grants when it runs: flag:terrain_L5_622.</summary>
        public string Flag;
        /// <summary>The square the trap acts on, and for a terrain change the rectangle's size.</summary>
        public int X, Y, W = 1, H = 1;
        /// <summary>A look trigger's height (the Search check, event/TRIGGER.C UseTrigger), or -1 for other triggers.</summary>
        public int Look = -1;
        /// <summary>A door trap's quality: 1 opens, 2 closes, 3 toggles (event/TRIGGER.C TRAP_DOOR); 0 for other traps.</summary>
        public int DoorAction;
        /// <summary>
        /// UW2: a door trap that swaps the lock of the door on its square (owner set), set off by
        /// an open or a close trigger held by another door: an airlock. Opening one door locks
        /// the other and closing it takes the lock off again (the Prison Tower's doors at level
        /// 10 (32, 36) and (34, 36)), so the lock comes and goes and the way stays: not a change
        /// of the world.
        /// </summary>
        public bool Airlock;
        /// <summary>Set off by walking onto it or picking something up: the player does not choose it.</summary>
        public bool Unavoidable;
        /// <summary>A free chain (the facts' kind "free"): its conditions and the reach of its switch were dropped.</summary>
        public bool Free;
        /// <summary>The regions its squares lie in now.</summary>
        public readonly List<int> Regions = new List<int>();
        public override string ToString() { return string.Format("{0}  L{1} trigger #{2} -> {3} #{4}: {5}{6}", Flag, Level, Trigger, Kind, Trap, What, Conditions.Length > 0 ? " (if " + Conditions + ")" : ""); }
    }

    /// <summary>
    /// The walkable regions of the levels and the ways between them, read from the geometry, the
    /// doors and the trigger chains (Phase A of the automatic zones). Movement is heuristic, after
    /// the game's jump (JumpModel): inside a region every step is one a jump gets onto, both ways
    /// (compared at the touching edges; 3 floor steps from a floor of level 10 or lower, 2 from
    /// 11, 1 from 12 up, at the default tick length); a drop is free; a climb above that needs
    /// the jump spell (6, 4 and 2), and above that levitation; a gap is jumped where the flight
    /// clears every tile under it and comes down on the far floor, higher or lower, with a sprint
    /// jump where there is room to run up, else the standing long jump. A solid object that covers
    /// its tile (a bridge) is a deck: a second floor over the tile's own, walked and jumped onto by
    /// the same rules, with the floor under it kept apart (and closed when the deck leaves no
    /// headroom). An object that closes a square (a force field, a grating: a box of radius 3 or
    /// more across the player's body, not solid) is a barrier: nothing passes, and what lies
    /// behind it in a dead end is reached only with Telekinesis. Ceilings are ignored (UW1
    /// has no area cut off by a low one). Water joins regions (UW1 swimming is normal movement,
    /// motion/PHYSICS.C parse_player_terr) but is counted for waterwalk; lava regions stand apart
    /// and entering one needs hazard:lava.
    /// </summary>
    public sealed class NavGraph
    {
        public NavOptions Options { get; private set; }
        public int Levels { get; private set; }
        public readonly List<NavRegion> Regions = new List<NavRegion>();
        public readonly List<NavEdge> Edges = new List<NavEdge>();
        public readonly List<NavLever> Levers = new List<NavLever>();
        public readonly List<NavMechanism> Mechanisms = new List<NavMechanism>();
        /// <summary>Per level (0-based), the region id of each tile (y * 64 + x), -1 for rock or none.</summary>
        public int[][] RegionAt;
        /// <summary>The decks of every level (bridges), with their regions.</summary>
        public readonly List<NavDeck> Decks = new List<NavDeck>();
        Dictionary<int, List<NavDeck>>[] decksAt;
        /// <summary>Things worth saying that are not edges (triggers the player cannot set off, conditions met).</summary>
        public readonly List<string> Notes = new List<string>();
        /// <summary>UW2: the tests of the game's state the chains make that the starting state fails, by token.</summary>
        public readonly Dictionary<string, StateTest> Tests = new Dictionary<string, StateTest>();
        /// <summary>UW2: the set variable traps the player can set off.</summary>
        public readonly List<StateSet> Sets = new List<StateSet>();
        /// <summary>UW2: the state tokens and what grants each (StateRules.Derive); AutoZones makes them placements.</summary>
        public readonly List<NavState> States = new List<NavState>();
        /// <summary>UW2: the tiles of water with a current, and the ice slopes (one way down), by level.</summary>
        public readonly Dictionary<int, int> StreamTiles = new Dictionary<int, int>(), IceSlopes = new Dictionary<int, int>();
        /// <summary>The flag a schedule row grants when it fires the scheduled triggers of a square.</summary>
        public static string ScheduleToken(int level, int x, int y) { return string.Format("flag:schedule_l{0}_{1}_{2}", level, x, y); }
        /// <summary>Doors closed behind the player by a trigger he cannot avoid (with ClosersOneWay).</summary>
        public readonly List<NavCloser> Closers = new List<NavCloser>();

        static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

        public int RegionOf(int level1, int x, int y)
        {
            if (level1 < 1 || level1 > Levels || RegionAt[level1 - 1] == null || x < 0 || y < 0 || x > 63 || y > 63) return -1;
            return RegionAt[level1 - 1][y * 64 + x];
        }

        /// <summary>The decks over a tile, lowest first (null: none).</summary>
        public List<NavDeck> DecksAt(int level1, int x, int y)
        {
            List<NavDeck> l;
            if (level1 < 1 || level1 > Levels || decksAt == null || decksAt[level1 - 1] == null || x < 0 || y < 0 || x > 63 || y > 63) return null;
            return decksAt[level1 - 1].TryGetValue(y * 64 + x, out l) ? l : null;
        }

        /// <summary>The region of what stands or lies at height z (height units) on a tile: the highest deck at or under it, else the tile's floor.</summary>
        public int RegionOf(int level1, int x, int y, int z)
        {
            var l = DecksAt(level1, x, y);
            if (l != null) for (int i = l.Count - 1; i >= 0; i--) if (l[i].Top <= z + 2) return l[i].Region;
            return RegionOf(level1, x, y);
        }

        /// <summary>Whether a square is a barrier's or touches one (north, east, south or west): nothing is put there by the shuffles (Karl: for safety, the object stands across the edge).</summary>
        public bool NearBarrier(int level1, int x, int y)
        {
            for (int d = -1; d < 4; d++)
            {
                int r = d < 0 ? RegionOf(level1, x, y) : RegionOf(level1, x + Dx[d], y + Dy[d]);
                if (r >= 0 && Regions[r].IsBarrier) return true;
            }
            return false;
        }

        /// <summary>The region at (x, y), or the nearest non-door region within radius (Chebyshev rings, as the game's teleport searches a 9 by 9 box: event/WORLDEV.C do_teleport).</summary>
        public int NearestRegion(int level1, int x, int y, int radius = 4, bool allowDoor = false)
        {
            for (int r = 0; r <= radius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int k = RegionOf(level1, x + dx, y + dy);
                        if (k >= 0 && (allowDoor || !Regions[k].IsDoor)) return k;
                    }
            return -1;
        }

        public int EdgeCount(string kind) { return Edges.Count(e => e.Kind == kind); }

        // ---------------------------------------------------------------- building

        /// <summary>Builds the regions, edges, levers and mechanisms of every level present.</summary>
        public static NavGraph Build(GameSession s, Func<int, LevelModel> level, NavOptions options = null, IEnumerable<int> only = null)
        {
            var g = new NavGraph { Options = options ?? new NavOptions(), Levels = s.Profile.LevelSlots };
            g.RegionAt = new int[g.Levels][];
            g.decksAt = new Dictionary<int, List<NavDeck>>[g.Levels];
            g.comobj = ComObjOf(s);
            TerrainTable terrain = null;
            try { terrain = TerrainTable.Load(s); } catch (Exception) { }
            var models = new Dictionary<int, LevelModel>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present || (only != null && !only.Contains(slot.Level))) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                models[slot.Level] = m;
            }
            var lv = new Dictionary<int, LevelNav>();
            foreach (var kv in models) lv[kv.Key] = g.BuildRegions(s, kv.Value, terrain);
            foreach (var kv in models) g.BuildEdges(lv[kv.Key]);
            foreach (var kv in models) g.ReadTriggers(s, lv[kv.Key], lv);
            if (s.IsUw2) g.States.AddRange(StateRules.Derive(g.Tests.Values, g.Sets));
            foreach (var kv in models) g.ReachPastBarriers(lv[kv.Key]);
            g.ApplyDoorRequirements(lv);
            if (g.Options.ClosersOneWay) g.OneWayClosers(s, lv);
            g.Merge();
            return g;
        }

        byte[] comobj;
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameSession, byte[]> comobjOf = new System.Runtime.CompilerServices.ConditionalWeakTable<GameSession, byte[]>();

        /// <summary>COMOBJ.DAT of a game, read once (the graph is built again for every change of a level).</summary>
        static byte[] ComObjOf(GameSession s)
        {
            byte[] c;
            lock (comobjOf)
            {
                if (!comobjOf.TryGetValue(s, out c)) { c = Hitboxes.ReadComObj(s) ?? new byte[0]; comobjOf.Add(s, c); }
            }
            return c.Length == 0 ? null : c;
        }

        /// <summary>Something to stand on in a tile: its floor (Deck null) or a deck.</summary>
        struct Surf
        {
            public int Tile;
            public NavDeck Deck;
            public int Node { get { return Deck == null ? Tile : Deck.Node; } }
        }

        /// <summary>What a level's tiles are, for the walk rules.</summary>
        sealed class LevelNav
        {
            /// <summary>The decks over each tile, lowest first.</summary>
            public Dictionary<int, List<NavDeck>> Decks = new Dictionary<int, List<NavDeck>>();
            public List<NavDeck> AllDecks = new List<NavDeck>();
            /// <summary>A floor with a deck too low over it to stand under.</summary>
            public bool[] Low = new bool[64 * 64];
            /// <summary>The object that closes the square for good, or 0.</summary>
            public int[] Barrier = new int[64 * 64];
            public string[] BarrierName = new string[64 * 64];
            /// <summary>The region of each surface: the floors by tile, then the decks.</summary>
            public int[] RegionOfNode;
            public bool Closed(int k) { return Door[k] || Barrier[k] != 0; }
            /// <summary>The undersides of the objects over each tile that the head may meet (boxes of radius 3 and more: bridges, also those laid as a ceiling, gratings high up), in height units.</summary>
            public Dictionary<int, List<int>> Overhead = new Dictionary<int, List<int>>();

            /// <summary>What is over a tile above a height: the lowest underside above it, or the ceiling (128), in height units.</summary>
            public int Ceiling(int k, int above)
            {
                int c = 128;
                List<int> l;
                if (Overhead.TryGetValue(k, out l)) foreach (int b in l) if (b > above && b < c) c = b;
                return c;
            }

            /// <summary>The highest the feet may get above a floor of this level on a tile, in floor levels (a little under, to be safe).</summary>
            public double Cap(int k, int floor) { return (Ceiling(k, floor * 8) - PlayerHeight) / 8.0 - floor - 0.05; }

            public IEnumerable<Surf> Surfs(int x, int y)
            {
                if (!Open(x, y)) yield break;
                int k = y * 64 + x;
                yield return new Surf { Tile = k };
                List<NavDeck> l;
                if (Decks.TryGetValue(k, out l)) foreach (var d in l) yield return new Surf { Tile = k, Deck = d };
            }

            /// <summary>Whether a surface is open towards dir, and its height in floor steps along that edge.</summary>
            public bool Edge(Surf s, int dir, out int lo, out int hi)
            {
                if (s.Deck != null) { lo = hi = s.Deck.Step; return true; }
                var t = M.Tiles[s.Tile % 64, s.Tile / 64];
                lo = hi = 0;
                if (!ZoneFlood.IsSideOpen(t.Type, dir)) return false;
                ZoneFlood.EdgeHeights(t, dir, out lo, out hi);
                return true;
            }

            /// <summary>Room to stand on a surface: no deck within the player's height over it.</summary>
            public bool Clear(Surf s)
            {
                if (s.Deck == null) return !Low[s.Tile];
                foreach (var c in Decks[s.Tile]) if (c != s.Deck && c.Top > s.Deck.Top && c.Bottom < s.Deck.Top + PlayerHeight) return false;
                return true;
            }

            /// <summary>
            /// Going from a over its edge towards dir onto b, a surface of the next tile: the climb
            /// (negative going down) and the floor it starts from. False when a side is closed,
            /// there is no room to stand on b, or another deck of b's tile is met first: one at or
            /// under a's height and above b (the player comes onto that one), or one across his body.
            /// </summary>
            public bool Step(Surf a, Surf b, int dir, out int rise, out int floor)
            {
                rise = floor = 0;
                int alo, ahi, blo, bhi;
                if (!Edge(a, dir, out alo, out ahi) || !Edge(b, (dir + 2) & 3, out blo, out bhi)) return false;
                rise = blo - ahi; floor = ahi;
                if (!Clear(b)) return false;
                List<NavDeck> l;
                if (Decks.TryGetValue(b.Tile, out l))
                    foreach (var c in l)
                    {
                        if (c == b.Deck) continue;
                        if (c.Step > bhi && c.Step <= ahi) return false;
                        if (c.Step > ahi && c.Bottom < ahi * 8 + PlayerHeight && (b.Deck == null || b.Deck.Top < c.Top)) return false;
                    }
                return true;
            }

            public LevelModel M;
            public int Level;
            public bool[] Door = new bool[64 * 64];
            public ObjectInfo[] DoorObj = new ObjectInfo[64 * 64];
            public bool[] Water = new bool[64 * 64], Lava = new bool[64 * 64];
            public int[] Class = new int[64 * 64];      // floor terrain class: 0 floor, 1 water, 2 lava
            /// <summary>
            /// UW2: the way a tile's water flows (0 north, 1 east, 2 south, 3 west), or -1. The floor
            /// texture's terrain bits 3-5 on water give the current (motion/PHYSICS.C
            /// set_player_phys_params: 1 south, 2 north, 3 west, 4 east); the swimmer cannot go
            /// against it.
            /// </summary>
            public int[] Flow = Enumerable.Repeat(-1, 64 * 64).ToArray();
            /// <summary>UW2: the way an ice slope rises (0 north, 1 east, 2 south, 3 west), or -1: on ice a slope pushes downhill and cannot be walked up.</summary>
            public int[] IceUp = Enumerable.Repeat(-1, 64 * 64).ToArray();

            /// <summary>A tile of a stream: water with a current (its floor, not a bridge over it).</summary>
            public bool Stream(Surf s) { return s.Deck == null && Flow[s.Tile] >= 0; }

            /// <summary>Going from a towards dir onto b is against a current (of either tile) or up an ice slope: not done on foot or swimming.</summary>
            public bool Pushed(Surf a, Surf b, int dir)
            {
                int back = (dir + 2) & 3;
                if (a.Deck == null && (Flow[a.Tile] == back || IceUp[a.Tile] == dir)) return true;
                if (b.Deck == null && (Flow[b.Tile] == back || IceUp[b.Tile] == dir)) return true;
                return false;
            }
            /// <summary>Terms that open each door tile besides its lock (levers, triggers), by tile.</summary>
            public Dictionary<int, List<string>> DoorOpeners = new Dictionary<int, List<string>>();
            public bool Open(int x, int y) { return x >= 0 && y >= 0 && x < 64 && y < 64 && !M.Tiles[x, y].IsSolid; }
        }

        /// <summary>The player's height in height units (COMOBJ.DAT item 0x7F, both games).</summary>
        public const int PlayerHeight = 23;

        /// <summary>
        /// The decks and barriers of a level, from its objects' collision boxes. A deck: solid
        /// and the whole tile, its top more than a walking step above the floor (lower, it is
        /// part of the floor). A barrier: not solid, radius 3 or more (a door's size), across the
        /// body of a player standing on the floor; doors are not counted here.
        /// </summary>
        void ReadBoxes(LevelNav n)
        {
            var m = n.M;
            if (comobj == null) return;
            foreach (var h in Hitboxes.Of(m, comobj))
            {
                if (h.Creature || h.TileX < 0 || h.TileX > 63 || h.TileY < 0 || h.TileY > 63 || !n.Open(h.TileX, h.TileY)) continue;
                if (h.Item >= 0x140 && h.Item <= 0x14F) continue;
                int k = h.TileY * 64 + h.TileX;
                int fl = LevelGeometry.CornerHeights(m.Tiles[h.TileX, h.TileY]).Max() * 8;
                if (h.Radius >= 3 && h.Bottom > fl)
                {
                    List<int> ov;
                    if (!n.Overhead.TryGetValue(k, out ov)) n.Overhead[k] = ov = new List<int>();
                    ov.Add(h.Bottom);
                    // the whole tile under it without the player's height: nobody stands on the floor
                    if (h.Radius == 4 && h.Bottom < fl + PlayerHeight && h.Top > fl + 8) n.Low[k] = true;
                }
                if (h.Solid && h.Radius == 4)
                {
                    if (h.Top <= fl + 8) continue;
                    if (h.Top + PlayerHeight > 128) continue;         // against the ceiling (a bridge laid as a ceiling): nobody stands on it
                    List<NavDeck> l;
                    if (!n.Decks.TryGetValue(k, out l)) n.Decks[k] = l = new List<NavDeck>();
                    if (l.Any(d => Math.Abs(d.Top - h.Top) <= 2)) continue;       // two bridges laid on one another
                    var deck = new NavDeck { Level = m.Level, X = h.TileX, Y = h.TileY, Top = h.Top, Bottom = h.Bottom, Object = h.Index };
                    l.Add(deck);
                    if (h.Bottom < fl + PlayerHeight) n.Low[k] = true;
                }
                else if (!h.Solid && h.Radius >= 3 && h.Bottom < fl + PlayerHeight && h.Top > fl + 8 && !n.Door[k] && n.Barrier[k] == 0)
                {
                    n.Barrier[k] = h.Index;
                    n.BarrierName[k] = m.Objects[h.Index].Name;
                }
            }
            // a floor too near the ceiling to stand on (get_pcoll refuses a step onto a floor whose top and the mover's height pass 127)
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (n.Open(x, y) && LevelGeometry.CornerHeights(m.Tiles[x, y]).Min() * 8 + PlayerHeight > 127) n.Low[y * 64 + x] = true;
            foreach (var l in n.Decks.Values)
            {
                l.Sort((a, b) => a.Top.CompareTo(b.Top));
                foreach (var d in l) { d.Node = 64 * 64 + n.AllDecks.Count; n.AllDecks.Add(d); }
            }
            decksAt[m.Level - 1] = n.Decks;
        }

        LevelNav BuildRegions(GameSession s, LevelModel m, TerrainTable terrain)
        {
            var n = new LevelNav { M = m, Level = m.Level };
            foreach (var o in m.Objects)
                if (!o.Free && o.OnTile && o.Item >= 0x140 && o.Item <= 0x14F && n.Open(o.TileX, o.TileY))
                {
                    int k = o.TileY * 64 + o.TileX;
                    if (!n.Door[k]) { n.Door[k] = true; n.DoorObj[k] = o; }
                }
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var t = m.Tiles[x, y];
                    if (t.IsSolid || terrain == null || t.FloorIndex < 0 || t.FloorIndex >= m.FloorMap.Length) continue;
                    // UW1: a floor's class is its TERRAIN.DAT word >> 4, 1 water and 2 lava (map/TEXTMAPS.C, motion/COLLIDE.C); UW2: bits 6-7
                    int w = terrain.Get(terrain.EntryOf(m.FloorMap[t.FloorIndex], true));
                    int c = s.IsUw2 ? (w >> 6) & 3 : (w >> 4) & 3;
                    n.Class[y * 64 + x] = c;
                    n.Water[y * 64 + x] = c == 1;
                    n.Lava[y * 64 + x] = c == 2;
                    if (s.IsUw2)
                    {
                        int bits = (w & 0x38) >> 3;
                        if (c == 1 && bits >= 1 && bits <= 4) { n.Flow[y * 64 + x] = bits == 1 ? 2 : bits == 2 ? 0 : bits == 3 ? 3 : 1; int q; StreamTiles.TryGetValue(m.Level, out q); StreamTiles[m.Level] = q + 1; }
                        // ice with any of its effects (bits 7: none) on a slope
                        if (c == 3 && bits != 7 && t.Type >= TileType.SlopeUpN && t.Type <= TileType.SlopeUpW)
                        {
                            n.IceUp[y * 64 + x] = t.Type == TileType.SlopeUpN ? 0 : t.Type == TileType.SlopeUpE ? 1 : t.Type == TileType.SlopeUpS ? 2 : 3;
                            int q; IceSlopes.TryGetValue(m.Level, out q); IceSlopes[m.Level] = q + 1;
                        }
                    }
                }
            ReadBoxes(n);
            // union of the two-way steps, over the floors and the decks
            var parent = new int[64 * 64 + n.AllDecks.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            Func<int, int> find = null;
            find = i => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    foreach (var sa in n.Surfs(x, y))
                    {
                        int a = sa.Tile;
                        if (sa.Deck == null && (n.Closed(a) || n.Low[a])) continue;
                        for (int d = 0; d < 2; d++)     // north and east cover every pair once
                            foreach (var sb in n.Surfs(x + Dx[d], y + Dy[d]))
                            {
                                int b = sb.Tile;
                                if (sb.Deck == null && (n.Closed(b) || n.Low[b])) continue;
                                if ((sa.Deck == null && n.Lava[a]) != (sb.Deck == null && n.Lava[b])) continue;
                                // a stream is a region of its own (its ways in and out go by the current: Streams); an ice slope is one way
                                bool sta = n.Stream(sa), stb = n.Stream(sb);
                                if (sta != stb) continue;
                                if (!sta && (n.Pushed(sa, sb, d) || n.Pushed(sb, sa, (d + 2) & 3))) continue;
                                int up, down, fa, fb;
                                if (!n.Step(sa, sb, d, out up, out fa) || !n.Step(sb, sa, (d + 2) & 3, out down, out fb)) continue;
                                if (up <= ClimbFrom(n, sa, fa, false) && down <= ClimbFrom(n, sb, fb, false)) parent[find(sa.Node)] = find(sb.Node);
                            }
                    }
            var at = new int[64 * 64];
            for (int i = 0; i < at.Length; i++) at[i] = -1;
            n.RegionOfNode = new int[parent.Length];
            for (int i = 0; i < parent.Length; i++) n.RegionOfNode[i] = -1;
            var byRoot = new Dictionary<int, NavRegion>();
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int k = y * 64 + x;
                    if (!n.Open(x, y)) continue;
                    NavRegion r;
                    if (n.Door[k])
                    {
                        var d = n.DoorObj[k];
                        string notes;
                        r = new NavRegion { Id = Regions.Count, Level = m.Level, IsDoor = true, DoorIndex = d.Index, DoorKind = (d.Item & 7) == 6 ? "portcullis" : (d.Item & 7) == 7 ? "secret" : "door" };
                        r.DoorRequires = ZoneDoors.Requirement(s, m, d, out notes);
                        r.DoorNotes = notes;
                        Regions.Add(r);
                    }
                    else if (n.Barrier[k] != 0)
                    {
                        // closed for good: a door that nothing opens, a zone of its own
                        r = new NavRegion { Id = Regions.Count, Level = m.Level, IsDoor = true, IsBarrier = true, KeepApart = true, DoorIndex = n.Barrier[k], DoorKind = "barrier", DoorRequires = BarrierToken };
                        r.DoorNotes = string.Format("{0} #{1} closes the square", n.BarrierName[k], n.Barrier[k]);
                        Regions.Add(r);
                    }
                    else if (!byRoot.TryGetValue(find(k), out r))
                    {
                        r = new NavRegion { Id = Regions.Count, Level = m.Level, Lava = n.Lava[k] };
                        Regions.Add(r);
                        byRoot[find(k)] = r;
                    }
                    r.Tiles.Add(k);
                    at[k] = r.Id;
                    n.RegionOfNode[k] = r.Id;
                    var t = m.Tiles[x, y];
                    if (n.Water[k]) r.WaterTiles++;
                    if (n.Lava[k]) r.LavaTiles++;
                    // no magic: the tile's bit (combat/SPELLS.C anti_magic_p), and all of UW1's level 9 (do_spell refuses there)
                    if (t.NoMagic || (!s.IsUw2 && m.Level == 9)) r.NoMagicTiles++;
                    r.MinHeight = Math.Min(r.MinHeight, t.Height);
                    r.MaxHeight = Math.Max(r.MaxHeight, t.Height + (t.Type >= TileType.SlopeUpN ? 1 : 0));
                }
            foreach (var d in n.AllDecks)
            {
                NavRegion r;
                if (!byRoot.TryGetValue(find(d.Node), out r))
                {
                    r = new NavRegion { Id = Regions.Count, Level = m.Level };
                    Regions.Add(r);
                    byRoot[find(d.Node)] = r;
                }
                r.Decks.Add(d);
                d.Region = r.Id;
                n.RegionOfNode[d.Node] = r.Id;
                r.MinHeight = Math.Min(r.MinHeight, d.Step);
                r.MaxHeight = Math.Max(r.MaxHeight, d.Step);
                Decks.Add(d);
            }
            RegionAt[m.Level - 1] = at;
            return n;
        }

        /// <summary>What a barrier's square asks for: a token nothing grants.</summary>
        public const string BarrierToken = "blocked";

        /// <summary>Whether (x, y) is open towards dir, the climb from its edge into the neighbour (negative going down), and the floor the climb starts from.</summary>
        static bool Rise(LevelModel m, int x, int y, int dir, out int rise, out int floor)
        {
            rise = 0; floor = 0;
            int nx = x + Dx[dir], ny = y + Dy[dir];
            if (nx < 0 || ny < 0 || nx > 63 || ny > 63) return false;
            var a = m.Tiles[x, y];
            var b = m.Tiles[nx, ny];
            if (a.IsSolid || b.IsSolid || !ZoneFlood.IsSideOpen(a.Type, dir) || !ZoneFlood.IsSideOpen(b.Type, (dir + 2) & 3)) return false;
            int alo, ahi, blo, bhi;
            ZoneFlood.EdgeHeights(a, dir, out alo, out ahi);
            ZoneFlood.EdgeHeights(b, (dir + 2) & 3, out blo, out bhi);
            rise = blo - ahi;
            floor = ahi;
            return true;
        }

        /// <summary>The geometric part of a step: free, a drop, the jump spell or levitation.</summary>
        string ClimbRequirement(LevelNav n, Surf from, int rise, int floor, out string kind)
        {
            // a drop: deeper than a jump from the bottom gets back up
            if (rise <= ClimbFrom(n, from, floor, false)) { kind = rise < 0 && -rise > Options.Climb(floor + rise, false) ? "drop" : "walk"; return "free"; }
            kind = "climb";
            return rise <= ClimbFrom(n, from, floor, true) ? "spell:jump" : "spell:levitate";
        }

        readonly Dictionary<long, int> climbs = new Dictionary<long, int>();

        /// <summary>
        /// The highest step a jump from a surface gets onto, with nothing or with the jump spell:
        /// the jump from that floor's height, cut where the head meets the ceiling or an
        /// object's underside over the tile (JumpFlight).
        /// </summary>
        int ClimbFrom(LevelNav n, Surf from, int floor, bool spell)
        {
            int ceiling = n.Ceiling(from.Tile, floor * 8);
            long key = (long)ceiling << 16 | (uint)(floor << 1 | (spell ? 1 : 0));
            int c;
            if (climbs.TryGetValue(key, out c)) return c;
            int dt = Math.Max(1, Math.Min(64, Options.TickLength));
            c = new JumpFlight(JumpKind.Standing, spell, floor, dt, (ceiling - PlayerHeight) / 8.0 - floor - 0.05).Climb;
            climbs[key] = c;
            return c;
        }

        void AddEdge(NavEdge e) { Edges.Add(e); }

        void BuildEdges(LevelNav n)
        {
            var m = n.M;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    foreach (var sa in n.Surfs(x, y))
                    {
                        int a = sa.Tile, ra0 = n.RegionOfNode[sa.Node];
                        if (ra0 < 0) continue;
                        for (int d = 0; d < 4; d++)
                            foreach (var sb in n.Surfs(x + Dx[d], y + Dy[d]))
                            {
                                int b = sb.Tile, rb0 = n.RegionOfNode[sb.Node];
                                if (rb0 < 0 || rb0 == ra0) continue;
                                int rise, floor;
                                if (!n.Step(sa, sb, d, out rise, out floor)) continue;
                                // the floor under a low deck: nobody stands there
                                if (sa.Deck == null && n.Low[a]) continue;
                                // into and out of a stream: by the current (Streams)
                                if (n.Stream(sa) || n.Stream(sb)) continue;
                                string kind;
                                var req = new List<string>();
                                string geo = ClimbRequirement(n, sa, rise, floor, out kind);
                                req.Add(geo);
                                // up an ice slope: only off the ground
                                if (n.Pushed(sa, sb, d)) { req.Add("spell:levitate"); if (kind == "walk") kind = "climb"; }
                                var ra = Regions[ra0];
                                var rb = Regions[rb0];
                                if (rb.IsDoor || ra.IsDoor)
                                {
                                    var door = rb.IsDoor ? rb : ra;
                                    if (kind == "walk") kind = door.DoorKind;
                                    req.Add("door:" + door.Id);     // replaced by the door's requirement once the levers are known
                                }
                                bool lavaB = sb.Deck == null && n.Lava[b], lavaA = sa.Deck == null && n.Lava[a];
                                if (lavaB && !lavaA) req.Add("hazard:lava");
                                AddEdge(new NavEdge { From = ra0, To = rb0, Kind = kind, Requires = ZoneLink.JoinAnd(req), Level = m.Level, X = x, Y = y, Fall = kind == "drop" ? -rise : 0 });
                            }
                    }
            Streams(n);
            if (Options.GapJumps) GapJumps(n);
        }

        /// <summary>
        /// UW2's spring pad: a jump trap under a trigger the player walks onto (motion/MOTION.C
        /// set_jmp) throws him up at force * 47 / 2 with the jump spell's gravity (2) and halves
        /// his forward speed (a trap with owner set stops him dead: it only lifts, and is left
        /// out). From the pad's square, towards each side: the next square when its floor is
        /// within the pad's climb (he rises almost straight up at a walk), and the squares
        /// beyond, up to four, that a running player's flight comes down on with the squares
        /// between cleared. The flight is counted from the pad's near edge and from its middle,
        /// and a square must do for both. The ceiling over the pad cuts the rise.
        /// </summary>
        void SpringPad(LevelNav n, ObjectInfo t, ObjectInfo trap, string cond)
        {
            int px = t.TileX, py = t.TileY;
            if (!n.Open(px, py)) return;
            var pad = new Surf { Tile = py * 64 + px };
            int from = n.RegionOfNode[pad.Node];
            if (from < 0) return;
            int dt = Math.Max(1, Math.Min(64, Options.TickLength));
            double v = trap.Quality * 47 / 2 - 2.0 * dt / 2, u = (JumpModel.SprintSpeed - 1) / 2.0;
            double b = 4 * v / u, c = 131072 * 2 / (u * u);
            Func<double, double> z = x => b * x - c * x * x - JumpModel.Sag(true, dt) - Math.Min(Math.Max(x, 0), b / (2 * c)) / 64;
            for (int d = 0; d < 4; d++)
            {
                int lo, floor;
                if (!n.Edge(pad, d, out lo, out floor)) continue;
                double cap = n.Cap(pad.Tile, floor);
                if (cap < 0) continue;
                double peak = Math.Min(Math.Max(0, v * v / 4 / 16384 - JumpModel.Sag(true, dt)), cap);
                int climb = Math.Max(JumpModel.WalkStep, (int)Math.Floor(peak + 1));
                // the lowest the feet are over a stretch, and the highest (cut by what is overhead)
                Func<double, double, double> least = (x0, x1) => Math.Min(Math.Min(z(x0), z(x1)), cap);
                Func<double, double, double> most = (x0, x1) => { double top = b / (2 * c); return Math.Min(x0 <= top && top <= x1 ? z(top) : Math.Max(z(x0), z(x1)), cap); };
                for (int k = 1; k <= 4; k++)
                {
                    int tx = px + Dx[d] * k, ty = py + Dy[d] * k;
                    if (!n.Open(tx, ty)) break;
                    int tk = ty * 64 + tx;
                    if (n.Closed(tk)) break;
                    bool stop = false;
                    foreach (var sb in n.Surfs(tx, ty))
                    {
                        int blo, bhi;
                        if (!n.Edge(sb, (d + 2) & 3, out blo, out bhi) || !n.Clear(sb)) { if (sb.Deck == null) stop = true; continue; }
                        int rise = bhi - floor;
                        bool lands = k == 1 ? rise <= climb : most(k - 0.5, k + 1) >= rise - 1 && most(k - 0.5, k + 1) <= cap + 1e-9 && least(k - 0.5, k - 0.5) >= rise - 1;
                        int to = n.RegionOfNode[sb.Node];
                        if (lands && to >= 0 && to != from && !Regions[to].IsDoor && !Edges.Exists(e => e.From == from && e.To == to && e.Trigger == t.Index && e.Kind == "jump"))
                            AddEdge(new NavEdge
                            {
                                From = from, To = to, Kind = "jump", Requires = string.IsNullOrEmpty(cond) ? "free" : cond, Level = n.Level, X = px, Y = py, Trigger = t.Index,
                                Notes = string.Format("spring pad (jump trap #{0}, force {1}: up to {2} levels) to ({3}, {4})", trap.Index, trap.Quality, climb, tx, ty),
                            });
                        // a floor the flight does not clear ends it here
                        if (sb.Deck == null && least(k - 0.5, k + 1) < rise) stop = true;
                    }
                    if (stop) break;
                }
            }
        }

        /// <summary>Over the water, whichever way it flows.</summary>
        public const string OverWater = "spell:waterwalk | spell:levitate";

        /// <summary>
        /// UW2's streams: water with a current carries the swimmer and cannot be swum against
        /// (not a step against the current of the tile he is on, nor into a tile whose current
        /// comes at him). A stream (the current tiles that touch) is one region. From a bank
        /// the player gets in where he does not step against the current, and is carried to
        /// every bank he can get out at from there: a way from bank to bank ("carried by the
        /// current"). Out of the stream as a region (for a thing lying in it) only the banks
        /// reached from every one of its tiles are free. Every other way in or out needs to be
        /// over the water: Water Walk, Levitate or Fly.
        /// </summary>
        void Streams(LevelNav n)
        {
            var tiles = new Dictionary<int, List<int>>();
            for (int k = 0; k < 64 * 64; k++)
            {
                if (n.Flow[k] < 0 || n.RegionOfNode[k] < 0) continue;
                List<int> l;
                if (!tiles.TryGetValue(n.RegionOfNode[k], out l)) tiles[n.RegionOfNode[k]] = l = new List<int>();
                l.Add(k);
            }
            var made = new HashSet<string>();
            Action<int, int, string, string, int, string> edge = (from, to, kind, req, at, notes) =>
            {
                if (from == to || !made.Add(from + ">" + to + ">" + req)) return;
                AddEdge(new NavEdge { From = from, To = to, Kind = kind, Requires = req, Level = n.Level, X = at % 64, Y = at / 64, Notes = notes });
            };
            foreach (var kv in tiles)
            {
                int S = kv.Key;
                var set = new HashSet<int>(kv.Value);
                // where the current lets the swimmer go from each tile
                Func<int, HashSet<int>> carried = t0 =>
                {
                    var seen = new HashSet<int> { t0 };
                    var queue = new Queue<int>();
                    queue.Enqueue(t0);
                    while (queue.Count > 0)
                    {
                        int u = queue.Dequeue();
                        var su = new Surf { Tile = u };
                        for (int d = 0; d < 4; d++)
                        {
                            int vx = u % 64 + Dx[d], vy = u / 64 + Dy[d];
                            if (!n.Open(vx, vy)) continue;
                            int v = vy * 64 + vx;
                            if (!set.Contains(v) || seen.Contains(v)) continue;
                            var sv = new Surf { Tile = v };
                            int rise, floor;
                            if (!n.Step(su, sv, d, out rise, out floor) || rise > ClimbFrom(n, su, floor, false) || n.Pushed(su, sv, d)) continue;
                            seen.Add(v);
                            queue.Enqueue(v);
                        }
                    }
                    return seen;
                };
                var reach = kv.Value.ToDictionary(t0 => t0, t0 => carried(t0));
                var common = new HashSet<int>(kv.Value);
                foreach (var r in reach.Values) common.IntersectWith(r);
                var ins = new List<KeyValuePair<int, int>>();       // bank region, stream tile
                var outs = new List<KeyValuePair<int, int>>();      // stream tile, bank region
                foreach (int u in kv.Value)
                {
                    var su = new Surf { Tile = u };
                    for (int d = 0; d < 4; d++)
                        foreach (var sb in n.Surfs(u % 64 + Dx[d], u / 64 + Dy[d]))
                        {
                            if (n.Stream(sb) || (sb.Deck == null && n.Low[sb.Tile])) continue;
                            int B = n.RegionOfNode[sb.Node];
                            if (B < 0 || B == S) continue;
                            string door = Regions[B].IsDoor ? "door:" + Regions[B].Id : "";
                            string doorKind = Regions[B].IsDoor ? Regions[B].DoorKind : "walk";
                            int rise, floor;
                            // out onto the bank
                            if (n.Step(su, sb, d, out rise, out floor) && rise <= ClimbFrom(n, su, floor, false))
                            {
                                bool with = !n.Pushed(su, sb, d);
                                if (with && door.Length == 0) outs.Add(new KeyValuePair<int, int>(u, B));
                                edge(S, B, doorKind, ZoneLink.JoinAnd(new[] { with && common.Contains(u) ? "" : OverWater, door }), u, with && common.Contains(u) ? "out of the stream, where its current brings every swimmer" : "out of the stream against or across its current");
                            }
                            // in from the bank
                            int back = (d + 2) & 3;
                            if (n.Step(sb, su, back, out rise, out floor) && rise <= ClimbFrom(n, sb, floor, false))
                            {
                                bool with = !n.Pushed(sb, su, back);
                                if (with && door.Length == 0) ins.Add(new KeyValuePair<int, int>(B, u));
                                edge(B, S, doorKind, ZoneLink.JoinAnd(new[] { with ? "" : OverWater, door }), sb.Tile, with ? "into the stream" : "into the stream against its current");
                            }
                        }
                }
                foreach (var a in ins)
                    foreach (var b in outs)
                        if (a.Key != b.Value && reach[a.Value].Contains(b.Key))
                            edge(a.Key, b.Value, "other", "free", a.Value, string.Format("carried by the current from ({0}, {1}) to ({2}, {3})", a.Value % 64, a.Value / 64, b.Key % 64, b.Key / 64));
            }
        }

        /// <summary>
        /// What lies behind a barrier in a dead end (a region, or the barrier's own square, with
        /// no way on but through the barrier) is reached from the other side with Telekinesis:
        /// its things are taken and its switches used from afar (game/PLAYDATA.C: PickDist 0),
        /// though nobody walks there. Karl: UW2 level 34 (30, 50), the things behind the grating.
        /// The line of sight is not checked.
        /// </summary>
        void ReachPastBarriers(LevelNav n)
        {
            var outOf = new Dictionary<int, HashSet<int>>();
            foreach (var e in Edges)
            {
                HashSet<int> l;
                if (!outOf.TryGetValue(e.From, out l)) outOf[e.From] = l = new HashSet<int>();
                l.Add(e.To);
            }
            var add = new List<NavEdge>();
            foreach (var bar in Regions.Where(r => r.IsBarrier && r.Level == n.Level).ToList())
            {
                HashSet<int> beside;
                if (!outOf.TryGetValue(bar.Id, out beside)) continue;
                // the dead ends: nothing leads out of them but the barrier
                var pockets = beside.Where(p => !Regions[p].IsDoor && Regions[p].Tiles.Count > 0 && (!outOf.ContainsKey(p) || outOf[p].All(t => t == bar.Id))).ToList();
                var open = beside.Where(p => !pockets.Contains(p) && !Regions[p].IsBarrier).ToList();
                if (open.Count == 0) continue;
                foreach (int from in open)
                {
                    foreach (int p in pockets.Concat(new[] { bar.Id }))
                        add.Add(new NavEdge
                        {
                            From = from, To = p, Kind = "reach", Requires = "spell:telekinesis", Level = n.Level, X = bar.X, Y = bar.Y,
                            Notes = string.Format("past {0}: reached from afar only", bar.DoorNotes),
                        });
                }
            }
            Edges.AddRange(add);
        }

        /// <summary>
        /// The jumps over lower ground (JumpModel). From each tile and along each direction the
        /// flight of each jump the player has there is followed: the standing long jump, the
        /// sprint jump where the tiles behind give the run-up, and both with the jump spell.
        /// The player's centre leaves a quarter tile past the edge (the back corners still on
        /// the ledge) and he lands when the front corners are over the far floor, less the
        /// margin. A tile under the flight must lie below the feet for as long as the footprint
        /// is over it; the far floor is reached when the feet are still at or above it on
        /// arrival (on the way up: within the step the jump climbs). A jump that would carry
        /// too far is taken earlier or slower: that is not checked.
        /// </summary>
        void GapJumps(LevelNav n)
        {
            var m = n.M;
            var free = new HashSet<long>();
            foreach (var e in Edges) if (e.Level == m.Level && e.Requires == "free") free.Add((long)e.From << 32 | (uint)e.To);
            var dirs = new List<int[]> { new[] { 0, 1 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { -1, 0 } };
            if (Options.DiagonalJumps) { dirs.Add(new[] { 1, 1 }); dirs.Add(new[] { 1, -1 }); dirs.Add(new[] { -1, 1 }); dirs.Add(new[] { -1, -1 }); }
            int dt = Math.Max(1, Math.Min(64, Options.TickLength));
            double margin = Math.Max(0, Options.JumpMargin) / 8.0, runUp = JumpModel.RunUp(dt);
            double t0 = JumpModel.Radius - margin / 2;       // the centre past the edge at the take-off
            var best = new Dictionary<long, NavEdge>();
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    foreach (var sa in n.Surfs(x, y))
                    {
                        int a = y * 64 + x, from = n.RegionOfNode[sa.Node];
                        if (from < 0 || Regions[from].IsDoor || (sa.Deck == null && n.Low[a])) continue;
                        var ta = m.Tiles[x, y];
                        foreach (var d in dirs)
                        {
                            bool diag = d[0] != 0 && d[1] != 0;
                            int card = d[1] > 0 ? 0 : d[0] > 0 ? 1 : d[1] < 0 ? 2 : 3;
                            int nearLo, nearHi;
                            if (sa.Deck != null) nearLo = nearHi = sa.Deck.Step;
                            else if (diag) { nearLo = LevelGeometry.CornerHeights(ta).Min(); nearHi = LevelGeometry.CornerHeights(ta).Max(); if (ta.Type != TileType.Open && ta.Type < TileType.SlopeUpN) continue; }
                            else { if (!ZoneFlood.IsSideOpen(ta.Type, card)) continue; ZoneFlood.EdgeHeights(ta, card, out nearLo, out nearHi); }
                            // nothing to jump over: the next tile is not all lower (or over the head)
                            int fx = x + d[0], fy = y + d[1];
                            if (!Under(n, fx, fy, nearLo - 0.01, nearLo - 0.01, nearLo - 0.01)) continue;
                            double len = diag ? 1.4142 : 1;
                            // the run-up: this tile and the tiles straight behind it with a surface of the same region
                            double room = len;
                            for (int k = 1; room < runUp; k++)
                            {
                                int bx = x - d[0] * k, by = y - d[1] * k;
                                if (!n.Surfs(bx, by).Any(sb => n.RegionOfNode[sb.Node] == from)) break;
                                room += len;
                            }
                            for (int pass = 0; pass < 4; pass++)
                            {
                                var kind = (pass & 1) == 0 ? JumpKind.Standing : JumpKind.Sprint;
                                bool leap = pass >= 2;
                                if (kind == JumpKind.Sprint && room < runUp) continue;
                                // the take-off floor: its low side for the heights, its high side for the cut of the jump on high floors.
                                // The head: the flight is cut under the lowest thing it would meet, the ceiling or an object's underside,
                                // over the take-off tile and each tile it comes to (taken to hold from the take-off on: never more than the game gives)
                                double cap = n.Cap(a, nearLo);
                                if (cap < 0) break;
                                var flight = new JumpFlight(kind, leap, nearHi, dt, cap);
                                double top = flight.PeakAt, peak = nearLo + flight.Peak;
                                int climb = flight.Climb;
                                Func<double, double> feet = xx => nearLo + flight.Height(xx);
                                for (int k = 1; k <= 14; k++)
                                {
                                    int tx = x + d[0] * k, ty = y + d[1] * k;
                                    if (!n.Open(tx, ty)) break;
                                    int tk = ty * 64 + tx;
                                    if (n.Closed(tk)) break;
                                    {
                                        // what is over this tile, if the feet's highest point while over it would bring the head to it
                                        double ua = Math.Max(0, (k - 1) * len - JumpModel.Radius - t0), ub = k * len + JumpModel.Radius - t0;
                                        double most = ua <= top && top <= ub ? peak : Math.Max(feet(ua), feet(ub));
                                        double capHere = n.Cap(tk, nearLo);
                                        if (most - nearLo > capHere)
                                        {
                                            if (capHere < 0) break;
                                            cap = capHere;
                                            flight = new JumpFlight(kind, leap, nearHi, dt, cap);
                                            top = flight.PeakAt; peak = nearLo + flight.Peak; climb = flight.Climb;
                                        }
                                    }
                                    var tt = m.Tiles[tx, ty];
                                    bool sideIn = diag ? tt.Type == TileType.Open || tt.Type >= TileType.SlopeUpN : ZoneFlood.IsSideOpen(tt.Type, (card + 2) & 3);
                                    double start = (k - 1) * len;                     // the gap so far
                                    if (diag)
                                    {
                                        // the footprint also crosses the two tiles beside the corner it goes through
                                        double sa0 = Math.Max(0, start - len / 2 - JumpModel.Radius - t0), sb0 = start + len / 2 + JumpModel.Radius - t0;
                                        double hi0 = sa0 <= top && top <= sb0 ? peak : Math.Max(feet(sa0), feet(sb0));
                                        if (!Under(n, tx, ty - d[1], feet(sa0), feet(sb0), hi0) || !Under(n, tx - d[0], ty, feet(sa0), feet(sb0), hi0)) break;
                                    }
                                    if (k > 1)
                                    {
                                        // landing here, on its floor or on a deck: the front corners over it, the feet not under it
                                        double arrive = Math.Max(0, start - 2 * JumpModel.Radius + margin);
                                        foreach (var sb in n.Surfs(tx, ty))
                                        {
                                            double far;
                                            if (sb.Deck != null) far = sb.Deck.Top / 8.0;
                                            else
                                            {
                                                if (!sideIn || n.Low[tk]) continue;
                                                int farLo, farHi;
                                                if (diag) farLo = farHi = LevelGeometry.CornerHeights(tt).Max();
                                                else ZoneFlood.EdgeHeights(tt, (card + 2) & 3, out farLo, out farHi);
                                                far = farHi;
                                            }
                                            if (!n.Clear(sb)) continue;
                                            double dh = far - nearLo;
                                            bool rising = arrive <= top;
                                            bool lands = rising ? dh <= climb : feet(arrive) >= far;
                                            if (!lands) continue;
                                            // the decks over it must be over the head on arrival (else he comes onto them, or into them)
                                            double standing = rising ? Math.Max(far, nearLo) : feet(arrive);
                                            List<NavDeck> over;
                                            if (n.Decks.TryGetValue(tk, out over) && over.Any(c => c != sb.Deck && c.Top / 8.0 > far && c.Bottom < standing * 8 + PlayerHeight)) continue;
                                            int to = n.RegionOfNode[sb.Node];
                                            if (to < 0 || to == from || free.Contains((long)from << 32 | (uint)to)) continue;
                                            string req = leap ? "spell:jump" : "free";
                                            if (sb.Deck == null && n.Lava[tk] && !(sa.Deck == null && n.Lava[a])) req = req == "free" ? "hazard:lava" : req + " & hazard:lava";
                                            long key = (long)from << 32 | (uint)to;
                                            NavEdge old;
                                            int idh = (int)Math.Round(dh);
                                            if (!best.TryGetValue(key, out old) || (old.Requires.Contains("spell:jump") && !leap))
                                                best[key] = new NavEdge
                                                {
                                                    From = from, To = to, Kind = "jump", Requires = req, Level = m.Level, X = x, Y = y,
                                                    Notes = string.Format("{0} over {1} tile{2} to ({3}, {4}){5}{6}{7}{8}", kind == JumpKind.Sprint ? "sprint jump" : "standing long jump", k - 1, k == 2 ? "" : "s", tx, ty,
                                                        idh == 0 ? "" : idh > 0 ? ", " + idh + " up" : ", " + -idh + " down", diag ? ", diagonal" : "", sa.Deck != null ? ", from a deck" : "", sb.Deck != null ? ", onto a deck" : ""),
                                                };
                                            else if (old.X != x || old.Y != y || !old.Notes.Contains("(" + tx + ", " + ty + ")")) old.Count++;
                                        }
                                    }
                                    // flying on over it
                                    if (!sideIn || (!diag && !ZoneFlood.IsSideOpen(tt.Type, card))) break;
                                    double xa = Math.Max(0, start - JumpModel.Radius - t0), xb = start + len + JumpModel.Radius - t0;
                                    if (!Under(n, tx, ty, feet(xa), feet(xb), xa <= top && top <= xb ? peak : Math.Max(feet(xa), feet(xb)))) break;
                                }
                            }
                        }
                    }
            Edges.AddRange(best.Values);
        }

        /// <summary>
        /// Whether a tile lets a jump over it: its floor below the feet while the footprint is
        /// over it (feet at the two ends; a curve that only bends down is lowest at one of them),
        /// and each deck either below the feet too or over the head all the way (highest: the
        /// feet's highest point there).
        /// </summary>
        static bool Under(LevelNav n, int x, int y, double feetA, double feetB, double highest)
        {
            if (!n.Open(x, y) || n.Closed(y * 64 + x)) return false;
            double low = Math.Min(feetA, feetB);
            if (LevelGeometry.CornerHeights(n.M.Tiles[x, y]).Max() >= low) return false;
            List<NavDeck> l;
            if (n.Decks.TryGetValue(y * 64 + x, out l))
                foreach (var c in l)
                    if (!(c.Top / 8.0 < low || c.Bottom >= highest * 8 + PlayerHeight)) return false;
            return true;
        }

        // ---------------------------------------------------------------- triggers

        /// <summary>A trap met along a chain, the square the chain runs at there, and the conditions passed on the way.</summary>
        sealed class ChainStep
        {
            public ObjectInfo Trap;
            public int RunX, RunY;
            public List<string> Conditions;
            /// <summary>UW2: the rectangle a proximity trap before it asks the player to stand in (x0, y0, x1, y1), or null.</summary>
            public int[] Near;
        }

        /// <summary>
        /// UW2's chain (event/TRIGGER.C UseTrap): as UW1's, and the condition traps branch. A
        /// check variable trap goes on when the variables match and else runs its other chain
        /// (the object after the one it links to, RUN_ELSE_CHAIN); a skill trap goes on when the
        /// check fails and runs the other chain when it passes; a proximity trap goes on with the
        /// player inside its rectangle; an inventory trap goes on with the item held. The state
        /// the game starts in costs nothing; any other is a condition (StateTest.Token). A skill
        /// trap that rolls (heading 1) or reads the player's own numbers may go either way; one
        /// that compares "skill" 3, the constant 15, plainly runs only one of its chains. A door
        /// trap that swaps a lock (owner set) ends the chain.
        /// </summary>
        static void WalkUw2(GameSession s, LevelModel m, ObjectInfo next, int rx, int ry, List<string> conds, int[] near, HashSet<int> seen, List<ChainStep> list, Dictionary<string, StateTest> tests)
        {
            while (next != null && list.Count < 256)
            {
                if (!seen.Add(next.Index)) return;
                if (Triggers.IsTrigger(next.Item)) { rx = next.Quality; ry = next.Owner; next = Triggers.LinksTo(m, next); continue; }
                if (!Triggers.IsTrap(next.Item)) return;
                list.Add(new ChainStep { Trap = next, RunX = rx, RunY = ry, Conditions = new List<string>(conds), Near = near });
                int it = next.Item;
                var link = Triggers.LinksTo(m, next);
                var other = link != null && link.Next > 0 && link.Next < m.Objects.Length && !m.Objects[link.Next].Free ? m.Objects[link.Next] : null;
                Action<string> elseChain = cond =>
                {
                    if (other == null) return;
                    var c = new List<string>(conds);
                    if (cond != null) c.Add(cond);
                    WalkUw2(s, m, other, rx, ry, c, near, new HashSet<int>(seen), list, tests);
                };
                switch (it)
                {
                    case 0x18C:
                        elseChain(null);
                        conds = new List<string>(conds) { ItemToken(s, next.Quality << 5 | next.Owner) };
                        break;
                    case 0x18E:
                        {
                            var yes = StateTest.Of(next, false);
                            var no = StateTest.Of(next, true);
                            bool atStart = yes.Holds(i => 0);
                            if (tests != null) tests[(atStart ? no : yes).Token] = atStart ? no : yes;
                            elseChain(atStart ? no.Token : null);
                            if (!atStart) conds = new List<string>(conds) { yes.Token };
                            break;
                        }
                    case 0x18A:
                        {
                            // quality 3 is the constant 15, compared plainly unless the heading asks for a roll: value < owner * 3 fails
                            bool known = next.Quality == 3 && next.Heading != 1;
                            bool fails = 15 < next.Owner * 3;
                            if (!known || !fails) elseChain(null);
                            if (known && !fails) return;
                            break;
                        }
                    case 0x196:
                        elseChain(null);
                        near = new[] { rx, ry, rx + next.Quality, ry + next.Owner };
                        break;
                }
                if (it == 0x187 || it == 0x18B || it == 0x189 || (it == 0x188 && next.Owner != 0)) return;
                next = link;
            }
        }

        /// <summary>
        /// The traps a trigger's chain runs, in order (event/TRIGGER.C UseTrap, SetOffTrap): a trigger
        /// met along the way runs its own chain at its own target square; create object, delete
        /// object, ward, tell and UW1's door trap end the chain; an inventory trap ends it when the
        /// item is missing (so later traps need the item), a check-variable trap branches.
        /// </summary>
        static List<ChainStep> Walk(GameSession s, LevelModel m, ObjectInfo trigger, Dictionary<string, StateTest> tests = null)
        {
            var list = new List<ChainStep>();
            if (s.IsUw2)
            {
                WalkUw2(s, m, Triggers.LinksTo(m, trigger), trigger.Quality, trigger.Owner, new List<string>(), null, new HashSet<int> { trigger.Index }, list, tests);
                return list;
            }
            var conds = new List<string>();
            int rx = trigger.Quality, ry = trigger.Owner;
            var seen = new HashSet<int> { trigger.Index };
            var next = Triggers.LinksTo(m, trigger);
            while (next != null && seen.Add(next.Index) && list.Count < 256)
            {
                if (Triggers.IsTrigger(next.Item)) { rx = next.Quality; ry = next.Owner; next = Triggers.LinksTo(m, next); continue; }
                if (!Triggers.IsTrap(next.Item)) break;
                list.Add(new ChainStep { Trap = next, RunX = rx, RunY = ry, Conditions = new List<string>(conds) });
                int it = next.Item;
                if (it == 0x18C) conds.Add(ItemToken(s, next.Quality << 5 | next.Owner));
                else if (it == 0x18E && !s.IsUw2) conds.Add(string.Format("flag:var{0}_is_{1}", next.Z, (next.Quality * 32 | next.Owner) * 8 + next.FineY));
                if (it == 0x187 || it == 0x18B || it == 0x189 || (!s.IsUw2 && (it == 0x18A || it == 0x188))) break;
                next = Triggers.LinksTo(m, next);
            }
            return list;
        }

        static string Slug(string t) { return new string((t ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_'); }

        /// <summary>The token an item grants when held (the key items' own token when it has one).</summary>
        public static string ItemToken(GameSession s, int item)
        {
            string tok;
            if (!s.IsUw2 && KeyItems.Uw1Notable.TryGetValue(item, out tok)) return tok;
            return "item:" + Slug(s.Names.Get(item));
        }

        static bool PlayerSetsOff(ObjectInfo t) { return ((t.Record[1] >> 3) & 1) != 0; }      // id bit 11 (event/TRIGGER.C UseTrigger)

        /// <summary>A trigger placed by the level, not one reached along another chain.</summary>
        static bool IsRoot(LevelModel m, ObjectInfo o)
        {
            if (o.Free || o.TileX < 0 || !Triggers.IsTrigger(o.Item)) return false;
            return o.Parent < 0 || !m.Objects[o.Parent].IsTrapOrTrigger;
        }

        static readonly string[] TriggerVerb = { "walk onto", "pick up", "use", "look at", "step on", "open", "unlock" };

        void ReadTriggers(GameSession s, LevelNav n, Dictionary<int, LevelNav> all)
        {
            var m = n.M;
            int L = m.Level;
            // the levers first: their reach
            var leverOf = new Dictionary<int, NavLever>();
            foreach (var o in m.Objects)
            {
                if (o.Free || !o.OnTile || o.Item < 0x170 || o.Item > 0x17F) continue;
                bool holds = false;
                foreach (int c in o.Children) if (Triggers.IsTrigger(m.Objects[c].Item)) holds = true;
                if (!holds) continue;
                var lv = Lever(n, o);
                Levers.Add(lv);
                leverOf[o.Index] = lv;
            }
            foreach (var t in m.Objects)
            {
                if (!IsRoot(m, t)) continue;
                int ti = t.Item - 0x1A0;
                if (!s.IsUw2 && ti == 4) continue;      // UW1's step-on trigger never goes off (no code reports mode 1)
                var holder = t.Parent >= 0 ? m.Objects[t.Parent] : null;
                NavLever lever = null;
                if (holder != null) leverOf.TryGetValue(holder.Index, out lever);
                // where the player sets it off
                string setOff;
                var fromRegions = new List<int>();
                string extra = "";
                bool free = Options.IsFree(L, t.Index) || (holder != null && Options.IsFree(L, holder.Index));
                if (lever != null && free)
                {
                    // the facts say the switch is no hindrance: any region near it will do
                    var near = lever.Reach.Concat(lever.PoleReach).ToList();
                    if (near.Count == 0 && lever.Nearest >= 0) near.Add(lever.Nearest);
                    fromRegions.AddRange(near.Distinct());
                    setOff = near.Count == 0 ? "" : ZoneLink.JoinAnd(new[] { Or(near.Select(r => "region:" + r)), lever.InRock });
                    lever.Requires = setOff.Length == 0 ? lever.Requires : setOff;
                    lever.Free = true;
                }
                else if (lever != null)
                {
                    setOff = lever.Requires;
                    fromRegions.AddRange(lever.Reach.Count > 0 ? lever.Reach : lever.PoleReach.Count > 0 ? lever.PoleReach : new List<int> { lever.Nearest });
                    if (lever.Reach.Count == 0) extra = lever.PoleReach.Count > 0 ? PoleOrLevitate : "spell:levitate";
                }
                else
                {
                    int r = NearestRegion(L, t.TileX, t.TileY, 2, true);
                    if (r >= 0) fromRegions.Add(r);
                    setOff = r >= 0 ? "region:" + r : "";
                }
                // UW2's trigger kinds come twice (0x1A0.. and 0x1B0..): 9 a timer, 12 a scheduled trigger; neither is set off by the player's own action,
                // so the flag that lets the player set a trigger off (event/TRIGGER.C UseTrigger, ID_FLAG11) is not asked of them
                int mode = s.IsUw2 ? ti & 0xF : ti;
                bool player = s.IsUw2 ? mode == 9 || mode == 12 || PlayerSetsOff(t) : PlayerSetsOff(t);
                if (s.IsUw2 && mode == 12)
                {
                    // a scheduled trigger goes off when a schedule row fires the triggers of its square: a flag, until the schedules are read
                    setOff = ScheduleToken(L, t.TileX, t.TileY);
                    fromRegions.Clear();
                }
                if (!player)
                {
                    if (s.IsUw2 && setOff.Length > 0) Notes.Add(string.Format("L{0} trigger #{1} {2} at ({3}, {4}): the player cannot set it off (no flag 11).", L, t.Index, t.Name, t.TileX, t.TileY));
                    setOff = "";
                }
                string how = (s.IsUw2 ? "trigger" : TriggerVerb[Math.Min(ti, TriggerVerb.Length - 1)]) + (holder != null ? " " + holder.Name : "");
                foreach (var st in Walk(s, m, t, Tests))
                {
                    var trap = st.Trap;
                    string cond = free ? "free" : ZoneLink.JoinAnd(st.Conditions);
                    if (s.IsUw2 && trap.Item == 0x192 && player && (mode == 0 || mode == 6) && trap.Owner == 0)
                        SpringPad(n, t, trap, ZoneLink.JoinAnd(new[] { extra, cond }));
                    if (s.IsUw2 && trap.Item == 0x183 && trap.Quality == 55 && player)
                    {
                        // the blackrock gem: each facet takes the player to its world once the castle plot lets the gem show it
                        foreach (var w in StateRules.GemWorlds)
                        {
                            int dest = NearestRegion(w[0], w[1], w[2]);
                            if (dest < 0) continue;
                            foreach (int fr in fromRegions)
                            {
                                // (the gem itself closes its square: nobody stands there)
                                if (fr < 0 || fr == dest || Regions[fr].IsBarrier || Edges.Exists(e => e.From == fr && e.To == dest && e.Kind == "stairs" && e.Trigger == t.Index)) continue;
                                AddEdge(new NavEdge
                                {
                                    From = fr, To = dest, Kind = "stairs", Requires = ZoneLink.JoinAnd(new[] { extra, cond, w[3] > 0 ? "plot:" + w[3] : "" }), Level = L, X = t.TileX, Y = t.TileY, Trigger = t.Index,
                                    Notes = string.Format("the blackrock gem (trigger #{0}, hack 55): the facet to L{1} ({2}, {3}){4}", t.Index, w[0], w[1], w[2], w[3] > 0 ? ", shown from castle plot " + w[3] : ""),
                                });
                            }
                        }
                    }
                    if (Triggers.IsTeleport(trap.Item))
                    {
                        if (!player) { Notes.Add(string.Format("L{0} trigger #{1} at ({2}, {3}) teleports, but the player cannot set it off (no flag 11).", L, t.Index, t.TileX, t.TileY)); continue; }
                        int to = trap.Z == 0 ? L : trap.Z;
                        int dest = NearestRegion(to, trap.Quality, trap.Owner);
                        if (dest < 0) { Notes.Add(string.Format("L{0} teleport #{1} (trigger #{2}) lands on L{3} ({4}, {5}): no open square there.", L, trap.Index, t.Index, to, trap.Quality, trap.Owner)); continue; }
                        // past a proximity trap the player stands in its rectangle, wherever the trigger is
                        var starts = fromRegions;
                        if (st.Near != null && setOff.Length > 0)
                        {
                            starts = new List<int>();
                            for (int py = st.Near[1]; py <= st.Near[3]; py++)
                                for (int px = st.Near[0]; px <= st.Near[2]; px++)
                                {
                                    int pr = px < 0 || py < 0 || px > 63 || py > 63 ? -1 : RegionOf(L, px, py);
                                    if (pr >= 0 && !starts.Contains(pr)) starts.Add(pr);
                                }
                        }
                        foreach (int fr in starts)
                        {
                            if (fr < 0 || (fr == dest)) continue;
                            var req = new List<string> { extra, cond };
                            AddEdge(new NavEdge
                            {
                                From = fr, To = dest, Kind = to != L ? "stairs" : "teleport", Requires = ZoneLink.JoinAnd(req), Level = L, X = t.TileX, Y = t.TileY, Trigger = t.Index,
                                Notes = string.Format("{0} #{1} ({2}, {3}) -> teleport #{4} to L{5} ({6}, {7})", Triggers.IsTrigger(t.Item) ? (s.IsUw2 ? "trigger" : Uw1Name(t.Item)) : "", t.Index, t.TileX, t.TileY, trap.Index, to, trap.Quality, trap.Owner),
                            });
                        }
                        continue;
                    }
                    if (trap.Item == 0x188 && (trap.Quality == 1 || trap.Quality == 3) && player)
                    {
                        // a door trap opens the door on the square its chain runs at (event/TRIGGER.C, UW1 TRAP_DOOR)
                        int k = st.RunY * 64 + st.RunX;
                        if (st.RunX >= 0 && st.RunX < 64 && st.RunY >= 0 && st.RunY < 64 && n.Door[k])
                        {
                            List<string> ops;
                            if (!n.DoorOpeners.TryGetValue(k, out ops)) n.DoorOpeners[k] = ops = new List<string>();
                            ops.Add(ZoneLink.JoinAnd(new[] { setOff, cond }));
                            if (lever != null) lever.What += (lever.What.Length > 0 ? "; " : "") + string.Format("opens the {0} at ({1}, {2})", Regions[RegionOf(L, st.RunX, st.RunY)].DoorKind, st.RunX, st.RunY);
                        }
                    }
                    var mech = Mechanism(s, m, t, st, how);
                    if (mech == null) continue;
                    mech.SetOff = setOff;
                    mech.Conditions = cond == "free" ? "" : cond;
                    mech.Free = free;
                    mech.Look = ti == 3 ? t.Z : -1;
                    mech.Unavoidable = !s.IsUw2 && (ti == 0 || ti == 1);
                    if (trap.Item == 0x188) mech.DoorAction = trap.Quality;
                    if (s.IsUw2 && trap.Item == 0x188 && trap.Owner != 0 && (mode == 10 || mode == 11) && holder != null && holder.Item >= 0x140 && holder.Item <= 0x14F) mech.Airlock = true;
                    if (lever != null) lever.What += (lever.What.Length > 0 ? "; " : "") + mech.Kind + ": " + mech.What;
                    if (mech.Kind == "door" && lever != null) continue;     // a lever's door is on the door's edges
                    // UW2: the traps of one trigger behind different conditions are different mechanisms
                    var same = Mechanisms.Find(x => x.Level == L && x.Trigger == t.Index && x.Kind == mech.Kind && (!s.IsUw2 || x.Conditions == mech.Conditions));
                    if (same == null && s.IsUw2 && Mechanisms.Exists(x => x.Level == L && x.Trigger == t.Index && x.Kind == mech.Kind)) mech.Flag += "_" + trap.Index;
                    if (s.IsUw2 && trap.Item == 0x18D && setOff.Length > 0)
                    {
                        var set = Sets.Find(x => x.Level == L && x.Trap == trap.Index);
                        if (set == null) Sets.Add(set = new StateSet { Level = L, Trap = trap.Index, Variable = trap.Z + (trap.FineX << 7), Op = trap.Heading & 7, Value = (trap.Quality << 6) + trap.Owner });
                        string fl = (same ?? mech).Flag;
                        if (!set.Flags.Contains(fl)) set.Flags.Add(fl);
                    }
                    if (same != null)
                    {
                        // one mechanism per trigger and kind: the effects joined
                        int colon = mech.What.IndexOf(": ");
                        same.What += "; " + (colon >= 0 ? mech.What.Substring(colon + 2) : mech.What);
                        foreach (int r in mech.Regions) if (!same.Regions.Contains(r)) same.Regions.Add(r);
                        continue;
                    }
                    Mechanisms.Add(mech);
                }
            }
        }

        static string Uw1Name(int item) { return UltimateEditor.Uw1.Uw1Traps.IsTrigger(item) ? UltimateEditor.Uw1.Uw1Traps.TriggerNames[item - 0x1A0].ToLowerInvariant() : "trap"; }

        NavMechanism Mechanism(GameSession s, LevelModel m, ObjectInfo t, ChainStep st, string how)
        {
            var trap = st.Trap;
            int L = m.Level;
            var me = new NavMechanism { Level = L, Trigger = t.Index, TriggerItem = t.Item, Trap = trap.Index, TrapItem = trap.Item, X = st.RunX, Y = st.RunY };
            string flag;
            switch (trap.Item)
            {
                case 0x185:
                    {
                        // change terrain (event/TRIGGER.C TRAP_CHANGE_TERRAIN, event/WORLDEV.C change_terrain): the rectangle x..x+fine x, y..y+fine y
                        me.Kind = "terrain";
                        me.W = trap.FineX + 1; me.H = trap.FineY + 1;
                        var parts = new List<string>();
                        int type = trap.Heading * 2 + (trap.Quality & 1);
                        if (type == 15) type = 10;
                        int height = trap.Z / 8, floor = trap.Quality / 2;
                        bool uw2 = s.IsUw2;
                        if (type < 10) parts.Add(type == 0 ? "rock" : type == 1 ? "open" : type < 6 ? "diagonal" : "slope");
                        if (height < (uw2 ? 15 : 14)) parts.Add("height " + height);
                        if (floor < (uw2 ? 15 : 11)) parts.Add("floor texture " + floor);
                        if (trap.Owner < (uw2 ? 63 : 48)) parts.Add("wall texture " + trap.Owner);
                        me.What = string.Format("({0}, {1}){2} becomes {3}", me.X, me.Y, me.W * me.H > 1 ? string.Format(" to ({0}, {1})", me.X + me.W - 1, me.Y + me.H - 1) : "", parts.Count == 0 ? "the same" : string.Join(", ", parts));
                        break;
                    }
                case 0x18B:
                    {
                        me.Kind = "delete";
                        me.X = trap.Quality; me.Y = trap.Owner;
                        var o = trap.Link > 0 && trap.Link < m.Objects.Length ? m.Objects[trap.Link] : null;
                        me.What = string.Format("deletes #{0} {1} from ({2}, {3})", trap.Link, o == null || o.Free ? "?" : o.Name, me.X, me.Y);
                        if (o != null && !o.Free && o.Item >= 0x140 && o.Item <= 0x14F) me.What += " (a door)";
                        break;
                    }
                case 0x187:
                    {
                        if (Triggers.IsSpawner(m, trap)) return null;
                        me.Kind = "create";
                        var o = trap.Link > 0 && trap.Link < m.Objects.Length ? m.Objects[trap.Link] : null;
                        me.What = string.Format("creates a copy of #{0} {1} at ({2}, {3}){4}", trap.Link, o == null || o.Free ? "?" : o.Name, me.X, me.Y, trap.Quality > 0 ? string.Format(", {0:0} %", Triggers.SpawnChance(trap.Quality)) : "");
                        break;
                    }
                case 0x183:
                    me.Kind = "hack";
                    me.What = s.IsUw2 ? "hack " + trap.Quality : UltimateEditor.Uw1.Uw1Traps.HackTrapName(trap.Quality);
                    if (!s.IsUw2 && UltimateEditor.Uw1.Uw1Traps.HackTrapName(trap.Quality) == "No effect in UW1") return null;
                    break;
                case 0x18D:
                    me.Kind = "variable";
                    me.What = Triggers.Short(s, trap) + (s.IsUw2 ? "" : string.Format(" (operation {0}, value {1})", trap.Heading, (trap.Quality * 32 | trap.Owner) * 8 + trap.FineY));
                    if (!s.IsUw2 && trap.Z == 0) me.Kind = "quest";
                    break;
                case 0x188:
                    me.Kind = "door";
                    me.What = Triggers.Short(s, trap) + string.Format(" at ({0}, {1})", me.X, me.Y);
                    break;
                default:
                    return null;
            }
            flag = string.Format("flag:{0}_L{1}_{2}", me.Kind, L, t.Index);
            me.Flag = flag;
            me.What = Uw1HowText(how) + ": " + me.What;
            if (me.Kind == "terrain")
            {
                for (int y = me.Y; y < me.Y + me.H; y++)
                    for (int x = me.X; x < me.X + me.W; x++)
                    {
                        int r = RegionOf(L, x, y);
                        if (r >= 0 && !me.Regions.Contains(r)) me.Regions.Add(r);
                    }
            }
            return me;
        }

        static string Uw1HowText(string how) { return how; }

        // the player's reach (ui/INTERACT.C InPickRange, BlockingTerrain; game/PLAYDATA.C init_spells):
        // squared distance in 1/8 tiles at most 0x90 (0x190 with the pole), the object at most 24 z
        // units above the feet and 8 below (48 and 16 with the pole), and for the hand no tile on the
        // line lower than the feet less a step, nor of another terrain class.
        const int HandDist = 0x90, PoleDist = 0x190;

        /// <summary>
        /// What reaches a switch the hand does not: the pole, Telekinesis (PickDist 0, so no
        /// distance or height limit: game/PLAYDATA.C player_affected_by, ui/INTERACT.C InPickRange;
        /// Karl: the same uses as the pole for now), or levitating up to it.
        /// </summary>
        public const string PoleOrLevitate = "item:pole | spell:telekinesis | spell:levitate";

        NavLever Lever(LevelNav n, ObjectInfo o)
        {
            var m = n.M;
            int L = m.Level;
            var lv = new NavLever { Level = L, Index = o.Index, Item = o.Item, X = o.TileX, Y = o.TileY, Z = o.Z, Name = o.Name };
            int ox = o.TileX * 8 + o.FineX, oy = o.TileY * 8 + o.FineY, oz = o.Z;
            int bestGap = int.MaxValue;
            double nearest = double.MaxValue;
            for (int ty = o.TileY - 3; ty <= o.TileY + 3; ty++)
                for (int tx = o.TileX - 3; tx <= o.TileX + 3; tx++)
                {
                    int r = RegionOf(L, tx, ty);
                    if (r < 0 || Regions[r].IsDoor) continue;
                    // the nearest point of the tile to the lever
                    int px = Math.Max(tx * 8, Math.Min(tx * 8 + 7, ox)), py = Math.Max(ty * 8, Math.Min(ty * 8 + 7, oy));
                    int d2 = (px - ox) * (px - ox) + (py - oy) * (py - oy);
                    // the lever is used from its own side of a door: not across a closed door square (Phase C)
                    if (DoorBetween(n, tx, ty, o.TileX, o.TileY)) continue;
                    if (d2 < nearest) { nearest = d2; lv.Nearest = r; }
                    var h = LevelGeometry.CornerHeights(m.Tiles[tx, ty]);
                    int fmin = h.Min() * 8, fmax = h.Max() * 8;
                    // the best foot height in the tile's range: the lever from 8 below to 24 above
                    int pz = Math.Max(fmin, Math.Min(fmax, oz - 8));
                    int gap = oz - pz;
                    if (d2 <= HandDist && oz <= pz + 24 && oz >= pz - 8 && !HandBlocked(n, tx, ty, o.TileX, o.TileY, pz))
                    {
                        if (!lv.Reach.Contains(r)) lv.Reach.Add(r);
                    }
                    else if (d2 <= PoleDist && oz <= pz + 48 && oz >= pz - 16)
                    {
                        if (!lv.PoleReach.Contains(r)) lv.PoleReach.Add(r);
                    }
                    if (d2 <= PoleDist && Math.Abs(gap) < Math.Abs(bestGap)) { bestGap = gap; lv.Floor = pz / 8; lv.Above = gap; }
                }
            lv.PoleReach.RemoveAll(r => lv.Reach.Contains(r));
            lv.InRock = SquareOpenedBy(n.M.Session, m, o.TileX, o.TileY);
            if (lv.Reach.Count > 0) lv.Requires = Or(lv.Reach.Select(r => "region:" + r));
            else if (lv.PoleReach.Count > 0) lv.Requires = ZoneLink.JoinAnd(new[] { Or(lv.PoleReach.Select(r => "region:" + r)), PoleOrLevitate });
            else lv.Requires = lv.Nearest >= 0 ? "region:" + lv.Nearest + " & spell:levitate" : "flag:unreachable_lever_L" + L + "_" + o.Index;
            if (lv.InRock != "free") lv.Requires = ZoneLink.JoinAnd(new[] { lv.Requires, lv.InRock });
            return lv;
        }

        static string Or(IEnumerable<string> terms)
        {
            var t = terms.Distinct().ToList();
            return t.Count == 0 ? "free" : string.Join(" | ", t);
        }

        /// <summary>A closed door square on the straight line from (px, py) to the switch's square (the switch's own square not counted).</summary>
        static bool DoorBetween(LevelNav n, int px, int py, int lx, int ly)
        {
            if (px == lx && py == ly) return false;
            int xdir = Math.Sign(lx - px), ydir = Math.Sign(ly - py);
            int x = px, y = py;
            for (int guard = 0; guard < 16; guard++)
            {
                if (x == lx && y == ly) return false;
                int k = y * 64 + x;
                if ((x != px || y != py) && n.Door[k] && n.DoorObj[k].Item < 0x148) return true;
                x += xdir; if ((xdir < 0 && x < lx) || (xdir > 0 && x > lx)) x = lx;
                y += ydir; if ((ydir < 0 && y < ly) || (ydir > 0 && y > ly)) y = ly;
            }
            return false;
        }

        /// <summary>
        /// A switch lying in a rock square (behind a wall decoration) cannot be seen or used until
        /// a change-terrain trap opens the square: the flags of the mechanisms that do (event/TRIGGER.C
        /// TRAP_CHANGE_TERRAIN), or a flag nothing grants. Free for a switch on an open square.
        /// </summary>
        static string SquareOpenedBy(GameSession s, LevelModel m, int x, int y)
        {
            if (x < 0 || y < 0 || x > 63 || y > 63 || !m.Tiles[x, y].IsSolid) return "free";
            var flags = new List<string>();
            foreach (var t in m.Objects)
            {
                if (!IsRoot(m, t)) continue;
                foreach (var st in Walk(s, m, t))
                {
                    var trap = st.Trap;
                    if (trap.Item != 0x185) continue;
                    int type = trap.Heading * 2 + (trap.Quality & 1);
                    if (type == 0 || type >= 10) continue;
                    if (x >= st.RunX && x <= st.RunX + trap.FineX && y >= st.RunY && y <= st.RunY + trap.FineY) flags.Add(string.Format("flag:terrain_L{0}_{1}", m.Level, t.Index));
                }
            }
            flags = flags.Distinct().ToList();
            return flags.Count == 0 ? string.Format("flag:never_opened_L{0}_{1}_{2}", m.Level, x, y) : string.Join(" | ", flags);
        }

        bool HandBlocked(LevelNav n, int px, int py, int lx, int ly, int footZ)
        {
            if (px == lx && py == ly) return false;
            var m = n.M;
            int pz = Math.Max(0, footZ - 8);
            int terr = n.Class[py * 64 + px];
            int xdir = Math.Sign(lx - px), ydir = Math.Sign(ly - py);
            int x = px, y = py;
            for (int guard = 0; guard < 16; guard++)
            {
                x += xdir; if ((xdir < 0 && x < lx) || (xdir > 0 && x > lx)) x = lx;
                y += ydir; if ((ydir < 0 && y < ly) || (ydir > 0 && y > ly)) y = ly;
                var t = m.Tiles[x, y];
                if ((pz >> 3) > t.Height && !HasBridge(m, x, y)) return true;
                if (x == lx && y == ly) break;
                int c = n.Class[y * 64 + x];
                if (c != 0 && c != terr && !HasBridge(m, x, y)) return true;
            }
            return false;
        }

        static bool HasBridge(LevelModel m, int x, int y)
        {
            foreach (var o in m.TileObjects(x, y)) if (o.Item == 0x164) return true;
            return false;
        }

        // ---------------------------------------------------------------- doors and tidying

        void ApplyDoorRequirements(Dictionary<int, LevelNav> lv)
        {
            var reqOf = new Dictionary<int, string>();
            foreach (var r in Regions)
            {
                if (!r.IsDoor) continue;
                string req = r.DoorRequires;
                LevelNav n;
                List<string> ops;
                if (lv.TryGetValue(r.Level, out n) && n.DoorOpeners.TryGetValue(r.Tiles[0], out ops))
                {
                    var alts = ops.Distinct().Select(x => x.Contains("|") && ops.Count > 1 ? "(" + x + ")" : x).ToList();
                    string by = string.Join(" | ", alts);
                    if (req.StartsWith("flag:door_")) req = by;
                    else if (req != "free") req = req + " | " + (alts.Count == 1 && by.Contains("&") ? "(" + by + ")" : by);
                    r.DoorNotes += (r.DoorNotes.Length > 0 ? "; " : "") + "opened by a trap chain (" + by + ")";
                }
                r.DoorRequires = req;
                reqOf[r.Id] = req;
            }
            foreach (var e in Edges)
            {
                if (!e.Requires.Contains("door:")) continue;
                var terms = ZoneLink.SplitAnd(e.Requires).Select(t => t.StartsWith("door:") ? reqOf[int.Parse(t.Substring(5))] : t);
                e.Requires = ZoneLink.JoinAnd(terms);
            }
        }

        /// <summary>
        /// Doors that a trigger the player cannot avoid (walking onto it, picking something up)
        /// closes or toggles. UW1's door trap first takes the door's lock away and gives it a copy
        /// of the trap's own lock when the trap links one, then opens, closes or toggles it
        /// (event/TRIGGER.C TRAP_DOOR). When the closed door then needs more than a hand (a locked
        /// lock), the ways through it are free only towards the trigger: an edge from the door to a
        /// side the trigger does not lie on (not reachable from that side without the door) needs
        /// the closed door's requirement (the lock, or the levers that open it). When the trigger
        /// lies on both sides, or on neither, every way out of the door needs it. A way that
        /// another route bypasses is unaffected, since the other route stays.
        /// </summary>
        void OneWayClosers(GameSession s, Dictionary<int, LevelNav> lv)
        {
            if (s.IsUw2) return;
            // neighbours: undirected ignoring requirements, and the free edges (directed)
            var nb = new Dictionary<int, HashSet<int>>();
            var freeOut = new Dictionary<int, HashSet<int>>();
            foreach (var e in Edges)
            {
                if (e.Kind == "stairs") continue;
                HashSet<int> h;
                if (!nb.TryGetValue(e.From, out h)) nb[e.From] = h = new HashSet<int>(); h.Add(e.To);
                if (!nb.TryGetValue(e.To, out h)) nb[e.To] = h = new HashSet<int>(); h.Add(e.From);
                if (e.Requires == "free") { if (!freeOut.TryGetValue(e.From, out h)) freeOut[e.From] = h = new HashSet<int>(); h.Add(e.To); }
            }
            foreach (var mech in Mechanisms)
            {
                if (mech.Kind != "door" || !mech.Unavoidable || mech.SetOff.Length == 0) continue;
                if (mech.DoorAction != 2 && mech.DoorAction != 3) continue;
                LevelNav n;
                if (!lv.TryGetValue(mech.Level, out n)) continue;
                var m = n.M;
                if (mech.X < 0 || mech.Y < 0 || mech.X > 63 || mech.Y > 63) continue;
                int k = mech.Y * 64 + mech.X;
                var c = new NavCloser { Mechanism = mech, Level = mech.Level, X = mech.X, Y = mech.Y };
                Closers.Add(c);
                if (!n.Door[k]) { c.Treatment = "no door on the square: no effect"; continue; }
                var door = n.DoorObj[k];
                c.Door = door.Index;
                int D = RegionAt[mech.Level - 1][k];
                c.DoorRegion = D;
                var trap = m.Objects[mech.Trap];
                // the door once closed: its own lock gone, a copy of the trap's lock in its place
                ObjectInfo lockObj = !trap.IsQuantity && trap.Link > 0 && trap.Link < m.Objects.Length && m.Objects[trap.Link].Item == 0x10F ? m.Objects[trap.Link] : null;
                var closed = new ObjectInfo { Index = door.Index, Item = door.Item >= 0x148 ? door.Item - 8 : door.Item, Record = door.Record, TileX = door.TileX, TileY = door.TileY, Name = door.Name };
                if (lockObj != null) closed.Children.Add(lockObj.Index);
                string notes;
                string lockReq = ZoneDoors.Requirement(s, m, closed, out notes);
                if (lockReq == "free") { c.Treatment = "closes it, but without a locked lock: it opens by hand, no effect"; continue; }
                // the levers and other triggers that open it (not the closing trigger itself)
                var openers = new List<string>();
                List<string> ops;
                if (n.DoorOpeners.TryGetValue(k, out ops)) openers.AddRange(ops.Where(o => o != ZoneLink.JoinAnd(new[] { mech.SetOff, mech.Conditions })).Distinct());
                var alts = new List<string>();
                if (!lockReq.StartsWith("flag:door_")) alts.Add(lockReq);
                // a lever must be worked again once the door has closed: now_region (now_zone in the zones)
                alts.AddRange(openers.Select(o => System.Text.RegularExpressions.Regex.Replace(o, @"(?<![a-z_])region:(\d+)", "now_region:$1")));
                c.After = alts.Count == 0 ? lockReq : string.Join(" | ", alts.Select(a => a.Contains("&") || (a.Contains("|") && alts.Count > 1) ? "(" + a + ")" : a));
                var targets = new HashSet<int>();
                foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(mech.SetOff, @"region:(\d+)")) targets.Add(int.Parse(mm.Groups[1].Value));
                HashSet<int> beside;
                if (!nb.TryGetValue(D, out beside)) { c.Treatment = "the door joins nothing: no effect"; continue; }
                // does the trigger lie on a side: reachable from it without passing the door, by free ways first, else by any way
                Func<int, Dictionary<int, HashSet<int>>, bool> reaches = (x, adj) =>
                {
                    var seen = new HashSet<int> { D, x };
                    var q = new Queue<int>();
                    q.Enqueue(x);
                    if (targets.Contains(x)) return true;
                    while (q.Count > 0)
                    {
                        int r = q.Dequeue();
                        HashSet<int> h;
                        if (!adj.TryGetValue(r, out h)) continue;
                        foreach (int y in h) { if (seen.Add(y)) { if (targets.Contains(y)) return true; q.Enqueue(y); } }
                    }
                    return false;
                };
                var sides = beside.Where(x => Regions[x].Level == mech.Level).OrderBy(v => v).ToList();
                var byFree = sides.Where(x => reaches(x, freeOut)).ToList();
                bool loose = false;
                if (byFree.Count == 0) { byFree = sides.Where(x => reaches(x, nb)).ToList(); loose = true; }
                foreach (int x in sides) c.Sides.Add(new KeyValuePair<int, bool>(x, byFree.Contains(x)));
                int withT = byFree.Count;
                bool all = withT == 0 || withT == sides.Count;
                c.Treatment = targets.Contains(D) ? "the trigger is on the door's own square: every way out of the door needs it closed" :
                    all ? (withT == 0 ? "the trigger lies on neither side: every way out of the door needs it closed" : (loose ? "the trigger lies on every side: every way out of the door needs it closed" : "another free way joins the sides: every way out of the door needs it closed, the other way stays"))
                    : "one way: free towards the trigger, back only " + c.After;
                if (all || targets.Contains(D)) Regions[D].KeepApart = true;
                foreach (var e in Edges)
                {
                    if (e.From != D) continue;
                    bool towards = c.Sides.Any(p => p.Key == e.To && p.Value);
                    if (!all && !targets.Contains(D) && towards) continue;
                    e.Requires = ZoneLink.JoinAnd(new[] { e.Requires, c.After });
                    e.Notes = (e.Notes.Length > 0 ? e.Notes + "; " : "") + string.Format("door #{0} closed behind by trigger #{1}", door.Index, mech.Trigger);
                    c.EdgesChanged++;
                }
            }
        }

        /// <summary>One edge per (from, to, kind, requirement): the passages counted, the highest fall kept.</summary>
        void Merge()
        {
            var by = new Dictionary<string, NavEdge>();
            var list = new List<NavEdge>();
            foreach (var e in Edges)
            {
                string k = e.From + ">" + e.To + "|" + e.Kind + "|" + e.Requires;
                NavEdge old;
                if (by.TryGetValue(k, out old)) { old.Count += e.Count; old.Fall = Math.Max(old.Fall, e.Fall); continue; }
                by[k] = e;
                list.Add(e);
            }
            Edges.Clear();
            Edges.AddRange(list);
        }

        /// <summary>The worst drops, in floor steps, with what they do to a fresh character (see <see cref="FallDamage"/>).</summary>
        public List<NavEdge> Drops(int minFall)
        {
            return Edges.Where(e => e.Kind == "drop" && e.Fall >= minFall).OrderByDescending(e => e.Fall).ToList();
        }

        /// <summary>
        /// The damage of landing after a fall of so many floor steps, before the Acrobat check
        /// (motion/PHYSICS.C phys_affect_player: impact &gt;&gt; 8, doubled when the landing
        /// bounces, dealt only above 3; motion/MOTION.C do_zbounce: impact = |v / 15| × (15 −
        /// bounce), bounce 5 for the player). The landing speed grows with the square root of the
        /// height, scaled so that a fall of jumpPeak steps lands at the jump's take-off speed 0x263
        /// (PIN_JUMP). Without a measured jump peak this is an estimate.
        /// </summary>
        public static int FallDamage(int steps, double jumpPeak = 2.0)
        {
            if (steps <= 0) return 0;
            double v = 0x263 * Math.Sqrt(steps / jumpPeak);
            int vz = (int)(v / 15);
            int impact = vz * (15 - 5);
            int dmg = impact >> 8;
            if (vz * 5 >= 0x8D) dmg <<= 1;      // it bounces up again: vel[2] stays non-zero, doubling the damage
            return dmg > 3 ? dmg : 0;
        }
    }
}
