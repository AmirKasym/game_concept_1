# Game feel validation — 19 September 2026

The physics component system is integrated into `Assets/TradeWinds/Scenes/FirstVoyage.unity`. The final Windows x64 Development player was built successfully with Unity 6000.6.0f1 (exit code 0).

## Results

- **13/13 physics checks passed:** buoyancy equilibrium, moving-deck support, wave stability, recovery from a 30-degree roll, directional drag, jump momentum, landing, sudden-stop slip, high-speed hull collision, impact capture, pause and resume.
- **15/15 interaction checks passed:** deck attachment/detachment, cargo pickup and weight penalty, throwing, cargo securing, ladders and shore transitions.
- **Host and client checks passed** in two local player processes: connection, jump/landing, sailing, carrying and dropping cargo.
- Coast geometry checks passed: clear starting water, shore contacts and clear seaward approaches.
- Final host and client screenshots were visually inspected. Final filtered runtime logs contained no exceptions, shader errors or unsupported collision-mode warnings.

The wall-impact test stopped the ship before penetration; captured impact velocity change was capped at 8 m/s. The calm-water equilibrium test settled with effectively zero velocity. Earlier resting-contact impact overwrites and secured-cargo feedback were corrected before the final run.

## Deliverables

- Player: `Builds/Windows/FirstVoyage.exe`. Keep this executable together with its entire build directory, including the Data folder and UnityPlayer.dll.
- Complete source listing: [GameFeelSource.md](GameFeelSource.md). It contains all 16 component/support files, followed by configuration instructions.
- Standalone configuration guide: [GameFeelSetup.md](GameFeelSetup.md).
- Final reports and screenshots: [Validation](Validation/).

## Practical limits

Validation covers the current ship, islands and local two-process cooperation. Four-device sessions, WAN latency/packet loss, long-duration soak tests and target-hardware FPS profiling remain release checks. The system does not implement client prediction or rollback. Sampled buoyancy is an arcade approximation; arbitrary hulls and extreme capsizes require separate tuning and validation.

Secured cargo does not automatically increase the hull's Rigidbody mass or inertia tensor. Include expected secured payload in the configured effective ship mass when tuning displacement, or implement a payload mass aggregation system before requiring variable-load draft simulation.
