#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Text.RegularExpressions;

namespace HaDeskLink;

/// <summary>
/// Collects system sensor data on Linux using /sys, /proc, and command-line tools.
/// </summary>
public class SensorManager
{
    public List<SensorData> CollectAll()
    {
        var sensors = new List<SensorData>();

        sensors.AddRange(GetCpuSensors());
        sensors.AddRange(GetMemorySensors());
        sensors.AddRange(GetDiskSensors());
        sensors.Add(GetUptime());
        sensors.Add(GetConnectivity());
        sensors.Add(GetProcessCount());
        sensors.Add(GetIpAddress());

        var battery = GetBattery();
        if (battery != null) sensors.Add(battery);

        var tempSensors = GetHwmonSensors();
        sensors.AddRange(tempSensors);

        var fanSensors = GetFanSensors();
        sensors.AddRange(fanSensors);

        var wifi = GetWifiSsid();
        if (wifi != null) sensors.Add(wifi);

        return sensors;
    }

    private List<SensorData> GetCpuSensors()
    {
        var result = new List<SensorData>();
        try
        {
            // CPU usage from /proc/stat
            var stat1 = File.ReadAllText("/proc/stat").Split('\n')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Thread.Sleep(100);
            var stat2 = File.ReadAllText("/proc/stat").Split('\n')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var idle1 = double.Parse(stat1[3]) + double.Parse(stat1[4]); // idle + iowait
            var total1 = stat1.Skip(1).Sum(s => double.Parse(s));
            var idle2 = double.Parse(stat2[3]) + double.Parse(stat2[4]);
            var total2 = stat2.Skip(1).Sum(s => double.Parse(s));

            var usage = Math.Round((1 - (idle2 - idle1) / (total2 - total1)) * 100, 1);
            result.Add(new SensorData("cpu_percent", "CPU Usage", usage, "%",
                icon: "mdi:cpu-64-bit", stateClass: "measurement"));

            // CPU temperature
            var cpuTemp = ReadThermalZone();
            if (cpuTemp != null)
                result.Add(new SensorData("cpu_temperature", "CPU Temperature", cpuTemp.Value, "°C",
                    icon: "mdi:thermometer", stateClass: "measurement"));

            // CPU clock
            var freq = ReadCpuFreq();
            if (freq != null)
                result.Add(new SensorData("cpu_clock", "CPU Clock", freq.Value, "MHz",
                    icon: "mdi:speedometer", stateClass: "measurement"));
        }
        catch { }

        return result;
    }

    private List<SensorData> GetMemorySensors()
    {
        var result = new List<SensorData>();
        try
        {
            var info = File.ReadAllText("/proc/meminfo");
            var total = ParseMemValue(info, "MemTotal:");
            var available = ParseMemValue(info, "MemAvailable:");
            var used = total - available;
            var percent = total > 0 ? Math.Round((double)used / total * 100, 1) : 0;

            result.Add(new SensorData("memory_percent", "Memory Usage", percent, "%",
                icon: "mdi:memory", stateClass: "measurement"));
            result.Add(new SensorData("memory_used", "Memory Used", Math.Round(used / 1048576.0, 2), "GB",
                icon: "mdi:memory", stateClass: "measurement"));
            result.Add(new SensorData("memory_free", "Memory Free", Math.Round(available / 1048576.0, 2), "GB",
                icon: "mdi:memory", stateClass: "measurement"));
            result.Add(new SensorData("memory_total", "Memory Total", Math.Round(total / 1048576.0, 2), "GB",
                icon: "mdi:memory"));
        }
        catch { }
        return result;
    }

