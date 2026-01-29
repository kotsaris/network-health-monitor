using System.Drawing.Drawing2D;

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

        // Let WinForms handle DPI scaling via PerMonitorV2
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Network Health Monitor";
        ClientSize = new Size(500, 520);
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
            Height = 60,
            BackColor = Color.FromArgb(45, 45, 45)
        };

        int indicatorSize = 20;
        _statusIndicator = new Panel
        {
            Size = new Size(indicatorSize, indicatorSize),
            Location = new Point(20, 20),
            BackColor = Color.Gray
        };
        // Create circular region
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(0, 0, indicatorSize, indicatorSize);
            _statusIndicator.Region = new Region(path);
        }

        _statusLabel = new Label
        {
            Text = "Checking...",
            Location = new Point(50, 17),
            AutoSize = true,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = Color.White
        };

        headerPanel.Controls.Add(_statusIndicator);
        headerPanel.Controls.Add(_statusLabel);
        Controls.Add(headerPanel);

        // Content panel
        var contentPanel = new Panel
        {
            Location = new Point(0, 60),
            Size = new Size(500, 460),
            AutoScroll = true,
            Padding = new Padding(15)
        };

        int yOffset = 15;
        foreach (var target in _monitor.GetTargets())
        {
            var targetPanel = CreateTargetPanel(target, yOffset);
            contentPanel.Controls.Add(targetPanel);
            yOffset += 210;
        }

        Controls.Add(contentPanel);

        ResumeLayout(false);
    }

    private Panel CreateTargetPanel(string target, int yOffset)
    {
        var panel = new Panel
        {
            Location = new Point(15, yOffset),
            Size = new Size(450, 195),
            BackColor = Color.FromArgb(45, 45, 45)
        };

        // Target header
        var targetLabel = new Label
        {
            Text = target,
            Location = new Point(15, 12),
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255)
        };
        panel.Controls.Add(targetLabel);

        // Row 1: Current and Min/Max
        var currentLabel = new Label
        {
            Text = "Current:",
            Location = new Point(15, 48),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(currentLabel);

        var currentValueLabel = new Label
        {
            Text = "-- ms",
            Location = new Point(90, 48),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        };
        panel.Controls.Add(currentValueLabel);
        _currentLatencyLabels[target] = currentValueLabel;

        var minMaxLabel = new Label
        {
            Text = "Min/Max:",
            Location = new Point(230, 48),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(minMaxLabel);

        var minMaxValueLabel = new Label
        {
            Text = "-- / -- ms",
            Location = new Point(310, 48),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.White
        };
        panel.Controls.Add(minMaxValueLabel);
        _minMaxLabels[target] = minMaxValueLabel;

        // Row 2: Average and Packet Loss
        var avgLabel = new Label
        {
            Text = "Average:",
            Location = new Point(15, 75),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(avgLabel);

        var avgValueLabel = new Label
        {
            Text = "-- ms",
            Location = new Point(90, 75),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.White
        };
        panel.Controls.Add(avgValueLabel);
        _avgLatencyLabels[target] = avgValueLabel;

        var lossLabel = new Label
        {
            Text = "Packet Loss:",
            Location = new Point(230, 75),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.LightGray
        };
        panel.Controls.Add(lossLabel);

        var lossValueLabel = new Label
        {
            Text = "0.0%",
            Location = new Point(330, 75),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.White
        };
        panel.Controls.Add(lossValueLabel);
        _packetLossLabels[target] = lossValueLabel;

        // Graph section
        var graphLabel = new Label
        {
            Text = "Latency History (last 60 pings)",
            Location = new Point(15, 108),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5F)
        };
        panel.Controls.Add(graphLabel);

        var graphPanel = new Panel
        {
            Location = new Point(15, 130),
            Size = new Size(420, 55),
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
        g.SmoothingMode = SmoothingMode.AntiAlias;
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
        using var thresholdPen = new Pen(Color.FromArgb(60, 255, 255, 0), 1) { DashStyle = DashStyle.Dot };
        var y100 = size.Height - (int)(100.0 / maxLatency * (size.Height - 10)) - 5;
        var y200 = size.Height - (int)(200.0 / maxLatency * (size.Height - 10)) - 5;
        g.DrawLine(thresholdPen, 0, y100, size.Width, y100);
        using var redThresholdPen = new Pen(Color.FromArgb(60, 255, 0, 0), 1) { DashStyle = DashStyle.Dot };
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
