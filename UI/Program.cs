using System.Globalization;
using System.Windows.Forms;
using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;
using HackTK.Next.Core;
using HackTK.Next.Diagnostics;
using HackTK.Next.Simulation;

namespace HackTK.Next.UI;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();

        if (!EngineModuleLoader.VerifyAndLoadAssemblies(out _, out var missingAssemblies))
        {
            string missingMsg = "Thiếu assembly bắt buộc khi khởi động: " + string.Join(", ", missingAssemblies);
            RecordUnhandledFault("ASSEMBLY_LOAD_ERROR", new FileNotFoundException(missingMsg));
            MessageBox.Show(missingMsg, "HackTK Rebuild — lỗi nạp module", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        Application.ThreadException += (_, e) =>
        {
            RecordUnhandledFault("UI_THREAD_EXCEPTION", e.Exception);
            MessageBox.Show(e.Exception.Message, "HackTK Rebuild — lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                RecordUnhandledFault("APPDOMAIN_UNHANDLED_EXCEPTION", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            RecordUnhandledFault("UNOBSERVED_TASK_EXCEPTION", e.Exception);
            e.SetObserved();
        };

        if (args.Length >= 2 && string.Equals(args[0], "--smoke-test", StringComparison.OrdinalIgnoreCase))
        {
            string targetDir = Path.GetFullPath(args[1]);
            try
            {
                bool ok = EngineDashboardForm.RunSmokeVerification(targetDir, out string details);
                Console.WriteLine(details);
                if (!ok)
                    File.WriteAllText(targetDir + ".error.txt", details);
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                RecordUnhandledFault("SMOKE_TEST_EXCEPTION", ex);
                File.WriteAllText(targetDir + ".error.txt", ex.ToString());
                return 1;
            }
        }

        using var mutex = new Mutex(initiallyOwned: false, "Local\\HackTKRebuild.Controller");
        bool ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(0, exitContext: false);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                MessageBox.Show("Bảng điều khiển đã mở trong phiên Windows này.", "HackTK Rebuild", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }

            string defaultProfilePath = Path.Combine(
                RuntimeSessionPaths.RootDirectory,
                "profiles",
                "default.xml");
            string bundledProfilePath = Path.Combine(AppContext.BaseDirectory, "profiles", "default.xml");
            if (!File.Exists(defaultProfilePath) && File.Exists(bundledProfilePath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(defaultProfilePath)!);
                    File.Copy(bundledProfilePath, defaultProfilePath, overwrite: false);
                }
                catch { }
            }

            using var container = new ServiceContainer();
            EngineModuleLoader.RegisterRuntimeServices(container);
            container.RegisterSingleton<DashboardPresenter>(r =>
                new DashboardPresenter(
                    r.Resolve<IConfigurationStore<EngineProfile>>(),
                    r.Resolve<IConfigurationStore<SessionCommandEnvelope>>(),
                    r.Resolve<IConfigurationStore<RuntimeSessionStatus>>(),
                    r.Resolve<ITimeProvider>(),
                    r.Resolve<IDiagnosticLogger>(),
                    defaultProfilePath));

            using var form = new EngineDashboardForm(container.Resolve<DashboardPresenter>());
            Application.Run(form);
            return 0;
        }
        finally
        {
            if (ownsMutex)
            {
                try { mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
        }
    }

    public static void RecordUnhandledFault(string category, Exception exception)
    {
        try
        {
            string dir = Path.Combine(RuntimeSessionPaths.RootDirectory, "diagnostics");
            Directory.CreateDirectory(dir);
            string logPath = Path.Combine(dir, "controller-errors.log");
            string entry = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O}\t{1}\t{2}: {3}{4}",
                DateTimeOffset.UtcNow,
                category,
                exception.GetType().FullName,
                exception.Message.Replace('\r', ' ').Replace('\n', ' '),
                Environment.NewLine);
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Never throw from last-chance fault logging.
        }
    }
}
