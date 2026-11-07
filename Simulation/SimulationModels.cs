using HackTK.Next.Abstractions;

namespace HackTK.Next.Simulation;

public sealed class DeterministicTimeProvider : ITimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    public long MonotonicMilliseconds { get; set; } = 1000L;
    public float UnscaledSeconds { get; set; } = 1.0f;

    public void AdvanceMilliseconds(long milliseconds)
    {
        UtcNow = UtcNow.AddMilliseconds(milliseconds);
        MonotonicMilliseconds += milliseconds;
        UnscaledSeconds += milliseconds / 1000f;
    }
}

public sealed class SsjjCoordinateTransformer : ICoordinateTransformer
{
    // Verified mapping: SSJJ (x, y, z) -> Unity (-y, z, x)
    public Vector3D SsjjToUnity(Vector3D ssjjPoint) =>
        new(-ssjjPoint.Y, ssjjPoint.Z, ssjjPoint.X);

    // Inverse mapping: Unity (ux, uy, uz) -> SSJJ (uz, -ux, uy)
    public Vector3D UnityToSsjj(Vector3D unityPoint) =>
        new(unityPoint.Z, -unityPoint.X, unityPoint.Y);
}

public sealed class TriangularSpreadDirectionModel : IBallisticDirectionModel
{
    public static double SampleUniform(int seed, double min, double max)
    {
        uint u = unchecked((uint)seed);
        u ^= u << 13;
        u ^= u >> 17;
        u ^= u << 5;
        u *= 0x9E3779B1u;
        double t = (u & 0xFFFFFFu) / (double)0x1000000u;
        return min + (max - min) * t;
    }

    public Vector3D CalculateDirection(
        int seed,
        double yawDegrees,
        double pitchDegrees,
        double spread,
        float spreadScaleY,
        double shotsFiredMultiplier)
    {
        double yr = yawDegrees * (Math.PI / 180.0);
        double pr = pitchDegrees * (Math.PI / 180.0);
        double cy = Math.Cos(yr);
        double sy = Math.Sin(yr);
        double cp = Math.Cos(pr);
        double sp = Math.Sin(pr);

        Vector3D forward = new(cp * cy, cp * sy, -sp);
        Vector3D right = new(-sy, cy, 0.0);
        Vector3D up = new(sp * cy, sp * sy, cp);

        double u1 = SampleUniform(seed, -0.5, 0.5) + SampleUniform(seed + 1, -0.5, 0.5);
        double u2 = SampleUniform(seed + 2, -0.5, 0.5) + SampleUniform(seed + 3, -0.5, 0.5);
        double scaleRight = spread * u1 * shotsFiredMultiplier;
        double scaleUp = spread * u2 * shotsFiredMultiplier * spreadScaleY;

        return new Vector3D(
            forward.X + right.X * scaleRight + up.X * scaleUp,
            forward.Y + right.Y * scaleRight + up.Y * scaleUp,
            forward.Z + right.Z * scaleRight + up.Z * scaleUp);
    }

    public IReadOnlyList<Vector3D> CalculatePelletDirections(
        int seed,
        double yawDegrees,
        double pitchDegrees,
        double spread,
        float spreadScaleY,
        double shotsFiredMultiplier,
        int pelletCount)
    {
        int count = Math.Clamp(pelletCount, 1, 32);
        Vector3D primary = CalculateDirection(seed, yawDegrees, pitchDegrees, spread, spreadScaleY, shotsFiredMultiplier);
        if (count == 1)
            return new[] { primary };

        if (!primary.TryNormalize(out var centerUnit))
            centerUnit = Vector3D.UnitX;

        if (!Vector3D.UnitZ.Cross(centerUnit).TryNormalize(out var ringRight))
            ringRight = Vector3D.UnitY;
        if (!centerUnit.Cross(ringRight).TryNormalize(out var ringUp))
            ringUp = Vector3D.UnitZ;

        var pellets = new Vector3D[count];
        pellets[0] = primary;

        int ringCount = count - 1;
        double baseRadius = spread * shotsFiredMultiplier;
        for (int i = 1; i < count; i++)
        {
            // Paired symmetric ring + per-pellet stochastic radial/angular perturbation
            double baseAngle = (2.0 * Math.PI * (i - 1)) / ringCount;
            double angleJitter = SampleUniform(seed + i * 31, -0.18, 0.18);
            double radialFactor = 0.55 + 0.45 * SampleUniform(seed + i * 31 + 7, 0.0, 1.0);
            double theta = baseAngle + angleJitter;
            double offsetRight = baseRadius * radialFactor * Math.Cos(theta);
            double offsetUp = baseRadius * radialFactor * spreadScaleY * Math.Sin(theta);

            pellets[i] = new Vector3D(
                centerUnit.X + ringRight.X * offsetRight + ringUp.X * offsetUp,
                centerUnit.Y + ringRight.Y * offsetRight + ringUp.Y * offsetUp,
                centerUnit.Z + ringRight.Z * offsetRight + ringUp.Z * offsetUp);
        }

        return pellets;
    }
}
