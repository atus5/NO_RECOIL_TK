using HackTK.Next.Abstractions;

namespace HackTK.Next.Services;

public sealed class SubpixelAimComputationService : IAimTrajectoryService
{
    public float ComputeFovRadiusPixels(float fovDegrees, int screenHeight) =>
        fovDegrees * screenHeight / 180f;

    public float ComputeScaledRadiusPixels(float referencePixelsAt1080p, int screenHeight)
    {
        if (!IsFinite(referencePixelsAt1080p) || referencePixelsAt1080p <= 0f || screenHeight <= 0)
            return 0f;
        return referencePixelsAt1080p * screenHeight / 1080f;
    }

    public bool IsWithinFov(ScreenPoint point, int screenWidth, int screenHeight, float radiusPixels)
    {
        if (screenWidth <= 0 || screenHeight <= 0 || !point.IsFinite || !IsFinite(radiusPixels) || radiusPixels <= 0f)
            return false;

        double dx = point.X - screenWidth / 2.0;
        double dy = point.Y - screenHeight / 2.0;
        return point.Depth >= 0.01f && (dx * dx + dy * dy) < (double)radiusPixels * radiusPixels;
    }

    public int ComputeDelta(float targetCoord, float centerCoord, float punchAngle, float smoothFactor)
    {
        if (smoothFactor < 1f || !IsFinite(smoothFactor))
            throw new ArgumentOutOfRangeException(nameof(smoothFactor));

        double delta = (targetCoord - centerCoord + punchAngle * 15.0) / smoothFactor;
        if (double.IsNaN(delta) || double.IsInfinity(delta))
            return 0;

        return (int)Math.Max(-32767.0, Math.Min(32767.0, delta));
    }

    public (int WholePixels, float NextRemainder) ComputeSubpixelStep(
        float targetCoord,
        float centerCoord,
        float punchAngle,
        float smoothFactor,
        float currentRemainder)
    {
        if (smoothFactor < 1f || !IsFinite(smoothFactor))
            throw new ArgumentOutOfRangeException(nameof(smoothFactor));

        double delta = (targetCoord - centerCoord + punchAngle * 15.0) / smoothFactor;
        if (double.IsNaN(delta) || double.IsInfinity(delta) || !IsFinite(currentRemainder))
            return (0, 0f);

        if ((delta > 0.0 && currentRemainder < 0f) || (delta < 0.0 && currentRemainder > 0f))
            currentRemainder = 0f;

        double combined = Math.Max(-32767.0, Math.Min(32767.0, delta)) + currentRemainder;
        int whole = (int)combined;
        float nextRemainder = (float)(combined - whole);
        return (whole, nextRemainder);
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
