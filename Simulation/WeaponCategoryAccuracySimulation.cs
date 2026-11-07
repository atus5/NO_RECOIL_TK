using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;
using HackTK.Next.Diagnostics;
using HackTK.Next.Modules;
using HackTK.Next.Services;

namespace HackTK.Next.Simulation;

public sealed record WeaponAccuracyConfiguration(
    string WeaponId,
    string DisplayName,
    WeaponCategory Category,
    WeaponAccuracyFormulaKind FormulaKind,
    int WeaponTypeCode,
    double DefaultAccuracy,
    double AccuracyDivisor,
    double AccuracyOffset,
    double MaxInaccuracy,
    double PistolDecayRate,
    double PistolMinAccuracy,
    double PistolMaxAccuracy,
    double StandSpreadParam,
    double CrouchSpreadParam,
    double WalkSpreadParam,
    double RunSpreadParam,
    double AirSpreadParam,
    double UnscopedExtraSpread,
    double AdsSpreadMultiplier,
    double WalkSpeedThreshold,
    double RunSpeedThreshold,
    float SpreadScaleY,
    int AttackIntervalMs,
    int ShotsFiredSpreadMin,
    int ShotsFiredSpreadMax,
    int ShotsFiredSpreadTimeMs,
    int MaxClip,
    int RaiseDurationMs,
    bool AutoCloseScopeAfterShot,
    int PelletCount = 1,
    float BasePunchYaw = 0.25f,
    float BasePunchPitch = 1.00f,
    float MaxPunchYaw = 1.50f,
    float MaxPunchPitch = 4.50f);

public struct SimulatedWeaponAccuracyState
{
    public string ActiveWeaponId;
    public int ClipAmmo;
    public int ShotsFired;
    public bool DelayFire;
    public long DecreaseShotsFiredTimeMs;
    public long NextAttackTimerMs;
    public long NextSecondaryAttackTimerMs;
    public long LastShotTimeMs;
    public double Accuracy;
    public double Spread;
    public float SpreadScaleY;
    public bool SpecialMode;
    public int EffectLevel;
    public int PelletCount;
    public float PunchYaw;
    public float PunchPitch;
}

public struct SimulatedOperatorPostureState
{
    public bool IsOnGround;
    public bool IsWeightless;
    public int DuckState; // 0 = DUCKED (Crouching), 3 = UNDUCK (Standing)
    public double HorizontalSpeed2D;
    public int Fov; // 90 = unscoped, 40 = single scope, 10 = double scope
    public int LastFov;
    public int DelayFov;
    public bool ResumeZoom;
    public bool IsAimingDownSights;
    public long ClientTimeMs;

    public static SimulatedOperatorPostureState CreateDefault(long clientTimeMs = 1000L) => new()
    {
        IsOnGround = true,
        IsWeightless = false,
        DuckState = 3,
        HorizontalSpeed2D = 0.0,
        Fov = 90,
        LastFov = 90,
        DelayFov = 90,
        ResumeZoom = false,
        IsAimingDownSights = false,
        ClientTimeMs = clientTimeMs
    };

    public static SimulatedOperatorPostureState FromMovementState(MovementStateKind state, long clientTimeMs = 1000L) => state switch
    {
        MovementStateKind.Stand => CreateDefault(clientTimeMs),
        MovementStateKind.Walk => new SimulatedOperatorPostureState
        {
            IsOnGround = true,
            DuckState = 3,
            HorizontalSpeed2D = 90.0,
            Fov = 90,
            LastFov = 90,
            DelayFov = 90,
            ClientTimeMs = clientTimeMs
        },
        MovementStateKind.Run or MovementStateKind.Strafe or MovementStateKind.MoveTurn => new SimulatedOperatorPostureState
        {
            IsOnGround = true,
            DuckState = 3,
            HorizontalSpeed2D = 220.0,
            Fov = 90,
            LastFov = 90,
            DelayFov = 90,
            ClientTimeMs = clientTimeMs
        },
        MovementStateKind.JumpAir => new SimulatedOperatorPostureState
        {
            IsOnGround = false,
            DuckState = 3,
            HorizontalSpeed2D = 200.0,
            Fov = 90,
            LastFov = 90,
            DelayFov = 90,
            ClientTimeMs = clientTimeMs
        },
        MovementStateKind.Crouch => new SimulatedOperatorPostureState
        {
            IsOnGround = true,
            DuckState = 0,
            HorizontalSpeed2D = 0.0,
            Fov = 90,
            LastFov = 90,
            DelayFov = 90,
            ClientTimeMs = clientTimeMs
        },
        MovementStateKind.AimDownSights => new SimulatedOperatorPostureState
        {
            IsOnGround = true,
            DuckState = 3,
            HorizontalSpeed2D = 0.0,
            Fov = 40,
            LastFov = 40,
            DelayFov = 40,
            IsAimingDownSights = true,
            ClientTimeMs = clientTimeMs
        },
        _ => CreateDefault(clientTimeMs)
    };
}

public readonly record struct WeaponShotEvaluation(
    string WeaponId,
    WeaponCategory Category,
    OperatorPostureKind Posture,
    bool IsAimingDownSights,
    bool IsScoped,
    int FovBeforeShot,
    int FovAfterShot,
    int ShotsFired,
    double Accuracy,
    double Spread,
    float SpreadScaleY,
    double ShotsFiredSpreadMultiplier,
    double EffectiveHorizontalSpread,
    double EffectiveVerticalSpread,
    int PelletCount = 1,
    float PunchYaw = 0f,
    float PunchPitch = 0f);

public readonly record struct ShotgunPelletClusterEvaluation(
    string WeaponId,
    int Seed,
    int PelletCount,
    OperatorPostureKind Posture,
    bool IsAimingDownSights,
    double UncompensatedPrimaryErrorDegrees,
    double UncompensatedCentroidErrorDegrees,
    double RecoilOnlyErrorDegrees,
    double CompensatedPrimaryContinuousErrorDegrees,
    double CompensatedPrimaryQuantizedErrorDegrees,
    double CompensatedCentroidErrorDegrees,
    double IrreduciblePelletConeMeanDegrees,
    double IrreduciblePelletConeMaxDegrees,
    IReadOnlyList<Vector3D> UncompensatedPelletDirections,
    IReadOnlyList<Vector3D> CompensatedPelletDirections);

public interface IWeaponCategoryAccuracyModel
{
    WeaponCategory Category { get; }
    void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState);
    void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld);
    WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs);
    void SynchronizeState(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, in SimulatedOperatorPostureState operatorState) =>
        WeaponAccuracyFormulas.SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
}

public static class WeaponAccuracyFormulas
{
    public static OperatorPostureKind ClassifyPosture(WeaponAccuracyConfiguration config, in SimulatedOperatorPostureState operatorState)
    {
        if (!operatorState.IsOnGround && !operatorState.IsWeightless)
            return OperatorPostureKind.Jumping;
        if (operatorState.HorizontalSpeed2D > config.RunSpeedThreshold)
            return OperatorPostureKind.Running;
        if (operatorState.HorizontalSpeed2D > config.WalkSpeedThreshold)
            return OperatorPostureKind.Walking;
        if (operatorState.DuckState == 0)
            return OperatorPostureKind.Crouching;
        return OperatorPostureKind.StandingStill;
    }

    public static double GetPostureSpreadParam(WeaponAccuracyConfiguration config, OperatorPostureKind posture) => posture switch
    {
        OperatorPostureKind.Jumping => config.AirSpreadParam,
        OperatorPostureKind.Running => config.RunSpreadParam,
        OperatorPostureKind.Walking => config.WalkSpreadParam,
        OperatorPostureKind.Crouching => config.CrouchSpreadParam,
        _ => config.StandSpreadParam
    };

    // Mirrors FireFunctions.CalShotsFiredSpread(IWeaponPropertiesAdapter weapon)
    public static double CalculateShotsFiredSpread(WeaponAccuracyConfiguration config, int shotsFired)
    {
        double num = 0.0;
        if ((config.ShotsFiredSpreadMin != 0 || config.ShotsFiredSpreadMax != 0) && config.ShotsFiredSpreadTimeMs > 0)
        {
            double vX = (double)(shotsFired * config.AttackIntervalMs) / config.ShotsFiredSpreadTimeMs;
            vX = Math.Clamp(vX, 0.0, 1.0);
            num = config.ShotsFiredSpreadMin + (config.ShotsFiredSpreadMax - config.ShotsFiredSpreadMin) * vX;
        }
        return 1.0 - num / 1000.0;
    }

    public static int ResolveEffectiveFov(in SimulatedOperatorPostureState operatorState) =>
        operatorState.Fov <= 0
            ? (operatorState.IsAimingDownSights ? 40 : 90)
            : operatorState.Fov;

    public static (float PunchYaw, float PunchPitch) CalculateSniperRecoilKick(
        WeaponAccuracyConfiguration config,
        int shotsFired,
        OperatorPostureKind posture,
        bool isScoped,
        int fov)
    {
        int clampedShots = Math.Max(1, shotsFired);
        double postureScale = posture switch
        {
            OperatorPostureKind.Crouching => 0.80,
            OperatorPostureKind.Walking => 1.08,
            OperatorPostureKind.Running => 1.20,
            OperatorPostureKind.Jumping => 1.35,
            _ => 1.00
        };

        if (isScoped && posture != OperatorPostureKind.Jumping)
        {
            postureScale *= fov == 10 ? 0.78 : 0.85;
        }

        double progression = 1.0 + Math.Min(4, clampedShots - 1) * 0.22;
        double alternatingSign = (clampedShots > 1 && (clampedShots % 2 == 0)) ? -0.85 : 1.0;
        float pitch = (float)Math.Clamp(config.BasePunchPitch * progression * postureScale, -config.MaxPunchPitch, config.MaxPunchPitch);
        float yaw = (float)Math.Clamp(config.BasePunchYaw * Math.Sqrt(progression) * postureScale * alternatingSign, -config.MaxPunchYaw, config.MaxPunchYaw);
        return (yaw, pitch);
    }

