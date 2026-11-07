using System.Globalization;
using System.Xml.Serialization;
using HackTK.Next.Abstractions;

namespace HackTK.Next.Configuration;

public static class NextWireProtocol
{
    public const int CurrentVersion = 3;
}

public static class RuntimeSessionPaths
{
    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HackTKRebuild");

    public static string ForProcess(int processId) =>
        Path.Combine(RootDirectory, "sessions", processId.ToString(CultureInfo.InvariantCulture));

    public static string StatusFile(int processId) =>
        Path.Combine(ForProcess(processId), "status.xml");

    public static string CommandFile(int processId) =>
        Path.Combine(ForProcess(processId), "command.xml");
}

[XmlRoot("Command")]
public sealed class SessionCommandEnvelope
{
    [XmlElement("Protocol")]
    public int ProtocolVersion { get; set; } = NextWireProtocol.CurrentVersion;

    [XmlElement("Session")]
    public string SessionToken { get; set; } = string.Empty;

    public long Revision { get; set; }
    public long ExpiresUtcTicks { get; set; }
    public bool Connected { get; set; }

    [XmlElement("Settings")]
    public EngineProfile Profile { get; set; } = new();

    public bool IsValid(string expectedSessionToken, long nowUtcTicks, long minimumRevision)
    {
        return ProtocolVersion == NextWireProtocol.CurrentVersion &&
               !string.IsNullOrWhiteSpace(SessionToken) &&
               string.Equals(SessionToken, expectedSessionToken, StringComparison.Ordinal) &&
               Revision >= minimumRevision &&
               Revision > 0 &&
               ExpiresUtcTicks > nowUtcTicks &&
               ExpiresUtcTicks <= nowUtcTicks + TimeSpan.FromSeconds(6).Ticks &&
               Profile is not null &&
               Profile.Validate().Count == 0;
    }
}

[XmlType("FeatureState")]
public sealed class RuntimeFeatureState
{
    public string Feature { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

[XmlRoot("Status")]
public sealed class RuntimeSessionStatus
{
    public int Protocol { get; set; } = NextWireProtocol.CurrentVersion;
    public int ProcessId { get; set; }
    public long ProcessStartUtcTicks { get; set; }
    public string Session { get; set; } = string.Empty;
    public long MainThreadUtcTicks { get; set; }
    public long AppliedRevision { get; set; }
    public bool Connected { get; set; }
    public string GameDirectory { get; set; } = string.Empty;
    public string UnityVersion { get; set; } = string.Empty;
    public string ModuleVersion { get; set; } = string.Empty;
    public string ModuleSha256 { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string TransportError { get; set; } = string.Empty;
    public string CommandError { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public RuntimeFeatureState[] Features { get; set; } = Array.Empty<RuntimeFeatureState>();

    public bool IsFresh(long nowUtcTicks)
    {
        long age = nowUtcTicks - MainThreadUtcTicks;
        return Protocol == NextWireProtocol.CurrentVersion &&
               age >= -TimeSpan.FromSeconds(1).Ticks &&
               age < TimeSpan.FromSeconds(5).Ticks;
    }
}

public sealed class SessionLeaseManager
{
    private readonly ITimeProvider _timeProvider;
    private SessionCommandEnvelope? _accepted;
    private long _monotonicDeadlineMs;
    private bool _expired;

    public SessionLeaseManager(ITimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public long CurrentRevision => _accepted?.Revision ?? 0;
    public EngineProfile CurrentProfile => _accepted?.Profile.Clone() ?? new EngineProfile();
    public string LastRejectionReason { get; private set; } = string.Empty;

    public bool TryApplyEnvelope(SessionCommandEnvelope? next, string sessionToken)
    {
        long utcNowTicks = _timeProvider.UtcNow.UtcTicks;
        long monotonicMs = _timeProvider.MonotonicMilliseconds;

        if (_accepted is not null && monotonicMs >= _monotonicDeadlineMs)
            _expired = true;

        if (next is null)
        {
            LastRejectionReason = "Chưa có gói lệnh hợp lệ.";
            return false;
        }

        if (!next.IsValid(sessionToken, utcNowTicks, CurrentRevision))
        {
            LastRejectionReason = "Gói lệnh sai phiên, sai giao thức, cấu hình không hợp lệ hoặc đã hết hạn.";
            return false;
        }

        if (_accepted is not null && next.Revision == _accepted.Revision)
        {
            if (_expired)
            {
                LastRejectionReason = "Lease đã hết hạn; cần tăng revision mới để kết nối lại.";
                return false;
            }

            if (next.Connected != _accepted.Connected || !next.Profile.SameEffectiveValues(_accepted.Profile))
            {
                LastRejectionReason = "Không được phép thay đổi cấu hình hoặc trạng thái kết nối trên cùng một revision.";
                return false;
            }

            if (next.ExpiresUtcTicks > _accepted.ExpiresUtcTicks)
            {
                long extensionMs = Math.Min(5000L, (next.ExpiresUtcTicks - utcNowTicks) / TimeSpan.TicksPerMillisecond);
                _monotonicDeadlineMs = monotonicMs + extensionMs;
                _accepted.ExpiresUtcTicks = next.ExpiresUtcTicks;
            }
        }
        else
        {
            long leaseDurationMs = Math.Min(5000L, (next.ExpiresUtcTicks - utcNowTicks) / TimeSpan.TicksPerMillisecond);
            _accepted = new SessionCommandEnvelope
            {
                ProtocolVersion = next.ProtocolVersion,
                SessionToken = sessionToken,
                Revision = next.Revision,
                Connected = next.Connected,
                ExpiresUtcTicks = next.ExpiresUtcTicks,
                Profile = next.Profile.Clone()
            };
            _monotonicDeadlineMs = monotonicMs + leaseDurationMs;
            _expired = false;
        }

        LastRejectionReason = string.Empty;
        return _accepted.Connected && monotonicMs < _monotonicDeadlineMs;
    }
}
