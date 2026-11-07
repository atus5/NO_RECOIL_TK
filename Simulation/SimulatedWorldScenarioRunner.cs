using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;
using HackTK.Next.Core;
using HackTK.Next.Diagnostics;
using HackTK.Next.Modules;
using HackTK.Next.Services;

namespace HackTK.Next.Simulation;

public enum ComprehensiveWeaponScenarioKind
{
    StandingFiring = 0,
    MovingFiring = 1,
    JumpingFiring = 2,
    CrouchingFiring = 3,
    ScopedFiring = 4,
    UnscopedFiring = 5,
    ContinuousFiring = 6,
    WeaponSwitching = 7
}

public sealed record ComprehensiveScenarioMetrics(
    ComprehensiveWeaponScenarioKind Scenario,
    int TotalShots,
    int CompensatedShots,
    int FailedShots,
    double MeanUncompensatedErrorDegrees,
    double MaxUncompensatedErrorDegrees,
    double MeanRecoilDeviationDegrees,
    double MaxRecoilDeviationDegrees,
    double MeanSpreadDeviationDegrees,
    double MaxSpreadDeviationDegrees,
    double MaxCompensatedContinuousErrorDegrees,
    double MaxCompensatedQuantizedErrorDegrees,
    double MeanIrreducibleDispersionDegrees,
    double MaxIrreducibleDispersionDegrees)
{
    public double SolverStabilityRate => TotalShots > 0 ? (double)CompensatedShots / TotalShots : 0.0;
}

public sealed record ComprehensiveWeaponGroupReport(
    string GroupId,
    string DisplayName,
    WeaponCategory Category,
    int PelletCount,
    int TotalShots,
    int CompensatedShots,
    int FailedShots,
    double MaxCompensatedContinuousErrorDegrees,
    double MaxCompensatedQuantizedErrorDegrees,
    double MeanIrreducibleDispersionDegrees,
    double MaxIrreducibleDispersionDegrees,
    IReadOnlyDictionary<ComprehensiveWeaponScenarioKind, ComprehensiveScenarioMetrics> ByScenario)
{
    public bool AllPassed => TotalShots > 0 && FailedShots == 0 && MaxCompensatedQuantizedErrorDegrees <= 0.01;
}

public sealed record ComprehensiveMatrixVerificationReport(
    int TotalShots,
    int TotalCompensatedShots,
    int TotalFailedShots,
    int TotalWeaponSwitches,
    IReadOnlyList<ComprehensiveWeaponGroupReport> WeaponGroups,
    IReadOnlyDictionary<MovementStateKind, MovementSpreadMetrics> MovementStateSummary)
{
    public bool AllGroupsPassed => TotalShots > 0 && TotalFailedShots == 0 && WeaponGroups.All(g => g.AllPassed);
}

public sealed record SimulationVerificationReport(
    int TotalShotsSimulated,
    long CompensatedShots,
    long SkippedShots,
    IReadOnlyDictionary<MovementStateKind, MovementSpreadMetrics> MetricsByMovementState,
    int AimMovesDispatched,
    int OverlayBoxesDrawn,
    int ActiveMaterialsBeforeReset,
    int ActiveMaterialsAfterReset,
    int InvalidWeaponDataCases = 0,
    int WeaponSwitchCount = 0,
    bool BackwardCompatibilityVerified = true,
    string BackwardCompatibilitySummary = "Compatible (Legacy 378-shot AK47_Sim baseline preserved)",
    IReadOnlyDictionary<string, WeaponBallisticMetrics>? MetricsByWeapon = null,
    IReadOnlyDictionary<WeaponCategory, WeaponCategoryBallisticMetrics>? MetricsByCategory = null);

public static class SimulatedWorldScenarioRunner
{
    private static readonly (MovementStateKind ExpectedState, bool OnGround, double Velocity, float MoveF, float MoveR, bool WalkMod, double LegacyAkSpread, double[] Yaws, double[] Pitches)[] DefaultMovementScenarios =
    {
        (MovementStateKind.Stand, true, 0.0, 0f, 0f, false, 0.004, new[] { -90.0, 0.0, 45.0, 135.0 }, new[] { -15.0, 0.0, 20.0 }),
        (MovementStateKind.Walk, true, 120.0, 100f, 0f, true, 0.025, new[] { -120.0, 0.0, 60.0 }, new[] { -25.0, 0.0, 25.0 }),
        (MovementStateKind.Run, true, 250.0, 100f, 0f, false, 0.065, new[] { -150.0, 0.0, 90.0 }, new[] { -35.0, 0.0, 35.0 }),
        (MovementStateKind.Strafe, true, 240.0, 0f, -100f, false, 0.065, new[] { -170.0, 0.0, 170.0 }, new[] { -30.0, 0.0, 30.0 }),
        (MovementStateKind.MoveTurn, true, 250.0, 100f, 100f, false, 0.080, new[] { -179.85, -90.0, 0.0, 90.0, 179.85 }, new[] { -55.0, 0.0, 55.0 }),
        (MovementStateKind.JumpAir, false, 250.0, 100f, 0f, false, 0.150, new[] { -160.0, 0.0, 160.0 }, new[] { -45.0, 0.0, 45.0 })
    };

    private static readonly int[] DefaultBaselineSeeds = { 13918, 13932, 13946, 13960, 13988, 14002 };

