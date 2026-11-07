using HackTK.Next.Abstractions;

namespace HackTK.Next.Simulation;

public sealed record WeaponSimulationProfile(
    string WeaponId,
    string DisplayName,
    WeaponCategory Category,
    string CustomGroupTag,
    int ClipCapacity,
    int DefaultShotsFired,
    float SpreadScaleY,
    double ShotsFiredMultiplier,
    float DefaultPunchYaw,
    float DefaultPunchPitch,
    int PelletCount,
    IReadOnlyDictionary<MovementStateKind, double> SpreadByMovementState)
{
    public double GetSpreadForState(MovementStateKind state)
    {
        if (SpreadByMovementState is not null && SpreadByMovementState.TryGetValue(state, out double spread))
            return spread;
        if (SpreadByMovementState is not null && SpreadByMovementState.TryGetValue(MovementStateKind.Stand, out double standSpread))
            return standSpread;
        throw new KeyNotFoundException($"Weapon profile '{WeaponId}' does not define spread for movement state '{state}'.");
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(WeaponId))
            errors.Add("WeaponId must not be empty.");

        if (string.IsNullOrWhiteSpace(DisplayName))
            errors.Add("DisplayName must not be empty.");

        if (!Enum.IsDefined(Category))
            errors.Add($"Undefined WeaponCategory value: {Category}.");

        if (Category == WeaponCategory.Custom && string.IsNullOrWhiteSpace(CustomGroupTag))
            errors.Add("Custom weapon category requires a non-empty CustomGroupTag.");

        if (ClipCapacity <= 0 || ClipCapacity > 1000)
            errors.Add($"ClipCapacity ({ClipCapacity}) must be in range [1, 1000].");

        if (DefaultShotsFired < 0 || DefaultShotsFired > 1000)
            errors.Add($"DefaultShotsFired ({DefaultShotsFired}) must be in range [0, 1000].");

        if (!float.IsFinite(SpreadScaleY) || SpreadScaleY <= 0.0f || SpreadScaleY > 10.0f)
            errors.Add($"SpreadScaleY ({SpreadScaleY}) must be finite and in range (0, 10].");

        if (!double.IsFinite(ShotsFiredMultiplier) || ShotsFiredMultiplier <= 0.0 || ShotsFiredMultiplier > 20.0)
            errors.Add($"ShotsFiredMultiplier ({ShotsFiredMultiplier}) must be finite and in range (0, 20].");

        if (!float.IsFinite(DefaultPunchYaw) || Math.Abs(DefaultPunchYaw) > 45.0f)
            errors.Add($"DefaultPunchYaw ({DefaultPunchYaw}) must be finite and within [-45, 45].");

        if (!float.IsFinite(DefaultPunchPitch) || Math.Abs(DefaultPunchPitch) > 45.0f)
            errors.Add($"DefaultPunchPitch ({DefaultPunchPitch}) must be finite and within [-45, 45].");

        if (PelletCount <= 0 || PelletCount > 32)
            errors.Add($"PelletCount ({PelletCount}) must be in range [1, 32].");

        if (SpreadByMovementState is null || SpreadByMovementState.Count == 0)
        {
            errors.Add("SpreadByMovementState must contain at least one movement state entry.");
        }
        else
        {
            MovementStateKind[] requiredCoreStates =
            {
                MovementStateKind.Stand,
                MovementStateKind.Walk,
                MovementStateKind.Run,
                MovementStateKind.Strafe,
                MovementStateKind.MoveTurn,
                MovementStateKind.JumpAir
            };

            foreach (var req in requiredCoreStates)
            {
                if (!SpreadByMovementState.ContainsKey(req))
                    errors.Add($"SpreadByMovementState is missing required state '{req}'.");
            }

            foreach (var (state, value) in SpreadByMovementState)
            {
                if (!Enum.IsDefined(state))
                    errors.Add($"SpreadByMovementState contains undefined state '{state}'.");
                if (!double.IsFinite(value) || value < 0.0 || value > 2.5)
                    errors.Add($"Spread for state '{state}' ({value}) must be finite and in range [0.0, 2.5].");
            }
        }

        return errors;
    }
}

