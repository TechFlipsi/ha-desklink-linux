
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
using System.Linq;
using System.Text.RegularExpressions;

namespace HaDeskLink;

/// <summary>
/// Shared model for now-playing media state across all platforms.
/// </summary>
public class MediaState
{
    public string State { get; set; } = "idle";  // idle, playing, paused
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Source { get; set; }           // App name (Spotify, Chrome, etc.)
    public int? Volume { get; set; }              // 0-100
    public bool? Muted { get; set; }
}

/// <summary>
/// Detects now-playing media on Linux using MPRIS D-Bus protocol.
/// Uses playerctl as the primary method, with dbus-send as fallback.
/// </summary>
public class MediaPlayer
{
    public MediaPlayer()
    {
    }

    /// <summary>
    /// Get the current media playback state via MPRIS D-Bus.
    /// Primary: playerctl (simpler, more compatible).
    /// Fallback: raw dbus-send commands.
    /// </summary>
    public MediaState GetCurrentMediaState()
    {
        // Strategy 1: Use playerctl if available (most reliable)
        try
        {
            var state = GetMediaStateViaPlayerctl();
            if (state != null && state.State != "idle")
                return state;
        }
        catch { }

        // Strategy 2: Use raw dbus-send commands
        try
        {
            var state = GetMediaStateViaDbus();
            if (state != null)
                return state;
        }
        catch { }

        return new MediaState { State = "idle" };
    }

