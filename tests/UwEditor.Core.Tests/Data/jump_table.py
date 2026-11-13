"""Writes jump-sim.csv from jump_sim.py (the game's per-tick integer maths): for each jump
kind, Leap or not, take-off floor and tick length, the peak of the feet (levels above the
take-off floor) and the distance of the player's centre (tiles) where the feet come down
to a floor dh levels away (empty: never that high). JumpModelTests reads the file.
Run: python jump_table.py"""
import jump_sim as s

rows = ["kind,leap,floor,dt,peak," + ",".join("r%+d" % dh for dh in range(-4, 7))]
for kind, spd in (("sprint", s.SPRINT), ("standing", s.RUNJUMP)):
    for leap in (0, 1):
        for floor in (0, 10, 11, 12):
            for dt in (4, 8, 16, 32, 64):
                p = s.flight(spd, dt, bool(leap), floor)
                peak = max(q[1] for q in p)
                cells = []
                for dh in range(-4, 7):
                    x = s.crossing(p, float(dh)) if dh < peak else None
                    cells.append("" if x is None else "%.4f" % x)
                rows.append("%s,%d,%d,%d,%.4f,%s" % (kind, leap, floor, dt, peak, ",".join(cells)))
open("jump-sim.csv", "w", newline="\n").write("\n".join(rows) + "\n")
