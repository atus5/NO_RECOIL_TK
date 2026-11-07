using System.Globalization;
using HackTK.Next.Abstractions;

namespace HackTK.Next.Modules;

public sealed class SpreadCompensationModule : IEngineModule
{
    private readonly ISpreadCompensationSolver _solver;
    private readonly IBallisticDirectionModel _directionModel;
    private readonly IDiagnosticLogger _logger;
    private readonly IWeaponParameterResolver? _weaponResolver;
    private bool _enabled;
    private bool _cancelPunch;
    private bool _hasPreviousAngles;
    private double _previousYawDegrees;
    private double _previousPitchDegrees;
    private long _compensatedCount;
    private long _skippedCount;
    private long _invalidWeaponDataCount;
    private string _lastResolvedWeaponId = string.Empty;

    public SpreadCompensationModule(
        ISpreadCompensationSolver solver,
        IBallisticDirectionModel directionModel,
        IDiagnosticLogger logger,
        IWeaponParameterResolver? weaponResolver = null)
    {
        _solver = solver ?? throw new ArgumentNullException(nameof(solver));
        _directionModel = directionModel ?? throw new ArgumentNullException(nameof(directionModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _weaponResolver = weaponResolver;
    }

    public string ModuleId => "module.spread-compensation";
    public string DisplayName => "Bù phân tán đường đạn (Command Spread)";
    public ModuleLifecycleState State { get; private set; } = ModuleLifecycleState.Uninitialized;
    public string StatusSummary { get; private set; } = "Chưa khởi tạo";
    public string? LastError { get; private set; }
    public long CompensatedCount => _compensatedCount;
    public long SkippedCount => _skippedCount;
    public long InvalidWeaponDataCount => _invalidWeaponDataCount;
    public string LastResolvedWeaponId => _lastResolvedWeaponId;
    public WeaponCategory LastWeaponCategory { get; private set; } = WeaponCategory.AssaultRifle;
    public WeaponAccuracyFormulaKind LastFormulaKind { get; private set; } = WeaponAccuracyFormulaKind.RifleCubic;
    public MovementStateKind LastMovementState { get; private set; } = MovementStateKind.Stand;
    public double LastUncompensatedErrorDegrees { get; private set; }
    public double LastQuantizedErrorDegrees { get; private set; }
    public double LastIrreduciblePelletConeMeanDegrees { get; private set; }

    public void Initialize()
    {
        State = ModuleLifecycleState.Idle;
        StatusSummary = "Đã khởi tạo; mặc định tắt";
        LastError = null;
    }

    public void Configure(IEngineProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _enabled = profile.CompensateCommandSpread;
        _cancelPunch = profile.SmoothCameraRecoil;
        if (!_enabled)
        {
            Reset();
            return;
        }

        State = ModuleLifecycleState.Active;
        StatusSummary = $"Sẵn sàng bù góc command mọi trạng thái; đã bù={_compensatedCount}";
    }

    public MovementStateKind ClassifyMovementState(IActorSnapshot actor, IUserCommandFrame command)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(command);

        if (!actor.IsOnGround)
            return MovementStateKind.JumpAir;

        double velocity = Math.Abs(actor.HorizontalVelocity);
        bool moving = velocity > 0.1 || Math.Abs(command.MoveForward) > 0.001f || Math.Abs(command.MoveRight) > 0.001f;
        if (!moving)
        {
            if (actor.IsAimingDownSights ||
                command.IsAimDownSightsOn ||
                (actor.CurrentWeapon is not null && actor.CurrentWeapon.IsScoped) ||
                (actor.Fov > 0 && actor.Fov < 90))
            {
                return MovementStateKind.AimDownSights;
            }

            if (actor.IsCrouching || command.IsCrouchOn)
                return MovementStateKind.Crouch;

            return MovementStateKind.Stand;
        }

        double rawYaw = command.CameraYaw16 / 100.0;
        double rawPitch = command.CameraPitch16 / 100.0;
        if (_hasPreviousAngles &&
            (Math.Abs(_solver.WrapAngleDegrees(rawYaw - _previousYawDegrees)) > 0.05 ||
             Math.Abs(rawPitch - _previousPitchDegrees) > 0.05))
        {
            return MovementStateKind.MoveTurn;
        }

        if (Math.Abs(command.MoveRight) > 0.001f && Math.Abs(command.MoveForward) <= 0.001f)
            return MovementStateKind.Strafe;

        if (command.IsWalkModifierOn || (velocity > 0.1 && velocity <= 150.0))
            return MovementStateKind.Walk;

        return MovementStateKind.Run;
    }

    public void ExecuteTick(IWorldStateProvider world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_enabled)
        {
            State = ModuleLifecycleState.Idle;
            StatusSummary = "Tắt";
            return;
        }

        var actor = world.LocalActor;
        var cmd = world.CurrentCommand;
        if (actor is null || !actor.IsAlive || !actor.IsFirstPerson || actor.IsThirdPersonActionMove || actor.CurrentWeapon is null)
        {
            StatusSummary = "Chờ nhân vật sống, góc nhìn thứ nhất, có vũ khí";
            return;
        }

        if (cmd is null)
        {
            StatusSummary = "Chờ khung lệnh UserCommand";
            return;
        }

        double rawYaw = cmd.CameraYaw16 / 100.0;
        double rawPitch = cmd.CameraPitch16 / 100.0;
        MovementStateKind moveState = ClassifyMovementState(actor, cmd);

        _previousYawDegrees = rawYaw;
        _previousPitchDegrees = rawPitch;
        _hasPreviousAngles = true;

        if (!cmd.IsAttackOn || cmd.WasCompensated)
            return;

        var weapon = actor.CurrentWeapon;
        int seed = cmd.SequenceNumber;
        string weaponName = weapon.WeaponName ?? string.Empty;
        double spread = weapon.Spread;
        float scaleY = weapon.SpreadScaleY;
        double multiplier = weapon.ShotsFiredMultiplier;
        double punchYaw = actor.PunchYaw * 2.0;
        double punchPitch = actor.PunchPitch * 2.0;

        if (!ValidateWeaponParameters(weapon, out string validationReason))
        {
            _skippedCount++;
            _invalidWeaponDataCount++;
            StatusSummary = $"Từ chối thông số vũ khí không hợp lệ ({ validationReason })";
            _logger.Record("COMMAND_INVALID_WEAPON", $"Thông số vũ khí không hợp lệ tại cmd={seed}: {validationReason}", new Dictionary<string, string>
            {
                ["cmd"] = seed.ToString(CultureInfo.InvariantCulture),
                ["weaponName"] = string.IsNullOrWhiteSpace(weaponName) ? "<MissingWeaponName>" : weaponName,
                ["moveState"] = moveState.ToString(),
                ["reason"] = "InvalidWeaponParameters:" + validationReason,
                ["spread"] = spread.ToString("R", CultureInfo.InvariantCulture),
                ["spreadScaleY"] = scaleY.ToString("R", CultureInfo.InvariantCulture),
                ["shotsFiredMultiplier"] = multiplier.ToString("R", CultureInfo.InvariantCulture)
            });
            return;
        }

        WeaponCategory resolvedCategory = weapon.Category;
        WeaponAccuracyFormulaKind resolvedFormula = WeaponAccuracyFormulaKind.RifleCubic;
        int pelletCount = weapon.PelletCount;

        if (_weaponResolver is not null &&
            _weaponResolver.TryResolveWeapon(actor, weapon, moveState, seed, out var resolvedParams))
        {
            resolvedCategory = resolvedParams.Category;
            resolvedFormula = resolvedParams.FormulaKind;
            pelletCount = resolvedParams.PelletCount;
            spread = resolvedParams.Spread;
            scaleY = resolvedParams.SpreadScaleY;
            multiplier = resolvedParams.ShotsFiredMultiplier;
            punchYaw = resolvedParams.PunchYaw * 2.0;
            punchPitch = resolvedParams.PunchPitch * 2.0;
            if (!string.Equals(_lastResolvedWeaponId, resolvedParams.CanonicalWeaponId, StringComparison.OrdinalIgnoreCase))
            {
                string previousId = _lastResolvedWeaponId;
                _lastResolvedWeaponId = resolvedParams.CanonicalWeaponId;
                _logger.Record("WEAPON_DETECTED", $"Nhận diện vũ khí '{resolvedParams.CanonicalWeaponId}' ({resolvedCategory}/{resolvedFormula})", new Dictionary<string, string>
                {
                    ["previousWeaponId"] = previousId,
                    ["weaponId"] = resolvedParams.CanonicalWeaponId,
                    ["displayName"] = resolvedParams.DisplayName,
                    ["category"] = resolvedCategory.ToString(),
                    ["formula"] = resolvedFormula.ToString(),
                    ["pelletCount"] = pelletCount.ToString(CultureInfo.InvariantCulture)
                });
            }
        }
        else if (!string.Equals(_lastResolvedWeaponId, weaponName, StringComparison.OrdinalIgnoreCase))
        {
            _lastResolvedWeaponId = weaponName;
        }

        double targetYaw = _solver.WrapAngleDegrees(rawYaw + (_cancelPunch ? 0.0 : punchYaw));
        double targetPitch = rawPitch + (_cancelPunch ? 0.0 : punchPitch);

        Vector3D rawAimDir = _directionModel.CalculateDirection(seed, rawYaw, rawPitch, 0.0, scaleY, multiplier);
        Vector3D recoiledNoSpreadDir = _directionModel.CalculateDirection(
            seed,
            (float)(rawYaw + (float)punchYaw),
            (float)(rawPitch + (float)punchPitch),
            0.0,
            scaleY,
            multiplier);
        Vector3D targetDir = _directionModel.CalculateDirection(seed, targetYaw, targetPitch, 0.0, scaleY, multiplier);
        Vector3D uncompensatedDir = _directionModel.CalculateDirection(
            seed,
            (float)(rawYaw + (float)punchYaw),
            (float)(rawPitch + (float)punchPitch),
            spread,
            scaleY,
            multiplier);
        double uncompensatedError = _solver.ComputeAngularErrorDegrees(uncompensatedDir, targetDir);
        double recoilError = _solver.ComputeAngularErrorDegrees(recoiledNoSpreadDir, rawAimDir);
        double spreadOnlyError = _solver.ComputeAngularErrorDegrees(uncompensatedDir, recoiledNoSpreadDir);

        Func<double, double, Vector3D> evaluator = (y, p) =>
            _directionModel.CalculateDirection(seed, y, p, spread, scaleY, multiplier);

        if (!_solver.TrySolveInverse(evaluator, targetDir, targetYaw, targetPitch, out var solution) ||
            !_solver.TryQuantizeCommandAngles(evaluator, targetDir, solution.YawDegrees, solution.PitchDegrees, punchYaw, punchPitch, out var quantized) ||
            quantized.QuantizedErrorDegrees > 0.02)
        {
            _skippedCount++;
            StatusSummary = $"Giữ phát gốc ({moveState}); không đạt ngưỡng lượng tử hóa <= 0.02 độ";
            _logger.Record("COMMAND_SKIPPED", $"Bỏ qua bù góc tại cmd={seed} ({moveState})", new Dictionary<string, string>
            {
                ["cmd"] = seed.ToString(CultureInfo.InvariantCulture),
                ["weaponName"] = weaponName,
                ["moveState"] = moveState.ToString(),
                ["spread"] = spread.ToString("R", CultureInfo.InvariantCulture),
                ["uncompensatedErrorDegrees"] = uncompensatedError.ToString("R", CultureInfo.InvariantCulture),
                ["recoilErrorDegrees"] = recoilError.ToString("R", CultureInfo.InvariantCulture),
                ["spreadOnlyErrorDegrees"] = spreadOnlyError.ToString("R", CultureInfo.InvariantCulture)
            });
            return;
        }

        double pelletConeMeanDeg = 0.0;
        if (pelletCount > 1)
        {
            double evalYaw = (float)(quantized.Yaw16 / 100f + (float)punchYaw);
            double evalPitch = (float)(quantized.Pitch16 / 100f + (float)punchPitch);
            var pellets = _directionModel.CalculatePelletDirections(seed, evalYaw, evalPitch, spread, scaleY, multiplier, pelletCount);
            if (pellets.Count > 1)
            {
                double sumCone = 0.0;
                for (int i = 1; i < pellets.Count; i++)
                    sumCone += _solver.ComputeAngularErrorDegrees(pellets[i], pellets[0]);
                pelletConeMeanDeg = sumCone / (pellets.Count - 1);
            }
        }

        short originalYaw16 = cmd.CameraYaw16;
        short originalPitch16 = cmd.CameraPitch16;
        float savedOrientationYaw = actor.OrientationYaw;
        float savedOrientationPitch = actor.OrientationPitch;

        try
        {
            cmd.CameraYaw16 = quantized.Yaw16;
            cmd.CameraPitch16 = quantized.Pitch16;
            cmd.WasCompensated = true;
            actor.OrientationYaw = quantized.Yaw16 / 100f;
            actor.OrientationPitch = quantized.Pitch16 / 100f;
        }
        finally
        {
            // Always restore actor orientation so compensated shot angles never leak into subsequent movement or camera state.
            actor.OrientationYaw = savedOrientationYaw;
            actor.OrientationPitch = savedOrientationPitch;
        }

        _compensatedCount++;
        LastWeaponCategory = resolvedCategory;
        LastFormulaKind = resolvedFormula;
        LastMovementState = moveState;
        LastUncompensatedErrorDegrees = uncompensatedError;
        LastQuantizedErrorDegrees = quantized.QuantizedErrorDegrees;
        LastIrreduciblePelletConeMeanDegrees = pelletConeMeanDeg;

        StatusSummary = $"Đã bù góc ({ weaponName }/{ moveState }); tổng bù={_compensatedCount}; sai số={quantized.QuantizedErrorDegrees:F5}°";
        _logger.Record("COMMAND_COMPENSATED", $"Đã bù góc command cmd={seed} ({moveState})", new Dictionary<string, string>
        {
            ["cmd"] = seed.ToString(CultureInfo.InvariantCulture),
            ["weaponName"] = weaponName,
            ["weaponCategory"] = resolvedCategory.ToString(),
            ["formulaKind"] = resolvedFormula.ToString(),
            ["moveState"] = moveState.ToString(),
            ["onGround"] = actor.IsOnGround.ToString(),
            ["isCrouching"] = actor.IsCrouching.ToString(),
            ["isScoped"] = (actor.IsAimingDownSights || weapon.IsScoped || (actor.Fov > 0 && actor.Fov < 90)).ToString(),
            ["pelletCount"] = pelletCount.ToString(CultureInfo.InvariantCulture),
            ["irreduciblePelletConeMeanDegrees"] = pelletConeMeanDeg.ToString("R", CultureInfo.InvariantCulture),
            ["velocity"] = actor.HorizontalVelocity.ToString("R", CultureInfo.InvariantCulture),
            ["moveForward"] = cmd.MoveForward.ToString("R", CultureInfo.InvariantCulture),
            ["moveRight"] = cmd.MoveRight.ToString("R", CultureInfo.InvariantCulture),
            ["originalYaw16"] = originalYaw16.ToString(CultureInfo.InvariantCulture),
            ["originalPitch16"] = originalPitch16.ToString(CultureInfo.InvariantCulture),
            ["yaw16"] = quantized.Yaw16.ToString(CultureInfo.InvariantCulture),
            ["pitch16"] = quantized.Pitch16.ToString(CultureInfo.InvariantCulture),
            ["spread"] = spread.ToString("R", CultureInfo.InvariantCulture),
            ["spreadScaleY"] = scaleY.ToString("R", CultureInfo.InvariantCulture),
            ["shotsFiredMultiplier"] = multiplier.ToString("R", CultureInfo.InvariantCulture),
            ["punchYaw"] = punchYaw.ToString("R", CultureInfo.InvariantCulture),
            ["punchPitch"] = punchPitch.ToString("R", CultureInfo.InvariantCulture),
            ["uncompensatedErrorDegrees"] = uncompensatedError.ToString("R", CultureInfo.InvariantCulture),
            ["recoilErrorDegrees"] = recoilError.ToString("R", CultureInfo.InvariantCulture),
            ["spreadOnlyErrorDegrees"] = spreadOnlyError.ToString("R", CultureInfo.InvariantCulture),
            ["continuousErrorDegrees"] = quantized.ContinuousErrorDegrees.ToString("R", CultureInfo.InvariantCulture),
            ["quantizedErrorDegrees"] = quantized.QuantizedErrorDegrees.ToString("R", CultureInfo.InvariantCulture)
        });
    }

