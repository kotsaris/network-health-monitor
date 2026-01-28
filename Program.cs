namespace NetworkHealthMonitor;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Single instance check
        using var mutex = new Mutex(true, "NetworkHealthMonitor_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("Network Health Monitor is already running.", "Already Running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Handle Ctrl+C from console
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Application.Exit();
        };

        Application.Run(new WidgetForm());
    }
}
