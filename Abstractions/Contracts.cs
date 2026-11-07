namespace HackTK.Next.Abstractions;

public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
    long MonotonicMilliseconds { get; }
    float UnscaledSeconds { get; }
}

public interface IServiceRegistry
{
    void RegisterSingleton<TService>(TService instance) where TService : class;
    void RegisterSingleton<TService>(Func<IServiceResolver, TService> factory) where TService : class;
    void RegisterTransient<TService>(Func<IServiceResolver, TService> factory) where TService : class;
}

public interface IServiceResolver : IDisposable
{
    TService Resolve<TService>() where TService : class;
    bool TryResolve<TService>(out TService? service) where TService : class;
}

public interface IEngineProfile
{
    bool AimEnabled { get; }
    bool DrawAimRadius { get; }
    float AimRadiusPixels { get; }
    float Smooth { get; }
    TargetPart Target { get; }
    TriggerKey ActivationKey { get; }
    bool RecoilCompensation { get; }
    bool SmoothCameraRecoil { get; }
    bool CompensateCommandSpread { get; }
    bool RecordDiagnostics { get; }
    bool ManageRawInput { get; }
    bool EspBox { get; }
    bool EspSkeleton { get; }
    bool EspSnaplines { get; }
    bool EspNames { get; }
    bool EspHealth { get; }
    float BoxHue { get; }
    float BoneHue { get; }
    bool PlayerChams { get; }
    bool HandChams { get; }
    bool WeaponChams { get; }
    ShaderRenderMode ChamsMode { get; }
    float PlayerHue { get; }
    float HandHue { get; }
    float WeaponHue { get; }
    bool Crosshair { get; }
    bool BunnyHop { get; }
    bool ViewmodelOffset { get; }
    float ViewX { get; }
    float ViewY { get; }
    float ViewZ { get; }
    IReadOnlyList<string> Validate();
}

public interface IConfigurationStore<TProfile>
{
    TProfile Load(string path);
    void Save(string path, TProfile profile);
}

public interface IDiagnosticLogger
{
    void Record(string category, string message, IReadOnlyDictionary<string, string>? fields = null);
    IReadOnlyList<DiagnosticEvent> GetRecentEvents();
}

public interface IWeaponSnapshot
{
    string WeaponName { get; }
    int ClipAmmo { get; }
    int ShotsFired { get; }
    double Spread { get; }
    float SpreadScaleY { get; }
    double ShotsFiredMultiplier { get; }
    WeaponCategory Category => WeaponCategory.AssaultRifle;
    int PelletCount => 1;
    double Accuracy => 1.0;
    bool IsScoped => false;
}

public interface IActorSnapshot
{
    int EntityId { get; }
    string Name { get; }
    int TeamId { get; }
    bool IsLocal { get; }
    bool IsAlive { get; }
    bool IsFirstPerson { get; }
    bool IsThirdPersonActionMove { get; }
    bool IsOnGround { get; }
    bool IsCrouching => false;
    bool IsAimingDownSights => false;
    int Fov => 90;
    double HorizontalVelocity { get; }
    float HealthPercent { get; }
    float OrientationYaw { get; set; }
    float OrientationPitch { get; set; }
    float PunchYaw { get; }
    float PunchPitch { get; }
    Vector3D EyePositionUnity { get; }
    Vector3D CompensatedShotOriginSsjj { get; }
    IWeaponSnapshot? CurrentWeapon { get; }
    bool TryGetBonePositionUnity(string boneName, out Vector3D position);
    IReadOnlyList<(string ChildBone, string ParentBone, Vector3D ChildPos, Vector3D ParentPos)> GetSkeletonSegmentsUnity();
}

public interface IUserCommandFrame
{
    int SequenceNumber { get; }
    int RandomSeed { get; }
    int Buttons { get; }
    float MoveForward { get; }
    float MoveRight { get; }
    short CameraYaw16 { get; set; }
    short CameraPitch16 { get; set; }
    bool IsAttackOn { get; }
    bool IsWalkModifierOn { get; }
    bool IsCrouchOn => false;
    bool IsAimDownSightsOn => false;
    bool WasCompensated { get; set; }
}

public interface IWorldStateProvider
{
    bool IsApplicationFocused { get; }
    int ScreenWidth { get; }
    int ScreenHeight { get; }
    IActorSnapshot? LocalActor { get; }
    IReadOnlyList<IActorSnapshot> Actors { get; }
    IUserCommandFrame? CurrentCommand { get; }
    bool IsTriggerKeyPressed(TriggerKey key);
    bool IsJumpKeyPressed { get; }
    ScreenPoint ProjectWorldToScreen(Vector3D worldPositionUnity);
}

public interface ICoordinateTransformer
{
    Vector3D UnityToSsjj(Vector3D unityPoint);
    Vector3D SsjjToUnity(Vector3D ssjjPoint);
}

