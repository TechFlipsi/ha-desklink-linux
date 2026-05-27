
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
using System;
using System.Threading.Tasks;

namespace HaDeskLink;

/// <summary>
/// Console-based setup wizard (for --setup without GUI).
/// </summary>
public class SetupWizard
{
    public string HaUrl { get; private set; } = "";
    public string HaToken { get; private set; } = "";
    public bool VerifySsl { get; private set; } = false;
    public bool MqttConfigured { get; private set; } = false;

    public async Task<bool> RunAsync()
    {
        Console.WriteLine("=== HA DeskLink Linux Setup ===\n");

        Console.Write("Home Assistant URL (z.B. https://homeassistant.local:8123): ");
        HaUrl = Console.ReadLine()?.Trim() ?? "";

        Console.Write("Long-Lived Access Token (HA → Profil → Sicherheit): ");
        HaToken = Console.ReadLine()?.Trim() ?? "";

        Console.Write("SSL-Zertifikat prüfen? (j/n, Standard: n): ");
        var ssl = Console.ReadLine()?.Trim().ToLowerInvariant();
        VerifySsl = ssl == "j" || ssl == "y" || ssl == "ja" || ssl == "yes";

        var configDir = Config.GetConfigDir();
        var api = new HaApiClient(configDir, VerifySsl);

        while (true)
        {
            Console.WriteLine("\nVerbinde mit Home Assistant...");

            try
            {
                await api.RegisterAsync(HaUrl, HaToken);
                Console.WriteLine("✓ Verbindung erfolgreich!");
                break;
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException && ex.Message.Contains("Login fehlgeschlagen"))
                {
                    Console.WriteLine($"✗ {ex.Message}");
                    Console.Write("\nMöchtest du es erneut versuchen? (j/n): ");
                    var retry = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (retry == "j" || retry == "y" || retry == "ja" || retry == "yes")
                    {
                        api.ResetBlockState();
                        Console.Write("Neuer Long-Lived Access Token: ");
                        HaToken = Console.ReadLine()?.Trim() ?? "";
                        continue;
                    }
                    return false;
                }

                Console.WriteLine($"✗ Verbindung fehlgeschlagen (Versuch {api.FailedLoginAttempts}/{HaApiClient.MaxFailedLoginAttempts}): {ex.Message}");

                if (api.IsBlocked)
                {
                    Console.WriteLine("\n✗ Login gesperrt nach 3 Fehlversuchen. Token ungültig.");
                    Console.Write("Möchtest du es erneut versuchen? (j/n): ");
                    var retry = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (retry == "j" || retry == "y" || retry == "ja" || retry == "yes")
                    {
                        api.ResetBlockState();
                        Console.Write("Neuer Long-Lived Access Token: ");
                        HaToken = Console.ReadLine()?.Trim() ?? "";
                        continue;
                    }
                    return false;
                }
            }
        }

        // ── Step 2: MQTT Configuration ────────────────────────────────
        return await RunManualMqttAsync();
    }

    private async Task<bool> RunManualMqttAsync()
    {
        Console.WriteLine("\n=== MQTT Setup ===\n");

        Console.WriteLine("📡 MQTT-Funktionen:");
        Console.WriteLine("  Mit MQTT:                          Ohne MQTT:");
        Console.WriteLine("  ✓ PC Status                        ✓ PC Status");
        Console.WriteLine("  ✓ Sensoren                         ✓ Sensoren");
        Console.WriteLine("  ✓ Quick Actions                    ✓ Quick Actions");
        Console.WriteLine("  ✓ Mediensteuerung (Echtzeit)       ✗ Mediensteuerung");
        Console.WriteLine("  ✓ Schnelle Sensor-Updates          ✗ Schnelle Sensor-Updates");
        Console.WriteLine();

        Console.Write("MQTT nutzen? (j/n) [j]: ");
        var useMqtt = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (useMqtt != "y" && useMqtt != "j" && useMqtt != "ja" && useMqtt != "yes" && !string.IsNullOrWhiteSpace(useMqtt))
        {
            Console.WriteLine("✓ Ohne MQTT fortfahren.");
            MqttConfigured = false;
            return true;
        }

        Console.Write("Broker Host (z.B. homeassistant.local): ");
        var broker = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(broker))
        {
            // Extract host from HA URL
            try { broker = new Uri(HaUrl).Host; } catch { broker = "homeassistant.local"; }
            Console.WriteLine($"  Verwende: {broker}");
        }

        Console.Write("Port [1883]: ");
        var portStr = Console.ReadLine()?.Trim();
        if (!int.TryParse(portStr, out var port) || port <= 0) port = 1883;

        Console.Write("Benutzername (optional): ");
        var username = Console.ReadLine()?.Trim() ?? "";

        Console.Write("Passwort (optional): ");
        var password = Console.ReadLine()?.Trim() ?? "";

        Console.Write("SSL/TLS verwenden? (j/n) [n]: ");
        var ssl = Console.ReadLine()?.Trim().ToLowerInvariant();
        var useSsl = ssl == "j" || ssl == "y" || ssl == "ja" || ssl == "yes";

        Console.WriteLine("\nTeste MQTT-Verbindung...");

        var ok = await MqttSetupHelper.TestConnectionAsync(broker, port,
            string.IsNullOrEmpty(username) ? null : username,
            string.IsNullOrEmpty(password) ? null : password, useSsl);

        if (ok)
        {
            var config = Config.Load();
            config.MqttEnabled = true;
            config.MqttBroker = broker;
            config.MqttPort = port;
            config.MqttUsername = username;
            config.MqttPassword = password;
            config.MqttUseSsl = useSsl;
            config.MqttAutoConfigured = false;
            config.Save();

            Console.WriteLine($"✓ MQTT-Verbindung zu {broker}:{port} erfolgreich!");
            MqttConfigured = true;
            return true;
        }
        else
        {
            Console.WriteLine($"✗ Verbindung zu {broker}:{port} fehlgeschlagen!");
            Console.Write("\nOhne MQTT fortfahren? (j/n) [j]: ");
            var skip = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (skip == "n" || skip == "no") return false;
            Console.WriteLine("✓ Ohne MQTT fortfahren.");
            MqttConfigured = false;
            return true;
        }
    }
}