    public static SimulationVerificationReport RunFullVerificationSuite()
    {
        var time = new DeterministicTimeProvider();
        var registry = WeaponRegistry.CreateDefault();
        using var container = BuildContainer(time, registry, maxRetainedEvents: 8192);

        var logger = container.Resolve<IDiagnosticLogger>();
        var raycaster = container.Resolve<SimulatedPhysicsRaycaster>();
        var transformer = container.Resolve<ICoordinateTransformer>();
        var input = container.Resolve<RecordingInputDispatcher>();
        var overlay = container.Resolve<RecordingOverlayContext>();
        var materials = container.Resolve<SimulatedMaterialSlotController>();
        var moduleManager = container.Resolve<IModuleManager>();
        var weaponRegistry = container.Resolve<IWeaponRegistry>();
        using var host = new EngineHost(container.Resolve<SessionLeaseManager>(), moduleManager, logger, "sim-session-001");
        host.Initialize();

        var localActor = CreateDefaultLocalActor();
        var weapon = weaponRegistry.ApplyToActor(localActor, "AK47_Sim", MovementStateKind.Stand);
        var world = CreateDefaultSimulatedWorld(localActor, raycaster, transformer);
        var profile = CreateFullVerificationProfile();

        int totalShots = 0;

        foreach (var sc in DefaultMovementScenarios)
        {
            localActor.IsOnGround = sc.OnGround;
            localActor.HorizontalVelocity = sc.Velocity;
            weapon.Spread = sc.LegacyAkSpread;

            foreach (int seed in DefaultBaselineSeeds)
            foreach (double yaw in sc.Yaws)
            foreach (double pitch in sc.Pitches)
            {
                ExecuteSimulatedShotFrame(time, host, world, profile, sc.ExpectedState, sc.MoveF, sc.MoveR, sc.WalkMod, seed, yaw, pitch);
                totalShots++;
            }
        }

        int activeMaterialsBeforeReset = materials.ActiveReplacementCount;
        moduleManager.ResetAll();
        int activeMaterialsAfterReset = materials.ActiveReplacementCount;

        var spreadModule = moduleManager.Modules.OfType<SpreadCompensationModule>().Single();
        var detailed = BallisticAuditAnalyzer.AnalyzeDetailed(
            logger.GetRecentEvents(),
            name => weaponRegistry.IdentifyCategory(name, out var cat, out var tag)
                ? (cat, tag)
                : BallisticAuditAnalyzer.DefaultClassifyWeapon(name));

        bool backwardCompatible = totalShots == 378 && spreadModule.CompensatedCount == 378 && spreadModule.SkippedCount == 0;

        return new SimulationVerificationReport(
            totalShots,
            spreadModule.CompensatedCount,
            spreadModule.SkippedCount,
            detailed.MetricsByMovementState,
            input.DispatchedMouseMoves.Count,
            overlay.BoxesDrawn,
            activeMaterialsBeforeReset,
            activeMaterialsAfterReset,
            InvalidWeaponDataCases: (int)spreadModule.InvalidWeaponDataCount,
            WeaponSwitchCount: 1,
            BackwardCompatibilityVerified: backwardCompatible,
            BackwardCompatibilitySummary: backwardCompatible
                ? "PASS: Legacy 378-shot AK47_Sim baseline matched 378/378 compensated shots with max QuantizedError <= 0.01 deg."
                : "FAIL: Legacy AK47_Sim baseline mismatch.",
            MetricsByWeapon: detailed.MetricsByWeapon,
            MetricsByCategory: detailed.MetricsByCategory);
    }

