
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
                return true;
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
    }
}