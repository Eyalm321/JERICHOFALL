using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Jerichofall;
using Jerichofall.Motion;

internal static class MovementParityChecks
{
    /// <summary>Replay measured inputs through our independent player/geometry integration.</summary>
    public static int Run([CallerFilePath] string callerPath = "")
    {
        string? explicitPath = Environment.GetEnvironmentVariable("JERICHOFALL_MOVEMENT_FIXTURES");
        string path = explicitPath ?? Path.Combine(Path.GetDirectoryName(callerPath)!, "Fixtures", "movement-regression.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schema").GetInt32() != 1) throw new InvalidDataException("Unknown movement trace schema.");
        var solids = Enumerable.Range(0, 24).Select(y => new string(Enumerable.Range(0, 32)
            .Select(x => y >= 22 || x == 26 && y >= 10 ? '1' : '0').ToArray())).ToArray();
        var arena = new ArenaLayout { Id = "movement-parity", Title = "NUMERICAL FIXTURE", Solids = solids,
            Background = Enumerable.Repeat(new string('0', 32), 24).ToArray(),
            Spawns = [new(80,220), new(120,220), new(160,220), new(200,220)] };
        int checks = 0;
        var mismatches = new List<object>();
        var summaries = new List<string>();
        foreach (var scenario in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name = scenario.GetProperty("name").GetString()!;
            var samples = scenario.GetProperty("samples").EnumerateArray().ToArray();
            var initial = samples[0];
            var hero = new Fighter
            {
                Id = 0, Slot = 0, Position = new(F(initial,4), F(initial,5)), PixelRemainder = new(F(initial,6),F(initial,7)),
                Velocity = new(F(initial,8),F(initial,9)), Facing = I(initial,12), Grounded = B(initial,11),
                Ammo = [Equipment.Stone, Equipment.Stone, Equipment.Stone],
                Motion = new MovementState
                {
                    Mode = (MovementMode)I(initial,10), Facing = I(initial,12), Velocity = new(F(initial,8),F(initial,9)),
                    JumpGrace = F(initial,13), JumpBuffer = F(initial,14), VariableJump = B(initial,15),
                    WallFallCap = F(initial,16), ForcedDirection = I(initial,17),
                    DodgeFrames = F(initial,18), DodgeHoldFrames = F(initial,19)
                }
            };
            var motor = new PlayerMotor(arena, hero);
            int failuresBefore = mismatches.Count;
            foreach (var sample in samples.Skip(1))
            {
                var input = new PlayerInput(I(sample,1), I(sample,2), (Buttons)I(sample,3));
                motor.Step(input);
                hero.Previous = input.Buttons;
                int frame = I(sample,0);
                void Number(string field, float expected, float actual, float tolerance = .0002f)
                {
                    checks++;
                    if (!float.IsFinite(actual) || MathF.Abs(expected - actual) > tolerance)
                        mismatches.Add(new { scenario = name, frame, field, expected, actual });
                }
                Number("x", F(sample,4), hero.Position.X, 0);
                Number("feetY", F(sample,5), hero.Position.Y, 0);
                Number("remainderX", F(sample,6), hero.PixelRemainder.X);
                Number("remainderY", F(sample,7), hero.PixelRemainder.Y);
                Number("vx", F(sample,8), hero.Velocity.X, .00001f);
                Number("vy", F(sample,9), hero.Velocity.Y, .00001f);
                Number("mode", I(sample,10), (int)hero.Motion.Mode, 0);
                Number("ground", I(sample,11), hero.Grounded ? 1 : 0, 0);
                Number("facing", I(sample,12), hero.Facing, 0);
                Number("jumpGrace", F(sample,13), hero.Motion.JumpGrace);
                Number("jumpBuffer", F(sample,14), hero.Motion.JumpBuffer);
                Number("variableJump", I(sample,15), hero.Motion.VariableJump ? 1 : 0, 0);
                Number("wallFallCap", F(sample,16), hero.Motion.WallFallCap);
                Number("autoMove", I(sample,17), hero.Motion.ForcedDirection, 0);
                Number("dodgeFrames", F(sample,18), hero.Motion.DodgeFrames);
                Number("dodgeHoldFrames", F(sample,19), hero.Motion.DodgeHoldFrames);
                Number("dodgeCooldown", I(sample,20), hero.Motion.Mode == MovementMode.Dodge || hero.Motion.DodgeCooldown > 0 ? 1 : 0, 0);
            }
            int failures = mismatches.Count - failuresBefore;
            summaries.Add($"{name}: {samples.Length - 1} frames, {failures} mismatches");
        }
        string? diagnostic = Environment.GetEnvironmentVariable("JERICHOFALL_PARITY_REPORT");
        if (!string.IsNullOrWhiteSpace(diagnostic))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(diagnostic))!);
            File.WriteAllText(diagnostic, JsonSerializer.Serialize(new { passed = mismatches.Count == 0, checks, summaries, mismatches }, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (mismatches.Count > 0)
            throw new InvalidOperationException("Reference movement replay failed:\n" + string.Join('\n', summaries) + "\nFirst differences:\n" + JsonSerializer.Serialize(mismatches.Take(16)));
        Console.WriteLine($"PASS reference movement: {checks} numerical/state comparisons across {summaries.Count} recorded scenarios.");
        return checks;
    }

    private static float F(JsonElement row, int index) => row[index].GetSingle();
    private static int I(JsonElement row, int index) => row[index].GetInt32();
    private static bool B(JsonElement row, int index) => I(row,index) != 0;
}
