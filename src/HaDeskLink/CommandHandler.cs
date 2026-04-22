
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
using System.Runtime.InteropServices;

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
                    Run("systemctl", "suspend");
                    break;
                case "lock":
                    Run("loginctl", "lock-session");
                    break;
                case "mute":
                    Run("amixer", "set Master mute");
                    break;
                case "volume_up":
                    Run("amixer", "set Master 10%+");
                    break;
                case "volume_down":
                    Run("amixer", "set Master 10%-");
                    break;
                case "monitor_off":
                    Run("xset", "dpms force off");
                    break;
                case "monitor_on":
                    Run("xset", "dpms force on");
                    break;
                case "screenshot":
                    // Not implemented on Linux headless
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
}