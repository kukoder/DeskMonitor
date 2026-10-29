using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskMonitor
{
    public class MetricSnapshot
    {
        public double CpuPercent { get; set; }
        public double[] CorePercents { get; set; }
        public double GpuPercent { get; set; }
        public double RamPercent { get; set; }
        public double RamUsedGb { get; set; }
        public double RamTotalGb { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SystemMetrics : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private readonly int _coreCount;
        private readonly PerformanceCounter[] _coreCounters;
        private readonly GpuMonitor _gpuMonitor;

        private long _prevIdle;
        private long _prevKernel;
        private long _prevUser;
        private bool _isFirstCall = true;

        public int CoreCount { get { return _coreCount; } }

        public SystemMetrics()
        {
            _coreCount = Environment.ProcessorCount;
            _gpuMonitor = new GpuMonitor();

            _coreCounters = new PerformanceCounter[_coreCount];
            for (int i = 0; i < _coreCount; i++)
            {
                try
                {
                    _coreCounters[i] = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), true);
                    _coreCounters[i].NextValue(); // Prime counter
                }
                catch
                {
                    _coreCounters[i] = null;
                }
            }

            GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);
        }

        public MetricSnapshot GetSnapshot()
        {
            MetricSnapshot snapshot = new MetricSnapshot
            {
                Timestamp = DateTime.Now,
                CorePercents = new double[_coreCount]
            };

            // 1. Calculate General CPU Usage
            long currentIdle, currentKernel, currentUser;
            if (GetSystemTimes(out currentIdle, out currentKernel, out currentUser))
            {
                if (_isFirstCall)
                {
                    _prevIdle = currentIdle;
                    _prevKernel = currentKernel;
                    _prevUser = currentUser;
                    _isFirstCall = false;
                    snapshot.CpuPercent = 0;
                }
                else
                {
                    long usr = currentUser - _prevUser;
                    long ker = currentKernel - _prevKernel;
                    long idl = currentIdle - _prevIdle;
                    long total = usr + ker;

                    _prevIdle = currentIdle;
                    _prevKernel = currentKernel;
                    _prevUser = currentUser;

                    if (total > 0)
                    {
                        double cpu = (double)(total - idl) * 100.0 / total;
                        if (cpu < 0) cpu = 0;
                        if (cpu > 100) cpu = 100;
                        snapshot.CpuPercent = Math.Round(cpu, 1);
                    }
                }
            }

            // 2. Calculate Per-Core CPU Usage using managed Performance Counters
            for (int i = 0; i < _coreCount; i++)
            {
                double corePct = 0;
                if (_coreCounters[i] != null)
                {
                    try
                    {
                        corePct = _coreCounters[i].NextValue();
                        if (corePct < 0) corePct = 0;
                        if (corePct > 100) corePct = 100;
                    }
                    catch
                    {
                        corePct = 0;
                    }
                }
                snapshot.CorePercents[i] = Math.Round(corePct, 0);
            }

            // 3. Calculate GPU Usage
            snapshot.GpuPercent = _gpuMonitor.GetGpuUsage();

            // 4. Calculate RAM Usage
            MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
                double ramPercent = totalGb > 0 ? (usedGb * 100.0 / totalGb) : mem.dwMemoryLoad;

                snapshot.RamPercent = Math.Round(ramPercent, 1);
                snapshot.RamUsedGb = Math.Round(usedGb, 1);
                snapshot.RamTotalGb = Math.Round(totalGb, 1);
            }

            return snapshot;
        }

        public void Dispose()
        {
            if (_coreCounters != null)
            {
                for (int i = 0; i < _coreCounters.Length; i++)
                {
                    if (_coreCounters[i] != null)
                    {
                        try { _coreCounters[i].Dispose(); } catch { }
                    }
                }
            }

            if (_gpuMonitor != null)
            {
                try { _gpuMonitor.Dispose(); } catch { }
            }
        }
    }
}
