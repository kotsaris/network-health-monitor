using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NetworkHealthMonitor;

public class WidgetForm : Form
{
    private readonly NetworkMonitor _monitor;
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _positionTimer;

    private Label _latencyLabel = null!;
    private Label _lossLabel = null!;
    private Panel _statusIndicator = null!;

    private bool _isDragging;
    private Point _dragStart;

    // For taskbar detection
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public WidgetForm()
    {
        _monitor = new NetworkMonitor();
        _monitor.HealthStatusChanged += OnHealthStatusChanged;
        _monitor.StatsUpdated += OnStatsUpdated;

        InitializeWidget();
        InitializeTrayIcon();

        // Timer to keep widget positioned near taskbar
        _positionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _positionTimer.Tick += (s, e) => EnsureVisibleOnScreen();

        _trayIcon = new NotifyIcon
        {
            Icon = CreateStatusIcon(Color.Gray),
            Visible = true,
            Text = "Network Health Monitor"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show Details", null, (s, e) => ShowDetailsForm());
        menu.Items.Add("Snap to Taskbar", null, (s, e) => SnapToTaskbar());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (s, e) => ShowDetailsForm();

        SnapToTaskbar();
        _monitor.Start();
    }

    private void InitializeWidget()
    {
        // Enable DPI scaling
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        // Borderless, tool window (doesn't show in taskbar)
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(120, 40);
        BackColor = Color.FromArgb(40, 40, 40);

        // Rounded corners
        Region = CreateRoundedRegion(Width, Height, 8);

        // Status indicator (colored circle)
        _statusIndicator = new Panel
        {
            Size = new Size(12, 12),
            Location = new Point(8, 14),
            BackColor = Color.Gray
        };
        _statusIndicator.Paint += PaintStatusIndicator;
        Controls.Add(_statusIndicator);

        // Latency label
        _latencyLabel = new Label
        {
            Text = "-- ms",
            Location = new Point(26, 5),
            Size = new Size(60, 16),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        Controls.Add(_latencyLabel);

        // Loss label
        _lossLabel = new Label
        {
            Text = "0% loss",
            Location = new Point(26, 21),
            Size = new Size(60, 14),
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 7.5F),
            BackColor = Color.Transparent
        };
        Controls.Add(_lossLabel);

        // Close button
        var closeBtn = new Label
        {
            Text = "×",
            Location = new Point(100, 2),
            Size = new Size(16, 16),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 10F),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter
        };
        closeBtn.Click += (s, e) => Hide();
        closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Color.White;
        closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Color.Gray;
        Controls.Add(closeBtn);

        // Dragging support
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        // Double-click to show details (on form and all child controls)
        DoubleClick += (s, e) => ShowDetailsForm();
        _statusIndicator.DoubleClick += (s, e) => ShowDetailsForm();
        _latencyLabel.DoubleClick += (s, e) => ShowDetailsForm();
        _lossLabel.DoubleClick += (s, e) => ShowDetailsForm();

        // Right-click menu
        var widgetMenu = new ContextMenuStrip();
        widgetMenu.Items.Add("Show Details", null, (s, e) => ShowDetailsForm());
        widgetMenu.Items.Add("Snap to Taskbar", null, (s, e) => SnapToTaskbar());
        widgetMenu.Items.Add(new ToolStripSeparator());
        widgetMenu.Items.Add("Hide Widget", null, (s, e) => Hide());
        widgetMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
        ContextMenuStrip = widgetMenu;
    }

    private void InitializeTrayIcon()
    {
        // Tray icon already set up in constructor
    }

    private static Region CreateRoundedRegion(int width, int height, int radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
        path.AddArc(width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
        path.AddArc(width - radius * 2, height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(0, height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseAllFigures();
        return new Region(path);
    }

    private void PaintStatusIndicator(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_statusIndicator.BackColor);
        e.Graphics.FillEllipse(brush, 0, 0, _statusIndicator.Width - 1, _statusIndicator.Height - 1);
    }

    private void OnHealthStatusChanged(object? sender, HealthStatus status)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnHealthStatusChanged(sender, status));
            return;
        }

        var color = status switch
        {
            HealthStatus.Healthy => Color.FromArgb(0, 200, 0),
            HealthStatus.Degraded => Color.FromArgb(255, 200, 0),
            HealthStatus.Poor => Color.FromArgb(220, 50, 50),
            _ => Color.Gray
        };

