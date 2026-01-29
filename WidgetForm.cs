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
    private Label _closeBtn = null!;

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

    // Base dimensions at 96 DPI (100% scaling)
    private const int BaseWidth = 150;
    private const int BaseHeight = 58;
    private const int BaseIndicatorSize = 18;
    private const int BaseCornerRadius = 10;

    public WidgetForm()
    {
        _monitor = new NetworkMonitor();
        _monitor.HealthStatusChanged += OnHealthStatusChanged;
        _monitor.StatsUpdated += OnStatsUpdated;

        InitializeWidget();

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

        // Timer to keep widget positioned near taskbar
        _positionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _positionTimer.Tick += (s, e) => EnsureVisibleOnScreen();

        SnapToTaskbar();
        _monitor.Start();
    }

    private float ScaleFactor => DeviceDpi / 96f;
    private int Scale(int value) => (int)(value * ScaleFactor);

    private void InitializeWidget()
    {
        SuspendLayout();

        // Borderless, tool window (doesn't show in taskbar)
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(40, 40, 40);

        ApplyScaledLayout();

        // Dragging support
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        // Right-click menu
        var widgetMenu = new ContextMenuStrip();
        widgetMenu.Items.Add("Show Details", null, (s, e) => ShowDetailsForm());
        widgetMenu.Items.Add("Snap to Taskbar", null, (s, e) => SnapToTaskbar());
        widgetMenu.Items.Add(new ToolStripSeparator());
        widgetMenu.Items.Add("Hide Widget", null, (s, e) => Hide());
        widgetMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
        ContextMenuStrip = widgetMenu;

        ResumeLayout(true);
    }

    private void ApplyScaledLayout()
    {
        // Scale form size
        Size = new Size(Scale(BaseWidth), Scale(BaseHeight));
        Region = CreateRoundedRegion(Width, Height, Scale(BaseCornerRadius));

        // Clear existing controls if rebuilding
        Controls.Clear();

        // Status indicator (colored circle)
        int indicatorSize = Scale(BaseIndicatorSize);
        _statusIndicator = new Panel
        {
            Size = new Size(indicatorSize, indicatorSize),
            Location = new Point(Scale(12), Scale(20)),
            BackColor = Color.Gray
        };
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(0, 0, indicatorSize, indicatorSize);
            _statusIndicator.Region = new Region(path);
        }
        _statusIndicator.DoubleClick += (s, e) => ShowDetailsForm();
        Controls.Add(_statusIndicator);

        // Latency label - don't scale font, Windows handles that
        _latencyLabel = new Label
        {
            Text = "-- ms",
            Location = new Point(Scale(36), Scale(8)),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        _latencyLabel.DoubleClick += (s, e) => ShowDetailsForm();
        Controls.Add(_latencyLabel);

        // Loss label
        _lossLabel = new Label
        {
            Text = "0.0% loss",
            Location = new Point(Scale(36), Scale(32)),
            AutoSize = true,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 8.5F),
            BackColor = Color.Transparent
        };
        _lossLabel.DoubleClick += (s, e) => ShowDetailsForm();
        Controls.Add(_lossLabel);

        // Close button
        _closeBtn = new Label
        {
            Text = "\u00d7",
            Location = new Point(Scale(BaseWidth - 24), Scale(4)),
            Size = new Size(Scale(20), Scale(20)),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 10F),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _closeBtn.Click += (s, e) => Hide();
        _closeBtn.MouseEnter += (s, e) => _closeBtn.ForeColor = Color.White;
        _closeBtn.MouseLeave += (s, e) => _closeBtn.ForeColor = Color.Gray;
        Controls.Add(_closeBtn);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        SuspendLayout();
        ApplyScaledLayout();
        ResumeLayout(true);

        // Reposition after DPI change
        SnapToTaskbar();
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
        if (_trayIcon != null)
        {
            _trayIcon.Icon = CreateStatusIcon(color);
        }
    }

    private void OnStatsUpdated(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => OnStatsUpdated(sender, e));
            return;
        }

        var stats = _monitor.GetStats();

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

        if (stats.Values.Any())
        {
            var avgLoss = stats.Values.Average(s => s.PacketLossPercent);
            _lossLabel.Text = $"{avgLoss:F1}% loss";
            _lossLabel.ForeColor = avgLoss > 5 ? Color.Red :
                                   avgLoss > 0 ? Color.Yellow : Color.LightGray;
        }

        if (_trayIcon != null)
        {
            _trayIcon.Text = _monitor.GetTooltipText();
        }

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

            if (taskbarRect.Top > workingArea.Bottom - 10)
            {
                Location = new Point(workingArea.Right - Width - 10, workingArea.Bottom - Height - 10);
            }
            else if (taskbarRect.Left < workingArea.Left + 10)
            {
                Location = new Point(workingArea.Left + 10, workingArea.Bottom - Height - 10);
            }
            else if (taskbarRect.Top < workingArea.Top + 10)
            {
                Location = new Point(workingArea.Right - Width - 10, workingArea.Top + 10);
            }
            else
            {
                Location = new Point(workingArea.Right - Width - 10, workingArea.Bottom - Height - 10);
            }
        }
        else
        {
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
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SendToDesktopLayer();
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
        SetWindowPos(Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
