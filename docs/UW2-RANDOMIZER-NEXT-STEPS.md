# UW2 randomizer: next steps and checks

Written 2026-11-14, at editor version 0.1.36 (uncommitted on top of `d48d0901`); section 1
and the starting positions brought up to date at 0.1.37, section 2 at 0.1.38, 2.1 at 0.1.39;
at 0.1.41 the Pits' gem by Blog (2.3), the brain creatures' rule (2.1) and timed gifts (3);
at 0.1.44 where magic works: Britannia's circle rule and the no-magic squares (end of 3);
at 0.1.47 the key item scatter on UW2 from the game's own start (4.1); at 0.1.49 random and
given starts on UW2 (end of 3, 4.1); at 0.1.50 a start in a shut world, links taken once or
marked, what the Prison Tower's goblins carry, Merzan's gold (2.1, 2.6, end of 3).

## Where it stands

- The automatic zones and the timeline solve of UW2 reach the end of the game (castle plot
  16, Nystul's ceremony) with no cut-offs: 379 zones, 336 reached with the default options
  (slot 73, a leftover level of 37 zones, is no longer zoned). Still 336 at 0.1.38, with
  the conversation doors and the quests of section 2 modelled; the timeline report now
  prints that number (it counted only zone ids below 1000 before, 23 for UW2). 334 at 0.1.44:
  Telekinesis from runes no longer reaches two barrier squares of Britannia (end of 3).
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
     item in a place of kind b or c (`KeyShuffle.KeyPlaceAllowed`); it runs on UW2 since 0.1.47 (4.1).

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
     Avatar has another pair (conversation 6: find_barter(51), item 0x33, give_ptr_npc). Marked at
     0.1.50: `bishop_force_field` carries `mayBeLost` of kind a, and the walkthrough warns at the
     step that crosses it. A link may carry only kind a (`FactSet.Check`).
   - The Historian sends the Avatar to the island once (conversation 156: `set_quest(39, 1)`
     then `teleport_player(31, 35, 34)`, the whole talk skipped once quest 39 is set). Modelled at
     0.1.50: a link fact may be `"once": true` (`ZoneLink.Once`). The solve crosses it once; the
     zone it leads to gives what it gives on arrival, is walked out of in the next round, and then
     leaves the reach unless another way into it holds (the island: lava protection from the
     shore, a repeatable link); the return check and the walkthrough's routes never use it. The
     default solve is unchanged token for token (the island holds only the key gem, needing
     nothing). Left: what the arrival round grants comes from the whole sphere, so in principle a
     token gained elsewhere in the same sphere could count on the island (nothing there needs one).
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
   own placements carry windows); a choice between a lasting and a timed source is not weighed
   (the solve simply takes what comes first in its order). `npc:mors_gotha` is granted before the
   schedule makes her (plot 14), but nothing asks for it (checked at 0.1.50, below).

   **Mors Gotha before plot 14 (checked at 0.1.50).** She cannot be met before it, from any
   start or route: the Mors Gotha of level 17 (#240) is the template of create object trap #534,
   held off the map, and only scheduled trigger #861 at (16, 43) sets the trap off (schedule block
   1 row 50, at plot 14); her book is shown by #819 on the same square at the same time
   (`mors_gotha_book` needs both flags). The other Mors Gotha (level 1 #243) is the template of create
   object trap #851, on the walled-in square (1, 62), for her attack on the castle. So no early meeting or kill is possible; the token
   `npc:mors_gotha` (given at the template's zone) is asked for by no fact or way. Nothing changed.

   **What creatures carry on UW2 (0.1.50).** UW1's rule that what a named, friendly creature
   carries needs `option:kill_npcs` (`AbilityZones.ChainPlacements`, with the create trap
   templates and the locked containers) does not run on UW2: there, 36 placements of things NPCs
   carry are free (keys 4, 5, 6 and 27 on Borne, key 6 on three goblin guards, keys 4 and 6 in
   Janar's key ring, Altara's and Zaria's runes, the horn on Praecor Loth, Fissif's lock picks …).
   A new fact kind `carried` adds a requirement to what one creature carries. Janar (level 12
   #251) gives his key ring (#978, keys 4 and 6) for the codeword "beacon", which Borne tells
   when asked at a later meeting whether he has keys to the other cells (conversation 3 func_0EC7
   sets quest 45; conversation 17 asks for it and the word, `take_from_npc(140)`):
   `carried_janar` asks `npc:borne | option:kill_npcs`. Borne and the goblin guards of levels 10
   (#251, #255) and 14 (#254) never hand over their keys: `option:kill_npcs` (Garg let out kills
   them, not modelled). The default solve changes: keys 4 and 6 come at sphere 11 (Janar, after
   Borne) instead of 9 and 6, key 27 is no longer reached (nothing needs it), the solve still
   ends at plot 16 with 334 zones; the pure fighter too (257 zones). Left for later: the other
   NPC-carried things (the runes of Altara, Zaria and the liches, the horn, which comes by a
   fight, Fissif's lock picks, the create trap templates and locked containers) are still free on
   UW2; a review creature by creature, or UW1's rule with trade facts for what is given.
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
6. **Gold and Charm.** At 0.1.50 Merzan's potion of Iron Flesh asks for a hundred gold coins
   (conversation 105: the price 2 x (50 - a Charm discount), paid in coins, item 160, or in
   treasures at a third of their value): a count fact `gold_100` over the coins in the zones
   reached. The gate guards' bribe and Altara's trust by Charm are other ways beside the ones
   the logic takes (the password or voucher, Bishop or Mystell), so leaving them out is safe.
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
- **Random starts on UW2 (0.1.40 numbers, the stand-in; superseded at 0.1.49, end of this section).** Candidate squares:
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
  removes it, the log says why. Tested on UW1 with a test fact (`TimedGiftTests`); on UW2 (0.1.47, the game's own start
  only) the solve gets every gift in time, so nothing is made (4.1). The numbers for Dupre (sweep of every UW2 zone with candidate squares, default
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
  - Since 0.1.49 the UW2 shuffle takes a random or given start too (end of this section);
    nothing of this is played in the game.

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
  - Britannia's circle rule and Gate Travel's warp: modelled at 0.1.44 (next bullet).

- **Where magic works (0.1.44).** Read in the decompiled sources (byte-matching, so the C is
  what the game runs) and checked against the shipped levels by running the Core.
  - *Britannia.* uw2 combat/RUNES.C player_cast:232, `if ((PlayerLevel - 1) / 8 == 0 && level > 3)
    return fail_spell(3);` ("Casting was not successful", string 0xE4; tested before the level,
    mana and skill checks, so no mana is spent). PlayerLevel is the editor's level number (1 at a
    new game, game/CHARGEN.C:128; 0x44 is level 68 in 3d/GRIDDB.C), so levels 1 to 8; nothing
    else in the test, so no plot state, quest or item lifts it. Objects do not go through
    player_cast: obj/OBJUSE.C checkSpell:353 calls event/WORLDEV.C inanimate_spell:505, which
    calls do_spell directly, so scrolls, wands and enchanted things work in Britannia. UW1's
    player_cast has no such test.
  - *The no-magic bit.* Bit 0 of the tile's `door` field, bit 14 of the tile word (include/map.h
    struct Tile; the editor's `TileInfo.NoMagic`, Uw1Schemas "No magic"). Its only reader is
    combat/SPELLS.C anti_magic_p (uw1:76, uw2:54). Callers: do_spell (uw1:98-102, uw2:86-90): an
    object caster (who at or above objdata: a trap) is tested at the trap's square, every other
    caster at its own square, `anti_magic_p(OBJ_HOMEX(who), OBJ_HOMEY(who))`, and in UW1 also
    `|| PlayerLevel == 9`. The player is critdata + 1 (game/PLAYER.C), below objdata, and his home
    square is kept at the square he stands on (motion/PHYSICS.C uw1:247, 355, 396); a scroll or
    wand he uses is cast with who = the player (checkSpell: `src = who` when used), so runes and
    things used both fail there, in both games. Creatures: critter/AI.C maybe_cast_defensive_spell
    and crit_magik_attack (uw1:509, 530-533; uw2:484, 503) do not cast while standing on one, and
    a creature's own do_spell is tested at its square. Nothing else reads it: no test when a spell
    is already running (set_curmagic, the active spells), none on missiles in flight, none on
    mana. Worn enchantments never go through do_spell.
  - *Where the bit is set.* UW1: sixteen squares, all on level 6: (6, 17), (7, 17), (5, 18) to
    (8, 18), and (3, 27) to (6, 27), (4, 28), (5, 28), (4, 29), (5, 29), (4, 30), (5, 30), all in
    the big zone of level 6 at (24, 3). UW2: none on any level. So Karl's memory fits the data: in
    UW1 the bit works but sits on two small patches nobody needs to cast in; in UW2 it would work
    the same, but no square has it.
  - *UW1 level 7 before the orb.* game/GAMEWRAP.C do_level_hacks:459 sets mana and maximum mana
    to 0 on arrival while `player->orb` is 0; combat/SPELLS.C restore_mana caps at the maximum, so
    potions give nothing; player_cast's mana test fails every rune spell. Scrolls and wands need
    no mana and work. Tybal's guards (race 0x13) do not cast there either (AI.C:353, 510, 531).
    Breaking the orb (obj/USEITEMS.C:524) gives the mana back. The logic had this right.
  - *UW1 level 9.* do_spell refuses every spell of the player and of creatures there (SPELLS.C:101);
    traps are tested only at their square. Left as it was: reaching level 9 ends the game.
  - *What the logic assumed.* The ways (region edges) were narrowed for no-magic squares in UW1
    only, and for the nomana levels; the facts' links, the placements, the look triggers but for
    Reveal, and the warps were not narrowed at all. The 0.1.43 note that a made scroll or wand
    "still fails on a no-magic square and on level 9" was right; the one that Gate Travel's warp
    ignored where magic fails was right too.
  - *What changed.* One rule for all of them, `AbilityZones.CastAt` (where the spell is cast):
    worn sources only on a no-magic square (both games now) or UW1's level 9; cast with the fact's
    token where a nomana fact holds; in Britannia `use:X` for an ability whose spells are all above
    circle 3, `(cast:X & the runes of a low spell) | use:X` for Light (Light or Night Vision, not
    Daylight), unchanged for Leaping and Water Walk. The ways get it when the levels are built (36
    UW2 edges in Britannia); in the timeline the facts' links (at their start, a two-way link with
    ends that differ split in two), the placements (at their square), the mechanisms (at the
    trigger's square) and the warps (from any zone reached, one square of each kind a zone holds).
    A lasting spell cast elsewhere and carried in does not count, as for the nomana levels. A made
    wand (perm:X) counts only where it works, so StartAbilities does not rely on one on a no-magic
    square: its trial solves go through the same rule.
  - *Numbers.* UW2 default: 334 of 379 zones (was 336), 41 spheres, plot 16, no cut-off; every
    token in the same sphere as before (gems 1 to 8 at 11, 13, 13, 18, 30, 31, 14, 29). The two
    zones lost are reached only by Telekinesis across a barrier: level 1 (31, 53) and level 5
    (28, 40), the large blackrock gem's square, which is used from the square before it; nothing
    lies there that the logic needs (the game's one scroll of Telekinesis reaches one of them when
    single-use things count). The walkthrough now takes level 43's telekinesis wand before the
    pillar room (the link starts on a level without mana). Pure fighter: 257 zones, 42 spheres,
    plot 16, unchanged. StartPicker on UW2: 20 of 25 castle zones and 159 of 252 zones viable,
    unchanged. UW1 default: unchanged (22 spheres, 189 zones); the Ring of Levitation's link now
    reads `(cast:lava & flag:orb_destroyed) | use:lava`. UW1 starts on level 7 with every circle
    counted no longer leave by Gate Travel cast from runes before the orb: from (47, 43) the start
    is then not viable (nothing makes the wand when no circle is withheld), as in the game. With
    the default circle it still gets its wand of Flameproof.
  - To check in the game (DOSBox or Exhumed): (1) UW2, level 1, with a character of level 7 or
    more and the runes: Levitate (Uus Hur Por) gives "Casting was not successful" and keeps the
    mana; Leaping works; a scroll or wand of Levitate works. (2) The same on level 9 or another
    world: Levitate works. (3) UW1 level 6, standing on (6, 18): Light from runes fails and keeps
    the mana; a lit torch and a worn ring go on; a Light cast beside the patch and carried in
    stays lit. (4) UW1 level 7 before the orb: a scroll of Gate Travel works, runes do not.
  - Not done: StartAbilities still makes objects only for the circles a new character lacks, not
    for a start whose runes fail where it lies (UW1 level 7 before the orb with every circle
    counted).

- **Random and given starts on UW2 (0.1.49).** `KeyShuffle.RunOn` no longer refuses them;
  `RunWithStart` works as on UW1 and Apply writes the teleport at level 1 (19, 48).
  `Uw2KeyShuffleStartTests`.
  - *The filter's real solve.* `StartPicker.For` holds the moving items' tokens on UW2 too
    (`KeyShuffle.MovingTokens`, the UW2 survey: 61 tokens); `StandIn` is false. Castle: 22 of 25
    zones viable (was 20 with the stand-in), 2,516 of 2,563 squares; the nine worlds: 195 of 252
    (was 159), 16,810 of 18,031 squares; 207 before the plot-order rule below. Rejected: 45 small
    pockets (1 to 8 zones reached even with every item held: Fissif's cell L1 (38, 38), the
    listener's area L4 (48, 3) and room (17, 19), rooms and ledges of levels 16, 17, 18, 25, 26, 33, 34, 46,
    49, 50, 51, 52, 57, 65, 67, 69, 72), 12 by the plot's order (Prison Tower levels 10 to 16). About
    2 minutes for every zone (112 s with the stand-in), 5 s for the castle; lazy as before.
  - *The plot from a start elsewhere.* Read in uw2 event/TRIGGER.C set_numbered_variable: a set
    variable trap on X clock 1 writes its value (op 2) whatever the clock holds; the triggers of
    level 3 #571/#573, level 9 #965/#966 and level 14 #945 have flags 4 only (no flag 10), so each
    goes off once. Conversations 136 (Miranda) test the clock for exactly 3, 7 and 11; the + steps
    (listener, gem 3 #797, gem 5 #901, Zoranthus's first talk, gem 4 fused) add one. The count
    model took sets as a maximum and steps in any order, which only the castle gem's facets made
    true. Now: `plotset:3` needs `plotset:1 & plotset:2` (`StateRules`), the early steps
    (listener, gem 3 and its trigger, a `needs` fact) need `plotstep:miranda_tower`, the late ones
    (Zoranthus, his bottle, gem 4 fused, gem 5 and its trigger) need `plotstep:miranda_7`;
    `Timeline.PlotOrderProblem` rejects a start or a check whose solve reaches level 14's trigger
    before the other sets are held. The game's own start is unchanged. The walkthrough of a start
    elsewhere names the steps that must wait. Lord British's speech was already modelled (0.1.40).
    Dupre's window (plot 0) and Charles's (9 to 11) are judged as the solve reaches them (AvoidLost).
  - *Britannia.* `StartAbilities.Withheld(s, logic, startLevel)` adds, for a start on levels 1 to 8,
    every ability whose spells are all above circle 3 whatever the circle option; `Needed` also
    accepts a solve that never leaves Britannia. No shipped castle zone needs it.
  - *Made objects.* The records match the game's wand of Light (L67 #899: item 0x9B, quality 40,
    link 517 = 0x200 + 5, flags 4, enchanted, a quantity) and scroll of Portal (L57 #477: 0x137,
    link 566 = 0x200 + 54); `Abilities.Find` reads the made wand as `perm:`. Spell numbers checked:
    Levitate 32, Gate Travel 43, Portal 54, Light 5. Eight zones get a wand of Levitate.
  - *Gifts.* From a start elsewhere the missed gifts come from the solve with every moving item
    held, less those whose token a moving item gives (Charles's key 2 was made needlessly from the
    castle before that rule).
  - *The kit.* UW2's new game has a sack at level 1 (20, 49) with torch #1012 and map #834 (item
    0x13A); `StartAids.PutKit` now does the same beside a UW2 start (torch filled to 63, map).
  - *The map notes.* Left UW1 only (UW2 has 80 levels and no pages past them); the window greys the
    box out on UW2.
  - *A start in a world the gem keeps shut, in the logic (0.1.50).* `Timeline.Engine.Run`: from a
    start on levels 17 to 72 (a world whose facet the gem does not show at plot 0;
    `SealedWorld`), the solve first takes no way off the world's levels and runs to its end; then
    it leaves by a way out that holds (to the castle when one does, else to the lowest zone of
    another world a way leads to, which is taken the same way when it is shut too), stands there
    alone (the reach is that zone), and goes on. The world's zones come back only by a way that
    holds: the gem's facets (plot) or a copy of them with the world's key gem treated and fused
    (`item:key_gem_N & npc:nystul`; gem n lights facet n - 1 with 6 and 7 swapped, obj/USEITEMS.C
    UseKeyGem; facet 5 is the Pits, gem 7). `TimelineSolve.Leaves` records each leaving (sphere,
    event, the zone come out in, what opens the world). The return check walks the zones reached
    after a leaving back to the zone come out in, those before it to the start (ReRoot only over
    those). The fill, the check, the start filter and the walkthrough all use this solve; the
    walkthrough marks the leaving step (">>> Leave the Pits of Carnage now: …") and its routes
    then need the gem. The Pits seed 8 start: no NO WAY FOUND (10 before), reverse check passes,
    Zoranthus's plot step after the Pits reopen (key gem 7 fused). Start filter, before and after:
    castle 22 of 25 zones (2,516 squares), the nine worlds 195 of 252 (16,810 squares), the same
    zones (one rejected zone, level 52 (29, 2), now reaches 2 zones instead of 8). A first version
    that left only for the castle lost 17 zones (level 67's, level 72's, the Pits room at level 59
    (53, 41)), whose ways out lead through another world; they pass again. Not modelled: an
    excursion into another world that comes back (Killorn to level 72 and back) counts as leaving.
    `Uw2LogicGapTests`.
  - *The note* (Karl's request): `KeyShuffle.LockedWorldNote` (levels
    17 to 72; facets from black_gem_rotate: plot 4 Killorn and the Ice Caverns, 8 Talorus, the
    Academy and the Pits, 13 the Tomb and the Void; also open once the world's gem is fused) goes
    into the log's start lines, the walkthrough's head and the window's summary. The Prison Tower
    is not marked: below plot 4 the gem shows only its facet (every other turn: range 1 gives facet
    0, then 8 when 0 was the last). Since 0.1.50 the logic orders the steps instead (above).
  - *Things of Opening* (found by the sweep): `KeyShuffle.Needed` did not count the Open spell's
    sources for a lock (`pick:Z`), though the solver does, so the cudgel of Entry (L57 #484) and
    the scroll of Open (L50 #855) were filler: assumed held through the whole fill, then put
    anywhere. Starts whose way out is a locked door (L3 (33, 16), L2 (37, 34) and (46, 44), L51
    (11, 14) and (14, 21)) failed every check. Fixed for UW2 only (`Needed(t, so, uw2)`); UW1 has the
    same gap (its Open scrolls) but keeps its seeds. Measured at 0.1.50 (a temporary switch, not
    kept): with the Open sources counted and the UW2 place rules (containers a trap names, squares a
    trap turns to rock: UW1 has 6 such squares, no place on them) on UW1 too, the game's own start
    gives the same placements for seeds 1 to 6, all on the first attempt; random UW1 starts were not
    measured.
  - *Sample sweep* (every castle zone and 30 random zones of the other worlds, 2 seeds, no retries):
    55 zones looked at, 42 of them viable
    (the castle's 22, and 20 of the 30 drawn: the other 10 are pockets the filter rejects). 84 of
    84 runs fill on the first attempt (Britannia 44, the Tomb's levels 16, the Void 10, the Academy
    6, Killorn, the Ice Caverns, Talorus and the Pits 2 each); 49 minutes, about 33 s a run, 2
    threads at low priority beside other work. Nothing made in the sample (no sealed zone was
    drawn); a place moved into reach in 12 runs (L2 (46, 44), L2 (37, 34), L59 (53, 42), L51
    (11, 14), (16, 22), (9, 19)). Before the fix for things of Opening the same sample filled 72 of 84,
    the 12 failures all "the shuffled levels do not reach the goal" from the six zones behind a
    locked door. A random castle start (seed 1, L1 (37, 47)) and one in the Pits (seed 8, L58
    (52, 5), beside Zoranthus) were read through: plot 16, no cut-off, the reverse check passes;
    in the Pits log the walkthrough left the Pits, went to the Prison Tower and came back to
    level 59 before plot 8 ("NO WAY FOUND", 10 lines): fixed at 0.1.50 (above).
  - To check in the game: a random UW2 start's teleport fires on the first step from the bedroom;
    Lord British's speech when first met; from a start in another world, the walkthrough's order of
    the plot steps holds (Miranda at 3, 7 and 11 moves the plot each time); a made wand of
    Levitate works.

## 4. Randomizer features to try on UW2

The UW2 work so far is the logic. The shuffles themselves were built on UW1 and have not
been run on UW2 in this stretch; each needs a first run and a look at its log.

1. **Key shuffle** on UW2: done at 0.1.47, from the game's own start; random and given starts
   at 0.1.49 (end of 3). `Uw2KeyShuffleTests`, `Uw2KeyShuffleStartTests`.
   - *Categories.* UW2 has its own list (`KeyShuffle.Uw2Categories`): keys, runes, ability
     items, plot items, with the same names as UW1's so the options and settings files serve
     both. Items 0xE1-0xE7 are potions in UW2, not the Key of Infinity; UW1's orb rule (item
     0x117, UW2's amethyst rod) and `NamedItems` (UW1's 0x118 is Caliburn's blade, UW2's a key
     gem) are UW1's only. The artifacts hinted on UW2 are the key gems.
   - *Facts that follow their object.* A flag fact may carry `match` (item, quality, link):
     the game checks the thing by its kind (key gems: obj/USEITEMS.C UseKeyGem, item - 0x117,
     with the owner Nystul sets; basilisk oil: motion/OBJPHYS.C do_filanium, the spell in the
     link; the crystals: event/WORLDEV.C skup_ductosnore, quality 2 or 6; the voucher:
     conversation 1, find_barter_total(310) with link 545). `AbilityZones.FactObjects` finds the
     object wherever it lies (the named one if still of its kind, else an untaken one of its
     kind, never in a trap or carried by a creature that talks), so the scatter moves it with its
     token and the check, and any later solve of the shuffled levels, finds it. On: key gems 2,
     4, 6, basilisk oil 1-4, the crystals E0Y2 and controller, the voucher.
   - *Held fixed, and why.* Key gem 1 (Borne), 3 and 5 (pick-up triggers), 7 (Dorstag; Blog's
     copy is a create trap's template), 8 (the facts give it on the Void's platform; a second
     gem 8 lies on level 65); the Sceptre of the Deadly Seeker (its spell is an object in it) and
     the Delgnizator (a pick-up trigger in it); the horn (Praecor Loth); the Void's bottle (a
     mobile record); the Academy's telekinesis wand (game/GAMEWRAP.C do_level_hacks and
     event/WORLDEV.C remove_TK_wand put it back on level 43 whenever the player leaves, and
     `AbilitySources` makes it level-bound only where it lies: moved, the check read it as a
     telekinesis wand for everywhere); every plot item the logic asks nothing of (strange
     artifacts, resilient spheres, eyeballs …: a puzzle not modelled may look for them).
     Moving: keys, runes and the bag, lock picks, the ability items the logic counts, amethyst
     rods, black eggshells and dream plants (taken by kind).
   - *Places.* As before (lost marks b and c, spared creatures, traps and their templates,
     barrier squares), and on UW2 also: inside a container a trap names (a delete trap takes it
     along), and squares a trap turns to rock (the terrain mechanisms' descriptions; change_terrain
     removes static objects on a tile made solid), for places and for Place anywhere. The
     schedule's rows (SCD.ARK) were read: its creature removals touch creatures with
     conversations or lost marks only, its freeze floors (hack 3) and the standing wave lift
     objects with the floor (raise_up), its scheduled triggers' terrain changes are among the
     mechanisms.
   - *Gifts.* From the game's own start every gift is taken in its window (Dupre's key at sphere
     0, Altara's dagger at 12, rod at 13; Charles's key when key 2 has moved): nothing is made.
   - *A game the options cannot finish.* Magic off with single-use things not counted cannot
     glaze the djinn (Iron Flesh), so the scatter now refuses at once with the reason (UW2 only)
     instead of trying 21 seeds. The pure fighter (magic off, single-use Iron Flesh only) fills.
   - *Numbers* (seeds 1-20, default options, retries 20, 2 threads at low priority): 19 fill on
     the first attempt, 20 within the retries; the one failure (seed 9) was a fill whose fresh
     check missed the horn (the time-aware solver is not monotone in what is held, so an assumed
     fill can rarely place an item the unassumed solve then does not reach in time; the check
     catches it and the next seed passes). 17 to 35 s a run on a quiet machine (most of it the
     timeline builds: the first, the Place anywhere solve, one fill solve a key item, about 130,
     and the check), up to 95 s with other work running. Every check: plot 16, no cut-off, no
     strand, 332 zones (two empty ledges of level 34 fold into their neighbour once Place
     anywhere has moved the lock pick off them; 334 when nothing moves off). Single-use on,
     lock picking on, the Pits' gem by Blog, avoid lost off: seeds 1 and 2 fill on the first
     attempt each.
   - *To check in the game:* a scattered key gem fuses (treated by Nystul) wherever it was
     found; basilisk oil found elsewhere permeates the mud; crystals found elsewhere work in the
     Skup Chamber; the voucher found elsewhere opens the gate; the walkthrough's route of one
     seed played through.
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
