
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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HaDeskLink;

/// <summary>
/// Handles PC commands received from Home Assistant notifications.
/// </summary>
public static class CommandHandler
{
    public static void Execute(string command)
    {
        try
        {
            switch (command.ToLowerInvariant().Trim())
            {
                case "shutdown":
                    Run("systemctl", "poweroff");
                    break;
                case "restart":
                    Run("systemctl", "reboot");
                    break;
                case "hibernate":
                    Run("systemctl", "hibernate");
                    break;
                case "suspend":
                case "sleep":
                    Run("systemctl", "suspend");
                    break;
                case "lock":
                case "lock_screen":
                    // Primary: loginctl, fallback: xdg-screensaver
                    if (!TryRun("loginctl", "lock-session"))
                        TryRun("xdg-screensaver", "lock");
                    break;
                case "mute":
                    Run("amixer", "set Master mute");
                    break;
                case "volume_mute":
                    // Primary: amixer toggle, fallback: pactl toggle
                    if (!TryRun("amixer", "set Master toggle"))
                        TryRun("pactl", "set-sink-mute @DEFAULT_SINK@ toggle");
                    break;
                case "volume_up":
                    // Primary: amixer 5%+, fallback: pactl +5%
                    if (!TryRun("amixer", "set Master 5%+"))
                        TryRun("pactl", "set-sink-volume @DEFAULT_SINK@ +5%");
                    break;
                case "volume_down":
                    // Primary: amixer 5%-, fallback: pactl -5%
                    if (!TryRun("amixer", "set Master 5%-"))
                        TryRun("pactl", "set-sink-volume @DEFAULT_SINK@ -5%");
                    break;
                case "media_play_pause":
                    // Primary: xdotool, fallback: playerctl
                    if (!TryRun("xdotool", "key XF86AudioPlay"))
                        TryRun("playerctl", "play-pause");
                    break;
                case "media_next":
                    // Primary: xdotool, fallback: playerctl
                    if (!TryRun("xdotool", "key XF86AudioNext"))
                        TryRun("playerctl", "next");
                    break;
                case "media_previous":
                    // Primary: xdotool, fallback: playerctl
                    if (!TryRun("xdotool", "key XF86AudioPrev"))
                        TryRun("playerctl", "previous");
                    break;
                case "monitor_off":
                    Run("xset", "dpms force off");
                    break;
                case "monitor_on":
                    Run("xset", "dpms force on");
                    break;
                case "screenshot":
                case "screenshot_save":
                    TakeAndSaveScreenshot();
                    break;
                case "snipping_tool":
                    // Not available on Linux
                    break;
                case "brightness_up":
                    {
                        var current = SensorManager.GetCurrentBrightness();
                        if (current.HasValue)
                            SensorManager.SetBrightness(Math.Min(100, current.Value + 10));
                    }
                    break;
                case "brightness_down":
                    {
                        var current = SensorManager.GetCurrentBrightness();
                        if (current.HasValue)
                            SensorManager.SetBrightness(Math.Max(0, current.Value - 10));
                    }
                    break;
                default:
                    // Check for brightness value command: "brightness:50"
                    if (command.StartsWith("brightness:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(command.Substring("brightness:".Length), out int value))
                            SensorManager.SetBrightness(Math.Clamp(value, 0, 100));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CommandHandler] Error executing '{command}': {ex.Message}");
        }
    }

    private static void Run(string cmd, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi)?.WaitForExit(5000);
    }

    private static bool TryRun(string cmd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TakeAndSaveScreenshot()
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "ha_desklink");
            Directory.CreateDirectory(tempPath);
            var filePath = Path.Combine(tempPath, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            // Try gnome-screenshot first, then scrot, then grim (Wayland)
            var captured = false;

            // gnome-screenshot
            try
            {
                var psi = new ProcessStartInfo("gnome-screenshot", $"-f \"{filePath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                if (proc?.ExitCode == 0 && File.Exists(filePath)) captured = true;
            }
            catch { }

            // scrot (X11)
            if (!captured)
            {
                try
                {
                    var psi = new ProcessStartInfo("scrot", $"\"{filePath}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                    if (proc?.ExitCode == 0 && File.Exists(filePath)) captured = true;
                }
                catch { }
            }

            // grim (Wayland)
            if (!captured)
            {
                try
                {
                    var psi = new ProcessStartInfo("grim", filePath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                    if (proc?.ExitCode == 0 && File.Exists(filePath)) captured = true;
                }
                catch { }
            }

            if (captured)
            {
                Console.WriteLine($"[Screenshot] Saved: {filePath}");
                // Upload to HA asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        var config = Config.Load();
                        var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
                        api.LoadRegistration();
                        await api.UploadScreenshotAsync(filePath);
                        Console.WriteLine("[Screenshot] Uploaded to HA");
                    }
                    catch (Exception ex) { Console.WriteLine($"[Screenshot] Upload failed: {ex.Message}"); }
                });
            }
            else
            {
                Console.WriteLine("[Screenshot] Failed: no screenshot tool available (install gnome-screenshot, scrot, or grim)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Screenshot] Error: {ex.Message}");
        }
    }
}