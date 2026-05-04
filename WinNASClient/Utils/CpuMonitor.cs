using System.Diagnostics;

namespace WinNASClient.Utils;

public static class CpuMonitor
{
    private static PerformanceCounter? _cpuCounter;
    private static float _lastCpuValue;
    private static DateTime _lastReadTime = DateTime.MinValue;
    private static readonly object _lock = new();

    public static float GetCpuUsage()
    {
        lock (_lock)
        {
            try
            {
                if (_cpuCounter == null)
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                    _lastReadTime = DateTime.UtcNow;
                    Thread.Sleep(500);
                    _lastCpuValue = _cpuCounter.NextValue();
                    return (float)Math.Round(_lastCpuValue, 1);
                }

                if ((DateTime.UtcNow - _lastReadTime).TotalSeconds > 1)
                {
                    _lastCpuValue = _cpuCounter.NextValue();
                    _lastReadTime = DateTime.UtcNow;
                }

                return (float)Math.Round(_lastCpuValue, 1);
            }
            catch
            {
                return GetCpuUsageFallback();
            }
        }
    }

    private static float GetCpuUsageFallback()
    {
        try
        {
            var cpuTime = new List<float>();
            var proc = Process.GetCurrentProcess();

            var startTime = DateTime.UtcNow;
            var startCpuUsage = proc.TotalProcessorTime;

            Thread.Sleep(200);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = proc.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return (float)Math.Round(cpuUsageTotal * 100, 1);
        }
        catch
        {
            return 0;
        }
    }
}