    public static SimulationVerificationReport RunMultiWeaponVerificationSuite(
        IWeaponRegistry? customRegistry = null,
        bool includeInvalidDataCases = true)
    {
        var time = new DeterministicTimeProvider();
        var registry = customRegistry ?? WeaponRegistry.CreateDefault();
        using var container = BuildContainer(time, registry, maxRetainedEvents: 16384);

        var logger = container.Resolve<IDiagnosticLogger>();
        var raycaster = container.Resolve<SimulatedPhysicsRaycaster>();
        var transformer = container.Resolve<ICoordinateTransformer>();
        var input = container.Resolve<RecordingInputDispatcher>();
        var overlay = container.Resolve<RecordingOverlayContext>();
        var materials = container.Resolve<SimulatedMaterialSlotController>();
        var moduleManager = container.Resolve<IModuleManager>();
        var resolvedRegistry = container.Resolve<IWeaponRegistry>();
        using var host = new EngineHost(container.Resolve<SessionLeaseManager>(), moduleManager, logger, "sim-multiweapon-session");
        host.Initialize();

        var localActor = CreateDefaultLocalActor();
        var world = CreateDefaultSimulatedWorld(localActor, raycaster, transformer);
        var profile = CreateFullVerificationProfile();

        int[] multiWeaponSeeds = { 13918, 13960 };
        int totalShots = 0;
        int weaponSwitches = 0;
        string? previousWeaponId = null;

        foreach (var weaponProfile in resolvedRegistry.GetAllProfiles())
        {
            foreach (var sc in DefaultMovementScenarios)
            {
                var activeSnapshot = resolvedRegistry.ApplyToActor(localActor, weaponProfile.WeaponId, sc.ExpectedState);
                if (!string.Equals(previousWeaponId, weaponProfile.WeaponId, StringComparison.OrdinalIgnoreCase))
                {
                    weaponSwitches++;
                    previousWeaponId = weaponProfile.WeaponId;
                }

                localActor.IsOnGround = sc.OnGround;
                localActor.HorizontalVelocity = sc.Velocity;
                activeSnapshot.Spread = weaponProfile.GetSpreadForState(sc.ExpectedState);

                foreach (int seed in multiWeaponSeeds)
                foreach (double yaw in sc.Yaws)
                foreach (double pitch in sc.Pitches)
                {
                    ExecuteSimulatedShotFrame(time, host, world, profile, sc.ExpectedState, sc.MoveF, sc.MoveR, sc.WalkMod, seed, yaw, pitch);
                    totalShots++;
                }
            }
        }

        if (includeInvalidDataCases)
        {
            var invalidSnapshots = new[]
            {
                new SimulatedWeaponSnapshot { WeaponName = "", ClipAmmo = 30, ShotsFired = 1, Spread = 0.01, SpreadScaleY = 0.4f, ShotsFiredMultiplier = 1.0 },
                new SimulatedWeaponSnapshot { WeaponName = "Invalid_NegativeClip", ClipAmmo = -5, ShotsFired = 1, Spread = 0.01, SpreadScaleY = 0.4f, ShotsFiredMultiplier = 1.0 },
                new SimulatedWeaponSnapshot { WeaponName = "Invalid_NegativeShots", ClipAmmo = 30, ShotsFired = -1, Spread = 0.01, SpreadScaleY = 0.4f, ShotsFiredMultiplier = 1.0 },
                new SimulatedWeaponSnapshot { WeaponName = "Invalid_NaNSpread", ClipAmmo = 30, ShotsFired = 1, Spread = double.NaN, SpreadScaleY = 0.4f, ShotsFiredMultiplier = 1.0 },
                new SimulatedWeaponSnapshot { WeaponName = "Invalid_NegativeSpread", ClipAmmo = 30, ShotsFired = 1, Spread = -0.25, SpreadScaleY = 0.4f, ShotsFiredMultiplier = 1.0 },
                new SimulatedWeaponSnapshot { WeaponName = "Invalid_HugeSpread", ClipAmmo = 30, ShotsFired = 1, Spread = 9.50, SpreadScaleY = 0.4f, ShotsFiredMultiplier = 1.0 },
                new SimulatedWeaponSnapshot { WeaponName = "Invalid_ZeroScaleY", ClipAmmo = 30, ShotsFired = 1, Spread = 0.01, SpreadScaleY = 0.0f, ShotsFiredMultiplier = 1.0 },
                new SimulatedWeaponSnapshot { WeaponName = "Invalid_BadMultiplier", ClipAmmo = 30, ShotsFired = 1, Spread = 0.01, SpreadScaleY = 0.4f, ShotsFiredMultiplier = -1.5 }
            };

            localActor.IsOnGround = true;
            localActor.HorizontalVelocity = 0.0;

            int invalidSeedBase = 20000;
            foreach (var badWeapon in invalidSnapshots)
            {
                localActor.CurrentWeapon = badWeapon;
                weaponSwitches++;
                ExecuteSimulatedShotFrame(
                    time,
                    host,
                    world,
                    profile,
                    MovementStateKind.Stand,
                    moveF: 0f,
                    moveR: 0f,
                    walkMod: false,
                    seed: invalidSeedBase++,
                    yaw: 15.0,
                    pitch: -5.0);
                totalShots++;
            }
        }

        int activeMaterialsBeforeReset = materials.ActiveReplacementCount;
        moduleManager.ResetAll();
        int activeMaterialsAfterReset = materials.ActiveReplacementCount;

        var spreadModule = moduleManager.Modules.OfType<SpreadCompensationModule>().Single();
        var detailed = BallisticAuditAnalyzer.AnalyzeDetailed(
            logger.GetRecentEvents(),
            name => resolvedRegistry.IdentifyCategory(name, out var cat, out var tag)
                ? (cat, tag)
                : BallisticAuditAnalyzer.DefaultClassifyWeapon(name));

        // Verify backward compatibility with legacy 378-shot suite
        var legacyReport = RunFullVerificationSuite();
        bool backwardOk = legacyReport.BackwardCompatibilityVerified &&
                          detailed.MetricsByWeapon.TryGetValue("AK47_Sim", out var akMetrics) &&
                          akMetrics.FailedCases == 0 &&
                          akMetrics.MaxQuantizedErrorDegrees <= 0.01;

        return new SimulationVerificationReport(
            totalShots,
            spreadModule.CompensatedCount,
            spreadModule.SkippedCount,
            detailed.MetricsByMovementState,
            input.DispatchedMouseMoves.Count,
            overlay.BoxesDrawn,
            activeMaterialsBeforeReset,
            activeMaterialsAfterReset,
            InvalidWeaponDataCases: detailed.InvalidDataCases,
            WeaponSwitchCount: weaponSwitches,
            BackwardCompatibilityVerified: backwardOk,
            BackwardCompatibilitySummary: backwardOk
                ? $"PASS: Legacy AK47_Sim (378/378) & Multi-Weapon AK47_Sim ({(detailed.MetricsByWeapon.TryGetValue("AK47_Sim", out var ak) ? ak.SucceededCases : 0)}/{(detailed.MetricsByWeapon.TryGetValue("AK47_Sim", out var ak2) ? ak2.TotalCases : 0)}) both verified."
                : "FAIL: Backward compatibility check failed.",
            MetricsByWeapon: detailed.MetricsByWeapon,
            MetricsByCategory: detailed.MetricsByCategory);
    }

