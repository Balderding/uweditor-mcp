using System;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The two jumps the player has (motion/PHYSICS.C do_player_input).</summary>
    public enum JumpKind
    {
        /// <summary>Jump while running: the W key's speed, 940 * 28 / 32 = 822, kept through the flight. Needs room to get up to speed.</summary>
        Sprint,
        /// <summary>The standing long jump (PIN_RUN_JUMP): from standing still, at 940 / 2 = 470.</summary>
        Standing,
    }

    /// <summary>
    /// The player's jump as the game computes it (underworld-exhumed-enhanced uw1/src/motion:
    /// PHYSICS.C do_player_input, set_player_phys_params, newFPS; MOTION.C space_to_motion,
    /// full_move, set_targz, get_pcoll; UW2 the same). Each tick the game does vel += acc * dt,
    /// then z += vel * dt, so at the tick boundaries the feet lie on a parabola; with x in tiles
    /// from the take-off and z in floor levels above it:
    ///   z(x) = B x - C x^2,  B = 4 v0' / u,  C = 131072 a / u^2,  v0' = v0 - a dt / 2
    /// v0 is 0x263 = 611, cut to 5/6 from floor 11 and to 5/9 from floor 12 up; a is 4, or 2
    /// with the jump spell (MB_LEAP); u the forward speed; dt the tick length in 1/256 s (the
    /// frame's time, at most 64). Between ticks the path is a straight chord, lower than the
    /// curve by at most a dt^2 / 8 (<see cref="Sag"/>): the model takes that off, so it never
    /// says more than the game does. The game also rounds the rise per step towards zero
    /// (full_move's zspeed = vel * 256 / speed, a whole number), which costs up to 1/64 level
    /// a tile on the way up (<see cref="Rounding"/>). Load does not change the flight, only the run-up.
    /// Distances are of the player's centre; the footprint is a quarter tile to each side.
    /// tests/UwEditor.Core.Tests/Data/jump-sim.csv is the game's integer maths, tick by tick.
    /// </summary>
    public static class JumpModel
    {
        /// <summary>The footprint's half width in tiles (radius 2 cells of 1/8 tile; COMOBJ.DAT item 0x7F, both games).</summary>
        public const double Radius = 0.25;
        /// <summary>The walking step in floor levels (PN.b24 = 8 z units: get_pcoll steps up or down by at most that).</summary>
        public const int WalkStep = 1;
        public const int SprintSpeed = 822, StandingSpeed = 470;

        public static int Speed(JumpKind kind) { return kind == JumpKind.Sprint ? SprintSpeed : StandingSpeed; }
        static int Gravity(bool leap) { return leap ? 2 : 4; }

        /// <summary>The upward speed leaving a floor of this level (0-15): cut when PN.z &gt; 0x280 and again when &gt; 0x2C0.</summary>
        public static int UpSpeed(int floor)
        {
            if (floor >= 12) return 611 * 5 / 6 * 2 / 3;   // 339
            if (floor >= 11) return 611 * 5 / 6;           // 509
            return 611;
        }

        /// <summary>How far, in levels, the straight path between two ticks lies under the parabola at most.</summary>
        public static double Sag(bool leap, int dt) { return Gravity(leap) * (double)dt * dt / 8 / 16384; }

        static void Curve(JumpKind kind, bool leap, int floor, int dt, out double b, out double c)
        {
            // the speed along the heading is sin * speed >> 15 with sin 32767: one less
            double u = Speed(kind) - 1, a = Gravity(leap);
            b = 4 * (UpSpeed(floor) - a * dt / 2) / u;
            c = 131072 * a / (u * u);
        }

        /// <summary>The feet's height in levels above the take-off floor, x tiles on (never more than the game's).</summary>
        public static double Height(double x, JumpKind kind, bool leap, int floor, int dt)
        {
            double b, c;
            Curve(kind, leap, floor, dt, out b, out c);
            return b * x - c * x * x - Sag(leap, dt) - Math.Min(Math.Max(x, 0), b / (2 * c)) / 64;
        }

        /// <summary>What the whole-number rise per step costs by the peak, in levels: 1/64 a tile travelled.</summary>
        public static double Rounding(JumpKind kind, bool leap, int floor, int dt) { return PeakAt(kind, leap, floor, dt) / 64; }

        /// <summary>The highest the feet get, in levels above the take-off floor (the same for both kinds).</summary>
        public static double Peak(bool leap, int floor, int dt)
        {
            double a = Gravity(leap), v = UpSpeed(floor) - a * dt / 2;
            // the rounding by the standing long jump's peak: the slower jump rounds less, and both peak at the same height otherwise
            return Math.Max(0, v * v / (2 * a) / 16384 - Sag(leap, dt) - Rounding(JumpKind.Sprint, leap, floor, dt));
        }

        /// <summary>Where the feet are highest, in tiles from the take-off.</summary>
        public static double PeakAt(JumpKind kind, bool leap, int floor, int dt)
        {
            double b, c;
            Curve(kind, leap, floor, dt, out b, out c);
            return b / (2 * c);
        }

        /// <summary>
        /// The highest step, in floor levels, a jump gets onto: while rising, the feet snap onto
        /// a floor up to one level above them (set_targz with pos + radius &lt; top, then
        /// get_pcoll's |pos - top| &lt;= b24), so floor(peak + 1); never less than the walking step.
        /// </summary>
        public static int Climb(bool leap, int floor, int dt)
        {
            return Math.Max(WalkStep, (int)Math.Floor(Peak(leap, floor, dt) + 1));
        }

        /// <summary>How far on, in tiles, the feet come down to a floor dh levels from the take-off's (negative: lower); NaN when the jump never gets that high.</summary>
        public static double Range(double dh, JumpKind kind, bool leap, int floor, int dt)
        {
            double b, c;
            Curve(kind, leap, floor, dt, out b, out c);
            double d = b * b - 4 * c * (dh + Sag(leap, dt) + b / (2 * c) / 64);
            return d < 0 ? double.NaN : (b + Math.Sqrt(d)) / (2 * c);
        }

        /// <summary>
        /// The run from standing to full sprint, in tiles, at full load: the speed changes by
        /// MaxPlayerAccel a tick, 96 up to half load and 48 at full (newFPS), so 18 ticks.
        /// </summary>
        public static double RunUp(int dt)
        {
            int s = 0;
            long dist = 0;
            while (s < SprintSpeed) { s = Math.Min(s + 48, SprintSpeed); dist += (long)s * dt; }
            return dist / 65536.0;
        }
    }
}
