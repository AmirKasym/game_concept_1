using System;

namespace TradeWinds
{
    [Serializable]
    public struct ShipSnapshot
    {
        public double x, z, heading, speed, sail, rudder, distance, clock, wind;
        public bool anchored;
    }
    // Plain C# authoritative state. Input and rendering never own a second copy.
    public sealed class ShipSimulation
    {
        public double X { get; private set; }
        public double Z { get; private set; }
        public double Heading { get; private set; }
        public double Speed { get; private set; }
        public double Sail { get; private set; }
        public double Rudder { get; private set; }
        public bool Anchored { get; private set; }
        public double Distance { get; private set; }
        public double Clock { get; private set; }
        public double WindEfficiency { get; private set; }
        public const double WorldRadius = 700;

        public ShipSimulation() { Reset(); }

        public void Reset()
        {
            X = Z = Heading = Speed = Sail = Rudder = Distance = Clock = 0;
            Anchored = true;
            WindEfficiency = 1;
        }

        public void ToggleAnchor() { Anchored = !Anchored; }

        public ShipSnapshot Capture()
        {
            return new ShipSnapshot { x = X, z = Z, heading = Heading, speed = Speed, sail = Sail,
                rudder = Rudder, distance = Distance, clock = Clock, wind = WindEfficiency, anchored = Anchored };
        }

        public void Restore(ShipSnapshot state)
        {
            if (!Finite(state.x) || !Finite(state.z) || !Finite(state.heading) || !Finite(state.speed)
                || !Finite(state.sail) || !Finite(state.rudder) || !Finite(state.distance)
                || !Finite(state.clock) || !Finite(state.wind)) throw new ArgumentException("Invalid snapshot");
            X = state.x; Z = state.z; Heading = state.heading; Speed = state.speed; Sail = state.sail;
            Rudder = state.rudder; Distance = state.distance; Clock = state.clock;
            WindEfficiency = state.wind; Anchored = state.anchored;
        }

        public void Step(double dt, double steering, double sailChange)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0 || dt > 0.1)
                throw new ArgumentOutOfRangeException("dt");
            if (!Finite(steering) || !Finite(sailChange))
                throw new ArgumentException("Ship commands must be finite.");
            Clock += dt;
            Sail = Clamp(Sail + Clamp(sailChange, -1, 1) * dt * 0.3, 0, 1);
            Rudder = Approach(Rudder, Clamp(steering, -1, 1), dt * 1.8);
            WindEfficiency = 0.6 + 0.4 * Math.Cos((Heading - 35) * Math.PI / 180);
            double target = Anchored ? 0 : Sail * 8 * WindEfficiency;
            Speed = Approach(Speed, target, dt * (Anchored ? 2.5 : 0.6));
            Heading = (Heading + Rudder * 19 * Math.Min(Speed / 3, 1) * dt + 360) % 360;
            double radians = Heading * Math.PI / 180;
            double nextX = X + Math.Sin(radians) * Speed * dt;
            double nextZ = Z + Math.Cos(radians) * Speed * dt;
            if (nextX * nextX + nextZ * nextZ <= WorldRadius * WorldRadius)
            {
                X = nextX;
                Z = nextZ;
                Distance += Speed * dt;
            }
            else
            {
                Speed = 0;
                Sail = 0;
                Anchored = true;
            }
        }

        public void StopAtObstacle(double previousX, double previousZ)
        {
            X = previousX;
            Z = previousZ;
            Speed = Sail = 0;
            Anchored = true;
        }

        public static double WaveHeight(double x, double z, double time, double strength)
        {
            return strength * (Math.Sin(x * 0.075 + z * 0.12 + time * 0.9) * 0.32
                + Math.Sin(x * -0.16 + z * 0.05 + time * 1.3) * 0.18);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private static double Approach(double value, double target, double step)
        {
            return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
        }
    }
}
