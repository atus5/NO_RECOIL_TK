using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;
using HackTK.Next.Core;
using HackTK.Next.Diagnostics;
using HackTK.Next.Modules;
using HackTK.Next.Runtime;
using HackTK.Next.Services;
using HackTK.Next.Simulation;
using HackTK.Next.UI;

namespace HackTK.Next.Tests;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" HACKTK.NEXT — BỘ KIỂM THỬ PHÂN TẦNG: UNIT / INTEGRATION / RUNTIME / END-TO-END");
        Console.WriteLine("================================================================================");

        // TIER 1: UNIT TESTS
        RunTest("1. [UNIT TEST] DI Container: Singleton, Transient, Circular Detection, Thread-Safety & Reverse Dispose (TestServiceContainer)", TestServiceContainer);
        RunTest("2. [UNIT TEST] Configuration & XML Store: Validation, Legacy Migration, XXE/Size Guard & Lock Retry (TestConfigurationAndXmlStore)", TestConfigurationAndXmlStore);
        RunTest("3. [UNIT TEST] Session Lease Manager: Token Guard, Monotonic Revision & Deterministic Expiry (TestSessionLeaseManager)", TestSessionLeaseManager);
        RunTest("4. [UNIT TEST] Ballistics Solver: Newton 2D Inverse, Yaw Wrap +/-180, Steep Pitch & 5x5 Int16 Quantize (TestNewtonSpreadSolver)", TestNewtonSpreadSolver);
        RunTest("5. [UNIT TEST] Coordinate & Dual-Origin Visibility: Broad Collider Filter, Wall Occlusion & Torso Reject (TestCoordinateAndVisibility)", TestCoordinateAndVisibility);
        RunTest("6. [UNIT TEST] Subpixel Aim Trajectory: Remainder Accumulation, FOV Scaling & Sign Flip Reset (TestSubpixelAimTrajectory)", TestSubpixelAimTrajectory);
        RunTest("7. [UNIT TEST] Resource Ownership: Transactional Value Rebase & Partial Material Slot Restoration (TestResourceOwnership)", TestResourceOwnership);
        RunTest("11. [UNIT TEST] Multi-Category Weapon Accuracy & Dispersion Formulas (AR, Sniper, LMG, SMG, Pistol, Shotgun, Custom) (TestMultiCategoryWeaponAccuracySimulation)", TestMultiCategoryWeaponAccuracySimulation);

        // TIER 2: INTEGRATION & SIMULATION TESTS
        RunTest("8. [INTEGRATION TEST] Module Manager & EngineHost: Fault Isolation, Focus Loss Reset & Orientation Restoration (TestModuleManagerAndEngineHost)", TestModuleManagerAndEngineHost);
        RunTest("9. [INTEGRATION/SIMULATION TEST] Full 6-State Offline Simulation Suite (Stand, Walk, Run, Strafe, MoveTurn, JumpAir) (TestFullSimulationSuite)", TestFullSimulationSuite);
        RunTest("10. [INTEGRATION TEST] WinForms UI Presenter & Dashboard Smoke Verification (TestUiSmokeVerification)", TestUiSmokeVerification);
        RunTest("12. [INTEGRATION/SIMULATION TEST] WeaponRegistry, Multi-Weapon Scenario Runner, Invalid Data Guard & Per-Category Audit (TestWeaponRegistryAndMultiWeaponVerificationSuite)", TestWeaponRegistryAndMultiWeaponVerificationSuite);
        RunTest("13. [INTEGRATION/SIMULATION TEST] Comprehensive 8-Weapon-Group × 8-Operational-State Matrix & Shotgun Multi-Pellet Audit (TestComprehensiveEightGroupEightStateMatrix)", TestComprehensiveEightGroupEightStateMatrix);
        RunTest("14. [INTEGRATION TEST] EngineModuleLoader & Full DI Graph Assembly Verification (TestEngineModuleLoaderAndFullDiGraph)", TestEngineModuleLoaderAndFullDiGraph);
        RunTest("17. [SNIPER & AR REGRESSION TEST] Dedicated AWP / Barrett / Scout Recoil, Scope/FOV, Bolt Recovery, Weapon Switch & AK47/M4A1 Regression (TestSniperModelsAndAkM4RegressionVerification)", TestSniperModelsAndAkM4RegressionVerification);

        // TIER 3: RUNTIME TESTS
        RunTest("15. [RUNTIME TEST] AuthorizedTestRuntimeHost, UnityRuntimeBridgeHost & Apply ACK Enforcement (TestAuthorizedRuntimeHostAndApplyAckEnforcement)", TestAuthorizedRuntimeHostAndApplyAckEnforcement);

        // TIER 4: END-TO-END (E2E) TESTS
        RunTest("16. [END-TO-END TEST] Full 12-Step Desktop UI <-> External HackTK.Next.Runtime.exe Process Verification (TestFullTwelveStepEndToEndDesktopToRuntime)", TestFullTwelveStepEndToEndDesktopToRuntime);

        Console.WriteLine("================================================================================");
        Console.WriteLine($" HOÀN TẤT: {_passed}/17 nhóm kiểm thử (Unit + Integration + Sniper/AR Regression + Runtime + End-to-End đều ĐẠT).");
        Console.WriteLine("================================================================================");
        return 0;
    }

    private static void RunTest(string name, Action body)
    {
        body();
        _passed++;
        Console.WriteLine($"[PASS] {name}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Assertion failed: " + message);
    }

    private static void TestServiceContainer()
    {
        var disposeOrder = new List<string>();
        using (var container = new ServiceContainer())
        {
            int singletonConstructCount = 0;
            container.RegisterSingleton<DisposableProbe>(_ =>
            {
                Interlocked.Increment(ref singletonConstructCount);
                Thread.Sleep(5);
                return new DisposableProbe("First", disposeOrder);
            });
            container.RegisterSingleton<SecondDisposableProbe>(r =>
            {
                _ = r.Resolve<DisposableProbe>();
                return new SecondDisposableProbe("Second", disposeOrder);
            });
            container.RegisterTransient<TransientProbe>(_ => new TransientProbe());

            // Concurrent thread-safety check on lazy singleton construction
            Parallel.For(0, 16, _ =>
            {
                var instance = container.Resolve<DisposableProbe>();
                Require(instance.Name == "First", "Singleton name mismatch.");
            });
            Require(singletonConstructCount == 1, $"Lazy singleton must be constructed once, actual={singletonConstructCount}.");

            var second = container.Resolve<SecondDisposableProbe>();
            Require(second.Name == "Second", "Second singleton resolution failed.");

            var t1 = container.Resolve<TransientProbe>();
            var t2 = container.Resolve<TransientProbe>();
            Require(!ReferenceEquals(t1, t2), "Transient services must return distinct instances.");

            // Circular dependency detection
            container.RegisterSingleton<CycleA>(r => new CycleA(r.Resolve<CycleB>()));
            container.RegisterSingleton<CycleB>(r => new CycleB(r.Resolve<CycleA>()));
            bool threwCycle = false;
            try
            {
                _ = container.Resolve<CycleA>();
            }
            catch (InvalidOperationException)
            {
                threwCycle = true;
            }
            Require(threwCycle, "Circular dependency must throw InvalidOperationException.");
        }

        Require(disposeOrder.Count == 2 && disposeOrder[0] == "Second" && disposeOrder[1] == "First",
            "Disposables must be disposed in reverse creation order.");
    }

    private static void TestConfigurationAndXmlStore()
    {
        var legacy = new EngineProfile { AimFov = 30f };
        Require(Math.Abs(legacy.AimRadiusPixels - 180f) < 0.01f, "Legacy AimFov=30 must map to 180px at 1080p.");

        var explicitRadius = new EngineProfile { AimFov = 30f, AimRadiusPixels = 14f };
        Require(Math.Abs(explicitRadius.AimRadiusPixels - 14f) < 0.01f, "Explicit AimRadiusPixels must take precedence.");

        var invalid = new EngineProfile { Smooth = 99f, BoxHue = -1f };
        Require(invalid.Validate().Count >= 2, "Out-of-range profile properties must be flagged.");

        string tempDir = Path.Combine(Path.GetTempPath(), "HackTK.Next.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new AtomicXmlConfigurationStore<EngineProfile>();
            string xmlPath = Path.Combine(tempDir, "profile.xml");

            var profile = new EngineProfile
            {
                AimEnabled = true,
                AimRadiusPixels = 22.5f,
                CompensateCommandSpread = true,
                Target = TargetPart.Chest,
                ChamsMode = ShaderRenderMode.Glow
            };
            store.Save(xmlPath, profile);
            var loaded = store.Load(xmlPath);
            Require(profile.SameEffectiveValues(loaded), "XML save/load roundtrip must preserve all profile values.");

            // XXE / DTD rejection
            string dtdPath = Path.Combine(tempDir, "dtd.xml");
            File.WriteAllText(dtdPath, "<?xml version=\"1.0\"?><!DOCTYPE Settings [<!ENTITY xxe \"boom\">]><Settings />");
            bool rejectedDtd = false;
            try { _ = store.Load(dtdPath); } catch { rejectedDtd = true; }
            Require(rejectedDtd, "XML store must reject DTD/XXE payloads.");

            // Oversized XML rejection (>64 KiB)
            string hugePath = Path.Combine(tempDir, "huge.xml");
            File.WriteAllText(hugePath, new string('A', 70 * 1024));
            bool rejectedHuge = false;
            try { _ = store.Load(hugePath); } catch (InvalidDataException) { rejectedHuge = true; }
            Require(rejectedHuge, "XML store must reject files larger than 64 KiB.");

            // Concurrent read lock recovery
            using (var sharedReader = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var concurrentLoaded = store.Load(xmlPath);
                Require(concurrentLoaded.AimEnabled, "Concurrent shared read must succeed.");
            }

            // Malformed XML throws InvalidDataException
            string malformedPath = Path.Combine(tempDir, "malformed.xml");
            File.WriteAllText(malformedPath, "<Settings><Smooth>not-a-float</Smooth>");
            bool rejectedMalformed = false;
            try { _ = store.Load(malformedPath); } catch (InvalidDataException) { rejectedMalformed = true; }
            Require(rejectedMalformed, "Malformed XML must throw InvalidDataException.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void TestSessionLeaseManager()
    {
        var time = new DeterministicTimeProvider();
        var lease = new SessionLeaseManager(time);
        const string token = "session-alpha";

        var env1 = new SessionCommandEnvelope
        {
            ProtocolVersion = NextWireProtocol.CurrentVersion,
            SessionToken = token,
            Revision = 1,
            Connected = true,
            ExpiresUtcTicks = time.UtcNow.AddSeconds(2).UtcTicks,
            Profile = new EngineProfile { AimEnabled = true, AimRadiusPixels = 16f }
        };

        Require(lease.TryApplyEnvelope(env1, token), "Valid envelope rev 1 must activate lease.");
        Require(lease.CurrentRevision == 1 && lease.CurrentProfile.AimEnabled, "Lease state must match rev 1.");

        // Token mismatch must disconnect
        var wrongToken = new SessionCommandEnvelope
        {
            ProtocolVersion = NextWireProtocol.CurrentVersion,
            SessionToken = "wrong-token",
            Revision = 2,
            Connected = true,
            ExpiresUtcTicks = time.UtcNow.AddSeconds(2).UtcTicks,
            Profile = new EngineProfile { AimEnabled = true }
        };
        Require(!lease.TryApplyEnvelope(wrongToken, token), "Mismatched session token must be rejected.");

        // Re-apply valid rev 2
        var env2 = new SessionCommandEnvelope
        {
            ProtocolVersion = NextWireProtocol.CurrentVersion,
            SessionToken = token,
            Revision = 2,
            Connected = true,
            ExpiresUtcTicks = time.UtcNow.AddSeconds(2).UtcTicks,
            Profile = new EngineProfile { AimEnabled = true, AimRadiusPixels = 24f }
        };
        Require(lease.TryApplyEnvelope(env2, token), "Valid envelope rev 2 must activate lease.");

        // Rollback revision (rev 1 < rev 2) must be rejected
        Require(!lease.TryApplyEnvelope(env1, token), "Revision rollback must be rejected.");

        // Advance deterministic clock past expiration without Thread.Sleep
        time.AdvanceMilliseconds(2500);
        Require(!lease.TryApplyEnvelope(env2, token), "Expired lease must be rejected deterministically.");
    }

    private static void TestNewtonSpreadSolver()
    {
        var model = new TriangularSpreadDirectionModel();
        var solver = new NewtonSpreadSolverService();

        double[] yaws = { -179.95, -135.0, -45.0, 0.0, 45.0, 135.0, 179.95 };
        double[] pitches = { -75.0, -35.0, 0.0, 35.0, 75.0 };
        double[] spreads = { 0.004, 0.025, 0.065, 0.150 };
        int[] seeds = { 13918, 13932, 13960, 14002 };

        foreach (int seed in seeds)
        foreach (double spread in spreads)
        foreach (double yaw in yaws)
        foreach (double pitch in pitches)
        {
            Vector3D targetDir = model.CalculateDirection(seed, yaw, pitch, 0.0, 1.0f, 1.0);
            Func<double, double, Vector3D> eval = (y, p) => model.CalculateDirection(seed, y, p, spread, 0.4f, 1.25);

            bool solved = solver.TrySolveInverse(eval, targetDir, yaw, pitch, out var sol);
            Require(solved && sol.AngularErrorDegrees < 1e-4, $"Newton solver failed at yaw={yaw}, pitch={pitch}, spread={spread}.");

            bool quantizedOk = solver.TryQuantizeCommandAngles(eval, targetDir, sol.YawDegrees, sol.PitchDegrees, 0.35, -0.85, out var q);
            Require(quantizedOk && q.QuantizedErrorDegrees <= 0.01, $"Quantization error exceeded 0.01 deg: {q.QuantizedErrorDegrees:F6}");
        }
    }

    private static void TestCoordinateAndVisibility()
    {
        var transformer = new SsjjCoordinateTransformer();
        var unityPoint = new Vector3D(3.25, 1.75, -12.5);
        var ssjjPoint = transformer.UnityToSsjj(unityPoint);
        var roundtrip = transformer.SsjjToUnity(ssjjPoint);
        Require(unityPoint.Subtract(roundtrip).Length < 1e-9, "Unity <-> SSJJ coordinate transformation must be exact.");

        var raycaster = new SimulatedPhysicsRaycaster();
        var visibility = new DualOriginVisibilityService(raycaster, transformer);

        var local = new SimulatedActorSnapshot
        {
            EntityId = 1,
            TeamId = 1,
            IsLocal = true,
            IsAlive = true,
            EyePositionUnity = new Vector3D(0, 1.7, 0),
            CompensatedShotOriginSsjj = transformer.UnityToSsjj(new Vector3D(0, 1.7, 0))
        };

        var headUnity = new Vector3D(0, 1.7, 15.0);
        var target = new SimulatedActorSnapshot
        {
            EntityId = 2,
            TeamId = 2,
            IsAlive = true
        };
        target.BonesUnity["Bip01_Head"] = headUnity;

        var world = new SimulatedWorldState { LocalActor = local };
        world.ActorList.Add(local);
        world.ActorList.Add(target);

        // Broad body collider ('dummy') in front of head + actual head sphere
        raycaster.Colliders.Add(new SimulatedSphereCollider(2, "dummy", transformer.UnityToSsjj(new Vector3D(0, 1.7, 14.2)), 0.6, IsBroadBodyCollider: true));
        raycaster.Colliders.Add(new SimulatedSphereCollider(2, "Bip01_Head", transformer.UnityToSsjj(headUnity), 0.2, IsBroadBodyCollider: false));

        Require(visibility.IsActorVisible(world, local, target, headUnity, headUnity, out _),
            "Broad body collider must not self-occlude target head.");

        // Add world wall behind broad collider but in front of head -> must block visibility
        raycaster.Colliders.Add(new SimulatedSphereCollider(0, "Wall_Concrete", transformer.UnityToSsjj(new Vector3D(0, 1.7, 10.0)), 1.0, IsBroadBodyCollider: false));
        Require(!visibility.IsActorVisible(world, local, target, headUnity, headUnity, out string wallReason) &&
                wallReason.Contains("camera-head") && wallReason.Contains("Wall_Concrete"),
            "World wall must block visibility even when broad collider is ignored.");

        // Remove wall, replace head collider with torso collider -> must reject non-head hit
        raycaster.Colliders.Clear();
        raycaster.Colliders.Add(new SimulatedSphereCollider(2, "Bip01_Spine1", transformer.UnityToSsjj(headUnity), 0.25, IsBroadBodyCollider: false));
        Require(!visibility.IsActorVisible(world, local, target, headUnity, headUnity, out string spineReason) &&
                spineReason.Contains("camera-head") && spineReason.Contains("Bip01_Spine1"),
            "Torso collider hit must be rejected when verifying head visibility.");
    }

    private static void TestSubpixelAimTrajectory()
    {
        var aim = new SubpixelAimComputationService();
        Require(Math.Abs(aim.ComputeFovRadiusPixels(30f, 1080) - 180f) < 0.01f, "30 deg FOV at 1080p must equal 180px.");
        Require(Math.Abs(aim.ComputeScaledRadiusPixels(10f, 720) - 6.6666f) < 0.01f, "720p radius scaling mismatch.");

        // 0.4px step per frame with smooth=5: Frame 1 -> 0px (rem 0.4), Frame 2 -> 0px (rem 0.8), Frame 3 -> 1px (rem 0.2)
        var step1 = aim.ComputeSubpixelStep(targetCoord: 962f, centerCoord: 960f, punchAngle: 0f, smoothFactor: 5f, currentRemainder: 0f);
        var step2 = aim.ComputeSubpixelStep(targetCoord: 962f, centerCoord: 960f, punchAngle: 0f, smoothFactor: 5f, currentRemainder: step1.NextRemainder);
        var step3 = aim.ComputeSubpixelStep(targetCoord: 962f, centerCoord: 960f, punchAngle: 0f, smoothFactor: 5f, currentRemainder: step2.NextRemainder);
        Require(step1.WholePixels == 0 && step2.WholePixels == 0 && step3.WholePixels == 1 && Math.Abs(step3.NextRemainder - 0.2f) < 1e-4f,
            "Subpixel remainder must accumulate across frames.");

        // Sign flip resets remainder
        var flipped = aim.ComputeSubpixelStep(targetCoord: 955f, centerCoord: 960f, punchAngle: 0f, smoothFactor: 5f, currentRemainder: 0.45f);
        Require(flipped.WholePixels == -1 && Math.Abs(flipped.NextRemainder) < 1e-4f, "Direction reversal must clear stale opposite remainder.");
    }

    private static void TestResourceOwnership()
    {
        // TransactionalValueOwnership rebase test
        var holder = new IntSettingHolder { Value = 1 };
        var ownership = new TransactionalValueOwnership<IntSettingHolder, int>(
            h => h.Value,
            (h, v) => h.Value = v);

        ownership.Apply(holder, 0);
        Require(holder.Value == 0 && ownership.HasActiveOwner, "Desired value 0 must be applied.");

        // External config reload changes backing value to 2 while owned -> must rebase baseline to 2 and re-apply 0
        holder.Value = 2;
        ownership.Apply(holder, 0);
        Require(holder.Value == 0 && ownership.OriginalValue == 2, "Desired value 0 must remain enforced after rebase.");

        ownership.Restore();
        Require(holder.Value == 2 && !ownership.HasActiveOwner, "Restore must return to rebased external value 2.");

        // MaterialSlotDiffService partial slot capture test
        string?[] previousOriginals = { "Original_Mat0", null, "Original_Mat2" };
        string?[] currentSlots = { "Override_Glow", "NewDynamic_Mat1", "Override_Glow" };
        var captured = MaterialSlotDiffService.CaptureOriginalSlots(currentSlots, previousOriginals, "Override_Glow");
        Require(captured[0] == "Original_Mat0" && captured[1] == "NewDynamic_Mat1" && captured[2] == "Original_Mat2",
            "CaptureOriginalSlots must preserve originals for owned slots and capture new external materials.");
    }

    private static void TestModuleManagerAndEngineHost()
    {
        var time = new DeterministicTimeProvider();
        var logger = new StructuredDiagnosticLogger(time);
        var mgr = new ModuleManager(logger);

        var faulty = new FaultInjectingModule();
        var spread = new SpreadCompensationModule(new NewtonSpreadSolverService(), new TriangularSpreadDirectionModel(), logger);
        mgr.Register(faulty);
        mgr.Register(spread);

        using var host = new EngineHost(new SessionLeaseManager(time), mgr, logger, "host-token");
        host.Initialize();

        var localActor = new SimulatedActorSnapshot
        {
            EntityId = 1,
            IsLocal = true,
            IsAlive = true,
            IsOnGround = false,
            HorizontalVelocity = 240.0,
            OrientationYaw = 33.25f,
            OrientationPitch = -11.5f,
            CurrentWeapon = new SimulatedWeaponSnapshot { Spread = 0.12, SpreadScaleY = 0.4f, ShotsFiredMultiplier = 1.1 }
        };

        var world = new SimulatedWorldState
        {
            IsApplicationFocused = true,
            LocalActor = localActor,
            CurrentCommand = new SimulatedUserCommandFrame
            {
                SequenceNumber = 13960,
                CameraYaw16 = 3325,
                CameraPitch16 = -1150,
                IsAttackOn = true
            }
        };

        var env = new SessionCommandEnvelope
        {
            ProtocolVersion = NextWireProtocol.CurrentVersion,
            SessionToken = host.SessionToken,
            Revision = 1,
            Connected = true,
            ExpiresUtcTicks = time.UtcNow.AddSeconds(5).UtcTicks,
            Profile = new EngineProfile { CompensateCommandSpread = true, RecordDiagnostics = true }
        };

        // Frame 1: faulty module throws during ExecuteTick, but SpreadCompensationModule still executes and restores actor orientation!
        host.ProcessFrame(env, world);
        Require(faulty.State == ModuleLifecycleState.Faulted && faulty.TickCallCount == 1 && faulty.ResetCallCount >= 1,
            "Faulting module must transition to Faulted and invoke Reset immediately.");
        Require(spread.CompensatedCount == 1 && world.CurrentCommand.WasCompensated,
            "Healthy module must continue executing despite sibling module fault.");
        Require(Math.Abs(localActor.OrientationYaw - 33.25f) < 1e-5f && Math.Abs(localActor.OrientationPitch - (-11.5f)) < 1e-5f,
            "LocalActor OrientationYaw/Pitch must be restored after spread compensation.");

        // Frame 2 in same revision: faulty module must remain isolated (TickCallCount stays 1)
        world.CurrentCommand = new SimulatedUserCommandFrame
        {
            SequenceNumber = 13961,
            CameraYaw16 = 3325,
            CameraPitch16 = -1150,
            IsAttackOn = true
        };
        host.ProcessFrame(env, world);
        Require(faulty.TickCallCount == 1 && spread.CompensatedCount == 2,
            "Faulted module must be skipped in subsequent frames of the same revision while healthy modules continue.");

        // Unfocus window -> EngineHost must automatically reset modules
        world.IsApplicationFocused = false;
        host.ProcessFrame(env, world);
        Require(!host.IsRunning, "EngineHost must pause when application loses focus.");
    }

    private static void TestFullSimulationSuite()
    {
        var report = SimulatedWorldScenarioRunner.RunFullVerificationSuite();
        Require(report.TotalShotsSimulated == 378, $"Expected 378 simulated shots, actual={report.TotalShotsSimulated}.");
        Require(report.CompensatedShots == 378 && report.SkippedShots == 0,
            $"All 378 shots must be compensated (compensated={report.CompensatedShots}, skipped={report.SkippedShots}).");
        Require(report.AimMovesDispatched > 0, "TargetAcquisitionModule must dispatch aim movements in simulation.");
        Require(report.OverlayBoxesDrawn > 0, "EspProjectionModule must draw bounding boxes in simulation.");
        Require(report.ActiveMaterialsBeforeReset == 3 && report.ActiveMaterialsAfterReset == 0,
            "MaterialSlotModule must apply 3 slot groups and restore to 0 on reset.");

        MovementStateKind[] expectedStates =
        {
            MovementStateKind.Stand,
            MovementStateKind.Walk,
            MovementStateKind.Run,
            MovementStateKind.Strafe,
            MovementStateKind.MoveTurn,
            MovementStateKind.JumpAir
        };

        foreach (var state in expectedStates)
        {
            Require(report.MetricsByMovementState.TryGetValue(state, out var m) && m is not null, $"Missing metrics for state {state}.");
            Require(m!.SampleCount > 0, $"Zero samples for state {state}.");
            Require(m.MaxQuantizedErrorDegrees <= 0.01, $"State {state} exceeded 0.01 deg quantized tolerance: {m.MaxQuantizedErrorDegrees:F6}");
        }

        Console.WriteLine();
        Console.Write(SimulatedWorldScenarioRunner.FormatSimulationReport(report));
    }

    private sealed class IntSettingHolder
    {
        public int Value { get; set; }
    }

    private static void TestUiSmokeVerification()
    {
        Require(
            EngineModuleLoader.VerifyAndLoadAssemblies(out var loadedAssemblies, out var failedAssemblies),
            "Desktop UI must load all required assemblies: " + string.Join(", ", failedAssemblies));
        var uiDir = Path.GetDirectoryName(typeof(EngineDashboardForm).Assembly.Location) ?? AppContext.BaseDirectory;
        foreach (string requiredDll in new[]
        {
            "HackTK.Next.Abstractions.dll",
            "HackTK.Next.Configuration.dll",
            "HackTK.Next.Diagnostics.dll",
            "HackTK.Next.Services.dll",
            "HackTK.Next.Core.dll",
            "HackTK.Next.Modules.dll",
            "HackTK.Next.Simulation.dll",
            "HackTK.Next.Runtime.dll"
        })
        {
            Require(File.Exists(Path.Combine(uiDir, requiredDll)),
                $"Desktop UI output directory is missing required dependency '{requiredDll}'.");
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "HackTK.Next.UI.Smoke." + Guid.NewGuid().ToString("N"));
        try
        {
            bool ok = EngineDashboardForm.RunSmokeVerification(tempDir, out string details);
            Require(ok, details);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void TestMultiCategoryWeaponAccuracySimulation()
    {
        var engine = new WeaponCategorySimulationEngine();
        var solver = new NewtonSpreadSolverService();
        var dirModel = new TriangularSpreadDirectionModel();
        var time = new DeterministicTimeProvider();
        var logger = new StructuredDiagnosticLogger(time);
        var spreadModule = new SpreadCompensationModule(solver, dirModel, logger);
        spreadModule.Initialize();
        spreadModule.Configure(new EngineProfile { CompensateCommandSpread = true, RecordDiagnostics = true });

        // 1. Verify classification of all 7 categories (AR, Sniper, LMG, SMG, Pistol, Shotgun, Custom)
        Require(WeaponCategorySimulationEngine.ClassifyWeapon("m4a1") == WeaponCategory.AssaultRifle &&
                WeaponCategorySimulationEngine.ClassifyWeapon("ak47") == WeaponCategory.AssaultRifle,
                "AR classification failed.");
        Require(WeaponCategorySimulationEngine.ClassifyWeapon("awp") == WeaponCategory.SniperRifle &&
                WeaponCategorySimulationEngine.ClassifyWeapon("barrett") == WeaponCategory.SniperRifle,
                "Sniper classification failed.");
        Require(WeaponCategorySimulationEngine.ClassifyWeapon("m249") == WeaponCategory.LightMachineGun,
                "LMG classification failed.");
        Require(WeaponCategorySimulationEngine.ClassifyWeapon("mp5") == WeaponCategory.SubmachineGun &&
                WeaponCategorySimulationEngine.ClassifyWeapon("p90") == WeaponCategory.SubmachineGun,
                "SMG classification failed.");
        Require(WeaponCategorySimulationEngine.ClassifyWeapon("deagle") == WeaponCategory.Pistol &&
                WeaponCategorySimulationEngine.ClassifyWeapon("usp45") == WeaponCategory.Pistol,
                "Pistol classification failed.");
        Require(WeaponCategorySimulationEngine.ClassifyWeapon("xm1014") == WeaponCategory.Shotgun &&
                WeaponCategorySimulationEngine.ClassifyWeapon("m3") == WeaponCategory.Shotgun,
                "Shotgun classification failed.");
        Require(WeaponCategorySimulationEngine.ClassifyWeapon("custompulse") == WeaponCategory.Custom &&
                WeaponCategorySimulationEngine.ClassifyWeapon("Custom:RailGun") == WeaponCategory.Custom,
                "Custom weapon classification failed.");

        void VerifyCompensationForEvaluation(WeaponShotEvaluation eval)
        {
            var weaponSnap = new SimulatedWeaponSnapshot();
            WeaponCategorySimulationEngine.ApplyEvaluationToSnapshot(in eval, weaponSnap);
            var localActor = new SimulatedActorSnapshot
            {
                EntityId = 1,
                IsLocal = true,
                IsAlive = true,
                IsOnGround = eval.Posture != OperatorPostureKind.Jumping,
                IsCrouching = eval.Posture == OperatorPostureKind.Crouching,
                IsAimingDownSights = eval.IsAimingDownSights || eval.IsScoped,
                Fov = eval.FovBeforeShot,
                OrientationYaw = 25.0f,
                OrientationPitch = -10.0f,
                PunchYaw = eval.PunchYaw,
                PunchPitch = eval.PunchPitch,
                CurrentWeapon = weaponSnap
            };
            var cmd = new SimulatedUserCommandFrame
            {
                SequenceNumber = 13960,
                CameraYaw16 = solver.EncodeAngle16(25.0),
                CameraPitch16 = solver.EncodeAngle16(-10.0),
                IsCrouchOn = eval.Posture == OperatorPostureKind.Crouching,
                IsAimDownSightsOn = eval.IsAimingDownSights || eval.IsScoped,
                IsAttackOn = true
            };
            var world = new SimulatedWorldState
            {
                IsApplicationFocused = true,
                LocalActor = localActor,
                CurrentCommand = cmd
            };
            spreadModule.ExecuteTick(world);

            int seed = cmd.SequenceNumber;
            double punchYawDeg = eval.PunchYaw * 2.0;
            double punchPitchDeg = eval.PunchPitch * 2.0;
            Vector3D targetDir = dirModel.CalculateDirection(seed, 25.0 + punchYawDeg, -10.0 + punchPitchDeg, 0.0, eval.SpreadScaleY, eval.ShotsFiredSpreadMultiplier);
            double compYaw = cmd.CameraYaw16 / 100.0 + punchYawDeg;
            double compPitch = cmd.CameraPitch16 / 100.0 + punchPitchDeg;
            Vector3D actualDir = dirModel.CalculateDirection(seed, compYaw, compPitch, eval.Spread, eval.SpreadScaleY, eval.ShotsFiredSpreadMultiplier);
            double quantErr = solver.ComputeAngularErrorDegrees(actualDir, targetDir);

            Require(cmd.WasCompensated && quantErr <= 0.01,
                $"SpreadCompensationModule failed for {eval.WeaponId} ({eval.Category}) in posture {eval.Posture}: quantErr={quantErr:F5}");
        }

        MovementStateKind[] coreMovementStates =
        {
            MovementStateKind.Stand,
            MovementStateKind.Walk,
            MovementStateKind.Run,
            MovementStateKind.JumpAir,
            MovementStateKind.Crouch,
            MovementStateKind.AimDownSights
        };

        // 2. Test all 6 posture/movement states across all 7 categories
        string[] representativeWeapons = { "m4a1", "ak47", "awp", "barrett", "scout", "m249", "mp5", "p90", "deagle", "usp45", "xm1014", "m3", "custompulse" };
        foreach (string weaponId in representativeWeapons)
        {
            var evalsByState = new Dictionary<MovementStateKind, WeaponShotEvaluation>();
            foreach (var moveState in coreMovementStates)
            {
                var wState = default(SimulatedWeaponAccuracyState);
                var opState = SimulatedOperatorPostureState.FromMovementState(moveState, 1000);
                engine.SwitchWeapon(weaponId, ref wState, ref opState);
                // Re-apply posture after SwitchWeapon (which resets Fov to 90)
                opState = SimulatedOperatorPostureState.FromMovementState(moveState, 1000);
                engine.SynchronizeState(ref wState, in opState);
                var eval = engine.FireWeapon(ref wState, ref opState);
                evalsByState[moveState] = eval;
                VerifyCompensationForEvaluation(eval);
            }

            // Verify monotone spread ordering: Crouch/ADS <= Stand < Walk < Run < JumpAir
            Require(evalsByState[MovementStateKind.Crouch].Spread <= evalsByState[MovementStateKind.Stand].Spread &&
                    evalsByState[MovementStateKind.AimDownSights].Spread <= evalsByState[MovementStateKind.Stand].Spread &&
                    evalsByState[MovementStateKind.Stand].Spread < evalsByState[MovementStateKind.Walk].Spread &&
                    evalsByState[MovementStateKind.Walk].Spread < evalsByState[MovementStateKind.Run].Spread &&
                    evalsByState[MovementStateKind.Run].Spread < evalsByState[MovementStateKind.JumpAir].Spread,
                    $"Spread ordering violated for weapon {weaponId}.");
        }

        // 3. Sniper specific: scoped (Fov=40, Fov=10) vs unscoped (Fov=90), CheckCloseSniperScope, and mid-air jumping penalty
        var awpUnscoped = default(SimulatedWeaponAccuracyState);
        var opUnscoped = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("awp", ref awpUnscoped, ref opUnscoped);
        var evalAwpUnscoped = engine.FireWeapon(ref awpUnscoped, ref opUnscoped);

        var awpScoped = default(SimulatedWeaponAccuracyState);
        var opScoped = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("awp", ref awpScoped, ref opScoped);
        SniperRifleCategoryModel.ToggleScopeZoom(engine.GetConfiguration("awp"), ref awpScoped, ref opScoped); // Fov 40
        SniperRifleCategoryModel.ToggleScopeZoom(engine.GetConfiguration("awp"), ref awpScoped, ref opScoped); // Fov 10
        Require(opScoped.Fov == 10 && awpScoped.Spread < 0.0012, "AWP double zoom must set Fov=10 and immediately update state Spread.");
        var evalAwpScoped = engine.FireWeapon(ref awpScoped, ref opScoped);
        Require(evalAwpScoped.Spread < 0.0015 && evalAwpUnscoped.Spread >= evalAwpScoped.Spread + 0.08,
            "AWP unscoped penalty must be >= 0.08 over scoped spread.");
        Require(opScoped.Fov == 90 && !opScoped.IsAimingDownSights && opScoped.ResumeZoom && awpScoped.DelayFire,
            "AWP CheckCloseSniperScope must reset Fov=90, clear IsAimingDownSights, and set ResumeZoom=true after firing.");

        // Sniper jumping while scoped must still incur AirSpreadParam (no stationary scope stability in mid-air)
        var awpJumpScoped = default(SimulatedWeaponAccuracyState);
        var opJumpScoped = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.JumpAir, 1000);
        engine.SwitchWeapon("awp", ref awpJumpScoped, ref opJumpScoped);
        opJumpScoped = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.JumpAir, 1000);
        SniperRifleCategoryModel.ToggleScopeZoom(engine.GetConfiguration("awp"), ref awpJumpScoped, ref opJumpScoped);
        var evalAwpJumpScoped = engine.FireWeapon(ref awpJumpScoped, ref opJumpScoped);
        Require(evalAwpJumpScoped.Spread >= 0.45 && evalAwpJumpScoped.Posture == OperatorPostureKind.Jumping,
            "AWP jumping while scoped must enforce AirSpreadParam.");
        VerifyCompensationForEvaluation(evalAwpJumpScoped);

        // 4. Shotgun specific: multi-pellet cluster evaluation (8 pellets for XM1014, 9 pellets for M3)
        var xm1014 = default(SimulatedWeaponAccuracyState);
        var opXm = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("xm1014", ref xm1014, ref opXm);
        var evalXm = engine.FireWeapon(ref xm1014, ref opXm, explicitIntervalMs: 240);
        var xmCluster = ShotgunCategoryModel.EvaluatePelletCluster(
            in evalXm,
            seed: 13960,
            rawYawDegrees: 30.0,
            rawPitchDegrees: -12.0,
            cancelRecoilPunch: true,
            dirModel,
            solver);
        Require(evalXm.PelletCount == 8 && xmCluster.CompensatedPelletDirections.Count == 8,
            "XM1014 must evaluate all 8 pellets.");
        Require(xmCluster.CompensatedPrimaryQuantizedErrorDegrees <= 0.01 &&
                xmCluster.IrreduciblePelletConeMeanDegrees > 0.50,
            "Shotgun evaluation must separate compensatable primary error (<= 0.01 deg) from irreducible multi-pellet cone spread (> 0.50 deg).");

        // 5. LMG specific: continuous 20-shot fire progression
        var m249 = default(SimulatedWeaponAccuracyState);
        var opM249 = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("m249", ref m249, ref opM249);
        WeaponShotEvaluation m249Shot1 = default;
        WeaponShotEvaluation m249Shot20 = default;
        for (int i = 1; i <= 20; i++)
        {
            var ev = engine.FireWeapon(ref m249, ref opM249, explicitIntervalMs: 85);
            if (i == 1) m249Shot1 = ev;
            if (i == 20) m249Shot20 = ev;
        }
        Require(m249Shot1.ShotsFiredSpreadMultiplier > 1.0 &&
                Math.Abs(m249Shot20.ShotsFiredSpreadMultiplier - 1.45) < 1e-6 &&
                m249Shot20.Spread > m249Shot1.Spread &&
                m249Shot20.PunchPitch > m249Shot1.PunchPitch,
                "LMG M249 continuous fire must progress ShotsFiredSpreadMultiplier to MaxFactor (1.45) and build recoil.");
        VerifyCompensationForEvaluation(m249Shot20);

        // 6. SMG specific: quadratic burst & lower moving spread than AR
        var mp5Run = default(SimulatedWeaponAccuracyState);
        var opMp5Run = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Run, 1000);
        engine.SwitchWeapon("mp5", ref mp5Run, ref opMp5Run);
        opMp5Run = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Run, 1000);
        var evalMp5Run = engine.FireWeapon(ref mp5Run, ref opMp5Run);

        var akRun = default(SimulatedWeaponAccuracyState);
        var opAkRun = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Run, 1000);
        engine.SwitchWeapon("ak47", ref akRun, ref opAkRun);
        opAkRun = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Run, 1000);
        var evalAkRun = engine.FireWeapon(ref akRun, ref opAkRun);
        Require(evalMp5Run.Spread < evalAkRun.Spread, "SMG MP5 running spread must be tighter than AK47 running spread.");

        // 7. Pistol specific: rapid-fire (dt=120ms) vs single-shot (dt=450ms) and first-shot rest guard
        var deagleRapid = default(SimulatedWeaponAccuracyState);
        var opDeagleRapid = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("deagle", ref deagleRapid, ref opDeagleRapid);
        WeaponShotEvaluation deagleShot1 = default;
        WeaponShotEvaluation deagleRapidEval = default;
        for (int i = 0; i < 6; i++)
        {
            var ev = engine.FireWeapon(ref deagleRapid, ref opDeagleRapid, explicitIntervalMs: 120);
            if (i == 0) deagleShot1 = ev;
            deagleRapidEval = ev;
        }
        Require(Math.Abs(deagleShot1.Accuracy - 0.90) < 1e-6, "First rested pistol shot must start at DefaultAccuracy (0.90).");

        var deagleSingle = default(SimulatedWeaponAccuracyState);
        var opDeagleSingle = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("deagle", ref deagleSingle, ref opDeagleSingle);
        WeaponShotEvaluation deagleSingleEval = default;
        for (int i = 0; i < 6; i++)
            deagleSingleEval = engine.FireWeapon(ref deagleSingle, ref opDeagleSingle, explicitIntervalMs: 450);

        Require(deagleRapidEval.Accuracy < deagleSingleEval.Accuracy &&
                deagleRapidEval.Spread > deagleSingleEval.Spread,
                "Pistol rapid-fire must decay Accuracy and increase Spread compared to paced single-shot fire.");
        VerifyCompensationForEvaluation(deagleRapidEval);
        VerifyCompensationForEvaluation(deagleSingleEval);

        // 8. Weapon switching & idle decay
        var switched = default(SimulatedWeaponAccuracyState);
        var opSwitched = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.AimDownSights, 5000);
        engine.SwitchWeapon("m4a1", ref switched, ref opSwitched);
        Require(switched.ShotsFired == 0 && opSwitched.Fov == 90 && !opSwitched.ResumeZoom && Math.Abs(switched.Accuracy - 0.2) < 1e-6,
            "Switching weapon must reset ShotsFired, Fov, ResumeZoom, and DefaultAccuracy.");
        for (int i = 1; i <= 3; i++)
            engine.FireWeapon(ref switched, ref opSwitched, explicitIntervalMs: 90);
        Require(switched.ShotsFired == 3 && switched.Accuracy > 0.20, "M4A1 must accumulate ShotsFired during burst.");
        for (int i = 1; i <= 6; i++)
        {
            opSwitched.ClientTimeMs += 200;
            engine.ProcessIdleFrame(ref switched, ref opSwitched, attackHeld: false, secondaryAttackHeld: false);
        }
        Require(switched.ShotsFired == 0 && Math.Abs(switched.Accuracy - 0.2) < 1e-6,
            "ProcessIdleFrame must decay ShotsFired to 0 and restore DefaultAccuracy.");
    }

    private static void TestWeaponRegistryAndMultiWeaponVerificationSuite()
    {
        var registry = WeaponRegistry.CreateDefault();

        // 1. Verify identification of all 7 weapon groups (AR, SMG, LMG, Pistol, Sniper, Shotgun, Custom)
        Require(registry.IdentifyCategory("AK47_Sim", out var catAr, out _) && catAr == WeaponCategory.AssaultRifle,
            "AK47_Sim must be identified as AssaultRifle.");
        Require(registry.IdentifyCategory("M4A1_Sim", out var catM4, out _) && catM4 == WeaponCategory.AssaultRifle,
            "M4A1_Sim must be identified as AssaultRifle.");
        Require(registry.IdentifyCategory("MP5_Sim", out var catSmg, out _) && catSmg == WeaponCategory.SubmachineGun,
            "MP5_Sim must be identified as SubmachineGun.");
        Require(registry.IdentifyCategory("M249_Sim", out var catLmg, out _) && catLmg == WeaponCategory.LightMachineGun,
            "M249_Sim must be identified as LightMachineGun.");
        Require(registry.IdentifyCategory("Deagle_Sim", out var catPistol, out _) && catPistol == WeaponCategory.Pistol,
            "Deagle_Sim must be identified as Pistol.");
        Require(registry.IdentifyCategory("AWP_Sim", out var catSniper, out _) && catSniper == WeaponCategory.SniperRifle,
            "AWP_Sim must be identified as SniperRifle.");
        Require(registry.IdentifyCategory("Barrett_Sim", out var catBarrett, out _) && catBarrett == WeaponCategory.SniperRifle,
            "Barrett_Sim must be identified as SniperRifle.");
        Require(registry.IdentifyCategory("Scout_Sim", out var catScout, out _) && catScout == WeaponCategory.SniperRifle,
            "Scout_Sim must be identified as SniperRifle.");
        Require(registry.IdentifyCategory("Steyr Scout", out var catScoutName, out _) && catScoutName == WeaponCategory.SniperRifle,
            "Steyr Scout must be identified as SniperRifle.");
        Require(registry.IdentifyCategory("M82A1", out var catM82, out _) && catM82 == WeaponCategory.SniperRifle,
            "M82A1 must be identified as SniperRifle.");
        Require(registry.IdentifyCategory("XM1014_Sim", out var catShotgun, out _) && catShotgun == WeaponCategory.Shotgun,
            "XM1014_Sim must be identified as Shotgun.");
        Require(registry.IdentifyCategory("CustomPulse_Sim", out var catCustom, out string customTag) &&
                catCustom == WeaponCategory.Custom && customTag == "TacticalBurstPrototype",
            "CustomPulse_Sim must be identified as Custom with tag TacticalBurstPrototype.");
        Require(!registry.IdentifyCategory("   ", out _, out _) && !registry.IdentifyCategory("UnknownGadget_999", out _, out _),
            "Blank or unknown weapon name must return false in IdentifyCategory.");

        // 2. Verify loading exact parameters and dynamic custom registration without changing runner code
        var shotgunProfile = registry.GetProfile("XM1014_Sim");
        Require(shotgunProfile.PelletCount == 8 && Math.Abs(shotgunProfile.SpreadScaleY - 0.85f) < 1e-5f,
            "XM1014_Sim parameters must match registered Shotgun profile.");

        var customRailProfile = new WeaponSimulationProfile(
            WeaponId: "CustomRail_Sim",
            DisplayName: "Custom Magnetic Rail Carbine",
            Category: WeaponCategory.Custom,
            CustomGroupTag: "MagneticCarbineGroup",
            ClipCapacity: 20,
            DefaultShotsFired: 1,
            SpreadScaleY: 0.35f,
            ShotsFiredMultiplier: 1.08,
            DefaultPunchYaw: 0.12f,
            DefaultPunchPitch: 0.40f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0018,
                [MovementStateKind.Walk] = 0.0085,
                [MovementStateKind.Run] = 0.0210,
                [MovementStateKind.Strafe] = 0.0210,
                [MovementStateKind.MoveTurn] = 0.0260,
                [MovementStateKind.JumpAir] = 0.0550,
                [MovementStateKind.Crouch] = 0.0010,
                [MovementStateKind.AimDownSights] = 0.0011
            });
        registry.Register(customRailProfile);
        Require(registry.GetByCustomGroup("MagneticCarbineGroup").Count == 1,
            "Dynamically registered custom group must be queryable.");

        // 3. Verify weapon switching on SimulatedActorSnapshot
        var actor = new SimulatedActorSnapshot { EntityId = 1, IsLocal = true, IsAlive = true };
        var snap1 = registry.ApplyToActor(actor, "AK47_Sim", MovementStateKind.Run);
        Require(actor.CurrentWeapon is not null && actor.CurrentWeapon.WeaponName == "AK47_Sim" &&
                Math.Abs(snap1.Spread - 0.065) < 1e-6 && Math.Abs(actor.PunchPitch - 1.20f) < 1e-5f,
            "ApplyToActor must load AK47_Sim Run parameters.");

        var snap2 = registry.ApplyToActor(actor, "AWP_Sim", MovementStateKind.Stand);
        Require(actor.CurrentWeapon is not null &&
                actor.CurrentWeapon.WeaponName == "AWP_Sim" &&
                Math.Abs(snap2.Spread - 0.0012) < 1e-6 &&
                Math.Abs(snap2.SpreadScaleY - 1.0f) < 1e-5f &&
                Math.Abs(actor.PunchPitch - 2.20f) < 1e-5f,
            "Switching actor weapon to AWP_Sim must replace snapshot and recoil parameters cleanly.");

        // 4. Verify invalid data, missing configurations, and out-of-range values
        bool missingThrew = false;
        try { _ = registry.GetProfile("Missing_Weapon_ID"); }
        catch (KeyNotFoundException) { missingThrew = true; }
        Require(missingThrew, "Requesting unregistered weapon ID must throw KeyNotFoundException.");

        // Duplicate registration without overwrite must fail
        Require(!registry.TryRegister(customRailProfile, out var dupErrors, overwrite: false) && dupErrors.Count > 0,
            "Duplicate registration with overwrite=false must be rejected.");

        // Out-of-range profile registration must fail
        var badProfile = customRailProfile with
        {
            WeaponId = "Bad_Sim",
            ClipCapacity = -10,
            SpreadScaleY = -0.5f,
            ShotsFiredMultiplier = double.NaN,
            SpreadByMovementState = new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = -0.1,
                [MovementStateKind.Walk] = double.PositiveInfinity
            }
        };
        Require(!registry.TryRegister(badProfile, out var badErrors, overwrite: true) && badErrors.Count >= 4,
            "Invalid WeaponSimulationProfile must fail validation with multiple errors.");

        // Snapshot validation
        var badSnapshot = new SimulatedWeaponSnapshot { WeaponName = "", ClipAmmo = -1, ShotsFired = -2, Spread = 9.9, SpreadScaleY = 0f, ShotsFiredMultiplier = -1.0 };
        Require(!registry.ValidateSnapshot(badSnapshot, out var snapErrors) && snapErrors.Count >= 5,
            "ValidateSnapshot must flag all invalid snapshot fields.");

        // 5 & 6. Execute full multi-weapon simulation suite (including dynamically added CustomRail_Sim and 8 invalid data cases)
        var multiReport = SimulatedWorldScenarioRunner.RunMultiWeaponVerificationSuite(registry, includeInvalidDataCases: true);

        // 11 registered valid weapons (10 built-in + CustomRail_Sim) * 126 shots per weapon (2 seeds * 63 angle/state combinations) = 1386 valid shots + 8 invalid data shots = 1394 total shots
        Require(multiReport.TotalShotsSimulated == 1394, $"Expected 1394 total shots (1386 valid + 8 invalid), actual={multiReport.TotalShotsSimulated}.");
        Require(multiReport.CompensatedShots == 1386, $"Expected 1386 compensated valid shots, actual={multiReport.CompensatedShots}.");
        Require(multiReport.SkippedShots == 8 && multiReport.InvalidWeaponDataCases == 8,
            $"Expected 8 skipped invalid weapon data cases, actual skipped={multiReport.SkippedShots}, invalid={multiReport.InvalidWeaponDataCases}.");
        Require(multiReport.BackwardCompatibilityVerified, "Backward compatibility with legacy 378-shot AK47_Sim baseline must pass.");
        Require(multiReport.WeaponSwitchCount >= 17, $"Expected at least 17 weapon switches, actual={multiReport.WeaponSwitchCount}.");
        Require(multiReport.MetricsByCategory is not null && multiReport.MetricsByWeapon is not null,
            "Multi-weapon report must include per-category and per-weapon metrics.");

        WeaponCategory[] allCategories =
        {
            WeaponCategory.AssaultRifle,
            WeaponCategory.SubmachineGun,
            WeaponCategory.LightMachineGun,
            WeaponCategory.Pistol,
            WeaponCategory.SniperRifle,
            WeaponCategory.Shotgun,
            WeaponCategory.Custom
        };

        foreach (var cat in allCategories)
        {
            Require(multiReport.MetricsByCategory!.TryGetValue(cat, out var catMetrics) && catMetrics is not null,
                $"Missing category metrics for {cat}.");
            Require(catMetrics!.SucceededCases > 0 && catMetrics.MaxQuantizedErrorDegrees <= 0.01,
                $"Category {cat} failed verification: succeeded={catMetrics.SucceededCases}, maxQuantErr={catMetrics.MaxQuantizedErrorDegrees:F6}");
        }

        Console.WriteLine();
        Console.Write(SimulatedWorldScenarioRunner.FormatSimulationReport(multiReport));
    }

    private static void TestComprehensiveEightGroupEightStateMatrix()
    {
        var matrixReport = SimulatedWorldScenarioRunner.RunComprehensiveWeaponStateMatrixSuite();
        Require(matrixReport.AllGroupsPassed, "All 8 weapon groups must pass the 8-state matrix verification.");
        Require(matrixReport.WeaponGroups.Count == 8, $"Expected 8 weapon groups, actual={matrixReport.WeaponGroups.Count}.");
        Require(matrixReport.TotalFailedShots == 0, $"Expected 0 failed shots across matrix, actual={matrixReport.TotalFailedShots}.");

        string[] expectedGroups = { "AK47", "M4A1", "SMG", "LMG", "Pistol", "Sniper", "Shotgun", "Custom" };
        foreach (string groupId in expectedGroups)
        {
            var group = matrixReport.WeaponGroups.SingleOrDefault(g => string.Equals(g.GroupId, groupId, StringComparison.OrdinalIgnoreCase));
            Require(group is not null, $"Missing weapon group '{groupId}' in matrix report.");
            Require(group!.AllPassed, $"Weapon group '{groupId}' failed matrix verification.");
            Require(group.MaxCompensatedQuantizedErrorDegrees <= 0.01,
                $"Weapon group '{groupId}' exceeded 0.01 deg quantized tolerance: {group.MaxCompensatedQuantizedErrorDegrees:F6}");

            foreach (ComprehensiveWeaponScenarioKind scenario in Enum.GetValues<ComprehensiveWeaponScenarioKind>())
            {
                Require(group.ByScenario.TryGetValue(scenario, out var scMetrics) && scMetrics is not null,
                    $"Missing scenario '{scenario}' for weapon group '{groupId}'.");
                Require(scMetrics!.TotalShots > 0 && scMetrics.FailedShots == 0 && scMetrics.SolverStabilityRate == 1.0,
                    $"Scenario '{scenario}' for '{groupId}' failed stability check.");
                Require(scMetrics.MaxCompensatedQuantizedErrorDegrees <= 0.01,
                    $"Scenario '{scenario}' for '{groupId}' exceeded 0.01 deg quantized error: {scMetrics.MaxCompensatedQuantizedErrorDegrees:F6}");
            }

            if (group.Category == WeaponCategory.Shotgun)
            {
                Require(group.PelletCount == 8 && group.MeanIrreducibleDispersionDegrees > 0.40,
                    $"Shotgun group '{groupId}' must report multi-pellet irreducible cone dispersion (> 0.40 deg) separately from algorithmic error.");
            }
            else
            {
                Require(group.PelletCount == 1 && group.MeanIrreducibleDispersionDegrees == 0.0,
                    $"Single-projectile group '{groupId}' must have 0 multi-pellet cone dispersion.");
            }
        }

        // Verify that ALL 8 MovementStateKind buckets (including Crouch and AimDownSights) have non-zero samples and <= 0.01 deg quantized error
        foreach (MovementStateKind moveState in Enum.GetValues<MovementStateKind>())
        {
            Require(matrixReport.MovementStateSummary.TryGetValue(moveState, out var m) && m is not null && m.SampleCount > 0,
                $"MovementStateKind '{moveState}' must have > 0 samples in matrix verification.");
            Require(m!.MaxQuantizedErrorDegrees <= 0.01,
                $"MovementStateKind '{moveState}' exceeded 0.01 deg quantized error: {m.MaxQuantizedErrorDegrees:F6}");
        }

        Console.WriteLine();
        Console.Write(SimulatedWorldScenarioRunner.FormatComprehensiveMatrixReport(matrixReport));
    }

    private static void TestEngineModuleLoaderAndFullDiGraph()
    {
        Require(
            EngineModuleLoader.VerifyAndLoadAssemblies(out var loaded, out var failed),
            "All required assemblies must load cleanly: " + string.Join(", ", failed));
        Require(loaded.Count == EngineModuleLoader.RequiredAssemblies.Count,
            $"Expected {EngineModuleLoader.RequiredAssemblies.Count} loaded assemblies, actual={loaded.Count}.");

        using var container = EngineModuleLoader.CreateRuntimeServiceContainer();
        var host = container.Resolve<EngineHost>();
        var moduleManager = container.Resolve<IModuleManager>();
        var weaponRegistry = container.Resolve<IWeaponRegistry>();
        var categoryEngine = container.Resolve<WeaponCategorySimulationEngine>();
        var weaponResolver = container.Resolve<IWeaponParameterResolver>();
        var spreadModule = container.Resolve<SpreadCompensationModule>();

        Require(moduleManager.Modules.Count == 5, $"Expected 5 registered engine modules, actual={moduleManager.Modules.Count}.");
        Require(weaponRegistry.GetAllProfiles().Count >= 8, "WeaponRegistry must contain all 8 built-in weapon profiles across 7 categories.");
        Require(categoryEngine.GetAllConfigurations().Count >= 12, "WeaponCategorySimulationEngine must contain all 12 default configs across 7 categories.");
        Require(host.GetModuleStatuses().All(m => m.LifecycleState == ModuleLifecycleState.Idle),
            "All 5 modules must be initialized to Idle by EngineModuleLoader.");

        var world = container.Resolve<SimulatedWorldState>();
        var localActor = (SimulatedActorSnapshot)world.LocalActor!;
        weaponRegistry.ApplyToActor(localActor, "XM1014_Sim", MovementStateKind.Stand);
        Require(
            weaponResolver.TryResolveWeapon(localActor, localActor.CurrentWeapon!, MovementStateKind.Stand, 1, out var resolved),
            "RuntimeWeaponParameterResolver must resolve XM1014_Sim.");
        Require(resolved.Category == WeaponCategory.Shotgun && resolved.FormulaKind == WeaponAccuracyFormulaKind.ShotgunPelletFixed && resolved.PelletCount == 8,
            "XM1014_Sim must resolve to Shotgun / ShotgunPelletFixed / 8 pellets.");
        Require(spreadModule.CompensatedCount == 0, "SpreadCompensationModule must start with 0 compensated shots.");
    }

    private static void TestAuthorizedRuntimeHostAndApplyAckEnforcement()
    {
        using var currentProc = Process.GetCurrentProcess();
        int testPid = currentProc.Id;
        long startTicks = currentProc.StartTime.ToUniversalTime().Ticks;

        using var runtimeHost = new AuthorizedTestRuntimeHost(testPid, startTicks);
        Require(File.Exists(runtimeHost.StatusFilePath), "AuthorizedTestRuntimeHost must create initial status.xml.");

        string profilePath = Path.Combine(Path.GetTempPath(), $"hacktk-rt-test-{Guid.NewGuid():N}.xml");
        try
        {
            var profileStore = new AtomicXmlConfigurationStore<EngineProfile>();
            var commandStore = new AtomicXmlConfigurationStore<SessionCommandEnvelope>();
            var statusStore = new AtomicXmlConfigurationStore<RuntimeSessionStatus>();
            var timeProvider = new SystemTimeProvider();
            using var logger = new StructuredDiagnosticLogger(timeProvider);

            using var presenter = new DashboardPresenter(profileStore, commandStore, statusStore, timeProvider, logger, profilePath)
            {
                AckTimeoutMs = 300
            };

            var candidate = new RuntimeProcessOption(
                testPid,
                startTicks,
                currentProc.MainModule?.FileName ?? "HackTK.Next.Tests",
                true,
                currentProc.ProcessName,
                IsAuthorizedTestRuntime: true);

            // 1. Verify that TryConnect FAILS if the runtime does not process and acknowledge the connect command within AckTimeoutMs
            bool connectWithoutRuntimeTick = presenter.TryConnect(candidate, out _, out string unackedConnectMsg);
            Require(!connectWithoutRuntimeTick, "TryConnect must FAIL when runtime has not acknowledged revision #1.");
            Require(unackedConnectMsg.Contains("không xác nhận", StringComparison.OrdinalIgnoreCase),
                $"Expected unacknowledged connect error message, actual: {unackedConnectMsg}");

            // 2. Now run runtime tick concurrently so it processes command.xml and writes status.xml with AppliedRevision
            presenter.AckTimeoutMs = 1500;
            using var cts = new CancellationTokenSource();
            var pumpTask = Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    runtimeHost.StepTick();
                    Thread.Sleep(20);
                }
            });

            try
            {
                Require(presenter.TryConnect(candidate, out var connectedStatus, out string connectOkMsg),
                    "TryConnect must succeed when runtime acknowledges revision: " + connectOkMsg);
                Require(connectedStatus is not null && connectedStatus.Connected && connectedStatus.AppliedRevision == presenter.ActiveRevision,
                    "Connected status must match presenter ActiveRevision.");

                presenter.CurrentProfile.CompensateCommandSpread = true;
                presenter.CurrentProfile.SmoothCameraRecoil = true;
                presenter.CurrentProfile.AimEnabled = true;
                presenter.CurrentProfile.EspBox = true;
                presenter.CurrentProfile.PlayerChams = true;
                presenter.CurrentProfile.BunnyHop = true;

                Require(presenter.TryApplyCurrentProfile(out var appliedStatus, out string applyOkMsg),
                    "TryApplyCurrentProfile must succeed when runtime acknowledges revision: " + applyOkMsg);
                Require(appliedStatus is not null && appliedStatus.AppliedRevision == presenter.ActiveRevision,
                    "AppliedRevision in confirmed status must equal presenter ActiveRevision.");
                Require(presenter.LastConfirmedRevision == presenter.ActiveRevision,
                    "LastConfirmedRevision must equal ActiveRevision after ACK.");
            }
            finally
            {
                cts.Cancel();
                pumpTask.Wait(1000);
            }

            // 3. Stop the runtime pump and verify that a subsequent Apply FAILS because no runtime acknowledges the new revision
            runtimeHost.StepTick(); // Keep heartbeat fresh once, but do not process future commands
            presenter.AckTimeoutMs = 200;
            presenter.CurrentProfile.Crosshair = true;
            bool applyWithoutAck = presenter.TryApplyCurrentProfile(out _, out string unackedApplyMsg);
            Require(!applyWithoutAck,
                "TryApplyCurrentProfile must FAIL when command.xml is written but runtime does not acknowledge the new revision!");
            Require(unackedApplyMsg.Contains("Chưa nhận được xác nhận (ACK)", StringComparison.OrdinalIgnoreCase),
                $"Expected explicit ACK failure message, actual: {unackedApplyMsg}");

            // 4. Regression test: when status.xml contains a pre-connect CommandError ("Chưa có lệnh hợp lệ.")
            // and the runtime flushes status.xml every ~450ms (> 200ms), TryConnect must NOT abort prematurely.
            presenter.Disconnect(out _);
            var preConnectStaleStatus = statusStore.Load(runtimeHost.StatusFilePath);
            preConnectStaleStatus.Connected = false;
            preConnectStaleStatus.CommandError = "Chưa có lệnh hợp lệ.";
            preConnectStaleStatus.MainThreadUtcTicks = timeProvider.UtcNow.UtcTicks;
            statusStore.Save(runtimeHost.StatusFilePath, preConnectStaleStatus);

            presenter.AckTimeoutMs = 2000;
            var delayedAckTask = Task.Run(() =>
            {
                Thread.Sleep(450);
                runtimeHost.StepTick();
            });
            try
            {
                bool delayedConnectOk = presenter.TryConnect(candidate, out var delayedStatus, out string delayedMsg);
                Require(delayedConnectOk && delayedStatus is not null && delayedStatus.Connected,
                    "TryConnect must wait past stale pre-dispatch CommandError ('Chưa có lệnh hợp lệ.') when runtime heartbeat interval > 200ms: " + delayedMsg);
            }
            finally
            {
                delayedAckTask.Wait(2000);
            }
        }
        finally
        {
            if (File.Exists(profilePath)) File.Delete(profilePath);
            string tempSessionDir = runtimeHost.SessionDirectory;
            runtimeHost.Dispose();
            if (Directory.Exists(tempSessionDir))
            {
                try { Directory.Delete(tempSessionDir, recursive: true); } catch { }
            }
        }
    }

    private static void TestFullTwelveStepEndToEndDesktopToRuntime()
    {
        var logLines = new List<string>();
        void LogStep(int step, string title, string evidence)
        {
            string entry = $"[E2E-STEP-{step:D2}] {title} => PASS | {evidence}";
            logLines.Add(entry);
            Console.WriteLine("   " + entry);
        }

        string e2eDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "verification", "e2e"));
        Directory.CreateDirectory(e2eDir);
        string profilePath = Path.Combine(e2eDir, "e2e-desktop-profile.xml");

        // STEP 1: Khởi động ứng dụng Desktop
        using var container1 = EngineModuleLoader.CreateRuntimeServiceContainer();
        var profileStore = container1.Resolve<IConfigurationStore<EngineProfile>>();
        var commandStore = container1.Resolve<IConfigurationStore<SessionCommandEnvelope>>();
        var statusStore = container1.Resolve<IConfigurationStore<RuntimeSessionStatus>>();
        var timeProvider = container1.Resolve<ITimeProvider>();
        var logger = container1.Resolve<IDiagnosticLogger>();

        using var presenter1 = new DashboardPresenter(profileStore, commandStore, statusStore, timeProvider, logger, profilePath)
        {
            AckTimeoutMs = 3000
        };
        using var form1 = new EngineDashboardForm(presenter1);
        form1.Show();
        System.Windows.Forms.Application.DoEvents();
        LogStep(1, "Khởi động ứng dụng Desktop (EngineDashboardForm + DashboardPresenter)",
            $"FormVisible={form1.Visible}, Title='{form1.Text}', ProfilePath='{presenter1.ProfilePath}'");

        // STEP 2: Xác minh các module cần thiết được nạp
        Require(EngineModuleLoader.VerifyAndLoadAssemblies(out var loadedAssemblies, out var failedAssemblies),
            "Missing required assemblies: " + string.Join(", ", failedAssemblies));
        var moduleManager1 = container1.Resolve<IModuleManager>();
        Require(moduleManager1.Modules.Count == 5, "Desktop DI container must load all 5 IEngineModules.");
        LogStep(2, "Xác minh các module và assembly cần thiết được nạp",
            $"LoadedAssemblies={loadedAssemblies.Count} ({string.Join(", ", loadedAssemblies.Select(a => a.Split(' ')[0]))}), RegisteredModules={moduleManager1.Modules.Count}");

        int runtimePid = 0;
        try
        {
            // STEP 3: Khởi động runtime thử nghiệm (tiến trình độc lập HackTK.Next.Runtime.exe)
            form1.StartTestRuntimeFromUi();
            System.Windows.Forms.Application.DoEvents();
            var discovered = presenter1.DiscoverProcesses(out string discoverMsg);
            var runtimeOption = discovered.FirstOrDefault(p => p.IsAuthorizedTestRuntime && p.HasStatusFile);
            Require(runtimeOption is not null, "Must discover running HackTK.Next.Runtime.exe process with status.xml.");
            runtimePid = runtimeOption!.ProcessId;
            Require(runtimePid != Environment.ProcessId, "HackTK.Next.Runtime.exe must run in a separate OS process.");
            LogStep(3, "Khởi động tiến trình Runtime Thử Nghiệm độc lập (HackTK.Next.Runtime.exe)",
                $"PID={runtimePid}, ProcessName={runtimeOption.ProcessName}, StatusFile={RuntimeSessionPaths.StatusFile(runtimePid)}, Discover='{discoverMsg}'");

            // STEP 4: Xác nhận kết nối hai chiều
            form1.ConnectSelectedProcessFromUi();
            System.Windows.Forms.Application.DoEvents();
            Require(presenter1.IsConnected && presenter1.LastConfirmedRevision >= 1,
                "Desktop UI must establish bidirectional connection and receive ACK for initial connect revision.");
            long connectRev = presenter1.LastConfirmedRevision;
            LogStep(4, "Xác nhận kết nối hai chiều (Desktop UI <-> HackTK.Next.Runtime.exe)",
                $"IsConnected={presenter1.IsConnected}, SessionToken={presenter1.LastConfirmedStatus?.Session}, ConfirmedRevision=#{connectRev}, Banner='{form1.ConnectionBannerText.Replace("\r\n", " | ")}'");

            // STEP 5: Thay đổi cấu hình từ Desktop
            presenter1.CurrentProfile.AimEnabled = true;
            presenter1.CurrentProfile.DrawAimRadius = true;
            presenter1.CurrentProfile.RecoilCompensation = true;
            presenter1.CurrentProfile.SmoothCameraRecoil = true;
            presenter1.CurrentProfile.CompensateCommandSpread = true;
            presenter1.CurrentProfile.RecordDiagnostics = true;
            presenter1.CurrentProfile.EspBox = true;
            presenter1.CurrentProfile.EspSkeleton = true;
            presenter1.CurrentProfile.EspNames = true;
            presenter1.CurrentProfile.EspHealth = true;
            presenter1.CurrentProfile.PlayerChams = true;
            presenter1.CurrentProfile.Crosshair = true;
            presenter1.CurrentProfile.BunnyHop = true;
            form1.SyncControlsFromProfile();
            Require( presenter1.TrySaveProfile(profilePath, out string saveMsg), "Must save updated profile to XML: " + saveMsg);
            LogStep(5, "Thay đổi cấu hình từ Desktop & lưu XML",
                $"NoSpread={presenter1.CurrentProfile.CompensateCommandSpread}, NoRecoil={presenter1.CurrentProfile.SmoothCameraRecoil}, Aim={presenter1.CurrentProfile.AimEnabled}, ESP={presenter1.CurrentProfile.EspBox}, Chams={presenter1.CurrentProfile.PlayerChams}, Bhop={presenter1.CurrentProfile.BunnyHop}");

            // STEP 6: Nhấn Apply từ giao diện Desktop
            form1.ApplyCurrentProfileFromUi();
            System.Windows.Forms.Application.DoEvents();
            long applyRev = presenter1.ActiveRevision;
            Require(presenter1.LastConfirmedRevision == applyRev,
                $"Apply must wait for and confirm AppliedRevision=={applyRev}, actual={presenter1.LastConfirmedRevision}.");
            LogStep(6, "Nhấn Apply trên Desktop UI và chờ ACK thực thi",
                $"DispatchedRevision=#{applyRev}, LastConfirmedRevision=#{presenter1.LastConfirmedRevision}, Status='{form1.StatusText}'");

            // STEP 7: Xác minh runtime nhận cấu hình
            string cmdXmlPath = RuntimeSessionPaths.CommandFile(runtimePid);
            Require(File.Exists(cmdXmlPath), "command.xml must exist in runtime session directory.");
            var diskEnvelope = commandStore.Load(cmdXmlPath);
            Require(diskEnvelope.Revision == applyRev && diskEnvelope.Connected && diskEnvelope.Profile.CompensateCommandSpread,
                "command.xml on disk must match Revision and Profile settings.");
            var confirmedStatus = presenter1.LastConfirmedStatus!;
            Require(confirmedStatus.AppliedRevision == applyRev && confirmedStatus.Connected,
                "status.xml from runtime must confirm AppliedRevision == applyRev.");
            LogStep(7, "Xác minh Runtime đã nhận và áp dụng gói lệnh cấu hình",
                $"CommandFileRevision=#{diskEnvelope.Revision}, RuntimeAppliedRevision=#{confirmedStatus.AppliedRevision}, RuntimeState='{confirmedStatus.State}'");

            // STEP 8: Xác minh cấu hình được xử lý qua đầy đủ 8 nhóm súng & 8 trạng thái trong Runtime
            // Allow external runtime process to tick through >= 64 frames (8 weapon groups x 8 states)
            Thread.Sleep(2200);
            Require(presenter1.TryTickConnection(out var liveStatusAfterRun, out string tickMsg) && liveStatusAfterRun is not null,
                "Heartbeat tick must succeed while runtime is executing: " + tickMsg);
            form1.Refresh();
            var activeModules = liveStatusAfterRun!.Features
                .Where(f => string.Equals(f.State, nameof(ModuleLifecycleState.Active), StringComparison.OrdinalIgnoreCase))
                .ToList();
            Require(activeModules.Count >= 5, $"All 5 engine modules must be Active in runtime status, actual={activeModules.Count}.");
            LogStep(8, "Xác minh cấu hình được xử lý bởi cả 5 module trong Runtime",
                $"ActiveFeatureEntries={activeModules.Count}, TickMsg='{tickMsg}'");

            // STEP 9: Kiểm tra kết quả đầu ra
            var spreadFeature = liveStatusAfterRun.Features.FirstOrDefault(f => f.Feature == "No recoil / No spread");
            var aimFeature = liveStatusAfterRun.Features.FirstOrDefault(f => f.Feature == "Aimbot + FOV + Smooth");
            var espFeature = liveStatusAfterRun.Features.FirstOrDefault(f => f.Feature == "ESP hộp / xương / tên / máu / snaplines");
            var chamsFeature = liveStatusAfterRun.Features.FirstOrDefault(f => f.Feature == "Chams");
            var bhopFeature = liveStatusAfterRun.Features.FirstOrDefault(f => f.Feature == "Bhop");

            Require(spreadFeature is not null && spreadFeature.Detail.Contains("Đã bù=", StringComparison.Ordinal) && !spreadFeature.Detail.Contains("Đã bù=0,", StringComparison.Ordinal),
                $"SpreadCompensationModule must have compensated > 0 shots in runtime, actual='{spreadFeature?.Detail}'.");
            Require(aimFeature is not null && !aimFeature.Detail.Contains("Tổng bước chuột=0", StringComparison.Ordinal),
                $"TargetAcquisitionModule must have dispatched mouse steps, actual='{aimFeature?.Detail}'.");
            Require(espFeature is not null && !espFeature.Detail.Contains("Lệnh vẽ overlay=0", StringComparison.Ordinal),
                $"EspProjectionModule must have rendered overlay elements, actual='{espFeature?.Detail}'.");
            Require(chamsFeature is not null && !chamsFeature.Detail.Contains("Slot đang ghi đè=0", StringComparison.Ordinal),
                $"MaterialSlotModule must have active material slot replacements, actual='{chamsFeature?.Detail}'.");
            Require(bhopFeature is not null && !bhopFeature.Detail.Contains("Xung nhảy=0", StringComparison.Ordinal),
                $"MovementPulseModule must have dispatched jump pulses, actual='{bhopFeature?.Detail}'.");
            LogStep(9, "Kiểm tra kết quả đầu ra thực tế từ tất cả các module",
                $"Spread=[{spreadFeature!.Detail}] | Aim=[{aimFeature!.Detail}] | ESP=[{espFeature!.Detail}] | Chams=[{chamsFeature!.Detail}] | Bhop=[{bhopFeature!.Detail}]");

            // STEP 10: Kiểm tra log và trạng thái trên Desktop UI
            string runtimeEventsPath = Path.Combine(RuntimeSessionPaths.ForProcess(runtimePid), "runtime-events.log");
            Require(File.Exists(runtimeEventsPath), "runtime-events.log must be written by HackTK.Next.Runtime.exe.");
            string eventsContent = File.ReadAllText(runtimeEventsPath);
            Require(eventsContent.Contains("ENGINE_REVISION_APPLIED", StringComparison.Ordinal) &&
                    eventsContent.Contains("WEAPON_DETECTED", StringComparison.Ordinal) &&
                    eventsContent.Contains("COMMAND_COMPENSATED", StringComparison.Ordinal),
                "runtime-events.log must contain ENGINE_REVISION_APPLIED, WEAPON_DETECTED, and COMMAND_COMPENSATED events.");

            // Trigger UI heartbeat update so DataGridView reflects latest status
            form1.ApplyCurrentProfileFromUi();
            var gridRows = form1.GetLiveModuleGridRows();
            Require(gridRows.Count >= 10 && gridRows.Any(r => r.State == "Active"),
                "EngineDashboardForm DataGridView must display live Active module rows.");
            LogStep(10, "Kiểm tra log thực thi (runtime-events.log) và bảng trạng thái Desktop UI",
                $"LogFileBytes={new FileInfo(runtimeEventsPath).Length}, GridRowCount={gridRows.Count}, ActiveRowsInUiGrid={gridRows.Count(r => r.State == "Active")}");

            // STEP 11: Tắt/bật module và xác nhận chuyển trạng thái
            form1.DisableAllFromUi();
            System.Windows.Forms.Application.DoEvents();
            var statusAfterDisable = presenter1.LastConfirmedStatus!;
            Require(statusAfterDisable.Features.All(f => f.State == nameof(ModuleLifecycleState.Idle)),
                "All modules must transition to Idle when DisableAll is applied.");
            long disableRev = presenter1.LastConfirmedRevision;

            // Re-enable SpreadCompensationModule + SmoothCameraRecoil and Apply
            presenter1.CurrentProfile.CompensateCommandSpread = true;
            presenter1.CurrentProfile.SmoothCameraRecoil = true;
            form1.SyncControlsFromProfile();
            form1.ApplyCurrentProfileFromUi();
            var statusAfterReEnable = presenter1.LastConfirmedStatus!;
            var reEnabledSpread = statusAfterReEnable.Features.First(f => f.Feature == "No recoil / No spread");
            Require(reEnabledSpread.State == nameof(ModuleLifecycleState.Active),
                "SpreadCompensationModule must return to Active after re-enabling and clicking Apply.");
            LogStep(11, "Tắt toàn bộ module (Idle) và bật lại module No Spread/No Recoil (Active)",
                $"DisableRev=#{disableRev} (All Idle=true) -> ReEnableRev=#{presenter1.LastConfirmedRevision} (SpreadState={reEnabledSpread.State})");

            // STEP 12: Khởi động lại ứng dụng Desktop và kiểm tra tính ổn định kết nối lại
            presenter1.KeepSpawnedRuntimeOnDispose = true;
            form1.DisconnectFromUi();
            Require(!presenter1.IsConnected, "First Desktop instance must disconnect cleanly.");
            form1.Close();

            using var presenter2 = new DashboardPresenter(profileStore, commandStore, statusStore, timeProvider, logger, profilePath)
            {
                AckTimeoutMs = 3000
            };
            using var form2 = new EngineDashboardForm(presenter2);
            form2.Show();
            System.Windows.Forms.Application.DoEvents();
            form2.RefreshProcessesFromUi();
            form2.ConnectSelectedProcessFromUi();
            Require(presenter2.IsConnected, "Restarted Desktop UI instance must reconnect to running runtime process.");
            presenter2.CurrentProfile.CompensateCommandSpread = true;
            presenter2.CurrentProfile.SmoothCameraRecoil = true;
            presenter2.CurrentProfile.AimEnabled = true;
            form2.SyncControlsFromProfile();
            form2.ApplyCurrentProfileFromUi();
            Require(presenter2.LastConfirmedRevision == presenter2.ActiveRevision,
                "Restarted Desktop UI instance must apply profile and receive ACK.");
            LogStep(12, "Khởi động lại ứng dụng Desktop (Instance #2), kết nối lại & kiểm tra tính ổn định",
                $"Instance2Connected={presenter2.IsConnected}, Instance2ConfirmedRevision=#{presenter2.LastConfirmedRevision}, Banner='{form2.ConnectionBannerText.Replace("\r\n", " | ")}'");

            form2.Close();
        }
        finally
        {
            presenter1.StopSpawnedTestRuntime();
        }

        // Verify runtime-summary.json written by HackTK.Next.Runtime.exe upon clean shutdown
        string summaryPath = Path.Combine(RuntimeSessionPaths.ForProcess(runtimePid), "runtime-summary.json");
        Require(File.Exists(summaryPath), $"HackTK.Next.Runtime.exe must write {summaryPath} on shutdown.");
        string summaryJson = File.ReadAllText(summaryPath);
        using var summaryDoc = JsonDocument.Parse(summaryJson);
        int totalCompensatedInRuntime = summaryDoc.RootElement.GetProperty("CompensatedShots").GetInt32();
        Require(totalCompensatedInRuntime >= 8, $"Expected >= 8 compensated shots across weapon groups in live runtime, actual={totalCompensatedInRuntime}.");

        var byGroupElement = summaryDoc.RootElement.GetProperty("CompensatedByWeaponGroup");
        foreach (var group in AuthorizedTestRuntimeHost.CanonicalWeaponGroups)
        {
            int groupCount = byGroupElement.GetProperty(group.GroupKey).GetInt32();
            Require(groupCount > 0, $"Weapon group '{group.GroupKey}' must have > 0 compensated shots in live runtime execution.");
        }

        File.WriteAllLines(Path.Combine(e2eDir, "e2e-verification-log.txt"), logLines, Encoding.UTF8);
        File.Copy(summaryPath, Path.Combine(e2eDir, "e2e-runtime-summary.json"), overwrite: true);
    }

    private static void TestSniperModelsAndAkM4RegressionVerification()
    {
        var registry = WeaponRegistry.CreateDefault();
        var engine = new WeaponCategorySimulationEngine(registry);
        var resolver = new RuntimeWeaponParameterResolver(registry, engine);
        var solver = new NewtonSpreadSolverService();
        var dirModel = new TriangularSpreadDirectionModel();
        var time = new DeterministicTimeProvider();
        var logger = new StructuredDiagnosticLogger(time, maxRetainedEvents: 16384);
        var spreadModule = new SpreadCompensationModule(solver, dirModel, logger, resolver);
        spreadModule.Initialize();
        spreadModule.Configure(new EngineProfile
        {
            CompensateCommandSpread = true,
            SmoothCameraRecoil = true,
            RecoilCompensation = true,
            RecordDiagnostics = true
        });

        // 1. Registry & Classification check for AWP, Barrett, Scout and common aliases
        (string Alias, string ExpectedCanonicalConfigId, string ExpectedProfileId)[] sniperAliases =
        {
            ("awp", "awp", "AWP_Sim"),
            ("AWP_Sim", "awp", "AWP_Sim"),
            ("AWM", "awp", "AWP_Sim"),
            ("barrett", "barrett", "Barrett_Sim"),
            ("Barrett_Sim", "barrett", "Barrett_Sim"),
            ("M82A1", "barrett", "Barrett_Sim"),
            ("M99", "barrett", "Barrett_Sim"),
            ("scout", "scout", "Scout_Sim"),
            ("Scout_Sim", "scout", "Scout_Sim"),
            ("SSG08", "scout", "Scout_Sim"),
            ("Steyr_Scout", "scout", "Scout_Sim")
        };

        foreach (var item in sniperAliases)
        {
            Require(registry.IdentifyCategory(item.Alias, out var cat1, out _) && cat1 == WeaponCategory.SniperRifle,
                $"WeaponRegistry.IdentifyCategory failed for Sniper alias '{item.Alias}'.");
            Require(WeaponCategorySimulationEngine.ClassifyWeapon(item.Alias) == WeaponCategory.SniperRifle,
                $"WeaponCategorySimulationEngine.ClassifyWeapon failed for Sniper alias '{item.Alias}'.");
            Require(BallisticAuditAnalyzer.DefaultClassifyWeapon(item.Alias).Category == WeaponCategory.SniperRifle,
                $"BallisticAuditAnalyzer.DefaultClassifyWeapon failed for Sniper alias '{item.Alias}'.");

            var cfg = engine.GetConfiguration(item.Alias);
            Require(cfg.Category == WeaponCategory.SniperRifle && cfg.FormulaKind == WeaponAccuracyFormulaKind.SniperScopedPosture,
                $"Configuration '{item.Alias}' must use SniperRifle / SniperScopedPosture.");
            var prof = registry.GetProfile(item.Alias);
            Require(string.Equals(prof.WeaponId, item.ExpectedProfileId, StringComparison.OrdinalIgnoreCase),
                $"Profile '{item.Alias}' resolved to '{prof.WeaponId}', expected '{item.ExpectedProfileId}'.");
        }

        // Verify FromSimulationProfile preserves Sniper AttackIntervalMs, AccuracyDivisor=-1, and UnscopedExtraSpread
        foreach (string profileId in new[] { "AWP_Sim", "Barrett_Sim", "Scout_Sim" })
        {
            var converted = WeaponCategorySimulationEngine.FromSimulationProfile(registry.GetProfile(profileId));
            Require(converted.Category == WeaponCategory.SniperRifle &&
                    converted.FormulaKind == WeaponAccuracyFormulaKind.SniperScopedPosture &&
                    converted.AccuracyDivisor < 0.0 &&
                    converted.AttackIntervalMs >= 1000 &&
                    converted.UnscopedExtraSpread >= 0.045 &&
                    Math.Abs(converted.SpreadScaleY - 1.0f) < 1e-6f,
                $"FromSimulationProfile corrupted Sniper parameters for '{profileId}'.");
        }

        // 2. Per-weapon Sniper model verification: AWP, Barrett, Scout (Recoil H/V, Scope/ADS/FOV, Bolt Recovery, 8-state Solver)
        (string Id, string Label, int ExpectedIntervalMs, double ExpectedUnscopedExtra)[] snipers =
        {
            ("awp", "AWP", 1250, 0.0800),
            ("barrett", "Barrett", 1450, 0.0950),
            ("scout", "Scout", 1050, 0.0450)
        };

        var sniperSummaryLines = new List<string>();
        var unscopedPitchBySniper = new Dictionary<string, float>();
        var unscopedYawBySniper = new Dictionary<string, float>();

        foreach (var sn in snipers)
        {
            var cfg = engine.GetConfiguration(sn.Id);
            Require(cfg.AttackIntervalMs == sn.ExpectedIntervalMs && Math.Abs(cfg.UnscopedExtraSpread - sn.ExpectedUnscopedExtra) < 1e-6,
                $"{sn.Label} interval/unscoped spread mismatch.");

            // 2a. Uninitialized default struct guard (Fov == 0 must normalize to 90 unscoped, NOT scoped!)
            var defaultW = default(SimulatedWeaponAccuracyState);
            var defaultOp = new SimulatedOperatorPostureState { IsOnGround = true, DuckState = 3, Fov = 0, ClientTimeMs = 1000 };
            engine.SwitchWeapon(sn.Id, ref defaultW, ref defaultOp);
            Require(defaultW.PunchYaw == 0f && defaultW.PunchPitch == 0f && defaultW.ShotsFired == 0 && defaultOp.Fov == 90,
                $"{sn.Label} Deploy must start with 0 recoil punch and Fov=90.");

            var zeroFovOp = new SimulatedOperatorPostureState { IsOnGround = true, DuckState = 3, Fov = 0, IsAimingDownSights = false, ClientTimeMs = 1000 };
            var evalZeroFov = engine.GetModel(WeaponCategory.SniperRifle).EvaluateFire(cfg, ref defaultW, ref zeroFovOp, 1000);
            Require(!evalZeroFov.IsScoped && evalZeroFov.FovBeforeShot == 90 && evalZeroFov.Spread >= sn.ExpectedUnscopedExtra,
                $"{sn.Label} uninitialized Fov=0 with IsAimingDownSights=false must evaluate as unscoped (Fov=90).");

            // 2b. Scope zoom cycle 90 -> 40 -> 10 -> 90, FOV spread & H/V recoil scaling, CheckCloseSniperScope, and bolt-recovery timing
            var wState = default(SimulatedWeaponAccuracyState);
            var opState = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 2000);
            engine.SwitchWeapon(sn.Id, ref wState, ref opState);
            double hipSpread = wState.Spread;
            var evalHip = engine.FireWeapon(ref wState, ref opState, explicitIntervalMs: cfg.AttackIntervalMs);
            unscopedPitchBySniper[sn.Id] = evalHip.PunchPitch;
            unscopedYawBySniper[sn.Id] = evalHip.PunchYaw;

            engine.SwitchWeapon(sn.Id, ref wState, ref opState);
            SniperRifleCategoryModel.ToggleScopeZoom(cfg, ref wState, ref opState); // 90 -> 40
            Require(opState.Fov == 40 && opState.IsAimingDownSights && Math.Abs(wState.Spread - cfg.StandSpreadParam) < 1e-9,
                $"{sn.Label} single zoom (Fov=40) must apply StandSpreadParam.");
            var evalZoom1 = engine.FireWeapon(ref wState, ref opState, explicitIntervalMs: cfg.AttackIntervalMs);

            engine.SwitchWeapon(sn.Id, ref wState, ref opState);
            SniperRifleCategoryModel.ToggleScopeZoom(cfg, ref wState, ref opState); // 90 -> 40
            SniperRifleCategoryModel.ToggleScopeZoom(cfg, ref wState, ref opState); // 40 -> 10
            Require(opState.Fov == 10 && opState.IsAimingDownSights && Math.Abs(wState.Spread - cfg.StandSpreadParam * cfg.AdsSpreadMultiplier) < 1e-9,
                $"{sn.Label} double zoom (Fov=10) must apply StandSpreadParam * AdsSpreadMultiplier.");
            double doubleZoomSpread = wState.Spread;

            // Fire while double-scoped (Fov=10) at t = 2000 + AttackIntervalMs
            var evalZoom2 = engine.FireWeapon(ref wState, ref opState, explicitIntervalMs: cfg.AttackIntervalMs);
            Require(evalZoom2.FovBeforeShot == 10 && evalZoom2.FovAfterShot == 90 && evalZoom2.IsScoped,
                $"{sn.Label} scoped shot must capture FovBeforeShot=10 and close scope to FovAfterShot=90.");
            Require(Math.Abs(evalZoom2.Spread - doubleZoomSpread) < 1e-9 && Math.Abs(wState.Spread - hipSpread) < 1e-9,
                $"{sn.Label} post-shot weaponState.Spread must immediately reflect unscoped Fov=90 while eval.Spread preserves shot-time scoped spread.");
            Require(evalZoom2.PunchPitch < evalZoom1.PunchPitch && evalZoom1.PunchPitch < evalHip.PunchPitch &&
                    evalZoom2.PunchYaw < evalZoom1.PunchYaw && evalZoom1.PunchYaw < evalHip.PunchYaw,
                $"{sn.Label} H/V recoil must decrease monotonically from Unscoped(90) -> SingleScope(40) -> DoubleScope(10).");

            // Mid-bolt chambering check (t + 250ms < NextAttackTimerMs):
            // 1) Manual ToggleScopeZoom must be blocked while chambering
            // 2) ShotsFired must NOT prematurely decay at 150ms
            // 3) PunchYaw / PunchPitch must smoothly decay (0 < midPunch < shotPunch) instead of jumping to BasePunch
            opState.ClientTimeMs += 250;
            SniperRifleCategoryModel.ToggleScopeZoom(cfg, ref wState, ref opState);
            Require(opState.Fov == 90 && opState.ResumeZoom,
                $"{sn.Label} manual ToggleScopeZoom must be locked while bolt cycle is incomplete.");
            engine.ProcessIdleFrame(ref wState, ref opState, attackHeld: false, secondaryAttackHeld: false);
            Require(wState.ShotsFired == 1 && opState.Fov == 90 && opState.ResumeZoom &&
                    wState.PunchPitch > 0f && wState.PunchPitch < evalZoom2.PunchPitch &&
                    wState.PunchYaw > 0f && wState.PunchYaw < evalZoom2.PunchYaw,
                $"{sn.Label} mid-bolt recovery (+250ms) must retain ShotsFired=1, keep scope closed, and decay H/V punch smoothly.");

            // Complete bolt cycle (advance past AttackIntervalMs): ResumeZoom restores Fov=10, ShotsFired=0, PunchYaw=0, PunchPitch=0, Spread=doubleZoomSpread
            opState.ClientTimeMs += cfg.AttackIntervalMs;
            engine.ProcessIdleFrame(ref wState, ref opState, attackHeld: false, secondaryAttackHeld: false);
            Require(opState.Fov == 10 && opState.IsAimingDownSights && !opState.ResumeZoom &&
                    wState.ShotsFired == 0 && !wState.DelayFire &&
                    wState.PunchYaw == 0f && wState.PunchPitch == 0f &&
                    Math.Abs(wState.Spread - doubleZoomSpread) < 1e-9,
                $"{sn.Label} post-bolt recovery must restore Fov=10, clear ShotsFired/PunchYaw/PunchPitch to 0, and restore double-zoom Spread.");

            // Rapid unrecovered follow-up check (dt = 180ms < AttackIntervalMs): ShotsFired=2, higher vertical kick, alternating horizontal yaw sign
            var rapidState = default(SimulatedWeaponAccuracyState);
            var rapidOp = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 5000);
            engine.SwitchWeapon(sn.Id, ref rapidState, ref rapidOp);
            var rapid1 = engine.FireWeapon(ref rapidState, ref rapidOp, explicitIntervalMs: cfg.AttackIntervalMs);
            var rapid2 = engine.FireWeapon(ref rapidState, ref rapidOp, explicitIntervalMs: 180);
            Require(rapid2.ShotsFired == 2 && rapid2.PunchPitch > rapid1.PunchPitch && rapid2.PunchYaw < 0f && rapid1.PunchYaw > 0f,
                $"{sn.Label} unrecovered rapid follow-up shot must increase vertical pitch kick and alternate horizontal yaw sign.");

            // 2c. Full 8-operational-state compensation run for this Sniper (including Fov=40 and Fov=10)
            (MovementStateKind MoveKind, int CustomFov, float MoveF, float MoveR, bool WalkMod, double Speed)[] sniperScenarios =
            {
                (MovementStateKind.Stand, 90, 0f, 0f, false, 0.0),
                (MovementStateKind.Walk, 90, 100f, 0f, true, 90.0),
                (MovementStateKind.Run, 90, 100f, 0f, false, 220.0),
                (MovementStateKind.Strafe, 90, 0f, -100f, false, 220.0),
                (MovementStateKind.MoveTurn, 90, 100f, 100f, false, 225.0),
                (MovementStateKind.JumpAir, 90, 100f, 0f, false, 210.0),
                (MovementStateKind.Crouch, 90, 0f, 0f, false, 0.0),
                (MovementStateKind.AimDownSights, 40, 0f, 0f, false, 0.0),
                (MovementStateKind.AimDownSights, 10, 0f, 0f, false, 0.0)
            };

            (double Yaw, double Pitch)[] angles = { (-135.0, -25.0), (0.0, 0.0), (65.0, 20.0), (175.0, -35.0) };
            int sniperShots = 0;
            int sniperPassed = 0;
            double maxContErr = 0.0;
            double maxQuantErr = 0.0;
            int seedBase = 50000 + sn.ExpectedIntervalMs;

            foreach (var sc in sniperScenarios)
            foreach (var (yaw, pitch) in angles)
            {
                var sw = default(SimulatedWeaponAccuracyState);
                var sop = SimulatedOperatorPostureState.FromMovementState(sc.MoveKind, 8000);
                engine.SwitchWeapon(sn.Id, ref sw, ref sop);
                sop = SimulatedOperatorPostureState.FromMovementState(sc.MoveKind, 8000);
                if (sc.Speed > 0.0) sop.HorizontalSpeed2D = sc.Speed;
                if (sc.MoveKind == MovementStateKind.AimDownSights)
                {
                    sop.Fov = sc.CustomFov;
                    sop.LastFov = sc.CustomFov;
                    sop.DelayFov = sc.CustomFov;
                    sop.IsAimingDownSights = true;
                }
                engine.SynchronizeState(ref sw, in sop);
                var ev = engine.FireWeapon(ref sw, ref sop, explicitIntervalMs: cfg.AttackIntervalMs);

                var actor = new SimulatedActorSnapshot
                {
                    EntityId = 1,
                    IsLocal = true,
                    IsAlive = true,
                    IsFirstPerson = true
                };
                WeaponCategorySimulationEngine.ApplyEvaluationToActor(in ev, in sop, actor);

                int seed = ++seedBase;
                if (sc.MoveKind == MovementStateKind.MoveTurn)
                {
                    var preWorld = new SimulatedWorldState
                    {
                        IsApplicationFocused = true,
                        LocalActor = actor,
                        CurrentCommand = new SimulatedUserCommandFrame
                        {
                            SequenceNumber = seed - 1,
                            CameraYaw16 = solver.EncodeAngle16(yaw - 5.0),
                            CameraPitch16 = solver.EncodeAngle16(pitch),
                            MoveForward = sc.MoveF,
                            MoveRight = sc.MoveR,
                            IsAttackOn = false
                        }
                    };
                    spreadModule.ExecuteTick(preWorld);
                }

                var cmd = new SimulatedUserCommandFrame
                {
                    SequenceNumber = seed,
                    CameraYaw16 = solver.EncodeAngle16(yaw),
                    CameraPitch16 = solver.EncodeAngle16(pitch),
                    MoveForward = sc.MoveF,
                    MoveRight = sc.MoveR,
                    IsWalkModifierOn = sc.WalkMod,
                    IsCrouchOn = sc.MoveKind == MovementStateKind.Crouch,
                    IsAimDownSightsOn = sc.MoveKind == MovementStateKind.AimDownSights,
                    IsAttackOn = true
                };
                var world = new SimulatedWorldState
                {
                    IsApplicationFocused = true,
                    LocalActor = actor,
                    CurrentCommand = cmd
                };
                spreadModule.ExecuteTick(world);

                var cluster = ShotgunCategoryModel.EvaluatePelletCluster(
                    in ev,
                    seed,
                    yaw,
                    pitch,
                    cancelRecoilPunch: true,
                    dirModel,
                    solver);

                sniperShots++;
                if (cmd.WasCompensated && cluster.CompensatedPrimaryQuantizedErrorDegrees <= 0.01 &&
                    spreadModule.LastWeaponCategory == WeaponCategory.SniperRifle &&
                    spreadModule.LastFormulaKind == WeaponAccuracyFormulaKind.SniperScopedPosture)
                {
                    sniperPassed++;
                }
                if (cluster.CompensatedPrimaryContinuousErrorDegrees > maxContErr)
                    maxContErr = cluster.CompensatedPrimaryContinuousErrorDegrees;
                if (cluster.CompensatedPrimaryQuantizedErrorDegrees > maxQuantErr)
                    maxQuantErr = cluster.CompensatedPrimaryQuantizedErrorDegrees;
            }

            Require(sniperPassed == sniperShots, $"{sn.Label} failed compensation on {sniperShots - sniperPassed}/{sniperShots} shots.");
            sniperSummaryLines.Add(
                $"   [SNIPER PASS] {sn.Label,-7} | Shots={sniperPassed}/{sniperShots} | Interval={cfg.AttackIntervalMs}ms | HipSpread={hipSpread:F4} | Fov40={cfg.StandSpreadParam:F4} | Fov10={doubleZoomSpread:F6} | Kick(Pitch/Yaw)={evalHip.PunchPitch:F2}°/{evalHip.PunchYaw:F2}° | ContMax={maxContErr:F6}° | QuantMax={maxQuantErr:F6}°");
        }

        // Verify inter-sniper ballistic hierarchy: Scout < AWP < Barrett for both vertical and horizontal recoil
        Require(unscopedPitchBySniper["scout"] < unscopedPitchBySniper["awp"] &&
                unscopedPitchBySniper["awp"] < unscopedPitchBySniper["barrett"] &&
                unscopedYawBySniper["scout"] < unscopedYawBySniper["awp"] &&
                unscopedYawBySniper["awp"] < unscopedYawBySniper["barrett"],
            "Sniper recoil hierarchy must satisfy Scout < AWP < Barrett on both Pitch and Yaw.");

        // 3. Cross-weapon switch state integrity + partial adapter snapshot rehydration check
        var switchActor = new SimulatedActorSnapshot { EntityId = 1, IsLocal = true, IsAlive = true, IsFirstPerson = true };
        var swState = default(SimulatedWeaponAccuracyState);
        var swOp = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 10000);

        // Spray 6 shots with AK47 -> switch to Barrett -> verify zero AK47 leak -> zoom to 10 & fire Barrett -> switch to M4A1 -> verify zero Sniper Fov/ResumeZoom leak -> switch to Scout
        engine.SwitchWeapon("ak47", ref swState, ref swOp);
        for (int i = 0; i < 6; i++)
            _ = engine.FireWeapon(ref swState, ref swOp, explicitIntervalMs: 100);
        Require(swState.ShotsFired == 6 && Math.Abs(swState.SpreadScaleY - 0.40f) < 1e-6f, "AK47 burst pre-condition failed.");

        engine.SwitchWeapon("barrett", ref swState, ref swOp);
        WeaponCategorySimulationEngine.ApplyStateToActor(engine.GetConfiguration("barrett"), in swState, in swOp, switchActor);
        Require(swState.ShotsFired == 0 && swState.PunchYaw == 0f && swState.PunchPitch == 0f &&
                Math.Abs(swState.SpreadScaleY - 1.00f) < 1e-6f && swOp.Fov == 90 && !swOp.ResumeZoom &&
                switchActor.CurrentWeapon!.ClipAmmo == 10 && Math.Abs(switchActor.CurrentWeapon.SpreadScaleY - 1.00f) < 1e-6f,
            "Switching AK47 -> Barrett must cleanly reset ShotsFired/Punch and restore SpreadScaleY=1.0, ClipAmmo=10.");

        SniperRifleCategoryModel.ToggleScopeZoom(engine.GetConfiguration("barrett"), ref swState, ref swOp);
        SniperRifleCategoryModel.ToggleScopeZoom(engine.GetConfiguration("barrett"), ref swState, ref swOp);
        _ = engine.FireWeapon(ref swState, ref swOp, explicitIntervalMs: 1450);
        Require(swOp.ResumeZoom && swOp.LastFov == 10, "Barrett post-shot ResumeZoom pre-condition failed.");

        engine.SwitchWeapon("m4a1", ref swState, ref swOp);
        WeaponCategorySimulationEngine.ApplyStateToActor(engine.GetConfiguration("m4a1"), in swState, in swOp, switchActor);
        Require(swOp.Fov == 90 && swOp.LastFov == 90 && !swOp.ResumeZoom && !swOp.IsAimingDownSights &&
                swState.ShotsFired == 0 && Math.Abs(swState.Accuracy - 0.20) < 1e-6 &&
                Math.Abs(swState.SpreadScaleY - 0.40f) < 1e-6f && switchActor.CurrentWeapon!.ClipAmmo == 30,
            "Switching Barrett -> M4A1 must clear ResumeZoom/LastFov and restore M4A1 DefaultAccuracy=0.20, SpreadScaleY=0.40, ClipAmmo=30.");

        // Partial adapter snapshot test: SimulatedWeaponSnapshot still carries default AK47 fields (Category=AssaultRifle, SpreadScaleY=0.4f, ShotsFiredMultiplier=1.35, Spread=0.004)
        // when WeaponName changes to "Scout" -> RuntimeWeaponParameterResolver + SpreadCompensationModule must restore Sniper parameters!
        var partialAdapterActor = new SimulatedActorSnapshot
        {
            EntityId = 1,
            IsLocal = true,
            IsAlive = true,
            IsFirstPerson = true,
            IsOnGround = true,
            Fov = 90,
            PunchYaw = 0.10f,
            PunchPitch = 1.45f,
            CurrentWeapon = new SimulatedWeaponSnapshot
            {
                WeaponName = "Scout"
                // Intentionally leaves Category=AssaultRifle, Spread=0.004, SpreadScaleY=0.4f, ShotsFiredMultiplier=1.35
            }
        };
        Require(resolver.TryResolveWeapon(partialAdapterActor, partialAdapterActor.CurrentWeapon!, MovementStateKind.Stand, 61001, out var hydratedScout),
            "Resolver must resolve partial 'Scout' snapshot.");
        Require(hydratedScout.Category == WeaponCategory.SniperRifle &&
                hydratedScout.FormulaKind == WeaponAccuracyFormulaKind.SniperScopedPosture &&
                Math.Abs(hydratedScout.SpreadScaleY - 1.00f) < 1e-6f &&
                Math.Abs(hydratedScout.ShotsFiredMultiplier - 1.00) < 1e-6 &&
                Math.Abs(hydratedScout.Spread - (0.0014 + 0.0450)) < 1e-9,
            "RuntimeWeaponParameterResolver must rehydrate lost Sniper SpreadScaleY=1.0, ShotsFiredMultiplier=1.0, and UnscopedExtraSpread when switching from AR snapshot.");

        var partialCmd = new SimulatedUserCommandFrame
        {
            SequenceNumber = 61001,
            CameraYaw16 = solver.EncodeAngle16(15.0),
            CameraPitch16 = solver.EncodeAngle16(-8.0),
            IsAttackOn = true
        };
        spreadModule.ExecuteTick(new SimulatedWorldState
        {
            IsApplicationFocused = true,
            LocalActor = partialAdapterActor,
            CurrentCommand = partialCmd
        });
        Vector3D scoutTarget = dirModel.CalculateDirection(61001, 15.0, -8.0, 0.0, 1.0f, 1.0);
        Vector3D scoutActual = dirModel.CalculateDirection(
            61001,
            partialCmd.CameraYaw16 / 100.0 + 0.10 * 2.0,
            partialCmd.CameraPitch16 / 100.0 + 1.45 * 2.0,
            0.0014 + 0.0450,
            1.0f,
            1.0);
        double hydratedErr = solver.ComputeAngularErrorDegrees(scoutActual, scoutTarget);
        Require(partialCmd.WasCompensated && hydratedErr <= 0.01,
            $"SpreadCompensationModule failed to propagate hydrated Scout Sniper parameters: err={hydratedErr:F6}");

        // 4. Strict AK47 & M4A1 Regression Verification (Preserved RifleCubic formula + Legacy 378-shot baseline + 8-state matrix)
        var akCfg = engine.GetConfiguration("ak47");
        var m4Cfg = engine.GetConfiguration("m4a1");
        Require(akCfg.FormulaKind == WeaponAccuracyFormulaKind.RifleCubic &&
                Math.Abs(akCfg.DefaultAccuracy - 0.20) < 1e-9 &&
                Math.Abs(akCfg.AccuracyDivisor - 200.0) < 1e-9 &&
                Math.Abs(akCfg.AccuracyOffset - 0.20) < 1e-9 &&
                Math.Abs(akCfg.MaxInaccuracy - 1.25) < 1e-9 &&
                Math.Abs(akCfg.SpreadScaleY - 0.40f) < 1e-6f &&
                Math.Abs(akCfg.AdsSpreadMultiplier - 0.75) < 1e-9,
            "AK47 RifleCubic configuration parameters regressed!");

        Require(m4Cfg.FormulaKind == WeaponAccuracyFormulaKind.RifleCubic &&
                Math.Abs(m4Cfg.DefaultAccuracy - 0.20) < 1e-9 &&
                Math.Abs(m4Cfg.AccuracyDivisor - 220.0) < 1e-9 &&
                Math.Abs(m4Cfg.AccuracyOffset - 0.15) < 1e-9 &&
                Math.Abs(m4Cfg.MaxInaccuracy - 1.00) < 1e-9 &&
                Math.Abs(m4Cfg.SpreadScaleY - 0.40f) < 1e-6f &&
                Math.Abs(m4Cfg.AdsSpreadMultiplier - 0.65) < 1e-9,
            "M4A1 RifleCubic configuration parameters regressed!");

        var akState = default(SimulatedWeaponAccuracyState);
        var akOp = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("ak47", ref akState, ref akOp);
        for (int s = 1; s <= 15; s++)
        {
            var ev = engine.FireWeapon(ref akState, ref akOp, explicitIntervalMs: 100);
            double expectedAcc = Math.Clamp((s * s * s) / 200.0 + 0.20, 0.20, 1.25);
            double expectedSpread = 0.020 * expectedAcc;
            Require(Math.Abs(ev.Accuracy - expectedAcc) < 1e-9 && Math.Abs(ev.Spread - expectedSpread) < 1e-9,
                $"AK47 shot #{s} cubic formula mismatch!");
        }

        var m4State = default(SimulatedWeaponAccuracyState);
        var m4Op = SimulatedOperatorPostureState.FromMovementState(MovementStateKind.Stand, 1000);
        engine.SwitchWeapon("m4a1", ref m4State, ref m4Op);
        for (int s = 1; s <= 15; s++)
        {
            var ev = engine.FireWeapon(ref m4State, ref m4Op, explicitIntervalMs: 90);
            double expectedAcc = Math.Clamp((s * s * s) / 220.0 + 0.15, 0.20, 1.00);
            double expectedSpread = 0.018 * expectedAcc;
            Require(Math.Abs(ev.Accuracy - expectedAcc) < 1e-9 && Math.Abs(ev.Spread - expectedSpread) < 1e-9,
                $"M4A1 shot #{s} cubic formula mismatch!");
        }

        var legacyAkReport = SimulatedWorldScenarioRunner.RunFullVerificationSuite();
        Require(legacyAkReport.BackwardCompatibilityVerified &&
                legacyAkReport.TotalShotsSimulated == 378 &&
                legacyAkReport.CompensatedShots == 378 &&
                legacyAkReport.SkippedShots == 0,
            "AK47_Sim 378-shot baseline regressed!");

        foreach (string line in sniperSummaryLines)
            Console.WriteLine(line);
        Console.WriteLine($"   [AR REGRESSION PASS] AK47_Sim Baseline={legacyAkReport.CompensatedShots}/{legacyAkReport.TotalShotsSimulated} | AK47 Cubic(15 shots)=EXACT | M4A1 Cubic(15 shots)=EXACT");
    }

    private sealed class DisposableProbe : IDisposable
    {
        private readonly List<string> _order;
        public DisposableProbe(string name, List<string> order) { Name = name; _order = order; }
        public string Name { get; }
        public void Dispose() => _order.Add(Name);
    }

    private sealed class SecondDisposableProbe : IDisposable
    {
        private readonly List<string> _order;
        public SecondDisposableProbe(string name, List<string> order) { Name = name; _order = order; }
        public string Name { get; }
        public void Dispose() => _order.Add(Name);
    }

    private sealed class TransientProbe { }
    private sealed class CycleA { public CycleA(CycleB _) { } }
    private sealed class CycleB { public CycleB(CycleA _) { } }

    private sealed class FaultInjectingModule : IEngineModule
    {
        public string ModuleId => "test.faulty";
        public string DisplayName => "Fault Injecting Module";
        public ModuleLifecycleState State { get; private set; } = ModuleLifecycleState.Uninitialized;
        public string StatusSummary { get; private set; } = "Init";
        public string? LastError { get; private set; }
        public int TickCallCount { get; private set; }
        public int ResetCallCount { get; private set; }

        public void Initialize() => State = ModuleLifecycleState.Idle;
        public void Configure(IEngineProfile profile) => State = ModuleLifecycleState.Active;
        public void ExecuteTick(IWorldStateProvider world)
        {
            TickCallCount++;
            LastError = "Simulated module exception";
            throw new InvalidOperationException(LastError);
        }
        public void Reset()
        {
            ResetCallCount++;
            State = ModuleLifecycleState.Faulted;
            StatusSummary = "Isolated after fault";
        }
        public void Dispose() => State = ModuleLifecycleState.Disposed;
    }
}