    public static bool ValidateWeaponParameters(IWeaponSnapshot? weapon, out string reason)
    {
        if (weapon is null)
        {
            reason = "WeaponNull";
            return false;
        }
        if (string.IsNullOrWhiteSpace(weapon.WeaponName))
        {
            reason = "MissingWeaponName";
            return false;
        }
        if (weapon.ClipAmmo < 0)
        {
            reason = "NegativeClipAmmo";
            return false;
        }
        if (weapon.ShotsFired < 0)
        {
            reason = "NegativeShotsFired";
            return false;
        }
        if (!double.IsFinite(weapon.Spread) || weapon.Spread < 0.0 || weapon.Spread > 2.5)
        {
            reason = "OutOfRangeSpread";
            return false;
        }
        if (!float.IsFinite(weapon.SpreadScaleY) || weapon.SpreadScaleY <= 0.0f || weapon.SpreadScaleY > 10.0f)
        {
            reason = "OutOfRangeSpreadScaleY";
            return false;
        }
        if (!double.IsFinite(weapon.ShotsFiredMultiplier) || weapon.ShotsFiredMultiplier <= 0.0 || weapon.ShotsFiredMultiplier > 20.0)
        {
            reason = "OutOfRangeShotsFiredMultiplier";
            return false;
        }
        if (weapon.PelletCount <= 0 || weapon.PelletCount > 32)
        {
            reason = "OutOfRangePelletCount";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public void Reset()
    {
        _enabled = false;
        _cancelPunch = false;
        _hasPreviousAngles = false;
        _previousYawDegrees = 0.0;
        _previousPitchDegrees = 0.0;
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
