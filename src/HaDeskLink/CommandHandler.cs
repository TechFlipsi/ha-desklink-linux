
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
using System.Runtime.InteropServices;
using System.Text.Json;
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
                    var cmdLower = command.ToLowerInvariant().Trim();
                    // TTS (Text-to-Speech): "tts:Hallo Welt"
                    if (cmdLower.StartsWith("tts:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Text aus originalen command extrahieren (Groß-/Kleinschreibung erhalten)
                        // Finde "tts:" im originalen command (case-insensitive)
                        var ttsIdx = command.IndexOf("tts:", StringComparison.OrdinalIgnoreCase);
                        var text = ttsIdx >= 0 ? command.Substring(ttsIdx + 4).Trim() : "";
                        SpeakText(text);
                    }
                    // App Launcher: "launch:spotify"
                    else if (cmdLower.StartsWith("launch:", StringComparison.OrdinalIgnoreCase))
                    {
                        var appCommand = cmdLower.Substring(7).Trim();
                        LaunchApp(appCommand);
                    }
                    // Check for brightness value command: "brightness:50"
                    else if (cmdLower.StartsWith("brightness:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(cmdLower.Substring("brightness:".Length), out int value))
                            SensorManager.SetBrightness(Math.Clamp(value, 0, 100));
                    }
                    // Custom Commands: prüfe ob der Command in der CustomCommands-Liste ist
                    else if (TryExecuteCustomCommand(cmdLower))
                    {
                        // Wurde bereits in TryExecuteCustomCommand ausgeführt
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
                // Upload to HA asynchronously, then clean up temp file
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
                    finally
                    {
                        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
                    }
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

    // ─────────────────────────────────────────────────────────────────
    //  TTS (Text-to-Speech) — Linux
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spricht Text über espeak (Primary) oder spd-say (Fallback).
    /// Der Text wird sicher als separates Argument übergeben, um
    /// Command-Injection zu verhindern.
    /// </summary>
    private static void SpeakText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Primary: espeak
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "espeak",
                Arguments = $"\"{text.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            return;
        }
        catch { /* espeak nicht verfügbar */ }

        // Fallback: spd-say
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "spd-say",
                Arguments = $"\"{text.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] Weder espeak noch spd-say verfügbar: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  App Launcher — Linux
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Startet eine App anhand des konfigurierten AppLauncher-Commands.
    /// Sucht in AppLaunchers Config nach dem command und startet den Pfad.
    /// </summary>
    private static void LaunchApp(string appCommand)
    {
        if (string.IsNullOrWhiteSpace(appCommand)) return;

        try
        {
            var config = Config.Load();
            var launchers = JsonSerializer.Deserialize<List<AppLauncherEntry>>(config.AppLaunchers);
            if (launchers == null) return;

            var entry = launchers.Find(l =>
                string.Equals(l.Command, appCommand, StringComparison.OrdinalIgnoreCase));
            if (entry == null || string.IsNullOrEmpty(entry.Path))
            {
                Console.WriteLine($"[AppLauncher] '{appCommand}' nicht gefunden");
                return;
            }

            // Linux: direkter Start; bash -c wenn Pfad Leerzeichen enthält
            if (entry.Path.Contains(' '))
            {
                Run("bash", $"-c \"{entry.Path.Replace("\"", "\\\"")}\"");
            }
            else
            {
                try { Process.Start(entry.Path); }
                catch
                {
                    // Fallback: über bash versuchen (z.B. bei .desktop-Dateien oder Kommandozeilen-Tools)
                    Run("bash", $"-c \"{entry.Path.Replace("\"", "\\\"")}\"");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppLauncher] Fehler: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Custom Commands — Linux
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prüft ob der Command in der CustomCommands-Liste der Config ist.
    /// Wenn ja, wird das konfigurierte Skript ausgeführt.
    /// </summary>
    /// <returns>true wenn der Command gefunden und ausgeführt wurde</returns>
    private static bool TryExecuteCustomCommand(string command)
    {
        try
        {
            var config = Config.Load();
            var customCommands = JsonSerializer.Deserialize<List<CustomCommandEntry>>(config.CustomCommands);
            if (customCommands == null || customCommands.Count == 0) return false;

            var entry = customCommands.Find(c =>
                string.Equals(c.Command, command, StringComparison.OrdinalIgnoreCase));
            if (entry == null || string.IsNullOrEmpty(entry.Script)) return false;

            // Linux: bash -c script
            Run("bash", $"-c \"{entry.Script.Replace("\"", "\\\"")}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  JSON Modelle für Custom Commands und App Launchers
    // ─────────────────────────────────────────────────────────────────

    private class CustomCommandEntry
    {
        public string Command { get; set; } = "";
        public string Script { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private class AppLauncherEntry
    {
        public string Command { get; set; } = "";
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
    }
}