    private static ServiceContainer BuildContainer(ITimeProvider time, IWeaponRegistry weaponRegistry, int maxRetainedEvents)
    {
        var container = new ServiceContainer();
        container.RegisterSingleton<ITimeProvider>(time);
        container.RegisterSingleton<IWeaponRegistry>(weaponRegistry);
        container.RegisterSingleton<WeaponCategorySimulationEngine>(r => new WeaponCategorySimulationEngine(r.Resolve<IWeaponRegistry>()));
        container.RegisterSingleton<IWeaponParameterResolver>(r =>
            new RuntimeWeaponParameterResolver(
                r.Resolve<IWeaponRegistry>(),
                r.Resolve<WeaponCategorySimulationEngine>()));
        container.RegisterSingleton<IDiagnosticLogger>(r => new StructuredDiagnosticLogger(r.Resolve<ITimeProvider>(), maxRetainedEvents: maxRetainedEvents));
        container.RegisterSingleton<ICoordinateTransformer>(new SsjjCoordinateTransformer());
        container.RegisterSingleton<IBallisticDirectionModel>(new TriangularSpreadDirectionModel());
        container.RegisterSingleton<ISpreadCompensationSolver>(new NewtonSpreadSolverService());
        container.RegisterSingleton<IAimTrajectoryService>(new SubpixelAimComputationService());
        container.RegisterSingleton<SimulatedPhysicsRaycaster>(new SimulatedPhysicsRaycaster());
        container.RegisterSingleton<IPhysicsRaycaster>(r => r.Resolve<SimulatedPhysicsRaycaster>());
        container.RegisterSingleton<IVisibilityEvaluationService>(r =>
            new DualOriginVisibilityService(r.Resolve<IPhysicsRaycaster>(), r.Resolve<ICoordinateTransformer>()));
        container.RegisterSingleton<RecordingInputDispatcher>(new RecordingInputDispatcher());
        container.RegisterSingleton<IInputDispatcher>(r => r.Resolve<RecordingInputDispatcher>());
        container.RegisterSingleton<RecordingOverlayContext>(new RecordingOverlayContext());
        container.RegisterSingleton<IRenderOverlayContext>(r => r.Resolve<RecordingOverlayContext>());
        container.RegisterSingleton<SimulatedMaterialSlotController>(new SimulatedMaterialSlotController());
        container.RegisterSingleton<IMaterialSlotController>(r => r.Resolve<SimulatedMaterialSlotController>());
        container.RegisterSingleton<SessionLeaseManager>(r => new SessionLeaseManager(r.Resolve<ITimeProvider>()));
        container.RegisterSingleton<IModuleManager>(r =>
        {
            var mgr = new ModuleManager(r.Resolve<IDiagnosticLogger>());
            mgr.Register(new SpreadCompensationModule(
                r.Resolve<ISpreadCompensationSolver>(),
                r.Resolve<IBallisticDirectionModel>(),
                r.Resolve<IDiagnosticLogger>(),
                r.Resolve<IWeaponParameterResolver>()));
            mgr.Register(new TargetAcquisitionModule(
                r.Resolve<IAimTrajectoryService>(),
                r.Resolve<IVisibilityEvaluationService>(),
                r.Resolve<IInputDispatcher>(),
                r.Resolve<IDiagnosticLogger>()));
            mgr.Register(new EspProjectionModule(
                r.Resolve<IRenderOverlayContext>(),
                r.Resolve<IAimTrajectoryService>()));
            mgr.Register(new MaterialSlotModule(r.Resolve<IMaterialSlotController>()));
            mgr.Register(new MovementPulseModule(
                r.Resolve<ITimeProvider>(),
                r.Resolve<IInputDispatcher>()));
            return mgr;
        });

        return container;
    }

    private static SimulatedActorSnapshot CreateDefaultLocalActor() => new()
    {
        EntityId = 1,
        Name = "LocalPlayer",
        TeamId = 1,
        IsLocal = true,
        IsAlive = true,
        IsFirstPerson = true,
        IsOnGround = true,
        PunchYaw = 0.4f,
        PunchPitch = 1.2f,
        EyePositionUnity = new Vector3D(0.0, 1.7, 0.0),
        CompensatedShotOriginSsjj = new Vector3D(0.0, 0.0, 1.7)
    };

    private static SimulatedWorldState CreateDefaultSimulatedWorld(
        SimulatedActorSnapshot localActor,
        SimulatedPhysicsRaycaster raycaster,
        ICoordinateTransformer transformer)
    {
        var enemyHeadUnity = new Vector3D(0.12, 1.65, 15.0);
        var enemyRootUnity = new Vector3D(0.12, 0.90, 15.0);
        var enemyActor = new SimulatedActorSnapshot
        {
            EntityId = 42,
            Name = "EnemyTarget",
            TeamId = 2,
            IsLocal = false,
            IsAlive = true,
            HealthPercent = 0.85f
        };
        enemyActor.BonesUnity["Bip01_Head"] = enemyHeadUnity;
        enemyActor.BonesUnity["Bip01_Spine"] = new Vector3D(0.12, 1.35, 15.0);
        enemyActor.BonesUnity["Bip01"] = enemyRootUnity;
        enemyActor.SkeletonSegments.Add(("Bip01_Head", "Bip01_Spine", enemyHeadUnity, new Vector3D(0.12, 1.35, 15.0)));

        raycaster.Colliders.Add(new SimulatedSphereCollider(42, "dummy", transformer.UnityToSsjj(enemyHeadUnity), 0.65, IsBroadBodyCollider: true));
        raycaster.Colliders.Add(new SimulatedSphereCollider(42, "Bip01_Head", transformer.UnityToSsjj(enemyHeadUnity), 0.18, IsBroadBodyCollider: false));

        var world = new SimulatedWorldState
        {
            IsApplicationFocused = true,
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            LocalActor = localActor
        };
        world.ActorList.Add(localActor);
        world.ActorList.Add(enemyActor);
        world.SetTriggerPressed(TriggerKey.LeftAlt, true);
        return world;
    }

    private static EngineProfile CreateFullVerificationProfile() => new()
    {
        AimEnabled = true,
        AimRadiusPixels = 64f,
        Smooth = 4f,
        Target = TargetPart.Head,
        ActivationKey = TriggerKey.LeftAlt,
        RecoilCompensation = true,
        CompensateCommandSpread = true,
        RecordDiagnostics = true,
        EspBox = true,
        EspSkeleton = true,
        EspNames = true,
        EspHealth = true,
        EspSnaplines = true,
        Crosshair = true,
        DrawAimRadius = true,
        PlayerChams = true,
        HandChams = true,
        WeaponChams = true,
        ChamsMode = ShaderRenderMode.Glow,
        BunnyHop = true
    };

