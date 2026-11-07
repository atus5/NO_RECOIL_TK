namespace HackTK.Next.Abstractions;

public enum TargetPart
{
    Head = 0,
    Chest = 1
}

public enum ShaderRenderMode
{
    Flat = 0,
    Additive = 1,
    Glow = 2
}

public enum TriggerKey
{
    LeftAlt = 0,
    LeftControl = 1,
    LeftShift = 2,
    Mouse0 = 3,
    Mouse1 = 4,
    Mouse2 = 5,
    Mouse3 = 6,
    Mouse4 = 7
}

public enum MovementStateKind
{
    Stand = 0,
    Walk = 1,
    Run = 2,
    Strafe = 3,
    MoveTurn = 4,
    JumpAir = 5,
    Crouch = 6,
    AimDownSights = 7
}

public enum WeaponCategory
{
    AssaultRifle = 0,
    SniperRifle = 1,
    LightMachineGun = 2,
    SubmachineGun = 3,
    Pistol = 4,
    Shotgun = 5,
    Custom = 6
}

public enum WeaponAccuracyFormulaKind
{
    RifleCubic = 0,
    SniperScopedPosture = 1,
    LmgSustainedBelt = 2,
    SmgQuadratic = 3,
    PistolIntervalDecay = 4,
    ShotgunPelletFixed = 5,
    CustomLinear = 6
}

public enum OperatorPostureKind
{
    StandingStill = 0,
    Walking = 1,
    Running = 2,
    Jumping = 3,
    Crouching = 4
}

public enum ModuleLifecycleState
{
    Uninitialized = 0,
    Idle = 1,
    Active = 2,
    Paused = 3,
    Faulted = 4,
    Disposed = 5
}

public readonly record struct Vector3D(double X, double Y, double Z)
{
    public static Vector3D Zero => new(0.0, 0.0, 0.0);
    public static Vector3D UnitX => new(1.0, 0.0, 0.0);
    public static Vector3D UnitY => new(0.0, 1.0, 0.0);
    public static Vector3D UnitZ => new(0.0, 0.0, 1.0);

    public bool IsFinite =>
        !double.IsNaN(X) && !double.IsInfinity(X) &&
        !double.IsNaN(Y) && !double.IsInfinity(Y) &&
        !double.IsNaN(Z) && !double.IsInfinity(Z);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public double Dot(Vector3D other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vector3D Cross(Vector3D other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    public Vector3D Add(Vector3D other) => new(X + other.X, Y + other.Y, Z + other.Z);
    public Vector3D Subtract(Vector3D other) => new(X - other.X, Y - other.Y, Z - other.Z);
    public Vector3D Scale(double factor) => new(X * factor, Y * factor, Z * factor);

    public bool TryNormalize(out Vector3D unit)
    {
        double len = Length;
        if (!IsFinite || double.IsNaN(len) || double.IsInfinity(len) || len < 1e-12)
        {
            unit = default;
            return false;
        }
        unit = new Vector3D(X / len, Y / len, Z / len);
        return true;
    }
}

public readonly record struct ScreenPoint(float X, float Y, float Depth)
{
    public bool IsFinite =>
        !float.IsNaN(X) && !float.IsInfinity(X) &&
        !float.IsNaN(Y) && !float.IsInfinity(Y) &&
        !float.IsNaN(Depth) && !float.IsInfinity(Depth);
}

public readonly record struct BoundingBox2D(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float CenterX => X + Width * 0.5f;
    public float CenterY => Y + Height * 0.5f;
}

public readonly record struct TraceHitResult(
    float Fraction,
    bool AllSolid,
    bool StartSolid,
    bool OverlapResolved,
    int HitEntityId,
    string BoneName)
{
    public bool HasSolidOrOverlap => AllSolid || StartSolid || OverlapResolved;
}

public readonly record struct InverseAngleSolution(double YawDegrees, double PitchDegrees, double AngularErrorDegrees);

public readonly record struct QuantizedCommandAngles(
    short Yaw16,
    short Pitch16,
    double ContinuousErrorDegrees,
    double QuantizedErrorDegrees);

public sealed record ModuleStatusSnapshot(
    string ModuleId,
    string DisplayName,
    ModuleLifecycleState LifecycleState,
    string Summary,
    string? LastError = null);

public sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string Category,
    string Message,
    IReadOnlyDictionary<string, string> Fields);
