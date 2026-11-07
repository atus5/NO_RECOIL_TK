using HackTK.Next.Abstractions;

namespace HackTK.Next.Modules;

public sealed class TargetAcquisitionModule : IEngineModule
{
    private readonly IAimTrajectoryService _aimService;
    private readonly IVisibilityEvaluationService _visibilityService;
    private readonly IInputDispatcher _inputDispatcher;
    private readonly IDiagnosticLogger _logger;
    private IEngineProfile _profile = null!;
    private bool _enabled;
    private int? _lockedTargetEntityId;
    private float _remainderX;
    private float _remainderY;

    public TargetAcquisitionModule(
        IAimTrajectoryService aimService,
        IVisibilityEvaluationService visibilityService,
        IInputDispatcher inputDispatcher,
        IDiagnosticLogger logger)
    {
        _aimService = aimService ?? throw new ArgumentNullException(nameof(aimService));
        _visibilityService = visibilityService ?? throw new ArgumentNullException(nameof(visibilityService));
        _inputDispatcher = inputDispatcher ?? throw new ArgumentNullException(nameof(inputDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ModuleId => "module.target-acquisition";
    public string DisplayName => "Chọn mục tiêu & hỗ trợ ngắm (Aim Trajectory)";
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
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _enabled = profile.AimEnabled;
        if (!_enabled)
        {
            Reset();
            return;
        }

        State = ModuleLifecycleState.Active;
        StatusSummary = $"Sẵn sàng (Bán kính={profile.AimRadiusPixels}px @1080p, Smooth={profile.Smooth})";
    }

    public void ExecuteTick(IWorldStateProvider world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_enabled || _profile is null)
        {
            ResetTracking();
            StatusSummary = "Tắt";
            return;
        }

        var local = world.LocalActor;
        if (local is null || !local.IsAlive)
        {
            ResetTracking();
            StatusSummary = "Chờ local actor còn sống";
            return;
        }

        if (!world.IsTriggerKeyPressed(_profile.ActivationKey))
        {
            ResetTracking();
            StatusSummary = $"Chờ giữ phím {_profile.ActivationKey}";
            return;
        }

        float radiusPixels = _aimService.ComputeScaledRadiusPixels(_profile.AimRadiusPixels, world.ScreenHeight);
        string targetBoneName = _profile.Target == TargetPart.Head ? "Bip01_Head" : "Bip01_Spine";
        float bestDistanceSq = float.MaxValue;
        ScreenPoint bestScreenPoint = default;
        IActorSnapshot? bestActor = null;
        string lastVisibilityReason = "Chưa có mục tiêu trong bán kính FOV";

        foreach (var candidate in world.Actors)
        {
            if (candidate.IsLocal || !candidate.IsAlive || candidate.TeamId == local.TeamId)
                continue;

            if (!candidate.TryGetBonePositionUnity("Bip01_Head", out var headPos) ||
                !candidate.TryGetBonePositionUnity(targetBoneName, out var aimBonePos))
                continue;

            ScreenPoint screenPt = world.ProjectWorldToScreen(aimBonePos);
            if (!_aimService.IsWithinFov(screenPt, world.ScreenWidth, world.ScreenHeight, radiusPixels))
                continue;

            if (!_visibilityService.IsActorVisible(world, local, candidate, headPos, aimBonePos, out string reason))
            {
                lastVisibilityReason = reason;
                continue;
            }

            float dx = screenPt.X - world.ScreenWidth * 0.5f;
            float dy = screenPt.Y - world.ScreenHeight * 0.5f;
            float distSq = dx * dx + dy * dy;
            if (float.IsNaN(distSq) || float.IsInfinity(distSq) || distSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distSq;
            bestScreenPoint = screenPt;
            bestActor = candidate;
        }

        if (bestActor is null)
        {
            ResetTracking();
            StatusSummary = lastVisibilityReason;
            return;
        }

        if (_lockedTargetEntityId != bestActor.EntityId)
        {
            _remainderX = 0f;
            _remainderY = 0f;
            _lockedTargetEntityId = bestActor.EntityId;
        }

        float punchYaw = (_profile.RecoilCompensation && !_profile.SmoothCameraRecoil) ? local.PunchYaw : 0f;
        float punchPitch = (_profile.RecoilCompensation && !_profile.SmoothCameraRecoil) ? local.PunchPitch : 0f;

        var (stepX, nextRemX) = _aimService.ComputeSubpixelStep(
            bestScreenPoint.X,
            world.ScreenWidth * 0.5f,
            punchYaw,
            _profile.Smooth,
            _remainderX);

        var (stepY, nextRemY) = _aimService.ComputeSubpixelStep(
            world.ScreenHeight - bestScreenPoint.Y,
            world.ScreenHeight * 0.5f,
            punchPitch,
            _profile.Smooth,
            _remainderY);

        _remainderX = nextRemX;
        _remainderY = nextRemY;

        if (stepX != 0 || stepY != 0)
            _inputDispatcher.MoveMouseRelative(stepX, stepY);

        StatusSummary = stepX == 0 && stepY == 0
            ? $"Khóa mục tiêu #{bestActor.EntityId}; đang tích lũy subpixel"
            : $"Khóa mục tiêu #{bestActor.EntityId}; gửi dx={stepX}, dy={stepY}";

        _logger.Record("AIM_STEP", StatusSummary, new Dictionary<string, string>
        {
            ["targetId"] = bestActor.EntityId.ToString(),
            ["dx"] = stepX.ToString(),
            ["dy"] = stepY.ToString()
        });
    }

    private void ResetTracking()
    {
        _lockedTargetEntityId = null;
        _remainderX = 0f;
        _remainderY = 0f;
    }

    public void Reset()
    {
        _enabled = false;
        ResetTracking();
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
