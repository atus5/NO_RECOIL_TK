using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;
using HackTK.Next.Core;
using HackTK.Next.Diagnostics;
using HackTK.Next.Modules;
using HackTK.Next.Services;

namespace HackTK.Next.Simulation;

public static class EngineModuleLoader
{
    public static readonly IReadOnlyList<string> RequiredAssemblies = new[]
    {
        "HackTK.Next.Abstractions",
        "HackTK.Next.Configuration",
        "HackTK.Next.Diagnostics",
        "HackTK.Next.Services",
        "HackTK.Next.Core",
        "HackTK.Next.Modules",
        "HackTK.Next.Simulation"
    };

    public static bool VerifyAndLoadAssemblies(
        out IReadOnlyList<string> loadedAssemblies,
        out IReadOnlyList<string> failedAssemblies)
    {
        var loaded = new List<string>();
        var failed = new List<string>();

        foreach (string assemblyName in RequiredAssemblies)
        {
            try
            {
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
                var asm = existing ?? Assembly.Load(new AssemblyName(assemblyName));
                if (asm is not null)
                    loaded.Add($"{asm.GetName().Name} ({asm.GetName().Version})");
                else
                    failed.Add(assemblyName);
            }
            catch (Exception ex)
            {
                failed.Add($"{assemblyName}: {ex.Message}");
            }
        }

        loadedAssemblies = loaded;
        failedAssemblies = failed;
        return failed.Count == 0;
    }

    public static ServiceContainer CreateRuntimeServiceContainer(
        ITimeProvider? timeProvider = null,
        SimulatedWorldState? worldState = null,
        string? sessionToken = null)
    {
        var container = new ServiceContainer();
        RegisterRuntimeServices(container, timeProvider, worldState, sessionToken);
        return container;
    }

