using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;

namespace HackTK.Next.UI;

public sealed record RuntimeProcessOption(
    int ProcessId,
    long StartUtcTicks,
    string ExecutablePath,
    bool HasStatusFile,
    string ProcessName = "SSJJ_BattleClient_Unity",
    bool IsAuthorizedTestRuntime = false)
{
    public override string ToString() =>
        IsAuthorizedTestRuntime
            ? $"PID {ProcessId} [Runtime Thử Nghiệm] — {ExecutablePath}"
            : $"PID {ProcessId} — {ExecutablePath}";
}

public sealed class DashboardPresenter : IDisposable
{
    public const string TargetGameProcessName = "SSJJ_BattleClient_Unity";
    public const string AuthorizedTestRuntimeProcessName = "HackTK.Next.Runtime";

    private static readonly string[] SupportedProcessNames =
    {
        TargetGameProcessName,
        AuthorizedTestRuntimeProcessName
    };

    private readonly IConfigurationStore<EngineProfile> _profileStore;
    private readonly IConfigurationStore<SessionCommandEnvelope> _commandStore;
    private readonly IConfigurationStore<RuntimeSessionStatus> _statusStore;
    private readonly ITimeProvider _timeProvider;
    private readonly IDiagnosticLogger _logger;
    private readonly Stopwatch _pendingAckWatch = new();
    private readonly string _defaultProfilePath;
    private string _profilePath;

    private RuntimeProcessOption? _selectedProcess;
    private Process? _spawnedTestRuntimeProcess;
    private string _activeSessionToken = string.Empty;
    private long _activeRevision;
    private long _lastConfirmedRevision;
    private RuntimeSessionStatus? _lastConfirmedStatus;
    private EngineProfile _appliedProfile = new();

    public DashboardPresenter(
        IConfigurationStore<EngineProfile> profileStore,
        IConfigurationStore<SessionCommandEnvelope> commandStore,
        IConfigurationStore<RuntimeSessionStatus> statusStore,
        ITimeProvider timeProvider,
        IDiagnosticLogger logger,
        string initialProfilePath)
    {
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _commandStore = commandStore ?? throw new ArgumentNullException(nameof(commandStore));
        _statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultProfilePath = string.IsNullOrWhiteSpace(initialProfilePath)
            ? throw new ArgumentException("Đường dẫn profile không hợp lệ.", nameof(initialProfilePath))
            : Path.GetFullPath(initialProfilePath);
        _profilePath = _defaultProfilePath;
        CurrentProfile = new EngineProfile();
    }

    public EngineProfile CurrentProfile { get; private set; }
    public string ProfilePath => _profilePath;
    public string DefaultProfilePath => _defaultProfilePath;
    public bool IsConnected { get; private set; }
    public RuntimeProcessOption? ConnectedProcess => _selectedProcess;
    public long ActiveRevision => _activeRevision;
    public long LastConfirmedRevision => _lastConfirmedRevision;
    public RuntimeSessionStatus? LastConfirmedStatus => _lastConfirmedStatus;
    public int AckTimeoutMs { get; set; } = 4000;
    public bool KeepSpawnedRuntimeOnDispose { get; set; }
    public string ConnectionBanner { get; private set; } = "Chưa kết nối — cần tiến trình runtime đang chạy và có heartbeat hợp lệ.";

