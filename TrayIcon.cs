using System.Drawing.Drawing2D;

namespace NetworkHealthMonitor;

public class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly NetworkMonitor _monitor;
    private readonly ContextMenuStrip _contextMenu;
    private DetailsForm? _detailsForm;

    private readonly Icon _greenIcon;
    private readonly Icon _yellowIcon;
    private readonly Icon _redIcon;

    public TrayIcon()
    {
        _greenIcon = CreateColoredIcon(Color.FromArgb(0, 200, 0));
        _yellowIcon = CreateColoredIcon(Color.FromArgb(255, 200, 0));
        _redIcon = CreateColoredIcon(Color.FromArgb(220, 50, 50));

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Show Details", null, OnShowDetails);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, OnExit);

        _notifyIcon = new NotifyIcon
        {
            Icon = _redIcon,
            Visible = true,
            Text = "Network Health Monitor\nStarting...",
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += OnShowDetails;

        _monitor = new NetworkMonitor();
        _monitor.HealthStatusChanged += OnHealthStatusChanged;
        _monitor.StatsUpdated += OnStatsUpdated;
        _monitor.Start();
    }

    private static Icon CreateColoredIcon(Color color)
    {
        const int size = 16;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(color);
        using var pen = new Pen(Color.FromArgb(60, 0, 0, 0), 1);

        g.FillEllipse(brush, 1, 1, size - 3, size - 3);
        g.DrawEllipse(pen, 1, 1, size - 3, size - 3);

        // Add highlight for 3D effect
        using var highlightBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
        g.FillEllipse(highlightBrush, 3, 2, 6, 4);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void OnHealthStatusChanged(object? sender, HealthStatus status)
    {
        _notifyIcon.Icon = status switch
        {
            HealthStatus.Healthy => _greenIcon,
            HealthStatus.Degraded => _yellowIcon,
            HealthStatus.Poor => _redIcon,
            _ => _redIcon
        };
    }

    private void OnStatsUpdated(object? sender, EventArgs e)
    {
        var tooltip = _monitor.GetTooltipText();
        // NotifyIcon.Text has 128 char limit
        if (tooltip.Length > 127)
            tooltip = tooltip.Substring(0, 124) + "...";
        _notifyIcon.Text = tooltip;

        _detailsForm?.UpdateStats(_monitor.GetStats());
    }

    private void OnShowDetails(object? sender, EventArgs e)
    {
        if (_detailsForm == null || _detailsForm.IsDisposed)
        {
            _detailsForm = new DetailsForm(_monitor);
        }

        if (!_detailsForm.Visible)
        {
            _detailsForm.Show();
        }

        _detailsForm.Activate();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _monitor.Stop();
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    public void Dispose()
    {
        _monitor.Dispose();
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _detailsForm?.Dispose();
        _greenIcon.Dispose();
        _yellowIcon.Dispose();
        _redIcon.Dispose();
    }
}
