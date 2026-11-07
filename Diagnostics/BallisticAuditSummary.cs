using System.Globalization;
using HackTK.Next.Abstractions;

namespace HackTK.Next.Diagnostics;

public sealed record MovementSpreadMetrics(
    MovementStateKind State,
    int SampleCount,
    double MaxUncompensatedErrorDegrees,
    double MeanUncompensatedErrorDegrees,
    double MaxContinuousErrorDegrees,
    double MaxQuantizedErrorDegrees);

public sealed record WeaponBallisticMetrics(
    string WeaponName,
    WeaponCategory Category,
    string CustomGroupTag,
    int TotalCases,
    int SucceededCases,
    int FailedCases,
    int InvalidDataCases,
    double MeanUncompensatedErrorDegrees,
    double MaxUncompensatedErrorDegrees,
    double MaxContinuousErrorDegrees,
    double MaxQuantizedErrorDegrees);

public sealed record WeaponCategoryBallisticMetrics(
    WeaponCategory Category,
    IReadOnlyList<string> WeaponNames,
    int TotalCases,
    int SucceededCases,
    int FailedCases,
    int InvalidDataCases,
    double MeanUncompensatedErrorDegrees,
    double MaxUncompensatedErrorDegrees,
    double MaxContinuousErrorDegrees,
    double MaxQuantizedErrorDegrees);

public sealed record BallisticAuditSummaryReport(
    int TotalCases,
    int SucceededCases,
    int FailedCases,
    int InvalidDataCases,
    IReadOnlyDictionary<MovementStateKind, MovementSpreadMetrics> MetricsByMovementState,
    IReadOnlyDictionary<string, WeaponBallisticMetrics> MetricsByWeapon,
    IReadOnlyDictionary<WeaponCategory, WeaponCategoryBallisticMetrics> MetricsByCategory);

public static class BallisticAuditAnalyzer
{
    public static IReadOnlyDictionary<MovementStateKind, MovementSpreadMetrics> Summarize(IEnumerable<DiagnosticEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var buckets = new Dictionary<MovementStateKind, List<(double Before, double Continuous, double Quantized)>>();
        foreach (MovementStateKind kind in Enum.GetValues<MovementStateKind>())
            buckets[kind] = new List<(double, double, double)>();

        foreach (var ev in events)
        {
            if (!string.Equals(ev.Category, "COMMAND_COMPENSATED", StringComparison.Ordinal))
                continue;

            if (!ev.Fields.TryGetValue("moveState", out string? stateRaw) ||
                !Enum.TryParse(stateRaw, ignoreCase: true, out MovementStateKind moveState))
                continue;

            if (!TryParseDouble(ev.Fields, "uncompensatedErrorDegrees", out double before) ||
                !TryParseDouble(ev.Fields, "continuousErrorDegrees", out double continuous) ||
                !TryParseDouble(ev.Fields, "quantizedErrorDegrees", out double quantized))
                continue;

            buckets[moveState].Add((before, continuous, quantized));
        }

        var result = new Dictionary<MovementStateKind, MovementSpreadMetrics>();
        foreach (var (state, samples) in buckets)
        {
            if (samples.Count == 0)
            {
                result[state] = new MovementSpreadMetrics(state, 0, 0.0, 0.0, 0.0, 0.0);
                continue;
            }

            double maxBefore = 0.0, sumBefore = 0.0, maxContinuous = 0.0, maxQuantized = 0.0;
            foreach (var s in samples)
            {
                if (s.Before > maxBefore) maxBefore = s.Before;
                sumBefore += s.Before;
                if (s.Continuous > maxContinuous) maxContinuous = s.Continuous;
                if (s.Quantized > maxQuantized) maxQuantized = s.Quantized;
            }

            result[state] = new MovementSpreadMetrics(
                state,
                samples.Count,
                maxBefore,
                sumBefore / samples.Count,
                maxContinuous,
                maxQuantized);
        }

        return result;
    }

