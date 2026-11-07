using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HackTK.Next.Simulation;

namespace HackTK.Next.Runtime;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (!EngineModuleLoader.VerifyAndLoadAssemblies(out var loaded, out var failed))
        {
            Console.Error.WriteLine("[RUNTIME_ERROR] Thiếu assembly bắt buộc: " + string.Join(", ", failed));
            return 2;
        }

        bool selfCheckOnly = false;
        int durationMs = 0;
        int? parentPid = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--self-check", StringComparison.OrdinalIgnoreCase))
            {
                selfCheckOnly = true;
            }
            else if (string.Equals(args[i], "--duration-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    durationMs = parsed;
                i++;
            }
            else if (string.Equals(args[i], "--parent-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pPid))
                    parentPid = pPid;
                i++;
            }
        }

        using var host = new AuthorizedTestRuntimeHost();

        if (selfCheckOnly)
        {
            var status = host.StepTick();
            Console.WriteLine($"[RUNTIME_SELF_CHECK_OK] PID={host.ProcessId} Session={host.SessionToken} Assemblies={loaded.Count} Modules={host.ModuleManager.Modules.Count} StatusFile={host.StatusFilePath}");
            return 0;
        }

        string stopSignalPath = Path.Combine(host.SessionDirectory, "stop.signal");
        if (File.Exists(stopSignalPath))
        {
            try { File.Delete(stopSignalPath); } catch { }
        }

        Console.WriteLine($"[RUNTIME_STARTED] PID={host.ProcessId} Session={host.SessionToken} StatusFile={host.StatusFilePath}");
        Console.Out.Flush();

        var watch = Stopwatch.StartNew();
        while (true)
        {
            host.StepTick();

            if (File.Exists(stopSignalPath))
            {
                try { File.Delete(stopSignalPath); } catch { }
                break;
            }

            if (durationMs > 0 && watch.ElapsedMilliseconds >= durationMs)
                break;

            if (parentPid.HasValue && !IsProcessRunning(parentPid.Value))
                break;

            Thread.Sleep(30);
        }

        WriteSummaryFile(host);
        Console.WriteLine($"[RUNTIME_STOPPED] PID={host.ProcessId} Ticks={host.TickCount} AppliedRevision={host.EngineHost.AppliedRevision} Compensated={host.SpreadModule.CompensatedCount}");
        return 0;
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteSummaryFile(AuthorizedTestRuntimeHost host)
    {
        try
        {
            string summaryPath = Path.Combine(host.SessionDirectory, "runtime-summary.json");
            var payload = new
            {
                host.ProcessId,
                host.SessionToken,
                host.TickCount,
                AppliedRevision = host.EngineHost.AppliedRevision,
                IsConnected = host.EngineHost.IsConnected,
                HostState = host.EngineHost.HostState,
                CompensatedShots = host.SpreadModule.CompensatedCount,
                SkippedShots = host.SpreadModule.SkippedCount,
                InvalidWeaponCount = host.SpreadModule.InvalidWeaponDataCount,
                LastQuantizedErrorDegrees = host.SpreadModule.LastQuantizedErrorDegrees,
                CompensatedByWeaponGroup = host.CompensatedShotsByWeaponGroup,
                CompensatedByMovementState = host.CompensatedShotsByMovementState.ToDictionary(k => k.Key.ToString(), v => v.Value),
                MouseStepsDispatched = host.InputDispatcher.DispatchedMouseMoves.Count,
                OverlayDrawCalls = host.TotalOverlayDrawCalls,
                ActiveMaterialReplacements = host.MaterialController.ActiveReplacementCount,
                JumpPulsesDispatched = host.InputDispatcher.DispatchedJumpStates.Count
            };

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(summaryPath, json, Encoding.UTF8);
        }
        catch
        {
            // Ignore summary write errors on shutdown.
        }
    }
}
