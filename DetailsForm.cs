namespace NetworkHealthMonitor;

public partial class DetailsForm : Form
{
    private readonly NetworkMonitor _monitor;
    private readonly Dictionary<string, Label> _currentLatencyLabels = new();
    private readonly Dictionary<string, Label> _avgLatencyLabels = new();
    private readonly Dictionary<string, Label> _minMaxLabels = new();
    private readonly Dictionary<string, Label> _packetLossLabels = new();
    private readonly Dictionary<string, Panel> _graphPanels = new();

    private Label _statusLabel = null!;
    private Panel _statusIndicator = null!;

    public DetailsForm(NetworkMonitor monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        UpdateStats(_monitor.GetStats());
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Network Health Monitor";
        Size = new Size(420, 480);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        // Status header
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.FromArgb(45, 45, 45)
        };

        _statusIndicator = new Panel
        {
            Size = new Size(16, 16),
            Location = new Point(15, 17),
            BackColor = Color.Red
        };
        _statusIndicator.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_statusIndicator.BackColor);
            e.Graphics.FillEllipse(brush, 0, 0, 15, 15);
        };

        _statusLabel = new Label
        {
            Text = "Checking...",
            Location = new Point(40, 15),
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.White
        };

        headerPanel.Controls.Add(_statusIndicator);
        headerPanel.Controls.Add(_statusLabel);
        Controls.Add(headerPanel);

        // Content panel
        var contentPanel = new Panel
        {
            Location = new Point(0, 50),
            Size = new Size(420, 400),
            AutoScroll = true,
            Padding = new Padding(10)
        };

        int yOffset = 10;
        foreach (var target in _monitor.GetTargets())
        {
            var targetPanel = CreateTargetPanel(target, yOffset);
            contentPanel.Controls.Add(targetPanel);
            yOffset += 175;
        }

        Controls.Add(contentPanel);

        ResumeLayout(false);
    }

    private Panel CreateTargetPanel(string target, int yOffset)
    {
        var panel = new Panel
        {
            Location = new Point(10, yOffset),
            Size = new Size(380, 165),
            BackColor = Color.FromArgb(45, 45, 45)
        };

        // Target header
        var targetLabel = new Label
        {
            Text = target,
            Location = new Point(10, 8),
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255)
        };
        panel.Controls.Add(targetLabel);

        // Current latency
        var currentLabel = new Label
        {
            Text = "Current:",
            Location = new Point(10, 35),
            AutoSize = true,
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(currentLabel);

        var currentValueLabel = new Label
        {
            Text = "-- ms",
            Location = new Point(100, 35),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        panel.Controls.Add(currentValueLabel);
        _currentLatencyLabels[target] = currentValueLabel;

        // Average latency
        var avgLabel = new Label
        {
            Text = "Average:",
            Location = new Point(10, 55),
            AutoSize = true,
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(avgLabel);

        var avgValueLabel = new Label
        {
            Text = "-- ms",
            Location = new Point(100, 55),
            AutoSize = true,
            ForeColor = Color.White
        };
        panel.Controls.Add(avgValueLabel);
        _avgLatencyLabels[target] = avgValueLabel;

        // Min/Max
        var minMaxLabel = new Label
        {
            Text = "Min/Max:",
            Location = new Point(180, 35),
            AutoSize = true,
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(minMaxLabel);

        var minMaxValueLabel = new Label
        {
            Text = "-- / -- ms",
            Location = new Point(250, 35),
            AutoSize = true,
            ForeColor = Color.White
        };
        panel.Controls.Add(minMaxValueLabel);
        _minMaxLabels[target] = minMaxValueLabel;

        // Packet loss
        var lossLabel = new Label
        {
            Text = "Packet Loss:",
            Location = new Point(180, 55),
            AutoSize = true,
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(lossLabel);

        var lossValueLabel = new Label
        {
            Text = "0.0%",
            Location = new Point(270, 55),
            AutoSize = true,
            ForeColor = Color.White
        };
        panel.Controls.Add(lossValueLabel);
        _packetLossLabels[target] = lossValueLabel;

        // Graph panel
        var graphLabel = new Label
        {
            Text = "Latency History (last 60 pings)",
            Location = new Point(10, 80),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8F)
        };
        panel.Controls.Add(graphLabel);

        var graphPanel = new Panel
        {
            Location = new Point(10, 98),
            Size = new Size(360, 60),
            BackColor = Color.FromArgb(25, 25, 25),
            BorderStyle = BorderStyle.FixedSingle
        };
        graphPanel.Paint += (s, e) => DrawGraph(e.Graphics, target, graphPanel.Size);
        panel.Controls.Add(graphPanel);
        _graphPanels[target] = graphPanel;

        return panel;
    }

    private void DrawGraph(Graphics g, string target, Size size)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(25, 25, 25));

        var stats = _monitor.GetStats();
        if (!stats.TryGetValue(target, out var targetStats) || targetStats.History.Count < 2)
            return;

        var history = targetStats.History;
        var maxLatency = Math.Max(200, history.Where(h => h.Success && h.Latency.HasValue)
                                               .Select(h => h.Latency!.Value)
                                               .DefaultIfEmpty(100)
                                               .Max());

        // Draw threshold lines
        using var thresholdPen = new Pen(Color.FromArgb(60, 255, 255, 0), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        var y100 = size.Height - (int)(100.0 / maxLatency * (size.Height - 10)) - 5;
        var y200 = size.Height - (int)(200.0 / maxLatency * (size.Height - 10)) - 5;
        g.DrawLine(thresholdPen, 0, y100, size.Width, y100);
        using var redThresholdPen = new Pen(Color.FromArgb(60, 255, 0, 0), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        g.DrawLine(redThresholdPen, 0, y200, size.Width, y200);

        // Draw latency line
        var points = new List<PointF>();
        var xStep = (float)size.Width / 59;

        for (int i = 0; i < history.Count; i++)
        {
            var ping = history[i];
            if (ping.Success && ping.Latency.HasValue)
            {
                var x = i * xStep;
                var y = size.Height - (int)(ping.Latency.Value / (double)maxLatency * (size.Height - 10)) - 5;
                points.Add(new PointF(x, Math.Max(2, y)));
            }
        }

        if (points.Count >= 2)
        {
            using var linePen = new Pen(Color.FromArgb(100, 180, 255), 2);
            g.DrawLines(linePen, points.ToArray());

            // Draw points
            using var pointBrush = new SolidBrush(Color.FromArgb(100, 180, 255));
            foreach (var point in points)
            {
                g.FillEllipse(pointBrush, point.X - 2, point.Y - 2, 4, 4);
            }
        }

        // Draw failed pings as red marks
        for (int i = 0; i < history.Count; i++)
        {
            if (!history[i].Success)
            {
                var x = i * xStep;
                using var failBrush = new SolidBrush(Color.FromArgb(220, 50, 50));
                g.FillRectangle(failBrush, x - 2, 2, 4, size.Height - 4);
            }
        }
    }

    public void UpdateStats(IReadOnlyDictionary<string, TargetStats> stats)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStats(stats));
            return;
        }

        foreach (var (target, targetStats) in stats)
        {
            if (_currentLatencyLabels.TryGetValue(target, out var currentLabel))
            {
                currentLabel.Text = targetStats.CurrentLatency.HasValue
                    ? $"{targetStats.CurrentLatency} ms"
                    : "Timeout";
                currentLabel.ForeColor = targetStats.CurrentLatency.HasValue
                    ? GetLatencyColor(targetStats.CurrentLatency.Value)
                    : Color.Red;
            }

            if (_avgLatencyLabels.TryGetValue(target, out var avgLabel))
            {
                avgLabel.Text = targetStats.History.Any(h => h.Success)
                    ? $"{targetStats.AverageLatency:F1} ms"
                    : "-- ms";
            }

            if (_minMaxLabels.TryGetValue(target, out var minMaxLabel))
            {
                if (targetStats.MinLatency != long.MaxValue)
                {
                    minMaxLabel.Text = $"{targetStats.MinLatency} / {targetStats.MaxLatency} ms";
                }
            }

            if (_packetLossLabels.TryGetValue(target, out var lossLabel))
            {
                lossLabel.Text = $"{targetStats.PacketLossPercent:F1}%";
                lossLabel.ForeColor = targetStats.PacketLossPercent > 5 ? Color.Red :
                                      targetStats.PacketLossPercent > 0 ? Color.Yellow : Color.LightGreen;
            }

            if (_graphPanels.TryGetValue(target, out var graphPanel))
            {
                graphPanel.Invalidate();
            }
        }

        // Update overall status
        var status = _monitor.CurrentStatus;
        _statusLabel.Text = status switch
        {
            HealthStatus.Healthy => "Connection Healthy",
            HealthStatus.Degraded => "Connection Degraded",
            HealthStatus.Poor => "Connection Poor",
            _ => "Unknown"
        };

        _statusIndicator.BackColor = status switch
        {
            HealthStatus.Healthy => Color.FromArgb(0, 200, 0),
            HealthStatus.Degraded => Color.FromArgb(255, 200, 0),
            HealthStatus.Poor => Color.FromArgb(220, 50, 50),
            _ => Color.Gray
        };
        _statusIndicator.Invalidate();
    }

    private static Color GetLatencyColor(long latency)
    {
        if (latency < 100) return Color.LightGreen;
        if (latency <= 200) return Color.Yellow;
        return Color.Red;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
