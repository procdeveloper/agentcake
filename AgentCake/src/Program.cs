using System.Threading;

namespace AgentCake;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main()
    {
        // Prevent two copies fighting over the same tray icon.
        _singleInstance = new Mutex(initiallyOwned: true, "AgentCake.SingleInstance", out bool isNew);
        if (!isNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());

        GC.KeepAlive(_singleInstance);
    }
}
