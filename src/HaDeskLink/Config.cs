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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace HaDeskLink;

/// <summary>
/// Application configuration persisted as JSON.
/// HA Token is encrypted with a machine-keyed AES for security.
/// If a hacker gains access to the PC, the token cannot be decrypted
/// without the machine-specific key stored separately.
/// </summary>
public class Config
{
    private static readonly string AppName = "HA_DeskLink";
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public string HaUrl { get; set; } = "";
    public string HaToken { get; set; } = "";
    public bool VerifySsl { get; set; } = true;
    /// <summary>
    /// Autostart via XDG autostart (~/.config/autostart). Default: false (daemon users use systemd instead).
    /// </summary>
    public bool Autostart { get; set; } = false;
    public int SensorInterval { get; set; } = 30;
    public string UpdateChannel { get; set; } = "stable";
    public string Language { get; set; } = "de";
    /// <summary>
    /// UI theme: "system", "light", or "dark". Default: dark (matches the built-in dark UI).
    /// </summary>
    public string Theme { get; set; } = "dark";
    /// <summary>
    /// Quick Actions hotkey modifiers: "ctrl_shift", "ctrl_alt", "ctrl", "alt", "shift", "none".
    /// Linux: hotkeys are handled while an HA DeskLink window has keyboard focus (Wayland has no global hotkeys).
    /// </summary>
    public string HotkeyModifiers { get; set; } = "ctrl_shift";
    /// <summary>Quick Actions hotkey key. Default: H</summary>
    public string HotkeyKey { get; set; } = "H";
    /// <summary>Dashboard hotkey modifiers. Default: ctrl_shift</summary>
    public string HotkeyDashboardModifiers { get; set; } = "ctrl_shift";
    /// <summary>Dashboard hotkey key. Default: D</summary>
    public string HotkeyDashboardKey { get; set; } = "D";
    /// <summary>Settings hotkey modifiers. Default: ctrl_shift</summary>
    public string HotkeySettingsModifiers { get; set; } = "ctrl_shift";
    /// <summary>Settings hotkey key. Default: S</summary>
    public string HotkeySettingsKey { get; set; } = "S";
    /// <summary>
    /// Quick Actions: JSON array of { entityId, name } objects.
    /// </summary>
    public string QuickActions { get; set; } = "[]";

    /// <summary>
    /// Desktop Widgets (Phase D): JSON array of WidgetConfig objects:
    /// { type: "sensor"|"toggle"|"multi_toggle", name, entityId, entities: [...],
    ///   monitor, offsetX, offsetY, clickThrough }
    /// </summary>
    public string Widgets { get; set; } = "[]";
    /// <summary>
    /// Encrypted HA token. When set, HaToken is cleared.
    /// If empty, HaToken is used (migration from old config).
    /// </summary>
    public string? HaTokenEncrypted { get; set; }

    // MQTT Configuration (optional, auto-configured)
    public bool MqttEnabled { get; set; } = false;
    public string MqttBroker { get; set; } = "";
    public int MqttPort { get; set; } = 1883;
    public string MqttUsername { get; set; } = "";
    public string MqttPassword { get; set; } = "";           // runtime only, never saved to config file
    public string MqttPasswordEncrypted { get; set; } = "";  // encrypted version for persistence
    public bool MqttUseSsl { get; set; } = false;
    public bool MqttAutoConfigured { get; set; } = false;    // set by auto-setup
    /// <summary>
    /// Fallback MQTT broker address for auto-configuration (e.g., local network IP
    /// when HA URL is a domain name that may not resolve MQTT correctly).
    /// </summary>
    public string MqttBrokerFallback { get; set; } = "";

    /// <summary>
    /// Notification position: "bottom_left", "bottom_right", "top_left", "top_right". Default: bottom_left
    /// </summary>
    public string NotificationPosition { get; set; } = "bottom_left";

    /// <summary>
    /// Monitor index for notifications (0 = primary, 1+ = specific monitor). Default: 0
    /// </summary>
    public int NotificationMonitor { get; set; } = 0;

    // Music Assistant (optional)
    /// <summary>MA host/IP (empty = MA integration disabled).</summary>
    public string MaHost { get; set; } = "";
    /// <summary>MA API port (TrueNAS-App style: 30278; native installs: 8095).</summary>
    public int MaPort { get; set; } = 8095;
    /// <summary>MA long-lived access token (runtime only, never saved plaintext).</summary>
    public string MaToken { get; set; } = "";
    public string? MaTokenEncrypted { get; set; }

