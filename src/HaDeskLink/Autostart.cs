// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License v3 as published
// by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
#nullable enable
using System;
using System.IO;

namespace HaDeskLink;

/// <summary>
/// Manages Linux autostart via the XDG autostart specification
/// (~/.config/autostart/ha-desklink.desktop). This is the Linux-native
/// equivalent of the Windows Task Scheduler/registry Autostart.
/// Note: the daemon setup via systemd (ha-desklink.service) is unaffected.
/// </summary>
public static class Autostart
{
    private const string DesktopFileName = "ha-desklink.desktop";
    private static readonly string AutostartDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");

    private static string DesktopFilePath => Path.Combine(AutostartDir, DesktopFileName);

    public static bool IsEnabled() => File.Exists(DesktopFilePath);

    public static void Enable()
    {
        try
        {
            Directory.CreateDirectory(AutostartDir);
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ha-desklink");

            // XDG autostart desktop entry
            var content = "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=HA DeskLink\n" +
                "Comment=Home Assistant Companion App\n" +
                $"Exec={exePath}\n" +
                "Terminal=false\n" +
                "X-GNOME-Autostart-enabled=true\n" +
                "Categories=Utility;\n";
            File.WriteAllText(DesktopFilePath, content);
            TrySetExecutablePermissions();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Autostart] Enable failed: {ex.Message}");
        }
    }

    public static void Disable()
    {
        try
        {
            if (File.Exists(DesktopFilePath))
                File.Delete(DesktopFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Autostart] Disable failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Set file permissions to user-readable (0600) where the API is available.
    /// </summary>
    private static void TrySetExecutablePermissions()
    {
        try
        {
#pragma warning disable CA1416
#if LINUX
            File.SetUnixFileMode(DesktopFilePath, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);
#endif
#pragma warning restore CA1416
        }
        catch { }
    }
}