    public static (float PunchYaw, float PunchPitch) CalculateRecoilKick(
        WeaponAccuracyConfiguration config,
        int shotsFired,
        OperatorPostureKind posture,
        bool isAds)
    {
        if (config.Category == WeaponCategory.SniperRifle)
        {
            return CalculateSniperRecoilKick(config, shotsFired, posture, isAds, isAds ? 40 : 90);
        }

        int clampedShots = Math.Max(1, shotsFired);
        double postureScale = posture switch
        {
            OperatorPostureKind.Crouching => 0.80,
            OperatorPostureKind.Walking => 1.08,
            OperatorPostureKind.Running => 1.20,
            OperatorPostureKind.Jumping => 1.35,
            _ => 1.00
        };
        if (isAds)
            postureScale *= 0.85;

        double progression = config.Category switch
        {
            WeaponCategory.LightMachineGun => 1.0 + Math.Min(18, clampedShots - 1) * 0.14,
            WeaponCategory.SubmachineGun => 1.0 + Math.Min(12, clampedShots - 1) * 0.11,
            WeaponCategory.AssaultRifle => 1.0 + Math.Min(10, clampedShots - 1) * 0.16,
            WeaponCategory.Pistol => 1.0 + Math.Min(5, clampedShots - 1) * 0.18,
            WeaponCategory.Shotgun => 1.0 + Math.Min(6, clampedShots - 1) * 0.20,
            WeaponCategory.SniperRifle => 1.0,
            _ => 1.0 + Math.Min(8, clampedShots - 1) * 0.12
        };

        double alternatingSign = (clampedShots > 4 && (clampedShots % 2 == 0)) ? -0.75 : 1.0;
        float pitch = (float)Math.Clamp(config.BasePunchPitch * progression * postureScale, -config.MaxPunchPitch, config.MaxPunchPitch);
        float yaw = (float)Math.Clamp(config.BasePunchYaw * Math.Sqrt(progression) * postureScale * alternatingSign, -config.MaxPunchYaw, config.MaxPunchYaw);
        return (yaw, pitch);
    }

    public static double CalculateAccuracyForShots(WeaponAccuracyConfiguration config, int shotsFired, double currentAccuracy)
    {
        if (shotsFired <= 0)
            return config.DefaultAccuracy;

        double s = shotsFired;
        return config.FormulaKind switch
        {
            WeaponAccuracyFormulaKind.RifleCubic or WeaponAccuracyFormulaKind.LmgSustainedBelt =>
                config.AccuracyDivisor > 0.0
                    ? Math.Clamp((s * s * s) / config.AccuracyDivisor + config.AccuracyOffset, config.DefaultAccuracy, config.MaxInaccuracy)
                    : config.DefaultAccuracy,
            WeaponAccuracyFormulaKind.SmgQuadratic =>
                config.AccuracyDivisor > 0.0
                    ? Math.Clamp((s * s) / config.AccuracyDivisor + config.AccuracyOffset, config.DefaultAccuracy, config.MaxInaccuracy)
                    : config.DefaultAccuracy,
            WeaponAccuracyFormulaKind.ShotgunPelletFixed =>
                config.AccuracyDivisor > 0.0
                    ? Math.Clamp(config.DefaultAccuracy + (s - 1.0) / config.AccuracyDivisor, config.DefaultAccuracy, config.MaxInaccuracy)
                    : config.DefaultAccuracy,
            WeaponAccuracyFormulaKind.CustomLinear =>
                config.AccuracyDivisor > 0.0
                    ? Math.Clamp(config.DefaultAccuracy + s / config.AccuracyDivisor + config.AccuracyOffset, config.DefaultAccuracy, config.MaxInaccuracy)
                    : config.DefaultAccuracy,
            WeaponAccuracyFormulaKind.PistolIntervalDecay =>
                Math.Clamp(currentAccuracy, config.PistolMinAccuracy, config.PistolMaxAccuracy),
            _ => config.DefaultAccuracy
        };
    }

    public static void SynchronizePostureAndScopeSpread(
        WeaponAccuracyConfiguration config,
        ref SimulatedWeaponAccuracyState weaponState,
        in SimulatedOperatorPostureState operatorState)
    {
        OperatorPostureKind posture = ClassifyPosture(config, in operatorState);
        double baseParam = GetPostureSpreadParam(config, posture);
        int effectiveFov = ResolveEffectiveFov(in operatorState);
        bool isScoped = config.FormulaKind == WeaponAccuracyFormulaKind.SniperScopedPosture
            ? effectiveFov < 90
            : (effectiveFov != 90 || operatorState.IsAimingDownSights || weaponState.SpecialMode || (weaponState.EffectLevel & 0x80) != 0);

        double spread = config.FormulaKind switch
        {
            WeaponAccuracyFormulaKind.PistolIntervalDecay =>
                baseParam * (1.0 - Math.Clamp(weaponState.Accuracy, config.PistolMinAccuracy, config.PistolMaxAccuracy)),
            WeaponAccuracyFormulaKind.SniperScopedPosture =>
                ComputeSniperSpread(config, baseParam, posture, isScoped, effectiveFov),
            _ => baseParam * weaponState.Accuracy
        };

        if (config.FormulaKind != WeaponAccuracyFormulaKind.SniperScopedPosture && isScoped)
        {
            spread *= config.AdsSpreadMultiplier;
        }

        weaponState.Spread = spread;
    }

    public static double ComputeSniperSpread(
        WeaponAccuracyConfiguration config,
        double baseParam,
        OperatorPostureKind posture,
        bool isScoped,
        int fov)
    {
        double spread = baseParam;
        if (!isScoped)
        {
            spread += config.UnscopedExtraSpread;
        }
        else if (posture != OperatorPostureKind.Jumping)
        {
            if (fov == 10)
                spread *= config.AdsSpreadMultiplier;
        }

        return spread;
    }

    public static void ApplyDeployReset(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState)
    {
        weaponState.ActiveWeaponId = config.WeaponId;
        weaponState.ClipAmmo = config.MaxClip;
        weaponState.ShotsFired = 0;
        weaponState.DelayFire = false;
        weaponState.DecreaseShotsFiredTimeMs = 0L;
        weaponState.Accuracy = config.DefaultAccuracy;
        weaponState.SpreadScaleY = config.SpreadScaleY;
        weaponState.SpecialMode = false;
        weaponState.EffectLevel = 0;
        weaponState.LastShotTimeMs = 0L;
        weaponState.PelletCount = Math.Max(1, config.PelletCount);
        weaponState.PunchYaw = config.BasePunchYaw;
        weaponState.PunchPitch = config.BasePunchPitch;
        long readyTime = operatorState.ClientTimeMs + config.RaiseDurationMs;
        weaponState.NextAttackTimerMs = readyTime;
        weaponState.NextSecondaryAttackTimerMs = readyTime;

        operatorState.Fov = 90;
        operatorState.LastFov = 90;
        operatorState.DelayFov = 90;
        operatorState.ResumeZoom = false;
        operatorState.IsAimingDownSights = false;

        SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
    }

    // Mirrors NormalFrameAction.Action(ICommonWeaponArgs args) with full Accuracy/Spread/Recoil state sync
    public static void ApplyIdleDecay(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld)
    {
        if (attackHeld || secondaryAttackHeld)
        {
            SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
            return;
        }

        if (weaponState.DelayFire)
        {
            weaponState.DelayFire = false;
            if (weaponState.ShotsFired > 8)
                weaponState.ShotsFired = 8;
            weaponState.DecreaseShotsFiredTimeMs = operatorState.ClientTimeMs + 150L;
        }

        if (weaponState.ShotsFired > 0 && weaponState.DecreaseShotsFiredTimeMs < operatorState.ClientTimeMs)
        {
            weaponState.DecreaseShotsFiredTimeMs = operatorState.ClientTimeMs + 16L;
            weaponState.ShotsFired--;
            weaponState.Accuracy = CalculateAccuracyForShots(config, weaponState.ShotsFired, weaponState.Accuracy);
        }

        if (weaponState.ShotsFired == 0)
        {
            weaponState.Accuracy = config.DefaultAccuracy;
            weaponState.PunchYaw = config.BasePunchYaw;
            weaponState.PunchPitch = config.BasePunchPitch;
        }
        else
        {
            bool isAds = operatorState.IsAimingDownSights || operatorState.Fov < 90 || weaponState.SpecialMode;
            var (py, pp) = CalculateRecoilKick(config, weaponState.ShotsFired, ClassifyPosture(config, in operatorState), isAds);
            weaponState.PunchYaw = py;
            weaponState.PunchPitch = pp;
        }

        SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
    }
}

public sealed class AssaultRifleCategoryModel : IWeaponCategoryAccuracyModel
{
    public WeaponCategory Category => WeaponCategory.AssaultRifle;

