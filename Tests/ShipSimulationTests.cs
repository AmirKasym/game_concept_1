using System;
using TradeWinds;

internal static class ShipSimulationTests
{
    private static int passed;
    private static int failed;
    private static int Main()
    {
        Run("Anchored ship stays put even with full sail", delegate {
            var ship = new ShipSimulation(); Tick(ship, 500, 1, 1);
            Expect(ship.Distance == 0 && ship.Heading == 0 && ship.Speed == 0, "Anchor allowed motion");
        });
        Run("Sails respect both limits", delegate {
            var ship = new ShipSimulation(); Tick(ship, 1000, 0, 1); Expect(ship.Sail == 1, "Upper bound");
            Tick(ship, 1000, 0, -1); Expect(ship.Sail == 0, "Lower bound");
        });
        Run("Departure accelerates progressively", delegate {
            var ship = Depart(); ship.Step(0.02, 0, 1);
            Expect(ship.Speed > 0 && ship.Speed < 0.02, "Instant acceleration");
            Tick(ship, 1000, 0, 1); Expect(ship.Speed > 3 && ship.Speed <= 8, "Cruise speed");
        });
        Run("Anchor brakes and holds", delegate {
            var ship = Depart(); Tick(ship, 1000, 0, 1); double before = ship.Speed;
            ship.ToggleAnchor(); ship.Step(0.02, 0, 0);
            Expect(ship.Speed > 0 && ship.Speed < before, "No braking inertia");
            Tick(ship, 200, 0, 0); double distance = ship.Distance;
            Tick(ship, 100, 0, 1); Expect(ship.Speed == 0 && ship.Distance == distance, "Anchor drift");
        });
        Run("Releasing sails preserves coasting", delegate {
            var ship = Depart(); Tick(ship, 1000, 0, 1); Tick(ship, 167, 0, -1);
            Expect(ship.Sail == 0 && ship.Speed > 0, "No coasting");
            Tick(ship, 1000, 0, 0); Expect(ship.Speed == 0, "Never stopped");
        });
        Run("Steering changes heading and returns to centre", delegate {
            var ship = Depart(); Tick(ship, 700, 0, 1); Tick(ship, 100, 1, 0);
            Expect(ship.Heading > 1 && ship.X > 0, "No starboard turn");
            Tick(ship, 100, 0, 0); Expect(ship.Rudder == 0, "Rudder stuck");
        });
        Run("Identical commands reproduce the same state", delegate {
            var a = Depart(); var b = Depart(); var random = new Random(42);
            for (int i = 0; i < 10000; i++) {
                double steering = random.NextDouble() * 2 - 1;
                double sail = random.NextDouble() * 2 - 0.8;
                a.Step(0.02, steering, sail); b.Step(0.02, steering, sail);
            }
            Expect(a.X == b.X && a.Z == b.Z && a.Heading == b.Heading && a.Speed == b.Speed, "Replay diverged");
        });
        Run("Map boundary stops the voyage", delegate {
            var ship = Depart(); Tick(ship, 30000, 0, 1);
            Expect(ship.X * ship.X + ship.Z * ship.Z <= 700 * 700, "Outside map");
            Expect(ship.Anchored && ship.Speed == 0, "Boundary did not stop ship");
        });
        Run("Reset removes previous voyage state", delegate {
            var ship = Depart(); Tick(ship, 1000, 1, 1); ship.Reset();
            Expect(ship.Anchored && ship.X == 0 && ship.Z == 0 && ship.Distance == 0 && ship.Clock == 0
                && ship.Speed == 0 && ship.Heading == 0 && ship.Sail == 0 && ship.Rudder == 0, "Stale state");
        });
        Run("Obstacle stop restores the last safe position", delegate {
            var ship = Depart(); Tick(ship, 200, 0, 1); double x = ship.X, z = ship.Z;
            ship.Step(0.02, 0, 1); ship.StopAtObstacle(x, z);
            Expect(ship.X == x && ship.Z == z && ship.Speed == 0 && ship.Anchored, "Obstacle penetration");
        });
        Run("Invalid timestep and commands are rejected", delegate {
            foreach (double dt in new[] { 0, -1, 0.2, double.NaN, double.PositiveInfinity }) {
                bool rejected = false;
                try { new ShipSimulation().Step(dt, 0, 0); } catch (ArgumentException) { rejected = true; }
                Expect(rejected, "Accepted invalid dt");
            }
            bool badInput = false;
            try { new ShipSimulation().Step(0.02, double.NaN, 0); } catch (ArgumentException) { badInput = true; }
            Expect(badInput, "Accepted invalid command");
        });
        Run("Wave samples stay bounded during long sessions", delegate {
            for (int i = 0; i < 10000; i++) {
                double wave = ShipSimulation.WaveHeight(i * 0.37, -i * 0.15, i * 0.02, 1.6);
                Expect(Math.Abs(wave) <= 0.8 && !double.IsNaN(wave), "Unbounded wave");
            }
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    private static ShipSimulation Depart() { var ship = new ShipSimulation(); ship.ToggleAnchor(); return ship; }
    private static void Tick(ShipSimulation ship, int count, double steering, double sail)
    { for (int i = 0; i < count; i++) ship.Step(0.02, steering, sail); }
    private static void Expect(bool result, string message) { if (!result) throw new Exception(message); }
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
}
