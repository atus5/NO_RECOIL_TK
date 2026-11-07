using HackTK.Next.Abstractions;

namespace HackTK.Next.Core;

public sealed class ModuleManager : IModuleManager
{
    private readonly IDiagnosticLogger _logger;
    private readonly List<IEngineModule> _modules = new();
    private readonly HashSet<string> _faultedInCurrentRevision = new(StringComparer.Ordinal);
    private bool _disposed;

    public ModuleManager(IDiagnosticLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<IEngineModule> Modules => _modules;

    public void Register(IEngineModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (_disposed) throw new ObjectDisposedException(nameof(ModuleManager));
        if (_modules.Any(m => string.Equals(m.ModuleId, module.ModuleId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Module '{module.ModuleId}' đã được đăng ký.");

        _modules.Add(module);
    }

    public void InitializeAll()
    {
        foreach (var module in _modules)
        {
            ExecuteIsolated(module, "Initialize", m => m.Initialize());
        }
    }

    public void ConfigureAll(IEngineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _faultedInCurrentRevision.Clear();

        foreach (var module in _modules)
        {
            ExecuteIsolated(module, "Configure", m => m.Configure(profile));
        }
    }

    public void ExecuteTickAll(IWorldStateProvider world)
    {
        ArgumentNullException.ThrowIfNull(world);

        foreach (var module in _modules)
        {
            if (_faultedInCurrentRevision.Contains(module.ModuleId))
                continue;

            ExecuteIsolated(module, "ExecuteTick", m => m.ExecuteTick(world));
        }
    }

    public void ResetAll()
    {
        foreach (var module in _modules)
        {
            try
            {
                module.Reset();
            }
            catch (Exception ex)
            {
                _logger.Record("MODULE_RESET_ERROR", $"Lỗi khôi phục module {module.ModuleId}: {ex.Message}", new Dictionary<string, string>
                {
                    ["moduleId"] = module.ModuleId,
                    ["exception"] = ex.GetType().Name
                });
            }
        }
    }

    public IReadOnlyList<ModuleStatusSnapshot> CaptureSnapshots()
    {
        var snapshots = new List<ModuleStatusSnapshot>(_modules.Count);
        foreach (var module in _modules)
        {
            snapshots.Add(new ModuleStatusSnapshot(
                module.ModuleId,
                module.DisplayName,
                module.State,
                module.StatusSummary,
                module.LastError));
        }
        return snapshots;
    }

    private void ExecuteIsolated(IEngineModule module, string phase, Action<IEngineModule> action)
    {
        try
        {
            action(module);
        }
        catch (Exception ex)
        {
            _faultedInCurrentRevision.Add(module.ModuleId);
            _logger.Record("MODULE_FAULT", $"Module '{module.ModuleId}' gặp lỗi tại pha '{phase}': {ex.Message}", new Dictionary<string, string>
            {
                ["moduleId"] = module.ModuleId,
                ["phase"] = phase,
                ["exception"] = ex.GetType().Name,
                ["message"] = ex.Message
            });
            try { module.Reset(); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetAll();

        for (int i = _modules.Count - 1; i >= 0; i--)
        {
            try { _modules[i].Dispose(); }
            catch { }
        }

        _modules.Clear();
        _faultedInCurrentRevision.Clear();
    }
}