    public static void RegisterRuntimeServices(
        IServiceRegistry container,
        ITimeProvider? timeProvider = null,
        SimulatedWorldState? worldState = null,
        string? sessionToken = null)
    {
        ArgumentNullException.ThrowIfNull(container);

        container.RegisterSingleton<ITimeProvider>(timeProvider ?? new SystemTimeProvider());
        container.RegisterSingleton<IDiagnosticLogger>(r => new StructuredDiagnosticLogger(r.Resolve<ITimeProvider>()));
        container.RegisterSingleton<IConfigurationStore<EngineProfile>>(_ => new AtomicXmlConfigurationStore<EngineProfile>());
        container.RegisterSingleton<IConfigurationStore<SessionCommandEnvelope>>(_ => new AtomicXmlConfigurationStore<SessionCommandEnvelope>());
        container.RegisterSingleton<IConfigurationStore<RuntimeSessionStatus>>(_ => new AtomicXmlConfigurationStore<RuntimeSessionStatus>());

        var transformer = new SsjjCoordinateTransformer();
        container.RegisterSingleton<ICoordinateTransformer>(transformer);
        container.RegisterSingleton<IBallisticDirectionModel>(_ => new TriangularSpreadDirectionModel());
        container.RegisterSingleton<ISpreadCompensationSolver>(_ => new NewtonSpreadSolverService());
        container.RegisterSingleton<IAimTrajectoryService>(_ => new SubpixelAimComputationService());

        container.RegisterSingleton<IWeaponRegistry>(_ => WeaponRegistry.CreateDefault());
        container.RegisterSingleton<WeaponCategorySimulationEngine>(r => new WeaponCategorySimulationEngine(r.Resolve<IWeaponRegistry>()));
        container.RegisterSingleton<IWeaponParameterResolver>(r =>
            new RuntimeWeaponParameterResolver(
                r.Resolve<IWeaponRegistry>(),
                r.Resolve<WeaponCategorySimulationEngine>()));

        var (defaultWorld, raycaster) = CreateDefaultTestWorld(transformer);
        var world = worldState ?? defaultWorld;
        var inputDispatcher = new RecordingInputDispatcher();
        var overlayContext = new RecordingOverlayContext();
        var materialController = new SimulatedMaterialSlotController();

        container.RegisterSingleton<SimulatedWorldState>(world);
        container.RegisterSingleton<IWorldStateProvider>(world);
        container.RegisterSingleton<RecordingInputDispatcher>(inputDispatcher);
        container.RegisterSingleton<IInputDispatcher>(inputDispatcher);
        container.RegisterSingleton<RecordingOverlayContext>(overlayContext);
        container.RegisterSingleton<IRenderOverlayContext>(overlayContext);
        container.RegisterSingleton<SimulatedMaterialSlotController>(materialController);
        container.RegisterSingleton<IMaterialSlotController>(materialController);
        container.RegisterSingleton<SimulatedPhysicsRaycaster>(raycaster);
        container.RegisterSingleton<IPhysicsRaycaster>(raycaster);

        container.RegisterSingleton<IVisibilityEvaluationService>(r =>
            new DualOriginVisibilityService(
                r.Resolve<IPhysicsRaycaster>(),
                r.Resolve<ICoordinateTransformer>()));

        container.RegisterSingleton<TargetAcquisitionModule>(r =>
            new TargetAcquisitionModule(
                r.Resolve<IAimTrajectoryService>(),
                r.Resolve<IVisibilityEvaluationService>(),
                r.Resolve<IInputDispatcher>(),
                r.Resolve<IDiagnosticLogger>()));

        container.RegisterSingleton<SpreadCompensationModule>(r =>
            new SpreadCompensationModule(
                r.Resolve<ISpreadCompensationSolver>(),
                r.Resolve<IBallisticDirectionModel>(),
                r.Resolve<IDiagnosticLogger>(),
                r.Resolve<IWeaponParameterResolver>()));

        container.RegisterSingleton<EspProjectionModule>(r =>
            new EspProjectionModule(
                r.Resolve<IRenderOverlayContext>(),
                r.Resolve<IAimTrajectoryService>()));

        container.RegisterSingleton<MaterialSlotModule>(r =>
            new MaterialSlotModule(r.Resolve<IMaterialSlotController>()));

        container.RegisterSingleton<MovementPulseModule>(r =>
            new MovementPulseModule(
                r.Resolve<ITimeProvider>(),
                r.Resolve<IInputDispatcher>()));

        container.RegisterSingleton<IModuleManager>(r =>
        {
            var manager = new ModuleManager(r.Resolve<IDiagnosticLogger>());
            manager.Register(r.Resolve<TargetAcquisitionModule>());
            manager.Register(r.Resolve<SpreadCompensationModule>());
            manager.Register(r.Resolve<EspProjectionModule>());
            manager.Register(r.Resolve<MaterialSlotModule>());
            manager.Register(r.Resolve<MovementPulseModule>());
            return manager;
        });

        container.RegisterSingleton<SessionLeaseManager>(r => new SessionLeaseManager(r.Resolve<ITimeProvider>()));
        container.RegisterSingleton<EngineHost>(r =>
        {
            var host = new EngineHost(
                r.Resolve<SessionLeaseManager>(),
                r.Resolve<IModuleManager>(),
                r.Resolve<IDiagnosticLogger>(),
                sessionToken);
            host.Initialize();
            return host;
        });
    }