public interface IWeaponRegistry
{
    IReadOnlyList<WeaponSimulationProfile> GetAllProfiles();
    IReadOnlyList<WeaponSimulationProfile> GetByCategory(WeaponCategory category);
    IReadOnlyList<WeaponSimulationProfile> GetByCustomGroup(string customGroupTag);
    bool TryGetProfile(string weaponId, out WeaponSimulationProfile? profile);
    WeaponSimulationProfile GetProfile(string weaponId);
    void Register(WeaponSimulationProfile profile, bool overwrite = false);
    bool TryRegister(WeaponSimulationProfile profile, out IReadOnlyList<string> validationErrors, bool overwrite = false);
    bool IdentifyCategory(string? weaponIdOrName, out WeaponCategory category, out string customGroupTag);
    SimulatedWeaponSnapshot CreateSnapshot(string weaponId, MovementStateKind movementState = MovementStateKind.Stand, int? shotsFiredOverride = null);
    SimulatedWeaponSnapshot ApplyToActor(SimulatedActorSnapshot actor, string weaponId, MovementStateKind movementState = MovementStateKind.Stand);
    bool ValidateSnapshot(IWeaponSnapshot? snapshot, out IReadOnlyList<string> errors);
}

public sealed class WeaponRegistry : IWeaponRegistry
{
    private readonly Dictionary<string, WeaponSimulationProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _insertionOrder = new();

    public static WeaponRegistry CreateDefault()
    {
        var registry = new WeaponRegistry();
        foreach (var preset in CreateBuiltInProfiles())
        {
            registry.Register(preset, overwrite: true);
        }
        return registry;
    }

    public IReadOnlyList<WeaponSimulationProfile> GetAllProfiles()
    {
        var list = new List<WeaponSimulationProfile>(_insertionOrder.Count);
        foreach (string id in _insertionOrder)
        {
            if (_profiles.TryGetValue(id, out var profile))
                list.Add(profile);
        }
        return list;
    }

    public IReadOnlyList<WeaponSimulationProfile> GetByCategory(WeaponCategory category) =>
        GetAllProfiles().Where(p => p.Category == category).ToArray();

