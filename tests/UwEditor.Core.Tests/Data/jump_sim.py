"""Player jump model, transcribed from underworld-exhumed-enhanced uw1/src/motion
(PHYSICS.C do_player_input / set_player_phys_params / newFPS, MOTION.C space_to_motion /
full_move / flat_move / do_zbounce). Integer arithmetic as Turbo C (truncating division).

Units: x frac 0x2000 per cell (1/8 tile) -> 0x10000 per tile.
       z frac 0x800 per object z unit, 8 z units per floor level -> 0x4000 per level.
       time: 1/256 s; a tick is the frame's elapsed time, capped at 0x40.
"""
import sys

def tdiv(a, b):  # C truncating division
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q

RUN_FPS = 0x3AC
SPRINT = tdiv(RUN_FPS * (0x70 >> 2), 32)          # W key: ForwInpRate 0x70 -> 822
RUNJUMP = RUN_FPS // 2                            # PIN_RUN_JUMP from standing: 470
SIN1 = 32767                                      # cSinCos at heading 0, 15-bit

def max_accel(weight, max_weight):
    if max_weight and (weight * 2) & 0xFFFF > max_weight:
        return 0x60 - ((weight * 0x60) & 0xFFFF) // (max_weight * 2)
    return 0x60

def jump_v0(floor_level):
    pz = floor_level * 0x40                       # PN.z = hgt_val[level]
    v = 0x263
    if pz > 0x280:
        v = tdiv(v * 5, 6)
        if pz > 0x2C0:
            v = tdiv(v << 1, 3)
    return v

def flight(speed, dt, leap=False, floor_level=0, drop_levels=6):
    """Ticks of one jump over open ground; returns [(x_tiles, z_levels, t_s)] vertices,
    from takeoff until the feet are drop_levels below the takeoff floor."""
    acc = -2 if leap else -4
    v2 = jump_v0(floor_level)
    vmaj = SIN1 * speed >> 15
    xcell, xfrac, zfrac = 0, 0, 0                 # z relative to the takeoff floor, in frac
    pts = [(0.0, 0.0, 0.0)]
    t = 0
    while True:
        v2 += acc * dt                            # space_to_motion: vel += acc * time
        tt = vmaj * dt
        steps, rem = tt >> 13, tt & 0x1FFF
        a = v2 * 0x100
        zspeed = tdiv(a, vmaj)                    # signed here; full_move applies step[2]'s sign
        for _ in range(steps):                    # whole steps: z += zspeed << 5
            zfrac += zspeed << 5
            xcell += 1
        if rem:
            zfrac += tdiv(rem * zspeed, 0x100)
            xfrac += rem
            if xfrac & 0xE000:
                xcell += 1; xfrac &= 0x1FFF
        t += dt
        x = (xcell * 0x2000 + xfrac) / 0x10000
        z = zfrac / 0x4000
        pts.append((x, z, t / 256))
        if z < -drop_levels:
            return pts

def crossing(pts, level, descending=True):
    """x where the polyline crosses height `level` (on the way down by default)."""
    for (x0, z0, _), (x1, z1, _) in zip(pts, pts[1:]):
        if descending and z0 >= level > z1 or not descending and z0 <= level < z1:
            return x0 + (x1 - x0) * (level - z0) / (z1 - z0)
    return None

def quadfit(pts, upto=None):
    """Least squares z = b*x + c*x^2 through the origin (feet leave the floor at x=0),
    plus the free 3-term fit, over vertices with z >= upto."""
    P = [(x, z) for x, z, _ in pts if upto is None or z >= upto]

    def lstsq(cols):
        n = len(cols)
        M = [[sum(cols[i](x) * cols[j](x) for x, _ in P) for j in range(n)] for i in range(n)]
        v = [sum(cols[i](x) * z for x, z in P) for i in range(n)]
        for i in range(n):                        # Gauss-Jordan
            piv = M[i][i]
            M[i] = [m / piv for m in M[i]]; v[i] /= piv
            for k in range(n):
                if k != i:
                    f = M[k][i]
                    M[k] = [a - f * b for a, b in zip(M[k], M[i])]; v[k] -= f * v[i]
        err = max(abs(z - sum(v[i] * cols[i](x) for i in range(n))) for x, z in P)
        return v, err

    (b, c), e = lstsq([lambda x: x, lambda x: x * x])
    k3, e3 = lstsq([lambda x: 1.0, lambda x: x, lambda x: x * x])
    return b, c, e, k3, e3

