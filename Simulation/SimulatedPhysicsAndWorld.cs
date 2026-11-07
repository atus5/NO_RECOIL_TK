using HackTK.Next.Abstractions;

namespace HackTK.Next.Simulation;

public sealed record SimulatedSphereCollider(
    int EntityId,
    string BoneName,
    Vector3D CenterSsjj,
    double Radius,
    bool IsBroadBodyCollider = false);

public sealed class SimulatedPhysicsRaycaster : IPhysicsRaycaster
{
    private readonly List<SimulatedSphereCollider> _colliders = new();

    public string BackendName => "SimulatedPhysicsWorld.v1";
    public bool IsReady { get; set; } = true;
    public IList<SimulatedSphereCollider> Colliders => _colliders;

    public TraceHitResult CastRaySsjj(Vector3D startSsjj, Vector3D endSsjj, int localEntityId, int ignoredBroadTargetEntityId)
    {
        Vector3D delta = endSsjj.Subtract(startSsjj);
        double totalLength = delta.Length;
        if (totalLength < 1e-9 || !delta.TryNormalize(out var dir))
            return new TraceHitResult(1f, false, false, false, -1, "dummy");

        float closestFraction = 1f;
        int hitEntity = -1;
        string hitBone = "dummy";

        foreach (var col in _colliders)
        {
            if (col.EntityId == localEntityId)
                continue;

            if (col.IsBroadBodyCollider && col.EntityId == ignoredBroadTargetEntityId)
                continue;

            Vector3D oc = startSsjj.Subtract(col.CenterSsjj);
            double b = oc.Dot(dir);
            double c = oc.Dot(oc) - col.Radius * col.Radius;
            double discriminant = b * b - c;
            if (discriminant < 0.0)
                continue;

            double t = -b - Math.Sqrt(discriminant);
            if (t < 0.0) t = 0.0;
            if (t > totalLength)
                continue;

            float fraction = (float)(t / totalLength);
            if (fraction < closestFraction)
            {
                closestFraction = fraction;
                hitEntity = col.EntityId;
                hitBone = col.BoneName;
            }
        }

        return new TraceHitResult(closestFraction, false, false, false, hitEntity, hitBone);
    }
}

public sealed class SimulatedWeaponSnapshot : IWeaponSnapshot
{
    public string WeaponName { get; set; } = "AK47_Simulation";
    public int ClipAmmo { get; set; } = 30;
    public int ShotsFired { get; set; } = 3;
    public double Spread { get; set; } = 0.004;
    public float SpreadScaleY { get; set; } = 0.4f;
    public double ShotsFiredMultiplier { get; set; } = 1.35;
    public WeaponCategory Category { get; set; } = WeaponCategory.AssaultRifle;
    public int PelletCount { get; set; } = 1;
    public double Accuracy { get; set; } = 0.2;
    public bool IsScoped { get; set; }
}

public sealed class SimulatedActorSnapshot : IActorSnapshot
{
    public int EntityId { get; set; }
    public string Name { get; set; } = "Actor";
    public int TeamId { get; set; } = 1;
    public bool IsLocal { get; set; }
    public bool IsAlive { get; set; } = true;
    public bool IsFirstPerson { get; set; } = true;
    public bool IsThirdPersonActionMove { get; set; }
    public bool IsOnGround { get; set; } = true;
    public bool IsCrouching { get; set; }
    public bool IsAimingDownSights { get; set; }
    public int Fov { get; set; } = 90;
    public double HorizontalVelocity { get; set; }
    public float HealthPercent { get; set; } = 1.0f;
    public float OrientationYaw { get; set; }
    public float OrientationPitch { get; set; }
    public float PunchYaw { get; set; }
    public float PunchPitch { get; set; }
    public Vector3D EyePositionUnity { get; set; } = new(0.0, 1.7, 0.0);
    public Vector3D CompensatedShotOriginSsjj { get; set; } = new(0.0, 0.0, 1.7);
    public IWeaponSnapshot? CurrentWeapon { get; set; }
    public Dictionary<string, Vector3D> BonesUnity { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string ChildBone, string ParentBone, Vector3D ChildPos, Vector3D ParentPos)> SkeletonSegments { get; } = new();

    public bool TryGetBonePositionUnity(string boneName, out Vector3D position) =>
        BonesUnity.TryGetValue(boneName, out position);