    // Sendspin streaming (Phase E): play MA audio on this PC's speakers.
    /// <summary>Sendspin streaming enabled (player role on the MA server).</summary>
    public bool SendspinEnabled { get; set; } = false;
    /// <summary>Sendspin port on the MA host (MA serves ws://host:8927/sendspin).</summary>
    public int SendspinPort { get; set; } = 8927;
    /// <summary>Player name this client registers as on the Sendspin server.</summary>
    public string SendspinPlayerName { get; set; } = "HA DeskLink";

    /// <summary>
    /// Custom Commands: JSON-Array von benutzerdefinierten Skripten/Befehlen
    /// die von Home Assistant getriggert werden können.
    /// Format: [{"command":"start_streaming","script":"/usr/local/bin/stream.sh","name":"Start Streaming"}]
    /// </summary>
    public string CustomCommands { get; set; } = "[]";

    /// <summary>
    /// App Launchers: JSON-Array von Apps die von HA gestartet werden können.
    /// Format: [{"command":"launch_spotify","path":"spotify","name":"Spotify"}]
    /// </summary>
    public string AppLaunchers { get; set; } = "[]";

    private string ConfigPath => Path.Combine(ConfigDir, "config.json");

    /// <summary>
    /// Get or create a machine-specific encryption key.
    /// The key is stored in a separate file with restricted permissions (0600).
    /// </summary>
    private static byte[] GetOrCreateKey()
    {
        var keyPath = Path.Combine(ConfigDir, ".key");

        // Try to read existing key with file locking to avoid race conditions
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (File.Exists(keyPath))
                {
                    using var fs = new FileStream(keyPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    using var reader = new StreamReader(fs);
                    return Convert.FromBase64String(reader.ReadToEnd().Trim());
                }

                // File doesn't exist yet, create it with exclusive lock
                Directory.CreateDirectory(ConfigDir);
                using (var fs = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // Generate new 256-bit key
                    var key = new byte[32];
                    RandomNumberGenerator.Fill(key);
                    var keyStr = Convert.ToBase64String(key);
                    using var writer = new StreamWriter(fs);
                    writer.Write(keyStr);
                    fs.Flush();

                    // Set file permissions to owner-only (Linux/macOS)
#pragma warning disable CA1416
                    try
                    {
#if LINUX || MACOS
                        File.SetUnixFileMode(keyPath, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);
#endif
                    }
                    catch { }
#pragma warning restore CA1416

                    return key;
                }
            }
            catch (IOException)
            {
                // Another process is writing the key - wait and retry
                Thread.Sleep(50);
            }
        }

        // Fallback: if all retries exhausted, read without locking
        if (File.Exists(keyPath))
        {
            return Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
        }