    public IReadOnlyList<WeaponSimulationProfile> GetByCustomGroup(string customGroupTag)
    {
        if (string.IsNullOrWhiteSpace(customGroupTag))
            return Array.Empty<WeaponSimulationProfile>();

        return GetAllProfiles()
            .Where(p => string.Equals(p.CustomGroupTag, customGroupTag.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static string NormalizeWeaponKey(string? weaponIdOrName)
    {
        if (string.IsNullOrWhiteSpace(weaponIdOrName))
            return string.Empty;

        string key = weaponIdOrName.Trim();
        if (key.EndsWith("_Simulation", StringComparison.OrdinalIgnoreCase))
            key = key[..^"_Simulation".Length];
        else if (key.EndsWith("_Sim", StringComparison.OrdinalIgnoreCase))
            key = key[..^"_Sim".Length];

        return key.Trim();
    }

    public bool TryGetProfile(string weaponId, out WeaponSimulationProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(weaponId))
        {
            profile = null;
            return false;
        }

        string trimmed = weaponId.Trim();
        if (_profiles.TryGetValue(trimmed, out profile))
            return true;

        string baseKey = NormalizeWeaponKey(trimmed);
        if (!string.IsNullOrEmpty(baseKey))
        {
            if (_profiles.TryGetValue(baseKey, out profile))
                return true;
            if (_profiles.TryGetValue(baseKey + "_Sim", out profile))
                return true;
            if (TryResolveSniperAliasProfileId(baseKey, out string sniperAliasProfileId) &&
                _profiles.TryGetValue(sniperAliasProfileId, out profile))
            {
                return true;
            }
        }

        profile = null;
        return false;
    }

    public static bool TryResolveSniperAliasKey(string? rawIdOrName, out string canonicalSniperKey)
    {
        canonicalSniperKey = string.Empty;
        if (string.IsNullOrWhiteSpace(rawIdOrName))
            return false;

        string lower = NormalizeWeaponKey(rawIdOrName).ToLowerInvariant();
        if (lower is "barrett" or "m99" or "m82" or "m82a1" or "m107" ||
            lower.Contains("barrett", StringComparison.Ordinal) ||
            lower.Contains("m99", StringComparison.Ordinal) ||
            lower.Contains("m82", StringComparison.Ordinal) ||
            lower.Contains("m107", StringComparison.Ordinal))
        {
            canonicalSniperKey = "barrett";
            return true;
        }

        if (lower is "scout" or "ssg" or "ssg08" or "steyrscout" or "steyr_scout" or "lightsniper" ||
            lower.Contains("scout", StringComparison.Ordinal) ||
            lower.Contains("ssg", StringComparison.Ordinal) ||
            lower.Contains("lightsniper", StringComparison.Ordinal))
        {
            canonicalSniperKey = "scout";
            return true;
        }

        if (lower is "awp" or "awm" or "l96" or "l96a1" ||
            lower.Contains("awp", StringComparison.Ordinal) ||
            lower.Contains("awm", StringComparison.Ordinal) ||
            lower.Contains("l96", StringComparison.Ordinal))
        {
            canonicalSniperKey = "awp";
            return true;
        }

        return false;
    }

    private static bool TryResolveSniperAliasProfileId(string normalizedBaseKey, out string profileId)
    {
        string lower = normalizedBaseKey.ToLowerInvariant();
        switch (lower)
        {
            case "awm":
            case "l96":
            case "l96a1":
                profileId = "AWP_Sim";
                return true;
            case "m99":
            case "m82":
            case "m82a1":
            case "m107":
                profileId = "Barrett_Sim";
                return true;
            case "ssg":
            case "ssg08":
            case "steyrscout":
            case "steyr_scout":
            case "steyr scout":
            case "lightsniper":
                profileId = "Scout_Sim";
                return true;
            default:
                profileId = string.Empty;
                return false;
        }
    }

    public WeaponSimulationProfile GetProfile(string weaponId)
    {
        if (!TryGetProfile(weaponId, out var profile) || profile is null)
            throw new KeyNotFoundException($"Weapon configuration '{weaponId}' is not registered in WeaponRegistry.");
        return profile;
    }

    public void Register(WeaponSimulationProfile profile, bool overwrite = false)
    {
        if (!TryRegister(profile, out var errors, overwrite))
            throw new ArgumentException($"Invalid weapon profile '{profile?.WeaponId}': {string.Join(" ", errors)}", nameof(profile));
    }

    public bool TryRegister(WeaponSimulationProfile? profile, out IReadOnlyList<string> validationErrors, bool overwrite = false)
    {
        if (profile is null)
        {
            validationErrors = new[] { "Profile instance cannot be null." };
            return false;
        }

        var errors = new List<string>(profile.Validate());
        string normalizedId = (profile.WeaponId ?? string.Empty).Trim();

        if (!overwrite && !string.IsNullOrEmpty(normalizedId) && _profiles.ContainsKey(normalizedId))
        {
            errors.Add($"Duplicate weapon configuration ID '{normalizedId}'. Set overwrite=true to replace.");
        }

        if (errors.Count > 0)
        {
            validationErrors = errors;
            return false;
        }

        if (!_profiles.ContainsKey(normalizedId))
            _insertionOrder.Add(normalizedId);

        _profiles[normalizedId] = profile with { WeaponId = normalizedId };
        validationErrors = Array.Empty<string>();
        return true;
    }

    public bool IdentifyCategory(string? weaponIdOrName, out WeaponCategory category, out string customGroupTag)
    {
        if (string.IsNullOrWhiteSpace(weaponIdOrName))
        {
            category = default;
            customGroupTag = string.Empty;
            return false;
        }

        string trimmed = weaponIdOrName.Trim();
        if (TryGetProfile(trimmed, out var registered) && registered is not null)
        {
            category = registered.Category;
            customGroupTag = registered.CustomGroupTag;
            return true;
        }

        string lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("custom:", StringComparison.Ordinal) || lower.Contains("custom") || lower.Contains("prototype") || lower.Contains("pulse") || lower.Contains("rail") || lower.Contains("experimental"))
        {
            category = WeaponCategory.Custom;
            int colonIdx = trimmed.IndexOf(':');
            customGroupTag = colonIdx >= 0 && colonIdx + 1 < trimmed.Length
                ? trimmed[(colonIdx + 1)..].Trim()
                : "CustomExperimental";
            return true;
        }

        if (lower.Contains("xm1014") || lower.Contains("m3") || lower.Contains("shotgun") || lower.Contains("spas") || lower.Contains("remington") || lower.Contains("aa12") || lower.Contains("nova") || lower.Contains("mag7"))
        {
            category = WeaponCategory.Shotgun;
            customGroupTag = "Shotgun";
            return true;
        }

        if (lower.Contains("awp") || lower.Contains("awm") || lower.Contains("barrett") || lower.Contains("scout") ||
            lower.Contains("sniper") || lower.Contains("m99") || lower.Contains("m82") || lower.Contains("m107") ||
            lower.Contains("g3sg1") || lower.Contains("vss") || lower.Contains("dsr") || lower.Contains("ssg") ||
            lower.Contains("svd") || lower.Contains("dragunov") || lower.Contains("kar98") || lower.Contains("l96"))
        {
            category = WeaponCategory.SniperRifle;
            customGroupTag = "Sniper";
            return true;
        }

        if (lower.Contains("m249") || lower.Contains("mg3") || lower.Contains("rpk") || lower.Contains("gatling") || lower.Contains("m60") || lower.Contains("lmg") || lower.Contains("machinegun") || lower.Contains("negev"))
        {
            category = WeaponCategory.LightMachineGun;
            customGroupTag = "LMG";
            return true;
        }

        if (lower.Contains("mp5") || lower.Contains("p90") || lower.Contains("ump") || lower.Contains("kriss") || lower.Contains("vector") || lower.Contains("pp2000") || lower.Contains("smg") || lower.Contains("uzi") || lower.Contains("mp7") || lower.Contains("mac10") || lower.Contains("bizon"))
        {
            category = WeaponCategory.SubmachineGun;
            customGroupTag = "SMG";
            return true;
        }

        if (lower.Contains("deagle") || lower.Contains("usp") || lower.Contains("glock") || lower.Contains("g18") || lower.Contains("revolver") || lower.Contains("pistol") || lower.Contains("colt") || lower.Contains("m1911") || lower.Contains("p250") || lower.Contains("fiveseven"))
        {
            category = WeaponCategory.Pistol;
            customGroupTag = "Pistol";
            return true;
        }

        if (lower.Contains("ak") || lower.Contains("m4") || lower.Contains("ar") || lower.Contains("scar") || lower.Contains("aug") || lower.Contains("famas") || lower.Contains("galil") || lower.Contains("qbz") || lower.Contains("an94") || lower.Contains("sg55"))
        {
            category = WeaponCategory.AssaultRifle;
            customGroupTag = "AssaultRifle";
            return true;
        }

        category = default;
        customGroupTag = string.Empty;
        return false;
    }

    public SimulatedWeaponSnapshot CreateSnapshot(string weaponId, MovementStateKind movementState = MovementStateKind.Stand, int? shotsFiredOverride = null)
    {
        var profile = GetProfile(weaponId);
        double spread = profile.GetSpreadForState(movementState);
        return new SimulatedWeaponSnapshot
        {
            WeaponName = profile.WeaponId,
            Category = profile.Category,
            ClipAmmo = profile.ClipCapacity,
            ShotsFired = shotsFiredOverride ?? profile.DefaultShotsFired,
            Spread = spread,
            SpreadScaleY = profile.SpreadScaleY,
            ShotsFiredMultiplier = profile.ShotsFiredMultiplier,
            PelletCount = profile.PelletCount,
            IsScoped = movementState == MovementStateKind.AimDownSights
        };
    }

    public SimulatedWeaponSnapshot ApplyToActor(SimulatedActorSnapshot actor, string weaponId, MovementStateKind movementState = MovementStateKind.Stand)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var profile = GetProfile(weaponId);
        var snapshot = CreateSnapshot(weaponId, movementState);
        actor.CurrentWeapon = snapshot;
        actor.PunchYaw = profile.DefaultPunchYaw;
        actor.PunchPitch = profile.DefaultPunchPitch;
        actor.IsOnGround = movementState != MovementStateKind.JumpAir;
        actor.IsCrouching = movementState == MovementStateKind.Crouch;
        actor.IsAimingDownSights = movementState == MovementStateKind.AimDownSights;
        actor.Fov = movementState == MovementStateKind.AimDownSights ? 40 : 90;
        actor.HorizontalVelocity = movementState switch
        {
            MovementStateKind.Walk => 120.0,
            MovementStateKind.Run or MovementStateKind.Strafe or MovementStateKind.MoveTurn or MovementStateKind.JumpAir => 250.0,
            _ => 0.0
        };
        return snapshot;
    }