    private List<SensorData> GetDiskSensors()
    {
        var result = new List<SensorData>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var label = drive.Name.TrimEnd('/');
                var driveKey = label.Replace("/", "").ToLower();

                var total = (double)drive.TotalSize / (1024 * 1024 * 1024);
                var free = (double)drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                var used = total - free;
                var percent = Math.Round(used / total * 100, 1);

                result.Add(new SensorData($"disk_{driveKey}_percent", $"Disk {label} Usage",
                    percent, "%", icon: "mdi:harddisk", stateClass: "measurement"));
                result.Add(new SensorData($"disk_{driveKey}_free", $"Disk {label} Free",
                    Math.Round(free, 2), "GB", icon: "mdi:harddisk", stateClass: "measurement"));
                result.Add(new SensorData($"disk_{driveKey}_used", $"Disk {label} Used",
                    Math.Round(used, 2), "GB", icon: "mdi:harddisk", stateClass: "measurement"));
                result.Add(new SensorData($"disk_{driveKey}_total", $"Disk {label} Total",
                    Math.Round(total, 2), "GB", icon: "mdi:harddisk"));
            }
        }
        catch { }
        return result;
    }

    private static SensorData GetUptime()
    {
        try
        {
            var uptime = File.ReadAllText("/proc/uptime").Split(' ')[0];
            var seconds = double.Parse(uptime);
            var hours = Math.Round(seconds / 3600, 1);
            return new SensorData("uptime", "Uptime", hours, "h",
                icon: "mdi:clock-outline", stateClass: "measurement");
        }
        catch { return new SensorData("uptime", "Uptime", 0, "h", icon: "mdi:clock-outline"); }
    }

    private static SensorData GetConnectivity()
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send("8.8.8.8", 2000);
            if (reply.Status == IPStatus.Success)
                return new SensorData("connectivity", "Connectivity", "on",
                    deviceClass: "connectivity", icon: "mdi:check-network");
        }
        catch { }
        return new SensorData("connectivity", "Connectivity", "off",
            deviceClass: "connectivity", icon: "mdi:close-network");
    }

    private static SensorData GetProcessCount()
    {
        try
        {
            var count = Directory.GetDirectories("/proc").Count(d => int.TryParse(Path.GetFileName(d), out _));
            return new SensorData("process_count", "Running Processes", count, "",
                icon: "mdi:cog", stateClass: "measurement");
        }
        catch { return new SensorData("process_count", "Running Processes", 0, icon: "mdi:cog"); }
    }

    private static SensorData GetIpAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return new SensorData("ip_address", "IP Address", ip.Address.ToString(),
                            icon: "mdi:ip-network");
                }
            }
        }
        catch { }
        return new SensorData("ip_address", "IP Address", "unavailable", icon: "mdi:ip-network-off");
    }

    private static SensorData? GetBattery()
    {
        try
        {
            var batPath = "/sys/class/power_supply/BAT0";
            if (!Directory.Exists(batPath)) batPath = "/sys/class/power_supply/BAT1";
            if (!Directory.Exists(batPath)) return null;

            var capacity = File.ReadAllText(Path.Combine(batPath, "capacity")).Trim();
            var pct = int.Parse(capacity);
            return new SensorData("battery", "Battery", pct, "%",
                deviceClass: "battery", icon: "mdi:battery", stateClass: "measurement");
        }
        catch { return null; }
    }

    private List<SensorData> GetHwmonSensors()
    {
        var result = new List<SensorData>();
        try
        {
            foreach (var dir in Directory.GetDirectories("/sys/class/hwmon"))
            {
                var nameFile = Path.Combine(dir, "name");
                if (!File.Exists(nameFile)) continue;
                var hwmonName = File.ReadAllText(nameFile).Trim();

                foreach (var file in Directory.GetFiles(dir, "temp*_input"))
                {
                    var temp = double.Parse(File.ReadAllText(file).Trim()) / 1000.0;
                    var labelFile = file.Replace("_input", "_label");
                    var label = File.Exists(labelFile) ? File.ReadAllText(labelFile).Trim() : $"temp {Path.GetFileName(file)}";

                    if (hwmonName.Contains("cpu", StringComparison.OrdinalIgnoreCase) || hwmonName.Contains("k10temp", StringComparison.OrdinalIgnoreCase) || hwmonName.Contains("coretemp", StringComparison.OrdinalIgnoreCase))
                    {
                        // Already covered by cpu_temperature
                    }
                    else
                    {
                        var uid = $"hwmon_{hwmonName}_temp";
                        result.Add(new SensorData(uid, $"{hwmonName} {label}", Math.Round(temp, 1), "°C",
                            icon: "mdi:thermometer", stateClass: "measurement"));
                    }
                }
            }
        }
        catch { }
        return result;
    }

    private List<SensorData> GetFanSensors()
    {
        var result = new List<SensorData>();
        try
        {
            foreach (var dir in Directory.GetDirectories("/sys/class/hwmon"))
            {
                var nameFile = Path.Combine(dir, "name");
                if (!File.Exists(nameFile)) continue;
                var hwmonName = File.ReadAllText(nameFile).Trim();

                foreach (var file in Directory.GetFiles(dir, "fan*_input"))
                {
                    var rpm = int.Parse(File.ReadAllText(file).Trim());
                    var labelFile = file.Replace("_input", "_label");
                    var label = File.Exists(labelFile) ? File.ReadAllText(labelFile).Trim() : $"Fan {Path.GetFileName(file)}";
                    var uid = $"fan_{hwmonName}_{label.ToLowerInvariant().Replace(" ", "_")}";

                    result.Add(new SensorData(uid, $"{hwmonName} {label}", rpm, "RPM",
                        icon: "mdi:fan", stateClass: "measurement"));
                }
            }
        }
        catch { }
        return result;
    }

    private SensorData? GetWifiSsid()
    {
        try
        {
            var psi = new ProcessStartInfo("iwgetid", "-r")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var ssid = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit(3000);
            if (!string.IsNullOrEmpty(ssid))
                return new SensorData("wifi_ssid", "WiFi Network", ssid, icon: "mdi:wifi");
        }
        catch { }
        return null;
    }

    private static double? ReadThermalZone()
    {
        try
        {
            foreach (var zone in Directory.GetDirectories("/sys/class/thermal"))
            {
                var typeFile = Path.Combine(zone, "type");
                if (!File.Exists(typeFile)) continue;
                var type = File.ReadAllText(typeFile).Trim();
                if (type.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("x86", StringComparison.OrdinalIgnoreCase) ||
                    type.Contains("acpi", StringComparison.OrdinalIgnoreCase))
                {
                    var tempFile = Path.Combine(zone, "temp");
                    if (File.Exists(tempFile))
                        return Math.Round(double.Parse(File.ReadAllText(tempFile).Trim()) / 1000.0, 1);
                }
            }
            // Fallback: first thermal zone
            var firstTemp = "/sys/class/thermal/thermal_zone0/temp";
            if (File.Exists(firstTemp))
                return Math.Round(double.Parse(File.ReadAllText(firstTemp).Trim()) / 1000.0, 1);
        }
        catch { }
        return null;
    }

    private static double? ReadCpuFreq()
    {
        try
        {
            var freq = File.ReadAllText("/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq").Trim();
            return Math.Round(double.Parse(freq) / 1000.0, 0); // kHz -> MHz
        }
        catch { return null; }
    }

    private static double ParseMemValue(string info, string key)
    {
        var match = Regex.Match(info, $@"{key}\s+(\d+)");
        return match.Success ? double.Parse(match.Groups[1].Value) : 0;
    }
}