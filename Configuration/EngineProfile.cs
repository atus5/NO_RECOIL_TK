using System.Xml.Serialization;
using HackTK.Next.Abstractions;

namespace HackTK.Next.Configuration;

[XmlRoot("Settings")]
public sealed class EngineProfile : IEngineProfile
{
    private float _legacyAimFov = 30f;
    private float _aimRadiusPixels = 8f;
    private bool _legacyFovSpecified;
    private bool _radiusSpecified;

    public bool AimEnabled { get; set; }
    public bool DrawAimRadius { get; set; }

    public float AimFov
    {
        get => _legacyAimFov;
        set
        {
            _legacyAimFov = value;
            _legacyFovSpecified = true;
        }
    }

    public float AimRadiusPixels
    {
        get => _radiusSpecified
            ? _aimRadiusPixels
            : _legacyFovSpecified
                ? (_legacyAimFov * 1080f / 180f)
                : 8f;
        set
        {
            _aimRadiusPixels = value;
            _radiusSpecified = true;
        }
    }

    public float Smooth { get; set; } = 5f;
    public TargetPart Target { get; set; } = TargetPart.Head;
    public TriggerKey ActivationKey { get; set; } = TriggerKey.LeftAlt;
    public bool RecoilCompensation { get; set; }

    [XmlElement("NoRecoil")]
    public bool SmoothCameraRecoil { get; set; }

    [XmlElement("NoSpread")]
    public bool CompensateCommandSpread { get; set; }

    [XmlElement("RecordShotDiagnostics")]
    public bool RecordDiagnostics { get; set; }

    [XmlElement("AutoDisableRawInput")]
    public bool ManageRawInput { get; set; }

    [XmlElement("Box")]
    public bool EspBox { get; set; }

    [XmlElement("Skeleton")]
    public bool EspSkeleton { get; set; }

    [XmlElement("Snaplines")]
    public bool EspSnaplines { get; set; }

    [XmlElement("Names")]
    public bool EspNames { get; set; }

    [XmlElement("Health")]
    public bool EspHealth { get; set; }

    public float BoxHue { get; set; }
    public float BoneHue { get; set; } = 0.33f;
    public bool PlayerChams { get; set; }
    public bool HandChams { get; set; }
    public bool WeaponChams { get; set; }
    public ShaderRenderMode ChamsMode { get; set; } = ShaderRenderMode.Flat;
    public float PlayerHue { get; set; }
    public float HandHue { get; set; } = 0.33f;
    public float WeaponHue { get; set; } = 0.66f;
    public bool Crosshair { get; set; }
    public bool BunnyHop { get; set; }

    [XmlElement("Viewmodel")]
    public bool ViewmodelOffset { get; set; }

    public float ViewX { get; set; }
    public float ViewY { get; set; }
    public float ViewZ { get; set; }

    public EngineProfile Clone() => (EngineProfile)MemberwiseClone();

    public bool SameEffectiveValues(EngineProfile? other)
    {
        if (other is null) return false;
        return AimEnabled == other.AimEnabled &&
               DrawAimRadius == other.DrawAimRadius &&
               AimRadiusPixels.Equals(other.AimRadiusPixels) &&
               Smooth.Equals(other.Smooth) &&
               Target == other.Target &&
               ActivationKey == other.ActivationKey &&
               RecoilCompensation == other.RecoilCompensation &&
               SmoothCameraRecoil == other.SmoothCameraRecoil &&
               CompensateCommandSpread == other.CompensateCommandSpread &&
               RecordDiagnostics == other.RecordDiagnostics &&
               ManageRawInput == other.ManageRawInput &&
               EspBox == other.EspBox &&
               EspSkeleton == other.EspSkeleton &&
               EspSnaplines == other.EspSnaplines &&
               EspNames == other.EspNames &&
               EspHealth == other.EspHealth &&
               BoxHue.Equals(other.BoxHue) &&
               BoneHue.Equals(other.BoneHue) &&
               PlayerChams == other.PlayerChams &&
               HandChams == other.HandChams &&
               WeaponChams == other.WeaponChams &&
               ChamsMode == other.ChamsMode &&
               PlayerHue.Equals(other.PlayerHue) &&
               HandHue.Equals(other.HandHue) &&
               WeaponHue.Equals(other.WeaponHue) &&
               Crosshair == other.Crosshair &&
               BunnyHop == other.BunnyHop &&
               ViewmodelOffset == other.ViewmodelOffset &&
               ViewX.Equals(other.ViewX) &&
               ViewY.Equals(other.ViewY) &&
               ViewZ.Equals(other.ViewZ);
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        CheckRange(errors, nameof(AimFov), AimFov, 10f, 180f);
        CheckRange(errors, nameof(AimRadiusPixels), AimRadiusPixels, 1f, 2048f);
        CheckRange(errors, nameof(Smooth), Smooth, 1f, 10f);
        CheckRange(errors, nameof(ViewX), ViewX, -10f, 10f);
        CheckRange(errors, nameof(ViewY), ViewY, -10f, 10f);
        CheckRange(errors, nameof(ViewZ), ViewZ, -10f, 10f);
        CheckRange(errors, nameof(BoxHue), BoxHue, 0f, 1f);
        CheckRange(errors, nameof(BoneHue), BoneHue, 0f, 1f);
        CheckRange(errors, nameof(PlayerHue), PlayerHue, 0f, 1f);
        CheckRange(errors, nameof(HandHue), HandHue, 0f, 1f);
        CheckRange(errors, nameof(WeaponHue), WeaponHue, 0f, 1f);

        if (!Enum.IsDefined(typeof(TargetPart), Target))
            errors.Add("Target không hợp lệ.");
        if (!Enum.IsDefined(typeof(ShaderRenderMode), ChamsMode))
            errors.Add("ChamsMode không hợp lệ.");
        if (!Enum.IsDefined(typeof(TriggerKey), ActivationKey))
            errors.Add("ActivationKey không hợp lệ.");

        return errors;
    }

    private static void CheckRange(List<string> errors, string name, float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
        {
            errors.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} phải nằm trong [{1}, {2}].",
                name,
                min,
                max));
        }
    }
}