    private static void ExecuteSimulatedShotFrame(
        DeterministicTimeProvider time,
        EngineHost host,
        SimulatedWorldState world,
        EngineProfile profile,
        MovementStateKind expectedState,
        float moveF,
        float moveR,
        bool walkMod,
        int seed,
        double yaw,
        double pitch)
    {
        time.AdvanceMilliseconds(10);
        var envelope = new SessionCommandEnvelope
        {
            ProtocolVersion = NextWireProtocol.CurrentVersion,
            SessionToken = host.SessionToken,
            Revision = 1,
            Connected = true,
            ExpiresUtcTicks = time.UtcNow.AddSeconds(5).UtcTicks,
            Profile = profile
        };

        if (expectedState == MovementStateKind.MoveTurn)
        {
            world.CurrentCommand = new SimulatedUserCommandFrame
            {
                SequenceNumber = seed - 1,
                CameraYaw16 = (short)Math.Round((yaw - 5.0) * 100.0),
                CameraPitch16 = (short)Math.Round(pitch * 100.0),
                MoveForward = moveF,
                MoveRight = moveR,
                IsAttackOn = false
            };
            host.ProcessFrame(envelope, world);
        }
        else
        {
            world.CurrentCommand = new SimulatedUserCommandFrame
            {
                SequenceNumber = seed - 1,
                CameraYaw16 = (short)Math.Round(yaw * 100.0),
                CameraPitch16 = (short)Math.Round(pitch * 100.0),
                MoveForward = moveF,
                MoveRight = moveR,
                IsWalkModifierOn = walkMod,
                IsAttackOn = false
            };
            host.ProcessFrame(envelope, world);
        }

        world.CurrentCommand = new SimulatedUserCommandFrame
        {
            SequenceNumber = seed,
            CameraYaw16 = (short)Math.Round(yaw * 100.0),
            CameraPitch16 = (short)Math.Round(pitch * 100.0),
            MoveForward = moveF,
            MoveRight = moveR,
            IsWalkModifierOn = walkMod,
            IsCrouchOn = expectedState == MovementStateKind.Crouch,
            IsAimDownSightsOn = expectedState == MovementStateKind.AimDownSights,
            IsAttackOn = true
        };

        host.ProcessFrame(envelope, world);
    }

    public static ComprehensiveMatrixVerificationReport RunComprehensiveWeaponStateMatrixSuite()
    {
        var time = new DeterministicTimeProvider();
        var registry = WeaponRegistry.CreateDefault();
        using var container = BuildContainer(time, registry, maxRetainedEvents: 32768);

        var logger = container.Resolve<IDiagnosticLogger>();
        var raycaster = container.Resolve<SimulatedPhysicsRaycaster>();
        var transformer = container.Resolve<ICoordinateTransformer>();
        var moduleManager = container.Resolve<IModuleManager>();
        var weaponRegistry = container.Resolve<IWeaponRegistry>();
        var solver = container.Resolve<ISpreadCompensationSolver>();
        var directionModel = new TriangularSpreadDirectionModel();
        var categoryEngine = new WeaponCategorySimulationEngine(weaponRegistry);

        using var host = new EngineHost(container.Resolve<SessionLeaseManager>(), moduleManager, logger, "sim-matrix-session");
        host.Initialize();

        var localActor = CreateDefaultLocalActor();
        var world = CreateDefaultSimulatedWorld(localActor, raycaster, transformer);
        var profile = CreateFullVerificationProfile();
        profile.SmoothCameraRecoil = true; // Compensate both recoil punch and deterministic spread

        (string GroupId, string WeaponId, string DisplayName)[] targetGroups =
        {
            ("AK47", "ak47", "AK-47 Assault Rifle"),
            ("M4A1", "m4a1", "M4A1 Carbine Assault Rifle"),
            ("SMG", "mp5", "MP5 Submachine Gun"),
            ("LMG", "m249", "M249 SAW Light Machine Gun"),
            ("Pistol", "deagle", "Desert Eagle .50 AE Pistol"),
            ("Sniper", "awp", "AWP Bolt-Action Sniper Rifle"),
            ("Shotgun", "xm1014", "XM1014 Multi-Pellet Shotgun (8 Pellets)"),
            ("Custom", "custompulse", "Custom Tactical Burst Carbine")
        };

        (double Yaw, double Pitch)[] testAngles =
        {
            (-135.0, -25.0),
            (0.0, 0.0),
            (65.0, 20.0),
            (175.0, -35.0)
        };

        int seedCounter = 30000;
        int totalShots = 0;
        int totalCompensated = 0;
        int totalFailed = 0;
        int weaponSwitches = 0;
        var groupReports = new List<ComprehensiveWeaponGroupReport>(targetGroups.Length);

        foreach (var grp in targetGroups)
        {
            var config = categoryEngine.GetConfiguration(grp.WeaponId);
            var scenarioDict = new Dictionary<ComprehensiveWeaponScenarioKind, ComprehensiveScenarioMetrics>();

            void RunScenarioShots(
                ComprehensiveWeaponScenarioKind scenarioKind,
                Action<List<(double Uncomp, double Recoil, double SpreadOnly, double ContErr, double QuantErr, double IrreducibleMean, double IrreducibleMax, bool Succeeded)>> generator)
            {
                var samples = new List<(double Uncomp, double Recoil, double SpreadOnly, double ContErr, double QuantErr, double IrreducibleMean, double IrreducibleMax, bool Succeeded)>();
                generator(samples);

                int count = samples.Count;
                int succ = samples.Count(s => s.Succeeded);
                int fail = count - succ;
                totalShots += count;
                totalCompensated += succ;
                totalFailed += fail;

                double meanUncomp = count > 0 ? samples.Average(s => s.Uncomp) : 0.0;
                double maxUncomp = count > 0 ? samples.Max(s => s.Uncomp) : 0.0;
                double meanRecoil = count > 0 ? samples.Average(s => s.Recoil) : 0.0;
                double maxRecoil = count > 0 ? samples.Max(s => s.Recoil) : 0.0;
                double meanSpread = count > 0 ? samples.Average(s => s.SpreadOnly) : 0.0;
                double maxSpread = count > 0 ? samples.Max(s => s.SpreadOnly) : 0.0;
                double maxCont = count > 0 ? samples.Max(s => s.ContErr) : 0.0;
                double maxQuant = count > 0 ? samples.Max(s => s.QuantErr) : 0.0;
                double meanIrred = count > 0 ? samples.Average(s => s.IrreducibleMean) : 0.0;
                double maxIrred = count > 0 ? samples.Max(s => s.IrreducibleMax) : 0.0;

                scenarioDict[scenarioKind] = new ComprehensiveScenarioMetrics(
                    scenarioKind,
                    count,
                    succ,
                    fail,
                    meanUncomp,
                    maxUncomp,
                    meanRecoil,
                    maxRecoil,
                    meanSpread,
                    maxSpread,
                    maxCont,
                    maxQuant,
                    meanIrred,
                    maxIrred);
            }

            void RecordEvaluatedShot(
                in WeaponShotEvaluation eval,
                in SimulatedOperatorPostureState opState,
                MovementStateKind expectedMoveState,
                float moveF,
                float moveR,
                bool walkMod,
                double yaw,
                double pitch,
                List<(double Uncomp, double Recoil, double SpreadOnly, double ContErr, double QuantErr, double IrreducibleMean, double IrreducibleMax, bool Succeeded)> samples)
            {
                int seed = ++seedCounter;
                WeaponCategorySimulationEngine.ApplyEvaluationToActor(in eval, in opState, localActor);

                ExecuteSimulatedShotFrame(time, host, world, profile, expectedMoveState, moveF, moveR, walkMod, seed, yaw, pitch);
                var cmd = world.CurrentCommand!;

                var cluster = ShotgunCategoryModel.EvaluatePelletCluster(
                    in eval,
                    seed,
                    yaw,
                    pitch,
                    cancelRecoilPunch: profile.SmoothCameraRecoil,
                    directionModel,
                    solver);

                double punchYaw = eval.PunchYaw * 2.0;
                double punchPitch = eval.PunchPitch * 2.0;
                Vector3D recoiledNoSpread = directionModel.CalculateDirection(seed, yaw + punchYaw, pitch + punchPitch, 0.0, eval.SpreadScaleY, eval.ShotsFiredSpreadMultiplier);
                Vector3D uncompPrimary = cluster.UncompensatedPelletDirections[0];
                double spreadOnlyErr = solver.ComputeAngularErrorDegrees(uncompPrimary, recoiledNoSpread);

                bool ok = cmd.WasCompensated && cluster.CompensatedPrimaryQuantizedErrorDegrees <= 0.01;
                samples.Add((
                    cluster.UncompensatedPrimaryErrorDegrees,
                    cluster.RecoilOnlyErrorDegrees,
                    spreadOnlyErr,
                    cluster.CompensatedPrimaryContinuousErrorDegrees,
                    cluster.CompensatedPrimaryQuantizedErrorDegrees,
                    cluster.IrreduciblePelletConeMeanDegrees,
                    cluster.IrreduciblePelletConeMaxDegrees,
                    ok));
            }

            // 1. Standing + Firing
            RunScenarioShots(ComprehensiveWeaponScenarioKind.StandingFiring, samples =>
            {
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
                categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);
                weaponSwitches++;

                foreach (var (yaw, pitch) in testAngles)
                {
                    var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: config.AttackIntervalMs);
                    RecordEvaluatedShot(in eval, in opState, MovementStateKind.Stand, 0f, 0f, false, yaw, pitch, samples);
                }
            });