    public IReadOnlyList<(string ChildBone, string ParentBone, Vector3D ChildPos, Vector3D ParentPos)> GetSkeletonSegmentsUnity() =>
        SkeletonSegments;
}

public sealed class SimulatedUserCommandFrame : IUserCommandFrame
{
    public int SequenceNumber { get; set; }
    public int RandomSeed { get; set; }
    public int Buttons { get; set; }
    public float MoveForward { get; set; }
    public float MoveRight { get; set; }
    public short CameraYaw16 { get; set; }
    public short CameraPitch16 { get; set; }
    public bool IsAttackOn { get; set; }
    public bool IsWalkModifierOn { get; set; }
    public bool IsCrouchOn { get; set; }
    public bool IsAimDownSightsOn { get; set; }
    public bool WasCompensated { get; set; }
}

public sealed class SimulatedWorldState : IWorldStateProvider
{
    private readonly HashSet<TriggerKey> _pressedTriggers = new();

    public bool IsApplicationFocused { get; set; } = true;
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public IActorSnapshot? LocalActor { get; set; }
    public List<IActorSnapshot> ActorList { get; } = new();
    public IReadOnlyList<IActorSnapshot> Actors => ActorList;
    public IUserCommandFrame? CurrentCommand { get; set; }
    public bool IsJumpKeyPressed { get; set; }

    public void SetTriggerPressed(TriggerKey key, bool pressed)
    {
        if (pressed) _pressedTriggers.Add(key);
        else _pressedTriggers.Remove(key);
    }

    public bool IsTriggerKeyPressed(TriggerKey key) => _pressedTriggers.Contains(key);

    public ScreenPoint ProjectWorldToScreen(Vector3D worldPositionUnity)
    {
        if (worldPositionUnity.Z <= 0.01)
            return new ScreenPoint(0f, 0f, (float)worldPositionUnity.Z);

        float cx = ScreenWidth * 0.5f;
        float cy = ScreenHeight * 0.5f;
        float focal = ScreenHeight * 0.8f;
        float sx = cx + (float)(worldPositionUnity.X / worldPositionUnity.Z * focal);
        float sy = cy + (float)((worldPositionUnity.Y - 1.5) / worldPositionUnity.Z * focal);
        return new ScreenPoint(sx, sy, (float)worldPositionUnity.Z);
    }
}

public sealed class RecordingInputDispatcher : IInputDispatcher
{
    private readonly List<(int DeltaX, int DeltaY)> _mouseMoves = new();
    private readonly List<bool> _jumpStates = new();

    public IReadOnlyList<(int DeltaX, int DeltaY)> DispatchedMouseMoves => _mouseMoves;
    public IReadOnlyList<bool> DispatchedJumpStates => _jumpStates;

    public void MoveMouseRelative(int deltaX, int deltaY) => _mouseMoves.Add((deltaX, deltaY));
    public void SetJumpKeyState(bool pressed) => _jumpStates.Add(pressed);
    public void Clear() { _mouseMoves.Clear(); _jumpStates.Clear(); }
}

public sealed class RecordingOverlayContext : IRenderOverlayContext
{
    public int BoxesDrawn { get; private set; }
    public int LinesDrawn { get; private set; }
    public int TextsDrawn { get; private set; }
    public int HealthBarsDrawn { get; private set; }
    public int CrosshairsDrawn { get; private set; }
    public int CirclesDrawn { get; private set; }

    public void DrawBox(BoundingBox2D box, float hue, float thickness) => BoxesDrawn++;
    public void DrawLine(float x1, float y1, float x2, float y2, float hue, float thickness) => LinesDrawn++;
    public void DrawText(float x, float y, string text) => TextsDrawn++;
    public void DrawHealthBar(BoundingBox2D box, float healthFraction) => HealthBarsDrawn++;
    public void DrawCrosshair(float centerX, float centerY) => CrosshairsDrawn++;
    public void DrawCircle(float centerX, float centerY, float radiusPixels) => CirclesDrawn++;
}

public sealed class SimulatedMaterialSlotController : IMaterialSlotController
{
    private readonly Dictionary<(int EntityId, string SlotGroup), (ShaderRenderMode Mode, float Hue)> _overrides = new();

    public int ActiveReplacementCount => _overrides.Count;

    public void ApplyMaterialOverride(int entityId, string slotGroup, ShaderRenderMode mode, float hue) =>
        _overrides[(entityId, slotGroup)] = (mode, hue);

    public void RestoreAll() => _overrides.Clear();
}