        // Last resort: generate without locking
        var fallbackKey = new byte[32];
        RandomNumberGenerator.Fill(fallbackKey);
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(keyPath, Convert.ToBase64String(fallbackKey));
        return fallbackKey;
    }

    /// <summary>
    /// Encrypt a string using AES-GCM with machine-keyed encryption.
    /// </summary>
    private static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        var key = GetOrCreateKey();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);

        using var aes = new AesGcm(key, 16);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        // Combine: nonce + tag + ciphertext (base64)
        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decrypt a string using AES-GCM with machine-keyed encryption.
    /// </summary>
    private static string DecryptString(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return "";
        try
        {
            var key = GetOrCreateKey();
            var combined = Convert.FromBase64String(encryptedText);

            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;

            if (combined.Length < nonceSize + tagSize) return "";

            var nonce = new byte[nonceSize];
            var tag = new byte[tagSize];
            var ciphertext = new byte[combined.Length - nonceSize - tagSize];

            Buffer.BlockCopy(combined, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(combined, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(combined, nonceSize + tagSize, ciphertext, 0, ciphertext.Length);

            using var aes = new AesGcm(key, 16);
            var plainBytes = new byte[ciphertext.Length];
            aes.Decrypt(nonce, ciphertext, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Load config and automatically migrate plaintext tokens to encrypted storage.
    /// </summary>
    public static Config Load()
    {
        Directory.CreateDirectory(ConfigDir);
        var path = Path.Combine(ConfigDir, "config.json");
        Config config;

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        else
        {
            config = new Config();
        }

        // Migration: if HaTokenEncrypted is empty but HaToken has a value,
        // encrypt HaToken and clear the plaintext
        if (string.IsNullOrEmpty(config.HaTokenEncrypted) && !string.IsNullOrEmpty(config.HaToken))
        {
            config.HaTokenEncrypted = EncryptString(config.HaToken);
            config.HaToken = "";
            config.Save();
        }
        else if (!string.IsNullOrEmpty(config.HaTokenEncrypted))
        {
            var decrypted = DecryptString(config.HaTokenEncrypted);
            if (!string.IsNullOrEmpty(decrypted))
                config.HaToken = decrypted;
        }

        // Migration: if MqttPasswordEncrypted is empty but MqttPassword has a value,
        // encrypt MqttPassword and clear the plaintext
        if (string.IsNullOrEmpty(config.MqttPasswordEncrypted) && !string.IsNullOrEmpty(config.MqttPassword))
        {
            config.MqttPasswordEncrypted = EncryptString(config.MqttPassword);
            config.MqttPassword = ""; // Clear plaintext
            config.Save(); // Save encrypted version immediately
        }
        else if (!string.IsNullOrEmpty(config.MqttPasswordEncrypted))
        {
            // Decrypt the MQTT password for use in the app
            var decrypted = DecryptString(config.MqttPasswordEncrypted);
            if (!string.IsNullOrEmpty(decrypted))
                config.MqttPassword = decrypted;
        }

        // Migration: if MaTokenEncrypted is empty but MaToken has a value,
        // encrypt MaToken and clear the plaintext
        if (string.IsNullOrEmpty(config.MaTokenEncrypted) && !string.IsNullOrEmpty(config.MaToken))
        {
            config.MaTokenEncrypted = EncryptString(config.MaToken);
            config.MaToken = "";
            config.Save();
        }
        else if (!string.IsNullOrEmpty(config.MaTokenEncrypted))
        {
            var decrypted = DecryptString(config.MaTokenEncrypted);
            if (!string.IsNullOrEmpty(decrypted))
                config.MaToken = decrypted;
        }

        return config;
    }

    /// <summary>
    /// Save config with encrypted token. Never saves HaToken in plaintext.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);

        if (!string.IsNullOrEmpty(HaToken))
        {
            HaTokenEncrypted = EncryptString(HaToken);
        }

        if (!string.IsNullOrEmpty(MqttPassword))
        {
            MqttPasswordEncrypted = EncryptString(MqttPassword);
        }

        if (!string.IsNullOrEmpty(MaToken))
        {
            MaTokenEncrypted = EncryptString(MaToken);
        }
        else
        {
            // Token was cleared (or decryption failed on load) — also drop the
            // stale ciphertext so a cleared token really stays cleared.
            MaTokenEncrypted = null;
        }

        var saveConfig = new Config
        {
            HaUrl = HaUrl,
            HaToken = "", // NEVER save plaintext token
            VerifySsl = VerifySsl,
            Autostart = Autostart,
            SensorInterval = SensorInterval,
            UpdateChannel = UpdateChannel,
            Language = Language,
            HaTokenEncrypted = HaTokenEncrypted,
            QuickActions = QuickActions,
            Widgets = Widgets,
            Theme = Theme,
            HotkeyModifiers = HotkeyModifiers,
            HotkeyKey = HotkeyKey,
            HotkeyDashboardModifiers = HotkeyDashboardModifiers,
            HotkeyDashboardKey = HotkeyDashboardKey,
            HotkeySettingsModifiers = HotkeySettingsModifiers,
            HotkeySettingsKey = HotkeySettingsKey,
            MqttEnabled = MqttEnabled,
            MqttBroker = MqttBroker,
            MqttPort = MqttPort,
            MqttUsername = MqttUsername,
            MqttPassword = "", // NEVER save plaintext password
            MqttPasswordEncrypted = MqttPasswordEncrypted,
            MqttUseSsl = MqttUseSsl,
            MqttAutoConfigured = MqttAutoConfigured,
            MqttBrokerFallback = MqttBrokerFallback,
            MaHost = MaHost,
            MaPort = MaPort,
            MaToken = "", // NEVER save plaintext token
            MaTokenEncrypted = MaTokenEncrypted,
            SendspinEnabled = SendspinEnabled,
            SendspinPort = SendspinPort,
            SendspinPlayerName = SendspinPlayerName,
            CustomCommands = CustomCommands,
            AppLaunchers = AppLaunchers,
            NotificationPosition = NotificationPosition,
            NotificationMonitor = NotificationMonitor
        };

        var json = JsonSerializer.Serialize(saveConfig, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);

        // Secure config file permissions (Linux/macOS)
#pragma warning disable CA1416
        try
        {
#if LINUX || MACOS
            File.SetUnixFileMode(ConfigPath, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite);
#endif
        }
        catch { }
#pragma warning restore CA1416
    }

    public static string GetConfigDir() => ConfigDir;
}