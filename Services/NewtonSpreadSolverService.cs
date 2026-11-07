using HackTK.Next.Abstractions;

namespace HackTK.Next.Services;

public sealed class NewtonSpreadSolverService : ISpreadCompensationSolver
{
    public double WrapAngleDegrees(double degrees) =>
        degrees - 360.0 * Math.Floor((degrees + 180.0) / 360.0);

    public double ComputeAngularErrorDegrees(Vector3D a, Vector3D b)
    {
        if (!a.TryNormalize(out var unitA) || !b.TryNormalize(out var unitB))
            return double.PositiveInfinity;

        double dot = Math.Max(-1.0, Math.Min(1.0, unitA.Dot(unitB)));
        return Math.Acos(dot) * (180.0 / Math.PI);
    }

    public short EncodeAngle16(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            throw new ArgumentOutOfRangeException(nameof(degrees));

        return checked((short)Math.Round(WrapAngleDegrees(degrees) * 100.0, MidpointRounding.AwayFromZero));
    }

    public bool TrySolveInverse(
        Func<double, double, Vector3D> directionEvaluator,
        Vector3D targetDirection,
        double initialYawDegrees,
        double initialPitchDegrees,
        out InverseAngleSolution solution)
    {
        ArgumentNullException.ThrowIfNull(directionEvaluator);
        solution = default;

        if (!targetDirection.TryNormalize(out var unitTarget) ||
            double.IsNaN(initialPitchDegrees) ||
            double.IsInfinity(initialPitchDegrees) ||
            Math.Abs(initialPitchDegrees) > 89.0 ||
            double.IsNaN(initialYawDegrees) ||
            double.IsInfinity(initialYawDegrees))
        {
            return false;
        }

        if (!Vector3D.UnitZ.Cross(unitTarget).TryNormalize(out var side) ||
            !unitTarget.Cross(side).TryNormalize(out var up))
        {
            return false;
        }

        double yaw = WrapAngleDegrees(initialYawDegrees);
        double pitch = Math.Clamp(initialPitchDegrees, -85.0, 85.0);
        InverseAngleSolution best = default;
        double bestError = double.PositiveInfinity;

        for (int iteration = 0; iteration < 32; iteration++)
        {
            if (!directionEvaluator(yaw, pitch).TryNormalize(out var current))
                return false;

            double error = ComputeAngularErrorDegrees(current, unitTarget);
            if (error < bestError)
            {
                bestError = error;
                best = new InverseAngleSolution(WrapAngleDegrees(yaw), pitch, error);
            }

            if (error < 0.00001)
            {
                solution = best;
                return true;
            }

            const double step = 0.01;
            if (!directionEvaluator(yaw + step, pitch).TryNormalize(out var dyaw) ||
                !directionEvaluator(yaw, pitch + step).TryNormalize(out var dpitch))
            {
                return false;
            }

            double x = current.Dot(side);
            double y = current.Dot(up);
            double j11 = (dyaw.Dot(side) - x) / step;
            double j12 = (dpitch.Dot(side) - x) / step;
            double j21 = (dyaw.Dot(up) - y) / step;
            double j22 = (dpitch.Dot(up) - y) / step;

            double det = j11 * j22 - j12 * j21;
            if (double.IsNaN(det) || Math.Abs(det) < 1e-10)
                break;

            double rawDeltaYaw = Math.Clamp((j22 * x - j12 * y) / det, -30.0, 30.0);
            double rawDeltaPitch = Math.Clamp((-j21 * x + j11 * y) / det, -25.0, 25.0);

            bool improved = false;
            double[] alphas = { 1.0, 0.5, 0.25, 0.125 };
            foreach (double alpha in alphas)
            {
                double candYaw = WrapAngleDegrees(yaw - alpha * rawDeltaYaw);
                double candPitch = Math.Clamp(pitch - alpha * rawDeltaPitch, -88.5, 88.5);
                if (!directionEvaluator(candYaw, candPitch).TryNormalize(out var candDir))
                    continue;

                double candErr = ComputeAngularErrorDegrees(candDir, unitTarget);
                if (candErr < error)
                {
                    yaw = candYaw;
                    pitch = candPitch;
                    improved = true;
                    break;
                }
            }

            if (!improved)
            {
                yaw = WrapAngleDegrees(yaw - 0.25 * rawDeltaYaw);
                pitch = Math.Clamp(pitch - 0.25 * rawDeltaPitch, -88.5, 88.5);
            }

            if (Math.Abs(WrapAngleDegrees(yaw - initialYawDegrees)) > 75.0 ||
                Math.Abs(pitch - initialPitchDegrees) > 65.0 ||
                Math.Abs(pitch) > 89.0)
            {
                break;
            }
        }

        if (bestError < 0.001)
        {
            solution = best;
            return true;
        }

        return false;
    }

    public bool TryQuantizeCommandAngles(
        Func<double, double, Vector3D> directionEvaluator,
        Vector3D targetDirection,
        double solvedYawDegrees,
        double solvedPitchDegrees,
        double punchYawDegrees,
        double punchPitchDegrees,
        out QuantizedCommandAngles quantized)
    {
        ArgumentNullException.ThrowIfNull(directionEvaluator);
        quantized = default;

        if (!targetDirection.TryNormalize(out var unitTarget) ||
            double.IsNaN(solvedYawDegrees) || double.IsInfinity(solvedYawDegrees) ||
            double.IsNaN(solvedPitchDegrees) || double.IsInfinity(solvedPitchDegrees) ||
            double.IsNaN(punchYawDegrees) || double.IsInfinity(punchYawDegrees) ||
            double.IsNaN(punchPitchDegrees) || double.IsInfinity(punchPitchDegrees))
        {
            return false;
        }

        double continuousErr = ComputeAngularErrorDegrees(
            directionEvaluator(solvedYawDegrees, solvedPitchDegrees),
            unitTarget);

        short baseYaw = EncodeAngle16(solvedYawDegrees - punchYawDegrees);
        double desiredCmdPitch = Math.Clamp(solvedPitchDegrees - punchPitchDegrees, -88.95, 88.95);
        short basePitch = EncodeAngle16(desiredCmdPitch);
        short bestYaw = 0;
        short bestPitch = 0;
        double bestQuantizedError = double.PositiveInfinity;

        for (int dy = -2; dy <= 2; dy++)
        for (int dp = -2; dp <= 2; dp++)
        {
            int cy = baseYaw + dy;
            if (cy > 18000) cy -= 36000;
            else if (cy < -18000) cy += 36000;

            int cp = basePitch + dp;
            if (cp < short.MinValue || cp > short.MaxValue || Math.Abs(cp / 100.0) > 89.0)
                continue;

            short qy = (short)cy;
            short qp = (short)cp;
            double evalYaw = (float)(qy / 100f + (float)punchYawDegrees);
            double evalPitch = (float)(qp / 100f + (float)punchPitchDegrees);
            double err = ComputeAngularErrorDegrees(directionEvaluator(evalYaw, evalPitch), unitTarget);

            if (err < bestQuantizedError)
            {
                bestQuantizedError = err;
                bestYaw = qy;
                bestPitch = qp;
            }
        }

        if (double.IsInfinity(bestQuantizedError) || double.IsNaN(bestQuantizedError))
            return false;

        quantized = new QuantizedCommandAngles(bestYaw, bestPitch, continuousErr, bestQuantizedError);
        return true;
    }
}
