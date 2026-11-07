using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;
using HackTK.Next.Core;
using HackTK.Next.Diagnostics;
using HackTK.Next.Modules;
using HackTK.Next.Services;
using HackTK.Next.Simulation;

namespace HackTK.Next.Runtime;

/// <summary>
/// Explicit contract that any Unity MonoBehaviour or host engine adapter must provide
/// to bridge real Unity engine frames (Update / LateUpdate / OnGUI) into HackTK.Next's EngineHost.
/// </summary>
public interface IUnityRuntimeHostAdapter
{
    int ProcessId { get; }
    long ProcessStartUtcTicks { get; }
    string UnityEngineVersion { get; }
    string GameRootDirectory { get; }
    IWorldStateProvider WorldStateProvider { get; }
    IInputDispatcher InputDispatcher { get; }
    IRenderOverlayContext OverlayContext { get; }
    IMaterialSlotController MaterialController { get; }
    IPhysicsRaycaster PhysicsRaycaster { get; }
}

/// <summary>
/// Concrete runtime bridge that connects an <see cref="IUnityRuntimeHostAdapter"/>
/// to <see cref="EngineHost"/>, <see cref="IModuleManager"/>, and the XML session wire protocol v3.
/// </summary>
public sealed class UnityRuntimeBridgeHost : IDisposable
{
    private readonly IUnityRuntimeHostAdapter _adapter;
    private readonly ITimeProvider _timeProvider;
    private readonly IDiagnosticLogger _logger;
    private readonly IConfigurationStore<SessionCommandEnvelope> _commandStore;
    private readonly IConfigurationStore<RuntimeSessionStatus> _statusStore;
    private readonly ModuleManager _moduleManager;
    private readonly EngineHost _engineHost;
    private readonly SpreadCompensationModule _spreadModule;
    private bool _disposed;

    public UnityRuntimeBridgeHost(IUnityRuntimeHostAdapter adapter, ITimeProvider? timeProvider = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _timeProvider = timeProvider ?? new SystemTimeProvider();
        _logger = new StructuredDiagnosticLogger(_timeProvider);
        _commandStore = new AtomicXmlConfigurationStore<SessionCommandEnvelope>();
        _statusStore = new AtomicXmlConfigurationStore<RuntimeSessionStatus>();

        var transformer = new SsjjCoordinateTransformer();
        var directionModel = new TriangularSpreadDirectionModel();
        var solver = new NewtonSpreadSolverService();
        var aimService = new SubpixelAimComputationService();
        var visibilityService = new DualOriginVisibilityService(_adapter.PhysicsRaycaster, transformer);
        var weaponRegistry = WeaponRegistry.CreateDefault();
        var categoryEngine = new WeaponCategorySimulationEngine(weaponRegistry);
        var weaponResolver = new RuntimeWeaponParameterResolver(weaponRegistry, categoryEngine);

        _spreadModule = new SpreadCompensationModule(solver, directionModel, _logger, weaponResolver);
        _moduleManager = new ModuleManager(_logger);
        _moduleManager.Register(new TargetAcquisitionModule(aimService, visibilityService, _adapter.InputDispatcher, _logger));
        _moduleManager.Register(_spreadModule);
        _moduleManager.Register(new EspProjectionModule(_adapter.OverlayContext, aimService));
        _moduleManager.Register(new MaterialSlotModule(_adapter.MaterialController));
        _moduleManager.Register(new MovementPulseModule(_timeProvider, _adapter.InputDispatcher));

        var leaseManager = new SessionLeaseManager(_timeProvider);
        _engineHost = new EngineHost(leaseManager, _moduleManager, _logger);
        _engineHost.Initialize();
    }

    public EngineHost EngineHost => _engineHost;
    public SpreadCompensationModule SpreadModule => _spreadModule;

    public RuntimeSessionStatus TickUnityFrame()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnityRuntimeBridgeHost));

        string cmdPath = RuntimeSessionPaths.CommandFile(_adapter.ProcessId);
        string statusPath = RuntimeSessionPaths.StatusFile(_adapter.ProcessId);
        SessionCommandEnvelope? envelope = null;
        string transportError = string.Empty;

        if (File.Exists(cmdPath))
        {
            try
            {
                envelope = _commandStore.Load(cmdPath);
            }
            catch (Exception ex)
            {
                transportError = ex.Message;
            }
        }

        _engineHost.ProcessFrame(envelope, _adapter.WorldStateProvider);

        var features = _engineHost.GetModuleStatuses()
            .Select(m => new RuntimeFeatureState
            {
                Feature = $"{m.DisplayName} [{m.ModuleId}]",
                State = m.LifecycleState.ToString(),
                Detail = m.Summary
            })
            .ToArray();

        var status = new RuntimeSessionStatus
        {
            Protocol = NextWireProtocol.CurrentVersion,
            ProcessId = _adapter.ProcessId,
            ProcessStartUtcTicks = _adapter.ProcessStartUtcTicks,
            Session = _engineHost.SessionToken,
            MainThreadUtcTicks = _timeProvider.UtcNow.UtcTicks,
            AppliedRevision = _engineHost.AppliedRevision,
            Connected = _engineHost.IsConnected,
            GameDirectory = _adapter.GameRootDirectory,
            UnityVersion = _adapter.UnityEngineVersion,
            State = _engineHost.HostState,
            TransportError = transportError,
            PlayerCount = _adapter.WorldStateProvider.Actors.Count,
            Features = features
        };

        _statusStore.Save(statusPath, status);
        return status;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engineHost.Dispose();
    }
}