    public bool ValidateSnapshot(IWeaponSnapshot? snapshot, out IReadOnlyList<string> errors)
    {
        var list = new List<string>();
        if (snapshot is null)
        {
            list.Add("Weapon snapshot is null.");
            errors = list;
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.WeaponName))
            list.Add("WeaponName is missing.");
        if (snapshot.ClipAmmo < 0)
            list.Add($"ClipAmmo ({snapshot.ClipAmmo}) cannot be negative.");
        if (snapshot.ShotsFired < 0)
            list.Add($"ShotsFired ({snapshot.ShotsFired}) cannot be negative.");
        if (!double.IsFinite(snapshot.Spread) || snapshot.Spread < 0.0 || snapshot.Spread > 2.5)
            list.Add($"Spread ({snapshot.Spread}) is out of valid range [0.0, 2.5].");
        if (!float.IsFinite(snapshot.SpreadScaleY) || snapshot.SpreadScaleY <= 0.0f || snapshot.SpreadScaleY > 10.0f)
            list.Add($"SpreadScaleY ({snapshot.SpreadScaleY}) is out of valid range (0.0, 10.0].");
        if (!double.IsFinite(snapshot.ShotsFiredMultiplier) || snapshot.ShotsFiredMultiplier <= 0.0 || snapshot.ShotsFiredMultiplier > 20.0)
            list.Add($"ShotsFiredMultiplier ({snapshot.ShotsFiredMultiplier}) is out of valid range (0.0, 20.0].");
        if (snapshot.PelletCount <= 0 || snapshot.PelletCount > 32)
            list.Add($"PelletCount ({snapshot.PelletCount}) is out of valid range [1, 32].");

