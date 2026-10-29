using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DeskMonitor
{
    public class TopProcessItem
    {
        public int Pid { get; set; }
        public string Name { get; set; }
        public double CpuPercent { get; set; }      // Average CPU % across all cores over the window
        public double CpuTimeSeconds { get; set; }  // Total CPU time consumed during window
        public bool IsExited { get; set; }          // True if process exited within window
        public string FormattedCpuTime { get; set; } // e.g. "1м 24с" or "14.2с"
    }

    public class ProcessCpuTracker : IDisposable
    {
        private class ProcessSample
        {
            public long TimestampMs;
            public double TotalCpuMs;

            public ProcessSample(long timeMs, double cpuMs)
            {
                TimestampMs = timeMs;
                TotalCpuMs = cpuMs;
            }
        }

        private class ProcessTrackEntry
        {
            public int Pid;
            public string Name;
            public bool IsExited;
            public long ExitTimeMs;
            public List<ProcessSample> Samples = new List<ProcessSample>();

            public ProcessTrackEntry(int pid, string name)
            {
                Pid = pid;
                Name = name;
            }
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, ProcessTrackEntry> _trackedProcesses = new Dictionary<int, ProcessTrackEntry>();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private Timer _pollTimer;
        private bool _isPolling = false;
        private bool _disposed = false;

        private int _pollIntervalSec = 3;
        private int _windowMinutes = 10;
        private bool _includeExited = true;
        private int _topCount = 5;
        private bool _isEnabled = true;

        private readonly int _logicalCores;

        public ProcessCpuTracker(AppSettings settings)
        {
            _logicalCores = Math.Max(1, Environment.ProcessorCount);
            ApplySettings(settings);
        }

        public void ApplySettings(AppSettings settings)
        {
            lock (_lock)
            {
                _isEnabled = settings.ShowTopProcessesButton;
                _pollIntervalSec = Math.Max(1, Math.Min(30, settings.TopProcessesPollIntervalSec));
                _windowMinutes = Math.Max(1, Math.Min(60, settings.TopProcessesWindowMinutes));
                _topCount = Math.Max(1, Math.Min(25, settings.TopProcessesCount));
                _includeExited = settings.TopProcessesIncludeExited;

                RestartTimer();
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                RestartTimer();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_pollTimer != null)
                {
                    _pollTimer.Dispose();
                    _pollTimer = null;
                }
            }
        }

        private void RestartTimer()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Dispose();
                _pollTimer = null;
            }

            if (_isEnabled && !_disposed)
            {
                int intervalMs = _pollIntervalSec * 1000;
                _pollTimer = new Timer(OnPollTick, null, 100, intervalMs);
            }
        }

        private void OnPollTick(object state)
        {
            if (_disposed || !_isEnabled) return;

            lock (_lock)
            {
                if (_isPolling) return;
                _isPolling = true;
            }

            try
            {
                long nowMs = _stopwatch.ElapsedMilliseconds;
                long cutoffMs = nowMs - ((long)_windowMinutes * 60 * 1000);

                Process[] procs = Process.GetProcesses();
                HashSet<int> seenPids = new HashSet<int>();

                foreach (Process p in procs)
                {
                    try
                    {
                        int pid = p.Id;
                        if (pid == 0) continue; // Skip System Idle Process

                        seenPids.Add(pid);
                        string name = p.ProcessName;
                        double totalCpuMs = p.TotalProcessorTime.TotalMilliseconds;

                        lock (_lock)
                        {
                            ProcessTrackEntry entry;
                            if (!_trackedProcesses.TryGetValue(pid, out entry))
                            {
                                entry = new ProcessTrackEntry(pid, name);
                                _trackedProcesses[pid] = entry;
                            }
                            else if (entry.Name != name)
                            {
                                // PID was reused by another process
                                entry.Name = name;
                                entry.IsExited = false;
                                entry.Samples.Clear();
                            }
                            else if (entry.IsExited)
                            {
                                entry.IsExited = false;
                            }

                            // Check if TotalProcessorTime decreased (PID reuse without name change)
                            if (entry.Samples.Count > 0 && totalCpuMs < entry.Samples[entry.Samples.Count - 1].TotalCpuMs)
                            {
                                entry.Samples.Clear();
                            }

                            entry.Samples.Add(new ProcessSample(nowMs, totalCpuMs));

                            // Prune samples older than the cutoff window
                            int removeCount = 0;
                            for (int i = 0; i < entry.Samples.Count - 1; i++)
                            {
                                if (entry.Samples[i].TimestampMs < cutoffMs)
                                {
                                    removeCount++;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (removeCount > 0)
                            {
                                entry.Samples.RemoveRange(0, removeCount);
                            }
                        }
                    }
                    catch
                    {
                        // Access denied or exited process
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                // Handle processes that were not seen (exited)
                lock (_lock)
                {
                    List<int> toRemove = new List<int>();

                    foreach (KeyValuePair<int, ProcessTrackEntry> kvp in _trackedProcesses)
                    {
                        if (!seenPids.Contains(kvp.Key))
                        {
                            ProcessTrackEntry entry = kvp.Value;
                            if (!entry.IsExited)
                            {
                                entry.IsExited = true;
                                entry.ExitTimeMs = nowMs;
                            }
                            else
                            {
                                // If exited longer ago than window, purge
                                if (entry.ExitTimeMs < cutoffMs)
                                {
                                    toRemove.Add(kvp.Key);
                                }
                            }
                        }
                    }

                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        _trackedProcesses.Remove(toRemove[i]);
                    }
                }
            }
            catch { }
            finally
            {
                lock (_lock)
                {
                    _isPolling = false;
                }
            }
        }

        public List<TopProcessItem> GetTopProcesses()
        {
            return GetTopProcesses(_topCount, _windowMinutes, _includeExited);
        }

        public List<TopProcessItem> GetTopProcesses(int count, int windowMinutes, bool includeExited)
        {
            List<TopProcessItem> result = new List<TopProcessItem>();

            lock (_lock)
            {
                long nowMs = _stopwatch.ElapsedMilliseconds;
                long windowMs = (long)windowMinutes * 60 * 1000;
                long cutoffMs = nowMs - windowMs;

                foreach (KeyValuePair<int, ProcessTrackEntry> kvp in _trackedProcesses)
                {
                    ProcessTrackEntry entry = kvp.Value;
                    if (entry.IsExited && !includeExited) continue;
                    if (entry.Samples.Count < 2) continue;

                    ProcessSample firstSample = null;
                    ProcessSample lastSample = entry.Samples[entry.Samples.Count - 1];

                    // Find earliest sample within the window
                    for (int i = 0; i < entry.Samples.Count; i++)
                    {
                        if (entry.Samples[i].TimestampMs >= cutoffMs)
                        {
                            firstSample = entry.Samples[i];
                            break;
                        }
                    }

                    if (firstSample == null)
                    {
                        firstSample = entry.Samples[0];
                    }

                    long deltaWallMs = lastSample.TimestampMs - firstSample.TimestampMs;
                    if (deltaWallMs <= 0) continue;

                    double deltaCpuMs = lastSample.TotalCpuMs - firstSample.TotalCpuMs;
                    if (deltaCpuMs < 0) continue;

                    // CPU % relative to total CPU capacity (all cores)
                    double cpuPct = (deltaCpuMs / (deltaWallMs * _logicalCores)) * 100.0;
                    if (cpuPct > 100.0) cpuPct = 100.0;

                    double cpuSeconds = deltaCpuMs / 1000.0;

                    TopProcessItem item = new TopProcessItem
                    {
                        Pid = entry.Pid,
                        Name = entry.Name,
                        CpuPercent = Math.Round(cpuPct, 2),
                        CpuTimeSeconds = Math.Round(cpuSeconds, 1),
                        IsExited = entry.IsExited,
                        FormattedCpuTime = FormatCpuTime(cpuSeconds)
                    };

                    result.Add(item);
                }
            }

            // Sort descending by CPU percentage (or CPU time)
            result.Sort((a, b) => b.CpuPercent.CompareTo(a.CpuPercent));

            if (result.Count > count)
            {
                result.RemoveRange(count, result.Count - count);
            }

            return result;
        }

        public static string FormatCpuTime(double seconds)
        {
            if (seconds < 60)
            {
                return string.Format("{0:0.0}с", seconds);
            }
            int mins = (int)(seconds / 60);
            double remSec = seconds % 60;
            if (mins < 60)
            {
                return string.Format("{0}м {1:0}с", mins, remSec);
            }
            int hours = mins / 60;
            int remMin = mins % 60;
            return string.Format("{0}ч {1}м", hours, remMin);
        }

        public void Dispose()
        {
            _disposed = true;
            Stop();
            lock (_lock)
            {
                _trackedProcesses.Clear();
            }
        }
    }
}
