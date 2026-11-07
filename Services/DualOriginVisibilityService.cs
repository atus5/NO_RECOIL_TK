using HackTK.Next.Abstractions;

namespace HackTK.Next.Services;

public sealed class DualOriginVisibilityService : IVisibilityEvaluationService
{
    private readonly IPhysicsRaycaster _raycaster;
    private readonly ICoordinateTransformer _transformer;

    public DualOriginVisibilityService(IPhysicsRaycaster raycaster, ICoordinateTransformer transformer)
    {
        _raycaster = raycaster ?? throw new ArgumentNullException(nameof(raycaster));
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    }

    public static bool EvaluateTraceHit(TraceHitResult hit, int targetEntityId, bool requireHeadBone)
    {
        if (hit.HasSolidOrOverlap ||
            targetEntityId <= 0 ||
            float.IsNaN(hit.Fraction) ||
            float.IsInfinity(hit.Fraction) ||
            hit.Fraction < 0f ||
            hit.Fraction > 1f)
        {
            return false;
        }

        if (hit.Fraction == 1f)
            return true;

        if (hit.HitEntityId != targetEntityId)
            return false;

        return !requireHeadBone ||
               string.Equals(hit.BoneName, "Bip01_Head", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(hit.BoneName, "head", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsActorVisible(
        IWorldStateProvider world,
        IActorSnapshot localActor,
        IActorSnapshot targetActor,
        Vector3D headUnity,
        Vector3D aimPointUnity,
        out string diagnosticReason)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(localActor);
        ArgumentNullException.ThrowIfNull(targetActor);

        if (!localActor.IsAlive || !targetActor.IsAlive || localActor.EntityId <= 0 || targetActor.EntityId <= 0)
        {
            diagnosticReason = "Thực thể local hoặc mục tiêu không hợp lệ / đã chết.";
            return false;
        }

        if (!_raycaster.IsReady)
        {
            diagnosticReason = $"Physics raycaster chưa sẵn sàng ({_raycaster.BackendName}).";
            return false;
        }

        Vector3D eyeSsjj = _transformer.UnityToSsjj(localActor.EyePositionUnity);
        Vector3D shotSsjj = localActor.CompensatedShotOriginSsjj;
        Vector3D headSsjj = _transformer.UnityToSsjj(headUnity);
        Vector3D aimSsjj = _transformer.UnityToSsjj(aimPointUnity);

        if (!CastAndCheck(eyeSsjj, headSsjj, localActor.EntityId, targetActor.EntityId, requireHead: true, "camera-head", out diagnosticReason) ||
            !CastAndCheck(shotSsjj, headSsjj, localActor.EntityId, targetActor.EntityId, requireHead: true, "shot-head", out diagnosticReason))
        {
            return false;
        }

        if (aimPointUnity != headUnity)
        {
            if (!CastAndCheck(eyeSsjj, aimSsjj, localActor.EntityId, targetActor.EntityId, requireHead: false, "camera-aim", out diagnosticReason) ||
                !CastAndCheck(shotSsjj, aimSsjj, localActor.EntityId, targetActor.EntityId, requireHead: false, "shot-aim", out diagnosticReason))
            {
                return false;
            }
        }

        diagnosticReason = $"Đầu và điểm ngắm rõ từ cả camera và điểm phát bắn ({_raycaster.BackendName}).";
        return true;
    }

    private bool CastAndCheck(
        Vector3D originSsjj,
        Vector3D endpointSsjj,
        int localEntityId,
        int targetEntityId,
        bool requireHead,
        string rayLabel,
        out string reason)
    {
        if (!originSsjj.IsFinite || !endpointSsjj.IsFinite || endpointSsjj.Subtract(originSsjj).LengthSquared < 1e-12)
        {
            reason = $"Tọa độ ray {rayLabel} không hợp lệ.";
            return false;
        }

        var hit = _raycaster.CastRaySsjj(originSsjj, endpointSsjj, localEntityId, targetEntityId);
        bool clear = EvaluateTraceHit(hit, targetEntityId, requireHead);
        if (!clear)
        {
            reason = $"Ray {rayLabel} bị chắn (entity={hit.HitEntityId}, bone={hit.BoneName}, fraction={hit.Fraction:F3}).";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