    public void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState) =>
        WeaponAccuracyFormulas.ApplyDeployReset(config, ref weaponState, ref operatorState);

    public void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld) =>
        WeaponAccuracyFormulas.ApplyIdleDecay(config, ref weaponState, ref operatorState, attackHeld, secondaryAttackHeld);

    public WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs)
    {
        weaponState.DelayFire = true;
        weaponState.ShotsFired++;
        if (weaponState.ClipAmmo > 0) weaponState.ClipAmmo--;

        if (config.AccuracyDivisor > 0.0)
        {
            double s = weaponState.ShotsFired;
            double acc = (s * s * s) / config.AccuracyDivisor + config.AccuracyOffset;
            weaponState.Accuracy = Math.Clamp(acc, config.DefaultAccuracy, config.MaxInaccuracy);
        }

        OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
        double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(config, posture);
        bool isAds = operatorState.IsAimingDownSights || operatorState.Fov < 90 || weaponState.SpecialMode || (weaponState.EffectLevel & 0x80) != 0;
        double spread = baseParam * weaponState.Accuracy;
        if (isAds)
            spread *= config.AdsSpreadMultiplier;

        var (punchYaw, punchPitch) = WeaponAccuracyFormulas.CalculateRecoilKick(config, weaponState.ShotsFired, posture, isAds);
        weaponState.PunchYaw = punchYaw;
        weaponState.PunchPitch = punchPitch;
        weaponState.Spread = spread;
        weaponState.LastShotTimeMs = operatorState.ClientTimeMs;
        weaponState.NextAttackTimerMs = operatorState.ClientTimeMs + config.AttackIntervalMs;
        double mult = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired);

        return new WeaponShotEvaluation(
            config.WeaponId,
            Category,
            posture,
            isAds,
            operatorState.Fov < 90,
            operatorState.Fov,
            operatorState.Fov,
            weaponState.ShotsFired,
            weaponState.Accuracy,
            spread,
            config.SpreadScaleY,
            mult,
            spread * mult,
            spread * mult * config.SpreadScaleY,
            Math.Max(1, config.PelletCount),
            punchYaw,
            punchPitch);
    }
}

public sealed class SniperRifleCategoryModel : IWeaponCategoryAccuracyModel
{
    public WeaponCategory Category => WeaponCategory.SniperRifle;

    public void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState)
    {
        WeaponAccuracyFormulas.ApplyDeployReset(config, ref weaponState, ref operatorState);
        weaponState.PunchYaw = 0f;
        weaponState.PunchPitch = 0f;
    }

    public void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld)
    {
        if (operatorState.Fov <= 0)
        {
            int initFov = operatorState.IsAimingDownSights ? 40 : 90;
            operatorState.Fov = initFov;
            operatorState.LastFov = initFov;
            operatorState.DelayFov = initFov;
        }

        bool inBoltRecovery = weaponState.LastShotTimeMs > 0 && operatorState.ClientTimeMs < weaponState.NextAttackTimerMs;
        if (inBoltRecovery)
        {
            double durationMs = Math.Max(1, config.AttackIntervalMs);
            double remainingRatio = Math.Clamp((weaponState.NextAttackTimerMs - operatorState.ClientTimeMs) / durationMs, 0.0, 1.0);
            OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
            int shotFov = operatorState.ResumeZoom ? operatorState.LastFov : operatorState.Fov;
            bool wasScoped = shotFov > 0 && shotFov < 90;
            var (peakYaw, peakPitch) = WeaponAccuracyFormulas.CalculateSniperRecoilKick(config, Math.Max(1, weaponState.ShotsFired), posture, wasScoped, shotFov);
            weaponState.PunchYaw = (float)(peakYaw * remainingRatio);
            weaponState.PunchPitch = (float)(peakPitch * remainingRatio);
            WeaponAccuracyFormulas.SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
            return;
        }

        if (operatorState.ResumeZoom && weaponState.NextAttackTimerMs <= operatorState.ClientTimeMs)
        {
            int restoredFov = operatorState.LastFov > 0 ? operatorState.LastFov : 90;
            operatorState.Fov = restoredFov;
            operatorState.DelayFov = restoredFov;
            operatorState.ResumeZoom = false;
            operatorState.IsAimingDownSights = restoredFov < 90;
        }

        if (!attackHeld)
        {
            weaponState.DelayFire = false;
            weaponState.ShotsFired = 0;
            weaponState.DecreaseShotsFiredTimeMs = 0L;
            weaponState.Accuracy = config.DefaultAccuracy;
            weaponState.PunchYaw = 0f;
            weaponState.PunchPitch = 0f;
        }

        WeaponAccuracyFormulas.SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
    }

    public static void ToggleScopeZoom(ref SimulatedOperatorPostureState operatorState)
    {
        int currentFov = WeaponAccuracyFormulas.ResolveEffectiveFov(in operatorState);
        if (currentFov == 90)
        {
            operatorState.Fov = 40;
            operatorState.LastFov = 40;
            operatorState.DelayFov = 40;
            operatorState.ResumeZoom = false;
            operatorState.IsAimingDownSights = true;
        }
        else if (currentFov == 40)
        {
            operatorState.Fov = 10;
            operatorState.LastFov = 10;
            operatorState.DelayFov = 10;
            operatorState.ResumeZoom = false;
            operatorState.IsAimingDownSights = true;
        }
        else
        {
            operatorState.Fov = 90;
            operatorState.LastFov = 90;
            operatorState.DelayFov = 90;
            operatorState.ResumeZoom = false;
            operatorState.IsAimingDownSights = false;
        }
    }

    public static void ToggleScopeZoom(
        WeaponAccuracyConfiguration config,
        ref SimulatedWeaponAccuracyState weaponState,
        ref SimulatedOperatorPostureState operatorState)
    {
        if (weaponState.LastShotTimeMs > 0 && operatorState.ClientTimeMs < weaponState.NextSecondaryAttackTimerMs)
        {
            WeaponAccuracyFormulas.SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
            return;
        }

        ToggleScopeZoom(ref operatorState);
        WeaponAccuracyFormulas.SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
    }

    public WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs)
    {
        if (operatorState.Fov <= 0)
        {
            int initFov = operatorState.IsAimingDownSights ? 40 : 90;
            operatorState.Fov = initFov;
            operatorState.LastFov = initFov;
            operatorState.DelayFov = initFov;
        }

        if (weaponState.LastShotTimeMs > 0 &&
            (timeSinceLastShotMs >= config.AttackIntervalMs || operatorState.ClientTimeMs >= weaponState.NextAttackTimerMs))
        {
            if (operatorState.ResumeZoom && operatorState.ClientTimeMs >= weaponState.NextAttackTimerMs)
            {
                int restoredFov = operatorState.LastFov > 0 ? operatorState.LastFov : 90;
                operatorState.Fov = restoredFov;
                operatorState.DelayFov = restoredFov;
                operatorState.ResumeZoom = false;
                operatorState.IsAimingDownSights = restoredFov < 90;
            }

            weaponState.ShotsFired = 0;
            weaponState.DelayFire = false;
        }

        weaponState.DelayFire = true;
        weaponState.ShotsFired++;
        if (weaponState.ClipAmmo > 0) weaponState.ClipAmmo--;

        OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
        double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(config, posture);

        int fovBefore = WeaponAccuracyFormulas.ResolveEffectiveFov(in operatorState);
        operatorState.Fov = fovBefore;
        bool isScoped = fovBefore < 90;
        double spread = WeaponAccuracyFormulas.ComputeSniperSpread(config, baseParam, posture, isScoped, fovBefore);

        int fovAfter = fovBefore;
        if (isScoped && config.AutoCloseScopeAfterShot && fovBefore != 90)
        {
            operatorState.ResumeZoom = true;
            operatorState.LastFov = fovBefore;
            operatorState.DelayFov = 90;
            operatorState.Fov = 90;
            operatorState.IsAimingDownSights = false;
            fovAfter = 90;
        }

        var (punchYaw, punchPitch) = WeaponAccuracyFormulas.CalculateSniperRecoilKick(config, weaponState.ShotsFired, posture, isScoped, fovBefore);
        weaponState.PunchYaw = punchYaw;
        weaponState.PunchPitch = punchPitch;
        weaponState.Accuracy = config.DefaultAccuracy;
        weaponState.SpreadScaleY = config.SpreadScaleY;
        weaponState.Spread = WeaponAccuracyFormulas.ComputeSniperSpread(config, baseParam, posture, fovAfter < 90, fovAfter);
        weaponState.LastShotTimeMs = operatorState.ClientTimeMs;
        weaponState.NextAttackTimerMs = operatorState.ClientTimeMs + config.AttackIntervalMs;
        weaponState.NextSecondaryAttackTimerMs = weaponState.NextAttackTimerMs;
        double mult = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired);

        return new WeaponShotEvaluation(
            config.WeaponId,
            Category,
            posture,
            isScoped,
            isScoped,
            fovBefore,
            fovAfter,
            weaponState.ShotsFired,
            weaponState.Accuracy,
            spread,
            config.SpreadScaleY,
            mult,
            spread * mult,
            spread * mult * config.SpreadScaleY,
            Math.Max(1, config.PelletCount),
            punchYaw,
            punchPitch);
    }
}

public sealed class LightMachineGunCategoryModel : IWeaponCategoryAccuracyModel
{
    public WeaponCategory Category => WeaponCategory.LightMachineGun;

