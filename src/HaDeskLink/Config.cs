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
    public int SensorInterval { get; set; } = 30;
    public string UpdateChannel { get; set; } = "stable";
    public string Language { get; set; } = "de";
    /// <summary>
    /// Quick Actions: JSON array of { entityId, name } objects.
    /// </summary>
    public string QuickActions { get; set; } = "[]";
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

        var saveConfig = new Config
        {
            HaUrl = HaUrl,
            HaToken = "", // NEVER save plaintext token
            VerifySsl = VerifySsl,
            SensorInterval = SensorInterval,
            UpdateChannel = UpdateChannel,
            Language = Language,
            HaTokenEncrypted = HaTokenEncrypted,
            QuickActions = QuickActions,
            MqttEnabled = MqttEnabled,
            MqttBroker = MqttBroker,
            MqttPort = MqttPort,
            MqttUsername = MqttUsername,
            MqttPassword = "", // NEVER save plaintext password
            MqttPasswordEncrypted = MqttPasswordEncrypted,
            MqttUseSsl = MqttUseSsl,
            MqttAutoConfigured = MqttAutoConfigured
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