        errors = list;
        return list.Count == 0;
    }

    public static IReadOnlyList<WeaponSimulationProfile> CreateBuiltInProfiles() => new[]
    {
        // 1. Assault Rifles (Preserves exact legacy AK47_Sim baseline + M4A1_Sim)
        new WeaponSimulationProfile(
            WeaponId: "AK47_Sim",
            DisplayName: "AK-47 Assault Rifle (Baseline)",
            Category: WeaponCategory.AssaultRifle,
            CustomGroupTag: "AssaultRifle",
            ClipCapacity: 30,
            DefaultShotsFired: 3,
            SpreadScaleY: 0.40f,
            ShotsFiredMultiplier: 1.35,
            DefaultPunchYaw: 0.40f,
            DefaultPunchPitch: 1.20f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0040,
                [MovementStateKind.Walk] = 0.0250,
                [MovementStateKind.Run] = 0.0650,
                [MovementStateKind.Strafe] = 0.0650,
                [MovementStateKind.MoveTurn] = 0.0800,
                [MovementStateKind.JumpAir] = 0.1500,
                [MovementStateKind.Crouch] = 0.0026,
                [MovementStateKind.AimDownSights] = 0.0030
            }),

        new WeaponSimulationProfile(
            WeaponId: "M4A1_Sim",
            DisplayName: "M4A1 Carbine Assault Rifle",
            Category: WeaponCategory.AssaultRifle,
            CustomGroupTag: "AssaultRifle",
            ClipCapacity: 30,
            DefaultShotsFired: 3,
            SpreadScaleY: 0.40f,
            ShotsFiredMultiplier: 1.25,
            DefaultPunchYaw: 0.30f,
            DefaultPunchPitch: 0.95f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0032,
                [MovementStateKind.Walk] = 0.0190,
                [MovementStateKind.Run] = 0.0480,
                [MovementStateKind.Strafe] = 0.0480,
                [MovementStateKind.MoveTurn] = 0.0600,
                [MovementStateKind.JumpAir] = 0.1100,
                [MovementStateKind.Crouch] = 0.0020,
                [MovementStateKind.AimDownSights] = 0.0021
            }),

        // 2. SMG (Submachine Gun)
        new WeaponSimulationProfile(
            WeaponId: "MP5_Sim",
            DisplayName: "MP5 Submachine Gun",
            Category: WeaponCategory.SubmachineGun,
            CustomGroupTag: "SMG",
            ClipCapacity: 35,
            DefaultShotsFired: 4,
            SpreadScaleY: 0.50f,
            ShotsFiredMultiplier: 1.15,
            DefaultPunchYaw: 0.22f,
            DefaultPunchPitch: 0.65f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0044,
                [MovementStateKind.Walk] = 0.0096,
                [MovementStateKind.Run] = 0.0220,
                [MovementStateKind.Strafe] = 0.0220,
                [MovementStateKind.MoveTurn] = 0.0280,
                [MovementStateKind.JumpAir] = 0.0640,
                [MovementStateKind.Crouch] = 0.0028,
                [MovementStateKind.AimDownSights] = 0.0031
            }),

        // 3. LMG (Light Machine Gun)
        new WeaponSimulationProfile(
            WeaponId: "M249_Sim",
            DisplayName: "M249 SAW Light Machine Gun",
            Category: WeaponCategory.LightMachineGun,
            CustomGroupTag: "LMG",
            ClipCapacity: 100,
            DefaultShotsFired: 8,
            SpreadScaleY: 0.55f,
            ShotsFiredMultiplier: 1.45,
            DefaultPunchYaw: 0.50f,
            DefaultPunchPitch: 1.45f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0070,
                [MovementStateKind.Walk] = 0.0350,
                [MovementStateKind.Run] = 0.0850,
                [MovementStateKind.Strafe] = 0.0850,
                [MovementStateKind.MoveTurn] = 0.1050,
                [MovementStateKind.JumpAir] = 0.1625,
                [MovementStateKind.Crouch] = 0.0038,
                [MovementStateKind.AimDownSights] = 0.0050
            }),

        // 4. Pistol
        new WeaponSimulationProfile(
            WeaponId: "Deagle_Sim",
            DisplayName: "Desert Eagle .50 AE Pistol",
            Category: WeaponCategory.Pistol,
            CustomGroupTag: "Pistol",
            ClipCapacity: 7,
            DefaultShotsFired: 1,
            SpreadScaleY: 0.65f,
            ShotsFiredMultiplier: 1.00,
            DefaultPunchYaw: 0.35f,
            DefaultPunchPitch: 1.60f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0040,
                [MovementStateKind.Walk] = 0.0120,
                [MovementStateKind.Run] = 0.0240,
                [MovementStateKind.Strafe] = 0.0240,
                [MovementStateKind.MoveTurn] = 0.0320,
                [MovementStateKind.JumpAir] = 0.0480,
                [MovementStateKind.Crouch] = 0.0026,
                [MovementStateKind.AimDownSights] = 0.0030
            }),

        // 5. Sniper Rifles (AWP, Barrett, Scout)
        new WeaponSimulationProfile(
            WeaponId: "AWP_Sim",
            DisplayName: "AWP Bolt-Action Sniper Rifle",
            Category: WeaponCategory.SniperRifle,
            CustomGroupTag: "Sniper",
            ClipCapacity: 10,
            DefaultShotsFired: 1,
            SpreadScaleY: 1.00f,
            ShotsFiredMultiplier: 1.00,
            DefaultPunchYaw: 0.15f,
            DefaultPunchPitch: 2.20f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0012,
                [MovementStateKind.Walk] = 0.0420,
                [MovementStateKind.Run] = 0.0950,
                [MovementStateKind.Strafe] = 0.0950,
                [MovementStateKind.MoveTurn] = 0.1150,
                [MovementStateKind.JumpAir] = 0.1800,
                [MovementStateKind.Crouch] = 0.0005,
                [MovementStateKind.AimDownSights] = 0.0003
            }),

        new WeaponSimulationProfile(
            WeaponId: "Barrett_Sim",
            DisplayName: "Barrett M82A1 Heavy Sniper Rifle",
            Category: WeaponCategory.SniperRifle,
            CustomGroupTag: "Sniper",
            ClipCapacity: 10,
            DefaultShotsFired: 1,
            SpreadScaleY: 1.00f,
            ShotsFiredMultiplier: 1.00,
            DefaultPunchYaw: 0.25f,
            DefaultPunchPitch: 2.80f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0008,
                [MovementStateKind.Walk] = 0.0580,
                [MovementStateKind.Run] = 0.1350,
                [MovementStateKind.Strafe] = 0.1350,
                [MovementStateKind.MoveTurn] = 0.1550,
                [MovementStateKind.JumpAir] = 0.1950,
                [MovementStateKind.Crouch] = 0.0004,
                [MovementStateKind.AimDownSights] = 0.0002
            }),

        new WeaponSimulationProfile(
            WeaponId: "Scout_Sim",
            DisplayName: "Steyr Scout / SSG-08 Light Sniper Rifle",
            Category: WeaponCategory.SniperRifle,
            CustomGroupTag: "Sniper",
            ClipCapacity: 10,
            DefaultShotsFired: 1,
            SpreadScaleY: 1.00f,
            ShotsFiredMultiplier: 1.00,
            DefaultPunchYaw: 0.10f,
            DefaultPunchPitch: 1.45f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0014,
                [MovementStateKind.Walk] = 0.0280,
                [MovementStateKind.Run] = 0.0680,
                [MovementStateKind.Strafe] = 0.0680,
                [MovementStateKind.MoveTurn] = 0.0820,
                [MovementStateKind.JumpAir] = 0.1250,
                [MovementStateKind.Crouch] = 0.0006,
                [MovementStateKind.AimDownSights] = 0.0004
            }),

        // 6. Shotgun
        new WeaponSimulationProfile(
            WeaponId: "XM1014_Sim",
            DisplayName: "XM1014 Tactical Semi-Auto Shotgun",
            Category: WeaponCategory.Shotgun,
            CustomGroupTag: "Shotgun",
            ClipCapacity: 7,
            DefaultShotsFired: 1,
            SpreadScaleY: 0.85f,
            ShotsFiredMultiplier: 1.05,
            DefaultPunchYaw: 0.45f,
            DefaultPunchPitch: 1.85f,
            PelletCount: 8,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0350,
                [MovementStateKind.Walk] = 0.0480,
                [MovementStateKind.Run] = 0.0720,
                [MovementStateKind.Strafe] = 0.0720,
                [MovementStateKind.MoveTurn] = 0.0880,
                [MovementStateKind.JumpAir] = 0.1350,
                [MovementStateKind.Crouch] = 0.0280,
                [MovementStateKind.AimDownSights] = 0.0300
            }),

        // 7. Custom Group (Extensible Burst / Tactical Prototype)
        new WeaponSimulationProfile(
            WeaponId: "CustomPulse_Sim",
            DisplayName: "Custom Tactical Burst Carbine",
            Category: WeaponCategory.Custom,
            CustomGroupTag: "TacticalBurstPrototype",
            ClipCapacity: 24,
            DefaultShotsFired: 2,
            SpreadScaleY: 0.45f,
            ShotsFiredMultiplier: 1.12,
            DefaultPunchYaw: 0.18f,
            DefaultPunchPitch: 0.55f,
            PelletCount: 1,
            SpreadByMovementState: new Dictionary<MovementStateKind, double>
            {
                [MovementStateKind.Stand] = 0.0025,
                [MovementStateKind.Walk] = 0.0110,
                [MovementStateKind.Run] = 0.0290,
                [MovementStateKind.Strafe] = 0.0290,
                [MovementStateKind.MoveTurn] = 0.0360,
                [MovementStateKind.JumpAir] = 0.0750,
                [MovementStateKind.Crouch] = 0.0015,
                [MovementStateKind.AimDownSights] = 0.0016
            })
    };
}