    public void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState) =>
        WeaponAccuracyFormulas.ApplyDeployReset(config, ref weaponState, ref operatorState);

    public void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld) =>
        WeaponAccuracyFormulas.ApplyIdleDecay(config, ref weaponState, ref operatorState, attackHeld, secondaryAttackHeld);

    public WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs)
    {
        weaponState.DelayFire = true;
        weaponState.ShotsFired++;
        if (weaponState.ClipAmmo > 0) weaponState.ClipAmmo--;

        if (config.AccuracyDivisor > 0.0)
        {
            double s = weaponState.ShotsFired;
            double acc = (s * s * s) / config.AccuracyDivisor + config.AccuracyOffset;
            weaponState.Accuracy = Math.Clamp(acc, config.DefaultAccuracy, config.MaxInaccuracy);
        }

        OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
        double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(config, posture);
        bool isAds = operatorState.IsAimingDownSights || operatorState.Fov < 90 || weaponState.SpecialMode;
        double spread = baseParam * weaponState.Accuracy;
        if (isAds)
            spread *= config.AdsSpreadMultiplier;

        var (punchYaw, punchPitch) = WeaponAccuracyFormulas.CalculateRecoilKick(config, weaponState.ShotsFired, posture, isAds);
        weaponState.PunchYaw = punchYaw;
        weaponState.PunchPitch = punchPitch;
        weaponState.Spread = spread;
        weaponState.LastShotTimeMs = operatorState.ClientTimeMs;
        weaponState.NextAttackTimerMs = operatorState.ClientTimeMs + config.AttackIntervalMs;
        double mult = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired);

        return new WeaponShotEvaluation(
            config.WeaponId,
            Category,
            posture,
            isAds,
            operatorState.Fov < 90,
            operatorState.Fov,
            operatorState.Fov,
            weaponState.ShotsFired,
            weaponState.Accuracy,
            spread,
            config.SpreadScaleY,
            mult,
            spread * mult,
            spread * mult * config.SpreadScaleY,
            Math.Max(1, config.PelletCount),
            punchYaw,
            punchPitch);
    }
}

public sealed class SubmachineGunCategoryModel : IWeaponCategoryAccuracyModel
{
    public WeaponCategory Category => WeaponCategory.SubmachineGun;

    public void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState) =>
        WeaponAccuracyFormulas.ApplyDeployReset(config, ref weaponState, ref operatorState);

    public void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld) =>
        WeaponAccuracyFormulas.ApplyIdleDecay(config, ref weaponState, ref operatorState, attackHeld, secondaryAttackHeld);

    public WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs)
    {
        weaponState.DelayFire = true;
        weaponState.ShotsFired++;
        if (weaponState.ClipAmmo > 0) weaponState.ClipAmmo--;

        // Mirrors SubmachineAccuracyLogic: quadratic in ShotsFired
        if (config.AccuracyDivisor > 0.0)
        {
            double s = weaponState.ShotsFired;
            double acc = (s * s) / config.AccuracyDivisor + config.AccuracyOffset;
            weaponState.Accuracy = Math.Clamp(acc, config.DefaultAccuracy, config.MaxInaccuracy);
        }

        OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
        double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(config, posture);
        bool isAds = operatorState.IsAimingDownSights || operatorState.Fov < 90 || weaponState.SpecialMode;
        double spread = baseParam * weaponState.Accuracy;
        if (isAds)
            spread *= config.AdsSpreadMultiplier;

        var (punchYaw, punchPitch) = WeaponAccuracyFormulas.CalculateRecoilKick(config, weaponState.ShotsFired, posture, isAds);
        weaponState.PunchYaw = punchYaw;
        weaponState.PunchPitch = punchPitch;
        weaponState.Spread = spread;
        weaponState.LastShotTimeMs = operatorState.ClientTimeMs;
        weaponState.NextAttackTimerMs = operatorState.ClientTimeMs + config.AttackIntervalMs;
        double mult = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired);

        return new WeaponShotEvaluation(
            config.WeaponId,
            Category,
            posture,
            isAds,
            operatorState.Fov < 90,
            operatorState.Fov,
            operatorState.Fov,
            weaponState.ShotsFired,
            weaponState.Accuracy,
            spread,
            config.SpreadScaleY,
            mult,
            spread * mult,
            spread * mult * config.SpreadScaleY,
            Math.Max(1, config.PelletCount),
            punchYaw,
            punchPitch);
    }
}

public sealed class PistolCategoryModel : IWeaponCategoryAccuracyModel
{
    public WeaponCategory Category => WeaponCategory.Pistol;

    public void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState) =>
        WeaponAccuracyFormulas.ApplyDeployReset(config, ref weaponState, ref operatorState);

    public void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld)
    {
        if (!attackHeld)
        {
            weaponState.DelayFire = false;
            long elapsedSinceShotMs = weaponState.LastShotTimeMs > 0 && operatorState.ClientTimeMs >= weaponState.LastShotTimeMs
                ? operatorState.ClientTimeMs - weaponState.LastShotTimeMs
                : 1000L;

            if (elapsedSinceShotMs >= 300L)
            {
                weaponState.ShotsFired = 0;
                double recoverySeconds = (elapsedSinceShotMs - 300L) / 1000.0;
                double recovered = weaponState.Accuracy + config.PistolDecayRate * Math.Max(0.05, recoverySeconds);
                weaponState.Accuracy = Math.Clamp(recovered, config.PistolMinAccuracy, config.PistolMaxAccuracy);
                weaponState.PunchYaw = config.BasePunchYaw;
                weaponState.PunchPitch = config.BasePunchPitch;
            }
            else if (weaponState.ShotsFired == 0)
            {
                weaponState.Accuracy = config.DefaultAccuracy;
            }
        }

        WeaponAccuracyFormulas.SynchronizePostureAndScopeSpread(config, ref weaponState, in operatorState);
    }

    public WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs)
    {
        bool isFirstRestedShot = weaponState.ShotsFired == 0 && weaponState.LastShotTimeMs == 0L;
        weaponState.ShotsFired++;
        if (weaponState.ClipAmmo > 0) weaponState.ClipAmmo--;

        double dtSeconds = isFirstRestedShot
            ? Math.Max(0.30, timeSinceLastShotMs / 1000.0)
            : timeSinceLastShotMs / 1000.0;
        double updatedAccuracy = weaponState.Accuracy - config.PistolDecayRate * (0.3 - dtSeconds);
        weaponState.Accuracy = Math.Clamp(updatedAccuracy, config.PistolMinAccuracy, config.PistolMaxAccuracy);

        OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
        double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(config, posture);
        bool isAds = operatorState.IsAimingDownSights || operatorState.Fov < 90 || weaponState.SpecialMode;
        double spread = baseParam * (1.0 - weaponState.Accuracy);
        if (isAds)
            spread *= config.AdsSpreadMultiplier;

        var (punchYaw, punchPitch) = WeaponAccuracyFormulas.CalculateRecoilKick(config, weaponState.ShotsFired, posture, isAds);
        if (dtSeconds >= 0.35)
        {
            punchYaw = config.BasePunchYaw;
            punchPitch = config.BasePunchPitch;
        }

        weaponState.PunchYaw = punchYaw;
        weaponState.PunchPitch = punchPitch;
        weaponState.Spread = spread;
        weaponState.LastShotTimeMs = operatorState.ClientTimeMs;
        weaponState.NextAttackTimerMs = operatorState.ClientTimeMs + config.AttackIntervalMs;
        double mult = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired);

        return new WeaponShotEvaluation(
            config.WeaponId,
            Category,
            posture,
            isAds,
            operatorState.Fov < 90,
            operatorState.Fov,
            operatorState.Fov,
            weaponState.ShotsFired,
            weaponState.Accuracy,
            spread,
            config.SpreadScaleY,
            mult,
            spread * mult,
            spread * mult * config.SpreadScaleY,
            Math.Max(1, config.PelletCount),
            punchYaw,
            punchPitch);
    }
}

public sealed class ShotgunCategoryModel : IWeaponCategoryAccuracyModel
{
    public WeaponCategory Category => WeaponCategory.Shotgun;