    public static (SimulatedWorldState World, SimulatedPhysicsRaycaster Raycaster) CreateDefaultTestWorld(ICoordinateTransformer transformer)
    {
        var world = new SimulatedWorldState
        {
            IsApplicationFocused = true,
            ScreenWidth = 1920,
            ScreenHeight = 1080,
            IsJumpKeyPressed = true
        };
        world.SetTriggerPressed(TriggerKey.LeftAlt, true);
        world.SetTriggerPressed(TriggerKey.Mouse0, true);
        world.SetTriggerPressed(TriggerKey.Mouse1, true);

        var local = new SimulatedActorSnapshot
        {
            EntityId = 1,
            Name = "LocalOperator",
            TeamId = 1,
            IsLocal = true,
            IsAlive = true,
            IsFirstPerson = true,
            IsOnGround = true,
            EyePositionUnity = new Vector3D(0.0, 1.7, 0.0),
            CompensatedShotOriginSsjj = transformer.UnityToSsjj(new Vector3D(0.0, 1.7, 0.0)),
            PunchYaw = 0.40f,
            PunchPitch = 1.15f,
            CurrentWeapon = new SimulatedWeaponSnapshot()
        };

        // Place enemy in front along Unity +Z so ProjectWorldToScreen projects within FOV near center of 1920x1080
        var headUnity = new Vector3D(0.04, 1.54, 15.0);
        var spineUnity = new Vector3D(0.04, 1.25, 15.0);
        var rootUnity = new Vector3D(0.04, 0.90, 15.0);

        var enemy = new SimulatedActorSnapshot
        {
            EntityId = 2,
            Name = "TargetDummy_Alpha",
            TeamId = 2,
            IsLocal = false,
            IsAlive = true,
            HealthPercent = 0.85f,
            EyePositionUnity = headUnity
        };
        enemy.BonesUnity["Bip01"] = rootUnity;
        enemy.BonesUnity["Bip01_Spine"] = spineUnity;
        enemy.BonesUnity["Bip01_Head"] = headUnity;
        enemy.SkeletonSegments.Add(("Bip01_Head", "Bip01_Spine", headUnity, spineUnity));
        enemy.SkeletonSegments.Add(("Bip01_Spine", "Bip01", spineUnity, rootUnity));

        world.LocalActor = local;
        world.ActorList.Add(local);
        world.ActorList.Add(enemy);

        var raycaster = new SimulatedPhysicsRaycaster { IsReady = true };
        raycaster.Colliders.Add(new SimulatedSphereCollider(
            EntityId: 2,
            BoneName: "Bip01_Head",
            CenterSsjj: transformer.UnityToSsjj(headUnity),
            Radius: 0.35,
            IsBroadBodyCollider: false));
        raycaster.Colliders.Add(new SimulatedSphereCollider(
            EntityId: 2,
            BoneName: "Bip01_Spine",
            CenterSsjj: transformer.UnityToSsjj(spineUnity),
            Radius: 0.45,
            IsBroadBodyCollider: false));

        return (world, raycaster);
    }
}

public sealed class AuthorizedTestRuntimeHost : IDisposable
{
    public static readonly IReadOnlyList<(string GroupKey, string WeaponId, string EngineKey)> CanonicalWeaponGroups =
        new (string, string, string)[]
        {
            ("AK47", "AK47_Sim", "ak47"),
            ("M4A1", "M4A1_Sim", "m4a1"),
            ("SMG", "MP5_Sim", "mp5"),
            ("LMG", "M249_Sim", "m249"),
            ("Pistol", "Deagle_Sim", "deagle"),
            ("Sniper", "AWP_Sim", "awp"),
            ("Shotgun", "XM1014_Sim", "xm1014"),
            ("Custom", "CustomPulse_Sim", "custompulse")
        };

    public static readonly IReadOnlyList<MovementStateKind> AllOperationalStates = new[]
    {
        MovementStateKind.Stand,
        MovementStateKind.Walk,
        MovementStateKind.Run,
        MovementStateKind.Strafe,
        MovementStateKind.MoveTurn,
        MovementStateKind.JumpAir,
        MovementStateKind.Crouch,
        MovementStateKind.AimDownSights
    };

    private readonly ServiceContainer _container;
    private readonly ITimeProvider _timeProvider;
    private readonly IDiagnosticLogger _logger;
    private readonly IConfigurationStore<SessionCommandEnvelope> _commandStore;
    private readonly IConfigurationStore<RuntimeSessionStatus> _statusStore;
    private readonly Dictionary<string, int> _compensatedByWeaponGroup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MovementStateKind, int> _compensatedByMoveState = new();
    private int _flushedEventCount;
    private int _tickCounter;
    private bool _disposed;

