using HackTK.Next.Abstractions;

namespace HackTK.Next.Modules;

public sealed class EspProjectionModule : IEngineModule
{
    private readonly IRenderOverlayContext _overlay;
    private readonly IAimTrajectoryService _aimService;
    private IEngineProfile? _profile;
    private bool _anyEnabled;

    public EspProjectionModule(IRenderOverlayContext overlay, IAimTrajectoryService aimService)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _aimService = aimService ?? throw new ArgumentNullException(nameof(aimService));
    }

    public string ModuleId => "module.esp-projection";
    public string DisplayName => "Hiển thị thông tin không gian 2D (ESP & Overlay)";
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
        _anyEnabled = profile.EspBox || profile.EspSkeleton || profile.EspSnaplines ||
                      profile.EspNames || profile.EspHealth || profile.Crosshair || profile.DrawAimRadius;
        if (!_anyEnabled)
        {
            Reset();
            return;
        }

        State = ModuleLifecycleState.Active;
        StatusSummary = "Đang hoạt động";
    }

    public void ExecuteTick(IWorldStateProvider world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_anyEnabled || _profile is null)
        {
            StatusSummary = "Tắt";
            return;
        }

        float centerX = world.ScreenWidth * 0.5f;
        float centerY = world.ScreenHeight * 0.5f;

        if (_profile.Crosshair)
            _overlay.DrawCrosshair(centerX, centerY);

        if (_profile.DrawAimRadius)
        {
            float radius = _aimService.ComputeScaledRadiusPixels(_profile.AimRadiusPixels, world.ScreenHeight);
            _overlay.DrawCircle(centerX, centerY, radius);
        }

        bool espActive = _profile.EspBox || _profile.EspSkeleton || _profile.EspSnaplines || _profile.EspNames || _profile.EspHealth;
        if (!espActive || world.LocalActor is null)
            return;

        int renderedCount = 0;
        foreach (var actor in world.Actors)
        {
            if (actor.IsLocal || !actor.IsAlive || actor.TeamId == world.LocalActor.TeamId)
                continue;

            if (!actor.TryGetBonePositionUnity("Bip01", out var rootPos) ||
                !actor.TryGetBonePositionUnity("Bip01_Head", out var headPos))
                continue;

            ScreenPoint headScreen = world.ProjectWorldToScreen(headPos);
            ScreenPoint rootScreen = world.ProjectWorldToScreen(rootPos);
            if (!headScreen.IsFinite || !rootScreen.IsFinite || headScreen.Depth < 0.01f || rootScreen.Depth < 0.01f)
                continue;

            float height = Math.Abs(headScreen.Y - rootScreen.Y) * 3f;
            float width = height * 0.5f;
            if (height < 2f)
                continue;

            var box = new BoundingBox2D(
                rootScreen.X - width * 0.5f,
                world.ScreenHeight - rootScreen.Y - height * 0.5f,
                width,
                height);

            if (_profile.EspBox)
                _overlay.DrawBox(box, _profile.BoxHue, thickness: 1f);

            if (_profile.EspSnaplines)
                _overlay.DrawLine(centerX, world.ScreenHeight, box.CenterX, box.Bottom, _profile.BoxHue, thickness: 1f);

            if (_profile.EspSkeleton)
            {
                foreach (var seg in actor.GetSkeletonSegmentsUnity())
                {
                    var a = world.ProjectWorldToScreen(seg.ChildPos);
                    var b = world.ProjectWorldToScreen(seg.ParentPos);
                    if (a.IsFinite && b.IsFinite && a.Depth >= 0.01f && b.Depth >= 0.01f)
                    {
                        _overlay.DrawLine(a.X, world.ScreenHeight - a.Y, b.X, world.ScreenHeight - b.Y, _profile.BoneHue, thickness: 1f);
                    }
                }
            }

            if (_profile.EspNames)
            {
                string sanitized = (actor.Name ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
                if (sanitized.Length > 64) sanitized = sanitized[..64];
                _overlay.DrawText(box.CenterX, box.Bottom + 2f, sanitized);
            }

            if (_profile.EspHealth)
            {
                float hp = float.IsNaN(actor.HealthPercent) || float.IsInfinity(actor.HealthPercent)
                    ? 0f
                    : Math.Clamp(actor.HealthPercent, 0f, 1f);
                _overlay.DrawHealthBar(box, hp);
            }

            renderedCount++;
        }

        StatusSummary = $"Đã chiếu {renderedCount} thực thể địch";
    }

    public void Reset()
    {
        _anyEnabled = false;
        _profile = null;
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