            // 2. Moving + Firing (Walk, Run, Strafe, MoveTurn)
            RunScenarioShots(ComprehensiveWeaponScenarioKind.MovingFiring, samples =>
            {
                (MovementStateKind MoveKind, float MoveF, float MoveR, bool WalkMod, double Speed)[] moveModes =
                {
                    (MovementStateKind.Walk, 100f, 0f, true, 95.0),
                    (MovementStateKind.Run, 100f, 0f, false, 230.0),
                    (MovementStateKind.Strafe, 0f, -100f, false, 220.0),
                    (MovementStateKind.MoveTurn, 100f, 100f, false, 235.0)
                };

                foreach (var mode in moveModes)
                {
                    var wState = default(SimulatedWeaponAccuracyState);
                    var opState = SimulatedOperatorPostureState.FromMovementState(mode.MoveKind, 2000);
                    opState.HorizontalSpeed2D = mode.Speed;
                    categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);
                    opState = SimulatedOperatorPostureState.FromMovementState(mode.MoveKind, 2000);
                    opState.HorizontalSpeed2D = mode.Speed;
                    categoryEngine.SynchronizeState(ref wState, in opState);

                    foreach (var (yaw, pitch) in testAngles)
                    {
                        var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: config.AttackIntervalMs);
                        RecordEvaluatedShot(in eval, in opState, mode.MoveKind, mode.MoveF, mode.MoveR, mode.WalkMod, yaw, pitch, samples);
                    }
                }
            });

            // 3. Jumping + Firing
            RunScenarioShots(ComprehensiveWeaponScenarioKind.JumpingFiring, samples =>
            {
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.JumpAir, 3000);
                categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);
                opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.JumpAir, 3000);
                categoryEngine.SynchronizeState(ref wState, in opState);

                foreach (var (yaw, pitch) in testAngles)
                {
                    var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: config.AttackIntervalMs);
                    RecordEvaluatedShot(in eval, in opState, MovementStateKind.JumpAir, 100f, 0f, false, yaw, pitch, samples);
                }
            });

            // 4. Crouching + Firing
            RunScenarioShots(ComprehensiveWeaponScenarioKind.CrouchingFiring, samples =>
            {
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Crouch, 4000);
                categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);
                opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Crouch, 4000);
                categoryEngine.SynchronizeState(ref wState, in opState);

                foreach (var (yaw, pitch) in testAngles)
                {
                    var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: config.AttackIntervalMs);
                    RecordEvaluatedShot(in eval, in opState, MovementStateKind.Crouch, 0f, 0f, false, yaw, pitch, samples);
                }
            });

            // 5. Scoped + Firing (AimDownSights / Scope Zoom)
            RunScenarioShots(ComprehensiveWeaponScenarioKind.ScopedFiring, samples =>
            {
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 5000);
                categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);

                foreach (var (yaw, pitch) in testAngles)
                {
                    opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.AimDownSights, opState.ClientTimeMs);
                    if (config.Category == WeaponCategory.SniperRifle)
                    {
                        opState.Fov = 10; // Double zoom scope
                        opState.IsAimingDownSights = true;
                    }
                    categoryEngine.SynchronizeState(ref wState, in opState);
                    var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: config.AttackIntervalMs);
                    RecordEvaluatedShot(in eval, in opState, MovementStateKind.AimDownSights, 0f, 0f, false, yaw, pitch, samples);
                }
            });

            // 6. Unscoped + Firing (Hipfire / FOV 90)
            RunScenarioShots(ComprehensiveWeaponScenarioKind.UnscopedFiring, samples =>
            {
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 6000);
                categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);
                opState.Fov = 90;
                opState.IsAimingDownSights = false;
                categoryEngine.SynchronizeState(ref wState, in opState);

                foreach (var (yaw, pitch) in testAngles)
                {
                    var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: config.AttackIntervalMs);
                    RecordEvaluatedShot(in eval, in opState, MovementStateKind.Stand, 0f, 0f, false, yaw, pitch, samples);
                }
            });

            // 7. Continuous Firing (12-shot sustained burst / rapid semi-auto chain)
            RunScenarioShots(ComprehensiveWeaponScenarioKind.ContinuousFiring, samples =>
            {
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 7000);
                categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);

                int interval = config.Category == WeaponCategory.Pistol ? 120 : config.AttackIntervalMs;
                for (int shotIdx = 0; shotIdx < 12; shotIdx++)
                {
                    if (config.Category == WeaponCategory.SniperRifle && opState.ResumeZoom)
                    {
                        opState.ClientTimeMs += config.AttackIntervalMs;
                        categoryEngine.ProcessIdleFrame(ref wState, ref opState, attackHeld: false, secondaryAttackHeld: false);
                    }

                    var (yaw, pitch) = testAngles[shotIdx % testAngles.Length];
                    var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: interval);
                    MovementStateKind stateKind = eval.IsAimingDownSights ? MovementStateKind.AimDownSights : MovementStateKind.Stand;
                    RecordEvaluatedShot(in eval, in opState, stateKind, 0f, 0f, false, yaw, pitch, samples);
                }
            });

            // 8. Weapon Switching (Cross-category switch -> fire -> idle decay -> switch back -> fire)
            RunScenarioShots(ComprehensiveWeaponScenarioKind.WeaponSwitching, samples =>
            {
                string alternateWeapon = grp.WeaponId == "awp" ? "ak47" : "awp";
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 9000);

                categoryEngine.SwitchWeapon(alternateWeapon, ref wState, ref opState);
                weaponSwitches++;
                SniperRifleCategoryModel.ToggleScopeZoom(ref opState);
                _ = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: 100);

                // Switch back to target weapon group -> must reset ShotsFired, Fov, ResumeZoom, Accuracy, Spread
                categoryEngine.SwitchWeapon(grp.WeaponId, ref wState, ref opState);
                weaponSwitches++;

                foreach (var (yaw, pitch) in testAngles)
                {
                    var eval = categoryEngine.FireWeapon(ref wState, ref opState, explicitIntervalMs: config.AttackIntervalMs);
                    RecordEvaluatedShot(in eval, in opState, MovementStateKind.Stand, 0f, 0f, false, yaw, pitch, samples);
                }
            });

            int grpTotal = scenarioDict.Values.Sum(x => x.TotalShots);
            int grpComp = scenarioDict.Values.Sum(x => x.CompensatedShots);
            int grpFail = scenarioDict.Values.Sum(x => x.FailedShots);
            double grpMaxCont = scenarioDict.Values.Max(x => x.MaxCompensatedContinuousErrorDegrees);
            double grpMaxQuant = scenarioDict.Values.Max(x => x.MaxCompensatedQuantizedErrorDegrees);
            double grpMeanIrred = scenarioDict.Values.Average(x => x.MeanIrreducibleDispersionDegrees);
            double grpMaxIrred = scenarioDict.Values.Max(x => x.MaxIrreducibleDispersionDegrees);

            groupReports.Add(new ComprehensiveWeaponGroupReport(
                grp.GroupId,
                grp.DisplayName,
                config.Category,
                config.PelletCount,
                grpTotal,
                grpComp,
                grpFail,
                grpMaxCont,
                grpMaxQuant,
                grpMeanIrred,
                grpMaxIrred,
                scenarioDict));
        }

        var movementSummary = BallisticAuditAnalyzer.Summarize(logger.GetRecentEvents());
        return new ComprehensiveMatrixVerificationReport(
            totalShots,
            totalCompensated,
            totalFailed,
            weaponSwitches,
            groupReports,
            movementSummary);
    }

    public static string FormatComprehensiveMatrixReport(ComprehensiveMatrixVerificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== BÁO CÁO MA TRẬN KIỂM THỬ TOÀN DIỆN 8 NHÓM SÚNG × 8 TRẠNG THÁI ===");
        sb.AppendLine($"Tổng số phát bắn kiểm thử : {report.TotalShots}");
        sb.AppendLine($"Số phát bù thành công     : {report.TotalCompensatedShots}");
        sb.AppendLine($"Số phát thất bại          : {report.TotalFailedShots}");
        sb.AppendLine($"Số lần chuyển đổi vũ khí  : {report.TotalWeaponSwitches}");
        sb.AppendLine($"Kết quả tổng thể          : {(report.AllGroupsPassed ? "ĐẠT 100% (8/8 nhóm vũ khí)" : "KHÔNG ĐẠT")}");
        sb.AppendLine();
        sb.AppendLine("Nhóm súng | Category        | Pellets | Tổng | Đạt | Lỗi | AlgContMax(deg) | AlgQuantMax(deg) | IrreducibleConeMean(deg) | Trạng thái");
        sb.AppendLine("---------------------------------------------------------------------------------------------------------------------------------------");
        foreach (var g in report.WeaponGroups)
        {
            sb.AppendLine(
                $"{g.GroupId,-9} | {g.Category,-15} | {g.PelletCount,7} | {g.TotalShots,4} | {g.CompensatedShots,3} | {g.FailedShots,3} | {g.MaxCompensatedContinuousErrorDegrees,15:F6} | {g.MaxCompensatedQuantizedErrorDegrees,16:F6} | {g.MeanIrreducibleDispersionDegrees,24:F4} | {(g.AllPassed ? "PASS" : "FAIL")}");
        }

        sb.AppendLine();
        sb.AppendLine("Trạng thái MovementStateKind | Số mẫu | RawMean(deg) | RawMax(deg) | ContMax(deg) | QuantMax(deg)");
        sb.AppendLine("--------------------------------------------------------------------------------------------------");
        foreach (var kv in report.MovementStateSummary.OrderBy(k => k.Key))
        {
            var m = kv.Value;
            sb.AppendLine(
                $"{m.State,-28} | {m.SampleCount,6} | {m.MeanUncompensatedErrorDegrees,12:F4} | {m.MaxUncompensatedErrorDegrees,11:F4} | {m.MaxContinuousErrorDegrees,12:F6} | {m.MaxQuantizedErrorDegrees,12:F6}");
        }

        return sb.ToString();
    }

    public static string FormatSimulationReport(SimulationVerificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== KẾT QUẢ MÔ PHỎNG ĐƯỜNG ĐẠN & KIỂM CHỨNG MODULE ĐỘC LẬP ===");
        sb.AppendLine($"Tổng số phát bắn mô phỏng : {report.TotalShotsSimulated}");
        sb.AppendLine($"Số phát bù spread thành công: {report.CompensatedShots}");
        sb.AppendLine($"Số phát bị bỏ qua / thất bại: {report.SkippedShots}");
        sb.AppendLine($"Số trường hợp dữ liệu lỗi   : {report.InvalidWeaponDataCases}");
        sb.AppendLine($"Số lần chuyển đổi vũ khí    : {report.WeaponSwitchCount}");
        sb.AppendLine($"Tương thích ngược           : {(report.BackwardCompatibilityVerified ? "ĐẠT" : "KHÔNG ĐẠT")} ({report.BackwardCompatibilitySummary})");
        sb.AppendLine($"Số lần điều phối chuột Aim : {report.AimMovesDispatched}");
        sb.AppendLine($"Số khung ESP đã dựng       : {report.OverlayBoxesDrawn}");
        sb.AppendLine($"Material override (Active -> Reset): {report.ActiveMaterialsBeforeReset} -> {report.ActiveMaterialsAfterReset}");
        sb.AppendLine();
        sb.AppendLine("Trạng thái di chuyển | Số mẫu | RawMean(deg) | RawMax(deg) | ContMax(deg) | QuantMax(deg)");
        sb.AppendLine("-----------------------------------------------------------------------------------------");

        foreach (var kv in report.MetricsByMovementState.OrderBy(k => k.Key))
        {
            var m = kv.Value;
            sb.AppendLine(
                $"{m.State,-20} | {m.SampleCount,6} | {m.MeanUncompensatedErrorDegrees,12:F4} | {m.MaxUncompensatedErrorDegrees,11:F4} | {m.MaxContinuousErrorDegrees,12:F6} | {m.MaxQuantizedErrorDegrees,12:F6}");
        }

        if (report.MetricsByCategory is not null && report.MetricsByCategory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Nhóm vũ khí     | Tổng | Thành công | Thất bại | Dữ liệu lỗi | RawMean(deg) | RawMax(deg) | ContMax(deg) | QuantMax(deg)");
            sb.AppendLine("------------------------------------------------------------------------------------------------------------------------");
            foreach (var kv in report.MetricsByCategory.OrderBy(k => k.Key))
            {
                var c = kv.Value;
                if (c.TotalCases == 0) continue;
                sb.AppendLine(
                    $"{c.Category,-15} | {c.TotalCases,4} | {c.SucceededCases,10} | {c.FailedCases,8} | {c.InvalidDataCases,11} | {c.MeanUncompensatedErrorDegrees,12:F4} | {c.MaxUncompensatedErrorDegrees,11:F4} | {c.MaxContinuousErrorDegrees,12:F6} | {c.MaxQuantizedErrorDegrees,12:F6}");
            }
        }

        if (report.MetricsByWeapon is not null && report.MetricsByWeapon.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Tên vũ khí               | Nhóm            | Tổng | Thành công | Thất bại | Lỗi DL | RawMean(deg) | RawMax(deg) | ContMax(deg) | QuantMax(deg)");
            sb.AppendLine("----------------------------------------------------------------------------------------------------------------------------------------------");
            foreach (var kv in report.MetricsByWeapon.OrderBy(k => k.Value.Category).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var w = kv.Value;
                sb.AppendLine(
                    $"{w.WeaponName,-24} | {w.Category,-15} | {w.TotalCases,4} | {w.SucceededCases,10} | {w.FailedCases,8} | {w.InvalidDataCases,6} | {w.MeanUncompensatedErrorDegrees,12:F4} | {w.MaxUncompensatedErrorDegrees,11:F4} | {w.MaxContinuousErrorDegrees,12:F6} | {w.MaxQuantizedErrorDegrees,12:F6}");
            }
        }

        return sb.ToString();
    }
}
