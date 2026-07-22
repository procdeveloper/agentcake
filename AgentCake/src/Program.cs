using System.Threading;

namespace AgentCake;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        _singleInstance = new Mutex(true, "AgentCake.LiveUsage", out bool isNew);
        if (!isNew) return;

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => CrashLog.Write("WinForms UI exception", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                CrashLog.Write("Unhandled application exception", exception);
        };
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
        GC.KeepAlive(_singleInstance);
    }
}
