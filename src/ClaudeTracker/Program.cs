namespace ClaudeTracker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Wait briefly for the mutex so a self-update restart can begin while the old instance exits.
        using var mutex = new Mutex(false, "ClaudeTrackerSingleInstance");
        bool owned = false;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(6), false);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        if (!owned) return;

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UpdateManager.CleanupAfterRestart();
            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