        _statusIndicator.BackColor = color;
        _statusIndicator.Invalidate();

        _trayIcon.Icon = CreateStatusIcon(color);
    }

    private void OnStatsUpdated(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnStatsUpdated(sender, e));
            return;
        }

        var stats = _monitor.GetStats();

        // Get best latency from all targets
        var latencies = stats.Values
            .Where(s => s.CurrentLatency.HasValue)
            .Select(s => s.CurrentLatency!.Value)
            .ToList();

        if (latencies.Count > 0)
        {
            var avgLatency = latencies.Average();
            _latencyLabel.Text = $"{avgLatency:F0} ms";
            _latencyLabel.ForeColor = avgLatency < 100 ? Color.LightGreen :
                                      avgLatency <= 200 ? Color.Yellow : Color.Red;
        }
        else
        {
            _latencyLabel.Text = "Timeout";
            _latencyLabel.ForeColor = Color.Red;
        }

        var avgLoss = stats.Values.Average(s => s.PacketLossPercent);
        _lossLabel.Text = $"{avgLoss:F1}% loss";
        _lossLabel.ForeColor = avgLoss > 5 ? Color.Red :
                               avgLoss > 0 ? Color.Yellow : Color.LightGray;

        // Update tooltip
        _trayIcon.Text = _monitor.GetTooltipText();

        // Update details form if visible
        if (_detailsForm != null && !_detailsForm.IsDisposed && _detailsForm.Visible)
        {
            _detailsForm.UpdateStats(stats);
        }
    }

    private void SnapToTaskbar()
    {
        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out RECT taskbarRect))
        {
            var screen = Screen.PrimaryScreen!;
            var workingArea = screen.WorkingArea;

            // Determine taskbar position
            if (taskbarRect.Top > workingArea.Bottom - 10)
            {
                // Taskbar at bottom
                Location = new Point(workingArea.Right - Width - 10, workingArea.Bottom - Height - 10);
            }
            else if (taskbarRect.Left < workingArea.Left + 10)
            {
                // Taskbar at left
                Location = new Point(workingArea.Left + 10, workingArea.Bottom - Height - 10);
            }
            else if (taskbarRect.Top < workingArea.Top + 10)
            {
                // Taskbar at top
                Location = new Point(workingArea.Right - Width - 10, workingArea.Top + 10);
            }
            else
            {
                // Taskbar at right
                Location = new Point(workingArea.Right - Width - 10, workingArea.Bottom - Height - 10);
            }
        }
        else
        {
            // Fallback: bottom right
            var screen = Screen.PrimaryScreen!;
            Location = new Point(screen.WorkingArea.Right - Width - 10,
                                 screen.WorkingArea.Bottom - Height - 10);
        }

        Show();
        SendToDesktopLayer();
    }

    private void EnsureVisibleOnScreen()
    {
        if (!Visible) return;

        var screen = Screen.FromPoint(Location);
        var bounds = screen.WorkingArea;

        if (Left < bounds.Left) Left = bounds.Left + 5;
        if (Top < bounds.Top) Top = bounds.Top + 5;
        if (Right > bounds.Right) Left = bounds.Right - Width - 5;
        if (Bottom > bounds.Bottom) Top = bounds.Bottom - Height - 5;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _dragStart = e.Location;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Location = new Point(Left + e.X - _dragStart.X, Top + e.Y - _dragStart.Y);
            Cursor = Cursors.SizeAll;
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        Cursor = Cursors.Default;
        SendToDesktopLayer();
    }

    private DetailsForm? _detailsForm;

    private void ShowDetailsForm()
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

    private static Icon CreateStatusIcon(Color color)
    {
        const int size = 16;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, size - 3, size - 3);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void ExitApplication()
    {
        _monitor.Stop();
        _monitor.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _positionTimer.Stop();
        _positionTimer.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            ExitApplication();
        }
        base.OnFormClosing(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            // Make it a tool window (no taskbar entry) and enable transparency
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Update region after DPI scaling has been applied
        Region = CreateRoundedRegion(Width, Height, (int)(8 * DeviceDpi / 96.0));
        SendToDesktopLayer();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Recreate rounded region for new DPI
        Region = CreateRoundedRegion(Width, Height, (int)(8 * e.DeviceDpiNew / 96.0));
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            SendToDesktopLayer();
        }
    }

    private void SendToDesktopLayer()
    {
        // Send window to bottom of z-order (above desktop, below other windows)
        SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