    public IReadOnlyList<RuntimeProcessOption> DiscoverProcesses(out string statusMessage)
    {
        var list = new List<RuntimeProcessOption>();
        var seenPids = new HashSet<int>();
        string? lastProcessError = null;

        foreach (string procName in SupportedProcessNames)
        {
            bool isTestRuntime = string.Equals(procName, AuthorizedTestRuntimeProcessName, StringComparison.OrdinalIgnoreCase);
            foreach (var proc in Process.GetProcessesByName(procName))
            {
                using (proc)
                {
                    try
                    {
                        if (!seenPids.Add(proc.Id))
                            continue;

                        long startTicks = proc.StartTime.ToUniversalTime().Ticks;
                        bool hasStatus = File.Exists(RuntimeSessionPaths.StatusFile(proc.Id));
                        string exePath;
                        try
                        {
                            exePath = proc.MainModule?.FileName ?? ResolveFallbackProcessDescription(proc.Id, hasStatus);
                        }
                        catch (Win32Exception)
                        {
                            exePath = ResolveFallbackProcessDescription(proc.Id, hasStatus);
                        }

                        list.Add(new RuntimeProcessOption(proc.Id, startTicks, exePath, hasStatus, proc.ProcessName, isTestRuntime));
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    {
                        lastProcessError = ex.Message;
                    }
                }
            }
        }

        // Also scan active session directories for any authorized host process publishing fresh status.xml
        string sessionsRoot = Path.Combine(RuntimeSessionPaths.RootDirectory, "sessions");
        if (Directory.Exists(sessionsRoot))
        {
            long nowTicks = _timeProvider.UtcNow.UtcTicks;
            foreach (string dir in Directory.GetDirectories(sessionsRoot))
            {
                string folderName = Path.GetFileName(dir);
                if (!int.TryParse(folderName, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) || seenPids.Contains(pid))
                    continue;

                string statusFile = RuntimeSessionPaths.StatusFile(pid);
                if (!File.Exists(statusFile))
                    continue;

                try
                {
                    var st = _statusStore.Load(statusFile);
                    if (!st.IsFresh(nowTicks))
                        continue;

                    using var proc = Process.GetProcessById(pid);
                    long startTicks = proc.StartTime.ToUniversalTime().Ticks;
                    if (startTicks != st.ProcessStartUtcTicks)
                        continue;

                    string exePath;
                    try { exePath = proc.MainModule?.FileName ?? proc.ProcessName; }
                    catch (Win32Exception) { exePath = proc.ProcessName; }

                    seenPids.Add(pid);
                    bool isTest = !string.Equals(proc.ProcessName, TargetGameProcessName, StringComparison.OrdinalIgnoreCase);
                    list.Add(new RuntimeProcessOption(pid, startTicks, exePath, true, proc.ProcessName, isTest));
                }
                catch
                {
                    // Ignore stale or exited session directories.
                }
            }
        }

        list.Sort((a, b) => b.StartUtcTicks.CompareTo(a.StartUtcTicks));

        if (list.Count > 0)
        {
            statusMessage = $"Tìm thấy {list.Count} tiến trình runtime khả dụng.";
        }
        else if (!string.IsNullOrEmpty(lastProcessError))
        {
            statusMessage = "Không đọc được tiến trình: " + lastProcessError;
        }
        else
        {
            statusMessage = $"Không thấy {TargetGameProcessName} hoặc {AuthorizedTestRuntimeProcessName} đang chạy.";
        }

        return list;
    }

    private string ResolveFallbackProcessDescription(int processId, bool hasStatus)
    {
        if (hasStatus)
        {
            try
            {
                var st = _statusStore.Load(RuntimeSessionPaths.StatusFile(processId));
                if (!string.IsNullOrWhiteSpace(st.GameDirectory))
                    return $"{st.GameDirectory} (xác minh qua heartbeat)";
            }
            catch
            {
                // Fall back to generic description below if status file is transiently locked or unreadable.
            }
        }

        return "Windows từ chối đọc đường dẫn; xác minh bằng heartbeat PID/start-time";
    }

    public static bool IsProcessAlive(RuntimeProcessOption? option)
    {
        if (option is null) return false;
        try
        {
            using var proc = Process.GetProcessById(option.ProcessId);
            if (proc.HasExited)
                return false;

            bool nameMatches = string.Equals(proc.ProcessName, option.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                               SupportedProcessNames.Any(n => string.Equals(proc.ProcessName, n, StringComparison.OrdinalIgnoreCase));
            return nameMatches && proc.StartTime.ToUniversalTime().Ticks == option.StartUtcTicks;
        }
        catch
        {
            return false;
        }
    }

    public bool TryStartAuthorizedTestRuntime(
        out RuntimeProcessOption? startedOption,
        out string message,
        int durationMs = 0)
    {
        startedOption = null;
        try
        {
            if (_spawnedTestRuntimeProcess is not null && !_spawnedTestRuntimeProcess.HasExited)
            {
                var existingList = DiscoverProcesses(out _);
                startedOption = existingList.FirstOrDefault(p => p.ProcessId == _spawnedTestRuntimeProcess.Id);
                if (startedOption is not null)
                {
                    message = $"Runtime thử nghiệm đang chạy (PID {startedOption.ProcessId}).";
                    return true;
                }
            }

            string? runtimeExe = ResolveTestRuntimeExecutablePath();
            if (string.IsNullOrEmpty(runtimeExe) || !File.Exists(runtimeExe))
            {
                message = "Không tìm thấy tệp thực thi HackTK.Next.Runtime.exe trong thư mục ứng dụng.";
                _logger.Record("RUNTIME_LAUNCH_ERROR", message);
                return false;
            }

            using var currentProc = Process.GetCurrentProcess();
            string args = $"--parent-pid {currentProc.Id}";
            if (durationMs > 0)
                args += $" --duration-ms {durationMs.ToString(CultureInfo.InvariantCulture)}";

            var psi = new ProcessStartInfo
            {
                FileName = runtimeExe,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(runtimeExe) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var proc = Process.Start(psi);
            if (proc is null)
            {
                message = "Không thể khởi chạy tiến trình HackTK.Next.Runtime.exe.";
                return false;
            }

            _spawnedTestRuntimeProcess = proc;
            long startTicks = proc.StartTime.ToUniversalTime().Ticks;
            string statusPath = RuntimeSessionPaths.StatusFile(proc.Id);

            var waitWatch = Stopwatch.StartNew();
            while (waitWatch.ElapsedMilliseconds < 3500)
            {
                if (proc.HasExited)
                {
                    string stderr = proc.StandardError.ReadToEnd();
                    message = $"HackTK.Next.Runtime.exe thoát sớm (ExitCode={proc.ExitCode}): {stderr}";
                    return false;
                }

                if (File.Exists(statusPath))
                {
                    try
                    {
                        var st = _statusStore.Load(statusPath);
                        if (st.ProcessId == proc.Id && st.IsFresh(_timeProvider.UtcNow.UtcTicks))
                        {
                            startedOption = new RuntimeProcessOption(
                                proc.Id,
                                startTicks,
                                runtimeExe,
                                true,
                                proc.ProcessName,
                                IsAuthorizedTestRuntime: true);
                            message = $"Đã khởi chạy Runtime Thử Nghiệm độc lập (PID {proc.Id}).";
                            _logger.Record("RUNTIME_LAUNCHED", message, new Dictionary<string, string>
                            {
                                ["pid"] = proc.Id.ToString(CultureInfo.InvariantCulture),
                                ["session"] = st.Session
                            });
                            return true;
                        }
                    }
                    catch
                    {
                        // Wait for atomic status file write to complete.
                    }
                }

                Thread.Sleep(25);
            }

            message = $"Đã khởi chạy PID {proc.Id} nhưng chưa nhận được status.xml sau 3.5 giây.";
            return false;
        }
        catch (Exception ex)
        {
            message = "Lỗi khởi chạy runtime thử nghiệm: " + ex.Message;
            _logger.Record("RUNTIME_LAUNCH_ERROR", message);
            return false;
        }
    }

    public void StopSpawnedTestRuntime()
    {
        if (_spawnedTestRuntimeProcess is null)
            return;

        try
        {
            if (!_spawnedTestRuntimeProcess.HasExited)
            {
                string stopSignal = Path.Combine(RuntimeSessionPaths.ForProcess(_spawnedTestRuntimeProcess.Id), "stop.signal");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(stopSignal)!);
                    File.WriteAllText(stopSignal, "STOP");
                }
                catch { }

                if (!_spawnedTestRuntimeProcess.WaitForExit(1500))
                {
                    _spawnedTestRuntimeProcess.Kill(entireProcessTree: true);
                    _spawnedTestRuntimeProcess.WaitForExit(1000);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors on process exit.
        }
        finally
        {
            _spawnedTestRuntimeProcess.Dispose();
            _spawnedTestRuntimeProcess = null;
        }
    }

    private static string? ResolveTestRuntimeExecutablePath()
    {
        string baseDir = AppContext.BaseDirectory;
        string direct = Path.Combine(baseDir, "HackTK.Next.Runtime.exe");
        if (File.Exists(direct))
            return direct;

        string[] relativeCandidates =
        {
            Path.Combine(baseDir, "..", "Runtime", "bin", "Release", "net10.0", "HackTK.Next.Runtime.exe"),
            Path.Combine(baseDir, "..", "Runtime", "bin", "Debug", "net10.0", "HackTK.Next.Runtime.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Runtime", "bin", "Release", "net10.0", "HackTK.Next.Runtime.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Runtime", "bin", "Debug", "net10.0", "HackTK.Next.Runtime.exe")
        };

        foreach (string candidate in relativeCandidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    public bool TryConnect(RuntimeProcessOption? candidate, out RuntimeSessionStatus? liveStatus, out string message)
    {
        liveStatus = null;
        if (candidate is null || !IsProcessAlive(candidate))
        {
            message = "Chọn tiến trình runtime đang chạy trước.";
            return false;
        }

        Disconnect(out _);

        try
        {
            var status = ReadValidatedStatus(candidate);
            long baseRevision = status.AppliedRevision;
            string existingCmdPath = RuntimeSessionPaths.CommandFile(candidate.ProcessId);
            if (File.Exists(existingCmdPath))
            {
                try
                {
                    var prevCmd = _commandStore.Load(existingCmdPath);
                    if (string.Equals(prevCmd.SessionToken, status.Session, StringComparison.Ordinal) &&
                        prevCmd.Revision > baseRevision)
                    {
                        baseRevision = prevCmd.Revision;
                    }
                }
                catch
                {
                    // Ignore unreadable prior command file; it will be atomically replaced below.
                }
            }

            _selectedProcess = candidate;
            _activeSessionToken = status.Session;
            _activeRevision = baseRevision + 1;
            _appliedProfile = new EngineProfile();
            IsConnected = true;
            _pendingAckWatch.Restart();
            long preDispatchMainThreadTicks = status.MainThreadUtcTicks;

            WriteCommandEnvelope(active: true);
            _logger.Record("RUNTIME_CONNECT_DISPATCH", $"Gửi yêu cầu kết nối revision #{_activeRevision} tới PID {candidate.ProcessId}.");

            if (!WaitForRevisionAcknowledgement(_activeRevision, AckTimeoutMs, preDispatchMainThreadTicks, out liveStatus, out string ackError))
            {
                Disconnect(out _);
                ConnectionBanner = "Kết nối thất bại — " + ackError;
                message = "Runtime không xác nhận lệnh kết nối: " + ackError;
                _logger.Record("RUNTIME_CONNECT_ERROR", message);
                return false;
            }

            _lastConfirmedRevision = _activeRevision;
            _lastConfirmedStatus = liveStatus;
            message = $"Đã kết nối và nhận xác nhận (ACK #{_activeRevision}) từ PID {candidate.ProcessId} với mọi chức năng mặc định tắt.";
            _logger.Record("RUNTIME_CONNECT", message, new Dictionary<string, string>
            {
                ["pid"] = candidate.ProcessId.ToString(CultureInfo.InvariantCulture),
                ["session"] = _activeSessionToken,
                ["acknowledgedRevision"] = _activeRevision.ToString(CultureInfo.InvariantCulture)
            });
            return true;
        }
        catch (Exception ex)
        {
            Disconnect(out _);
            ConnectionBanner = "Mất kết nối — " + ex.Message;
            message = ex.Message;
            _logger.Record("RUNTIME_CONNECT_ERROR", ex.Message);
            return false;
        }
    }

    public bool TryConnectAndApply(RuntimeProcessOption? candidate, out RuntimeSessionStatus? liveStatus, out string message)
    {
        if (!TryConnect(candidate, out liveStatus, out message))
            return false;

        return TryApplyCurrentProfile(out liveStatus, out message);
    }

    public bool TryApplyCurrentProfile(out string message) =>
        TryApplyCurrentProfile(out _, out message);

    public bool TryApplyCurrentProfile(out RuntimeSessionStatus? confirmedStatus, out string message)
    {
        confirmedStatus = null;
        if (!IsConnected || _selectedProcess is null)
        {
            message = "Chưa kết nối được module. Không có thay đổi nào được áp dụng.";
            return false;
        }

        var errors = CurrentProfile.Validate();
        if (errors.Count > 0)
        {
            message = string.Join(Environment.NewLine, errors);
            return false;
        }

        try
        {
            long preDispatchMainThreadTicks = TryPeekMainThreadTicks(_selectedProcess);
            _appliedProfile = CurrentProfile.Clone();
            _activeRevision++;
            _pendingAckWatch.Restart();
            WriteCommandEnvelope(active: true);

            _logger.Record("RUNTIME_APPLY_DISPATCH", $"Đã ghi gói lệnh cấu hình #{_activeRevision}; đang chờ xác nhận thực thi từ runtime PID {_selectedProcess.ProcessId}.");

            if (!WaitForRevisionAcknowledgement(_activeRevision, AckTimeoutMs, preDispatchMainThreadTicks, out confirmedStatus, out string ackError))
            {
                message = $"Chưa nhận được xác nhận (ACK) từ runtime cho cấu hình #{_activeRevision}: {ackError}";
                _logger.Record("RUNTIME_APPLY_UNCONFIRMED", message, new Dictionary<string, string>
                {
                    ["pid"] = _selectedProcess.ProcessId.ToString(CultureInfo.InvariantCulture),
                    ["revision"] = _activeRevision.ToString(CultureInfo.InvariantCulture),
                    ["reason"] = ackError
                });
                return false;
            }

            _lastConfirmedRevision = _activeRevision;
            _lastConfirmedStatus = confirmedStatus;
            int activeFeatures = confirmedStatus?.Features?.Count(IsFeatureEntryActive) ?? 0;

            message = $"Đã áp dụng và xác nhận thực thi cấu hình #{_activeRevision} trên PID {_selectedProcess.ProcessId} ({activeFeatures} hạng mục Active).";
            _logger.Record("RUNTIME_APPLY_ACK", message, new Dictionary<string, string>
            {
                ["pid"] = _selectedProcess.ProcessId.ToString(CultureInfo.InvariantCulture),
                ["acknowledgedRevision"] = _activeRevision.ToString(CultureInfo.InvariantCulture),
                ["activeFeatureEntries"] = activeFeatures.ToString(CultureInfo.InvariantCulture)
            });
            return true;
        }
        catch (Exception ex)
        {
            message = "Lỗi gửi hoặc xác nhận cấu hình: " + ex.Message;
            _logger.Record("RUNTIME_APPLY_ERROR", message);
            return false;
        }
    }

    public static bool IsFeatureEntryActive(RuntimeFeatureState? feature)
    {
        if (feature is null || string.IsNullOrWhiteSpace(feature.State))
            return false;

        if (string.Equals(feature.State, nameof(ModuleLifecycleState.Active), StringComparison.OrdinalIgnoreCase))
            return true;

        if (feature.State.Contains("hoạt động", StringComparison.OrdinalIgnoreCase))
        {
            string detail = feature.Detail?.Trim() ?? string.Empty;
            return !string.Equals(detail, "Tắt", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(detail, "Tắt | Tắt", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private long TryPeekMainThreadTicks(RuntimeProcessOption process)
    {
        try
        {
            string statusPath = RuntimeSessionPaths.StatusFile(process.ProcessId);
            if (File.Exists(statusPath))
                return _statusStore.Load(statusPath).MainThreadUtcTicks;
        }
        catch
        {
            // Ignore transient read lock when sampling pre-dispatch baseline timestamp.
        }

        return _lastConfirmedStatus?.MainThreadUtcTicks ?? 0L;
    }

    private bool WaitForRevisionAcknowledgement(
        long expectedRevision,
        int timeoutMs,
        long preDispatchMainThreadTicks,
        out RuntimeSessionStatus? confirmedStatus,
        out string errorDetail)
    {
        confirmedStatus = null;
        errorDetail = string.Empty;
        if (_selectedProcess is null)
        {
            errorDetail = "Không có tiến trình được chọn.";
            return false;
        }

        var watch = Stopwatch.StartNew();
        var leaseRefreshWatch = Stopwatch.StartNew();
        int effectiveTimeout = Math.Max(50, timeoutMs);
        string lastError = "Chưa nhận được phản hồi AppliedRevision mới.";

        while (watch.ElapsedMilliseconds <= effectiveTimeout)
        {
            try
            {
                if (leaseRefreshWatch.ElapsedMilliseconds >= 1500)
                {
                    WriteCommandEnvelope(active: true);
                    leaseRefreshWatch.Restart();
                }

                var status = ReadValidatedStatus(_selectedProcess);
                if (!string.Equals(status.Session, _activeSessionToken, StringComparison.Ordinal))
                {
                    errorDetail = "Phiên (SessionToken) của runtime đã thay đổi; cần kết nối lại.";
                    return false;
                }

                if (status.AppliedRevision > expectedRevision)
                {
                    errorDetail = "Revision đã bị bảng điều khiển khác thay đổi; cần kết nối lại.";
                    return false;
                }

                if (status.AppliedRevision == expectedRevision && status.Connected &&
                    string.IsNullOrEmpty(status.CommandError) && string.IsNullOrEmpty(status.TransportError))
                {
                    _pendingAckWatch.Reset();
                    confirmedStatus = status;
                    UpdateBannerFromStatus(_selectedProcess, status, acknowledged: true);
                    return true;
                }

                // Never abort early on stale pre-dispatch CommandError/TransportError (such as "Chưa có lệnh hợp lệ."
                // or expired-lease messages written before the runtime reads the newly dispatched command.xml).
                bool isPostDispatchSnapshot = status.MainThreadUtcTicks > preDispatchMainThreadTicks;
                if (isPostDispatchSnapshot &&
                    (!string.IsNullOrEmpty(status.CommandError) || !string.IsNullOrEmpty(status.TransportError)))
                {
                    lastError = $"{status.CommandError} {status.TransportError}".Trim();
                }
                else
                {
                    lastError = $"AppliedRevision hiện tại={status.AppliedRevision}, đang chờ #{expectedRevision} (Connected={status.Connected}).";
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            if (watch.ElapsedMilliseconds + 20 > effectiveTimeout)
                break;

            Thread.Sleep(20);
        }

        errorDetail = lastError;
        return false;
    }

    public void Disconnect(out string message)
    {
        string? disconnectError = null;
        if (IsConnected && _selectedProcess is not null)
        {
            try
            {
                _activeRevision++;
                WriteCommandEnvelope(active: false);
            }
            catch (Exception ex)
            {
                disconnectError = ex.Message;
            }
        }

        IsConnected = false;
        _selectedProcess = null;
        _activeSessionToken = string.Empty;
        _pendingAckWatch.Reset();
        ConnectionBanner = "Chưa kết nối — cần tiến trình runtime đang chạy và có heartbeat hợp lệ.";
        message = disconnectError is null
            ? "Đã ngắt kết nối an toàn."
            : $"Không gửi được lệnh ngắt: {disconnectError}. Module sẽ hết lease.";
    }

    public bool TryTickConnection(out RuntimeSessionStatus? liveStatus, out string statusMessage)
    {
        liveStatus = null;
        if (!IsConnected || _selectedProcess is null)
        {
            statusMessage = "Chưa kết nối.";
            return false;
        }

        try
        {
            var status = ReadValidatedStatus(_selectedProcess);
            if (!string.Equals(status.Session, _activeSessionToken, StringComparison.Ordinal))
                throw new InvalidOperationException("Module đã khởi động lại; kết nối lại để xác nhận phiên mới.");
            if (status.AppliedRevision > _activeRevision)
                throw new InvalidOperationException("Revision đã bị bảng điều khiển khác thay đổi; cần kết nối lại.");

            bool acknowledged = status.AppliedRevision == _activeRevision && status.Connected;
            if (acknowledged)
            {
                _pendingAckWatch.Reset();
                _lastConfirmedRevision = status.AppliedRevision;
                _lastConfirmedStatus = status;
            }
            else
            {
                if (!_pendingAckWatch.IsRunning) _pendingAckWatch.Start();
                if (_pendingAckWatch.ElapsedMilliseconds >= 5000)
                {
                    throw new TimeoutException(
                        $"Main thread còn heartbeat nhưng chưa xác nhận revision #{_activeRevision} sau 5 giây. {status.CommandError} {status.TransportError}");
                }
            }

            WriteCommandEnvelope(active: true);
            UpdateBannerFromStatus(_selectedProcess, status, acknowledged);

            liveStatus = status;
            statusMessage = acknowledged
                ? $"Đã xác nhận cấu hình #{_activeRevision}"
                : $"Chờ xác nhận cấu hình #{_activeRevision}";
            return true;
        }
        catch (Exception ex)
        {
            Disconnect(out _);
            ConnectionBanner = "Mất kết nối — " + ex.Message;
            statusMessage = "Module tự hết hạn lệnh trong tối đa 5 giây nếu bảng điều khiển không gửi tiếp.";
            _logger.Record("RUNTIME_TICK_DISCONNECT", ex.Message);
            return false;
        }
    }

    private void UpdateBannerFromStatus(RuntimeProcessOption proc, RuntimeSessionStatus status, bool acknowledged)
    {
        string ackText = acknowledged
            ? $"Đã xác nhận cấu hình #{_activeRevision}"
            : $"Chờ xác nhận cấu hình #{_activeRevision}";
        string envLabel = proc.IsAuthorizedTestRuntime
            ? "MÔI TRƯỜNG RUNTIME THỬ NGHIỆM ĐỘC LẬP"
            : "TIẾN TRÌNH GAME UNITY";
        string modVer = string.IsNullOrWhiteSpace(status.ModuleVersion)
            ? string.Empty
            : $" · Module {status.ModuleVersion}";

        ConnectionBanner =
            $"PID {proc.ProcessId} ({proc.ProcessName}) · {status.UnityVersion}{modVer} · {ackText}\r\n" +
            $"{status.State} · {status.PlayerCount} thực thể · {envLabel}";
    }

    private RuntimeSessionStatus ReadValidatedStatus(RuntimeProcessOption process)
    {
        if (!IsProcessAlive(process))
            throw new InvalidOperationException("Tiến trình đã thoát hoặc thay đổi.");

        string statusPath = RuntimeSessionPaths.StatusFile(process.ProcessId);
        if (!File.Exists(statusPath))
        {
            throw new InvalidDataException(
                $"Chưa có tệp trạng thái cho PID {process.ProcessId} ({statusPath}). Runtime chưa khởi tạo xong hoặc chưa nạp module.");
        }

        var status = _statusStore.Load(statusPath);
        if (status.Protocol != NextWireProtocol.CurrentVersion)
            throw new InvalidDataException("Giao thức module khác bản controller. Cập nhật đồng bộ controller và runtime.");

        long nowTicks = _timeProvider.UtcNow.UtcTicks;
        if (status.ProcessId != process.ProcessId ||
            status.ProcessStartUtcTicks != process.StartUtcTicks ||
            !status.IsFresh(nowTicks) ||
            !Guid.TryParseExact(status.Session, "N", out _))
        {
            throw new InvalidDataException("Heartbeat module thiếu, cũ hoặc không khớp PID.");
        }

        if (!string.IsNullOrEmpty(status.Error))
            throw new InvalidOperationException(status.Error);

        return status;
    }

    private void WriteCommandEnvelope(bool active)
    {
        if (_selectedProcess is null) return;
        string cmdPath = RuntimeSessionPaths.CommandFile(_selectedProcess.ProcessId);
        var envelope = new SessionCommandEnvelope
        {
            ProtocolVersion = NextWireProtocol.CurrentVersion,
            SessionToken = _activeSessionToken,
            Revision = _activeRevision,
            ExpiresUtcTicks = _timeProvider.UtcNow.AddSeconds(5).UtcTicks,
            Connected = active,
            Profile = _appliedProfile.Clone()
        };
        _commandStore.Save(cmdPath, envelope);
    }

    public bool TryLoadProfile(string? customPath, out string statusMessage)
    {
        string targetPath = string.IsNullOrWhiteSpace(customPath) ? _defaultProfilePath : Path.GetFullPath(customPath);
        try
        {
            if (!File.Exists(targetPath))
            {
                if (string.IsNullOrWhiteSpace(customPath))
                {
                    CurrentProfile = new EngineProfile();
                    statusMessage = "Cấu hình chỉ có tác dụng sau khi module xác nhận nhận lệnh.";
                    return true;
                }

                statusMessage = $"Không tìm thấy tệp cấu hình: {targetPath}";
                return false;
            }

            var loaded = _profileStore.Load(targetPath);
            var validationErrors = loaded.Validate();
            if (validationErrors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, validationErrors));

            CurrentProfile = loaded;
            _profilePath = targetPath;
            statusMessage = string.IsNullOrWhiteSpace(customPath)
                ? "Đã đọc cấu hình mặc định; chưa áp dụng."
                : "Đã đọc cấu hình; bấm Áp dụng khi có kết nối module.";
            _logger.Record("UI_PROFILE_LOAD", statusMessage);
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = string.IsNullOrWhiteSpace(customPath)
                ? "Không đọc cấu hình mặc định: " + ex.Message
                : "Không đọc được cấu hình: " + ex.Message;
            _logger.Record("UI_PROFILE_ERROR", statusMessage);
            return false;
        }
    }

    public bool TrySaveProfile(string? customPath, out string statusMessage)
    {
        string targetPath = string.IsNullOrWhiteSpace(customPath) ? _defaultProfilePath : Path.GetFullPath(customPath);
        var validationErrors = CurrentProfile.Validate();
        if (validationErrors.Count > 0)
        {
            statusMessage = "Cấu hình không hợp lệ: " + string.Join("; ", validationErrors);
            return false;
        }

        try
        {
            var snapshot = CurrentProfile.Clone();
            _profileStore.Save(targetPath, snapshot);
            if (!string.Equals(targetPath, _defaultProfilePath, StringComparison.OrdinalIgnoreCase))
            {
                _profileStore.Save(_defaultProfilePath, snapshot);
            }

            _profilePath = targetPath;
            statusMessage = "Đã lưu cấu hình; không tự áp dụng thay đổi vào runtime.";
            _logger.Record("UI_PROFILE_SAVE", statusMessage);
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = $"Lỗi lưu cấu hình: {ex.Message}";
            _logger.Record("UI_PROFILE_ERROR", statusMessage);
            return false;
        }
    }

    public void Dispose()
    {
        Disconnect(out _);
        if (!KeepSpawnedRuntimeOnDispose)
            StopSpawnedTestRuntime();
    }
}
