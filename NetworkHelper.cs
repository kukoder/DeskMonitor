using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace DeskMonitor
{
    public class PingTargetTracker : IDisposable
    {
        private const int MaxHistoryPoints = 60;
        private readonly string _host;
        private readonly List<int> _history;
        private readonly object _lock = new object();
        private readonly Ping _pinger;
        private bool _isPinging;

        public string Host { get { return _host; } }
        public int LastPing { get; private set; }
        public int MinPing { get; private set; }
        public int AvgPing { get; private set; }
        public int MaxPing { get; private set; }
        public bool IsTimeout { get; private set; }

        public PingTargetTracker(string host)
        {
            _host = host;
            _history = new List<int>();
            _pinger = new Ping();
            LastPing = -1;
            MinPing = -1;
            AvgPing = -1;
            MaxPing = -1;
        }

        public void PingAsync()
        {
            if (_isPinging) return;
            _isPinging = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    PingReply reply = _pinger.Send(_host, 1200);
                    lock (_lock)
                    {
                        if (reply.Status == IPStatus.Success)
                        {
                            int rtt = (int)reply.RoundtripTime;
                            LastPing = rtt;
                            IsTimeout = false;
                            _history.Add(rtt);
                        }
                        else
                        {
                            IsTimeout = true;
                            _history.Add(0);
                        }

                        while (_history.Count > MaxHistoryPoints)
                        {
                            _history.RemoveAt(0);
                        }

                        CalculateStats();
                    }
                }
                catch
                {
                    lock (_lock)
                    {
                        IsTimeout = true;
                    }
                }
                finally
                {
                    _isPinging = false;
                }
            });
        }

        private void CalculateStats()
        {
            int min = int.MaxValue;
            int max = 0;
            int sum = 0;
            int validCount = 0;

            for (int i = 0; i < _history.Count; i++)
            {
                int val = _history[i];
                if (val > 0)
                {
                    if (val < min) min = val;
                    if (val > max) max = val;
                    sum += val;
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                MinPing = min;
                MaxPing = max;
                AvgPing = (int)Math.Round((double)sum / validCount);
            }
        }

        public int[] GetHistorySnapshot()
        {
            lock (_lock)
            {
                return _history.ToArray();
            }
        }

        public void Dispose()
        {
            try
            {
                _pinger.Dispose();
            }
            catch { }
        }
    }

    public class NetworkTrafficSnapshot
    {
        public double DownloadBytesPerSec { get; set; }
        public double UploadBytesPerSec { get; set; }
    }

    public class NetworkTrafficTracker
    {
        private const int MaxHistory = 50;
        private long _prevBytesReceived;
        private long _prevBytesSent;
        private DateTime _prevTime;
        private bool _isFirstCall = true;

        private readonly List<double> _downloadHistory = new List<double>();
        private readonly List<double> _uploadHistory = new List<double>();
        private readonly object _lock = new object();

        public double CurrentDownloadSpeed { get; private set; }
        public double CurrentUploadSpeed { get; private set; }
        public double AutoScaleMaxBytesPerSec { get; private set; }

        public NetworkTrafficTracker()
        {
            _prevTime = DateTime.UtcNow;
            SampleCounters();
        }

        public void Update()
        {
            long currentReceived = 0;
            long currentSent = 0;

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    {
                        IPInterfaceStatistics stats = ni.GetIPStatistics();
                        currentReceived += stats.BytesReceived;
                        currentSent += stats.BytesSent;
                    }
                }
            }
            catch { }

            DateTime now = DateTime.UtcNow;
            double elapsedSeconds = (now - _prevTime).TotalSeconds;
            if (elapsedSeconds <= 0.05) elapsedSeconds = 1.0;

            if (_isFirstCall)
            {
                _prevBytesReceived = currentReceived;
                _prevBytesSent = currentSent;
                _prevTime = now;
                _isFirstCall = false;
                CurrentDownloadSpeed = 0;
                CurrentUploadSpeed = 0;
                return;
            }

            long diffReceived = currentReceived - _prevBytesReceived;
            long diffSent = currentSent - _prevBytesSent;

            _prevBytesReceived = currentReceived;
            _prevBytesSent = currentSent;
            _prevTime = now;

            if (diffReceived < 0) diffReceived = 0;
            if (diffSent < 0) diffSent = 0;

            double downSpeed = diffReceived / elapsedSeconds;
            double upSpeed = diffSent / elapsedSeconds;

            CurrentDownloadSpeed = downSpeed;
            CurrentUploadSpeed = upSpeed;

            lock (_lock)
            {
                _downloadHistory.Add(downSpeed);
                _uploadHistory.Add(upSpeed);

                while (_downloadHistory.Count > MaxHistory) _downloadHistory.RemoveAt(0);
                while (_uploadHistory.Count > MaxHistory) _uploadHistory.RemoveAt(0);

                // Calculate auto-scaling ceiling
                double maxVal = 10 * 1024; // baseline min ceiling: 10 KB/s
                for (int i = 0; i < _downloadHistory.Count; i++)
                {
                    if (_downloadHistory[i] > maxVal) maxVal = _downloadHistory[i];
                    if (_uploadHistory[i] > maxVal) maxVal = _uploadHistory[i];
                }
                AutoScaleMaxBytesPerSec = maxVal * 1.15; // +15% headroom
            }
        }

        private void SampleCounters()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    {
                        IPInterfaceStatistics stats = ni.GetIPStatistics();
                        _prevBytesReceived += stats.BytesReceived;
                        _prevBytesSent += stats.BytesSent;
                    }
                }
            }
            catch { }
        }

        public void GetHistorySnapshots(out double[] downloads, out double[] uploads)
        {
            lock (_lock)
            {
                downloads = _downloadHistory.ToArray();
                uploads = _uploadHistory.ToArray();
            }
        }

        public static string FormatSpeed(double bytesPerSec, bool compact)
        {
            if (bytesPerSec < 1024)
            {
                return string.Format("{0:0} B/s", bytesPerSec);
            }
            if (bytesPerSec < 1024 * 1024)
            {
                double kb = bytesPerSec / 1024.0;
                return compact ? string.Format("{0:0}K", kb) : string.Format("{0:0.0} КБ/с", kb);
            }
            double mb = bytesPerSec / (1024.0 * 1024.0);
            return compact ? string.Format("{0:0.0}M", mb) : string.Format("{0:0.0} МБ/с", mb);
        }
    }

    public static class NetworkHelper
    {
        public static string GetLocalIp()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                            {
                                string str = ip.Address.ToString();
                                if (!str.StartsWith("169.254.")) return str;
                            }
                        }
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        public static string GetExternalIp()
        {
            string[] services = new string[]
            {
                "https://api.ipify.org",
                "https://icanhazip.com",
                "https://ifconfig.me/ip"
            };

            foreach (string url in services)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 2500;
                    req.UserAgent = "DeskMonitor/1.0 (Windows NT 10.0; Win64; x64)";
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader reader = new StreamReader(resp.GetResponseStream()))
                    {
                        string ip = reader.ReadToEnd().Trim();
                        if (!string.IsNullOrEmpty(ip) && ip.Length <= 45)
                        {
                            return ip;
                        }
                    }
                }
                catch { }
            }

            return "Недоступен";
        }
    }
}