    public void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState) =>
        WeaponAccuracyFormulas.ApplyDeployReset(config, ref weaponState, ref operatorState);

    public void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld) =>
        WeaponAccuracyFormulas.ApplyIdleDecay(config, ref weaponState, ref operatorState, attackHeld, secondaryAttackHeld);

    public WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs)
    {
        weaponState.DelayFire = true;
        weaponState.ShotsFired++;
        if (weaponState.ClipAmmo > 0) weaponState.ClipAmmo--;

        if (config.AccuracyDivisor > 0.0)
        {
            double bloom = config.DefaultAccuracy + (weaponState.ShotsFired - 1.0) / config.AccuracyDivisor + config.AccuracyOffset;
            weaponState.Accuracy = Math.Clamp(bloom, config.DefaultAccuracy, config.MaxInaccuracy);
        }
        else
        {
            weaponState.Accuracy = config.DefaultAccuracy;
        }

        OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
        double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(config, posture);
        bool isAds = operatorState.IsAimingDownSights || operatorState.Fov < 90 || weaponState.SpecialMode;
        double spread = baseParam * weaponState.Accuracy;
        if (isAds)
            spread *= config.AdsSpreadMultiplier;

        int pellets = Math.Max(1, config.PelletCount);
        var (punchYaw, punchPitch) = WeaponAccuracyFormulas.CalculateRecoilKick(config, weaponState.ShotsFired, posture, isAds);
        weaponState.PelletCount = pellets;
        weaponState.PunchYaw = punchYaw;
        weaponState.PunchPitch = punchPitch;
        weaponState.Spread = spread;
        weaponState.LastShotTimeMs = operatorState.ClientTimeMs;
        weaponState.NextAttackTimerMs = operatorState.ClientTimeMs + config.AttackIntervalMs;
        double mult = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired);

        return new WeaponShotEvaluation(
            config.WeaponId,
            Category,
            posture,
            isAds,
            operatorState.Fov < 90,
            operatorState.Fov,
            operatorState.Fov,
            weaponState.ShotsFired,
            weaponState.Accuracy,
            spread,
            config.SpreadScaleY,
            mult,
            spread * mult,
            spread * mult * config.SpreadScaleY,
            pellets,
            punchYaw,
            punchPitch);
    }

    public static ShotgunPelletClusterEvaluation EvaluatePelletCluster(
        in WeaponShotEvaluation eval,
        int seed,
        double rawYawDegrees,
        double rawPitchDegrees,
        bool cancelRecoilPunch,
        TriangularSpreadDirectionModel directionModel,
        ISpreadCompensationSolver solver)
    {
        ArgumentNullException.ThrowIfNull(directionModel);
        ArgumentNullException.ThrowIfNull(solver);

        int pelletCount = Math.Max(1, eval.PelletCount);
        double punchYaw = eval.PunchYaw * 2.0;
        double punchPitch = eval.PunchPitch * 2.0;
        double targetYaw = solver.WrapAngleDegrees(rawYawDegrees + (cancelRecoilPunch ? 0.0 : punchYaw));
        double targetPitch = rawPitchDegrees + (cancelRecoilPunch ? 0.0 : punchPitch);

        Vector3D rawAimDir = directionModel.CalculateDirection(seed, rawYawDegrees, rawPitchDegrees, 0.0, eval.SpreadScaleY, eval.ShotsFiredSpreadMultiplier);
        Vector3D recoiledDir = directionModel.CalculateDirection(seed, rawYawDegrees + punchYaw, rawPitchDegrees + punchPitch, 0.0, eval.SpreadScaleY, eval.ShotsFiredSpreadMultiplier);
        Vector3D targetDir = directionModel.CalculateDirection(seed, targetYaw, targetPitch, 0.0, eval.SpreadScaleY, eval.ShotsFiredSpreadMultiplier);
        double recoilOnlyErr = solver.ComputeAngularErrorDegrees(recoiledDir, rawAimDir);

        var uncompensatedPellets = directionModel.CalculatePelletDirections(
            seed,
            (float)(rawYawDegrees + (float)punchYaw),
            (float)(rawPitchDegrees + (float)punchPitch),
            eval.Spread,
            eval.SpreadScaleY,
            eval.ShotsFiredSpreadMultiplier,
            pelletCount);

        Vector3D uncompCentroid = ComputeClusterCentroid(uncompensatedPellets);
        double uncompPrimaryErr = solver.ComputeAngularErrorDegrees(uncompensatedPellets[0], targetDir);
        double uncompCentroidErr = solver.ComputeAngularErrorDegrees(uncompCentroid, targetDir);

        double spread = eval.Spread;
        float scaleY = eval.SpreadScaleY;
        double mult = eval.ShotsFiredSpreadMultiplier;

        Func<double, double, Vector3D> evaluator = (y, p) =>
            directionModel.CalculateDirection(seed, y, p, spread, scaleY, mult);

        double compContErr = uncompPrimaryErr;
        double compQuantErr = uncompPrimaryErr;
        double compensatedEvalYaw = rawYawDegrees + punchYaw;
        double compensatedEvalPitch = rawPitchDegrees + punchPitch;

        if (solver.TrySolveInverse(evaluator, targetDir, targetYaw, targetPitch, out var sol) &&
            solver.TryQuantizeCommandAngles(evaluator, targetDir, sol.YawDegrees, sol.PitchDegrees, punchYaw, punchPitch, out var quant))
        {
            compContErr = quant.ContinuousErrorDegrees;
            compQuantErr = quant.QuantizedErrorDegrees;
            compensatedEvalYaw = (float)(quant.Yaw16 / 100f + (float)punchYaw);
            compensatedEvalPitch = (float)(quant.Pitch16 / 100f + (float)punchPitch);
        }

        var compensatedPellets = directionModel.CalculatePelletDirections(
            seed,
            compensatedEvalYaw,
            compensatedEvalPitch,
            eval.Spread,
            eval.SpreadScaleY,
            eval.ShotsFiredSpreadMultiplier,
            pelletCount);

        Vector3D compCentroid = ComputeClusterCentroid(compensatedPellets);
        double compCentroidErr = solver.ComputeAngularErrorDegrees(compCentroid, targetDir);

        double sumPelletCone = 0.0;
        double maxPelletCone = 0.0;
        if (pelletCount > 1)
        {
            for (int i = 1; i < pelletCount; i++)
            {
                double coneAngle = solver.ComputeAngularErrorDegrees(compensatedPellets[i], compensatedPellets[0]);
                sumPelletCone += coneAngle;
                if (coneAngle > maxPelletCone)
                    maxPelletCone = coneAngle;
            }
            sumPelletCone /= (pelletCount - 1);
        }

        return new ShotgunPelletClusterEvaluation(
            eval.WeaponId,
            seed,
            pelletCount,
            eval.Posture,
            eval.IsAimingDownSights,
            uncompPrimaryErr,
            uncompCentroidErr,
            recoilOnlyErr,
            compContErr,
            compQuantErr,
            compCentroidErr,
            sumPelletCone,
            maxPelletCone,
            uncompensatedPellets,
            compensatedPellets);
    }

    private static Vector3D ComputeClusterCentroid(IReadOnlyList<Vector3D> directions)
    {
        Vector3D sum = Vector3D.Zero;
        foreach (var d in directions)
        {
            if (d.TryNormalize(out var u))
                sum = sum.Add(u);
        }
        return sum.TryNormalize(out var unit) ? unit : directions[0];
    }
}

public sealed class CustomCategoryModel : IWeaponCategoryAccuracyModel
{
    public WeaponCategory Category => WeaponCategory.Custom;

    public void Deploy(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState) =>
        WeaponAccuracyFormulas.ApplyDeployReset(config, ref weaponState, ref operatorState);

    public void ProcessIdleFrame(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld) =>
        WeaponAccuracyFormulas.ApplyIdleDecay(config, ref weaponState, ref operatorState, attackHeld, secondaryAttackHeld);

    public WeaponShotEvaluation EvaluateFire(WeaponAccuracyConfiguration config, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int timeSinceLastShotMs)
    {
        weaponState.DelayFire = true;
        weaponState.ShotsFired++;
        if (weaponState.ClipAmmo > 0) weaponState.ClipAmmo--;

        weaponState.Accuracy = WeaponAccuracyFormulas.CalculateAccuracyForShots(config, weaponState.ShotsFired, weaponState.Accuracy);

        OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(config, in operatorState);
        double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(config, posture);
        bool isAds = operatorState.IsAimingDownSights || operatorState.Fov < 90 || weaponState.SpecialMode;
        double spread = baseParam * weaponState.Accuracy;
        if (isAds)
            spread *= config.AdsSpreadMultiplier;

        var (punchYaw, punchPitch) = WeaponAccuracyFormulas.CalculateRecoilKick(config, weaponState.ShotsFired, posture, isAds);
        weaponState.PunchYaw = punchYaw;
        weaponState.PunchPitch = punchPitch;
        weaponState.Spread = spread;
        weaponState.LastShotTimeMs = operatorState.ClientTimeMs;
        weaponState.NextAttackTimerMs = operatorState.ClientTimeMs + config.AttackIntervalMs;
        double mult = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired);

        return new WeaponShotEvaluation(
            config.WeaponId,
            Category,
            posture,
            isAds,
            operatorState.Fov < 90,
            operatorState.Fov,
            operatorState.Fov,
            weaponState.ShotsFired,
            weaponState.Accuracy,
            spread,
            config.SpreadScaleY,
            mult,
            spread * mult,
            spread * mult * config.SpreadScaleY,
            Math.Max(1, config.PelletCount),
            punchYaw,
            punchPitch);
    }
}

public sealed class WeaponCategorySimulationEngine
{
    private readonly Dictionary<string, WeaponAccuracyConfiguration> _configs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<WeaponCategory, IWeaponCategoryAccuracyModel> _models = new();
    private readonly IWeaponRegistry? _weaponRegistry;

    public WeaponCategorySimulationEngine(IWeaponRegistry? weaponRegistry = null)
    {
        _weaponRegistry = weaponRegistry;

        RegisterModel(new AssaultRifleCategoryModel());
        RegisterModel(new SniperRifleCategoryModel());
        RegisterModel(new LightMachineGunCategoryModel());
        RegisterModel(new SubmachineGunCategoryModel());
        RegisterModel(new PistolCategoryModel());
        RegisterModel(new ShotgunCategoryModel());
        RegisterModel(new CustomCategoryModel());

        foreach (var preset in CreateDefaultConfigurations())
        {
            RegisterConfiguration(preset);
        }
    }

    public void RegisterModel(IWeaponCategoryAccuracyModel model) => _models[model.Category] = model;

    public void RegisterConfiguration(WeaponAccuracyConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _configs[config.WeaponId] = config;
        string normalized = WeaponRegistry.NormalizeWeaponKey(config.WeaponId);
        if (!string.IsNullOrEmpty(normalized))
        {
            _configs.TryAdd(normalized, config);
            _configs.TryAdd(normalized + "_Sim", config);
        }
    }