    public AuthorizedTestRuntimeHost(
        int? processId = null,
        long? processStartUtcTicks = null,
        ITimeProvider? timeProvider = null,
        string? sessionToken = null)
    {
        using var currentProc = Process.GetCurrentProcess();
        ProcessId = processId ?? currentProc.Id;
        ProcessStartUtcTicks = processStartUtcTicks ?? currentProc.StartTime.ToUniversalTime().Ticks;

        _container = EngineModuleLoader.CreateRuntimeServiceContainer(timeProvider, sessionToken: sessionToken);
        _timeProvider = _container.Resolve<ITimeProvider>();
        _logger = _container.Resolve<IDiagnosticLogger>();
        _commandStore = _container.Resolve<IConfigurationStore<SessionCommandEnvelope>>();
        _statusStore = _container.Resolve<IConfigurationStore<RuntimeSessionStatus>>();

        WorldState = _container.Resolve<SimulatedWorldState>();
        InputDispatcher = _container.Resolve<RecordingInputDispatcher>();
        OverlayContext = _container.Resolve<RecordingOverlayContext>();
        MaterialController = _container.Resolve<SimulatedMaterialSlotController>();
        WeaponRegistry = _container.Resolve<IWeaponRegistry>();
        WeaponCategoryEngine = _container.Resolve<WeaponCategorySimulationEngine>();
        ModuleManager = _container.Resolve<IModuleManager>();
        EngineHost = _container.Resolve<EngineHost>();

        AimModule = _container.Resolve<TargetAcquisitionModule>();
        SpreadModule = _container.Resolve<SpreadCompensationModule>();
        EspModule = _container.Resolve<EspProjectionModule>();
        MaterialModule = _container.Resolve<MaterialSlotModule>();
        MovementModule = _container.Resolve<MovementPulseModule>();

        foreach (var group in CanonicalWeaponGroups)
            _compensatedByWeaponGroup[group.GroupKey] = 0;
        foreach (var state in AllOperationalStates)
            _compensatedByMoveState[state] = 0;

        Directory.CreateDirectory(SessionDirectory);
        WriteStatusSnapshot(string.Empty, string.Empty);
    }

    public int ProcessId { get; }
    public long ProcessStartUtcTicks { get; }
    public string SessionToken => EngineHost.SessionToken;
    public string SessionDirectory => RuntimeSessionPaths.ForProcess(ProcessId);
    public string StatusFilePath => RuntimeSessionPaths.StatusFile(ProcessId);
    public string CommandFilePath => RuntimeSessionPaths.CommandFile(ProcessId);
    public string RuntimeEventLogPath => Path.Combine(SessionDirectory, "runtime-events.log");

    public SimulatedWorldState WorldState { get; }
    public RecordingInputDispatcher InputDispatcher { get; }
    public RecordingOverlayContext OverlayContext { get; }
    public SimulatedMaterialSlotController MaterialController { get; }
    public IWeaponRegistry WeaponRegistry { get; }
    public WeaponCategorySimulationEngine WeaponCategoryEngine { get; }
    public IModuleManager ModuleManager { get; }
    public EngineHost EngineHost { get; }

    public TargetAcquisitionModule AimModule { get; }
    public SpreadCompensationModule SpreadModule { get; }
    public EspProjectionModule EspModule { get; }
    public MaterialSlotModule MaterialModule { get; }
    public MovementPulseModule MovementModule { get; }

    public int TickCount => _tickCounter;
    public int TotalOverlayDrawCalls =>
        OverlayContext.BoxesDrawn + OverlayContext.LinesDrawn + OverlayContext.TextsDrawn +
        OverlayContext.HealthBarsDrawn + OverlayContext.CrosshairsDrawn + OverlayContext.CirclesDrawn;
    public IReadOnlyDictionary<string, int> CompensatedShotsByWeaponGroup => _compensatedByWeaponGroup;
    public IReadOnlyDictionary<MovementStateKind, int> CompensatedShotsByMovementState => _compensatedByMoveState;

