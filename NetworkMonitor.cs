using System.Net.NetworkInformation;

namespace NetworkHealthMonitor;

public enum HealthStatus
{
    Healthy,    // Green: < 100ms, 0% loss
    Degraded,   // Yellow: 100-200ms or < 5% loss
    Poor        // Red: > 200ms or > 5% loss
}

public class PingResult
{
    public DateTime Timestamp { get; set; }
    public string Target { get; set; } = string.Empty;
    public long? Latency { get; set; }
    public bool Success { get; set; }
}

public class TargetStats
{
    public string Target { get; set; } = string.Empty;
    public long? CurrentLatency { get; set; }
    public double AverageLatency { get; set; }
    public long MinLatency { get; set; } = long.MaxValue;
    public long MaxLatency { get; set; }
    public double PacketLossPercent { get; set; }
    public List<PingResult> History { get; } = new();
}

public class NetworkMonitor : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<string, TargetStats> _targetStats;
    private readonly string[] _targets = { "8.8.8.8", "1.1.1.1" };
    private const int MaxHistorySize = 60;
    private const int PingTimeoutMs = 3000;

    public event EventHandler<HealthStatus>? HealthStatusChanged;
    public event EventHandler? StatsUpdated;

    public HealthStatus CurrentStatus { get; private set; } = HealthStatus.Poor;

    public NetworkMonitor()
    {
        _targetStats = new Dictionary<string, TargetStats>();
        foreach (var target in _targets)
        {
            _targetStats[target] = new TargetStats { Target = target };
        }

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 5000 // 5 seconds
        };
        _timer.Tick += async (s, e) => await PingAllTargetsAsync();
    }

    public void Start()
    {
        _timer.Start();
        _ = PingAllTargetsAsync(); // Initial ping
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public IReadOnlyDictionary<string, TargetStats> GetStats() => _targetStats;

    public string[] GetTargets() => _targets;

    private async Task PingAllTargetsAsync()
    {
        var tasks = _targets.Select(PingTargetAsync).ToArray();
        await Task.WhenAll(tasks);

        UpdateOverallStatus();
        StatsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private async Task PingTargetAsync(string target)
    {
        var stats = _targetStats[target];
        var result = new PingResult
        {
            Timestamp = DateTime.Now,
            Target = target
        };

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, PingTimeoutMs);

            if (reply.Status == IPStatus.Success)
            {
                result.Success = true;
                result.Latency = reply.RoundtripTime;
                stats.CurrentLatency = reply.RoundtripTime;
            }
            else
            {
                result.Success = false;
                stats.CurrentLatency = null;
            }
        }
        catch
        {
            result.Success = false;
            stats.CurrentLatency = null;
        }

        stats.History.Add(result);
        if (stats.History.Count > MaxHistorySize)
            stats.History.RemoveAt(0);

        // Recalculate stats from history window
        var successfulPings = stats.History.Where(r => r.Success && r.Latency.HasValue).ToList();
        if (successfulPings.Count > 0)
        {
            stats.AverageLatency = successfulPings.Average(r => r.Latency!.Value);
            stats.MinLatency = successfulPings.Min(r => r.Latency!.Value);
            stats.MaxLatency = successfulPings.Max(r => r.Latency!.Value);
        }
        else
        {
            stats.MinLatency = long.MaxValue;
            stats.MaxLatency = 0;
        }

        var totalPings = stats.History.Count;
        var failedPings = stats.History.Count(r => !r.Success);
        stats.PacketLossPercent = totalPings > 0 ? (failedPings * 100.0 / totalPings) : 0;
    }

    private void UpdateOverallStatus()
    {
        var allStats = _targetStats.Values.ToList();

        // Calculate worst-case metrics across all targets
        var maxAvgLatency = allStats.Where(s => s.History.Any(h => h.Success))
                                    .Select(s => s.AverageLatency)
                                    .DefaultIfEmpty(double.MaxValue)
                                    .Max();

        var maxPacketLoss = allStats.Select(s => s.PacketLossPercent).Max();

        // Determine status based on thresholds
        HealthStatus newStatus;
        if (maxAvgLatency > 200 || maxPacketLoss > 5)
        {
            newStatus = HealthStatus.Poor;
        }
        else if (maxAvgLatency >= 100 || maxPacketLoss > 0)
        {
            newStatus = HealthStatus.Degraded;
        }
        else
        {
            newStatus = HealthStatus.Healthy;
        }

        if (newStatus != CurrentStatus)
        {
            CurrentStatus = newStatus;
            HealthStatusChanged?.Invoke(this, newStatus);
        }
    }

    public string GetTooltipText()
    {
        var lines = new List<string> { "Network Health Monitor" };

        foreach (var target in _targets)
        {
            var stats = _targetStats[target];
            var latencyText = stats.CurrentLatency.HasValue
                ? $"{stats.CurrentLatency}ms"
                : "timeout";
            lines.Add($"{target}: {latencyText}");
        }

        var avgLoss = _targetStats.Values.Average(s => s.PacketLossPercent);
        lines.Add($"Loss: {avgLoss:F1}%");

        return string.Join("\n", lines);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
