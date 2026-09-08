using System;

namespace TradeWinds
{
    // Deck-relative feet position. The host can run exactly the same movement rules.
    public sealed class DeckMotor
    {
        public const float Floor = 2.15f;
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public float VerticalSpeed { get; private set; }
        public bool Grounded { get; private set; }
        public DeckMotor() { Reset(); }

        public void Reset(float x = 1.5f, float z = -4.6f)
        {
            X = x; Y = Floor; Z = z; VerticalSpeed = 0; Grounded = true;
        }

        public void Step(float dt, float horizontal, float forward, float yaw, bool sprint, bool jump)
        {
            if (dt <= 0 || dt > 0.1f || float.IsNaN(dt) || float.IsNaN(yaw) || float.IsInfinity(yaw)
                || float.IsNaN(horizontal) || float.IsInfinity(horizontal)
                || float.IsNaN(forward) || float.IsInfinity(forward)) throw new ArgumentException("Invalid movement input");
            float magnitude = (float)Math.Sqrt(horizontal * horizontal + forward * forward);
            if (magnitude > 1) { horizontal /= magnitude; forward /= magnitude; }
            float angle = yaw * (float)Math.PI / 180;
            float speed = sprint ? 4.2f : 2.7f;
            float dx = ((float)Math.Cos(angle) * horizontal + (float)Math.Sin(angle) * forward) * speed * dt;
            float dz = ((float)Math.Cos(angle) * forward - (float)Math.Sin(angle) * horizontal) * speed * dt;
            if (Grounded && jump) { VerticalSpeed = 6; Grounded = false; }
            float nextX = Math.Max(-2.5f, Math.Min(2.5f, X + dx));
            if (Surface(nextX, Z) <= Y + 0.02f) X = nextX;
            float nextZ = Math.Max(-6.5f, Math.Min(6.3f, Z + dz));
            if (Surface(X, nextZ) <= Y + 0.02f) Z = nextZ;
            float floor = Surface(X, Z);
            VerticalSpeed -= 16 * dt;
            Y += VerticalSpeed * dt;
            if (Y <= floor) { Y = floor; VerticalSpeed = 0; Grounded = true; }
            else Grounded = false;
        }

        private static float Surface(float x, float z)
        {
            if (x > -0.65f && x < 0.65f && z > -0.65f && z < 0.65f) return 14;
            if (x > -0.7f && x < 0.7f && z > -4.4f && z < -3.1f) return 3.7f;
            if (z > 1.7f && z < 4.3f && ((x > -2.8f && x < -0.65f) || (x > 0.75f && x < 2.8f))) return 3.45f;
            return Floor;
        }
    }
}
