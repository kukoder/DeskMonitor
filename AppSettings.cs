using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskMonitor
{
    public class AppSettings
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "DeskMonitor";

        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool TopMost { get; set; }
        public double Opacity { get; set; }
        public bool SnapToEdges { get; set; }
        public int SnapDistance { get; set; }
        public bool ShowGraph { get; set; }
        public int GraphPeriodSeconds { get; set; }
        public int UpdateIntervalMs { get; set; }
        public bool AutoStart { get; set; }

        public bool ShowCoreBars { get; set; }
        public string CpuColorHex { get; set; }
        public string GpuColorHex { get; set; }
        public string RamColorHex { get; set; }
        public string PingColorHex { get; set; }
        public string NetDownColorHex { get; set; }
        public string NetUpColorHex { get; set; }
        public string TextColorHex { get; set; }
        public List<string> PingTargets { get; set; }

        // Top CPU Processes Monitor
        public bool ShowTopProcessesButton { get; set; }
        public int TopProcessesCount { get; set; }
        public int TopProcessesWindowMinutes { get; set; }
        public int TopProcessesPollIntervalSec { get; set; }
        public bool TopProcessesIncludeExited { get; set; }

        public AppSettings()
        {
            PingTargets = new List<string>();
            SetDefaults();
        }

        public void SetDefaults()
        {
            WindowWidth = 160;
            WindowHeight = 440;
            WindowX = -1;
            WindowY = -1;
            TopMost = true;
            Opacity = 0.95;
            SnapToEdges = true;
            SnapDistance = 16;
            ShowGraph = true;
            GraphPeriodSeconds = 60;
            UpdateIntervalMs = 1000;
            AutoStart = false;

            ShowCoreBars = true;
            CpuColorHex = "#38BDF8";     // Sky Blue
            GpuColorHex = "#F59E0B";     // Amber / Gold
            RamColorHex = "#C084FC";     // Light Purple
            PingColorHex = "#10B981";    // Emerald Green
            NetDownColorHex = "#06B6D4"; // Cyan
            NetUpColorHex = "#EC4899";   // Pink
            TextColorHex = "#CDD6F4";    // Light Text

            PingTargets.Clear();
            PingTargets.Add("8.8.8.8");

            ShowTopProcessesButton = true;
            TopProcessesCount = 5;
            TopProcessesWindowMinutes = 10;
            TopProcessesPollIntervalSec = 3;
            TopProcessesIncludeExited = true;
        }

        public static string ColorToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        public static Color ParseColor(string str, Color fallback)
        {
            if (string.IsNullOrEmpty(str)) return fallback;
            str = str.Trim();
            try
            {
                return ColorTranslator.FromHtml(str);
            }
            catch
            {
                try
                {
                    if (!str.StartsWith("#"))
                    {
                        return ColorTranslator.FromHtml("#" + str);
                    }
                }
                catch { }
                return fallback;
            }
        }

        public Color GetCpuColor() { return ParseColor(CpuColorHex, Color.FromArgb(56, 189, 248)); }
        public Color GetGpuColor() { return ParseColor(GpuColorHex, Color.FromArgb(245, 158, 11)); }
        public Color GetRamColor() { return ParseColor(RamColorHex, Color.FromArgb(192, 132, 252)); }
        public Color GetPingColor() { return ParseColor(PingColorHex, Color.FromArgb(16, 185, 129)); }
        public Color GetNetDownColor() { return ParseColor(NetDownColorHex, Color.FromArgb(6, 182, 212)); }
        public Color GetNetUpColor() { return ParseColor(NetUpColorHex, Color.FromArgb(236, 72, 153)); }
        public Color GetTextColor() { return ParseColor(TextColorHex, Color.FromArgb(205, 214, 244)); }

        public static string GetConfigPath()
        {
            try
            {
                string asmPath = typeof(AppSettings).Assembly.Location;
                string dir = !string.IsNullOrEmpty(asmPath) ? Path.GetDirectoryName(asmPath) : AppDomain.CurrentDomain.BaseDirectory;
                string localPath = Path.Combine(dir, "settings.json");
                return localPath;
            }
            catch
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "DeskMonitor");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                return Path.Combine(folder, "settings.json");
            }
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            string path = GetConfigPath();

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    ParseJson(json, settings);
                }
                catch
                {
                    settings.SetDefaults();
                }
            }
            else
            {
                settings.Save();
            }

            settings.AutoStart = CheckRegistryAutoStart();

            // Validate constraints (Must allow narrow width down to 140px)
            if (settings.WindowWidth < 140) settings.WindowWidth = 140;
            if (settings.WindowHeight < 150) settings.WindowHeight = 150;
            if (settings.Opacity < 0.2) settings.Opacity = 0.2;
            if (settings.Opacity > 1.0) settings.Opacity = 1.0;
            if (settings.SnapDistance < 5) settings.SnapDistance = 5;
            if (settings.SnapDistance > 50) settings.SnapDistance = 50;
            if (settings.GraphPeriodSeconds < 10) settings.GraphPeriodSeconds = 10;
            if (settings.UpdateIntervalMs < 200) settings.UpdateIntervalMs = 1000;
            if (settings.PingTargets.Count == 0) settings.PingTargets.Add("8.8.8.8");

            if (settings.TopProcessesCount < 1) settings.TopProcessesCount = 5;
            if (settings.TopProcessesCount > 25) settings.TopProcessesCount = 25;
            if (settings.TopProcessesWindowMinutes < 1) settings.TopProcessesWindowMinutes = 10;
            if (settings.TopProcessesWindowMinutes > 120) settings.TopProcessesWindowMinutes = 120;
            if (settings.TopProcessesPollIntervalSec < 1) settings.TopProcessesPollIntervalSec = 3;
            if (settings.TopProcessesPollIntervalSec > 60) settings.TopProcessesPollIntervalSec = 60;

            return settings;
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();
                string json = SerializeJson();
                File.WriteAllText(path, json, Encoding.UTF8);
                ApplyRegistryAutoStart(AutoStart);
            }
            catch { }
        }

        private static bool CheckRegistryAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(AppName);
                        if (val != null && string.Equals(val.ToString(), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void ApplyRegistryAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(AppName, Application.ExecutablePath);
                        }
                        else
                        {
                            if (key.GetValue(AppName) != null)
                            {
                                key.DeleteValue(AppName, false);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private string SerializeJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat("  \"WindowX\": {0},\r\n", WindowX);
            sb.AppendFormat("  \"WindowY\": {0},\r\n", WindowY);
            sb.AppendFormat("  \"WindowWidth\": {0},\r\n", WindowWidth);
            sb.AppendFormat("  \"WindowHeight\": {0},\r\n", WindowHeight);
            sb.AppendFormat("  \"TopMost\": {0},\r\n", TopMost ? "true" : "false");
            string opacityStr = Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendFormat("  \"Opacity\": {0},\r\n", opacityStr);
            sb.AppendFormat("  \"SnapToEdges\": {0},\r\n", SnapToEdges ? "true" : "false");
            sb.AppendFormat("  \"SnapDistance\": {0},\r\n", SnapDistance);
            sb.AppendFormat("  \"ShowGraph\": {0},\r\n", ShowGraph ? "true" : "false");
            sb.AppendFormat("  \"ShowCoreBars\": {0},\r\n", ShowCoreBars ? "true" : "false");
            sb.AppendFormat("  \"GraphPeriodSeconds\": {0},\r\n", GraphPeriodSeconds);
            sb.AppendFormat("  \"UpdateIntervalMs\": {0},\r\n", UpdateIntervalMs);
            sb.AppendFormat("  \"AutoStart\": {0},\r\n", AutoStart ? "true" : "false");
            sb.AppendFormat("  \"CpuColorHex\": \"{0}\",\r\n", ColorToHex(GetCpuColor()));
            sb.AppendFormat("  \"GpuColorHex\": \"{0}\",\r\n", ColorToHex(GetGpuColor()));
            sb.AppendFormat("  \"RamColorHex\": \"{0}\",\r\n", ColorToHex(GetRamColor()));
            sb.AppendFormat("  \"PingColorHex\": \"{0}\",\r\n", ColorToHex(GetPingColor()));
            sb.AppendFormat("  \"NetDownColorHex\": \"{0}\",\r\n", ColorToHex(GetNetDownColor()));
            sb.AppendFormat("  \"NetUpColorHex\": \"{0}\",\r\n", ColorToHex(GetNetUpColor()));
            sb.AppendFormat("  \"TextColorHex\": \"{0}\",\r\n", ColorToHex(GetTextColor()));

            sb.Append("  \"PingTargets\": [");
            for (int i = 0; i < PingTargets.Count; i++)
            {
                sb.AppendFormat("\"{0}\"", PingTargets[i].Replace("\"", ""));
                if (i < PingTargets.Count - 1) sb.Append(", ");
            }
            sb.AppendLine("],");

            sb.AppendFormat("  \"ShowTopProcessesButton\": {0},\r\n", ShowTopProcessesButton ? "true" : "false");
            sb.AppendFormat("  \"TopProcessesCount\": {0},\r\n", TopProcessesCount);
            sb.AppendFormat("  \"TopProcessesWindowMinutes\": {0},\r\n", TopProcessesWindowMinutes);
            sb.AppendFormat("  \"TopProcessesPollIntervalSec\": {0},\r\n", TopProcessesPollIntervalSec);
            sb.AppendFormat("  \"TopProcessesIncludeExited\": {0}\r\n", TopProcessesIncludeExited ? "true" : "false");

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void ParseJson(string json, AppSettings s)
        {
            string[] lines = json.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim().TrimEnd(',');
                int colonIdx = line.IndexOf(':');
                if (colonIdx <= 0) continue;

                string key = line.Substring(0, colonIdx).Trim(' ', '"');
                string val = line.Substring(colonIdx + 1).Trim(' ', '"');

                int intVal;
                double dblVal;
                bool boolVal;

                switch (key)
                {
                    case "WindowX":
                        if (int.TryParse(val, out intVal)) s.WindowX = intVal;
                        break;
                    case "WindowY":
                        if (int.TryParse(val, out intVal)) s.WindowY = intVal;
                        break;
                    case "WindowWidth":
                        if (int.TryParse(val, out intVal)) s.WindowWidth = intVal;
                        break;
                    case "WindowHeight":
                        if (int.TryParse(val, out intVal)) s.WindowHeight = intVal;
                        break;
                    case "TopMost":
                        if (bool.TryParse(val, out boolVal)) s.TopMost = boolVal;
                        break;
                    case "Opacity":
                        val = val.Replace(',', '.');
                        if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dblVal))
                            s.Opacity = dblVal;
                        break;
                    case "SnapToEdges":
                        if (bool.TryParse(val, out boolVal)) s.SnapToEdges = boolVal;
                        break;
                    case "SnapDistance":
                        if (int.TryParse(val, out intVal)) s.SnapDistance = intVal;
                        break;
                    case "ShowGraph":
                        if (bool.TryParse(val, out boolVal)) s.ShowGraph = boolVal;
                        break;
                    case "ShowCoreBars":
                        if (bool.TryParse(val, out boolVal)) s.ShowCoreBars = boolVal;
                        break;
                    case "GraphPeriodSeconds":
                        if (int.TryParse(val, out intVal)) s.GraphPeriodSeconds = intVal;
                        break;
                    case "UpdateIntervalMs":
                        if (int.TryParse(val, out intVal)) s.UpdateIntervalMs = intVal;
                        break;
                    case "AutoStart":
                        if (bool.TryParse(val, out boolVal)) s.AutoStart = boolVal;
                        break;
                    case "CpuColorHex":
                        s.CpuColorHex = val;
                        break;
                    case "GpuColorHex":
                        s.GpuColorHex = val;
                        break;
                    case "RamColorHex":
                        s.RamColorHex = val;
                        break;
                    case "PingColorHex":
                        s.PingColorHex = val;
                        break;
                    case "NetDownColorHex":
                        s.NetDownColorHex = val;
                        break;
                    case "NetUpColorHex":
                        s.NetUpColorHex = val;
                        break;
                    case "TextColorHex":
                        s.TextColorHex = val;
                        break;
                    case "PingTargets":
                        s.PingTargets.Clear();
                        string arrayContent = val.Trim('[', ']');
                        string[] items = arrayContent.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string item in items)
                        {
                            string host = item.Trim(' ', '"', '\'');
                            if (!string.IsNullOrEmpty(host)) s.PingTargets.Add(host);
                        }
                        if (s.PingTargets.Count == 0) s.PingTargets.Add("8.8.8.8");
                        break;
                    case "ShowTopProcessesButton":
                        if (bool.TryParse(val, out boolVal)) s.ShowTopProcessesButton = boolVal;
                        break;
                    case "TopProcessesCount":
                        if (int.TryParse(val, out intVal)) s.TopProcessesCount = intVal;
                        break;
                    case "TopProcessesWindowMinutes":
                        if (int.TryParse(val, out intVal)) s.TopProcessesWindowMinutes = intVal;
                        break;
                    case "TopProcessesPollIntervalSec":
                        if (int.TryParse(val, out intVal)) s.TopProcessesPollIntervalSec = intVal;
                        break;
                    case "TopProcessesIncludeExited":
                        if (bool.TryParse(val, out boolVal)) s.TopProcessesIncludeExited = boolVal;
                        break;
                }
            }
        }
    }
}