public interface IPhysicsRaycaster
{
    string BackendName { get; }
    bool IsReady { get; }
    TraceHitResult CastRaySsjj(Vector3D startSsjj, Vector3D endSsjj, int localEntityId, int ignoredBroadTargetEntityId);
}

public interface IBallisticDirectionModel
{
    Vector3D CalculateDirection(int seed, double yawDegrees, double pitchDegrees, double spread, float spreadScaleY, double shotsFiredMultiplier);

    IReadOnlyList<Vector3D> CalculatePelletDirections(
        int seed,
        double yawDegrees,
        double pitchDegrees,
        double spread,
        float spreadScaleY,
        double shotsFiredMultiplier,
        int pelletCount) =>
        new[] { CalculateDirection(seed, yawDegrees, pitchDegrees, spread, spreadScaleY, shotsFiredMultiplier) };
}

public sealed record ResolvedWeaponParameters(
    string CanonicalWeaponId,
    string DisplayName,
    WeaponCategory Category,
    WeaponAccuracyFormulaKind FormulaKind,
    double Spread,
    float SpreadScaleY,
    double ShotsFiredMultiplier,
    float PunchYaw,
    float PunchPitch,
    int PelletCount,
    double Accuracy,
    bool IsScoped);

public interface IWeaponParameterResolver
{
    bool TryResolveWeapon(
        IActorSnapshot actor,
        IWeaponSnapshot weapon,
        MovementStateKind movementState,
        int sequenceNumber,
        out ResolvedWeaponParameters resolved);
}

public interface ISpreadCompensationSolver
{
    double WrapAngleDegrees(double degrees);
    double ComputeAngularErrorDegrees(Vector3D a, Vector3D b);
    short EncodeAngle16(double degrees);
    bool TrySolveInverse(
        Func<double, double, Vector3D> directionEvaluator,
        Vector3D targetDirection,
        double initialYawDegrees,
        double initialPitchDegrees,
        out InverseAngleSolution solution);
    bool TryQuantizeCommandAngles(
        Func<double, double, Vector3D> directionEvaluator,
        Vector3D targetDirection,
        double solvedYawDegrees,
        double solvedPitchDegrees,
        double punchYawDegrees,
        double punchPitchDegrees,
        out QuantizedCommandAngles quantized);
}

public interface IAimTrajectoryService
{
    float ComputeFovRadiusPixels(float fovDegrees, int screenHeight);
    float ComputeScaledRadiusPixels(float referencePixelsAt1080p, int screenHeight);
    bool IsWithinFov(ScreenPoint point, int screenWidth, int screenHeight, float radiusPixels);
    int ComputeDelta(float targetCoord, float centerCoord, float punchAngle, float smoothFactor);
    (int WholePixels, float NextRemainder) ComputeSubpixelStep(
        float targetCoord,
        float centerCoord,
        float punchAngle,
        float smoothFactor,
        float currentRemainder);
}

public interface IVisibilityEvaluationService
{
    bool IsActorVisible(
        IWorldStateProvider world,
        IActorSnapshot localActor,
        IActorSnapshot targetActor,
        Vector3D headUnity,
        Vector3D aimPointUnity,
        out string diagnosticReason);
}

public interface IInputDispatcher
{
    void MoveMouseRelative(int deltaX, int deltaY);
    void SetJumpKeyState(bool pressed);
    IReadOnlyList<(int DeltaX, int DeltaY)> DispatchedMouseMoves { get; }
    IReadOnlyList<bool> DispatchedJumpStates { get; }
}

public interface IRenderOverlayContext
{
    void DrawBox(BoundingBox2D box, float hue, float thickness);
    void DrawLine(float x1, float y1, float x2, float y2, float hue, float thickness);
    void DrawText(float x, float y, string text);
    void DrawHealthBar(BoundingBox2D box, float healthFraction);
    void DrawCrosshair(float centerX, float centerY);
    void DrawCircle(float centerX, float centerY, float radiusPixels);
}

public interface IMaterialSlotController
{
    int ActiveReplacementCount { get; }
    void ApplyMaterialOverride(int entityId, string slotGroup, ShaderRenderMode mode, float hue);
    void RestoreAll();
}

public interface IEngineModule : IDisposable
{
    string ModuleId { get; }
    string DisplayName { get; }
    ModuleLifecycleState State { get; }
    string StatusSummary { get; }
    string? LastError { get; }
    void Initialize();
    void Configure(IEngineProfile profile);
    void ExecuteTick(IWorldStateProvider world);
    void Reset();
}

public interface IModuleManager : IDisposable
{
    IReadOnlyList<IEngineModule> Modules { get; }
    void Register(IEngineModule module);
    void InitializeAll();
    void ConfigureAll(IEngineProfile profile);
    void ExecuteTickAll(IWorldStateProvider world);
    void ResetAll();
    IReadOnlyList<ModuleStatusSnapshot> CaptureSnapshots();
}