public sealed class RuntimeWeaponParameterResolver : IWeaponParameterResolver
{
    private readonly IWeaponRegistry _registry;
    private readonly WeaponCategorySimulationEngine _categoryEngine;

    public RuntimeWeaponParameterResolver(
        IWeaponRegistry registry,
        WeaponCategorySimulationEngine categoryEngine)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _categoryEngine = categoryEngine ?? throw new ArgumentNullException(nameof(categoryEngine));
    }

    public bool TryResolveWeapon(
        IActorSnapshot actor,
        IWeaponSnapshot weapon,
        MovementStateKind movementState,
        int sequenceNumber,
        out ResolvedWeaponParameters resolved)
    {
        resolved = default!;
        if (actor is null || weapon is null || string.IsNullOrWhiteSpace(weapon.WeaponName))
            return false;

        string rawName = weapon.WeaponName.Trim();
        WeaponSimulationProfile? profile = null;
        _registry.TryGetProfile(rawName, out profile);

        if (!_categoryEngine.TryGetConfiguration(rawName, out var catConfig) || catConfig is null)
        {
            if (profile is not null)
            {
                catConfig = WeaponCategorySimulationEngine.FromSimulationProfile(profile);
                _categoryEngine.RegisterConfiguration(catConfig);
            }
            else if (_registry.IdentifyCategory(rawName, out var inferredCat, out _))
            {
                if (inferredCat == WeaponCategory.SniperRifle &&
                    WeaponRegistry.TryResolveSniperAliasKey(rawName, out string sniperKey) &&
                    _categoryEngine.TryGetConfiguration(sniperKey, out var sniperCfg) &&
                    sniperCfg is not null)
                {
                    catConfig = sniperCfg;
                    _registry.TryGetProfile(sniperKey, out profile);
                }
                else if (inferredCat == WeaponCategory.SniperRifle &&
                         _categoryEngine.TryGetConfiguration("awp", out var defaultSniperCfg) &&
                         defaultSniperCfg is not null)
                {
                    catConfig = defaultSniperCfg;
                    _registry.TryGetProfile("awp", out profile);
                }
                else
                {
                    var fallback = _registry.GetByCategory(inferredCat).FirstOrDefault();
                    if (fallback is null)
                        return false;
                    catConfig = WeaponCategorySimulationEngine.FromSimulationProfile(fallback);
                }
            }
            else
            {
                return false;
            }
        }

        string canonicalId = profile?.WeaponId ?? catConfig.WeaponId;
        string displayName = profile?.DisplayName ?? catConfig.DisplayName;
        int pelletCount = profile?.PelletCount ?? Math.Max(1, weapon.PelletCount);

        double resolvedSpread = weapon.Spread;
        float resolvedScaleY = weapon.SpreadScaleY;
        double resolvedShotsMultiplier = weapon.ShotsFiredMultiplier;
        double resolvedAccuracy = weapon.Accuracy;
        bool resolvedIsScoped = weapon.IsScoped || actor.IsAimingDownSights || (actor.Fov > 0 && actor.Fov < 90);

        if (catConfig.Category == WeaponCategory.SniperRifle)
        {
            int effectiveFov = actor.Fov <= 0
                ? ((weapon.IsScoped || actor.IsAimingDownSights) ? 40 : 90)
                : actor.Fov;
            resolvedIsScoped = effectiveFov < 90;

            // If a weapon switch or adapter snapshot updated WeaponName to a Sniper without rehydrating
            // Sniper-specific snapshot parameters (still carrying default AssaultRifle fields), restore Sniper parameters.
            if (weapon.Category != WeaponCategory.SniperRifle)
            {
                resolvedScaleY = catConfig.SpreadScaleY;
                resolvedShotsMultiplier = WeaponAccuracyFormulas.CalculateShotsFiredSpread(catConfig, Math.Max(1, weapon.ShotsFired));
                resolvedAccuracy = catConfig.DefaultAccuracy;

                if (Math.Abs(weapon.Spread - 0.004) < 1e-6)
                {
                    var opPosture = new SimulatedOperatorPostureState
                    {
                        IsOnGround = actor.IsOnGround,
                        DuckState = actor.IsCrouching ? 0 : 3,
                        HorizontalSpeed2D = Math.Abs(actor.HorizontalVelocity),
                        Fov = effectiveFov,
                        LastFov = effectiveFov,
                        DelayFov = effectiveFov,
                        IsAimingDownSights = resolvedIsScoped
                    };
                    OperatorPostureKind posture = WeaponAccuracyFormulas.ClassifyPosture(catConfig, in opPosture);
                    double baseParam = WeaponAccuracyFormulas.GetPostureSpreadParam(catConfig, posture);
                    resolvedSpread = WeaponAccuracyFormulas.ComputeSniperSpread(catConfig, baseParam, posture, resolvedIsScoped, effectiveFov);
                }
            }
        }

        resolved = new ResolvedWeaponParameters(
            CanonicalWeaponId: canonicalId,
            DisplayName: displayName,
            Category: catConfig.Category,
            FormulaKind: catConfig.FormulaKind,
            Spread: resolvedSpread,
            SpreadScaleY: resolvedScaleY,
            ShotsFiredMultiplier: resolvedShotsMultiplier,
            PunchYaw: actor.PunchYaw,
            PunchPitch: actor.PunchPitch,
            PelletCount: pelletCount,
            Accuracy: resolvedAccuracy,
            IsScoped: resolvedIsScoped);
        return true;
    }
}
