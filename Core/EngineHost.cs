using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;

namespace HackTK.Next.Core;

public sealed class EngineHost : IDisposable
{
    private readonly SessionLeaseManager _leaseManager;
    private readonly IModuleManager _moduleManager;
    private readonly IDiagnosticLogger _logger;
    private long _appliedRevision;
    private bool _wasRunning;
    private bool _disposed;

    public EngineHost(
        SessionLeaseManager leaseManager,
        IModuleManager moduleManager,
        IDiagnosticLogger logger,
        string? sessionToken = null)
    {
        _leaseManager = leaseManager ?? throw new ArgumentNullException(nameof(leaseManager));
        _moduleManager = moduleManager ?? throw new ArgumentNullException(nameof(moduleManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SessionToken = string.IsNullOrWhiteSpace(sessionToken) ? Guid.NewGuid().ToString("N") : sessionToken;
    }

    public string SessionToken { get; }
    public bool IsConnected { get; private set; }
    public bool IsRunning { get; private set; }
    public long AppliedRevision => _appliedRevision;
    public string HostState { get; private set; } = "Chưa khởi tạo";

    public void Initialize()
    {
        ThrowIfDisposed();
        _moduleManager.InitializeAll();
        HostState = "Đã khởi tạo; chờ lệnh kết nối hợp lệ";
        _logger.Record("ENGINE_LIFECYCLE", "EngineHost đã khởi tạo toàn bộ module.");
    }

    public void ProcessFrame(SessionCommandEnvelope? commandEnvelope, IWorldStateProvider worldState)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(worldState);

        IsConnected = _leaseManager.TryApplyEnvelope(commandEnvelope, SessionToken);

        if (_leaseManager.CurrentRevision > _appliedRevision)
        {
            _moduleManager.ResetAll();
            _appliedRevision = _leaseManager.CurrentRevision;
            _moduleManager.ConfigureAll(_leaseManager.CurrentProfile);
            _logger.Record("ENGINE_REVISION_APPLIED", $"Áp dụng revision #{_appliedRevision}.", new Dictionary<string, string>
            {
                ["revision"] = _appliedRevision.ToString(),
                ["connected"] = IsConnected.ToString()
            });
        }

        bool shouldRun = IsConnected && worldState.IsApplicationFocused;
        if (!shouldRun)
        {
            if (_wasRunning)
            {
                _moduleManager.ResetAll();
                _wasRunning = false;
            }

            IsRunning = false;
            HostState = !IsConnected
                ? $"Tạm dừng — {_leaseManager.LastRejectionReason}"
                : "Tạm dừng — Cửa sổ mất tiêu điểm (unfocused)";
            return;
        }

        if (!_wasRunning)
        {
            _moduleManager.ConfigureAll(_leaseManager.CurrentProfile);
            _wasRunning = true;
        }

        IsRunning = true;
        HostState = $"Đang hoạt động (Revision #{_appliedRevision})";
        _moduleManager.ExecuteTickAll(worldState);
    }

    public IReadOnlyList<ModuleStatusSnapshot> GetModuleStatuses() => _moduleManager.CaptureSnapshots();

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EngineHost));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsRunning = false;
        IsConnected = false;
        HostState = "Đã đóng";
        _moduleManager.Dispose();
    }
}
