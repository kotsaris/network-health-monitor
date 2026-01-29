using System.Runtime.InteropServices;

namespace NetworkHealthMonitor;

static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    private delegate bool ConsoleCtrlDelegate(int sig);

    private static bool ConsoleCtrlHandler(int sig)
    {
        // Ctrl+C = 0, Ctrl+Break = 1, Close = 2
        Environment.Exit(0);
        return true;
    }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Handle Ctrl+C from console
        SetConsoleCtrlHandler(ConsoleCtrlHandler, true);

        // Single instance check
        using var mutex = new Mutex(true, "NetworkHealthMonitor_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Network Health Monitor is already running.", "Already Running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new WidgetForm());
    }
}
