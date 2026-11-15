# UW2 randomizer: next steps and checks

Written 2026-11-14, at editor version 0.1.36 (uncommitted on top of `d48d0901`); section 1
and the starting positions brought up to date at 0.1.37, section 2 at 0.1.38, 2.1 at 0.1.39;
at 0.1.41 the Pits' gem by Blog (2.3), the brain creatures' rule (2.1) and timed gifts (3).

## Where it stands

- The automatic zones and the timeline solve of UW2 reach the end of the game (castle plot
  16, Nystul's ceremony) with no cut-offs: 379 zones, 336 reached with the default options
  (slot 73, a leftover level of 37 zones, is no longer zoned). Still 336 at 0.1.38, with
  the conversation doors and the quests of section 2 modelled; the timeline report now
  prints that number (it counted only zone ids below 1000 before, 23 for UW2).
- A pure fighter (rune magic off, of the single-use things only Iron Flesh) also reaches
  the end (257 zones at 0.1.38); it spends one Iron Flesh, for the djinn.
- The Core suite (748 tests) and the App suite (238 tests) pass on this state; see
  "Housekeeping" for what has not been rerun.

## 1. Checks in the game

Karl played all twelve checks in DOSBox or Exhumed on 2026-11-14; each holds as the solver
has it.

| # | Where | Confirmed |
|---|---|---|
| 1 | Level 1 (37, 40) | Walking into the south wall at the end of the passage sends the Avatar to Nystul's hidden room, level 2 (11, 42), and the trigger there brings him back. Free from the first minute. |
| 2 | Level 5 (12, 17) to (21, 23) | The walls around the oubliette are walked through from the squares the solver uses ((18, 22) to (16, 22) and back). |
| 3 | Level 3 (38, 20), level 17 (24, 47) | The lurker's pool and the Killorn bedroom are entered through the wall at (43, 21) and (27, 51). |
| 4 | Level 16 (31, 33) | The force field needs the fraznium gauntlets; the door at (30, 33) behind it is unlocked. The solver agrees: door #1022 has no lock, and its square lies in the zone of Bishop's room, which the `bishop_force_field` link reaches with gauntlets or circlet alone. |
| 5 | Level 17 (21, 41) | Mors Gotha's door opens to the cudgel of Entry (level 57 #484) and to bashing; her book shows only after the schedule has brought her. |
| 6 | Level 26 | Once the schedule has opened the stream ((33, 33) to (33, 36)), it is crossed without magic. Karl's route: a jump from (42, 26) to (37, 25), then a walk to (32, 35). The solver gets there as freely by another way: the current carries the Avatar from (41, 26) to the ice, and a standing jump goes from (34, 33) over the stream to (32, 33). Walking out of the stream across its current still asks for Waterwalk or Levitate in the solver; Karl's walk suggests the game is kinder, but nothing depends on it. |
| 7 | Level 25 (53, 16) | The lever at (53, 15) steps the floor; standing at the height that leads on works as the solver assumes. |
| 8 | Level 34 (30, 33) | The Historian's teleport lands on the island; the brass plate lays the bridge north, one way. |
| 9 | Level 69, key gem 8 | The dream-plant route: carry the gem to the platform's edge asleep, pick it up awake in the ring. |
| 10 | Levels 42 to 47 | No mana comes back in the Academy's middle levels; wands and scrolls still work there. |
| 11 | Level 43 | The lava room is crossed on the pillars with the level's telekinesis wand alone. |
| 12 | Level 47 (35, 38) | The arrow platforms lead to the door with no requirement. |

Editor features only exercised in headless tests so far:

- **Tools ▸ Preview changed terrain** (the amber banner, ticking traps, the first edit ending
  the preview).
- **The Go to box** (a digit, Ctrl+G, View ▸ Go to a square). Check that typing a digit while
  a list or the object tree has the focus is not a nuisance.
- **Play test in DOSBox** now always uses a saved game. "Move the new-game start" still
  uses the executable patch that DOSBox ignored on level 1, so it is suspect.

## 2. Logic not modelled yet

Ordered by how much a shuffle could be hurt by the gap.

1. **Things that close for good.** Marked at 0.1.39. A fact's `mayBeLost` (kind, why, and
   for kind b `when`, a requirement that holds once it is gone) goes onto its placements; a
   fact of the new kind `lost` marks every source of a token, or a place (a level, a rect, or
   a creature with what it carries). Three kinds:
   - **a**, lost only by a choice the player can always avoid: counted, and the walkthrough
     says "Beware: ..." at the step.
   - **b**, lost with time whatever the player does: with the new logic option
     `AbilityOptions.AvoidLost` (on) counted only while `when` does not hold yet, as the solve
     stands when the source is reached; with no `when`, never. The solve takes each plot step
     as soon as it can, so a window met in its order can be met by the player.
   - **c**, a place that closes with what lies in it: as b. The key item shuffle puts no key
     item in a place of kind b or c (`KeyShuffle.KeyPlaceAllowed`); it still refuses UW2.

   What is marked:

   | What | Lost when | Kind | Evidence | Lasting alternative |
   |---|---|---|---|---|
   | Dupre's key 28 (`dupre_sewer_key`) | plot leaves 0 | b, `plot:1` | conversation 132 | none (no other key 28; pick 14 needs 31) |
   | Charles's key 2 (`charles_key`) | plot past 11 | b, `plot:12` | conversation 131 | key 2 at level 3 (34, 16) |
   | Altara: the dagger, the rod (`altara_dagger`, `altara_rod`, `lost_altara`) | plot 14 | b, `plot:14` | SCD.ARK block 1 row 46 removes her | none |
   | Kintara and what she carries (`lost_kintara`) | plot 14 | b, `plot:14` | block 1 row 49 | (nothing needed) |
   | Mors Gotha in Killorn, what she carries (`lost_mors_gotha_killorn`) | plot 16 | b, `plot:16` | block 1 row 56 | (the book lies apart and stays) |
   | Killorn Keep, levels 17 and 18 (`lost_killorn_17`, `_18`), Merzan's Iron Flesh | both brain creatures of level 18 killed (quest 50, then 54) | a | WORLDEV.C death_check 0x3A, Killorn_just_crashed; block 15 rows 10 to 17 | (avoid it) |
   | Borne's password (`prison_password_borne`) | a wrong answer, or after the first meeting | a | conversation 3 | the armorer |
   | The armorer, the gate guards, the guard of level 14 | a wrong answer or a threat to a goblin (quest 60: the tower's goblins hostile); Garg let out (quest 0: Borne, guard 9 and every goblin of the tower killed); the armorer also after Bishop's death (quest 3) | a | conversations 1, 3, 5, 9; block 15 rows 0 to 2, 42 to 59 | key 6, which the guards carry |
   | The rune bag (`lost_rune_bag`) | offered to Mors Gotha by one who joins her and has no weapon | a | conversation 47 | none |

   The default solve and the pure fighter's are the same with the option on as off: 336 and
   257 zones, plot 16, no cut-off. Every source of kind b they use comes inside its window
   (Dupre's key at sphere 0, plot 0; Altara's dagger at 12 and rod at 14, plot 4 and 8;
   the fighter takes Charles's key at sphere 17, plot 9). Left unused: Charles's key in the
   default solve (level 3's key comes first), Borne's password (the armorer's comes first).
   `LostSourcesTests` checks this.

   Looked at and not marked:
   - The servants' strike (quest 115) blocks Nystul, but it ends when Lord British grants
     the servants' demands (conversation 142, quest 116; the Avatar can take them to him even
     after refusing Nanna, conversation 137): it does not close for good.
   - Bishop takes a pair of fraznium gauntlets to leave his cell, and asks first whether the
     Avatar has another pair (conversation 6). An avoidable choice on a link
     (`bishop_force_field`); links carry no mark yet, so the walkthrough does not warn there.
   - The Historian sends the Avatar to the island once (quest 39). The solve takes what lies
     there on arrival; a place there needing something only found later is not modelled.
   - Bishop killed after the Avatar tells Borne to have him killed (quest 2, block 15 rows
     42 to 45): `npc:bishop` is then lost, by choice (a); Mystell is the other way to
     Altara's trust. Not marked: npc tokens of creatures are not marked one by one.
   - Lady Tory (killed at plot 8), Patterson and Nelson (the plot itself), Marcus, Mystell,
     Blog, Mokpo, Elster, Praecor Loth (taken off or killed by block 15 when a quest of their
     own is set): nothing the logic needs comes from them after that point (not traced one by
     one).
   - Kind c: no place in UW2 closes whatever the player does.

   **The brain creatures must live (0.1.41).** Karl's rule: no solve may depend on the deaths
   of Killorn's two brain creatures (level 18 #241 at (29, 30), #253 at (32, 28), whoami 0x3A,
   not hostile). A new fact kind `spare` (`spare_brain_creatures`, by whoami on level 18, with
   the token `flag:killorn_fallen` that would stand for the fall) is enforced where creatures'
   deaths become sources: `AbilityZones.Spare` drops every placement of an object that is or
   lies in a spared creature, and every flag fact granted on one (it dropped the four eyeballs
   they carry, #929, #930, #981, #982, item:eyeball, which nothing asked for); `FactSet.Check`
   refuses a requirement on the token; the key shuffle's survey keeps what they carry out of the
   places. Nothing else depended on them: no fact, link or mechanism names them, their room (the
   zone of level 18 (29, 24)) is walked through, and the monster swap leaves them alone (they
   have a whoami: "has a conversation"; and the "leave special kinds" option). The walkthrough
   warning (lost_killorn_17/18, kind a) stays. `SpareCreaturesTests`.

   What remains: npc:X tokens stay held after a creature of kind b leaves (only the facts'
   own placements carry windows); `npc:mors_gotha` is granted before the schedule makes her
   (plot 14), an older gap; marks on links; a choice between a lasting and a timed source
   is not weighed (the solve simply takes what comes first in its order).
2. **Doors that only a conversation opens.** Modelled at 0.1.38 with a new fact kind,
   `door` (an "or" beside a door's lock). The Prison Tower's gate, level 10 (30, 34), opens
   for the gate guards' word (`prison_gate_guard`): the password, or the forged delivery
   voucher lying on level 9 (31, 33) (conversation 1). The door on Borne's floor, level 14
   (29, 32), opens for the password told to the guard there (conversation 9). The password
   comes from Borne at the first meeting (conversation 3) or from the goblin armorer
   (conversation 5), both inside the gate. The guards of level 15 who ask for it only stay
   friendly; their portcullis has levers on both sides. Not counted: the gate guards' bribe
   (gold, see 6). Borne gives the password only at the first meeting and attacks after a
   wrong answer: a source that may be lost, for 1. The jail stays a dead end, as in the game: the Avatar's cell is
   level 1 (42, 38) (event/WORLDEV.C put_player_in_jail), and Lord British opens its
   portcullis only in a talk he holds with one the castle guards have jailed (conversation
   142 tests quest 112, which put_player_in_jail alone sets; he is moved outside the cell at
   the same time). A start there has no quest 112 and Lord British stays elsewhere. The cell
   at (38, 38) is Fissif's, and nothing opens its portcullis.
3. **Quests reduced to "the people met".** Done at 0.1.38:
   - Talorus asks for what the Bliy Skup Chamber takes (`talorus_new_ductosnore`): Bliy
     Skup Ductosnore killed (a fight), the Delgnizator (level 34 (31, 20)), the crystal E0Y2
     (level 34 (30, 42), quality 6) and the controller crystal (level 33 (2, 5), quality 2),
     then the pull chain at (56, 6) (hack 25 sets quest 38). The Dialogicians' scroll and
     the Data Integrator only tell the player how; the game checks neither.
   - Zoranthus's bottle asks for the Sceptre of the Deadly Seeker, level 65 (60, 57) #844
     (its spell is Deadly Seeker). The Void's bottle is still the other source.
   - The Pits' gem: Dorstag (level 57 #252) carries it and the game lets him be killed
     anywhere (death_check has no arena condition), so `key_gem_7` stays free with fights taken
     as won. Blog's way, traced at 0.1.41 from conversations 99 and 101, SCD.ARK block 15 and the
     level: Blog (level 59 #255, whoami 0x65) becomes a friend (quest 22) for a grey rock or
     three lost games of White Rock Black Rock (a black rock shown alone loses; three lie in his
     room at (28, 26)); Dorstag spoken to then hands the gem to Blog (quest 23, X clock 15 + 1);
     block 15 rows 73 to 78 make his fighters flee, fire the scheduled triggers of (1, 56)
     (#788 sets off create object trap #789 at (14, 62), which makes a copy of Blog #226 holding
     gem #782) and move Blog into Dorstag's room; row 80 removes the Blog of level 59. Blog,
     spoken to there, gives the gem. Lost only by a choice (killing Blog, quest 65, or Dorstag
     first). Facts `blog_friend`, `key_gem_7_blog`; the new logic option
     `AbilityOptions.PitsGemByBlog` (window row "The Pits' key gem only by Blog's way") makes
     `key_gem_7` need `nooption:pits_gem_by_blog` (a new requirement term). The gem comes at
     sphere 14 with the option off, 16 with it on, in the default and the pure fighter's solves
     alike (336 and 257 zones, plot 16, no cut-off). Inferred, not played: that the schedule
     runs block 15 at once while the Avatar stands on level 57, and that the create trap puts
     the copy at (14, 62) before hack 7 moves it. `PitsGemTests`.
4. **The Pits' arenas.** Not a gap. The conversation teleports land at (38, 19), (18, 38),
   (21, 21) and (36, 36), squares of the big zone of level 57, which is reached on foot.
   The six unreached one-square zones of level 57 are five bridge squares ((8, 18), and
   (25, 18), (26, 19), (18, 25), (19, 26) in the corners of the Arena of Fire's lava) and
   the square (14, 62) where Blog is kept until a create object trap brings him; none holds
   anything.
5. **Fights are taken as won**, everywhere. A challenge setting for UW2 (as UW1 has) would
   need the creature tables reviewed.
6. **Gold and Charm** are not counted (Merzan's potion, the gate guard's bribe, Altara's
   trust by Charm).
7. **Level 48 west of x 25** restores no mana: done at 0.1.38 (`academy_no_casting_48`,
   a `nomana` fact with a rect).
8. **Reveal spots** (level 3 (44, 55), level 59 (40, 41), level 26 (18, 45)): still asking
   for Reveal or Search. Karl to say whether an experienced player is taken to know them.
9. **Bounce** stays set aside (niche).

## 3. Starting positions

Brought up to date at 0.1.40, when the random start was built (StartPicker, StartTeleport,
the key shuffle's `KeyShuffleOptions.Start`; MANUAL "A random starting position").

The solve was run from 23 starting squares with the default options (a new option,
`NavOptions.StartOverride`, sets the square; it has no control in the windows).
`StartOverrideTests` covers the game's own start, level 1 (40, 30), the facets of levels 9
and 57, the three failures below (with the Avatar's cell beside the first) and a UW1 start
on level 2 (9, 35).

- **Reach the end (18):** the game's own start; level 1 (40, 30); levels 2, 3 and 5; the
  eight squares the blackrock gem sends to (levels 9, 17, 25, 33, 41, 49, 57, 69); levels
  26, 48, 52, 59 and 65 at an open square. Each reaches the same 336 zones.
- **Fail, as they would in the game (3):** the jail cell at level 1 (38, 38) (Fissif's;
  the Avatar's at (42, 38) fails too, see 2.2, and is a test case since 0.1.38); the top of
  the Prison Tower at level 16 (33, 33) (down to level 14, then doors locked with keys 4 and
  6; 4 zones); the round room of level 18 behind the door at (25, 28) (key 9 or a pick of 13).
- **Not meaningful (2):** level 1 (52, 20) and (10, 55) are not open squares.
- **The cut-off artefact: fixed at 0.1.40.** After a change that takes ways away, the solver
  works out what the player can still get to from where he stood. The report used to count as
  cut off every zone reached before that he could no longer get to, also zones already out of
  his reach before the change (a start left by a one-way way, and what lies only behind it).
  It now counts only the zones he could get to from where he stood just before the change (the
  same search over the world as it was then) and cannot after it. The solve itself is
  unchanged. With the default options 21 UW2 starting zones (ledges and rooms of levels 42 to
  67) had a cut-off and none has now; UW1 had none. A change that shuts the player in is still
  found (`CutOffTests`, a dead end of UW1 level 1 sealed by a test trap). What it does not
  cover: when two changes that take ways away go off in the same step, the player is taken to
  stand at both, so a cut-off of one can be hidden by the other (as before). Level 48 (30, 30)
  itself already reported none at 0.1.39.
- **Start-zone placements.** The build puts the spells, the crafts and UW2's state tokens in
  the start zone to mean "anywhere". From a start the player leaves one way, a change could
  take the start zone out of reach and those were then never granted (a start on the level 42
  ledge at (20, 32) missed the end that way). Since 0.1.40 the solver grants them wherever the
  player is (`Timeline.Everywhere`).
- **Lord British's speech (quest 109): modelled at 0.1.40.** The castle's conversations (130
  to 143 and 168: Nystul, Charles, Dupre, Geoffrey, Iolo, Julia, Miranda, Nanna, Nell, Nelson,
  Patterson, Lady Tory, Feridwyn, Syria) only send the Avatar to hear Lord British while quest
  109 is clear. It is set at the end of Lord British's first talk with the Avatar, wherever that
  happens (conversation 142, `avatar_the_time_to`: `met_before == 0` runs the speech, which ends
  with `set_quest(109, 1)`); he waits at level 1 (31, 52) with goal 10, talk to the player (#235),
  and walks up once the Avatar comes within reach of his attention. The new game puts the Avatar
  in his bedroom at (19, 48), not in the throne room, so the speech was never automatic. Facts:
  `lord_british_speech` (a flag at his object) and a trade fact for each castle NPC's token; the
  flag facts at those NPCs (Miranda's plot steps, Dupre's and Charles's keys, Nystul's plot steps,
  Patterson's) ask for it too. A start elsewhere gets the speech when the Avatar first meets him;
  the default solve is unchanged (336 zones, plot 16). Not played.
- **Random starts on UW2 (picker only; the key shuffle still refuses UW2).** Candidate squares:
  2,563 in 25 zones in the castle (slots 1 to 5), 18,031 in 252 zones in the nine worlds. The
  stand-in filter (the unshuffled game's solve from each zone) passes 20 of the 25 castle zones
  and 159 of the 252 (the others need a key from elsewhere, which only a key shuffle could place).
  Solving every zone takes about 5 s for the castle and 2 minutes for all; the picker solves
  only the zones it draws. The four failures above are rejected (the Avatar's cell by its exit
  trigger, the round room's middle as lava, the rest by the solve); the facets' landing squares
  hold enter triggers, and their zones pass.
- **The teleport at the start (StartTeleport), for both games.** The new game places the player
  without a collision check (motion/PHYSICS.C player_setup), so a move trigger on the start
  square goes off only when he moves; UW2's enter triggers do go off on placement
  (change_GrSq, check_pplate 6) but are not used. A move trigger on the start square and on each
  open square round it (nine at UW2 (19, 48)) runs one teleport trap; the triggers lack flag 10,
  so the chain is deleted after one use, and the trap's flags count the triggers so that
  delete_trap removes them all (event/TRIGGER.C UseTrigger, delete_trap, kill_triggers).

To check in the game (both games, after a key shuffle with a random start, or the start teleport
put by hand):

1. A new game: turning on the spot does nothing; the first step forward, back or sideways
   teleports to the chosen square, on another level too.
2. Back at the new-game square later, nothing happens (the triggers are gone); a saved game
   made after the teleport keeps it gone.
3. UW1: the start kit (torch and map) lies beside the new start.
4. UW2: Lord British gives his opening speech when the Avatar first comes to him after a start
   elsewhere, and the castle's people talk normally afterwards.

- **Timed gifts and made items (0.1.41).** `TimedGifts.Missed` lists the gifts of kind b a solve
  did not get (its placement not counted and its token got nowhere else). A fact may name the
  object of its gift (`gift`: item, owner, quality, link, flags, enchanted): Dupre's key (0x10D,
  owner 28), Charles's (owner 2), Altara's dagger (0x0A, as her own #971: link 707, enchanted)
  and rod (0x112, link 576, flags 4, enchanted, from x_obj_stuff's flags 12). The key shuffle
  makes a new key item for each missed gift the logic needs (`ShuffleObject.Made`): the fill
  puts it on the floor of a reachable place (the place keeps its item), the check solve and
  Apply see it as a new object (a scratch `LevelEditor.AddObject` on the attempt's bytes), Undo
  removes it, the log says why. Tested on UW1 with a test fact (`TimedGiftTests`); UW2 still
  refused. The numbers for Dupre (sweep of every UW2 zone with candidate squares, default
  options): of 159 viable start zones (20 of 25 in the castle), 153 do not take his key (15 in
  the castle); all of them still end the game. The castle plot goes to 1 at level 3's sewer
  entry: move triggers #571, #573 at (16, 53), (15, 53) set off the set variable trap #572 (the
  teleport at level 1 (14, 53) lands there), later steps at level 9 and 14. The solve takes
  that step as soon as the sewers are reached, and Dupre speaks only after Lord British's
  speech, so from a start away from the game's own one the solve passes the sewers first; a
  player could see Dupre first unless his way to the castle runs through those squares. No way
  of the logic needs key 28: the door #947 at level 1 (15, 53) has both sides in one zone
  (the far side is the teleport to level 3, reached from level 3), so nothing is made for it.
  None of the 93 failing zones becomes viable with AvoidLost off. Charles's key and Altara's
  gifts are missed only from starts that fail anyway.

- **Places in reach, the return check from another start, the start setting (0.1.42).**
  - The fill no longer fails when the start's reach holds too few places: an empty place moves
    to a square within reach (Place anywhere's square rules, the start's own sphere first) and
    takes the key item (`KeyShuffleOptions.PlacesInReach`, on by default for a random or given
    start, off for the game's own so its seeds give what they gave). A place of the same level
    keeps its slot; with none left on that level (UW1 level 7 has 8 places) one of another level
    moves over (deleted there, made here; a full object list fails the attempt). Invariant,
    tested in `PlacesInReachTests`: every moving item in one place, every place one item, the
    same objects over all levels.
  - The return check from a start other than the game's own counts the zones the start leads
    out into as "back" when nothing in the start zone is needed after sphere 0 and those zones
    reach one another (`Engine.ReRoot`; the sealed room of UW1 level 1 at (30, 30), left only by
    Gate Travel, made every zone a trap). For every start, a stalled walk back now takes in the
    later spheres' changes that only open ways and that the walk can set off (the moonstone
    room's floor opening after Gate Travel).
  - `TimelineSolve.CutOffRegained`: a zone cut off that the solve gets back to later; the start
    filter ignores it (the three level 7 ledges it rejected now pass: 163 of 163 UW1 zones).
  - The Apply window has a Start row (the game's own square, a random one with the scope in the
    game's words, this square or the selected one); the summary says where the game starts.
  - UW1 sweep (163 zones, 3 seeds, no retries): 473 of 489 runs fill (0.1.41: 211 of 480);
    147 zones with every seed, none with no seed; levels 1-6 all, level 7 119/132, level 8
    72/75. Left: 15 runs of seed 3 that put the broken blade in the one-way area of level 8 at
    (2, 2) (a real strand: a free jump in, no way out without later abilities; the fill does not
    avoid one-way pits, the retry does), and level 7 (55, 51) seed 3, where the fill moved place
    #474 into the start zone for the ring at L4 (14, 14) but the fresh check reaches only the
    start zone (not explained yet). Sweep 2 ran before the pocket change of ReRoot; its 20
    failures were rerun one by one after it (the 4 on level 6 now pass; the change only makes
    the return check more lenient, so no success could turn into a failure).
  - Not yet: the UW2 shuffle is still refused; nothing of this is played in the game.

- **A new character's rune casting (0.1.43).** Casting from runes asks for level 2 x circle - 1,
  mana 3 x circle and a Casting roll (combat/RUNES.C player_cast, both games), and a new
  character is level 1 (game/CHARGEN.C). The logic now counts no spell above
  `AbilityOptions.NewCharacterCircle` (1; control "New character casts up to circle") from runes
  until the solve has reached a zone outside the start's free area (the zones reached from the
  start by ways needing nothing; `TimelineSolve.FreeArea`, `FreeAreaLeft`; `Placement.Circle` on
  the cast placements). The game's own start is unchanged in result: UW1 22 spheres (free area
  75 zones, left in sphere 5; Water Walk, Telekinesis and Levitate move from spheres 3-4 to 5),
  UW2 41 spheres (free area 30 zones, left in sphere 5; Leaping, Water Walk and Telekinesis from
  sphere 1 to 5, Open from 3 to 5). Before, both solves took a new character to cast circles
  2 to 5 at once, but no step needed them before the free area was left. A start that cannot
  leave its free area otherwise gets an object made by the key shuffle (`StartAbilities`, a
  made item as for the timed gifts): a wand enchanted with the spell (lasting; UW2's own wand
  of Light, level 67 #899, is the pattern) or, when single-use things count, a scroll. The
  start picker counts it (`StartCheck.Made`). UW1 sample (18 starts: the sealed room, level 6
  (16, 10), three of level 7, three of level 8, ten random zones; 3 seeds, no retries): 51 of 54
  fill, the same three failures as at 0.1.42 (the broken blade in level 8's one-way area at
  (2, 2), seed 3, twice; level 7 (55, 51) seed 3). Made: a wand of Gate Travel for the sealed
  room and level 5 (57, 61); a wand of Flameproof for level 6 (16, 10), level 7 (47, 43) and
  (30, 46), level 8 (23, 45) and (48, 49).
  - For UW2 when its shuffle comes: in Britannia (levels 1 to 8) no spell above circle 3
    works from runes at all (player_cast: `(PlayerLevel - 1) / 8 == 0 && circle > 3`), which the
    logic does not model yet: Levitate, Flameproof, Open, Telekinesis, Reveal, Gate Travel,
    Portal and Iron Flesh cast from runes there are counted today. UW2 has no Gate Travel
    object of its own.
  - Not modelled: Gate Travel's warp (`gate_travel` fact) is not narrowed by `nomana` or no-magic
    squares as the ways are, so casting it from runes on UW1 level 7 before the orb is broken is
    counted (from level 7 (47, 43) the 0.1.42 fill relied on it).

## 4. Randomizer features to try on UW2

The UW2 work so far is the logic. The shuffles themselves were built on UW1 and have not
been run on UW2 in this stretch; each needs a first run and a look at its log.

1. **Key shuffle** on UW2: run it, read the spoiler log and the walkthrough, and solve
   the shuffled game again. Watch the keys the facts now add (key 28 from Dupre, key 2 from
   Charles) and the barrier squares the shuffle must keep clear of.
2. **Item shuffle / item cleaner** on UW2: the plot items the facts name (key gems,
   dagger, rod parts, horn, bottle, basilisk oil, fraznium gauntlets, dream plant) must be
   held fixed or tracked.
3. **Monster swap** on UW2: creatures that carry plot things or stand for a quest (the
   listener, Borne's goblin, Dorstag, the liches' key) must be exempt.
4. **A control for "only some single-use things"** (`SingleUseOnly`) and for Portal, in the
   ability zones window; today they are options in code only.
5. **A walkthrough read-through** for UW2: the spoiler log's steps for the facts added
   lately (wall teleports, the force field, the oscillator floor) have not been read for
   wording.

## 5. Housekeeping

- Commit the batch (version 0.1.36) when ready; the new files are LF.
- The Mods, Robustness and ModRoundTrip suites were last run before the UW2 logic changes
  of this batch; run each once before a release.
- Two helper worktrees under `.claude/worktrees/` can be removed.
- Slot 73 of UW2's level file (a leftover level that repeats level 51's triggers) is now left out of the
  automatic zones, as the item walk leaves it (`NavGraph.Build`, version 0.1.37).
- Standing reminders: fallen-adventurer item caches (on hold), the Exhumed font range for
  accents, the UW1 level 9 force-field barriers to play-check.