    public static BallisticAuditSummaryReport AnalyzeDetailed(
        IEnumerable<DiagnosticEvent> events,
        Func<string, (WeaponCategory Category, string CustomGroupTag)>? categoryResolver = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var eventList = events as IReadOnlyList<DiagnosticEvent> ?? events.ToList();
        var movementMetrics = Summarize(eventList);

        var weaponAccumulators = new Dictionary<string, WeaponBucket>(StringComparer.OrdinalIgnoreCase);
        int totalCases = 0;
        int succeededCases = 0;
        int failedCases = 0;
        int invalidDataCases = 0;

        foreach (var ev in eventList)
        {
            bool isCompensated = string.Equals(ev.Category, "COMMAND_COMPENSATED", StringComparison.Ordinal);
            bool isSkipped = string.Equals(ev.Category, "COMMAND_SKIPPED", StringComparison.Ordinal);
            bool isInvalid = string.Equals(ev.Category, "COMMAND_INVALID_WEAPON", StringComparison.Ordinal);

            if (!isCompensated && !isSkipped && !isInvalid)
                continue;

            string weaponName = ev.Fields.TryGetValue("weaponName", out string? rawName) && !string.IsNullOrWhiteSpace(rawName)
                ? rawName.Trim()
                : "<UnknownWeapon>";

            var (category, groupTag) = categoryResolver is not null
                ? categoryResolver(weaponName)
                : DefaultClassifyWeapon(weaponName);

            if (!weaponAccumulators.TryGetValue(weaponName, out var bucket))
            {
                bucket = new WeaponBucket(weaponName, category, groupTag);
                weaponAccumulators[weaponName] = bucket;
            }

            bucket.TotalCases++;
            totalCases++;

            if (isCompensated)
            {
                bucket.SucceededCases++;
                succeededCases++;

                if (TryParseDouble(ev.Fields, "uncompensatedErrorDegrees", out double before) &&
                    TryParseDouble(ev.Fields, "continuousErrorDegrees", out double continuous) &&
                    TryParseDouble(ev.Fields, "quantizedErrorDegrees", out double quantized))
                {
                    bucket.Samples.Add((before, continuous, quantized));
                }
            }
            else if (isInvalid)
            {
                bucket.FailedCases++;
                bucket.InvalidDataCases++;
                failedCases++;
                invalidDataCases++;
            }
            else if (isSkipped)
            {
                bucket.FailedCases++;
                failedCases++;
                if (ev.Fields.TryGetValue("reason", out string? reason) &&
                    reason.Contains("InvalidWeapon", StringComparison.OrdinalIgnoreCase))
                {
                    bucket.InvalidDataCases++;
                    invalidDataCases++;
                }
            }
        }

        var metricsByWeapon = new Dictionary<string, WeaponBallisticMetrics>(StringComparer.OrdinalIgnoreCase);
        foreach (var (weaponName, b) in weaponAccumulators)
        {
            var (meanBefore, maxBefore, maxCont, maxQuant) = ComputeStats(b.Samples);
            metricsByWeapon[weaponName] = new WeaponBallisticMetrics(
                b.WeaponName,
                b.Category,
                b.CustomGroupTag,
                b.TotalCases,
                b.SucceededCases,
                b.FailedCases,
                b.InvalidDataCases,
                meanBefore,
                maxBefore,
                maxCont,
                maxQuant);
        }

        var metricsByCategory = new Dictionary<WeaponCategory, WeaponCategoryBallisticMetrics>();
        foreach (WeaponCategory cat in Enum.GetValues<WeaponCategory>())
        {
            var matching = weaponAccumulators.Values.Where(w => w.Category == cat).ToList();
            if (matching.Count == 0)
            {
                metricsByCategory[cat] = new WeaponCategoryBallisticMetrics(
                    cat,
                    Array.Empty<string>(),
                    0, 0, 0, 0,
                    0.0, 0.0, 0.0, 0.0);
                continue;
            }

            var combinedSamples = new List<(double Before, double Continuous, double Quantized)>();
            int catTotal = 0, catSucc = 0, catFail = 0, catInvalid = 0;
            var names = new List<string>();
            foreach (var b in matching.OrderBy(x => x.WeaponName, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(b.WeaponName);
                catTotal += b.TotalCases;
                catSucc += b.SucceededCases;
                catFail += b.FailedCases;
                catInvalid += b.InvalidDataCases;
                combinedSamples.AddRange(b.Samples);
            }

            var (meanBefore, maxBefore, maxCont, maxQuant) = ComputeStats(combinedSamples);
            metricsByCategory[cat] = new WeaponCategoryBallisticMetrics(
                cat,
                names,
                catTotal,
                catSucc,
                catFail,
                catInvalid,
                meanBefore,
                maxBefore,
                maxCont,
                maxQuant);
        }

        return new BallisticAuditSummaryReport(
            totalCases,
            succeededCases,
            failedCases,
            invalidDataCases,
            movementMetrics,
            metricsByWeapon,
            metricsByCategory);
    }

    public static (WeaponCategory Category, string CustomGroupTag) DefaultClassifyWeapon(string weaponIdOrName)
    {
        if (string.IsNullOrWhiteSpace(weaponIdOrName))
            return (WeaponCategory.Custom, "Unidentified");

        string key = weaponIdOrName.Trim().ToLowerInvariant();
        if (key.StartsWith("custom:", StringComparison.Ordinal) || key.Contains("custom") || key.Contains("experimental") || key.Contains("pulse") || key.Contains("prototype") || key.Contains("rail"))
            return (WeaponCategory.Custom, "CustomGroup");

        if (key.Contains("xm1014") || key.Contains("m3") || key.Contains("shotgun") || key.Contains("spas") || key.Contains("remington") || key.Contains("aa12") || key.Contains("nova") || key.Contains("mag7"))
            return (WeaponCategory.Shotgun, "Shotgun");

        if (key.Contains("awp") || key.Contains("awm") || key.Contains("barrett") || key.Contains("scout") ||
            key.Contains("sniper") || key.Contains("m99") || key.Contains("m82") || key.Contains("m107") ||
            key.Contains("g3sg1") || key.Contains("vss") || key.Contains("dsr") || key.Contains("ssg") ||
            key.Contains("svd") || key.Contains("dragunov") || key.Contains("kar98") || key.Contains("l96"))
            return (WeaponCategory.SniperRifle, "Sniper");

        if (key.Contains("m249") || key.Contains("mg3") || key.Contains("rpk") || key.Contains("gatling") || key.Contains("m60") || key.Contains("lmg") || key.Contains("machinegun") || key.Contains("negev"))
            return (WeaponCategory.LightMachineGun, "LMG");

        if (key.Contains("mp5") || key.Contains("p90") || key.Contains("ump") || key.Contains("kriss") || key.Contains("vector") || key.Contains("pp2000") || key.Contains("smg") || key.Contains("uzi") || key.Contains("mp7") || key.Contains("mac10") || key.Contains("bizon"))
            return (WeaponCategory.SubmachineGun, "SMG");

        if (key.Contains("deagle") || key.Contains("usp") || key.Contains("glock") || key.Contains("g18") || key.Contains("revolver") || key.Contains("pistol") || key.Contains("colt") || key.Contains("m1911") || key.Contains("p250") || key.Contains("fiveseven"))
            return (WeaponCategory.Pistol, "Pistol");

        if (key.Contains("ak") || key.Contains("m4") || key.Contains("ar") || key.Contains("scar") || key.Contains("aug") || key.Contains("famas") || key.Contains("galil") || key.Contains("qbz") || key.Contains("an94") || key.Contains("sg55"))
            return (WeaponCategory.AssaultRifle, "AssaultRifle");

        return (WeaponCategory.Custom, "CustomGroup");
    }

    private static (double MeanBefore, double MaxBefore, double MaxContinuous, double MaxQuantized) ComputeStats(
        List<(double Before, double Continuous, double Quantized)> samples)
    {
        if (samples.Count == 0)
            return (0.0, 0.0, 0.0, 0.0);

        double maxBefore = 0.0, sumBefore = 0.0, maxContinuous = 0.0, maxQuantized = 0.0;
        foreach (var s in samples)
        {
            if (s.Before > maxBefore) maxBefore = s.Before;
            sumBefore += s.Before;
            if (s.Continuous > maxContinuous) maxContinuous = s.Continuous;
            if (s.Quantized > maxQuantized) maxQuantized = s.Quantized;
        }

        return (sumBefore / samples.Count, maxBefore, maxContinuous, maxQuantized);
    }

    private static bool TryParseDouble(IReadOnlyDictionary<string, string> fields, string key, out double value)
    {
        if (fields.TryGetValue(key, out string? raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }
        value = 0.0;
        return false;
    }

    private sealed class WeaponBucket
    {
        public WeaponBucket(string weaponName, WeaponCategory category, string customGroupTag)
        {
            WeaponName = weaponName;
            Category = category;
            CustomGroupTag = customGroupTag;
        }

        public string WeaponName { get; }
        public WeaponCategory Category { get; }
        public string CustomGroupTag { get; }
        public int TotalCases { get; set; }
        public int SucceededCases { get; set; }
        public int FailedCases { get; set; }
        public int InvalidDataCases { get; set; }
        public List<(double Before, double Continuous, double Quantized)> Samples { get; } = new();
    }
}