def runup(accel, dt, target=SPRINT):
    """Ticks and tiles from standing to full sprint (one MaxPlayerAccel per tick)."""
    s, ticks, dist = 0, 0, 0
    while s < target:
        s = min(s + accel, target)
        ticks += 1
        dist += s * dt
    return ticks, dist / 0x10000, ticks * dt / 256

if __name__ == "__main__":
    print("sprint speed", SPRINT, "run-jump speed", RUNJUMP)
    print("\nMaxPlayerAccel by load (UW1 max_weight = STR*20):")
    for strn in (10, 20, 30):
        mw = strn * 20
        print("  STR %d: empty %d, half %d, 3/4 %d, full %d" % (strn,
              max_accel(0, mw), max_accel(mw // 2, mw), max_accel(mw * 3 // 4, mw), max_accel(mw, mw)))
    print("UW2 (max_weight = STR*13+300): STR 30 full load", max_accel(690, 690), " 600/690", max_accel(600, 690))
    print("\nrun-up to sprint (ticks, tiles, seconds):")
    for dt in (8, 16, 32, 64):
        print("  dt %2d: empty %s   full %s" % (dt,
              "%d %.2f %.2fs" % runup(0x60, dt), "%d %.2f %.2fs" % runup(0x30, dt)))
    print("\njumps (floor <= 10): apex levels @ x, range back to takeoff height, x at -1/-2/-4 levels, air time")
    for leap in (False, True):
        for spd, name in ((SPRINT, "sprint"), (RUNJUMP, "runjump")):
            for dt in (4, 8, 16, 32, 64):
                p = flight(spd, dt, leap)
                ap = max(p, key=lambda q: q[1])
                r0 = crossing(p, 0.0)
                tl = next(q[2] for q in p[1:] if q[1] < 0)
                print("  %-5s %-7s dt %2d: apex %.2f @ %.2f  range %.2f  -1:%.2f -2:%.2f -4:%.2f  air %.2fs" % (
                      "leap" if leap else "plain", name, dt, ap[1], ap[0], r0,
                      crossing(p, -1), crossing(p, -2), crossing(p, -4), tl))
    print("\nhigh floors (sprint, dt 16):")
    for lvl in (10, 11, 12, 13):
        for leap in (False, True):
            p = flight(SPRINT, 16, leap, lvl)
            print("  floor %d %-5s v0 %d apex %.2f range %.2f" % (lvl, "leap" if leap else "plain",
                  jump_v0(lvl), max(q[1] for q in p), crossing(p, 0.0)))
    print("\nquadratic fits z = b x + c x^2 (x tiles, z levels), whole flight to -6 levels:")
    for leap in (False, True):
        for spd in (SPRINT, RUNJUMP):
            for dt in (4, 16, 64):
                p = flight(spd, dt, leap)
                b, c, e, k3, e3 = quadfit(p)
                a = 2 if leap else 4
                v0e = 0x263 - a * dt / 2
                print("  %-5s spd %d dt %2d: b %.4f c %.4f maxerr %.4f lv | 3-term %s err %.4f | closed form b %.4f c %.4f" % (
                      "leap" if leap else "plain", spd, dt, b, c, e,
                      " ".join("%.4f" % k for k in k3), e3, 4 * v0e / spd, -131072 * a / spd ** 2))