    /// <summary>
    /// Use playerctl command-line tool (recommended, handles all MPRIS players).
    /// </summary>
    private static MediaState? GetMediaStateViaPlayerctl()
    {
        try
        {
            // Check if playerctl is available
            using var whichProc = Process.Start(new ProcessStartInfo("which", "playerctl")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            var whichOutput = whichProc?.StandardOutput.ReadToEnd()?.Trim();
            whichProc?.WaitForExit(2000);
            if (string.IsNullOrEmpty(whichOutput))
                return null;

            var state = new MediaState();

            // Get status from first available player
            string? playerBusName = null;

            // First, list all players and get the first active one
            try
            {
                var psi = new ProcessStartInfo("playerctl", "--all-players status")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(3000);

                // If no output, no players are running
                if (string.IsNullOrEmpty(output))
                    return new MediaState { State = "idle" };

                // Check status: Playing or Paused
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Equals("Playing", StringComparison.OrdinalIgnoreCase))
                    {
                        state.State = "playing";
                        break;
                    }
                    else if (trimmed.Equals("Paused", StringComparison.OrdinalIgnoreCase))
                    {
                        state.State = "paused";
                        break;
                    }
                }

                // If nothing is playing/paused, return idle
                if (state.State == "idle")
                    return state;
            }
            catch { return null; }

            // Get the bus name of the active player for source info
            try
            {
                var psi = new ProcessStartInfo("playerctl", "--all-players -l metadata mpris:trackid")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3000);

                    if (!string.IsNullOrEmpty(output))
                    {
                        // output is like "org.mpris.MediaPlayer2.spotify mpris:trackid ..."
                        var firstLine = output.Split('\n')[0].Trim();
                        var parts = firstLine.Split(' ', 2);
                        if (parts.Length > 0)
                        {
                            playerBusName = parts[0].Trim();
                        }
                    }
                }
            }
            catch { }

            // Get metadata (title, artist, album)
            state.Title = RunPlayerctlAndGetResult("--all-players metadata xesam:title");
            state.Artist = RunPlayerctlAndGetResult("--all-players metadata xesam:artist");
            state.Album = RunPlayerctlAndGetResult("--all-players metadata xesam:album");

            // Determine source from bus name
            state.Source = BusNameToAppName(playerBusName);

            return state;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Run a playerctl metadata query and return the value.
    /// </summary>
    private static string? RunPlayerctlAndGetResult(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("playerctl", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);

            // playerctl output format: "player_name value"
            // Strip the player name prefix
            if (string.IsNullOrEmpty(output) || !output.Contains(' '))
                return string.IsNullOrEmpty(output) ? null : output;

            var spaceIdx = output.IndexOf(' ');
            var value = output.Substring(spaceIdx + 1).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fallback: Use raw dbus-send commands to query MPRIS players.
    /// </summary>
    private static MediaState? GetMediaStateViaDbus()
    {
        try
        {
            // First, find available MPRIS players by listing DBus names
            var playerBusNames = ListMprisPlayers();
            if (playerBusNames.Length == 0)
                return new MediaState { State = "idle" };

            // Use the first available player
            var busName = playerBusNames[0];
            var state = new MediaState();

            // Get playback status
            var status = GetDbusProperty(busName,
                "/org/mpris/MediaPlayer2",
                "org.mpris.MediaPlayer2.Player",
                "PlaybackStatus");

            state.State = status?.ToLowerInvariant() switch
            {
                "playing" => "playing",
                "paused" => "paused",
                "stopped" => "idle",
                _ => "idle"
            };

            // Get metadata (title, artist, album)
            state.Title = GetMetadataKey(busName, "xesam:title");
            state.Artist = GetMetadataKey(busName, "xesam:artist");
            state.Album = GetMetadataKey(busName, "xesam:album");

            // Source from bus name
            state.Source = BusNameToAppName(busName);

            return state;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// List all running MPRIS media players via DBus.
    /// </summary>
    private static string[] ListMprisPlayers()
    {
        try
        {
            var psi = new ProcessStartInfo("dbus-send",
                "--print-reply --dest=org.freedesktop.DBus /org/freedesktop/DBus org.freedesktop.DBus.ListNames")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return Array.Empty<string>();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            var result = new List<string>();
            foreach (var line in output.Split('\n'))
            {
                // Look for lines like: string "org.mpris.MediaPlayer2.spotify"
                var match = Regex.Match(line.Trim(), @"string\s+""(org\.mpris\.MediaPlayer2\.[^""]+)""");
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    // Skip *instance* and *chromium (they're browser MPRIS wrappers without media)
                    if (!name.Contains("instance", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(name);
                    }
                }
            }
            return result.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Get a D-Bus property from an MPRIS player.
    /// </summary>
    private static string? GetDbusProperty(string busName, string objectPath, string interfaceName, string propertyName)
    {
        try
        {
            var psi = new ProcessStartInfo("dbus-send",
                $"--print-reply --dest={busName} {objectPath} " +
                $"org.freedesktop.DBus.Properties.Get " +
                $"string:'{interfaceName}' string:'{propertyName}'")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            return ParseDbusVariant(output);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract a specific metadata key from an MPRIS player's metadata dictionary.
    /// </summary>
    private static string? GetMetadataKey(string busName, string key)
    {
        try
        {
            var psi = new ProcessStartInfo("dbus-send",
                $"--print-reply --dest={busName} /org/mpris/MediaPlayer2 " +
                $"org.freedesktop.DBus.Properties.Get " +
                $"string:'org.mpris.MediaPlayer2.Player' string:'Metadata'")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            return ParseMetadataValue(output, key);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse a simple D-Bus variant value from dbus-send output.
    /// Handles: variant string "value", variant int32 42, variant boolean true
    /// </summary>
    private static string? ParseDbusVariant(string dbusOutput)
    {
        // Typical output:
        // method return time=... sender=... -> destination=... serial=...
        //    variant       string "Playing"
        try
        {
            var match = Regex.Match(dbusOutput,
                @"variant\s+\w+\s+\""([^\""]*)\""",
                RegexOptions.Singleline);
            if (match.Success)
                return match.Groups[1].Value;

            // Try without the variant keyword (simpler property response)
            match = Regex.Match(dbusOutput,
                @"string\s+\""([^\""]*)\""");
            if (match.Success)
                return match.Groups[1].Value;

            // Try boolean
            match = Regex.Match(dbusOutput, @"boolean\s+(true|false)");
            if (match.Success)
                return match.Groups[1].Value;

            // Try int32
            match = Regex.Match(dbusOutput, @"int32\s+(\d+)");
            if (match.Success)
                return match.Groups[1].Value;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract a specific metadata value from a full Metadata D-Bus response.
    /// The metadata is an array of dict entries with key-value pairs.
    /// </summary>
    private static string? ParseMetadataValue(string dbusOutput, string key)
    {
        try
        {
            // Look for the key in the output, then find the following string value
            var keyPattern = $"string \"{Regex.Escape(key)}\"";
            var match = Regex.Match(dbusOutput, keyPattern, RegexOptions.Singleline);
            if (!match.Success) return null;

            // After the key, there should be a variant with the value
            var rest = dbusOutput.Substring(match.Index + match.Length);

            // Find the next variant string or array of strings
            var valMatch = Regex.Match(rest,
                @"variant\s+string\s+\""([^\""]*)\""",
                RegexOptions.Singleline);
            if (valMatch.Success)
                return valMatch.Groups[1].Value;

            // Could be an array of strings (for artist, etc.)
            valMatch = Regex.Match(rest,
                @"variant\s+array\s+\[\s*\n?\s*string\s+\""([^\""]*)\""",
                RegexOptions.Singleline);
            if (valMatch.Success)
                return valMatch.Groups[1].Value;

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Convert an MPRIS bus name to a human-readable app name.
    /// </summary>
    private static string? BusNameToAppName(string? busName)
    {
        if (string.IsNullOrEmpty(busName))
            return null;

        // Extract the app name from org.mpris.MediaPlayer2.{appname}
        var prefix = "org.mpris.MediaPlayer2.";
        if (busName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = busName.Substring(prefix.Length);

            // Handle instance suffixes: spotify.instance123 -> spotify
            var dotIdx = name.IndexOf('.');
            if (dotIdx > 0)
                name = name.Substring(0, dotIdx);

            // Capitalize first letter for known apps
            return name switch
            {
                "spotify" => "Spotify",
                "vlc" => "VLC",
                "firefox" => "Firefox",
                "chromium" => "Chromium",
                "chrome" => "Chrome",
                "brave" => "Brave",
                "mpv" => "mpv",
                "rhythmbox" => "Rhythmbox",
                "banshee" => "Banshee",
                "clementine" => "Clementine",
                "amarok" => "Amarok",
                "elisa" => "Elisa",
                "audacious" => "Audacious",
                "gmusicbrowser" => "gmusicbrowser",
                "quodlibet" => "Quod Libet",
                "deadbeef" => "DeaDBeeF",
                "qmmp" => "Qmmp",
                "lollypop" => "Lollypop",
                _ => char.ToUpperInvariant(name[0]) + name.Substring(1)
            };
        }

        // Not an MPRIS name, return as-is
        return busName;
    }
}
