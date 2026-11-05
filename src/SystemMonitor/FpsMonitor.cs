using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Session;

namespace SystemMonitor;

/// <summary>
/// Measures FPS per process from DXGI Present events delivered by an ETW session (same source PresentMon uses).
/// ETW hands events over in ~1s bursts, so FPS is computed from each event's own timestamp over a sliding
/// window, never from when the events happened to arrive. Covers DX9-DX12; Vulkan/OpenGL need DxgKrnl.
/// </summary>
public sealed class FpsMonitor : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int DxgiPresentStart = 42;
    private const string SessionName = "SystemMonitorFps";

    private const long WindowTicks = TimeSpan.TicksPerSecond;      // sliding window of event time
    private const double StaleSeconds = 2.0;                        // no events for this long => no FPS

    private sealed class PidFrames
    {
        public readonly Queue<long> Times = new();                  // event timestamps, ticks
        public long LastArrival = Stopwatch.GetTimestamp();         // wall clock of last event, for staleness
    }

    private readonly Dictionary<int, PidFrames> _pids = new();
    private readonly object _gate = new();
    private readonly TraceEventSession _session;
    private readonly Thread _worker;

    /// <summary>Diagnostic: DXGI event id -> count, to confirm the provider delivers events at all.</summary>
    public ConcurrentDictionary<int, int> SeenEventIds { get; } = new();

    /// <summary>Diagnostic: per-process FPS from the most recent <see cref="Sample"/> call.</summary>
    public IReadOnlyDictionary<int, float> LastRates { get; private set; } = new Dictionary<int, float>();

    public FpsMonitor()
    {
        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableProvider(DxgiProvider);

        _worker = new Thread(() =>
        {
            _session.Source.Dynamic.All += data =>
            {
                if (data.ProviderGuid != DxgiProvider) return;

                SeenEventIds.AddOrUpdate((int)data.ID, 1, (_, n) => n + 1);
                if ((int)data.ID == DxgiPresentStart) Record(data.ProcessID, data.TimeStamp.Ticks);
            };
            _session.Source.Process();
        })
        { IsBackground = true, Name = "FpsEtw" };
        _worker.Start();
    }

    private void Record(int pid, long ticks)
    {
        lock (_gate)
        {
            if (!_pids.TryGetValue(pid, out var frames)) _pids[pid] = frames = new PidFrames();
            frames.Times.Enqueue(ticks);
            frames.LastArrival = Stopwatch.GetTimestamp();
            while (frames.Times.Count > 0 && frames.Times.Peek() < ticks - WindowTicks) frames.Times.Dequeue();
        }
    }

    /// <summary>FPS of <paramref name="pid"/> over the last second of event time; null if it stopped presenting.</summary>
    public float? Sample(int pid)
    {
        var rates = new Dictionary<int, float>();
        long now = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            foreach (var (key, frames) in _pids.ToArray())
            {
                if ((now - frames.LastArrival) / (double)Stopwatch.Frequency > StaleSeconds)
                {
                    _pids.Remove(key);
                    continue;
                }
                if (RateOf(frames.Times) is float rate) rates[key] = rate;
            }
        }

        LastRates = rates;
        return rates.TryGetValue(pid, out float fps) ? fps : null;
    }

    // (frames - 1) intervals across the span between first and last event in the window.
    private static float? RateOf(Queue<long> times)
    {
        if (times.Count < 2) return null;
        long span = times.Last() - times.Peek();
        return span > 0 ? (float)((times.Count - 1) * (double)TimeSpan.TicksPerSecond / span) : null;
    }

    public void Dispose() => _session.Dispose();
}
