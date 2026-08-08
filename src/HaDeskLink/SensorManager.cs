
// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License v3 as published by
// the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
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
    // Network throughput tracking
    private static long _prevNetRxBytes = 0;
    private static long _prevNetTxBytes = 0;
    private static DateTime _prevNetTime = DateTime.MinValue;

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

        // Fullscreen sensor
        var fullscreen = GetFullscreenInfo();
        if (fullscreen != null) sensors.Add(fullscreen);

        // Monitor layout
        sensors.Add(GetMonitorLayout());

        // Brightness
        var brightness = GetBrightness();
        if (brightness != null) sensors.Add(brightness);

        // Webcam active sensor
        var webcam = GetWebcamActive();
        if (webcam != null) sensors.Add(webcam);

        // Idle time
        var idleTime = GetIdleTime();
        if (idleTime != null) sensors.Add(idleTime);

        // Presence Detection (binary_sensor: on wenn idle_time < 300s UND connectivity = on)
        var presence = GetPresence();
        if (presence != null) sensors.Add(presence);

        // Active window
        var activeWindow = GetActiveWindow();
        if (activeWindow != null) sensors.Add(activeWindow);

        // Audio volume
        var audioVolume = GetAudioVolume();
        if (audioVolume != null) sensors.Add(audioVolume);

        // Audio mute
        var audioMute = GetAudioMute();
        if (audioMute != null) sensors.Add(audioMute);

        // Microphone active
        var micActive = GetMicActive();
        if (micActive != null) sensors.Add(micActive);

        // GPU sensors
        sensors.AddRange(GetGpuSensors());

        // Network throughput
        sensors.AddRange(GetNetworkThroughput());

        // Bluetooth devices connected (Anzahl verbundener Geräte)
        var bluetooth = GetBluetoothDevices();
        if (bluetooth != null) sensors.Add(bluetooth);

        // App version
        sensors.Add(GetAppVersion());

        // PC status (binary_sensor: "on" while app is running)
        var pcStatus = new SensorData("pc_status", "PC Status", "on",
            deviceClass: "connectivity", icon: "mdi:desktop-classic")
        {
            SensorKind = SensorType.BinarySensor,
            EntityCategory = null
        };
        sensors.Add(pcStatus);

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
                var driveKey = (label == "/" ? "root" : label.Replace("/", "").ToLower());

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
            // Ping HA URL host instead of hardcoded 8.8.8.8 — works in isolated networks
            var pingHost = "8.8.8.8";
            try
            {
                var config = Config.Load();
                if (!string.IsNullOrEmpty(config.HaUrl) && Uri.TryCreate(config.HaUrl, UriKind.Absolute, out var haUri))
                {
                    pingHost = haUri.Host;
                }
            }
            catch { }

            using var ping = new Ping();
            var reply = ping.Send(pingHost, 2000);
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

                    // Extract index from filename (e.g., temp1_input -> 1) to create unique IDs
                    var idxMatch = Regex.Match(Path.GetFileName(file), @"temp(\d+)_input");
                    var idx = idxMatch.Success ? idxMatch.Groups[1].Value : "";

                    if (hwmonName.Contains("cpu", StringComparison.OrdinalIgnoreCase) || hwmonName.Contains("k10temp", StringComparison.OrdinalIgnoreCase) || hwmonName.Contains("coretemp", StringComparison.OrdinalIgnoreCase))
                    {
                        // Already covered by cpu_temperature
                    }
                    else
                    {
                        var uid = $"hwmon_{hwmonName}_temp{idx}";
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

    // === Fullscreen detection (X11 only) ===
    private SensorData? GetFullscreenInfo()
    {
        try
        {
            // xdotool + xprop to detect fullscreen window
            var psi = new ProcessStartInfo("xdotool", "getactivewindow")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var windowId = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit(2000);

            if (string.IsNullOrEmpty(windowId) || !long.TryParse(windowId, out _))
                return new SensorData("fullscreen", "Fullscreen", "off", icon: "mdi:fullscreen", stateClass: "measurement");

            // Get window state
            var psi2 = new ProcessStartInfo("xprop", $"-id {windowId} _NET_WM_STATE")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc2 = Process.Start(psi2);
            var state = proc2?.StandardOutput.ReadToEnd().Trim() ?? "";
            proc2?.WaitForExit(2000);

            var isFullscreen = state.Contains("_NET_WM_STATE_FULLSCREEN");

            return new SensorData("fullscreen", "Fullscreen", isFullscreen ? "on" : "off", icon: "mdi:fullscreen", stateClass: "measurement");
        }
        catch
        {
            // Wayland or xdotool not available
            return new SensorData("fullscreen", "Fullscreen", "unavailable", icon: "mdi:fullscreen");
        }
    }

    // === Monitor Layout ===
    private static SensorData GetMonitorLayout()
    {
        try
        {
            var psi = new ProcessStartInfo("xrandr", "--query")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(3000);

            // Count connected monitors
            var count = 0;
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains(" connected"))
                    count++;
            }

            var layout = count <= 1 ? "1" : string.Join("+", Enumerable.Range(1, count));
            return new SensorData("monitor_layout", "Monitor Layout", layout, icon: "mdi:monitor-multiple");
        }
        catch
        {
            return new SensorData("monitor_layout", "Monitor Layout", "unknown", icon: "mdi:monitor-multiple");
        }
    }

    // === Brightness ===
    private static SensorData? GetBrightness()
    {
        try
        {
            // Try brightnessctl first
            var psi = new ProcessStartInfo("brightnessctl", "info")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(2000);

            // Parse: "(XX%)" from brightnessctl output
            var match = Regex.Match(output, @"\((\d+)%\)");
            if (match.Success)
            {
                var pct = int.Parse(match.Groups[1].Value);
                return new SensorData("brightness", "Brightness", pct, "%",
                    deviceClass: "illuminance", icon: "mdi:brightness-6", stateClass: "measurement");
            }
        }
        catch { }

        // Fallback: try /sys/class/backlight
        try
        {
            var backlightDir = "/sys/class/backlight";
            if (Directory.Exists(backlightDir))
            {
                foreach (var dir in Directory.GetDirectories(backlightDir))
                {
                    var maxFile = Path.Combine(dir, "max_brightness");
                    var curFile = Path.Combine(dir, "brightness");
                    if (File.Exists(maxFile) && File.Exists(curFile))
                    {
                        var max = int.Parse(File.ReadAllText(maxFile).Trim());
                        var cur = int.Parse(File.ReadAllText(curFile).Trim());
                        var pct = max > 0 ? (int)Math.Round((double)cur / max * 100) : 0;
                        return new SensorData("brightness", "Brightness", pct, "%",
                            deviceClass: "illuminance", icon: "mdi:brightness-6", stateClass: "measurement");
                    }
                }
            }
        }
        catch { }

        return null;
    }

    // === Brightness control ===
    public static void SetBrightness(int targetBrightness)
    {
        try
        {
            // Try brightnessctl
            var psi = new ProcessStartInfo("brightnessctl", $"set {targetBrightness}%")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
            if (proc?.ExitCode == 0) return;
        }
        catch { }

        // Fallback: xrandr
        try
        {
            var level = Math.Clamp(targetBrightness / 100.0, 0.1, 1.0);
            var psi = new ProcessStartInfo("xrandr", $"--output $(xrandr --query | grep ' connected' | head -1 | cut -d' ' -f1) --brightness {level:F2}")
            {
                UseShellExecute = true,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit(2000);
        }
        catch { }
    }

    public static int? GetCurrentBrightness()
    {
        try
        {
            var psi = new ProcessStartInfo("brightnessctl", "info")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(2000);
            var match = Regex.Match(output, @"\((\d+)%\)");
            if (match.Success) return int.Parse(match.Groups[1].Value);
        }
        catch { }

        // Fallback: /sys/class/backlight
        try
        {
            var backlightDir = "/sys/class/backlight";
            if (Directory.Exists(backlightDir))
            {
                foreach (var dir in Directory.GetDirectories(backlightDir))
                {
                    var maxFile = Path.Combine(dir, "max_brightness");
                    var curFile = Path.Combine(dir, "brightness");
                    if (File.Exists(maxFile) && File.Exists(curFile))
                    {
                        var max = int.Parse(File.ReadAllText(maxFile).Trim());
                        var cur = int.Parse(File.ReadAllText(curFile).Trim());
                        return max > 0 ? (int)Math.Round((double)cur / max * 100) : 0;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    // === Webcam Active Sensor ===
    private static SensorData? GetWebcamActive()
    {
        try
        {
            // Check /dev/video* devices - if any exist, webcam is present
            // Check if any process has them open via /proc/*/fd/*
            var videoDevices = Directory.GetFiles("/dev", "video*");
            if (videoDevices.Length == 0) return null;

            // Check if any process has a video device open
            bool inUse = false;
            try
            {
                foreach (var procDir in Directory.GetDirectories("/proc"))
                {
                    if (!int.TryParse(Path.GetFileName(procDir), out _)) continue;
                    var fdDir = Path.Combine(procDir, "fd");
                    if (!Directory.Exists(fdDir)) continue;
                    foreach (var fd in Directory.GetFiles(fdDir))
                    {
                        try
                        {
                            var target = ReadSymlink(fd);
                            if (target.StartsWith("/dev/video"))
                            {
                                inUse = true;
                                break;
                            }
                        }
                        catch { }
                    }
                    if (inUse) break;
                }
            }
            catch { }

            return new SensorData("webcam_active", "Webcam Active",
                inUse ? "on" : "off", icon: "mdi:webcam", stateClass: "measurement");
        }
        catch { return null; }
    }

    private static string ReadSymlink(string path)
    {
        try
        {
            // Use .NET 8 native API instead of spawning a process
            var target = File.ResolveLinkTarget(path, returnFinalTarget: false);
            return target?.FullName ?? "";
        }
        catch { return ""; }
    }

    // === Idle Time ===
    private static SensorData? GetIdleTime()
    {
        try
        {
            // Primary: xprintidle (returns ms)
            var psi = new ProcessStartInfo("xprintidle")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit(2000);

            if (!string.IsNullOrEmpty(output) && double.TryParse(output, out var ms))
                return new SensorData("idle_time", "Idle Time", Math.Round(ms / 1000.0, 1), "s",
                    icon: "mdi:timer-outline", stateClass: "measurement");
        }
        catch { }

        // Fallback: try xdotool (unreliable for idle, but available)
        try
        {
            var psi = new ProcessStartInfo("xdotool", "getactivewindow")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.StandardOutput.ReadToEnd();
            proc?.WaitForExit(2000);
            // xdotool available but xprintidle wasn't; return 0
            if (proc?.ExitCode == 0)
                return new SensorData("idle_time", "Idle Time", 0.0, "s",
                    icon: "mdi:timer-outline", stateClass: "measurement");
        }
        catch { }

        return null;
    }

    // === Active Window ===
    private static SensorData? GetActiveWindow()
    {
        try
        {
            // Primary: xdotool
            var psi = new ProcessStartInfo("xdotool", "getwindowfocus getwindowname")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var title = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit(2000);

            if (!string.IsNullOrEmpty(title))
                return new SensorData("active_window", "Active Window", title,
                    icon: "mdi:window-maximize");
        }
        catch { }

        // Fallback: xprop
        try
        {
            // Get active window ID
            var psi1 = new ProcessStartInfo("xprop", "-root _NET_ACTIVE_WINDOW")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc1 = Process.Start(psi1);
            var output1 = proc1?.StandardOutput.ReadToEnd().Trim() ?? "";
            proc1?.WaitForExit(2000);

            var match = Regex.Match(output1, @"window id # (0x[0-9a-fA-F]+)");
            if (!match.Success) return null;
            var windowId = match.Groups[1].Value;

            var psi2 = new ProcessStartInfo("xprop", $"-id {windowId} _NET_WM_NAME")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc2 = Process.Start(psi2);
            var output2 = proc2?.StandardOutput.ReadToEnd().Trim() ?? "";
            proc2?.WaitForExit(2000);

            var nameMatch = Regex.Match(output2, @"_NET_WM_NAME.* = ""(.+)""");
            if (nameMatch.Success)
                return new SensorData("active_window", "Active Window", nameMatch.Groups[1].Value,
                    icon: "mdi:window-maximize");
        }
        catch { }

        return null;
    }

    // === Audio Volume ===
    private static SensorData? GetAudioVolume()
    {
        try
        {
            // Primary: amixer
            var psi = new ProcessStartInfo("amixer", "get Master")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(2000);

            var match = Regex.Match(output, @"\[(\d+)%\]");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var vol))
                return new SensorData("audio_volume", "Audio Volume", vol, "%",
                    icon: "mdi:volume-high", stateClass: "measurement");
        }
        catch { }

        // Fallback: pactl
        try
        {
            var psi = new ProcessStartInfo("pactl", "get-sink-volume @DEFAULT_SINK@")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(2000);

            var match = Regex.Match(output, @"(\d+)%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var vol))
                return new SensorData("audio_volume", "Audio Volume", vol, "%",
                    icon: "mdi:volume-high", stateClass: "measurement");
        }
        catch { }

        return null;
    }

    // === Audio Mute ===
    private static SensorData? GetAudioMute()
    {
        try
        {
            // Primary: amixer
            var psi = new ProcessStartInfo("amixer", "get Master")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(2000);

            var isMuted = output.Contains("[off]");
            return new SensorData("audio_mute", "Audio Mute", isMuted ? "on" : "off",
                icon: "mdi:volume-off", deviceClass: "plug");
        }
        catch { }

        // Fallback: pactl
        try
        {
            var psi = new ProcessStartInfo("pactl", "get-sink-mute @DEFAULT_SINK@")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(2000);

            var isMuted = output.Contains("yes");
            return new SensorData("audio_mute", "Audio Mute", isMuted ? "on" : "off",
                icon: "mdi:volume-off", deviceClass: "plug");
        }
        catch { }

        return null;
    }

    // === Microphone Active ===
    private static SensorData? GetMicActive()
    {
        try
        {
            // Check /proc/asound for active capture streams
            var statusFiles = Directory.GetFiles("/proc/asound", "status", SearchOption.AllDirectories);
            foreach (var file in statusFiles)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains("capture") && !content.Contains("closed"))
                        return new SensorData("mic_active", "Microphone Active", "on",
                            icon: "mdi:microphone", deviceClass: "plug");
                }
                catch { }
            }
        }
        catch { }

        // Fallback: pactl
        try
        {
            var psi = new ProcessStartInfo("pactl", "list source-outputs")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd() ?? "";
            proc?.WaitForExit(2000);

            // If there are any source-outputs, something is recording
            var hasOutputs = output.Contains("Source Output #");
            return new SensorData("mic_active", "Microphone Active", hasOutputs ? "on" : "off",
                icon: "mdi:microphone", deviceClass: "plug");
        }
        catch { }

        return null;
    }

    // === GPU Sensors ===
    private static List<SensorData> GetGpuSensors()
    {
        var result = new List<SensorData>();

        // Try NVIDIA first
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit(3000);

            if (!string.IsNullOrEmpty(output))
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim().Split(',');
                    if (parts.Length >= 3 &&
                        double.TryParse(parts[0].Trim(), out var gpuLoad) &&
                        double.TryParse(parts[1].Trim(), out var memUsed) &&
                        double.TryParse(parts[2].Trim(), out var memTotal))
                    {
                        result.Add(new SensorData("gpu_load", "GPU Load", gpuLoad, "%",
                            icon: "mdi:expansion-card", stateClass: "measurement"));
                        result.Add(new SensorData("gpu_memory_used", "GPU Memory Used", Math.Round(memUsed, 1), "MB",
                            icon: "mdi:memory", stateClass: "measurement"));
                        result.Add(new SensorData("gpu_memory_total", "GPU Memory Total", Math.Round(memTotal, 1), "MB",
                            icon: "mdi:memory"));
                        return result;
                    }
                }
            }
        }
        catch { }

        // Try AMD
        try
        {
            var drmPath = "/sys/class/drm";
            if (Directory.Exists(drmPath))
            {
                foreach (var cardDir in Directory.GetDirectories(drmPath, "card*"))
                {
                    var devicePath = Path.Combine(cardDir, "device");
                    if (!Directory.Exists(devicePath)) continue;

                    var gpuBusyFile = Path.Combine(devicePath, "gpu_busy_percent");
                    var memUsedFile = Path.Combine(devicePath, "mem_info_vram_used");
                    var memTotalFile = Path.Combine(devicePath, "mem_info_vram_total");

                    if (File.Exists(gpuBusyFile))
                    {
                        var gpuLoad = double.Parse(File.ReadAllText(gpuBusyFile).Trim());
                        result.Add(new SensorData("gpu_load", "GPU Load", gpuLoad, "%",
                            icon: "mdi:expansion-card", stateClass: "measurement"));
                    }

                    if (File.Exists(memUsedFile) && File.Exists(memTotalFile))
                    {
                        var memUsed = double.Parse(File.ReadAllText(memUsedFile).Trim()) / (1024.0 * 1024.0);
                        var memTotal = double.Parse(File.ReadAllText(memTotalFile).Trim()) / (1024.0 * 1024.0);
                        result.Add(new SensorData("gpu_memory_used", "GPU Memory Used", Math.Round(memUsed, 1), "MB",
                            icon: "mdi:memory", stateClass: "measurement"));
                        result.Add(new SensorData("gpu_memory_total", "GPU Memory Total", Math.Round(memTotal, 1), "MB",
                            icon: "mdi:memory"));
                    }

                    if (result.Count > 0) return result;
                }
            }
        }
        catch { }

        return result;
    }

    // === Network Throughput ===
    private static List<SensorData> GetNetworkThroughput()
    {
        var result = new List<SensorData>();
        try
        {
            var now = DateTime.UtcNow;
            var netDev = File.ReadAllText("/proc/net/dev");
            long totalRx = 0, totalTx = 0;

            foreach (var line in netDev.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("Inter-") || trimmed.StartsWith("face"))
                    continue;

                // Skip loopback
                if (trimmed.StartsWith("lo:")) continue;

                var parts = Regex.Split(trimmed, @"\s+");
                if (parts.Length >= 10)
                {
                    if (long.TryParse(parts[1], out var rx))
                        totalRx += rx;
                    if (long.TryParse(parts[9], out var tx))
                        totalTx += tx;
                }
            }

            if (_prevNetTime != DateTime.MinValue)
            {
                var elapsed = (now - _prevNetTime).TotalSeconds;
                if (elapsed > 0)
                {
                    var rxDelta = (totalRx - _prevNetRxBytes) / elapsed / 1024.0;
                    var txDelta = (totalTx - _prevNetTxBytes) / elapsed / 1024.0;

                    result.Add(new SensorData("network_download", "Network Download",
                        Math.Round(rxDelta, 2), "KB/s",
                        icon: "mdi:download", stateClass: "measurement"));
                    result.Add(new SensorData("network_upload", "Network Upload",
                        Math.Round(txDelta, 2), "KB/s",
                        icon: "mdi:upload", stateClass: "measurement"));
                }
            }

            _prevNetRxBytes = totalRx;
            _prevNetTxBytes = totalTx;
            _prevNetTime = now;
        }
        catch { }

        return result;
    }

    private static SensorData GetAppVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return new SensorData("ha_desklink_version", "HA DeskLink Version",
            version, icon: "mdi:information-outline");
    }

    // ─────────────────────────────────────────────────────────────────
    //  Presence Detection (binary_sensor)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Presence Detection: Kombiniert idle_time und connectivity.
    /// Sensor ist "on" wenn idle_time &lt; 300 Sekunden UND connectivity = on.
    /// </summary>
    private static SensorData? GetPresence()
    {
        try
        {
            // Idle time über xprintidle holen (in ms)
            double idleMs = 0;
            try
            {
                var psi = new ProcessStartInfo("xprintidle")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd().Trim();
                proc?.WaitForExit(2000);
                if (!string.IsNullOrEmpty(output) && double.TryParse(output, out var ms))
                    idleMs = ms;
            }
            catch { }

            var idleSeconds = idleMs / 1000.0;
            var isIdle = idleSeconds < 300;

            // Connectivity prüfen (Ping wie GetConnectivity)
            var isOnline = false;
            try
            {
                var pingHost = "8.8.8.8";
                try
                {
                    var config = Config.Load();
                    if (!string.IsNullOrEmpty(config.HaUrl) && Uri.TryCreate(config.HaUrl, UriKind.Absolute, out var haUri))
                        pingHost = haUri.Host;
                }
                catch { }

                using var ping = new Ping();
                var reply = ping.Send(pingHost, 2000);
                isOnline = reply.Status == IPStatus.Success;
            }
            catch { }

            var isPresent = isIdle && isOnline ? "on" : "off";

            return new SensorData("presence", "Presence", isPresent,
                deviceClass: "presence", icon: "mdi:account-check")
            {
                SensorKind = SensorType.BinarySensor
            };
        }
        catch
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Bluetooth Devices (Anzahl verbundener Geräte)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zählt verbundene Bluetooth-Geräte über bluetoothctl.
    /// Gibt null zurück wenn Bluetooth nicht verfügbar ist.
    /// </summary>
    private static SensorData? GetBluetoothDevices()
    {
        try
        {
            // Primary: bluetoothctl devices Connected
            try
            {
                var psi = new ProcessStartInfo("bluetoothctl", "devices Connected")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(3000);

                // Zähle Zeilen die mit "Device " beginnen
                var count = 0;
                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("Device ", StringComparison.OrdinalIgnoreCase))
                        count++;
                }

                return new SensorData("bluetooth_devices_connected", "Bluetooth Devices Connected",
                    count, "",
                    icon: "mdi:bluetooth-connect", stateClass: "measurement");
            }
            catch { }

            // Fallback: hcitool con
            try
            {
                var psi = new ProcessStartInfo("hcitool", "con")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(3000);

                // Zähle Zeilen die mit "<" beginnen (verbundene Geräte)
                var count = 0;
                foreach (var line in output.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("<"))
                        count++;
                }

                return new SensorData("bluetooth_devices_connected", "Bluetooth Devices Connected",
                    count, "",
                    icon: "mdi:bluetooth-connect", stateClass: "measurement");
            }
            catch { }
        }
        catch { /* Bluetooth nicht verfügbar */ }

        return null;
    }
}