    public RuntimeSessionStatus StepTick(
        string? forcedWeaponGroup = null,
        MovementStateKind? forcedMoveState = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AuthorizedTestRuntimeHost));

        _tickCounter++;
        if (_timeProvider is DeterministicTimeProvider detTime)
            detTime.AdvanceMilliseconds(40);

        SessionCommandEnvelope? envelope = null;
        string transportError = string.Empty;
        string commandError = string.Empty;

        if (File.Exists(CommandFilePath))
        {
            try
            {
                envelope = _commandStore.Load(CommandFilePath);
            }
            catch (Exception ex)
            {
                transportError = "Lỗi đọc command.xml: " + ex.Message;
                _logger.Record("RUNTIME_COMMAND_READ_ERROR", transportError);
            }
        }

        var groupEntry = SelectWeaponGroup(_tickCounter, forcedWeaponGroup);
        var moveState = forcedMoveState ?? AllOperationalStates[(_tickCounter - 1) % AllOperationalStates.Count];
        int burstIndex = ((_tickCounter - 1) % 4) + 1;

        PrepareCombatFrame(groupEntry, moveState, burstIndex, _tickCounter);

        long prevCompensated = SpreadModule.CompensatedCount;
        EngineHost.ProcessFrame(envelope, WorldState);

        if (!EngineHost.IsConnected && envelope is not null && !string.IsNullOrEmpty(SessionLeaseRejection()))
        {
            commandError = SessionLeaseRejection();
        }

        if (SpreadModule.CompensatedCount > prevCompensated && WorldState.CurrentCommand?.WasCompensated == true)
        {
            _compensatedByWeaponGroup[groupEntry.GroupKey] =
                _compensatedByWeaponGroup.GetValueOrDefault(groupEntry.GroupKey) + 1;
            _compensatedByMoveState[moveState] =
                _compensatedByMoveState.GetValueOrDefault(moveState) + 1;
        }

        var status = WriteStatusSnapshot(transportError, commandError);
        FlushDiagnosticsToDisk();
        return status;
    }

    private string SessionLeaseRejection() =>
        _container.Resolve<SessionLeaseManager>().LastRejectionReason;

    private static (string GroupKey, string WeaponId, string EngineKey) SelectWeaponGroup(int tick, string? forcedGroup)
    {
        if (!string.IsNullOrWhiteSpace(forcedGroup))
        {
            foreach (var g in CanonicalWeaponGroups)
            {
                if (string.Equals(g.GroupKey, forcedGroup, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(g.WeaponId, forcedGroup, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(g.EngineKey, forcedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    return g;
                }
            }
        }

        return CanonicalWeaponGroups[(tick - 1) % CanonicalWeaponGroups.Count];
    }

    private void PrepareCombatFrame(
        (string GroupKey, string WeaponId, string EngineKey) group,
        MovementStateKind moveState,
        int shotsFired,
        int sequenceNumber)
    {
        if (WorldState.LocalActor is not SimulatedActorSnapshot local)
            return;

        WeaponRegistry.ApplyToActor(local, group.WeaponId, moveState);

        var config = WeaponCategoryEngine.GetConfiguration(group.EngineKey);
        var model = WeaponCategoryEngine.GetModel(config.Category);

        var weaponState = new SimulatedWeaponAccuracyState();
        var opState = SimulatedOperatorPostureState.FromMovementState(moveState, _timeProvider.MonotonicMilliseconds);
        model.Deploy(config, ref weaponState, ref opState);
        opState = SimulatedOperatorPostureState.FromMovementState(moveState, _timeProvider.MonotonicMilliseconds);
        weaponState.ShotsFired = Math.Max(0, shotsFired - 1);
        if (config.Category == WeaponCategory.SniperRifle && moveState == MovementStateKind.AimDownSights)
        {
            SniperRifleCategoryModel.ToggleScopeZoom(config, ref weaponState, ref opState);
        }

        var eval = model.EvaluateFire(config, ref weaponState, ref opState, timeSinceLastShotMs: 110);
        WeaponCategorySimulationEngine.ApplyEvaluationToActor(in eval, in opState, local);
        if (local.CurrentWeapon is SimulatedWeaponSnapshot simWeapon)
        {
            simWeapon.WeaponName = group.WeaponId;
            if (WeaponRegistry.TryGetProfile(group.WeaponId, out var profile) && profile is not null)
                simWeapon.PelletCount = profile.PelletCount;
        }

        float baseYaw = (sequenceNumber % 12) * 3.0f - 15.0f;
        float basePitch = (sequenceNumber % 5) * 1.5f - 3.0f;
        short yaw16 = (short)Math.Round(baseYaw * 100f);
        short pitch16 = (short)Math.Round(basePitch * 100f);

        if (moveState == MovementStateKind.MoveTurn)
        {
            yaw16 = (short)(yaw16 + 450);
            pitch16 = (short)(pitch16 + 150);
        }

        WorldState.CurrentCommand = new SimulatedUserCommandFrame
        {
            SequenceNumber = 1000 + sequenceNumber,
            RandomSeed = 1000 + sequenceNumber,
            CameraYaw16 = yaw16,
            CameraPitch16 = pitch16,
            IsAttackOn = true,
            IsWalkModifierOn = moveState == MovementStateKind.Walk,
            IsCrouchOn = moveState == MovementStateKind.Crouch,
            IsAimDownSightsOn = moveState == MovementStateKind.AimDownSights,
            MoveForward = moveState is MovementStateKind.Walk or MovementStateKind.Run or MovementStateKind.MoveTurn ? 1.0f : 0.0f,
            MoveRight = moveState == MovementStateKind.Strafe ? 1.0f : 0.0f
        };

        WorldState.IsJumpKeyPressed = true;
    }

    private RuntimeSessionStatus WriteStatusSnapshot(string transportError, string commandError)
    {
        var moduleSnapshots = EngineHost.GetModuleStatuses();
        var features = new List<RuntimeFeatureState>(moduleSnapshots.Count + 5);

        foreach (var mod in moduleSnapshots)
        {
            features.Add(new RuntimeFeatureState
            {
                Feature = $"{mod.DisplayName} [{mod.ModuleId}]",
                State = mod.LifecycleState.ToString(),
                Detail = string.IsNullOrEmpty(mod.LastError)
                    ? mod.Summary
                    : $"{mod.Summary} | Lỗi: {mod.LastError}"
            });
        }

        features.Add(new RuntimeFeatureState
        {
            Feature = "No recoil / No spread",
            State = SpreadModule.State.ToString(),
            Detail = $"Đã bù={SpreadModule.CompensatedCount}, Bỏ qua={SpreadModule.SkippedCount}, Vũ khí={SpreadModule.LastResolvedWeaponId} ({SpreadModule.LastWeaponCategory}/{SpreadModule.LastMovementState}), Sai số lượng tử={SpreadModule.LastQuantizedErrorDegrees:F5}°"
        });
        features.Add(new RuntimeFeatureState
        {
            Feature = "Aimbot + FOV + Smooth",
            State = AimModule.State.ToString(),
            Detail = $"{AimModule.StatusSummary} (Tổng bước chuột={InputDispatcher.DispatchedMouseMoves.Count})"
        });
        features.Add(new RuntimeFeatureState
        {
            Feature = "ESP hộp / xương / tên / máu / snaplines",
            State = EspModule.State.ToString(),
            Detail = $"{EspModule.StatusSummary} (Lệnh vẽ overlay={TotalOverlayDrawCalls})"
        });
        features.Add(new RuntimeFeatureState
        {
            Feature = "Chams",
            State = MaterialModule.State.ToString(),
            Detail = $"{MaterialModule.StatusSummary} (Slot đang ghi đè={MaterialController.ActiveReplacementCount})"
        });
        features.Add(new RuntimeFeatureState
        {
            Feature = "Bhop",
            State = MovementModule.State.ToString(),
            Detail = $"{MovementModule.StatusSummary} (Xung nhảy={InputDispatcher.DispatchedJumpStates.Count})"
        });

        var status = new RuntimeSessionStatus
        {
            Protocol = NextWireProtocol.CurrentVersion,
            ProcessId = ProcessId,
            ProcessStartUtcTicks = ProcessStartUtcTicks,
            Session = EngineHost.SessionToken,
            MainThreadUtcTicks = _timeProvider.UtcNow.UtcTicks,
            AppliedRevision = EngineHost.AppliedRevision,
            Connected = EngineHost.IsConnected,
            GameDirectory = AppContext.BaseDirectory,
            UnityVersion = "AuthorizedTestRuntime-10.0",
            State = EngineHost.HostState,
            Error = string.Empty,
            TransportError = transportError,
            CommandError = commandError,
            PlayerCount = WorldState.Actors.Count,
            Features = features.ToArray()
        };

        _statusStore.Save(StatusFilePath, status);
        return status;
    }

    private void FlushDiagnosticsToDisk()
    {
        var events = _logger.GetRecentEvents();
        if (events.Count <= _flushedEventCount)
            return;

        var sb = new StringBuilder();
        for (int i = _flushedEventCount; i < events.Count; i++)
        {
            var ev = events[i];
            string fieldsText = ev.Fields is null || ev.Fields.Count == 0
                ? "-"
                : string.Join("; ", ev.Fields.Select(kv => $"{kv.Key}={kv.Value}"));
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0:O}\t{1}\t{2}\t{3}{4}",
                ev.TimestampUtc,
                ev.Category,
                ev.Message,
                fieldsText,
                Environment.NewLine);
        }

        _flushedEventCount = events.Count;
        File.AppendAllText(RuntimeEventLogPath, sb.ToString(), Encoding.UTF8);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FlushDiagnosticsToDisk();
        _container.Dispose();
    }
}
