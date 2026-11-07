using HackTK.Next.Abstractions;

namespace HackTK.Next.Modules;

public sealed class MovementPulseModule : IEngineModule
{
    private readonly ITimeProvider _timeProvider;
    private readonly IInputDispatcher _inputDispatcher;
    private bool _bhopEnabled;
    private bool _jumpPulseActive;
    private float _releaseAtSeconds;
    private float _nextJumpAllowedSeconds;

    public MovementPulseModule(ITimeProvider timeProvider, IInputDispatcher inputDispatcher)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _inputDispatcher = inputDispatcher ?? throw new ArgumentNullException(nameof(inputDispatcher));
    }

    public string ModuleId => "module.movement-pulse";
    public string DisplayName => "Điều nhịp xung nhảy (Ground Jump Pulse)";
    public ModuleLifecycleState State { get; private set; } = ModuleLifecycleState.Uninitialized;
    public string StatusSummary { get; private set; } = "Chưa khởi tạo";
    public string? LastError { get; private set; }

    public void Initialize()
    {
        State = ModuleLifecycleState.Idle;
        StatusSummary = "Đã khởi tạo; mặc định tắt";
        LastError = null;
    }

    public void Configure(IEngineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _bhopEnabled = profile.BunnyHop;
        if (!_bhopEnabled)
        {
            Reset();
            return;
        }

        State = ModuleLifecycleState.Active;
        StatusSummary = "Chờ phím nhảy & trạng thái chạm đất";
    }

    public void ExecuteTick(IWorldStateProvider world)
    {
        ArgumentNullException.ThrowIfNull(world);
        float now = _timeProvider.UnscaledSeconds;

        if (_jumpPulseActive && now >= _releaseAtSeconds)
        {
            _inputDispatcher.SetJumpKeyState(false);
            _jumpPulseActive = false;
        }

        var local = world.LocalActor;
        if (!_bhopEnabled || local is null || !local.IsAlive || !local.IsFirstPerson)
        {
            ReleaseJumpIfHeld();
            return;
        }

        if (world.IsJumpKeyPressed && local.IsOnGround && now >= _nextJumpAllowedSeconds)
        {
            _inputDispatcher.SetJumpKeyState(false);
            _inputDispatcher.SetJumpKeyState(true);
            _jumpPulseActive = true;
            _releaseAtSeconds = now + 0.025f;
            _nextJumpAllowedSeconds = now + 0.12f;
            StatusSummary = $"Đã phát xung nhảy tại t={now:F3}s";
        }
    }

    private void ReleaseJumpIfHeld()
    {
        if (_jumpPulseActive)
        {
            _inputDispatcher.SetJumpKeyState(false);
            _jumpPulseActive = false;
        }
    }

    public void Reset()
    {
        _bhopEnabled = false;
        ReleaseJumpIfHeld();
        _nextJumpAllowedSeconds = 0f;
        if (State != ModuleLifecycleState.Disposed)
            State = ModuleLifecycleState.Idle;
        StatusSummary = "Tắt";
    }

    public void Dispose()
    {
        Reset();
        State = ModuleLifecycleState.Disposed;
    }
}
