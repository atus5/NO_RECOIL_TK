using HackTK.Next.Abstractions;

namespace HackTK.Next.Modules;

public sealed class MaterialSlotModule : IEngineModule
{
    private readonly IMaterialSlotController _materialController;
    private IEngineProfile? _profile;
    private bool _enabled;

    public MaterialSlotModule(IMaterialSlotController materialController)
    {
        _materialController = materialController ?? throw new ArgumentNullException(nameof(materialController));
    }

    public string ModuleId => "module.material-slots";
    public string DisplayName => "Quản lý Material Slots (Chams & Khôi phục)";
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
        _enabled = profile.PlayerChams || profile.HandChams || profile.WeaponChams;
        if (!_enabled)
        {
            Reset();
            return;
        }

        State = ModuleLifecycleState.Active;
        StatusSummary = $"Đang hoạt động (Mode={profile.ChamsMode})";
    }

    public void ExecuteTick(IWorldStateProvider world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_enabled || _profile is null || world.LocalActor is null)
        {
            _materialController.RestoreAll();
            StatusSummary = "Tắt";
            return;
        }

        foreach (var actor in world.Actors)
        {
            if (_profile.PlayerChams && !actor.IsLocal && actor.IsAlive && actor.TeamId != world.LocalActor.TeamId)
            {
                _materialController.ApplyMaterialOverride(actor.EntityId, "BodyAndThirdPersonWeapon", _profile.ChamsMode, _profile.PlayerHue);
            }

            if (actor.IsLocal && actor.IsAlive && actor.IsFirstPerson)
            {
                if (_profile.HandChams)
                    _materialController.ApplyMaterialOverride(actor.EntityId, "FirstPersonHand", _profile.ChamsMode, _profile.HandHue);
                if (_profile.WeaponChams)
                    _materialController.ApplyMaterialOverride(actor.EntityId, "FirstPersonWeapon", _profile.ChamsMode, _profile.WeaponHue);
            }
        }

        StatusSummary = $"Đang quản lý {_materialController.ActiveReplacementCount} khe material";
    }

    public void Reset()
    {
        _enabled = false;
        _profile = null;
        _materialController.RestoreAll();
        if (State != ModuleLifecycleState.Disposed)
            State = ModuleLifecycleState.Idle;
        StatusSummary = "Tắt (Đã khôi phục material gốc)";
    }

    public void Dispose()
    {
        Reset();
        State = ModuleLifecycleState.Disposed;
    }
}