    public bool TryGetConfiguration(string weaponId, out WeaponAccuracyConfiguration? config)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            config = null;
            return false;
        }

        string trimmed = weaponId.Trim();
        if (_configs.TryGetValue(trimmed, out config))
            return true;

        string normalized = WeaponRegistry.NormalizeWeaponKey(trimmed);
        if (!string.IsNullOrEmpty(normalized))
        {
            if (_configs.TryGetValue(normalized, out config))
                return true;
            if (_configs.TryGetValue(normalized + "_Sim", out config))
                return true;
            if (WeaponRegistry.TryResolveSniperAliasKey(normalized, out string sniperAliasKey) &&
                _configs.TryGetValue(sniperAliasKey, out config))
            {
                return true;
            }
        }

        if (_weaponRegistry is not null && _weaponRegistry.TryGetProfile(trimmed, out var profile) && profile is not null)
        {
            config = FromSimulationProfile(profile);
            _configs[trimmed] = config;
            return true;
        }

        config = null;
        return false;
    }

    public WeaponAccuracyConfiguration GetConfiguration(string weaponId)
    {
        if (TryGetConfiguration(weaponId, out var config) && config is not null)
            return config;

        throw new KeyNotFoundException($"Weapon accuracy configuration '{weaponId}' is not registered.");
    }

    public IWeaponCategoryAccuracyModel GetModel(WeaponCategory category)
    {
        if (_models.TryGetValue(category, out var model))
            return model;

        throw new KeyNotFoundException($"No IWeaponCategoryAccuracyModel registered for category '{category}'.");
    }

    public IReadOnlyCollection<WeaponAccuracyConfiguration> GetAllConfigurations() =>
        _configs.Values.DistinctBy(c => c.WeaponId, StringComparer.OrdinalIgnoreCase).ToArray();

    public static WeaponAccuracyConfiguration FromSimulationProfile(WeaponSimulationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        WeaponAccuracyFormulaKind formula = profile.Category switch
        {
            WeaponCategory.AssaultRifle => WeaponAccuracyFormulaKind.RifleCubic,
            WeaponCategory.SniperRifle => WeaponAccuracyFormulaKind.SniperScopedPosture,
            WeaponCategory.LightMachineGun => WeaponAccuracyFormulaKind.LmgSustainedBelt,
            WeaponCategory.SubmachineGun => WeaponAccuracyFormulaKind.SmgQuadratic,
            WeaponCategory.Pistol => WeaponAccuracyFormulaKind.PistolIntervalDecay,
            WeaponCategory.Shotgun => WeaponAccuracyFormulaKind.ShotgunPelletFixed,
            _ => WeaponAccuracyFormulaKind.CustomLinear
        };

        bool isSniper = profile.Category == WeaponCategory.SniperRifle;
        string sniperKey = string.Empty;
        if (isSniper)
            WeaponRegistry.TryResolveSniperAliasKey(profile.WeaponId, out sniperKey);

        double stand = profile.GetSpreadForState(MovementStateKind.Stand);
        double crouch = profile.SpreadByMovementState.TryGetValue(MovementStateKind.Crouch, out double c) ? c : stand * 0.65;
        double walk = profile.GetSpreadForState(MovementStateKind.Walk);
        double run = profile.GetSpreadForState(MovementStateKind.Run);
        double air = profile.GetSpreadForState(MovementStateKind.JumpAir);
        double ads = profile.SpreadByMovementState.TryGetValue(MovementStateKind.AimDownSights, out double a) ? a : stand * 0.70;
        double adsMult = isSniper
            ? (sniperKey == "scout" ? 0.72 : 0.75)
            : (stand > 1e-9 ? Math.Clamp(ads / stand, 0.20, 1.00) : 0.70);
        int maxSpreadFactor = (int)Math.Round((1.0 - profile.ShotsFiredMultiplier) * 1000.0);

        double sniperUnscopedExtra = sniperKey switch
        {
            "barrett" => 0.0950,
            "scout" => 0.0450,
            _ => 0.0800
        };
        int sniperAttackIntervalMs = sniperKey switch
        {
            "barrett" => 1450,
            "scout" => 1050,
            _ => 1250
        };
        int sniperRaiseDurationMs = sniperKey switch
        {
            "barrett" => 1050,
            "scout" => 700,
            _ => 850
        };
        double sniperWalkThreshold = sniperKey == "scout" ? 30.0 : 25.0;
        double sniperRunThreshold = sniperKey switch
        {
            "barrett" => 120.0,
            "scout" => 165.0,
            _ => 140.0
        };

        return new WeaponAccuracyConfiguration(
            WeaponId: profile.WeaponId,
            DisplayName: profile.DisplayName,
            Category: profile.Category,
            FormulaKind: formula,
            WeaponTypeCode: profile.Category == WeaponCategory.Pistol ? 2 : 1,
            DefaultAccuracy: 1.0,
            AccuracyDivisor: isSniper ? -1.0 : 120.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: isSniper ? 1.0 : 1.65,
            PistolDecayRate: isSniper ? 0.0 : 0.30,
            PistolMinAccuracy: isSniper ? 1.0 : 0.45,
            PistolMaxAccuracy: isSniper ? 1.0 : 0.90,
            StandSpreadParam: stand,
            CrouchSpreadParam: crouch,
            WalkSpreadParam: walk,
            RunSpreadParam: run,
            AirSpreadParam: air,
            UnscopedExtraSpread: isSniper ? sniperUnscopedExtra : 0.0,
            AdsSpreadMultiplier: adsMult,
            WalkSpeedThreshold: isSniper ? sniperWalkThreshold : 25.0,
            RunSpeedThreshold: isSniper ? sniperRunThreshold : 140.0,
            SpreadScaleY: profile.SpreadScaleY,
            AttackIntervalMs: isSniper ? sniperAttackIntervalMs : 95,
            ShotsFiredSpreadMin: isSniper ? 0 : Math.Min(0, maxSpreadFactor / 4),
            ShotsFiredSpreadMax: isSniper ? 0 : maxSpreadFactor,
            ShotsFiredSpreadTimeMs: isSniper ? 0 : 900,
            MaxClip: profile.ClipCapacity,
            RaiseDurationMs: isSniper ? sniperRaiseDurationMs : 550,
            AutoCloseScopeAfterShot: isSniper,
            PelletCount: profile.PelletCount,
            BasePunchYaw: profile.DefaultPunchYaw,
            BasePunchPitch: profile.DefaultPunchPitch,
            MaxPunchYaw: Math.Max(isSniper ? 0.45f : 1.0f, profile.DefaultPunchYaw * 3.0f),
            MaxPunchPitch: Math.Max(isSniper ? 2.4f : 2.5f, profile.DefaultPunchPitch * 1.6f));
    }

    public static WeaponCategory ClassifyWeapon(string weaponId, string? attackType = null)
    {
        string key = (weaponId ?? string.Empty).Trim().ToLowerInvariant();
        string atk = (attackType ?? string.Empty).Trim().ToLowerInvariant();

        if (key.StartsWith("custom:", StringComparison.Ordinal) || key.Contains("custom") || key.Contains("prototype") || key.Contains("pulse") || key.Contains("rail") || key.Contains("experimental") ||
            atk.Contains("custom") || atk.Contains("prototype"))
            return WeaponCategory.Custom;

        if (key.Contains("xm1014") || key.Contains("m3") || key.Contains("shotgun") || key.Contains("spas") || key.Contains("remington") || key.Contains("aa12") || key.Contains("nova") || key.Contains("mag7") ||
            atk.Contains("shotgun") || atk.Contains("pellet"))
            return WeaponCategory.Shotgun;

        if (key.Contains("awp") || key.Contains("awm") || key.Contains("barrett") || key.Contains("scout") ||
            key.Contains("sniper") || key.Contains("m99") || key.Contains("m82") || key.Contains("m107") ||
            key.Contains("g3sg1") || key.Contains("vss") || key.Contains("dsr") || key.Contains("ssg") ||
            key.Contains("svd") || key.Contains("dragunov") || key.Contains("kar98") || key.Contains("l96") ||
            atk.Contains("sniper") || atk.Contains("barrett") || atk.Contains("scout") || atk.Contains("lightsniper"))
            return WeaponCategory.SniperRifle;

        if (key.Contains("m249") || key.Contains("mg3") || key.Contains("rpk") || key.Contains("gatling") || key.Contains("m60") || key.Contains("lmg") || key.Contains("machinegun") || key.Contains("negev") ||
            atk.Contains("heavymachine") || atk.Contains("machinegun") || atk.Contains("mg3") || atk.Contains("unlimitedfire"))
            return WeaponCategory.LightMachineGun;

        if (key.Contains("mp5") || key.Contains("p90") || key.Contains("ump") || key.Contains("kriss") || key.Contains("vector") || key.Contains("pp2000") || key.Contains("smg") || key.Contains("submachine") || key.Contains("uzi") || key.Contains("mp7") ||
            atk.Contains("submachine") || atk.Contains("smg") || atk.Contains("pp2000"))
            return WeaponCategory.SubmachineGun;

        if (key.Contains("deagle") || key.Contains("usp") || key.Contains("glock") || key.Contains("g18") || key.Contains("revolver") || key.Contains("pistol") || key.Contains("colt") || key.Contains("m1911") ||
            atk.Contains("pistol") || atk.Contains("revolver"))
            return WeaponCategory.Pistol;

        return WeaponCategory.AssaultRifle;
    }

    public void SwitchWeapon(string newWeaponId, ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState)
    {
        var config = GetConfiguration(newWeaponId);
        var model = GetModel(config.Category);
        model.Deploy(config, ref weaponState, ref operatorState);
    }

    public void SynchronizeState(ref SimulatedWeaponAccuracyState weaponState, in SimulatedOperatorPostureState operatorState)
    {
        var config = GetConfiguration(weaponState.ActiveWeaponId);
        var model = GetModel(config.Category);
        model.SynchronizeState(config, ref weaponState, in operatorState);
    }

    public void ProcessIdleFrame(ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, bool attackHeld, bool secondaryAttackHeld)
    {
        var config = GetConfiguration(weaponState.ActiveWeaponId);
        var model = GetModel(config.Category);
        model.ProcessIdleFrame(config, ref weaponState, ref operatorState, attackHeld, secondaryAttackHeld);
    }

    public WeaponShotEvaluation FireWeapon(ref SimulatedWeaponAccuracyState weaponState, ref SimulatedOperatorPostureState operatorState, int? explicitIntervalMs = null)
    {
        var config = GetConfiguration(weaponState.ActiveWeaponId);
        var model = GetModel(config.Category);
        int intervalMs;
        if (explicitIntervalMs.HasValue)
        {
            intervalMs = explicitIntervalMs.Value;
            operatorState.ClientTimeMs += intervalMs;
        }
        else if (weaponState.LastShotTimeMs > 0 && operatorState.ClientTimeMs >= weaponState.LastShotTimeMs)
        {
            intervalMs = (int)(operatorState.ClientTimeMs - weaponState.LastShotTimeMs);
        }
        else
        {
            intervalMs = 1000;
        }

        return model.EvaluateFire(config, ref weaponState, ref operatorState, intervalMs);
    }

    public static void ApplyEvaluationToSnapshot(in WeaponShotEvaluation eval, SimulatedWeaponSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.WeaponName = eval.WeaponId;
        snapshot.Category = eval.Category;
        snapshot.ShotsFired = eval.ShotsFired;
        snapshot.Accuracy = eval.Accuracy;
        snapshot.Spread = eval.Spread;
        snapshot.SpreadScaleY = eval.SpreadScaleY;
        snapshot.ShotsFiredMultiplier = eval.ShotsFiredSpreadMultiplier;
        snapshot.PelletCount = Math.Max(1, eval.PelletCount);
        snapshot.IsScoped = eval.IsScoped || eval.IsAimingDownSights;
    }

    public static void ApplyEvaluationToActor(
        in WeaponShotEvaluation eval,
        in SimulatedOperatorPostureState operatorState,
        SimulatedActorSnapshot actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var existing = actor.CurrentWeapon as SimulatedWeaponSnapshot;
        var snapshot = existing is not null && string.Equals(existing.WeaponName, eval.WeaponId, StringComparison.OrdinalIgnoreCase)
            ? existing
            : new SimulatedWeaponSnapshot { ClipAmmo = existing?.ClipAmmo ?? 10 };
        ApplyEvaluationToSnapshot(in eval, snapshot);
        actor.CurrentWeapon = snapshot;
        actor.PunchYaw = eval.PunchYaw;
        actor.PunchPitch = eval.PunchPitch;
        actor.IsOnGround = operatorState.IsOnGround;
        actor.IsCrouching = operatorState.DuckState == 0;
        actor.IsAimingDownSights = eval.IsAimingDownSights || eval.IsScoped;
        actor.Fov = eval.FovBeforeShot;
        actor.HorizontalVelocity = operatorState.HorizontalSpeed2D;
    }

    public static void ApplyStateToActor(
        WeaponAccuracyConfiguration config,
        in SimulatedWeaponAccuracyState weaponState,
        in SimulatedOperatorPostureState operatorState,
        SimulatedActorSnapshot actor)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(actor);

        int effectiveFov = WeaponAccuracyFormulas.ResolveEffectiveFov(in operatorState);
        bool isScoped = config.Category == WeaponCategory.SniperRifle
            ? effectiveFov < 90
            : (effectiveFov < 90 || operatorState.IsAimingDownSights || weaponState.SpecialMode);

        actor.CurrentWeapon = new SimulatedWeaponSnapshot
        {
            WeaponName = config.WeaponId,
            Category = config.Category,
            ClipAmmo = weaponState.ClipAmmo,
            ShotsFired = weaponState.ShotsFired,
            Accuracy = weaponState.Accuracy,
            Spread = weaponState.Spread,
            SpreadScaleY = weaponState.SpreadScaleY,
            ShotsFiredMultiplier = WeaponAccuracyFormulas.CalculateShotsFiredSpread(config, weaponState.ShotsFired),
            PelletCount = Math.Max(1, weaponState.PelletCount),
            IsScoped = isScoped
        };
        actor.PunchYaw = weaponState.PunchYaw;
        actor.PunchPitch = weaponState.PunchPitch;
        actor.IsOnGround = operatorState.IsOnGround;
        actor.IsCrouching = operatorState.DuckState == 0;
        actor.IsAimingDownSights = isScoped;
        actor.Fov = effectiveFov;
        actor.HorizontalVelocity = operatorState.HorizontalSpeed2D;
    }

    public static IReadOnlyList<WeaponAccuracyConfiguration> CreateDefaultConfigurations() => new[]
    {
        new WeaponAccuracyConfiguration(
            WeaponId: "m4a1",
            DisplayName: "M4A1 Assault Rifle",
            Category: WeaponCategory.AssaultRifle,
            FormulaKind: WeaponAccuracyFormulaKind.RifleCubic,
            WeaponTypeCode: 1,
            DefaultAccuracy: 0.20,
            AccuracyDivisor: 220.0,
            AccuracyOffset: 0.15,
            MaxInaccuracy: 1.00,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.018,
            CrouchSpreadParam: 0.011,
            WalkSpreadParam: 0.095,
            RunSpreadParam: 0.240,
            AirSpreadParam: 0.550,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.65,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 140.0,
            SpreadScaleY: 0.40f,
            AttackIntervalMs: 90,
            ShotsFiredSpreadMin: -50,
            ShotsFiredSpreadMax: -350,
            ShotsFiredSpreadTimeMs: 900,
            MaxClip: 30,
            RaiseDurationMs: 600,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.30f,
            BasePunchPitch: 0.95f,
            MaxPunchYaw: 1.20f,
            MaxPunchPitch: 3.40f),

        new WeaponAccuracyConfiguration(
            WeaponId: "ak47",
            DisplayName: "AK47 Assault Rifle",
            Category: WeaponCategory.AssaultRifle,
            FormulaKind: WeaponAccuracyFormulaKind.RifleCubic,
            WeaponTypeCode: 1,
            DefaultAccuracy: 0.20,
            AccuracyDivisor: 200.0,
            AccuracyOffset: 0.20,
            MaxInaccuracy: 1.25,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.020,
            CrouchSpreadParam: 0.013,
            WalkSpreadParam: 0.125,
            RunSpreadParam: 0.325,
            AirSpreadParam: 0.750,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.75,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 140.0,
            SpreadScaleY: 0.40f,
            AttackIntervalMs: 100,
            ShotsFiredSpreadMin: -50,
            ShotsFiredSpreadMax: -350,
            ShotsFiredSpreadTimeMs: 1000,
            MaxClip: 30,
            RaiseDurationMs: 650,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.40f,
            BasePunchPitch: 1.20f,
            MaxPunchYaw: 1.60f,
            MaxPunchPitch: 4.20f),

        new WeaponAccuracyConfiguration(
            WeaponId: "awp",
            DisplayName: "AWP Sniper Rifle",
            Category: WeaponCategory.SniperRifle,
            FormulaKind: WeaponAccuracyFormulaKind.SniperScopedPosture,
            WeaponTypeCode: 1,
            DefaultAccuracy: 1.0,
            AccuracyDivisor: -1.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: 1.0,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 1.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.0012,
            CrouchSpreadParam: 0.0005,
            WalkSpreadParam: 0.0950,
            RunSpreadParam: 0.2200,
            AirSpreadParam: 0.4500,
            UnscopedExtraSpread: 0.0800,
            AdsSpreadMultiplier: 0.75,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 140.0,
            SpreadScaleY: 1.00f,
            AttackIntervalMs: 1250,
            ShotsFiredSpreadMin: 0,
            ShotsFiredSpreadMax: 0,
            ShotsFiredSpreadTimeMs: 0,
            MaxClip: 10,
            RaiseDurationMs: 850,
            AutoCloseScopeAfterShot: true,
            PelletCount: 1,
            BasePunchYaw: 0.15f,
            BasePunchPitch: 2.20f,
            MaxPunchYaw: 0.60f,
            MaxPunchPitch: 3.50f),

        new WeaponAccuracyConfiguration(
            WeaponId: "barrett",
            DisplayName: "Barrett M82A1 Heavy Sniper",
            Category: WeaponCategory.SniperRifle,
            FormulaKind: WeaponAccuracyFormulaKind.SniperScopedPosture,
            WeaponTypeCode: 1,
            DefaultAccuracy: 1.0,
            AccuracyDivisor: -1.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: 1.0,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 1.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.0008,
            CrouchSpreadParam: 0.0004,
            WalkSpreadParam: 0.1350,
            RunSpreadParam: 0.3500,
            AirSpreadParam: 0.6500,
            UnscopedExtraSpread: 0.0950,
            AdsSpreadMultiplier: 0.75,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 120.0,
            SpreadScaleY: 1.00f,
            AttackIntervalMs: 1450,
            ShotsFiredSpreadMin: 0,
            ShotsFiredSpreadMax: 0,
            ShotsFiredSpreadTimeMs: 0,
            MaxClip: 10,
            RaiseDurationMs: 1050,
            AutoCloseScopeAfterShot: true,
            PelletCount: 1,
            BasePunchYaw: 0.25f,
            BasePunchPitch: 2.80f,
            MaxPunchYaw: 0.80f,
            MaxPunchPitch: 4.20f),

        new WeaponAccuracyConfiguration(
            WeaponId: "scout",
            DisplayName: "Steyr Scout / SSG-08 Light Sniper",
            Category: WeaponCategory.SniperRifle,
            FormulaKind: WeaponAccuracyFormulaKind.SniperScopedPosture,
            WeaponTypeCode: 1,
            DefaultAccuracy: 1.0,
            AccuracyDivisor: -1.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: 1.0,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 1.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.0014,
            CrouchSpreadParam: 0.0006,
            WalkSpreadParam: 0.0550,
            RunSpreadParam: 0.1350,
            AirSpreadParam: 0.2400,
            UnscopedExtraSpread: 0.0450,
            AdsSpreadMultiplier: 0.72,
            WalkSpeedThreshold: 30.0,
            RunSpeedThreshold: 165.0,
            SpreadScaleY: 1.00f,
            AttackIntervalMs: 1050,
            ShotsFiredSpreadMin: 0,
            ShotsFiredSpreadMax: 0,
            ShotsFiredSpreadTimeMs: 0,
            MaxClip: 10,
            RaiseDurationMs: 700,
            AutoCloseScopeAfterShot: true,
            PelletCount: 1,
            BasePunchYaw: 0.10f,
            BasePunchPitch: 1.45f,
            MaxPunchYaw: 0.45f,
            MaxPunchPitch: 2.40f),

        new WeaponAccuracyConfiguration(
            WeaponId: "m249",
            DisplayName: "M249 Light Machine Gun",
            Category: WeaponCategory.LightMachineGun,
            FormulaKind: WeaponAccuracyFormulaKind.LmgSustainedBelt,
            WeaponTypeCode: 1,
            DefaultAccuracy: 0.25,
            AccuracyDivisor: 180.0,
            AccuracyOffset: 0.25,
            MaxInaccuracy: 1.60,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.028,
            CrouchSpreadParam: 0.015,
            WalkSpreadParam: 0.140,
            RunSpreadParam: 0.340,
            AirSpreadParam: 0.650,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.72,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 130.0,
            SpreadScaleY: 0.55f,
            AttackIntervalMs: 85,
            ShotsFiredSpreadMin: -100,
            ShotsFiredSpreadMax: -450,
            ShotsFiredSpreadTimeMs: 1500,
            MaxClip: 100,
            RaiseDurationMs: 900,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.50f,
            BasePunchPitch: 1.45f,
            MaxPunchYaw: 2.10f,
            MaxPunchPitch: 5.20f),

        new WeaponAccuracyConfiguration(
            WeaponId: "mp5",
            DisplayName: "MP5 Submachine Gun",
            Category: WeaponCategory.SubmachineGun,
            FormulaKind: WeaponAccuracyFormulaKind.SmgQuadratic,
            WeaponTypeCode: 1,
            DefaultAccuracy: 0.20,
            AccuracyDivisor: 220.0,
            AccuracyOffset: 0.16,
            MaxInaccuracy: 0.85,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.022,
            CrouchSpreadParam: 0.014,
            WalkSpreadParam: 0.048,
            RunSpreadParam: 0.110,
            AirSpreadParam: 0.320,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.70,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 160.0,
            SpreadScaleY: 0.50f,
            AttackIntervalMs: 75,
            ShotsFiredSpreadMin: -30,
            ShotsFiredSpreadMax: -220,
            ShotsFiredSpreadTimeMs: 800,
            MaxClip: 35,
            RaiseDurationMs: 500,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.22f,
            BasePunchPitch: 0.65f,
            MaxPunchYaw: 0.90f,
            MaxPunchPitch: 2.30f),

        new WeaponAccuracyConfiguration(
            WeaponId: "p90",
            DisplayName: "FN P90 Submachine Gun",
            Category: WeaponCategory.SubmachineGun,
            FormulaKind: WeaponAccuracyFormulaKind.SmgQuadratic,
            WeaponTypeCode: 1,
            DefaultAccuracy: 0.22,
            AccuracyDivisor: 240.0,
            AccuracyOffset: 0.20,
            MaxInaccuracy: 0.90,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.024,
            CrouchSpreadParam: 0.015,
            WalkSpreadParam: 0.052,
            RunSpreadParam: 0.115,
            AirSpreadParam: 0.340,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.72,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 165.0,
            SpreadScaleY: 0.50f,
            AttackIntervalMs: 70,
            ShotsFiredSpreadMin: -35,
            ShotsFiredSpreadMax: -240,
            ShotsFiredSpreadTimeMs: 850,
            MaxClip: 50,
            RaiseDurationMs: 520,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.20f,
            BasePunchPitch: 0.60f,
            MaxPunchYaw: 0.85f,
            MaxPunchPitch: 2.20f),

        new WeaponAccuracyConfiguration(
            WeaponId: "deagle",
            DisplayName: "Desert Eagle Pistol",
            Category: WeaponCategory.Pistol,
            FormulaKind: WeaponAccuracyFormulaKind.PistolIntervalDecay,
            WeaponTypeCode: 2,
            DefaultAccuracy: 0.90,
            AccuracyDivisor: -1.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: 1.0,
            PistolDecayRate: 0.35,
            PistolMinAccuracy: 0.45,
            PistolMaxAccuracy: 0.90,
            StandSpreadParam: 0.040,
            CrouchSpreadParam: 0.026,
            WalkSpreadParam: 0.120,
            RunSpreadParam: 0.240,
            AirSpreadParam: 0.480,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.75,
            WalkSpeedThreshold: 13.0,
            RunSpeedThreshold: 140.0,
            SpreadScaleY: 0.65f,
            AttackIntervalMs: 220,
            ShotsFiredSpreadMin: 0,
            ShotsFiredSpreadMax: 0,
            ShotsFiredSpreadTimeMs: 0,
            MaxClip: 7,
            RaiseDurationMs: 450,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.35f,
            BasePunchPitch: 1.60f,
            MaxPunchYaw: 1.20f,
            MaxPunchPitch: 3.60f),

        new WeaponAccuracyConfiguration(
            WeaponId: "usp45",
            DisplayName: "USP .45 Tactical Pistol",
            Category: WeaponCategory.Pistol,
            FormulaKind: WeaponAccuracyFormulaKind.PistolIntervalDecay,
            WeaponTypeCode: 2,
            DefaultAccuracy: 0.92,
            AccuracyDivisor: -1.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: 1.0,
            PistolDecayRate: 0.28,
            PistolMinAccuracy: 0.55,
            PistolMaxAccuracy: 0.92,
            StandSpreadParam: 0.032,
            CrouchSpreadParam: 0.020,
            WalkSpreadParam: 0.095,
            RunSpreadParam: 0.185,
            AirSpreadParam: 0.380,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.72,
            WalkSpeedThreshold: 13.0,
            RunSpeedThreshold: 150.0,
            SpreadScaleY: 0.60f,
            AttackIntervalMs: 150,
            ShotsFiredSpreadMin: 0,
            ShotsFiredSpreadMax: 0,
            ShotsFiredSpreadTimeMs: 0,
            MaxClip: 12,
            RaiseDurationMs: 400,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.20f,
            BasePunchPitch: 0.95f,
            MaxPunchYaw: 0.80f,
            MaxPunchPitch: 2.40f),

        new WeaponAccuracyConfiguration(
            WeaponId: "xm1014",
            DisplayName: "XM1014 Tactical Semi-Auto Shotgun",
            Category: WeaponCategory.Shotgun,
            FormulaKind: WeaponAccuracyFormulaKind.ShotgunPelletFixed,
            WeaponTypeCode: 1,
            DefaultAccuracy: 1.00,
            AccuracyDivisor: 14.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: 1.45,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.0350,
            CrouchSpreadParam: 0.0280,
            WalkSpreadParam: 0.0480,
            RunSpreadParam: 0.0720,
            AirSpreadParam: 0.1350,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.85,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 140.0,
            SpreadScaleY: 0.85f,
            AttackIntervalMs: 240,
            ShotsFiredSpreadMin: -10,
            ShotsFiredSpreadMax: -50,
            ShotsFiredSpreadTimeMs: 1200,
            MaxClip: 7,
            RaiseDurationMs: 650,
            AutoCloseScopeAfterShot: false,
            PelletCount: 8,
            BasePunchYaw: 0.45f,
            BasePunchPitch: 1.85f,
            MaxPunchYaw: 1.50f,
            MaxPunchPitch: 4.50f),

        new WeaponAccuracyConfiguration(
            WeaponId: "m3",
            DisplayName: "M3 Super 90 Pump-Action Shotgun",
            Category: WeaponCategory.Shotgun,
            FormulaKind: WeaponAccuracyFormulaKind.ShotgunPelletFixed,
            WeaponTypeCode: 1,
            DefaultAccuracy: 1.00,
            AccuracyDivisor: 18.0,
            AccuracyOffset: 0.0,
            MaxInaccuracy: 1.35,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.0400,
            CrouchSpreadParam: 0.0310,
            WalkSpreadParam: 0.0550,
            RunSpreadParam: 0.0820,
            AirSpreadParam: 0.1500,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.85,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 135.0,
            SpreadScaleY: 0.88f,
            AttackIntervalMs: 850,
            ShotsFiredSpreadMin: -10,
            ShotsFiredSpreadMax: -50,
            ShotsFiredSpreadTimeMs: 1500,
            MaxClip: 8,
            RaiseDurationMs: 700,
            AutoCloseScopeAfterShot: false,
            PelletCount: 9,
            BasePunchYaw: 0.55f,
            BasePunchPitch: 2.30f,
            MaxPunchYaw: 1.60f,
            MaxPunchPitch: 4.80f),

        new WeaponAccuracyConfiguration(
            WeaponId: "custompulse",
            DisplayName: "Custom Tactical Burst Carbine",
            Category: WeaponCategory.Custom,
            FormulaKind: WeaponAccuracyFormulaKind.CustomLinear,
            WeaponTypeCode: 1,
            DefaultAccuracy: 0.25,
            AccuracyDivisor: 25.0,
            AccuracyOffset: 0.25,
            MaxInaccuracy: 1.10,
            PistolDecayRate: 0.0,
            PistolMinAccuracy: 0.0,
            PistolMaxAccuracy: 1.0,
            StandSpreadParam: 0.0100,
            CrouchSpreadParam: 0.0060,
            WalkSpreadParam: 0.0440,
            RunSpreadParam: 0.1160,
            AirSpreadParam: 0.3000,
            UnscopedExtraSpread: 0.0,
            AdsSpreadMultiplier: 0.64,
            WalkSpeedThreshold: 25.0,
            RunSpeedThreshold: 150.0,
            SpreadScaleY: 0.45f,
            AttackIntervalMs: 80,
            ShotsFiredSpreadMin: -20,
            ShotsFiredSpreadMax: -120,
            ShotsFiredSpreadTimeMs: 800,
            MaxClip: 24,
            RaiseDurationMs: 500,
            AutoCloseScopeAfterShot: false,
            PelletCount: 1,
            BasePunchYaw: 0.18f,
            BasePunchPitch: 0.55f,
            MaxPunchYaw: 0.75f,
            MaxPunchPitch: 2.00f)
    };
}
