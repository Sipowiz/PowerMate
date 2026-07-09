using PowerMate.Services;
using Serilog;

namespace PowerMate.WinUI;

public static class Program
{
    // Held for the process lifetime; if it were collected a second instance could start.
    private static Mutex? _instanceMutex;

    [global::System.STAThread]
    static void Main(string[] args)
    {
        MauiProgram.ConfigureLogging();
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";
        Log.Information("PowerMate {Version} starting", version);

        // Two instances would both open the HID device and process every knob
        // event twice. Scoped to the session so fast user switching still works.
        _instanceMutex = new Mutex(true, @"Local\PowerMateDriver.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Log.Information("PowerMate is already running; exiting this instance");
            Log.CloseAndFlush();
            return;
        }

        try { StartupService.MigrateLegacyStartup(); }
        catch (Exception ex) { Log.Warning(ex, "[{Source}]", "StartupMigration"); }

        try
        {
            global::WinRT.ComWrappersSupport.InitializeComWrappers();
            global::Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "[{Source}]", "ApplicationStartup");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
