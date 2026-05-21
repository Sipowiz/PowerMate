using Serilog;

namespace PowerMate.WinUI;

public static class Program
{
    [global::System.STAThread]
    static void Main(string[] args)
    {
        MauiProgram.ConfigureLogging();
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";
        Log.Information("PowerMate {Version} starting", version);
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
